using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Nuventra.NuvexaDB;
using Nuventra.NuvexaDB.Documents;
using Nuventra.NuvexaDB.Query;

namespace Nuventra.NuvexaDB.Tools;

/// <summary>UI-agnostic explorer session. Optional temp SQLite cache is deleted on dispose.</summary>
public sealed class ExplorerSession : IAsyncDisposable
{
    private NuvexaDatabase? _db;
    private SqliteConnection? _cache;
    private string? _cachePath;
    private string? _encryptionKey;

    public string? Path { get; private set; }
    public bool Encrypted { get; private set; }
    public bool IsOpen => _db is not null;

    internal const string SchemaCollectionName = "__nuvexa_schema";

    public static bool PeekEncrypted(string path) => NuvexaDatabase.IsEncrypted(path);

    public async Task OpenAsync(string path, string? encryptionKey, CancellationToken cancellationToken = default)
    {
        await DisposeAsync().ConfigureAwait(false);
        _db = await NuvexaDatabase.OpenAsync(path, new NuvexaOpenOptions { EncryptionKey = encryptionKey }, cancellationToken)
            .ConfigureAwait(false);
        Path = path;
        Encrypted = _db.Encrypted;
        _encryptionKey = encryptionKey;
        OpenCache();
    }

    public async Task CreateAsync(string path, string? encryptionKey, CancellationToken cancellationToken = default)
    {
        await DisposeAsync().ConfigureAwait(false);
        _db = await NuvexaDatabase.CreateAsync(path, NuvexaCreateOptions.ForDesktop(encryptionKey), cancellationToken)
            .ConfigureAwait(false);
        Path = path;
        Encrypted = _db.Encrypted;
        _encryptionKey = encryptionKey;
        OpenCache();
    }

    public IReadOnlyList<string> Collections()
    {
        EnsureOpen();
        return _db!.GetCollectionNames().Where(n => n != SchemaCollectionName).ToList();
    }

    public NuvexaStats Stats()
    {
        EnsureOpen();
        var raw = _db!.GetStats();
        var hiddenCollections = 0;
        var hiddenDocuments = 0L;
        foreach (var name in _db.GetCollectionNames())
        {
            if (name != SchemaCollectionName)
            {
                continue;
            }

            hiddenCollections++;
            hiddenDocuments += _db.GetCollection(name).Count;
        }

        return new NuvexaStats
        {
            Path = raw.Path,
            FileBytes = raw.FileBytes,
            WalBytes = raw.WalBytes,
            PageCount = raw.PageCount,
            DocumentCount = Math.Max(0, raw.DocumentCount - hiddenDocuments),
            CollectionCount = Math.Max(0, raw.CollectionCount - hiddenCollections),
            CachedPages = raw.CachedPages,
            CacheSizeMb = raw.CacheSizeMb,
            Encrypted = raw.Encrypted,
            CommittedLsn = raw.CommittedLsn,
            CompactNeeded = raw.CompactNeeded
        };
    }

    public async Task<List<NuvexaDocument>> QueryAsync(string text, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var result = await _db!.ExecuteAsync(text, cancellationToken).ConfigureAwait(false);
        MaterializeCache(result.Collection, result.Documents);
        return result.Documents;
    }

    public Task<List<NuvexaDocument>> ListAsync(string collection, int limit = 200, CancellationToken cancellationToken = default) =>
        FindAsync(collection, "{}", limit, skip: 0, cancellationToken: cancellationToken);

