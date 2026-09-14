using System.Text;
using System.Text.Json.Nodes;
using Nuventra.NuvexaDB;

namespace NuvexaDB.Samples;

/// <summary>
/// Builds an encrypted multi-tenant commerce .nvx: typed columns, nested documents,
/// compound indexes, $lookup-friendly ids, and GridFS files.
/// </summary>
public static class ComplexCommerceDb
{
    public const string EncryptionKey = "nuvexa-demo";

    public static Task<string> GenerateAsync(string path) => GenerateAsync(path, deprecatedFormat1: false);

    /// <summary>
    /// Builds the commerce sample. <paramref name="deprecatedFormat1"/> uses the internal
    /// format-1 fixture writer so older Data Studio builds can open the file.
    /// </summary>
    public static async Task<string> GenerateAsync(string path, bool deprecatedFormat1)
    {
        SampleTour.ResetFile(path);
        var log = new StringBuilder();
        var options = NuvexaCreateOptions.ForDesktop(EncryptionKey);

        await using var db = deprecatedFormat1
            ? NuvexaDatabase.CreateDeprecatedFormat1(path, options)
            : NuvexaDatabase.Create(path, options);

        await DefineSchemaAsync(db);
        await SeedOrganizationsAsync(db);
        await SeedWarehousesAsync(db);
        await SeedUsersAsync(db);
        await SeedCustomersAsync(db);
        await SeedCategoriesAsync(db);
        await SeedProductsAsync(db);
        await SeedInventoryAsync(db);
        await SeedOrdersAsync(db);
        await SeedInvoicesAsync(db);
        await SeedPaymentsAsync(db);
        await SeedShipmentsAsync(db);
        await SeedReviewsAsync(db);
        await SeedTicketsAsync(db);
        await SeedAuditAsync(db);
        await SeedFlagsAsync(db);
        await SeedFilesAsync(db);
        await EnsureIndexesAsync(db);
        await db.CheckpointAsync();

        log.AppendLine($"Wrote encrypted database: {path}");
        log.AppendLine(deprecatedFormat1
            ? $"Format: {db.FormatVersion} (deprecated format-1 fixture for older Data Studio)"
            : $"Format: {db.FormatVersion} (writes are format 2; format 1 is deprecated and still readable)");
        log.AppendLine($"Encryption key: {EncryptionKey}");
        log.AppendLine("Collections:");
        foreach (var name in db.GetCollectionNames().Where(n => n != "__nuvexa_schema").OrderBy(n => n, StringComparer.Ordinal))
        {
            log.AppendLine($"  {name}  ({db.GetCollection(name).Count})");
        }

        log.AppendLine();
        log.AppendLine("Try in Data Studio / nuvexa query:");
        log.AppendLine("""  db.orders.find({ status: "shipped" }).sort({ placedAt: -1 })""");
        log.AppendLine("""  db.products.find({ "attributes.color": { $in: ["obsidian", "ivory"] }, status: "active" })""");
        log.AppendLine("""  db.orders.aggregate([{ $lookup: { from: "customers", localField: "customerId", foreignField: "_id", as: "customer" } }])""");
        log.AppendLine("""  db.orders.aggregate([{ $group: { _id: "$status", n: { $sum: 1 }, revenue: { $sum: "$totals.grand" } } }])""");
        return log.ToString();
    }

