using Nuventra.NuvexaDB;

var dest = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads",
        "NuvexaRelationships.nvx");

Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
foreach (var leftover in new[] { dest, dest + "-wal" })
{
    if (File.Exists(leftover))
    {
        File.Delete(leftover);
    }
}

await using (var db = NuvexaDatabase.Create(dest))
{
    await SeedAsync(db);
    await db.CheckpointAsync();
    var joined = await db.ExecuteAsync(
        """db.order_items.aggregate([{ $lookup: { from: "orders", localField: "orderId", foreignField: "_id", as: "order" } }, { $lookup: { from: "products", localField: "productId", foreignField: "_id", as: "product" } }])""");
    if (joined.Documents.Count != 4)
    {
        throw new InvalidOperationException("Expected 4 order_items after $lookup.");
    }
}

Console.WriteLine($"Wrote {dest} ({new FileInfo(dest).Length} bytes, unencrypted).");
Console.WriteLine();
Console.WriteLine("Layers:");
Console.WriteLine("  regions → cities → warehouses → inventory");
Console.WriteLine("  categories → products");
Console.WriteLine("  customers → orders → order_items / payments / shipments");
Console.WriteLine();
Console.WriteLine("NQL (Data Studio → NQL tab):");
Console.WriteLine("""  db.orders.aggregate([{ $lookup: { from: "customers", localField: "customerId", foreignField: "_id", as: "customer" } }])""");
Console.WriteLine("""  db.order_items.aggregate([{ $lookup: { from: "orders", localField: "orderId", foreignField: "_id", as: "order" } }, { $lookup: { from: "products", localField: "productId", foreignField: "_id", as: "product" } }])""");
Console.WriteLine("""  db.inventory.aggregate([{ $lookup: { from: "warehouses", localField: "warehouseId", foreignField: "_id", as: "warehouse" } }, { $lookup: { from: "products", localField: "productId", foreignField: "_id", as: "product" } }])""");

