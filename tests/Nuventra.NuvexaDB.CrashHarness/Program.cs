using Nuventra.NuvexaDB;

if (args.Length < 1)
{
    return 2;
}

var path = args[0];
if (File.Exists(path))
{
    File.Delete(path);
}

if (File.Exists(path + "-wal"))
{
    File.Delete(path + "-wal");
}

using var db = NuvexaDatabase.Create(path);
var id = db.GetCollection("crash").InsertAsync(NuvexaDocument.Parse("""{"ok":true}""")).GetAwaiter().GetResult();
File.WriteAllText(path + ".id", id);
Environment.FailFast("nuvexa-crash-harness");
return 0;
