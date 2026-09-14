using System.Security.Cryptography;
using Nuventra.NuvexaDB.Encryption;
using Nuventra.NuvexaDB.Engine;
using Nuventra.NuvexaDB.Query;

namespace Nuventra.NuvexaDB;

/// <summary>
/// Embedded NuvexaDB handle for a single <c>.nvx</c> file.
/// One process may open a path at a time (exclusive lock). Concurrent
/// <c>Find</c> / reads are allowed; writes stay exclusive.
/// </summary>
public sealed class NuvexaDatabase : IDisposable, IAsyncDisposable
{
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly object _collectionsLock = new();
    private int _readers;
    private readonly int _cacheSizeMb;
    private readonly List<CollectionMeta> _collections;
    private string? _encryptionKey;
    private bool _disposed;
    private NuvexaTransaction? _currentTx;

    internal PageStore Store { get; }

    private NuvexaDatabase(PageStore store, int cacheSizeMb, string? encryptionKey)
    {
        Store = store;
        _cacheSizeMb = cacheSizeMb;
        _encryptionKey = encryptionKey;
        _collections = Catalog.Load(store);
        foreach (var meta in _collections)
        {
            if (meta.IndexRoot == 0)
            {
                var tree = new BPlusTree(store, 0);
                meta.IndexRoot = tree.RootPageId;
            }
        }
    }

    public string Path => Store.Path;
    public bool Encrypted => Store.Encrypted;
    public NuvexaGridFs Files => new(this);

    /// <summary>
    /// Maximum documents loaded by <c>$lookup</c> (and the foreign collection size check).
    /// Default <see cref="NuvexaLimits.DefaultLookupMaxDocuments"/>. Set 0 to disable the cap.
    /// </summary>
    public int LookupMaxDocuments { get; set; } = NuvexaLimits.DefaultLookupMaxDocuments;