    private static Task DefineSchemaAsync(NuvexaDatabase db)
    {
        var schema = db.GetCollection("__nuvexa_schema");
        return schema.InsertManyAsync(
        [
            Schema("organizations",
                Col("name", "TEXT"),
                Col("slug", "TEXT", unique: true),
                Col("plan", "TEXT", "starter"),
                Col("status", "TEXT", "active"),
                Col("region", "TEXT"),
                Col("billing", "TEXT"),
                Col("settings", "TEXT"),
                Col("createdAt", "DATETIME")),
            Schema("warehouses",
                Col("orgId", "TEXT"),
                Col("code", "TEXT", unique: true),
                Col("name", "TEXT"),
                Col("address", "TEXT"),
                Col("capacity", "INTEGER"),
                Col("active", "BOOLEAN", "true")),
            Schema("users",
                Col("orgId", "TEXT"),
                Col("email", "TEXT", unique: true),
                Col("role", "TEXT"),
                Col("status", "TEXT", "active"),
                Col("profile", "TEXT"),
                Col("preferences", "TEXT"),
                Col("permissions", "TEXT"),
                Col("lastLoginAt", "DATETIME")),
            Schema("customers",
                Col("orgId", "TEXT"),
                Col("email", "TEXT"),
                Col("name", "TEXT"),
                Col("tier", "TEXT", "bronze"),
                Col("loyaltyPoints", "INTEGER"),
                Col("addresses", "TEXT"),
                Col("tags", "TEXT"),
                Col("createdAt", "DATETIME")),
            Schema("categories",
                Col("orgId", "TEXT"),
                Col("parentId", "TEXT"),
                Col("name", "TEXT"),
                Col("slug", "TEXT"),
                Col("path", "TEXT"),
                Col("sortOrder", "INTEGER")),
            Schema("products",
                Col("orgId", "TEXT"),
                Col("sku", "TEXT", unique: true),
                Col("name", "TEXT"),
                Col("categoryId", "TEXT"),
                Col("status", "TEXT", "active"),
                Col("price", "REAL"),
                Col("currency", "TEXT", "USD"),
                Col("variants", "TEXT"),
                Col("attributes", "TEXT"),
                Col("tags", "TEXT"),
                Col("rating", "REAL")),
            Schema("inventory",
                Col("warehouseId", "TEXT"),
                Col("productId", "TEXT"),
                Col("sku", "TEXT"),
                Col("onHand", "INTEGER"),
                Col("reserved", "INTEGER"),
                Col("reorderPoint", "INTEGER")),
            Schema("orders",
                Col("orgId", "TEXT"),
                Col("customerId", "TEXT"),
                Col("status", "TEXT"),
                Col("currency", "TEXT", "USD"),
                Col("totals", "TEXT"),
                Col("lineItems", "TEXT"),
                Col("shipping", "TEXT"),
                Col("payment", "TEXT"),
                Col("placedAt", "DATETIME")),
            Schema("invoices",
                Col("orderId", "TEXT"),
                Col("customerId", "TEXT"),
                Col("number", "TEXT", unique: true),
                Col("status", "TEXT"),
                Col("dueAt", "DATETIME"),
                Col("amount", "REAL"),
                Col("tax", "REAL"),
                Col("lineItems", "TEXT")),
            Schema("payments",
                Col("invoiceId", "TEXT"),
                Col("orderId", "TEXT"),
                Col("method", "TEXT"),
                Col("status", "TEXT"),
                Col("amount", "REAL"),
                Col("capturedAt", "DATETIME"),
                Col("refunds", "TEXT")),
            Schema("shipments",
                Col("orderId", "TEXT"),
                Col("warehouseId", "TEXT"),
                Col("carrier", "TEXT"),
                Col("tracking", "TEXT"),
                Col("status", "TEXT"),
                Col("events", "TEXT")),
            Schema("reviews",
                Col("productId", "TEXT"),
                Col("customerId", "TEXT"),
                Col("rating", "INTEGER"),
                Col("title", "TEXT"),
                Col("body", "TEXT"),
                Col("verified", "BOOLEAN"),
                Col("createdAt", "DATETIME")),
            Schema("tickets",
                Col("orgId", "TEXT"),
                Col("customerId", "TEXT"),
                Col("orderId", "TEXT"),
                Col("status", "TEXT"),
                Col("priority", "TEXT"),
                Col("subject", "TEXT"),
                Col("comments", "TEXT")),
            Schema("audit_events",
                Col("orgId", "TEXT"),
                Col("actorId", "TEXT"),
                Col("action", "TEXT"),
                Col("entity", "TEXT"),
                Col("entityId", "TEXT"),
                Col("at", "DATETIME"),
                Col("meta", "TEXT")),
            Schema("feature_flags",
                Col("orgId", "TEXT"),
                Col("key", "TEXT"),
                Col("enabled", "BOOLEAN"),
                Col("rollout", "INTEGER"),
                Col("rules", "TEXT")),
        ]);
    }

    private static Task SeedOrganizationsAsync(NuvexaDatabase db) =>
        db.GetCollection("organizations").InsertManyAsync(
        [
            Doc("""{"_id":"org_northwind","name":"Northwind Labs","slug":"northwind","plan":"enterprise","status":"active","region":"apac","billing":{"currency":"INR","cycle":"annual","seats":120},"settings":{"mfa":true,"retentionDays":400,"locales":["en-IN","hi-IN"]},"createdAt":"2023-04-11T09:00:00Z"}"""),
            Doc("""{"_id":"org_contoso","name":"Contoso Retail","slug":"contoso","plan":"growth","status":"active","region":"emea","billing":{"currency":"EUR","cycle":"monthly","seats":40},"settings":{"mfa":true,"retentionDays":180,"locales":["en-GB","de-DE"]},"createdAt":"2024-01-18T11:30:00Z"}"""),
            Doc("""{"_id":"org_fabrikam","name":"Fabrikam Outlets","slug":"fabrikam","plan":"starter","status":"trial","region":"amer","billing":{"currency":"USD","cycle":"monthly","seats":8},"settings":{"mfa":false,"retentionDays":30,"locales":["en-US"]},"createdAt":"2026-07-02T16:45:00Z"}"""),
        ]);

