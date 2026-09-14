using System.Text.Json;
using Nuventra.NuvexaDB;
using Nuventra.NuvexaDB.Tools;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: nuvexa <info|collections|find|browse|query|replace|insert|delete-id|samples|explain> <file.nvx> [--key KEY] [args]");
    return 1;
}

var command = args[0];
if (command is "-h" or "--help")
{
    Console.WriteLine("nuvexa info <file.nvx> [--key KEY]");
    Console.WriteLine("nuvexa collections <file.nvx> [--key KEY]");
    Console.WriteLine("nuvexa indexes <file.nvx> <collection> [--key KEY]");
    Console.WriteLine("nuvexa find <file.nvx> <collection> [filterJson] [--skip N] [--limit N] [--page N] [--key KEY]");
    Console.WriteLine("nuvexa browse <file.nvx> <collection> [--filter TEXT] [--page N] [--limit N] [--key KEY]");
    Console.WriteLine("nuvexa query <file.nvx> <nql> [--key KEY]");
    Console.WriteLine("nuvexa insert <file.nvx> <collection> <json> [--key KEY]");
    Console.WriteLine("nuvexa replace <file.nvx> <collection> <json> [--key KEY]");
    Console.WriteLine("nuvexa delete-id <file.nvx> <collection> <id> [--key KEY]");
    Console.WriteLine("nuvexa explain <file.nvx> <nql|collection> [--filter TEXT] [--key KEY]");
    Console.WriteLine("nuvexa samples [collection]");
    Console.WriteLine("nuvexa tree <file.nvx> [--key KEY]");
    Console.WriteLine("nuvexa compact <file.nvx> [--key KEY]");
    Console.WriteLine("nuvexa backup <file.nvx> <dest.nvx> [--key KEY]");
    Console.WriteLine("nuvexa restore <backup.nvx> <dest.nvx> [--overwrite]");
    Console.WriteLine("nuvexa changkey <file.nvx> --key CURRENT --new NEXT");
    return 0;
}

if (command == "samples")
{
    var collection = "users";
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] is "--collection" && i + 1 < args.Length)
        {
            collection = args[++i];
        }
        else if (!args[i].StartsWith('-'))
        {
            collection = args[i];
        }
    }

    Write(ExplorerQuerySample.All.Select(s => new { s.Title, Query = s.Resolve(collection) }));
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
string? filterOpt = null;
int? pageOpt = null;
int? skipOpt = null;
int? limitOpt = null;
var overwrite = false;
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

    if (args[i] == "--filter" && i + 1 < args.Length)
    {
        filterOpt = args[++i];
        continue;
    }

    if (args[i] == "--page" && i + 1 < args.Length)
    {
        pageOpt = int.Parse(args[++i]);
        continue;
    }

    if (args[i] == "--skip" && i + 1 < args.Length)
    {
        skipOpt = int.Parse(args[++i]);
        continue;
    }

    if (args[i] == "--limit" && i + 1 < args.Length)
    {
        limitOpt = int.Parse(args[++i]);
        continue;
    }

    if (args[i] is "--overwrite")
    {
        overwrite = true;
        continue;
    }

    rest.Add(args[i]);
}

try
{
    if (command == "restore")
    {
        if (rest.Count == 0)
        {
            Console.Error.WriteLine("Destination path required.");
            return 1;
        }

        await NuvexaDatabase.RestoreAsync(path, rest[0], overwrite);
        Write(new { ok = true, backup = path, restored = rest[0] });
        return 0;
    }

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
        case "backup":
            if (rest.Count == 0)
            {
                Console.Error.WriteLine("Destination path required.");
                return 1;
            }

            await session.BackupAsync(rest[0]);
            Write(new { ok = true, backup = rest[0] });
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

            var findFilter = filterOpt ?? (rest.Count > 1 ? rest[1] : "{}");
            if (pageOpt is int findPage)
            {
                var page = await session.BrowsePageAsync(rest[0], findFilter, findPage, limitOpt ?? BrowsePageResult.DefaultPageSize);
                Write(Docs(page.Documents));
            }
            else
            {
                Write(Docs(await session.FindAsync(rest[0], findFilter, limitOpt ?? 200, skipOpt ?? 0)));
            }

            break;
        case "browse":
            if (rest.Count == 0)
            {
                Console.Error.WriteLine("Collection name required.");
                return 1;
            }

            var browseFilter = filterOpt ?? (rest.Count > 1 ? rest[1] : null);
            var browse = await session.BrowsePageAsync(
                rest[0],
                browseFilter,
                pageOpt ?? 0,
                limitOpt ?? BrowsePageResult.DefaultPageSize);
            Write(new
            {
                browse.Page,
                browse.PageSize,
                browse.HasPrevious,
                browse.HasNext,
                browse.Status,
                browse.PageText,
                browse.Explain,
                browse.FilterJson,
                browse.CollectionTotal,
                Documents = Docs(browse.Documents)
            });
            break;
        case "query":
            if (rest.Count == 0)
            {
                Console.Error.WriteLine("Query text required.");
                return 1;
            }

            var result = await session.ExecuteAsync(string.Join(' ', rest));
            if (result.Operation is "update" or "delete")
            {
                Write(new { result.Operation, result.Collection, result.Affected });
            }
            else
            {
                Write(Docs(result.Documents));
            }

            break;
        case "insert":
            if (rest.Count < 2)
            {
                Console.Error.WriteLine("Collection and JSON required.");
                return 1;
            }

            var inserted = await session.InsertDocumentAsync(rest[0], string.Join(' ', rest.Skip(1)));
            Write(new { ok = true, id = inserted });
            break;
        case "replace":
            if (rest.Count < 2)
            {
                Console.Error.WriteLine("Collection and JSON required.");
                return 1;
            }

            await session.ReplaceDocumentAsync(rest[0], string.Join(' ', rest.Skip(1)));
            Write(new { ok = true });
            break;
        case "delete-id":
            if (rest.Count < 2)
            {
                Console.Error.WriteLine("Collection and document id required.");
                return 1;
            }

            var removed = await session.DeleteDocumentAsync(rest[0], rest[1]);
            Write(new { ok = removed });
            break;
        case "explain":
            if (rest.Count == 0)
            {
                Console.Error.WriteLine("Query text or collection name required.");
                return 1;
            }

            var explainText = string.Join(' ', rest);
            if (explainText.StartsWith("db.", StringComparison.Ordinal))
            {
                var plan = await session.ExplainQueryAsync(explainText);
                Write(new
                {
                    explain = ExplorerSession.FormatExplain(plan),
                    plan.Strategy,
                    plan.Collection,
                    plan.IndexName,
                    plan.Examined,
                    plan.Returned
                });
            }
            else
            {
                var filter = ExplorerSession.NormalizeBrowseFilter(filterOpt ?? (rest.Count > 1 ? rest[1] : "{}"));
                var plan = await session.ExplainAsync(rest[0], filter);
                Write(new
                {
                    explain = ExplorerSession.FormatExplain(plan),
                    plan.Strategy,
                    plan.Collection,
                    plan.IndexName,
                    plan.Examined,
                    plan.Returned
                });
            }

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
catch (NuvexaIntegrityException ex)
{
    Write(new { tampered = true, error = ex.Message });
    return 3;
}
catch (NuvexaException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static List<JsonElement> Docs(IEnumerable<NuvexaDocument> docs) =>
    docs.Select(d => JsonDocument.Parse(d.ToJson()).RootElement.Clone()).ToList();

static void Write(object value) =>
    Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