static async Task SeedAsync(NuvexaDatabase db)
{
    await InsertAsync(db, "regions",
        """{"_id":"reg-west","name":"West","country":"India"}""",
        """{"_id":"reg-south","name":"South","country":"India"}""");

    await InsertAsync(db, "cities",
        """{"_id":"city-pune","regionId":"reg-west","name":"Pune"}""",
        """{"_id":"city-mumbai","regionId":"reg-west","name":"Mumbai"}""",
        """{"_id":"city-blr","regionId":"reg-south","name":"Bengaluru"}""");
    await db.GetCollection("cities").EnsureIndexAsync("regionId");

    await InsertAsync(db, "warehouses",
        """{"_id":"wh-pune-1","cityId":"city-pune","name":"Pune FC","code":"PNQ-1"}""",
        """{"_id":"wh-blr-1","cityId":"city-blr","name":"Bengaluru FC","code":"BLR-1"}""");
    await db.GetCollection("warehouses").EnsureIndexAsync("cityId");

    await InsertAsync(db, "categories",
        """{"_id":"cat-electronics","name":"Electronics"}""",
        """{"_id":"cat-home","name":"Home"}""");

    await InsertAsync(db, "products",
        """{"_id":"prd-phone","categoryId":"cat-electronics","sku":"PH-01","name":"Nuvexa Phone","price":24999,"specs":{"color":"black","storageGb":128}}""",
        """{"_id":"prd-laptop","categoryId":"cat-electronics","sku":"LT-01","name":"Nuvexa Laptop","price":79999,"specs":{"color":"silver","storageGb":512}}""",
        """{"_id":"prd-lamp","categoryId":"cat-home","sku":"HM-01","name":"Desk Lamp","price":1299,"specs":{"color":"white","weightKg":0.8}}""");
    await db.GetCollection("products").EnsureIndexAsync("categoryId");

    await InsertAsync(db, "customers",
        """{"_id":"cust-ada","cityId":"city-pune","name":"Ada","email":"ada@example.com","address":{"line1":"12 FC Road","city":"Pune"}}""",
        """{"_id":"cust-grace","cityId":"city-blr","name":"Grace","email":"grace@example.com","address":{"line1":"88 MG Road","city":"Bengaluru"}}""",
        """{"_id":"cust-cara","cityId":"city-mumbai","name":"Cara","email":"cara@example.com","address":{"line1":"4 Bandra West","city":"Mumbai"}}""");
    await db.GetCollection("customers").EnsureIndexAsync("cityId");
    await db.GetCollection("customers").EnsureIndexAsync("email", unique: true);

    await InsertAsync(db, "orders",
        """{"_id":"ord-1001","customerId":"cust-ada","warehouseId":"wh-pune-1","status":"paid","total":27597,"placedAt":"2026-03-01T10:00:00Z"}""",
        """{"_id":"ord-1002","customerId":"cust-ada","warehouseId":"wh-pune-1","status":"open","total":79999,"placedAt":"2026-03-08T14:30:00Z"}""",
        """{"_id":"ord-1003","customerId":"cust-grace","warehouseId":"wh-blr-1","status":"paid","total":24999,"placedAt":"2026-03-10T09:15:00Z"}""");
    await db.GetCollection("orders").EnsureIndexAsync("customerId");
    await db.GetCollection("orders").EnsureIndexAsync("warehouseId");
    await db.GetCollection("orders").EnsureIndexAsync("status");

    await InsertAsync(db, "order_items",
        """{"_id":"item-1001-1","orderId":"ord-1001","productId":"prd-phone","qty":1,"unitPrice":24999}""",
        """{"_id":"item-1001-2","orderId":"ord-1001","productId":"prd-lamp","qty":2,"unitPrice":1299}""",
        """{"_id":"item-1002-1","orderId":"ord-1002","productId":"prd-laptop","qty":1,"unitPrice":79999}""",
        """{"_id":"item-1003-1","orderId":"ord-1003","productId":"prd-phone","qty":1,"unitPrice":24999}""");
    await db.GetCollection("order_items").EnsureIndexAsync("orderId");
    await db.GetCollection("order_items").EnsureIndexAsync("productId");

    await InsertAsync(db, "payments",
        """{"_id":"pay-1001","orderId":"ord-1001","method":"card","amount":27597,"status":"paid"}""",
        """{"_id":"pay-1003","orderId":"ord-1003","method":"upi","amount":24999,"status":"paid"}""");
    await db.GetCollection("payments").EnsureIndexAsync("orderId");

    await InsertAsync(db, "shipments",
        """{"_id":"ship-1001","orderId":"ord-1001","carrier":"Delhivery","status":"delivered","events":[{"at":"2026-03-02T08:00:00Z","note":"picked up"},{"at":"2026-03-04T18:20:00Z","note":"delivered"}]}""",
        """{"_id":"ship-1003","orderId":"ord-1003","carrier":"BlueDart","status":"in_transit","events":[{"at":"2026-03-10T16:00:00Z","note":"picked up"}]}""");
    await db.GetCollection("shipments").EnsureIndexAsync("orderId");

    await InsertAsync(db, "inventory",
        """{"_id":"inv-1","warehouseId":"wh-pune-1","productId":"prd-phone","qty":40}""",
        """{"_id":"inv-2","warehouseId":"wh-pune-1","productId":"prd-lamp","qty":12}""",
        """{"_id":"inv-3","warehouseId":"wh-blr-1","productId":"prd-phone","qty":8}""",
        """{"_id":"inv-4","warehouseId":"wh-blr-1","productId":"prd-laptop","qty":5}""");
    await db.GetCollection("inventory").EnsureIndexAsync(["warehouseId", "productId"]);
}

static Task InsertAsync(NuvexaDatabase db, string collection, params string[] json) =>
    db.GetCollection(collection).InsertManyAsync(json.Select(NuvexaDocument.Parse));