    private static Task SeedWarehousesAsync(NuvexaDatabase db) =>
        db.GetCollection("warehouses").InsertManyAsync(
        [
            Doc("""{"_id":"wh_blr","orgId":"org_northwind","code":"BLR-01","name":"Bengaluru FC","address":{"line1":"Whitefield","city":"Bengaluru","country":"IN","geo":{"lat":12.9698,"lon":77.7500}},"capacity":18000,"active":true}"""),
            Doc("""{"_id":"wh_mum","orgId":"org_northwind","code":"MUM-02","name":"Mumbai Bonded","address":{"line1":"Bhiwandi","city":"Mumbai","country":"IN","geo":{"lat":19.2813,"lon":73.0483}},"capacity":9000,"active":true}"""),
            Doc("""{"_id":"wh_ber","orgId":"org_contoso","code":"BER-01","name":"Berlin Hub","address":{"line1":"Marzahn","city":"Berlin","country":"DE","geo":{"lat":52.541,"lon":13.551}},"capacity":12000,"active":true}"""),
            Doc("""{"_id":"wh_sea","orgId":"org_fabrikam","code":"SEA-01","name":"Seattle Crossdock","address":{"line1":"SODO","city":"Seattle","country":"US","geo":{"lat":47.581,"lon":-122.327}},"capacity":2500,"active":false}"""),
        ]);

    private static Task SeedUsersAsync(NuvexaDatabase db) =>
        db.GetCollection("users").InsertManyAsync(
        [
            Doc("""{"_id":"usr_ada","orgId":"org_northwind","email":"ada@northwind.dev","role":"owner","status":"active","profile":{"name":"Ada Lovelace","title":"CTO","timezone":"Asia/Kolkata"},"preferences":{"theme":"dark","digest":"weekly"},"permissions":["org.admin","catalog.write","finance.read"],"lastLoginAt":"2026-09-14T08:12:00Z"}"""),
            Doc("""{"_id":"usr_grace","orgId":"org_northwind","email":"grace@northwind.dev","role":"ops","status":"active","profile":{"name":"Grace Hopper","title":"Warehouse lead","timezone":"Asia/Kolkata"},"preferences":{"theme":"light","digest":"daily"},"permissions":["inventory.write","shipments.write"],"lastLoginAt":"2026-09-13T19:40:00Z"}"""),
            Doc("""{"_id":"usr_alan","orgId":"org_northwind","email":"alan@northwind.dev","role":"support","status":"active","profile":{"name":"Alan Turing","title":"Support","timezone":"Asia/Kolkata"},"preferences":{"theme":"system","digest":"off"},"permissions":["tickets.write","orders.read"],"lastLoginAt":"2026-09-14T06:01:00Z"}"""),
            Doc("""{"_id":"usr_kara","orgId":"org_contoso","email":"kara@contoso.eu","role":"owner","status":"active","profile":{"name":"Kara Danvers","title":"GM","timezone":"Europe/Berlin"},"preferences":{"theme":"dark","digest":"weekly"},"permissions":["org.admin","catalog.write"],"lastLoginAt":"2026-09-12T21:18:00Z"}"""),
            Doc("""{"_id":"usr_linus","orgId":"org_contoso","email":"linus@contoso.eu","role":"analyst","status":"invited","profile":{"name":"Linus Torvalds","title":"Analytics","timezone":"Europe/Helsinki"},"preferences":{"theme":"light","digest":"monthly"},"permissions":["orders.read","finance.read"],"lastLoginAt":null}"""),
            Doc("""{"_id":"usr_sam","orgId":"org_fabrikam","email":"sam@fabrikam.example","role":"owner","status":"active","profile":{"name":"Sam Altman","title":"Founder","timezone":"America/Los_Angeles"},"preferences":{"theme":"dark","digest":"off"},"permissions":["org.admin"],"lastLoginAt":"2026-09-10T03:22:00Z"}"""),
        ]);

    private static Task SeedCustomersAsync(NuvexaDatabase db) =>
        db.GetCollection("customers").InsertManyAsync(
        [
            Doc("""{"_id":"cus_priya","orgId":"org_northwind","email":"priya@example.in","name":"Priya Shah","tier":"gold","loyaltyPoints":8420,"addresses":[{"label":"home","city":"Bengaluru","country":"IN","postal":"560066"},{"label":"office","city":"Bengaluru","country":"IN","postal":"560001"}],"tags":["vip","referral"],"createdAt":"2024-11-02T10:00:00Z"}"""),
            Doc("""{"_id":"cus_arjun","orgId":"org_northwind","email":"arjun@example.in","name":"Arjun Mehta","tier":"silver","loyaltyPoints":1200,"addresses":[{"label":"home","city":"Pune","country":"IN","postal":"411001"}],"tags":["repeat"],"createdAt":"2025-03-19T14:22:00Z"}"""),
            Doc("""{"_id":"cus_meera","orgId":"org_northwind","email":"meera@example.in","name":"Meera Iyer","tier":"bronze","loyaltyPoints":90,"addresses":[{"label":"home","city":"Chennai","country":"IN","postal":"600004"}],"tags":["new"],"createdAt":"2026-08-28T09:15:00Z"}"""),
            Doc("""{"_id":"cus_jonas","orgId":"org_contoso","email":"jonas@example.de","name":"Jonas Weber","tier":"gold","loyaltyPoints":5100,"addresses":[{"label":"home","city":"Berlin","country":"DE","postal":"10115"}],"tags":["b2b"],"createdAt":"2024-06-01T08:00:00Z"}"""),
            Doc("""{"_id":"cus_elena","orgId":"org_contoso","email":"elena@example.de","name":"Elena Rossi","tier":"silver","loyaltyPoints":640,"addresses":[{"label":"home","city":"Milan","country":"IT","postal":"20121"}],"tags":["eu"],"createdAt":"2025-12-11T18:40:00Z"}"""),
            Doc("""{"_id":"cus_noah","orgId":"org_fabrikam","email":"noah@example.com","name":"Noah Kim","tier":"bronze","loyaltyPoints":15,"addresses":[{"label":"home","city":"Seattle","country":"US","postal":"98104"}],"tags":["trial"],"createdAt":"2026-07-20T01:05:00Z"}"""),
        ]);

