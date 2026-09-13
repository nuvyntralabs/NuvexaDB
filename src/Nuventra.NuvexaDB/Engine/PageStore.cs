using Nuventra.NuvexaDB.Encryption;

namespace Nuventra.NuvexaDB.Engine;

internal sealed class PageStore : IDisposable
{
    private FileStream _data;
    private Wal _wal;
    private readonly PageCache _cache;
    private byte[]? _dek;
    private AesGcmPageCipher? _cipher;
    private readonly bool _readOnly;
    private readonly int _checkpointThreshold;
    private int _pagesSinceCheckpoint;
    private readonly object _sync = new();

    public Superblock Superblock { get; }
    public string Path { get; }
    public string WalPath { get; }
    public bool Encrypted => Superblock.Encrypted;
    public PageCache Cache => _cache;

    public PageStore(string path, Superblock superblock, byte[]? dek, int cacheSizeMb, bool readOnly, int checkpointThreshold, bool create)
    {
        Path = path;
        WalPath = path + "-wal";
        Superblock = superblock;
        _dek = dek;
        _cipher = dek is not null ? new AesGcmPageCipher(dek) : null;
        _readOnly = readOnly;
        _checkpointThreshold = Math.Max(8, checkpointThreshold);
        _cache = new PageCache(cacheSizeMb);

        var access = readOnly ? FileAccess.Read : FileAccess.ReadWrite;
        var mode = create ? FileMode.Create : FileMode.Open;
        _data = new FileStream(path, mode, access, FileShare.None, Constants.PageSize, FileOptions.RandomAccess);
        _wal = new Wal(WalPath, readOnly);

        if (create)
        {
            WriteSuperblock();
            var catalog = Page.Create(Constants.CatalogPageId, PageType.Catalog);
            WritePage(catalog, toWal: false);
            _data.Flush(flushToDisk: true);
        }
        else
        {
            ReplayWal();
        }
    }

    public long AllocatePageId()
    {
        lock (_sync)
        {
            var id = Superblock.NextPageId;
            Superblock.NextPageId++;
            Superblock.PageCount = Math.Max(Superblock.PageCount, Superblock.NextPageId);
            return id;
        }
    }

    public Page Allocate(PageType type)
    {
        var page = Page.Create(AllocatePageId(), type);
        _cache.Set(page);
        return page;
    }

    public Page Get(long pageId)
    {
        if (pageId == Constants.SuperblockPageId)
        {
            throw new NuvexaException("Use Superblock APIs for page 0.");
        }

        if (_cache.TryGet(pageId, out var cached))
        {
            return cached;
        }

        var physical = ReadPhysical(pageId);
        var logical = DecryptLogical(pageId, physical);
        var page = new Page();
        logical.CopyTo(page.Buffer);
        if (page.PageId != 0 && page.PageId != pageId)
        {
            throw new NuvexaIntegrityException($"The database file is corrupt or has been tampered with (page {pageId}).");
        }

        page.PageId = pageId;
        if (page.Type != PageType.Free)
        {
            page.VerifyChecksum();
        }

        _cache.Set(page);
        return page;
    }

    public void MarkDirty(Page page)
    {
        page.Dirty = true;
        _cache.Set(page);
    }

    public long NextLsn()
    {
        lock (_sync)
        {
            Superblock.CommittedLsn++;
            return Superblock.CommittedLsn;
        }
    }

    public void Commit(IEnumerable<Page> dirty)
    {
        if (_readOnly)
        {
            throw new NuvexaException("The database is open read-only.");
        }

        var pages = dirty as IList<Page> ?? dirty.ToList();
        if (pages.Count == 0)
        {
            return;
        }

        var lsn = NextLsn();
        foreach (var page in pages)
        {
            page.Lsn = lsn;
            page.Seal();
            var physical = EncryptPhysical(page);
            _wal.AppendPage(page.PageId, lsn, physical);
            page.Dirty = false;
        }

        WriteSuperblockToWal(lsn);
        _wal.AppendCommit(lsn);
        _pagesSinceCheckpoint += pages.Count;
        if (_pagesSinceCheckpoint >= _checkpointThreshold)
        {
            Checkpoint();
        }
    }

    public void Checkpoint()
    {
        if (_readOnly)
        {
            return;
        }

        foreach (var page in _cache.DirtyPages().ToList())
        {
            page.Seal();
            WritePage(page, toWal: false);
            page.Dirty = false;
        }

        // Replay committed WAL pages onto the data file, then persist the superblock.
        foreach (var (pageId, _, physical) in _wal.Replay())
        {
            WritePhysical(pageId, physical);
        }

        WriteSuperblock();
        _data.Flush(flushToDisk: true);
        _wal.Truncate();
        _pagesSinceCheckpoint = 0;
    }

