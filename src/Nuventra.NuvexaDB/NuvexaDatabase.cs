using System.Security.Cryptography;
using Nuventra.NuvexaDB.Encryption;
using Nuventra.NuvexaDB.Engine;
using Nuventra.NuvexaDB.Query;

namespace Nuventra.NuvexaDB;

/// <summary>Embedded NuvexaDB handle for a single <c>.nvx</c> file.</summary>
public sealed class NuvexaDatabase : IDisposable, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _cacheSizeMb;
    private readonly List<CollectionMeta> _collections;
    private bool _disposed;
    private NuvexaTransaction? _currentTx;

    internal PageStore Store { get; }

    private NuvexaDatabase(PageStore store, int cacheSizeMb)
    {
        Store = store;
        _cacheSizeMb = cacheSizeMb;
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

    public static bool IsEncrypted(string path)
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

    public static NuvexaDatabase Create(string path, NuvexaCreateOptions? options = null)
    {
        options ??= new NuvexaCreateOptions();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".");
        if (File.Exists(path))
        {
            throw new NuvexaException($"A file already exists at '{path}'.");
        }

        var super = new Superblock
        {
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

        var store = new PageStore(path, super, dek, options.CacheSizeMb, readOnly: false, options.CheckpointThreshold, create: true);
        return new NuvexaDatabase(store, options.CacheSizeMb);
    }

    public static NuvexaDatabase Open(string path, NuvexaOpenOptions? options = null)
    {
        options ??= new NuvexaOpenOptions();
        if (!File.Exists(path))
        {
            throw new NuvexaException($"Database file '{path}' was not found.");
        }

        var header = new byte[Constants.PageSize];
        using (var stream = File.OpenRead(path))
        {
            if (stream.Read(header) < Constants.PageSize)
            {
                throw new NuvexaException("The .nvx file is truncated.");
            }
        }

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
            CryptographicOperations.ZeroMemory(kek);
        }

        var store = new PageStore(path, super, dek, options.CacheSizeMb, options.ReadOnly, options.CheckpointThreshold, create: false);
        return new NuvexaDatabase(store, options.CacheSizeMb);
    }

    public static Task<NuvexaDatabase> CreateAsync(string path, NuvexaCreateOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Create(path, options), cancellationToken);

    public static Task<NuvexaDatabase> OpenAsync(string path, NuvexaOpenOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Open(path, options), cancellationToken);

    public IReadOnlyList<string> GetCollectionNames() => _collections.Select(c => c.Name).ToList();

    public NuvexaCollection GetCollection(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > Constants.MaxCollectionName)
        {
            throw new NuvexaException("Collection name is too long.");
        }

        var meta = _collections.FirstOrDefault(c => c.Name == name);
        if (meta is null)
        {
            var tree = new BPlusTree(Store, 0);
            meta = new CollectionMeta { Name = name, IndexRoot = tree.RootPageId };
            _collections.Add(meta);
            PersistCatalog();
            CommitDirty();
        }

        return new NuvexaCollection(this, meta);
    }

    public NuvexaCollection<T> GetCollection<T>(string? name = null) where T : class =>
        new(GetCollection(name ?? typeof(T).Name));

    public async Task<NuvexaQueryResult> ExecuteAsync(string queryText, CancellationToken cancellationToken = default)
    {
        if (NuvexaAggregate.TryParse(queryText, out var aggCollection, out var pipeline))
        {
            return await NuvexaAggregate.RunAsync(this, aggCollection, pipeline, cancellationToken).ConfigureAwait(false);
        }

        var query = NuvexaQuery.Parse(queryText);
        var col = GetCollection(query.Collection);
        var docs = await col.Find(query.Filter)
            .Skip(query.Skip)
            .Limit(query.Limit)
            .Project(query.Projection?.ToArray() ?? [])
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (query.Sort.Count > 0)
        {
            foreach (var (path, asc) in query.Sort)
            {
                docs = (await col.Find(query.Filter).Sort(path, asc).Skip(query.Skip).Limit(query.Limit)
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }
        }

        return new NuvexaQueryResult { Documents = docs, Collection = query.Collection };
    }

    public Task<NuvexaTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(() =>
        {
            _currentTx ??= new NuvexaTransaction(this);
            return _currentTx;
        }, cancellationToken);

    public Task CheckpointAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(() => Store.Checkpoint(), cancellationToken);

    public async Task CompactAsync(CancellationToken cancellationToken = default)
    {
        await CheckpointAsync(cancellationToken).ConfigureAwait(false);
        var temp = Path + ".compact";
        if (File.Exists(temp))
        {
            File.Delete(temp);
        }

        var create = new NuvexaCreateOptions
        {
            CacheSizeMb = _cacheSizeMb,
            Argon2MemoryKb = Store.Superblock.KdfMemoryKb,
            Argon2Iterations = Store.Superblock.KdfIterations,
            Argon2Parallelism = Store.Superblock.KdfParallelism
        };
        // Compact writes a plaintext copy; caller can re-encrypt by creating with a key.
        await using (var dest = Create(temp, create))
        {
            foreach (var name in GetCollectionNames())
            {
                var source = GetCollection(name);
                var target = dest.GetCollection(name);
                var docs = await source.Find().ToListAsync(cancellationToken).ConfigureAwait(false);
                if (docs.Count > 0)
                {
                    await target.InsertManyAsync(docs, cancellationToken).ConfigureAwait(false);
                }
            }

            await dest.CheckpointAsync(cancellationToken).ConfigureAwait(false);
        }

        await DisposeAsync().ConfigureAwait(false);
        File.Delete(Path);
        if (File.Exists(Path + "-wal"))
        {
            File.Delete(Path + "-wal");
        }

        File.Move(temp, Path);
        if (File.Exists(temp + "-wal"))
        {
            File.Move(temp + "-wal", Path + "-wal");
        }
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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var result = action();
            if (_currentTx is null)
            {
                CommitDirty();
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<T> ReadAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return action();
        }
        finally
        {
            _gate.Release();
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

    internal void EndTransaction() => _currentTx = null;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Store.Dispose();
        _gate.Dispose();
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

    public ValueTask DisposeAsync()
    {
        if (!_done)
        {
            _db.EndTransaction();
            _db.CommitDirty();
            _done = true;
        }

        return ValueTask.CompletedTask;
    }
}