    private static Task SeedCategoriesAsync(NuvexaDatabase db) =>
        db.GetCollection("categories").InsertManyAsync(
        [
            Doc("""{"_id":"cat_hw","orgId":"org_northwind","parentId":null,"name":"Hardware","slug":"hardware","path":"/hardware","sortOrder":1}"""),
            Doc("""{"_id":"cat_laptops","orgId":"org_northwind","parentId":"cat_hw","name":"Laptops","slug":"laptops","path":"/hardware/laptops","sortOrder":1}"""),
            Doc("""{"_id":"cat_audio","orgId":"org_northwind","parentId":"cat_hw","name":"Audio","slug":"audio","path":"/hardware/audio","sortOrder":2}"""),
            Doc("""{"_id":"cat_soft","orgId":"org_northwind","parentId":null,"name":"Software","slug":"software","path":"/software","sortOrder":2}"""),
            Doc("""{"_id":"cat_home","orgId":"org_contoso","parentId":null,"name":"Home","slug":"home","path":"/home","sortOrder":1}"""),
            Doc("""{"_id":"cat_kitchen","orgId":"org_contoso","parentId":"cat_home","name":"Kitchen","slug":"kitchen","path":"/home/kitchen","sortOrder":1}"""),
        ]);

    private static Task SeedProductsAsync(NuvexaDatabase db) =>
        db.GetCollection("products").InsertManyAsync(
        [
            Doc("""{"_id":"prd_nova14","orgId":"org_northwind","sku":"NVX-NOVA-14","name":"Nova 14 Ultrabook","categoryId":"cat_laptops","status":"active","price":1299.0,"currency":"USD","variants":[{"sku":"NVX-NOVA-14-16","ram":16,"storage":512},{"sku":"NVX-NOVA-14-32","ram":32,"storage":1024}],"attributes":{"color":"obsidian","weightKg":1.18,"warrantyMonths":24},"tags":["flagship","ultrabook"],"rating":4.7}"""),
            Doc("""{"_id":"prd_nova16","orgId":"org_northwind","sku":"NVX-NOVA-16","name":"Nova 16 Creator","categoryId":"cat_laptops","status":"active","price":1899.0,"currency":"USD","variants":[{"sku":"NVX-NOVA-16-32","ram":32,"storage":1024}],"attributes":{"color":"graphite","weightKg":1.62,"warrantyMonths":24},"tags":["creator"],"rating":4.5}"""),
            Doc("""{"_id":"prd_pulse","orgId":"org_northwind","sku":"NVX-PULSE","name":"Pulse ANC Headphones","categoryId":"cat_audio","status":"active","price":249.0,"currency":"USD","variants":[{"sku":"NVX-PULSE-BLK","color":"obsidian"},{"sku":"NVX-PULSE-WHT","color":"ivory"}],"attributes":{"color":"obsidian","batteryHours":32,"warrantyMonths":12},"tags":["audio","anc"],"rating":4.4}"""),
            Doc("""{"_id":"prd_studio","orgId":"org_northwind","sku":"NVX-STUDIO","name":"Studio License","categoryId":"cat_soft","status":"active","price":99.0,"currency":"USD","variants":[{"sku":"NVX-STUDIO-YR","term":"annual"}],"attributes":{"seats":1,"channel":"download"},"tags":["software"],"rating":4.8}"""),
            Doc("""{"_id":"prd_dock","orgId":"org_northwind","sku":"NVX-DOCK","name":"Thunder Dock","categoryId":"cat_hw","status":"eol","price":179.0,"currency":"USD","variants":[],"attributes":{"ports":12,"color":"graphite"},"tags":["accessory"],"rating":4.1}"""),
            Doc("""{"_id":"prd_kettle","orgId":"org_contoso","sku":"CTS-KET-01","name":"Steel Kettle","categoryId":"cat_kitchen","status":"active","price":54.9,"currency":"EUR","variants":[{"sku":"CTS-KET-01-1.7","capacityL":1.7}],"attributes":{"color":"steel","watt":2200},"tags":["kitchen"],"rating":4.2}"""),
            Doc("""{"_id":"prd_mug","orgId":"org_contoso","sku":"CTS-MUG-04","name":"Travel Mug","categoryId":"cat_kitchen","status":"active","price":18.0,"currency":"EUR","variants":[{"sku":"CTS-MUG-04-BLU","color":"navy"}],"attributes":{"color":"navy","ml":450},"tags":["kitchen"],"rating":3.9}"""),
            Doc("""{"_id":"prd_kit","orgId":"org_fabrikam","sku":"FBK-STARTER","name":"Starter Kit","categoryId":null,"status":"draft","price":29.0,"currency":"USD","variants":[],"attributes":{"bundle":true},"tags":["promo"],"rating":0}"""),
        ]);

