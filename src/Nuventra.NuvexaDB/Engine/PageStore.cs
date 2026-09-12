using Nuventra.NuvexaDB.Encryption;

namespace Nuventra.NuvexaDB.Engine;

internal sealed class PageStore : IDisposable
{
    private readonly FileStream _data;
    private readonly Wal _wal;
    private readonly PageCache _cache;
    private readonly byte[]? _dek;
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
        _readOnly = readOnly;
        _checkpointThreshold = Math.Max(8, checkpointThreshold);
        _cache = new PageCache(cacheSizeMb);

        var access = readOnly ? FileAccess.Read : FileAccess.ReadWrite;
        var mode = create ? FileMode.Create : FileMode.Open;
        _data = new FileStream(path, mode, access, FileShare.Read, Constants.PageSize, FileOptions.RandomAccess);
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
            throw new NuvexaException($"Page {pageId} stored a mismatched id {page.PageId}.");
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

    private void WriteSuperblockToWal(long lsn)
    {
        var buf = new byte[Constants.PageSize];
        Superblock.Write(buf);
        _wal.AppendPage(Constants.SuperblockPageId, lsn, buf);
    }

    private void WriteSuperblock()
    {
        var buf = new byte[Constants.PageSize];
        Superblock.Write(buf);
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

        if (_dek is null)
        {
            throw new NuvexaEncryptionException("The encryption key is missing.");
        }

        AesGcmPageCipher.EncryptPage(_dek, page.PageId, Superblock.FileId, page.Buffer, physical);
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

        if (_dek is null)
        {
            throw new NuvexaEncryptionException("The encryption key is missing.");
        }

        AesGcmPageCipher.DecryptPage(_dek, pageId, Superblock.FileId, physical, logical);
        return logical;
    }

    private byte[] ReadPhysical(long pageId)
    {
        var offset = pageId * Constants.PageSize;
        if (offset + Constants.PageSize > _data.Length)
        {
            var empty = new byte[Constants.PageSize];
            return empty;
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
        var offset = pageId * Constants.PageSize;
        if (_data.Length < offset + Constants.PageSize)
        {
            _data.SetLength(offset + Constants.PageSize);
        }

        _data.Seek(offset, SeekOrigin.Begin);
        _data.Write(physical);
    }
}
