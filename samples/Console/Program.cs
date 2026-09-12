using Nuventra.NuvexaDB;
using Nuventra.NuvexaDB.Query;

var path = Path.Combine(AppContext.BaseDirectory, "sample.nvx");
if (File.Exists(path))
{
    File.Delete(path);
}

const string key = "sample-key";
await using (var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = key }))
{
    var users = db.GetCollection("users");
    await users.InsertManyAsync([
        NuvexaDocument.Parse("""{"name":"Ada","age":36,"address":{"city":"London"}}"""),
        NuvexaDocument.Parse("""{"name":"Grace","age":85,"address":{"city":"New York"}}"""),
        NuvexaDocument.Parse("""{"name":"Cara","age":21,"address":{"city":"Bengaluru"}}""")
    ]);
    await users.EnsureIndexAsync("age");
    await db.CheckpointAsync();
    Console.WriteLine($"Wrote encrypted sample to {path}");
}

Console.WriteLine($"IsEncrypted: {NuvexaDatabase.IsEncrypted(path)}");

try
{
    NuvexaDatabase.Open(path);
    Console.WriteLine("ERROR: open without key should have failed.");
}
catch (NuvexaEncryptionException ex)
{
    Console.WriteLine($"Lib fail-closed: {ex.Message}");
}

await using var opened = NuvexaDatabase.Open(path, new NuvexaOpenOptions { EncryptionKey = key });
var result = await opened.ExecuteAsync("""db.users.find({ age: { $gte: 21 } }).sort({ name: 1 })""");
foreach (var doc in result.Documents)
{
    Console.WriteLine(doc.ToJson());
}