    private static Task SeedInventoryAsync(NuvexaDatabase db) =>
        db.GetCollection("inventory").InsertManyAsync(
        [
            Doc("""{"_id":"inv_blr_nova14","warehouseId":"wh_blr","productId":"prd_nova14","sku":"NVX-NOVA-14","onHand":42,"reserved":6,"reorderPoint":12}"""),
            Doc("""{"_id":"inv_mum_nova14","warehouseId":"wh_mum","productId":"prd_nova14","sku":"NVX-NOVA-14","onHand":11,"reserved":2,"reorderPoint":8}"""),
            Doc("""{"_id":"inv_blr_nova16","warehouseId":"wh_blr","productId":"prd_nova16","sku":"NVX-NOVA-16","onHand":9,"reserved":3,"reorderPoint":6}"""),
            Doc("""{"_id":"inv_blr_pulse","warehouseId":"wh_blr","productId":"prd_pulse","sku":"NVX-PULSE","onHand":140,"reserved":18,"reorderPoint":40}"""),
            Doc("""{"_id":"inv_mum_pulse","warehouseId":"wh_mum","productId":"prd_pulse","sku":"NVX-PULSE","onHand":55,"reserved":4,"reorderPoint":20}"""),
            Doc("""{"_id":"inv_blr_dock","warehouseId":"wh_blr","productId":"prd_dock","sku":"NVX-DOCK","onHand":3,"reserved":0,"reorderPoint":10}"""),
            Doc("""{"_id":"inv_ber_kettle","warehouseId":"wh_ber","productId":"prd_kettle","sku":"CTS-KET-01","onHand":80,"reserved":5,"reorderPoint":25}"""),
            Doc("""{"_id":"inv_ber_mug","warehouseId":"wh_ber","productId":"prd_mug","sku":"CTS-MUG-04","onHand":210,"reserved":12,"reorderPoint":50}"""),
            Doc("""{"_id":"inv_sea_kit","warehouseId":"wh_sea","productId":"prd_kit","sku":"FBK-STARTER","onHand":0,"reserved":0,"reorderPoint":20}"""),
        ]);

    private static Task SeedOrdersAsync(NuvexaDatabase db) =>
        db.GetCollection("orders").InsertManyAsync(
        [
            Doc("""{"_id":"ord_1001","orgId":"org_northwind","customerId":"cus_priya","status":"delivered","currency":"USD","totals":{"sub":1548.0,"tax":278.64,"shipping":0,"grand":1826.64},"lineItems":[{"productId":"prd_nova14","sku":"NVX-NOVA-14-16","qty":1,"price":1299.0},{"productId":"prd_pulse","sku":"NVX-PULSE-BLK","qty":1,"price":249.0}],"shipping":{"warehouseId":"wh_blr","method":"express","city":"Bengaluru"},"payment":{"method":"upi","last4":"8821"},"placedAt":"2026-08-02T07:40:00Z"}"""),
            Doc("""{"_id":"ord_1002","orgId":"org_northwind","customerId":"cus_arjun","status":"shipped","currency":"USD","totals":{"sub":249.0,"tax":44.82,"shipping":8.0,"grand":301.82},"lineItems":[{"productId":"prd_pulse","sku":"NVX-PULSE-WHT","qty":1,"price":249.0}],"shipping":{"warehouseId":"wh_mum","method":"standard","city":"Pune"},"payment":{"method":"card","last4":"4242"},"placedAt":"2026-09-08T13:05:00Z"}"""),
            Doc("""{"_id":"ord_1003","orgId":"org_northwind","customerId":"cus_meera","status":"pending","currency":"USD","totals":{"sub":99.0,"tax":17.82,"shipping":0,"grand":116.82},"lineItems":[{"productId":"prd_studio","sku":"NVX-STUDIO-YR","qty":1,"price":99.0}],"shipping":{"warehouseId":null,"method":"digital","city":"Chennai"},"payment":{"method":"card","last4":"0012"},"placedAt":"2026-09-13T05:18:00Z"}"""),
            Doc("""{"_id":"ord_1004","orgId":"org_northwind","customerId":"cus_priya","status":"cancelled","currency":"USD","totals":{"sub":179.0,"tax":32.22,"shipping":12.0,"grand":223.22},"lineItems":[{"productId":"prd_dock","sku":"NVX-DOCK","qty":1,"price":179.0}],"shipping":{"warehouseId":"wh_blr","method":"standard","city":"Bengaluru"},"payment":{"method":"card","last4":"8821"},"placedAt":"2026-06-21T16:00:00Z"}"""),
            Doc("""{"_id":"ord_2001","orgId":"org_contoso","customerId":"cus_jonas","status":"delivered","currency":"EUR","totals":{"sub":72.9,"tax":13.85,"shipping":4.9,"grand":91.65},"lineItems":[{"productId":"prd_kettle","sku":"CTS-KET-01-1.7","qty":1,"price":54.9},{"productId":"prd_mug","sku":"CTS-MUG-04-BLU","qty":1,"price":18.0}],"shipping":{"warehouseId":"wh_ber","method":"standard","city":"Berlin"},"payment":{"method":"sepa","last4":"7788"},"placedAt":"2026-07-14T10:11:00Z"}"""),
            Doc("""{"_id":"ord_2002","orgId":"org_contoso","customerId":"cus_elena","status":"processing","currency":"EUR","totals":{"sub":54.9,"tax":10.43,"shipping":6.5,"grand":71.83},"lineItems":[{"productId":"prd_kettle","sku":"CTS-KET-01-1.7","qty":1,"price":54.9}],"shipping":{"warehouseId":"wh_ber","method":"express","city":"Milan"},"payment":{"method":"card","last4":"5555"},"placedAt":"2026-09-11T09:33:00Z"}"""),
            Doc("""{"_id":"ord_3001","orgId":"org_fabrikam","customerId":"cus_noah","status":"pending","currency":"USD","totals":{"sub":29.0,"tax":2.61,"shipping":5.0,"grand":36.61},"lineItems":[{"productId":"prd_kit","sku":"FBK-STARTER","qty":1,"price":29.0}],"shipping":{"warehouseId":"wh_sea","method":"standard","city":"Seattle"},"payment":{"method":"card","last4":"1111"},"placedAt":"2026-09-12T22:48:00Z"}"""),
        ]);

