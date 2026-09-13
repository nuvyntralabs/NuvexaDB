using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nuventra.NuvexaDB.Native;

/// <summary>
/// Handle + UTF-8 JSON surface shared by C exports, C# interop tests, and language SDKs.
/// </summary>
public static class NuvexaAbi
{
    public const int Ok = 0;
    public const int Error = 1;
    public const int Encryption = 2;
    public const int Integrity = 3;
    public const int NotFound = 4;
    public const int AbiVersion = 2;

    private static readonly ConcurrentDictionary<nint, NuvexaDatabase> Databases = new();
    private static readonly ConcurrentDictionary<nint, NuvexaTransaction> Transactions = new();
    private static readonly object Gate = new();
    private static nint _next = 1;

    [ThreadStatic]
    private static string? _error;

    public static string? LastError => _error;

    public static int Create(string? path, string? key, out nint handle) =>
        OpenOrCreate(path, key, create: true, out handle);

    public static int Open(string? path, string? key, out nint handle) =>
        OpenOrCreate(path, key, create: false, out handle);

    public static int Close(nint handle)
    {
        if (Transactions.TryRemove(handle, out var tx))
        {
            try
            {
                Run(() => tx.RollbackAsync());
            }
            catch
            {
                // Still close the file.
            }
        }

        if (!Databases.TryRemove(handle, out var db))
        {
            return Fail(Error, "Unknown database handle.");
        }

        try
        {
            db.Dispose();
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int IsEncrypted(string? path, out int encrypted)
    {
        encrypted = 0;
        if (string.IsNullOrWhiteSpace(path))
        {
            return Fail(Error, "A database path is required.");
        }

        try
        {
            encrypted = NuvexaDatabase.IsEncrypted(path) ? 1 : 0;
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int Insert(nint handle, string? collection, string? json, out string? id)
    {
        id = null;
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (string.IsNullOrWhiteSpace(collection) || string.IsNullOrWhiteSpace(json))
        {
            return Fail(Error, "Collection and document JSON are required.");
        }

        try
        {
            id = Run(() => db.GetCollection(collection).InsertAsync(NuvexaDocument.Parse(json)));
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int Replace(nint handle, string? collection, string? json)
    {
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (string.IsNullOrWhiteSpace(collection) || string.IsNullOrWhiteSpace(json))
        {
            return Fail(Error, "Collection and document JSON are required.");
        }

        try
        {
            Run(() => db.GetCollection(collection).ReplaceAsync(NuvexaDocument.Parse(json)));
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int DeleteById(nint handle, string? collection, string? id, out int deleted)
    {
        deleted = 0;
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (string.IsNullOrWhiteSpace(collection) || string.IsNullOrWhiteSpace(id))
        {
            return Fail(Error, "Collection and document id are required.");
        }

        try
        {
            deleted = Run(() => db.GetCollection(collection).DeleteByIdAsync(id)) ? 1 : 0;
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int FindById(nint handle, string? collection, string? id, out string? json)
    {
        json = null;
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (string.IsNullOrWhiteSpace(collection) || string.IsNullOrWhiteSpace(id))
        {
            return Fail(Error, "Collection and document id are required.");
        }

        try
        {
            var doc = Run(() => db.GetCollection(collection).FindByIdAsync(id));
            if (doc is null)
            {
                return Fail(NotFound, $"No document with _id '{id}'.");
            }

            json = doc.ToJson();
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int Execute(nint handle, string? nql, out string? json)
    {
        json = null;
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (string.IsNullOrWhiteSpace(nql))
        {
            return Fail(Error, "NQL text is required.");
        }

        try
        {
            var result = Run(() => db.ExecuteAsync(nql));
            var array = new JsonArray();
            foreach (var document in result.Documents)
            {
                array.Add(document.Root.DeepClone());
            }

            json = array.ToJsonString();
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int EnsureIndex(nint handle, string? collection, string? fieldsJson)
    {
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (string.IsNullOrWhiteSpace(collection) || string.IsNullOrWhiteSpace(fieldsJson))
        {
            return Fail(Error, "Collection and index field JSON are required.");
        }

        try
        {
            var col = db.GetCollection(collection);
            var node = JsonNode.Parse(fieldsJson)
                       ?? throw new NuvexaException("Index field JSON is empty.");
            if (node is JsonArray array)
            {
                var fields = array.Select(item => item?.ToString() ?? "").Where(s => s.Length > 0).ToList();
                if (fields.Count == 0)
                {
                    return Fail(Error, "Index field list is empty.");
                }

                Run(() => fields.Count == 1 ? col.EnsureIndexAsync(fields[0]) : col.EnsureIndexAsync(fields));
            }
            else
            {
                var field = node.ToString();
                if (string.IsNullOrWhiteSpace(field))
                {
                    return Fail(Error, "Index field is empty.");
                }

                Run(() => col.EnsureIndexAsync(field));
            }

            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int InsertMany(nint handle, string? collection, string? jsonArray, out string? ids)
    {
        ids = null;
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (string.IsNullOrWhiteSpace(collection) || string.IsNullOrWhiteSpace(jsonArray))
        {
            return Fail(Error, "Collection and a JSON array of documents are required.");
        }

        try
        {
            var node = JsonNode.Parse(jsonArray) as JsonArray
                       ?? throw new NuvexaException("insert_many requires a JSON array.");
            var col = db.GetCollection(collection);
            var idList = new JsonArray();
            foreach (var item in node)
            {
                if (item is null)
                {
                    return Fail(Error, "insert_many contains a null document.");
                }

                var id = Run(() => col.InsertAsync(NuvexaDocument.Parse(item.ToJsonString())));
                idList.Add((JsonNode?)id);
            }

            ids = idList.ToJsonString();
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int ListCollections(nint handle, out string? json)
    {
        json = null;
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        try
        {
            var names = new JsonArray();
            foreach (var name in db.GetCollectionNames())
            {
                names.Add((JsonNode?)name);
            }

            json = names.ToJsonString();
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int DropCollection(nint handle, string? collection)
    {
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (string.IsNullOrWhiteSpace(collection))
        {
            return Fail(Error, "Collection name is required.");
        }

        try
        {
            Run(() => db.DropCollectionAsync(collection));
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int RenameCollection(nint handle, string? from, string? to)
    {
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return Fail(Error, "Source and destination collection names are required.");
        }

        try
        {
            Run(() => db.RenameCollectionAsync(from, to));
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int ListIndexes(nint handle, string? collection, out string? json)
    {
        json = null;
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (string.IsNullOrWhiteSpace(collection))
        {
            return Fail(Error, "Collection name is required.");
        }

        try
        {
            var indexes = Run(() => db.GetCollection(collection).ListIndexesAsync());
            var array = new JsonArray();
            foreach (var index in indexes)
            {
                var fields = new JsonArray();
                foreach (var field in index.FieldPaths)
                {
                    fields.Add((JsonNode?)field);
                }

                array.Add((JsonNode?)new JsonObject
                {
                    ["name"] = index.Name,
                    ["fields"] = fields,
                    ["unique"] = index.Unique
                });
            }

            json = array.ToJsonString();
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int DropIndex(nint handle, string? collection, string? name)
    {
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (string.IsNullOrWhiteSpace(collection) || string.IsNullOrWhiteSpace(name))
        {
            return Fail(Error, "Collection and index name are required.");
        }

        try
        {
            Run(() => db.GetCollection(collection).DropIndexAsync(name));
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int Count(nint handle, string? collection, string? filterJson, out long count)
    {
        count = 0;
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (string.IsNullOrWhiteSpace(collection))
        {
            return Fail(Error, "Collection name is required.");
        }

        try
        {
            var filter = string.IsNullOrWhiteSpace(filterJson) ? "{}" : filterJson;
            var docs = Run(() => db.GetCollection(collection).Find(filter).ToListAsync());
            count = docs.Count;
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int Checkpoint(nint handle)
    {
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        try
        {
            Run(() => db.CheckpointAsync());
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int Backup(nint handle, string? destPath)
    {
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (string.IsNullOrWhiteSpace(destPath))
        {
            return Fail(Error, "A backup destination path is required.");
        }

        try
        {
            Run(() => db.BackupAsync(destPath));
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int Compact(nint handle)
    {
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        try
        {
            Run(() => db.CompactAsync());
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int Restore(string? backupPath, string? destPath, int overwrite)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || string.IsNullOrWhiteSpace(destPath))
        {
            return Fail(Error, "Backup and destination paths are required.");
        }

        try
        {
            Run(() => NuvexaDatabase.RestoreAsync(backupPath, destPath, overwrite != 0));
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int Stats(nint handle, out string? json)
    {
        json = null;
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        try
        {
            var stats = db.GetStats();
            json = new JsonObject
            {
                ["path"] = stats.Path,
                ["fileBytes"] = stats.FileBytes,
                ["walBytes"] = stats.WalBytes,
                ["pageCount"] = stats.PageCount,
                ["documentCount"] = stats.DocumentCount,
                ["collectionCount"] = stats.CollectionCount,
                ["cachedPages"] = stats.CachedPages,
                ["cacheSizeMb"] = stats.CacheSizeMb,
                ["encrypted"] = stats.Encrypted,
                ["committedLsn"] = stats.CommittedLsn,
                ["compactNeeded"] = stats.CompactNeeded
            }.ToJsonString();
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int ChangeEncryptionKey(nint handle, string? currentKey, string? nextKey)
    {
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (string.IsNullOrEmpty(currentKey) || string.IsNullOrEmpty(nextKey))
        {
            return Fail(Error, "Current and next encryption keys are required.");
        }

        try
        {
            Run(() => db.ChangeEncryptionKeyAsync(currentKey, nextKey));
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int BeginTransaction(nint handle)
    {
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (Transactions.ContainsKey(handle))
        {
            return Fail(Error, "A transaction is already open on this handle.");
        }

        try
        {
            Transactions[handle] = Run(() => db.BeginTransactionAsync());
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int Commit(nint handle)
    {
        if (!Transactions.TryGetValue(handle, out var tx))
        {
            return Fail(Error, "No transaction is open on this handle.");
        }

        try
        {
            Run(() => tx.CommitAsync());
            Transactions.TryRemove(handle, out _);
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int Rollback(nint handle)
    {
        if (!Transactions.TryGetValue(handle, out var tx))
        {
            return Fail(Error, "No transaction is open on this handle.");
        }

        try
        {
            Run(() => tx.RollbackAsync());
            Transactions.TryRemove(handle, out _);
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int FsUpload(nint handle, string? fileName, string? sourcePath, int chunkSize, out string? id)
    {
        id = null;
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(sourcePath))
        {
            return Fail(Error, "File name and source path are required.");
        }

        try
        {
            using var stream = File.OpenRead(sourcePath);
            var size = chunkSize > 0 ? chunkSize : NuvexaGridFs.DefaultChunkSize;
            id = Run(() => db.Files.UploadAsync(fileName, stream, size));
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int FsDownload(nint handle, string? fileId, string? destPath, out int found)
    {
        found = 0;
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (string.IsNullOrWhiteSpace(fileId) || string.IsNullOrWhiteSpace(destPath))
        {
            return Fail(Error, "File id and destination path are required.");
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destPath)) ?? ".");
            var ok = false;
            using (var stream = File.Create(destPath))
            {
                ok = Run(() => db.Files.DownloadAsync(fileId, stream));
            }

            found = ok ? 1 : 0;
            if (!ok && File.Exists(destPath))
            {
                File.Delete(destPath);
            }

            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static int FsMetadata(nint handle, string? fileId, out string? json)
    {
        json = null;
        if (!TryGet(handle, out var db, out var status))
        {
            return status;
        }

        if (string.IsNullOrWhiteSpace(fileId))
        {
            return Fail(Error, "File id is required.");
        }

        try
        {
            var doc = Run(() => db.Files.GetMetadataAsync(fileId));
            if (doc is null)
            {
                return Fail(NotFound, $"No GridFS file with _id '{fileId}'.");
            }

            json = doc.ToJson();
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static void ClearError() => _error = null;

    private static int OpenOrCreate(string? path, string? key, bool create, out nint handle)
    {
        handle = 0;
        if (string.IsNullOrWhiteSpace(path))
        {
            return Fail(Error, "A database path is required.");
        }

        try
        {
            var db = create
                ? NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = EmptyToNull(key) })
                : NuvexaDatabase.Open(path, new NuvexaOpenOptions { EncryptionKey = EmptyToNull(key) });
            handle = Register(db);
            return ClearOk();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    private static nint Register(NuvexaDatabase db)
    {
        lock (Gate)
        {
            var handle = _next++;
            Databases[handle] = db;
            return handle;
        }
    }

    private static bool TryGet(nint handle, out NuvexaDatabase db, out int status)
    {
        if (Databases.TryGetValue(handle, out db!))
        {
            status = Ok;
            return true;
        }

        db = null!;
        status = Fail(Error, "Unknown database handle.");
        return false;
    }

    private static string? EmptyToNull(string? key) => string.IsNullOrEmpty(key) ? null : key;

    private static T Run<T>(Func<Task<T>> work) => work().ConfigureAwait(false).GetAwaiter().GetResult();

    private static void Run(Func<Task> work) => work().ConfigureAwait(false).GetAwaiter().GetResult();

    private static int ClearOk()
    {
        _error = null;
        return Ok;
    }

    private static int Fail(int status, string message)
    {
        _error = message;
        return status;
    }

    private static int Fail(Exception ex)
    {
        _error = ex.Message;
        return ex switch
        {
            NuvexaEncryptionException => Encryption,
            NuvexaIntegrityException => Integrity,
            _ => Error
        };
    }
}
