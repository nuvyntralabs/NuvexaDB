using System.Buffers.Binary;

namespace Nuventra.NuvexaDB.Engine;

internal sealed class Wal : IDisposable
{
    private readonly string _path;
    private FileStream _stream;
    private int _headerSize = Constants.WalHeaderV2Size;

    public Wal(string path, bool readOnly)
    {
        _path = path;
        var access = readOnly ? FileAccess.Read : FileAccess.ReadWrite;
        var mode = File.Exists(path) ? FileMode.Open : readOnly ? FileMode.Open : FileMode.OpenOrCreate;
        if (readOnly && !File.Exists(path))
        {
            _stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            WriteHeader();
            _stream.Dispose();
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return;
        }

        _stream = new FileStream(path, mode == FileMode.OpenOrCreate ? FileMode.OpenOrCreate : mode, access, FileShare.None);
        if (_stream.Length == 0)
        {
            WriteHeader();
        }
        else
        {
            ValidateHeader();
        }
    }

    public long Length => _stream.Length;

    public void AppendPage(long pageId, long lsn, ReadOnlySpan<byte> physicalPage)
    {
        if (physicalPage.Length != Constants.PageSize)
        {
            throw new NuvexaException("WAL page must be a full physical page.");
        }

        var header = new byte[1 + 8 + 8 + 4];
        header[0] = (byte)WalRecordType.Page;
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(1), pageId);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(9), lsn);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(17), physicalPage.Length);
        _stream.Seek(0, SeekOrigin.End);
        _stream.Write(header);
        _stream.Write(physicalPage);
        var crc = Crc32.Compute(physicalPage);
        Span<byte> crcBuf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(crcBuf, crc);
        _stream.Write(crcBuf);
    }

    public void AppendCommit(long lsn)
    {
        Span<byte> rec = stackalloc byte[1 + 8 + 4];
        rec[0] = (byte)WalRecordType.Commit;
        BinaryPrimitives.WriteInt64LittleEndian(rec[1..], lsn);
        var crc = Crc32.Compute(rec[..9]);
        BinaryPrimitives.WriteUInt32LittleEndian(rec[9..], crc);
        _stream.Seek(0, SeekOrigin.End);
        _stream.Write(rec);
        _stream.Flush(flushToDisk: true);
    }

    public List<(long PageId, long Lsn, byte[] Physical)> Replay()
    {
        var pages = new List<(long, long, byte[])>();
        _stream.Seek(_headerSize, SeekOrigin.Begin);
        var lastCommit = 0L;
        var pending = new List<(long PageId, long Lsn, byte[] Physical)>();
        while (_stream.Position < _stream.Length)
        {
            var type = _stream.ReadByte();
            if (type < 0)
            {
                break;
            }

            if (type == (byte)WalRecordType.Commit)
            {
                Span<byte> rest = stackalloc byte[8 + 4];
                if (_stream.Read(rest) != rest.Length)
                {
                    break;
                }

                var lsn = BinaryPrimitives.ReadInt64LittleEndian(rest);
                var crc = BinaryPrimitives.ReadUInt32LittleEndian(rest[8..]);
                Span<byte> prefix = stackalloc byte[9];
                prefix[0] = (byte)WalRecordType.Commit;
                BinaryPrimitives.WriteInt64LittleEndian(prefix[1..], lsn);
                if (Crc32.Compute(prefix) != crc)
                {
                    break;
                }

                lastCommit = lsn;
                pages.AddRange(pending.Where(p => p.Lsn <= lastCommit));
                pending.Clear();
                continue;
            }

            if (type != (byte)WalRecordType.Page)
            {
                break;
            }

            Span<byte> hdr = stackalloc byte[8 + 8 + 4];
            if (_stream.Read(hdr) != hdr.Length)
            {
                break;
            }

            var pageId = BinaryPrimitives.ReadInt64LittleEndian(hdr);
            var lsn2 = BinaryPrimitives.ReadInt64LittleEndian(hdr[8..]);
            var len = BinaryPrimitives.ReadInt32LittleEndian(hdr[16..]);
            if (len != Constants.PageSize)
            {
                break;
            }

            var physical = new byte[len];
            if (_stream.Read(physical) != len)
            {
                break;
            }

            Span<byte> crcBuf = stackalloc byte[4];
            if (_stream.Read(crcBuf) != 4)
            {
                break;
            }

            var stored = BinaryPrimitives.ReadUInt32LittleEndian(crcBuf);
            if (Crc32.Compute(physical) != stored)
            {
                break;
            }

            pending.Add((pageId, lsn2, physical));
        }

        return pages;
    }

    public void Truncate()
    {
        _stream.SetLength(0);
        WriteHeader();
        _stream.Flush(flushToDisk: true);
    }

    public void Dispose() => _stream.Dispose();

    private void WriteHeader()
    {
        _headerSize = Constants.WalHeaderV2Size;
        _stream.Seek(0, SeekOrigin.Begin);
        _stream.Write(Constants.WalMagic);
        Span<byte> rest = stackalloc byte[28];
        BinaryPrimitives.WriteUInt16LittleEndian(rest, Constants.FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(rest[2..], Constants.PageSize);
        _stream.Write(rest);
        _stream.Flush(flushToDisk: true);
    }

    private void ValidateHeader()
    {
        _stream.Seek(0, SeekOrigin.Begin);
        Span<byte> magic = stackalloc byte[4];
        if (_stream.Read(magic) != 4 || !magic.SequenceEqual(Constants.WalMagic))
        {
            throw new NuvexaException("WAL file is not a NuvexaDB log.");
        }

        Span<byte> ver = stackalloc byte[2];
        if (_stream.Read(ver) != 2)
        {
            throw new NuvexaException("WAL file is truncated.");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(ver);
        if (version == 1)
        {
            _headerSize = Constants.WalHeaderV1Size;
            return;
        }

        if (version == 2)
        {
            _headerSize = Constants.WalHeaderV2Size;
            Span<byte> rest = stackalloc byte[26];
            if (_stream.Read(rest) != rest.Length)
            {
                throw new NuvexaException("WAL file is truncated.");
            }

            var pageSize = BinaryPrimitives.ReadInt32LittleEndian(rest);
            if (pageSize != 0 && pageSize != Constants.PageSize)
            {
                throw new NuvexaException($"Unsupported WAL page size {pageSize}.");
            }

            return;
        }

        throw new NuvexaException($"Unsupported WAL format version {version}.");
    }
}