    private static Task SeedInvoicesAsync(NuvexaDatabase db) =>
        db.GetCollection("invoices").InsertManyAsync(
        [
            Doc("""{"_id":"inv_n_1001","orderId":"ord_1001","customerId":"cus_priya","number":"NW-2026-1001","status":"paid","dueAt":"2026-08-16T00:00:00Z","amount":1826.64,"tax":278.64,"lineItems":[{"sku":"NVX-NOVA-14-16","qty":1,"amount":1299.0},{"sku":"NVX-PULSE-BLK","qty":1,"amount":249.0}]}"""),
            Doc("""{"_id":"inv_n_1002","orderId":"ord_1002","customerId":"cus_arjun","number":"NW-2026-1002","status":"open","dueAt":"2026-09-22T00:00:00Z","amount":301.82,"tax":44.82,"lineItems":[{"sku":"NVX-PULSE-WHT","qty":1,"amount":249.0}]}"""),
            Doc("""{"_id":"inv_n_1003","orderId":"ord_1003","customerId":"cus_meera","number":"NW-2026-1003","status":"draft","dueAt":"2026-09-27T00:00:00Z","amount":116.82,"tax":17.82,"lineItems":[{"sku":"NVX-STUDIO-YR","qty":1,"amount":99.0}]}"""),
            Doc("""{"_id":"inv_c_2001","orderId":"ord_2001","customerId":"cus_jonas","number":"CTS-2026-2001","status":"paid","dueAt":"2026-07-28T00:00:00Z","amount":91.65,"tax":13.85,"lineItems":[{"sku":"CTS-KET-01-1.7","qty":1,"amount":54.9},{"sku":"CTS-MUG-04-BLU","qty":1,"amount":18.0}]}"""),
            Doc("""{"_id":"inv_c_2002","orderId":"ord_2002","customerId":"cus_elena","number":"CTS-2026-2002","status":"open","dueAt":"2026-09-25T00:00:00Z","amount":71.83,"tax":10.43,"lineItems":[{"sku":"CTS-KET-01-1.7","qty":1,"amount":54.9}]}"""),
        ]);

    private static Task SeedPaymentsAsync(NuvexaDatabase db) =>
        db.GetCollection("payments").InsertManyAsync(
        [
            Doc("""{"_id":"pay_1001","invoiceId":"inv_n_1001","orderId":"ord_1001","method":"upi","status":"captured","amount":1826.64,"capturedAt":"2026-08-02T07:41:12Z","refunds":[]}"""),
            Doc("""{"_id":"pay_1002","invoiceId":"inv_n_1002","orderId":"ord_1002","method":"card","status":"authorized","amount":301.82,"capturedAt":null,"refunds":[]}"""),
            Doc("""{"_id":"pay_1004","invoiceId":null,"orderId":"ord_1004","method":"card","status":"refunded","amount":223.22,"capturedAt":"2026-06-21T16:01:00Z","refunds":[{"amount":223.22,"reason":"customer_cancel","at":"2026-06-22T09:10:00Z"}]}"""),
            Doc("""{"_id":"pay_2001","invoiceId":"inv_c_2001","orderId":"ord_2001","method":"sepa","status":"captured","amount":91.65,"capturedAt":"2026-07-15T11:02:00Z","refunds":[]}"""),
            Doc("""{"_id":"pay_2002","invoiceId":"inv_c_2002","orderId":"ord_2002","method":"card","status":"authorized","amount":71.83,"capturedAt":null,"refunds":[]}"""),
        ]);

