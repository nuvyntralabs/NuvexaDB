using System.Text.Json;
using Nuventra.NuvexaDB;
using Nuventra.NuvexaDB.Tools;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: nuvexa <info|collections|find|query> <file.nvx> [--key KEY] [args]");
    return 1;
}

var command = args[0];
if (command is "-h" or "--help")
{
    Console.WriteLine("nuvexa info <file.nvx> [--key KEY]");
    Console.WriteLine("nuvexa collections <file.nvx> [--key KEY]");
    Console.WriteLine("nuvexa indexes <file.nvx> <collection> [--key KEY]");
    Console.WriteLine("nuvexa find <file.nvx> <collection> [filterJson] [--key KEY]");
    Console.WriteLine("nuvexa query <file.nvx> <mongoQuery> [--key KEY]");
    Console.WriteLine("nuvexa tree <file.nvx> [--key KEY]");
    Console.WriteLine("nuvexa compact <file.nvx> [--key KEY]");
    Console.WriteLine("nuvexa changkey <file.nvx> --key CURRENT --new NEXT");
    return 0;
}

if (args.Length < 2)
{
    Console.Error.WriteLine("A .nvx path is required.");
    return 1;
}

var path = args[1];
string? key = null;
string? nextKey = null;
var rest = new List<string>();
for (var i = 2; i < args.Length; i++)
{
    if (args[i] == "--key" && i + 1 < args.Length)
    {
        key = args[++i];
        continue;
    }

    if (args[i] == "--new" && i + 1 < args.Length)
    {
        nextKey = args[++i];
        continue;
    }

    rest.Add(args[i]);
}

try
{
    if (command == "info" && File.Exists(path))
    {
        var encrypted = NuvexaDatabase.IsEncrypted(path);
        if (encrypted && key is null)
        {
            Write(new { encrypted, error = "encrypted" });
            return 2;
        }
    }

    await using var session = new ExplorerSession();
    await session.OpenAsync(path, key);
    switch (command)
    {
        case "info":
            Write(session.Stats());
            break;
        case "collections":
            Write(session.Collections());
            break;
        case "tree":
            Write(await session.LoadTreeAsync());
            break;
        case "compact":
            await session.CompactAsync();
            Write(new { ok = true, compacted = session.Path });
            break;
        case "indexes":
            if (rest.Count == 0)
            {
                Console.Error.WriteLine("Collection name required.");
                return 1;
            }

            Write(await session.ListIndexesAsync(rest[0]));
            break;
        case "changkey":
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(nextKey))
            {
                Console.Error.WriteLine("changkey requires --key CURRENT --new NEXT.");
                return 1;
            }

            await session.ChangeEncryptionKeyAsync(key, nextKey);
            Write(new { ok = true });
            break;
        case "find":
            if (rest.Count == 0)
            {
                Console.Error.WriteLine("Collection name required.");
                return 1;
            }

            var filter = rest.Count > 1 ? rest[1] : "{}";
            var docs = await session.QueryAsync($"db.{rest[0]}.find({filter}).limit(200)");
            Write(docs.Select(d => JsonDocument.Parse(d.ToJson()).RootElement.Clone()).ToList());
            break;
        case "query":
            if (rest.Count == 0)
            {
                Console.Error.WriteLine("Query text required.");
                return 1;
            }

            var result = await session.QueryAsync(string.Join(' ', rest));
            Write(result.Select(d => JsonDocument.Parse(d.ToJson()).RootElement.Clone()).ToList());
            break;
        default:
            Console.Error.WriteLine($"Unknown command '{command}'.");
            return 1;
    }

    return 0;
}
catch (NuvexaEncryptionException ex)
{
    Write(new { encrypted = true, error = ex.Message });
    return 2;
}

static void Write(object value) =>
    Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