    public async Task<BrowsePageResult> BrowsePageAsync(
        string collection,
        string? filterText = null,
        int page = 0,
        int pageSize = BrowsePageResult.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        if (pageSize < 1)
        {
            throw new NuvexaException("Browse page size must be at least 1.");
        }

        var filterJson = NormalizeBrowseFilter(filterText);
        var total = CollectionCount(collection);
        var filtered = !string.IsNullOrWhiteSpace(filterText);
        if (!filtered && total > 0)
        {
            var lastPage = (int)((total - 1) / pageSize);
            if (page > lastPage)
            {
                page = lastPage;
            }
        }

        page = Math.Max(0, page);
        var skip = page * pageSize;
        var docs = await FindAsync(collection, filterJson, pageSize + 1, skip, cancellationToken).ConfigureAwait(false);
        var hasNext = docs.Count > pageSize;
        if (hasNext)
        {
            docs = docs.Take(pageSize).ToList();
        }

        var plan = await ExplainAsync(collection, filterJson, cancellationToken).ConfigureAwait(false);
        var from = skip + 1;
        var to = skip + docs.Count;
        string status;
        string pageText;
        if (filtered)
        {
            status = docs.Count == 0
                ? $"No matching rows. Collection has {total}."
                : hasNext || page > 0
                    ? $"Showing {from}–{to} matching row(s). Collection has {total}."
                    : $"{docs.Count} matching row(s). Collection has {total}.";
            pageText = hasNext || page > 0 ? $"Page {page + 1}" : "";
        }
        else if (docs.Count == 0)
        {
            status = "0 record(s).";
            pageText = "";
        }
        else if (hasNext || page > 0)
        {
            status = $"Showing {from}–{to} of {total}.";
            var pages = (int)Math.Ceiling(total / (double)pageSize);
            pageText = $"Page {page + 1} of {pages}";
        }
        else
        {
            status = $"{docs.Count} record(s).";
            pageText = "";
        }

        return new BrowsePageResult
        {
            Documents = docs,
            Page = page,
            PageSize = pageSize,
            CollectionTotal = total,
            Filtered = filtered,
            HasPrevious = page > 0,
            HasNext = hasNext,
            FilterJson = filterJson,
            Status = status,
            PageText = pageText,
            Explain = FormatExplain(plan, docs.Count)
        };
    }

    public async Task<List<NuvexaDocument>> FindAsync(
        string collection,
        string filterJson,
        int limit = 200,
        int skip = 0,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var filter = string.IsNullOrWhiteSpace(filterJson) ? "{}" : filterJson;
        var find = _db!.GetCollection(collection).Find(filter);
        if (skip > 0)
        {
            find = find.Skip(skip);
        }

        if (limit > 0)
        {
            find = find.Limit(limit);
        }

        var docs = await find.ToListAsync(cancellationToken).ConfigureAwait(false);
        MaterializeCache(collection, docs);
        return docs;
    }

    public long CollectionCount(string collection)
    {
        EnsureOpen();
        return _db!.GetCollection(collection).Count;
    }

    public async Task<NuvexaExplainPlan> ExplainAsync(string collection, string filterJson, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        return await _db!.GetCollection(collection).Find(filterJson).ExplainAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<NuvexaExplainPlan> ExplainQueryAsync(string text, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        if (NuvexaAggregate.TryParse(text, out var aggregateCollection, out _))
        {
            return new NuvexaExplainPlan
            {
                Collection = aggregateCollection,
                Strategy = "AGGREGATE"
            };
        }

        var query = NuvexaQuery.Parse(text);
        var find = _db!.GetCollection(query.Collection).Find(query.Filter);
        if (query.Skip > 0)
        {
            find = find.Skip(query.Skip);
        }

        if (query.Limit > 0)
        {
            find = find.Limit(query.Limit);
        }

        return await find.ExplainAsync(cancellationToken).ConfigureAwait(false);
    }

    public static string FormatExplain(NuvexaExplainPlan plan, int? returned = null)
    {
        var count = returned ?? plan.Returned;
        if (plan.Strategy == "AGGREGATE")
        {
            return $"AGGREGATE  collection={plan.Collection}  returned={count}";
        }

        var index = string.IsNullOrEmpty(plan.IndexName) ? "none" : plan.IndexName;
        return $"{plan.Strategy}  examined={plan.Examined}  returned={count}  index={index}";
    }

    public static string NormalizeBrowseFilter(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "{}";
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith('{'))
        {
            _ = NuvexaFilter.Parse(trimmed);
            return trimmed;
        }

        var colon = trimmed.IndexOf(':');
        var equals = trimmed.IndexOf('=');
        var at = colon >= 0 && (equals < 0 || colon < equals) ? colon : equals;
        if (at <= 0)
        {
            throw new NuvexaException("Filter must be JSON, like { status: \"paid\" }, or field: value.");
        }

        var field = trimmed[..at].Trim();
        var value = trimmed[(at + 1)..].Trim();
        if (field.Length == 0 || !field.All(c => char.IsLetterOrDigit(c) || c is '_' or '.'))
        {
            throw new NuvexaException("Enter a field name and value, like status: paid.");
        }

        var json = "{ " + field + ": " + WrapFilterValue(value) + " }";
        _ = NuvexaFilter.Parse(json);
        return json;
    }