    private static Task SeedShipmentsAsync(NuvexaDatabase db) =>
        db.GetCollection("shipments").InsertManyAsync(
        [
            Doc("""{"_id":"shp_1001","orderId":"ord_1001","warehouseId":"wh_blr","carrier":"Delhivery","tracking":"DLV88291001","status":"delivered","events":[{"at":"2026-08-02T12:00:00Z","code":"picked"},{"at":"2026-08-03T04:20:00Z","code":"hub"},{"at":"2026-08-04T11:15:00Z","code":"delivered","city":"Bengaluru"}]}"""),
            Doc("""{"_id":"shp_1002","orderId":"ord_1002","warehouseId":"wh_mum","carrier":"BlueDart","tracking":"BD44019002","status":"in_transit","events":[{"at":"2026-09-09T02:10:00Z","code":"picked"},{"at":"2026-09-10T21:00:00Z","code":"hub","city":"Pune"}]}"""),
            Doc("""{"_id":"shp_2001","orderId":"ord_2001","warehouseId":"wh_ber","carrier":"DHL","tracking":"DHLDE2001","status":"delivered","events":[{"at":"2026-07-15T06:00:00Z","code":"picked"},{"at":"2026-07-16T14:40:00Z","code":"delivered","city":"Berlin"}]}"""),
            Doc("""{"_id":"shp_2002","orderId":"ord_2002","warehouseId":"wh_ber","carrier":"DHL","tracking":"DHLDE2002","status":"label_created","events":[{"at":"2026-09-11T10:00:00Z","code":"label"}]}"""),
        ]);

    private static Task SeedReviewsAsync(NuvexaDatabase db) =>
        db.GetCollection("reviews").InsertManyAsync(
        [
            Doc("""{"_id":"rev_1","productId":"prd_nova14","customerId":"cus_priya","rating":5,"title":"Daily driver","body":"Fanless, all-day battery, keyboard is excellent.","verified":true,"createdAt":"2026-08-20T09:00:00Z"}"""),
            Doc("""{"_id":"rev_2","productId":"prd_pulse","customerId":"cus_priya","rating":4,"title":"Great ANC","body":"Commute-proof. App EQ is a bit buried.","verified":true,"createdAt":"2026-08-21T07:30:00Z"}"""),
            Doc("""{"_id":"rev_3","productId":"prd_pulse","customerId":"cus_arjun","rating":5,"title":"Ivory looks sharp","body":"Comfortable for long calls.","verified":true,"createdAt":"2026-09-01T18:12:00Z"}"""),
            Doc("""{"_id":"rev_4","productId":"prd_studio","customerId":"cus_meera","rating":5,"title":"License activated instantly","body":"Offline project file just worked.","verified":false,"createdAt":"2026-09-13T06:00:00Z"}"""),
            Doc("""{"_id":"rev_5","productId":"prd_kettle","customerId":"cus_jonas","rating":4,"title":"Boils fast","body":"A bit loud at the end of the cycle.","verified":true,"createdAt":"2026-07-20T12:00:00Z"}"""),
        ]);

    private static Task SeedTicketsAsync(NuvexaDatabase db) =>
        db.GetCollection("tickets").InsertManyAsync(
        [
            Doc("""{"_id":"tkt_401","orgId":"org_northwind","customerId":"cus_arjun","orderId":"ord_1002","status":"open","priority":"high","subject":"Where is my Pulse shipment?","comments":[{"by":"cus_arjun","at":"2026-09-13T08:00:00Z","body":"Tracking stuck at Pune hub."},{"by":"usr_alan","at":"2026-09-13T08:40:00Z","body":"Carrier confirmed outbound scan tonight."}]}"""),
            Doc("""{"_id":"tkt_402","orgId":"org_northwind","customerId":"cus_priya","orderId":"ord_1004","status":"resolved","priority":"low","subject":"Refund confirmation","comments":[{"by":"cus_priya","at":"2026-06-22T10:00:00Z","body":"Need written refund."},{"by":"usr_alan","at":"2026-06-22T10:20:00Z","body":"Refunded in full, see pay_1004."}]}"""),
            Doc("""{"_id":"tkt_501","orgId":"org_contoso","customerId":"cus_elena","orderId":"ord_2002","status":"pending","priority":"normal","subject":"Change delivery address","comments":[{"by":"cus_elena","at":"2026-09-12T07:15:00Z","body":"Please reroute to office in Porta Nuova."}]}"""),
        ]);

    private static Task SeedAuditAsync(NuvexaDatabase db) =>
        db.GetCollection("audit_events").InsertManyAsync(
        [
            Doc("""{"_id":"aud_1","orgId":"org_northwind","actorId":"usr_ada","action":"product.update","entity":"products","entityId":"prd_nova14","at":"2026-08-01T06:00:00Z","meta":{"fields":["price","variants"]} }"""),
            Doc("""{"_id":"aud_2","orgId":"org_northwind","actorId":"usr_grace","action":"inventory.adjust","entity":"inventory","entityId":"inv_blr_pulse","at":"2026-09-07T04:30:00Z","meta":{"delta":20,"reason":"inbound"} }"""),
            Doc("""{"_id":"aud_3","orgId":"org_northwind","actorId":"usr_alan","action":"ticket.resolve","entity":"tickets","entityId":"tkt_402","at":"2026-06-22T10:21:00Z","meta":{"refund":true} }"""),
            Doc("""{"_id":"aud_4","orgId":"org_contoso","actorId":"usr_kara","action":"order.create","entity":"orders","entityId":"ord_2002","at":"2026-09-11T09:33:10Z","meta":{"channel":"web"} }"""),
            Doc("""{"_id":"aud_5","orgId":"org_fabrikam","actorId":"usr_sam","action":"flag.toggle","entity":"feature_flags","entityId":"flg_checkout_v2","at":"2026-09-01T00:00:00Z","meta":{"enabled":false} }"""),
        ]);

