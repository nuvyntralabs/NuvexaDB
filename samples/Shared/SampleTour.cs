using System.Text;
using Nuventra.NuvexaDB;
using Nuventra.NuvexaDB.Query;

namespace NuvexaDB.Samples;

/// <summary>
/// Shared integration tour: create DB, create collection, CRUD, complex NQL + fluent find.
/// </summary>
public static class SampleTour
{
    public const string EncryptionKey = "sample-key";

    public static void ResetFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        if (File.Exists(path + "-wal"))
        {
            File.Delete(path + "-wal");
        }
    }

    public static async Task<string> RunAsync(string path)
    {
        ResetFile(path);
        var log = new StringBuilder();

        await using (var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = EncryptionKey }))
        {
            var users = db.GetCollection("users");
            log.AppendLine($"Created collection '{users.Name}'.");

            var adaId = await users.InsertAsync(NuvexaDocument.Parse(
                """{"name":"Ada","age":36,"status":"active","address":{"city":"London"}}"""));
            await users.InsertManyAsync([
                NuvexaDocument.Parse("""{"name":"Grace","age":85,"status":"retired","address":{"city":"New York"}}"""),
                NuvexaDocument.Parse("""{"name":"Cara","age":21,"status":"active","address":{"city":"Bengaluru"}}"""),
                NuvexaDocument.Parse("""{"name":"Alan","age":42,"status":"active","address":{"city":"London"}}""")
            ]);
            var scratchId = await users.InsertAsync(NuvexaDocument.Parse(
                """{"name":"Scratch","age":19,"status":"active","address":{"city":"Paris"}}"""));

            await users.EnsureIndexAsync("age");
            await users.EnsureIndexAsync(["address.city", "status"]);
            log.AppendLine("Collections: " + string.Join(", ", db.GetCollectionNames()));

            var ada = await users.FindByIdAsync(adaId);
            log.AppendLine("Read Ada: " + ada?.ToJson());

            await users.ReplaceAsync(NuvexaDocument.Parse(
                $"{{\"_id\":\"{adaId}\",\"name\":\"Ada Lovelace\",\"age\":36,\"status\":\"active\",\"address\":{{\"city\":\"London\"}}}}"));
            log.AppendLine("Updated Ada: " + (await users.FindByIdAsync(adaId))?.ToJson());

            log.AppendLine("Deleted scratch: " + await users.DeleteByIdAsync(scratchId));

            log.AppendLine("-- NQL age >= 21, sort name, limit 10 --");
            AppendDocs(log, await db.ExecuteAsync("""db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(10)"""));

            log.AppendLine("-- NQL $and London + active, sort age desc --");
            AppendDocs(log, await db.ExecuteAsync(
                """db.users.find({ $and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })"""));

            log.AppendLine("-- NQL $or age < 30 or New York --");
            AppendDocs(log, await db.ExecuteAsync(
                """db.users.find({ $or: [ { age: { $lt: 30 } }, { "address.city": "New York" } ] })"""));

            log.AppendLine("-- Fluent filter: active adults --");
            var fluent = await users
                .Find(NuvexaFilter.And(NuvexaFilter.Gte("age", 21), NuvexaFilter.Eq("status", "active")))
                .Sort("name")
                .Limit(10)
                .ToListAsync();
            foreach (var doc in fluent)
            {
                log.AppendLine(doc.ToJson());
            }

            await db.CheckpointAsync();
        }

        log.AppendLine($"IsEncrypted: {NuvexaDatabase.IsEncrypted(path)}");
        try
        {
            NuvexaDatabase.Open(path);
            log.AppendLine("ERROR: open without key should have failed.");
        }
        catch (NuvexaEncryptionException ex)
        {
            log.AppendLine("Lib fail-closed: " + ex.Message);
        }

        return log.ToString();
    }

    private static void AppendDocs(StringBuilder log, NuvexaQueryResult result)
    {
        foreach (var doc in result.Documents)
        {
            log.AppendLine(doc.ToJson());
        }
    }
}