    public static bool IsEncrypted(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var header = new byte[Constants.PageSize];
            var read = stream.Read(header);
            if (read < 8)
            {
                throw new NuvexaException("File is too small to be a .nvx database.");
            }

            return Superblock.PeekEncrypted(header);
        }
        catch (IOException ex) when (File.Exists(path))
        {
            throw new NuvexaException($"Cannot read '{path}'. The database is already open or the file is locked.", ex);
        }
    }

    public static NuvexaDatabase Create(string path, NuvexaCreateOptions? options = null)
    {
        options ??= new NuvexaCreateOptions();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".");
        if (File.Exists(path))
        {
            throw new NuvexaException($"A file already exists at '{path}'.");
        }

        if (options.FormatVersion < Constants.MinFormatVersion || options.FormatVersion > Constants.FormatVersion)
        {
            throw new NuvexaException($"Unsupported .nvx format version {options.FormatVersion}.");
        }

        var super = new Superblock
        {
            Version = options.FormatVersion,
            FileId = RandomNumberGenerator.GetBytes(16),
            KdfMemoryKb = options.Argon2MemoryKb,
            KdfIterations = options.Argon2Iterations,
            KdfParallelism = options.Argon2Parallelism
        };
        byte[]? dek = null;
        if (!string.IsNullOrEmpty(options.EncryptionKey))
        {
            super.Flags |= SuperblockFlags.Encrypted;
            super.Salt = KeyDerivation.GenerateSalt();
            var kek = KeyDerivation.DeriveKek(options.EncryptionKey, super.Salt, super.KdfMemoryKb, super.KdfIterations, super.KdfParallelism);
            dek = KeyDerivation.GenerateDek();
            KeyDerivation.WrapKeys(super, kek, dek);
            CryptographicOperations.ZeroMemory(kek);
        }

        var store = OpenStore(path, super, dek, options.CacheSizeMb, readOnly: false, options.CheckpointThreshold, create: true);
        return new NuvexaDatabase(store, options.CacheSizeMb, options.EncryptionKey);
    }

    public static NuvexaDatabase Open(string path, NuvexaOpenOptions? options = null)
    {
        options ??= new NuvexaOpenOptions();
        if (!File.Exists(path))
        {
            throw new NuvexaException($"Database file '{path}' was not found.");
        }

        var header = ReadHeader(path);

        var super = Superblock.Read(header);
        byte[]? dek = null;
        if (super.Encrypted)
        {
            if (string.IsNullOrEmpty(options.EncryptionKey))
            {
                throw new NuvexaEncryptionException("The database is encrypted. Supply EncryptionKey to open it.");
            }

            var kek = KeyDerivation.DeriveKek(options.EncryptionKey, super.Salt, super.KdfMemoryKb, super.KdfIterations, super.KdfParallelism);
            KeyDerivation.VerifyKek(kek, super);
            dek = KeyDerivation.UnwrapDek(kek, super);
            Superblock.VerifyIntegrityMac(header, dek);
            CryptographicOperations.ZeroMemory(kek);
        }

        var store = OpenStore(path, super, dek, options.CacheSizeMb, options.ReadOnly, options.CheckpointThreshold, create: false);
        try
        {
            if (options.VerifyIntegrity && ShouldScanIntegrity(path, options.IntegrityScanMaxBytes))
            {
                store.VerifyIntegrity();
            }

            return new NuvexaDatabase(store, options.CacheSizeMb, options.EncryptionKey);
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }

    private static byte[] ReadHeader(string path)
    {
        try
        {
            var header = new byte[Constants.PageSize];
            using var stream = File.OpenRead(path);
            if (stream.Read(header) < Constants.PageSize)
            {
                throw new NuvexaException("The .nvx file is truncated.");
            }

            return header;
        }
        catch (IOException ex) when (File.Exists(path))
        {
            throw new NuvexaException($"Cannot open '{path}'. The database is already open or the file is locked.", ex);
        }
    }

    private static PageStore OpenStore(
        string path,
        Superblock super,
        byte[]? dek,
        int cacheSizeMb,
        bool readOnly,
        int checkpointThreshold,
        bool create)
    {
        try
        {
            return new PageStore(path, super, dek, cacheSizeMb, readOnly, checkpointThreshold, create);
        }
        catch (IOException ex) when (File.Exists(path) || create)
        {
            throw new NuvexaException($"Cannot open '{path}'. The database is already open or the file is locked.", ex);
        }
    }

    private static bool ShouldScanIntegrity(string path, long maxBytes) =>
        maxBytes <= 0 || new FileInfo(path).Length <= maxBytes;

    public static Task<NuvexaDatabase> CreateAsync(string path, NuvexaCreateOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Create(path, options), cancellationToken);

    public static Task<NuvexaDatabase> OpenAsync(string path, NuvexaOpenOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Open(path, options), cancellationToken);

    public IReadOnlyList<string> GetCollectionNames()
    {
        lock (_collectionsLock)
        {
            return _collections.Select(c => c.Name).ToList();
        }
    }

    public NuvexaCollection GetCollection(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > Constants.MaxCollectionName)
        {
            throw new NuvexaException("Collection name is too long.");
        }

        CollectionMeta meta;
        var created = false;
        lock (_collectionsLock)
        {
            var existing = _collections.Find(c => c.Name == name);
            if (existing is null)
            {
                var tree = new BPlusTree(Store, 0);
                meta = new CollectionMeta { Name = name, IndexRoot = tree.RootPageId };
                _collections.Add(meta);
                PersistCatalog();
                created = true;
            }
            else
            {
                meta = existing;
            }
        }

        if (created && _currentTx is null)
        {
            CommitDirty();
        }

        return new NuvexaCollection(this, meta);
    }

    public NuvexaCollection<T> GetCollection<T>(string? name = null) where T : class =>
        new(GetCollection(name ?? typeof(T).Name));

    public async Task DropCollectionAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_collections.All(c => c.Name != name))
        {
            return;
        }

        await GetCollection(name).DeleteAsync(NuvexaFilter.Parse("{}"), cancellationToken).ConfigureAwait(false);
        await WriteAsync(() =>
        {
            lock (_collectionsLock)
            {
                _collections.RemoveAll(c => c.Name == name);
            }

            PersistCatalog();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RenameCollectionAsync(string from, string to, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        if (from == to)
        {
            return;
        }

        if (to.Length > Constants.MaxCollectionName)
        {
            throw new NuvexaException("Collection name is too long.");
        }

        if (_collections.All(c => c.Name != from))
        {
            throw new NuvexaException($"Collection '{from}' was not found.");
        }

        if (_collections.Any(c => c.Name == to))
        {
            throw new NuvexaException($"Collection '{to}' already exists.");
        }

        await WriteAsync(() =>
        {
            lock (_collectionsLock)
            {
                var meta = _collections.First(c => c.Name == from);
                meta.Name = to;
            }

            PersistCatalog();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NuvexaQueryResult> ExecuteAsync(string queryText, CancellationToken cancellationToken = default)
    {
        if (NuvexaWriteQuery.TryParse(queryText, out var write))
        {
            var col = GetCollection(write.Collection);
            if (write.IsDelete)
            {
                var deleted = await col.DeleteAsync(write.Filter, cancellationToken).ConfigureAwait(false);
                return new NuvexaQueryResult
                {
                    Collection = write.Collection,
                    Operation = "delete",
                    Affected = deleted
                };
            }

            var updated = await col.UpdateAsync(write.Filter, write.UpdateJson!, cancellationToken).ConfigureAwait(false);
            return new NuvexaQueryResult
            {
                Collection = write.Collection,
                Operation = "update",
                Affected = updated
            };
        }

        if (NuvexaAggregate.TryParse(queryText, out var aggCollection, out var pipeline))
        {
            var agg = await NuvexaAggregate.RunAsync(this, aggCollection, pipeline, cancellationToken).ConfigureAwait(false);
            return new NuvexaQueryResult
            {
                Collection = agg.Collection,
                Documents = agg.Documents,
                Operation = "aggregate"
            };
        }

        var query = NuvexaQuery.Parse(queryText);
        var findCol = GetCollection(query.Collection);
        var docs = await findCol.Find(query.Filter)
            .Skip(query.Skip)
            .Limit(query.Limit)
            .Project(query.Projection?.ToArray() ?? [])
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (query.Sort.Count > 0)
        {
            foreach (var (path, asc) in query.Sort)
            {
                docs = (await findCol.Find(query.Filter).Sort(path, asc).Skip(query.Skip).Limit(query.Limit)
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }
        }

        return new NuvexaQueryResult { Documents = docs, Collection = query.Collection, Operation = "find" };
    }

    public Task<NuvexaTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(() =>
        {
            if (_currentTx is null)
            {
                Store.Checkpoint();
                _currentTx = new NuvexaTransaction(this);
            }

            return _currentTx;
        }, cancellationToken);

    public Task CheckpointAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(() => Store.Checkpoint(), cancellationToken);

    public async Task CompactAsync(CancellationToken cancellationToken = default)
    {
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_currentTx is not null)
            {
                throw new NuvexaException("Cannot compact while a transaction is open.");
            }

            await DrainReadersAsync().ConfigureAwait(false);
            Store.Checkpoint();
            var temp = Path + ".compact";
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            var create = new NuvexaCreateOptions
            {
                CacheSizeMb = _cacheSizeMb,
                EncryptionKey = Encrypted ? _encryptionKey : null,
                Argon2MemoryKb = Store.Superblock.KdfMemoryKb,
                Argon2Iterations = Store.Superblock.KdfIterations,
                Argon2Parallelism = Store.Superblock.KdfParallelism
            };
            if (Encrypted && string.IsNullOrEmpty(create.EncryptionKey))
            {
                throw new NuvexaEncryptionException("Compact of an encrypted database requires the encryption key used to open it.");
            }

            List<CollectionMeta> snapshot;
            lock (_collectionsLock)
            {
                snapshot = _collections.ToList();
            }

            await using (var dest = Create(temp, create))
            {
                foreach (var meta in snapshot)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var source = new NuvexaCollection(this, meta);
                    var target = dest.GetCollection(meta.Name);
                    var batch = new List<NuvexaDocument>(NuvexaLimits.CompactBatchSize);
                    foreach (var doc in source.EnumerateAll())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        batch.Add(doc);
                        if (batch.Count >= NuvexaLimits.CompactBatchSize)
                        {
                            await target.InsertManyAsync(batch, cancellationToken).ConfigureAwait(false);
                            batch.Clear();
                        }
                    }

                    if (batch.Count > 0)
                    {
                        await target.InsertManyAsync(batch, cancellationToken).ConfigureAwait(false);
                    }
                }

                await dest.CheckpointAsync(cancellationToken).ConfigureAwait(false);
            }

            Store.ReplaceDataFile(temp, _encryptionKey);
            ReloadCollectionsInPlace();
        }
        finally
        {
            _writer.Release();
        }
    }

    /// <summary>
    /// Copies <paramref name="backupPath"/> onto <paramref name="destinationPath"/>.
    /// Destination must not be open in this process. Use the same encryption key to open the restored file.
    /// </summary>
    public static Task RestoreAsync(string backupPath, string destinationPath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!File.Exists(backupPath))
        {
            throw new NuvexaException($"Backup file '{backupPath}' was not found.");
        }

        if (File.Exists(destinationPath) && !overwrite)
        {
            throw new NuvexaException($"A file already exists at '{destinationPath}'.");
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(destinationPath)) ?? ".");
        return Task.Run(() =>
        {
            File.Copy(backupPath, destinationPath, overwrite);
            var wal = backupPath + "-wal";
            if (File.Exists(wal))
            {
                File.Copy(wal, destinationPath + "-wal", overwrite);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Checkpoints, then copies the <c>.nvx</c> to <paramref name="destinationPath"/>.
    /// The copy can be opened later with the same key. Does not copy a live WAL
    /// (checkpoint truncates it).
    /// </summary>
    public async Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        await CheckpointAsync(cancellationToken).ConfigureAwait(false);
        var dest = destinationPath;
        if (Directory.Exists(dest) || dest.EndsWith(System.IO.Path.DirectorySeparatorChar) ||
            dest.EndsWith(System.IO.Path.AltDirectorySeparatorChar))
        {
            dest = System.IO.Path.Combine(dest, System.IO.Path.GetFileName(Path));
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(dest)) ?? ".");
        Store.CopyDataFile(dest);
    }

    public NuvexaStats GetStats()
    {
        var file = new FileInfo(Path);
        var wal = new FileInfo(Store.WalPath);
        return new NuvexaStats
        {
            Path = Path,
            FileBytes = file.Exists ? file.Length : 0,
            WalBytes = wal.Exists ? wal.Length : 0,
            PageCount = Store.Superblock.PageCount,
            DocumentCount = _collections.Sum(c => c.Count),
            CollectionCount = _collections.Count,
            CachedPages = Store.Cache.Count,
            CacheSizeMb = _cacheSizeMb,
            Encrypted = Encrypted,
            CommittedLsn = Store.Superblock.CommittedLsn,
            CompactNeeded = Store.Superblock.CompactNeeded
        };
    }

    public Task ChangeEncryptionKeyAsync(string currentKey, string nextKey, CancellationToken cancellationToken = default)
    {
        if (!Encrypted)
        {
            throw new NuvexaException("The database is not encrypted.");
        }

        return WriteAsync(() =>
        {
            var kek = KeyDerivation.DeriveKek(currentKey, Store.Superblock.Salt, Store.Superblock.KdfMemoryKb, Store.Superblock.KdfIterations, Store.Superblock.KdfParallelism);
            KeyDerivation.VerifyKek(kek, Store.Superblock);
            CryptographicOperations.ZeroMemory(kek);
            var nextKek = KeyDerivation.DeriveKek(nextKey, Store.Superblock.Salt, Store.Superblock.KdfMemoryKb, Store.Superblock.KdfIterations, Store.Superblock.KdfParallelism);
            // Re-wrap the existing DEK — pages stay as-is.
            var dek = KeyDerivation.UnwrapDek(
                KeyDerivation.DeriveKek(currentKey, Store.Superblock.Salt, Store.Superblock.KdfMemoryKb, Store.Superblock.KdfIterations, Store.Superblock.KdfParallelism),
                Store.Superblock);
            KeyDerivation.WrapKeys(Store.Superblock, nextKek, dek);
            CryptographicOperations.ZeroMemory(nextKek);
            CryptographicOperations.ZeroMemory(dek);
            Store.FlushSuperblock();
            _encryptionKey = nextKey;
        }, cancellationToken);
    }

    internal void PersistCatalog() => Catalog.Save(Store, _collections);

    internal Task WriteAsync(Action action, CancellationToken cancellationToken) =>
        WriteAsync(() =>
        {
            action();
            return true;
        }, cancellationToken);

    internal async Task<T> WriteAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await DrainReadersAsync().ConfigureAwait(false);
            var result = action();
            if (_currentTx is null)
            {
                CommitDirty();
            }

            return result;
        }
        finally
        {
            _writer.Release();
        }
    }

    internal async Task<T> ReadAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _readers);
        _writer.Release();
        try
        {
            ThrowIfDisposed();
            return action();
        }
        finally
        {
            Interlocked.Decrement(ref _readers);
        }
    }

    internal void CommitDirty()
    {
        var dirty = Store.Cache.DirtyPages().ToList();
        if (dirty.Count > 0)
        {
            Store.Commit(dirty);
        }
    }

    internal void RollbackUncommitted()
    {
        Store.AbortUncommitted();
        ReloadCollectionsInPlace();
    }

    internal void EndTransaction() => _currentTx = null;

    private void ReloadCollectionsInPlace()
    {
        var fresh = Catalog.Load(Store);
        lock (_collectionsLock)
        {
            var byName = fresh.ToDictionary(c => c.Name, StringComparer.Ordinal);
            for (var i = _collections.Count - 1; i >= 0; i--)
            {
                var meta = _collections[i];
                if (!byName.TryGetValue(meta.Name, out var next))
                {
                    _collections.RemoveAt(i);
                    continue;
                }

                meta.DataHead = next.DataHead;
                meta.DataTail = next.DataTail;
                meta.IndexRoot = next.IndexRoot;
                meta.Count = next.Count;
                meta.SecondaryCatalogPage = next.SecondaryCatalogPage;
                byName.Remove(meta.Name);
            }

            foreach (var extra in byName.Values)
            {
                _collections.Add(extra);
            }
        }
    }

    private async Task DrainReadersAsync()
    {
        var wait = new SpinWait();
        while (Volatile.Read(ref _readers) > 0)
        {
            if (wait.NextSpinWillYield)
            {
                await Task.Yield();
            }
            else
            {
                wait.SpinOnce();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Store.Dispose();
        _writer.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(NuvexaDatabase));
        }
    }
}

public sealed class NuvexaQueryResult
{
    public string Collection { get; init; } = "";
    public List<NuvexaDocument> Documents { get; init; } = [];
    public string Operation { get; init; } = "find";
    public long Affected { get; init; }
}

public sealed class NuvexaTransaction : IAsyncDisposable
{
    private readonly NuvexaDatabase _db;
    private bool _done;

    internal NuvexaTransaction(NuvexaDatabase db) => _db = db;

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_done)
        {
            return Task.CompletedTask;
        }

        _done = true;
        _db.EndTransaction();
        return _db.WriteAsync(() => _db.CommitDirty(), cancellationToken);
    }

    /// <summary>Discards uncommitted dirty pages. The last checkpoint / commit stays on disk.</summary>
    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_done)
        {
            return Task.CompletedTask;
        }

        _done = true;
        return _db.WriteAsync(() =>
        {
            _db.RollbackUncommitted();
            _db.EndTransaction();
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_done)
        {
            return;
        }

        await RollbackAsync().ConfigureAwait(false);
    }
}