    private static Task SeedFlagsAsync(NuvexaDatabase db) =>
        db.GetCollection("feature_flags").InsertManyAsync(
        [
            Doc("""{"_id":"flg_checkout_v2","orgId":"org_northwind","key":"checkout.v2","enabled":true,"rollout":80,"rules":[{"attr":"tier","op":"in","values":["gold","silver"]}]}"""),
            Doc("""{"_id":"flg_search_vec","orgId":"org_northwind","key":"search.vector","enabled":false,"rollout":0,"rules":[]}"""),
            Doc("""{"_id":"flg_eu_vat","orgId":"org_contoso","key":"tax.oss","enabled":true,"rollout":100,"rules":[{"attr":"region","op":"eq","values":["emea"]}]}"""),
            Doc("""{"_id":"flg_beta_kit","orgId":"org_fabrikam","key":"catalog.starter","enabled":false,"rollout":10,"rules":[]}"""),
        ]);

    private static async Task SeedFilesAsync(NuvexaDatabase db)
    {
        await using var invoice = new MemoryStream(Encoding.UTF8.GetBytes(
            "NuvexaDB demo invoice NW-2026-1001\nCustomer: Priya Shah\nGrand: 1826.64 USD\n"));
        await db.Files.UploadAsync("invoices/NW-2026-1001.txt", invoice);

        await using var spec = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"sku":"NVX-NOVA-14","cpu":"8c","display":"14.2 OLED","batteryWh":72}"""));
        await db.Files.UploadAsync("catalog/NVX-NOVA-14.spec.json", spec);
    }

    private static async Task EnsureIndexesAsync(NuvexaDatabase db)
    {
        await db.GetCollection("organizations").EnsureIndexAsync("slug", unique: true);
        await db.GetCollection("warehouses").EnsureIndexAsync("code", unique: true);
        await db.GetCollection("users").EnsureIndexAsync("email", unique: true);
        await db.GetCollection("users").EnsureIndexAsync(["orgId", "role"]);
        await db.GetCollection("customers").EnsureIndexAsync(["orgId", "email"]);
        await db.GetCollection("customers").EnsureIndexAsync("tier");
        await db.GetCollection("categories").EnsureIndexAsync(["orgId", "parentId"]);
        await db.GetCollection("products").EnsureIndexAsync("sku", unique: true);
        await db.GetCollection("products").EnsureIndexAsync(["orgId", "status"]);
        await db.GetCollection("products").EnsureIndexAsync("categoryId");
        await db.GetCollection("inventory").EnsureIndexAsync(["warehouseId", "productId"]);
        await db.GetCollection("orders").EnsureIndexAsync(["orgId", "status"]);
        await db.GetCollection("orders").EnsureIndexAsync("customerId");
        await db.GetCollection("orders").EnsureIndexAsync("placedAt");
        await db.GetCollection("invoices").EnsureIndexAsync("number", unique: true);
        await db.GetCollection("invoices").EnsureIndexAsync("orderId");
        await db.GetCollection("payments").EnsureIndexAsync("orderId");
        await db.GetCollection("shipments").EnsureIndexAsync("tracking");
        await db.GetCollection("reviews").EnsureIndexAsync(["productId", "rating"]);
        await db.GetCollection("tickets").EnsureIndexAsync(["orgId", "status"]);
        await db.GetCollection("audit_events").EnsureIndexAsync(["orgId", "at"]);
        await db.GetCollection("feature_flags").EnsureIndexAsync(["orgId", "key"]);
    }

    private static NuvexaDocument Doc(string json) => NuvexaDocument.Parse(json);

    private static NuvexaDocument Schema(string collection, params (string Name, string Type, string Default, bool Unique)[] columns)
    {
        var types = new JsonObject();
        var defaults = new JsonObject();
        var fields = new JsonArray();
        var unique = new JsonArray();
        foreach (var col in columns)
        {
            fields.Add(col.Name);
            types[col.Name] = col.Type;
            defaults[col.Name] = col.Default;
            if (col.Unique)
            {
                unique.Add(col.Name);
            }
        }

        return new NuvexaDocument(new JsonObject
        {
            ["_id"] = collection,
            ["fields"] = fields,
            ["types"] = types,
            ["defaults"] = defaults,
            ["unique"] = unique
        });
    }

    private static (string Name, string Type, string Default, bool Unique) Col(
        string name,
        string type,
        string defaultValue = "",
        bool unique = false) => (name, type, defaultValue, unique);
}