    public async Task CreateIndexAsync(
        string collection,
        string fieldPath,
        string? name = null,
        bool unique = false,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        fieldPath = fieldPath.Trim();
        if (fieldPath.Length == 0 || fieldPath == "_id")
        {
            throw new NuvexaException("Choose a column other than _id. The primary key is already indexed.");
        }

        var indexName = string.IsNullOrWhiteSpace(name) ? fieldPath : name.Trim();
        if (indexName == "_id_")
        {
            throw new NuvexaException("The _id_ index cannot be replaced.");
        }

        await _db!.GetCollection(collection)
            .EnsureIndexAsync(fieldPath, indexName, unique, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DropIndexAsync(string collection, string name, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        name = name.Trim();
        if (name.Length == 0 || name == "_id_")
        {
            throw new NuvexaException("The _id_ index cannot be dropped.");
        }

        await _db!.GetCollection(collection).DropIndexAsync(name, cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<string> InferFields(IEnumerable<NuvexaDocument> documents)
    {
        var fields = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var doc in documents)
        {
            Collect(doc.AsElement(), "", fields);
        }

        return fields.ToList();
    }

    public void EnsureCollection(string name)
    {
        EnsureOpen();
        _ = _db!.GetCollection(name);
    }

    public async Task<string> InsertDocumentAsync(string collection, string json, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        return await _db!.GetCollection(collection).InsertAsync(NuvexaDocument.Parse(json), cancellationToken).ConfigureAwait(false);
    }

    public async Task ReplaceDocumentAsync(string collection, string json, string? keepId = null, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var doc = NuvexaDocument.Parse(json);
        if (!string.IsNullOrWhiteSpace(keepId))
        {
            doc.Id = keepId;
        }

        await _db!.GetCollection(collection).ReplaceAsync(doc, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteDocumentAsync(string collection, string id, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        return await _db!.GetCollection(collection).DeleteByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task ImportJsonAsync(string collection, string jsonArray, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        using var parsed = JsonDocument.Parse(jsonArray);
        var docs = parsed.RootElement.EnumerateArray().Select(el => NuvexaDocument.Parse(el.GetRawText())).ToList();
        await _db!.GetCollection(collection).InsertManyAsync(docs, cancellationToken).ConfigureAwait(false);
    }

    public static string ExportJson(IEnumerable<NuvexaDocument> documents) =>
        "[" + string.Join(",", documents.Select(d => d.ToJson())) + "]";

    public async Task ChangeEncryptionKeyAsync(string currentKey, string nextKey, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await _db!.ChangeEncryptionKeyAsync(currentKey, nextKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NuvexaIndexInfo>> ListIndexesAsync(string collection, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        return await _db!.GetCollection(collection).ListIndexesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExplorerNode>> LoadTreeAsync(CancellationToken cancellationToken = default)
    {
        var nodes = new List<ExplorerNode>();
        foreach (var name in Collections())
        {
            var sample = await ListAsync(name, 50, cancellationToken).ConfigureAwait(false);
            var fields = MergeFields(sample, await GetDeclaredFieldsAsync(name, cancellationToken).ConfigureAwait(false))
                .Select(f => new ExplorerNode(f, "field", []))
                .ToList();
            var indexes = (await ListIndexesAsync(name, cancellationToken).ConfigureAwait(false))
                .Select(i => new ExplorerNode(
                    i.Name,
                    "index",
                    [],
                    i.Unique ? $"{i.FieldPath}, unique" : i.FieldPath))
                .ToList();
            nodes.Add(new ExplorerNode(name, "collection",
            [
                new ExplorerNode("Columns", "group", fields),
                new ExplorerNode("Indexes", "group", indexes)
            ], CollectionCount(name).ToString()));
        }

        return nodes;
    }

    public IReadOnlyList<ExplorerNode> BuildTree(IReadOnlyDictionary<string, IReadOnlyList<NuvexaIndexInfo>> indexes)
    {
        var nodes = new List<ExplorerNode>();
        foreach (var name in Collections())
        {
            var kids = new List<ExplorerNode>();
            if (indexes.TryGetValue(name, out var list))
            {
                kids.AddRange(list.Select(i => new ExplorerNode(i.Name, "index", [])));
            }

            nodes.Add(new ExplorerNode(name, "collection", kids));
        }

        return nodes;
    }

    public IReadOnlyList<string> MergeFields(IEnumerable<NuvexaDocument> documents, IEnumerable<string>? declaredFields = null)
    {
        var fields = new SortedSet<string>(InferFields(documents), StringComparer.Ordinal);
        if (declaredFields is not null)
        {
            foreach (var field in declaredFields)
            {
                if (!string.IsNullOrWhiteSpace(field) && field != "_id")
                {
                    fields.Add(field);
                }
            }
        }

        return fields.ToList();
    }

    public IReadOnlyList<DocumentRow> ToGrid(IEnumerable<NuvexaDocument> documents, IEnumerable<string>? declaredFields = null)
    {
        var docs = documents.ToList();
        var fields = MergeFields(docs, declaredFields);
        return docs.Select(d =>
        {
            var cells = new Dictionary<string, string>(StringComparer.Ordinal);
            var el = d.AsElement();
            foreach (var field in fields)
            {
                cells[field] = DocumentPath.TryGet(el, field, out var value) ? value.ToString() ?? "" : "";
            }

            return new DocumentRow(d.Id, d.ToJson(), cells);
        }).ToList();
    }

    public static string DocumentJsonFromCells(
        string? id,
        IEnumerable<KeyValuePair<string, string>> cells,
        IEnumerable<TableColumnDefinition>? columns = null)
    {
        var typeByName = (columns ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .ToDictionary(c => c.Name, c => c.Type, StringComparer.Ordinal);
        var obj = new JsonObject();
        if (!string.IsNullOrWhiteSpace(id))
        {
            obj["_id"] = id;
        }

        foreach (var (name, value) in cells)
        {
            if (string.IsNullOrWhiteSpace(name) || name == "_id")
            {
                continue;
            }

            var type = typeByName.TryGetValue(name, out var declared) ? declared : "TEXT";
            obj[name] = TableColumnTypes.ParseCell(type, value);
        }

        return obj.ToJsonString();
    }

    public async Task CreateCollectionAsync(TableDefinition definition, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var name = definition.Name.Trim();
        if (name.Length == 0 || name == "_id" || name == SchemaCollectionName || name.StartsWith("__", StringComparison.Ordinal))
        {
            throw new NuvexaException("Enter a table name that is not reserved.");
        }

        if (Collections().Contains(name, StringComparer.Ordinal))
        {
            throw new NuvexaException($"Collection '{name}' already exists.");
        }

        EnsureCollection(name);
        var columns = NormalizeColumns(definition.Columns);
        if (columns.Count > 0)
        {
            await WriteSchemaAsync(name, columns, cancellationToken).ConfigureAwait(false);
        }

        foreach (var column in columns.Where(c => c.Unique))
        {
            await _db!.GetCollection(name)
                .EnsureIndexAsync(column.Name, name: column.Name, unique: true, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task DropCollectionAsync(string name, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        name = name.Trim();
        if (name.Length == 0 || name == SchemaCollectionName)
        {
            throw new NuvexaException("That collection cannot be deleted.");
        }

        var schema = _db!.GetCollection(SchemaCollectionName);
        if (await schema.FindByIdAsync(name, cancellationToken).ConfigureAwait(false) is not null)
        {
            await schema.DeleteByIdAsync(name, cancellationToken).ConfigureAwait(false);
        }

        await _db.DropCollectionAsync(name, cancellationToken).ConfigureAwait(false);
    }

    public async Task RenameCollectionAsync(string from, string to, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        from = from.Trim();
        to = to.Trim();
        if (from.Length == 0 || from == SchemaCollectionName)
        {
            throw new NuvexaException("That collection cannot be renamed.");
        }

        if (to.Length == 0 || to == "_id" || to == SchemaCollectionName || to.StartsWith("__", StringComparison.Ordinal))
        {
            throw new NuvexaException("Enter a table name that is not reserved.");
        }

        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return;
        }

        if (Collections().Contains(to, StringComparer.Ordinal))
        {
            throw new NuvexaException($"Collection '{to}' already exists.");
        }

        var columns = await GetDeclaredColumnsAsync(from, cancellationToken).ConfigureAwait(false);
        await _db!.RenameCollectionAsync(from, to, cancellationToken).ConfigureAwait(false);
        if (columns.Count > 0)
        {
            await WriteSchemaAsync(to, columns, cancellationToken).ConfigureAwait(false);
        }

        var schema = _db.GetCollection(SchemaCollectionName);
        if (await schema.FindByIdAsync(from, cancellationToken).ConfigureAwait(false) is not null)
        {
            await schema.DeleteByIdAsync(from, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<string>> GetDeclaredFieldsAsync(string collection, CancellationToken cancellationToken = default) =>
        (await GetDeclaredColumnsAsync(collection, cancellationToken).ConfigureAwait(false)).Select(c => c.Name).ToList();

    public async Task<IReadOnlyList<TableColumnDefinition>> GetDeclaredColumnsAsync(string collection, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var schema = await _db!.GetCollection(SchemaCollectionName).FindByIdAsync(collection, cancellationToken).ConfigureAwait(false);
        if (schema?.Root["fields"] is not JsonArray array)
        {
            return [];
        }

        var types = schema.Root["types"] as JsonObject;
        var defaults = schema.Root["defaults"] as JsonObject;
        var unique = new HashSet<string>(StringComparer.Ordinal);
        if (schema.Root["unique"] is JsonArray uniqueArray)
        {
            foreach (var node in uniqueArray)
            {
                var name = node?.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    unique.Add(name);
                }
            }
        }

        return array
            .Select(n => n?.ToString() ?? "")
            .Where(s => s.Length > 0)
            .Select(name => new TableColumnDefinition(
                name,
                TableColumnTypes.Normalize(types?[name]?.ToString()),
                defaults?[name]?.ToString() ?? "",
                unique.Contains(name)))
            .ToList();
    }

    public Task AddColumnAsync(string collection, string field, string? defaultValue, CancellationToken cancellationToken = default) =>
        AddColumnAsync(collection, new TableColumnDefinition(field, "TEXT", defaultValue ?? "", Unique: false), cancellationToken);

    public async Task AddColumnAsync(string collection, TableColumnDefinition column, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var next = RequireColumn(column);
        var columns = (await GetDeclaredColumnsAsync(collection, cancellationToken).ConfigureAwait(false)).ToList();
        if (columns.Any(c => string.Equals(c.Name, next.Name, StringComparison.Ordinal)))
        {
            throw new NuvexaException($"Column '{next.Name}' already exists.");
        }

        columns.Add(next);
        await WriteSchemaAsync(collection, columns, cancellationToken).ConfigureAwait(false);
        if (next.Unique)
        {
            await _db!.GetCollection(collection)
                .EnsureIndexAsync(next.Name, name: next.Name, unique: true, cancellationToken)
                .ConfigureAwait(false);
        }

        var set = new JsonObject { [next.Name] = TableColumnTypes.DefaultNode(next.Type, next.Default) };
        var update = new JsonObject { ["$set"] = set };
        await _db!.GetCollection(collection)
            .UpdateAsync(NuvexaFilter.Parse("{}"), update.ToJsonString(), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DropColumnAsync(string collection, string field, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        field = field.Trim();
        if (field.Length == 0 || field == "_id")
        {
            throw new NuvexaException("The _id column cannot be deleted.");
        }

        var columns = (await GetDeclaredColumnsAsync(collection, cancellationToken).ConfigureAwait(false))
            .Where(c => !string.Equals(c.Name, field, StringComparison.Ordinal))
            .ToList();
        await WriteSchemaAsync(collection, columns, cancellationToken).ConfigureAwait(false);
        await DropIndexesForFieldAsync(collection, field, cancellationToken).ConfigureAwait(false);

        var update = new JsonObject { ["$unset"] = new JsonObject { [field] = "" } };
        await _db!.GetCollection(collection)
            .UpdateAsync(NuvexaFilter.Parse("{}"), update.ToJsonString(), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpdateColumnAsync(
        string collection,
        string oldName,
        TableColumnDefinition column,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        oldName = oldName.Trim();
        if (oldName.Length == 0 || oldName == "_id")
        {
            throw new NuvexaException("The _id column cannot be edited.");
        }

        var next = RequireColumn(column);
        var columns = (await GetDeclaredColumnsAsync(collection, cancellationToken).ConfigureAwait(false)).ToList();
        if (columns.Any(c =>
                !string.Equals(c.Name, oldName, StringComparison.Ordinal) &&
                string.Equals(c.Name, next.Name, StringComparison.Ordinal)))
        {
            throw new NuvexaException($"Column '{next.Name}' already exists.");
        }

        var index = columns.FindIndex(c => string.Equals(c.Name, oldName, StringComparison.Ordinal));
        if (index >= 0)
        {
            next = next with { Unique = columns[index].Unique };
            columns[index] = next;
        }
        else
        {
            columns.Add(next);
        }

        if (!string.Equals(oldName, next.Name, StringComparison.Ordinal))
        {
            var docs = await ListAsync(collection, 100_000, cancellationToken).ConfigureAwait(false);
            foreach (var doc in docs)
            {
                if (doc.Root is not JsonObject obj || obj[oldName] is not { } value)
                {
                    continue;
                }

                obj[next.Name] = value.DeepClone();
                obj.Remove(oldName);
                await ReplaceDocumentAsync(collection, doc.ToJson(), doc.Id, cancellationToken).ConfigureAwait(false);
            }

            await DropIndexesForFieldAsync(collection, oldName, cancellationToken).ConfigureAwait(false);
            if (next.Unique)
            {
                await _db!.GetCollection(collection)
                    .EnsureIndexAsync(next.Name, name: next.Name, unique: true, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await WriteSchemaAsync(collection, columns, cancellationToken).ConfigureAwait(false);
    }

    public string NewRecordJson(IEnumerable<TableColumnDefinition> columns)
    {
        var obj = new JsonObject();
        foreach (var column in columns)
        {
            if (column.Name is not "_id" && !string.IsNullOrWhiteSpace(column.Name))
            {
                obj[column.Name] = TableColumnTypes.DefaultNode(column.Type, column.Default);
            }
        }

        return obj.Count == 0 ? "{ }" : obj.ToJsonString();
    }

    public string NewRecordJson(IEnumerable<string> declaredFields, string? defaultValue = "")
    {
        var columns = declaredFields
            .Where(f => f is not "_id" && !string.IsNullOrWhiteSpace(f))
            .Select(f => new TableColumnDefinition(f, "TEXT", defaultValue ?? "", Unique: false));
        return NewRecordJson(columns);
    }

    public async Task CompactAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await _db!.CompactAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        return _db!.BackupAsync(destinationPath, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync().ConfigureAwait(false);
            _db = null;
        }

        _cache?.Dispose();
        _cache = null;
        if (_cachePath is not null && File.Exists(_cachePath))
        {
            try { File.Delete(_cachePath); } catch { /* temp */ }
        }

        Path = null;
    }

    private static TableColumnDefinition RequireColumn(TableColumnDefinition column)
    {
        var normalized = NormalizeColumns([column]);
        if (normalized.Count == 0)
        {
            throw new NuvexaException("Enter a column name other than _id.");
        }

        return normalized[0];
    }

    private async Task DropIndexesForFieldAsync(string collection, string field, CancellationToken cancellationToken)
    {
        foreach (var index in await ListIndexesAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            if (index.Name == "_id_" || !string.Equals(index.FieldPath, field, StringComparison.Ordinal))
            {
                continue;
            }

            await _db!.GetCollection(collection).DropIndexAsync(index.Name, cancellationToken).ConfigureAwait(false);
        }
    }

    private static List<TableColumnDefinition> NormalizeColumns(IEnumerable<TableColumnDefinition> columns)
    {
        var result = new List<TableColumnDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            var name = column.Name.Trim();
            if (name.Length == 0 || name == "_id" || name.StartsWith('$') || !seen.Add(name))
            {
                continue;
            }

            result.Add(column with { Name = name, Type = TableColumnTypes.Normalize(column.Type) });
        }

        return result;
    }

    private async Task WriteSchemaAsync(string collection, IReadOnlyList<TableColumnDefinition> columns, CancellationToken cancellationToken)
    {
        var types = new JsonObject();
        var defaults = new JsonObject();
        foreach (var column in columns)
        {
            types[column.Name] = column.Type;
            defaults[column.Name] = column.Default;
        }

        var schemaJson = new JsonObject
        {
            ["_id"] = collection,
            ["fields"] = new JsonArray(columns.Select(c => JsonValue.Create(c.Name)).ToArray()),
            ["types"] = types,
            ["defaults"] = defaults,
            ["unique"] = new JsonArray(columns.Where(c => c.Unique).Select(c => JsonValue.Create(c.Name)).ToArray())
        };
        var schemaDoc = NuvexaDocument.Parse(schemaJson.ToJsonString());
        var schemaCol = _db!.GetCollection(SchemaCollectionName);
        if (await schemaCol.FindByIdAsync(collection, cancellationToken).ConfigureAwait(false) is null)
        {
            await schemaCol.InsertAsync(schemaDoc, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await schemaCol.ReplaceAsync(schemaDoc, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string WrapFilterValue(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        if (value.StartsWith('{') || value.StartsWith('[') || value.StartsWith('"'))
        {
            return value;
        }

        if (value is "true" or "false" or "null")
        {
            return value;
        }

        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            return value;
        }

        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private void EnsureOpen()
    {
        if (_db is null)
        {
            throw new NuvexaException("No database is open.");
        }
    }

    private void OpenCache()
    {
        _cachePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nuvexa-" + Guid.NewGuid().ToString("N") + ".cache.db");
        _cache = new SqliteConnection($"Data Source={_cachePath}");
        _cache.Open();
        using var cmd = _cache.CreateCommand();
        cmd.CommandText = "CREATE TABLE grid (collection TEXT, id TEXT, json TEXT);";
        cmd.ExecuteNonQuery();
    }

    private void MaterializeCache(string collection, IReadOnlyList<NuvexaDocument> documents)
    {
        if (_cache is null)
        {
            return;
        }

        using var tx = _cache.BeginTransaction();
        using (var clear = _cache.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM grid WHERE collection = $c;";
            clear.Parameters.AddWithValue("$c", collection);
            clear.ExecuteNonQuery();
        }

        foreach (var doc in documents)
        {
            using var insert = _cache.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO grid(collection, id, json) VALUES ($c, $id, $j);";
            insert.Parameters.AddWithValue("$c", collection);
            insert.Parameters.AddWithValue("$id", doc.Id);
            insert.Parameters.AddWithValue("$j", doc.ToJson());
            insert.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static void Collect(JsonElement el, string prefix, SortedSet<string> fields)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var prop in el.EnumerateObject())
        {
            var path = string.IsNullOrEmpty(prefix) ? prop.Name : prefix + "." + prop.Name;
            fields.Add(path);
            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                Collect(prop.Value, path, fields);
            }
        }
    }
}