    public void FlushSuperblock()
    {
        if (_readOnly)
        {
            return;
        }

        WriteSuperblock();
        _data.Flush(flushToDisk: true);
    }

    public void Dispose()
    {
        if (!_readOnly)
        {
            try
            {
                Checkpoint();
            }
            catch (Exception)
            {
                // Dispose must not throw; WAL still has committed pages.
            }
        }

        _wal.Dispose();
        _data.Dispose();
        _cipher?.Dispose();
        if (_dek is not null)
        {
            CryptographicZero(_dek);
        }
    }

    private static void CryptographicZero(byte[] buffer)
    {
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer);
    }

    private void ReplayWal()
    {
        foreach (var (pageId, lsn, physical) in _wal.Replay())
        {
            if (pageId == Constants.SuperblockPageId)
            {
                if (Encrypted && _dek is not null)
                {
                    Superblock.VerifyIntegrityMac(physical, _dek);
                }

                Superblock.CommittedLsn = Math.Max(Superblock.CommittedLsn, lsn);
                continue;
            }

            var logical = DecryptLogical(pageId, physical);
            var page = new Page();
            logical.CopyTo(page.Buffer);
            page.PageId = pageId;
            page.Lsn = lsn;
            page.Dirty = true;
            _cache.Set(page);
            Superblock.CommittedLsn = Math.Max(Superblock.CommittedLsn, lsn);
            Superblock.NextPageId = Math.Max(Superblock.NextPageId, pageId + 1);
            Superblock.PageCount = Math.Max(Superblock.PageCount, Superblock.NextPageId);
        }
    }

    private void WritePage(Page page, bool toWal)
    {
        page.Seal();
        var physical = EncryptPhysical(page);
        if (toWal)
        {
            _wal.AppendPage(page.PageId, page.Lsn, physical);
        }
        else
        {
            WritePhysical(page.PageId, physical);
        }
    }

    public void CopyDataFile(string destinationPath)
    {
        lock (_sync)
        {
            _data.Flush(flushToDisk: true);
            _data.Seek(0, SeekOrigin.Begin);
            using var dest = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            _data.CopyTo(dest);
            dest.Flush(flushToDisk: true);
        }
    }

    /// <summary>Drops uncommitted dirty pages and restores the superblock from the data file.</summary>
    public void AbortUncommitted()
    {
        lock (_sync)
        {
            _cache.Clear();
            var header = ReadPhysicalUnlocked(Constants.SuperblockPageId);
            Superblock.CopyFrom(Superblock.Read(header));
            if (Encrypted && _dek is not null)
            {
                Superblock.VerifyIntegrityMac(header, _dek);
            }
        }
    }

    /// <summary>Closes the current files and opens a compacted replacement at the same path.</summary>
    public void ReplaceDataFile(string compactedPath, string? encryptionKey)
    {
        if (_readOnly)
        {
            throw new NuvexaException("The database is open read-only.");
        }

        lock (_sync)
        {
            _cache.Clear();
            _wal.Dispose();
            _data.Dispose();
            File.Delete(Path);
            if (File.Exists(WalPath))
            {
                File.Delete(WalPath);
            }

            File.Move(compactedPath, Path);
            var compactWal = compactedPath + "-wal";
            if (File.Exists(compactWal))
            {
                File.Move(compactWal, WalPath);
            }

            _data = new FileStream(Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, Constants.PageSize, FileOptions.RandomAccess);
            _wal = new Wal(WalPath, readOnly: false);
            var header = ReadPhysicalUnlocked(Constants.SuperblockPageId);
            Superblock.CopyFrom(Superblock.Read(header));
            if (Superblock.Encrypted)
            {
                if (string.IsNullOrEmpty(encryptionKey))
                {
                    throw new NuvexaEncryptionException("Compact of an encrypted database requires the encryption key used to open it.");
                }

                var kek = KeyDerivation.DeriveKek(
                    encryptionKey,
                    Superblock.Salt,
                    Superblock.KdfMemoryKb,
                    Superblock.KdfIterations,
                    Superblock.KdfParallelism);
                KeyDerivation.VerifyKek(kek, Superblock);
                var dek = KeyDerivation.UnwrapDek(kek, Superblock);
                CryptographicZero(kek);
                Superblock.VerifyIntegrityMac(header, dek);
                _cipher?.Dispose();
                if (_dek is not null)
                {
                    CryptographicZero(_dek);
                }

                _dek = dek;
                _cipher = new AesGcmPageCipher(dek);
            }

            ReplayWal();
            _pagesSinceCheckpoint = 0;
        }
    }

    private void WriteSuperblockToWal(long lsn)
    {
        var buf = new byte[Constants.PageSize];
        if (_dek is not null)
        {
            Superblock.Flags |= SuperblockFlags.IntegrityProtected;
        }

        Superblock.Write(buf);
        if (_dek is not null)
        {
            Superblock.WriteIntegrityMac(buf, _dek);
        }

        _wal.AppendPage(Constants.SuperblockPageId, lsn, buf);
    }

    public void VerifyIntegrity()
    {
        var filePages = _data.Length / Constants.PageSize;
        var last = Math.Max(Superblock.PageCount, Superblock.NextPageId);
        last = Math.Min(last, filePages);
        for (var pageId = 1L; pageId < last; pageId++)
        {
            if (_cache.TryGet(pageId, out var cached))
            {
                if (cached.Type != PageType.Free)
                {
                    cached.VerifyChecksum();
                }

                continue;
            }

            var physical = ReadPhysical(pageId);
            if (IsUnusedPhysical(physical))
            {
                continue;
            }

            var logical = DecryptLogical(pageId, physical);
            var page = new Page();
            logical.CopyTo(page.Buffer);
            if (page.PageId != 0 && page.PageId != pageId)
            {
                throw new NuvexaIntegrityException($"The database file is corrupt or has been tampered with (page {pageId}).");
            }

            if (page.Type != PageType.Free)
            {
                page.VerifyChecksum();
            }
        }
    }

    private void WriteSuperblock()
    {
        var buf = new byte[Constants.PageSize];
        if (_dek is not null)
        {
            Superblock.Flags |= SuperblockFlags.IntegrityProtected;
        }

        Superblock.Write(buf);
        if (_dek is not null)
        {
            Superblock.WriteIntegrityMac(buf, _dek);
        }

        WritePhysical(Constants.SuperblockPageId, buf);
    }

    private byte[] EncryptPhysical(Page page)
    {
        var physical = new byte[Constants.PageSize];
        if (!Encrypted || page.PageId == Constants.SuperblockPageId)
        {
            page.Buffer.CopyTo(physical, 0);
            return physical;
        }

        if (_cipher is null)
        {
            throw new NuvexaEncryptionException("The encryption key is missing.");
        }

        _cipher.EncryptPage(page.PageId, Superblock.FileId, page.Buffer, physical);
        return physical;
    }

    private byte[] DecryptLogical(long pageId, ReadOnlySpan<byte> physical)
    {
        var logical = new byte[Constants.PayloadSize];
        if (!Encrypted || pageId == Constants.SuperblockPageId)
        {
            physical[..Constants.PayloadSize].CopyTo(logical);
            return logical;
        }

        if (_cipher is null)
        {
            throw new NuvexaEncryptionException("The encryption key is missing.");
        }

        try
        {
            _cipher.DecryptPage(pageId, Superblock.FileId, physical, logical);
        }
        catch (NuvexaEncryptionException ex)
        {
            throw new NuvexaIntegrityException("The database file is corrupt or has been tampered with.", ex);
        }

        return logical;
    }

    private static bool IsUnusedPhysical(ReadOnlySpan<byte> physical)
    {
        for (var i = 0; i < physical.Length; i++)
        {
            if (physical[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private byte[] ReadPhysical(long pageId)
    {
        lock (_sync)
        {
            return ReadPhysicalUnlocked(pageId);
        }
    }

    private byte[] ReadPhysicalUnlocked(long pageId)
    {
        var offset = pageId * Constants.PageSize;
        if (offset + Constants.PageSize > _data.Length)
        {
            return new byte[Constants.PageSize];
        }

        var buffer = new byte[Constants.PageSize];
        _data.Seek(offset, SeekOrigin.Begin);
        var read = _data.Read(buffer);
        if (read < Constants.PageSize)
        {
            throw new NuvexaException($"Page {pageId} is truncated.");
        }

        return buffer;
    }

    private void WritePhysical(long pageId, ReadOnlySpan<byte> physical)
    {
        lock (_sync)
        {
            var offset = pageId * Constants.PageSize;
            if (_data.Length < offset + Constants.PageSize)
            {
                _data.SetLength(offset + Constants.PageSize);
            }

            _data.Seek(offset, SeekOrigin.Begin);
            _data.Write(physical);
        }
    }
}
