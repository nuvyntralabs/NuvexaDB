using System.Text.Json;
using Microsoft.Data.Sqlite;
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
        _db = await NuvexaDatabase.CreateAsync(path, new NuvexaCreateOptions { EncryptionKey = encryptionKey }, cancellationToken)
            .ConfigureAwait(false);
        Path = path;
        Encrypted = _db.Encrypted;
        _encryptionKey = encryptionKey;
        OpenCache();
    }

    public IReadOnlyList<string> Collections()
    {
        EnsureOpen();
        return _db!.GetCollectionNames();
    }

    public NuvexaStats Stats()
    {
        EnsureOpen();
        return _db!.GetStats();
    }

    public async Task<List<NuvexaDocument>> QueryAsync(string text, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var result = await _db!.ExecuteAsync(text, cancellationToken).ConfigureAwait(false);
        MaterializeCache(result.Collection, result.Documents);
        return result.Documents;
    }

    public async Task<List<NuvexaDocument>> ListAsync(string collection, int limit = 200, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var docs = await _db!.GetCollection(collection).Find().Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        MaterializeCache(collection, docs);
        return docs;
    }

    public async Task<NuvexaExplainPlan> ExplainAsync(string collection, string filterJson, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        return await _db!.GetCollection(collection).Find(filterJson).ExplainAsync(cancellationToken).ConfigureAwait(false);
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
        var indexes = new Dictionary<string, IReadOnlyList<NuvexaIndexInfo>>(StringComparer.Ordinal);
        foreach (var name in Collections())
        {
            indexes[name] = await ListIndexesAsync(name, cancellationToken).ConfigureAwait(false);
        }

        return BuildTree(indexes);
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

    public IReadOnlyList<DocumentRow> ToGrid(IEnumerable<NuvexaDocument> documents)
    {
        var docs = documents.ToList();
        var fields = InferFields(docs);
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

    public async Task CompactAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var path = Path ?? throw new NuvexaException("No database is open.");
        var key = _encryptionKey;
        await _db!.CompactAsync(cancellationToken).ConfigureAwait(false);
        _db = null;
        await OpenAsync(path, key, cancellationToken).ConfigureAwait(false);
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
