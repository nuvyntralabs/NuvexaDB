using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Nuventra.NuvexaDB.Engine;

internal sealed class Superblock
{
    public ushort Version { get; set; } = Constants.FormatVersion;
    public SuperblockFlags Flags { get; set; }
    public int PageSize { get; set; } = Constants.PageSize;
    public long PageCount { get; set; } = Constants.FirstAllocPageId;
    public byte[] FileId { get; set; } = new byte[16];
    public long CatalogPageId { get; set; } = Constants.CatalogPageId;
    public long NextPageId { get; set; } = Constants.FirstAllocPageId;
    public long CommittedLsn { get; set; }
    public int KdfMemoryKb { get; set; } = 16 * 1024;
    public int KdfIterations { get; set; } = 3;
    public int KdfParallelism { get; set; } = 2;
    public byte[] Salt { get; set; } = new byte[16];
    public byte[] VerifierNonce { get; set; } = new byte[12];
    public byte[] VerifierTag { get; set; } = new byte[16];
    public byte[] VerifierCipher { get; set; } = new byte[16];
    public byte[] DekNonce { get; set; } = new byte[12];
    public byte[] DekTag { get; set; } = new byte[16];
    public byte[] DekCipher { get; set; } = new byte[32];

    public bool Encrypted => (Flags & SuperblockFlags.Encrypted) != 0;
    public bool CompactNeeded => (Flags & SuperblockFlags.CompactNeeded) != 0;

    public void CopyFrom(Superblock other)
    {
        Version = other.Version;
        Flags = other.Flags;
        PageSize = other.PageSize;
        PageCount = other.PageCount;
        FileId = other.FileId;
        CatalogPageId = other.CatalogPageId;
        NextPageId = other.NextPageId;
        CommittedLsn = other.CommittedLsn;
        KdfMemoryKb = other.KdfMemoryKb;
        KdfIterations = other.KdfIterations;
        KdfParallelism = other.KdfParallelism;
        Salt = other.Salt;
        VerifierNonce = other.VerifierNonce;
        VerifierTag = other.VerifierTag;
        VerifierCipher = other.VerifierCipher;
        DekNonce = other.DekNonce;
        DekTag = other.DekTag;
        DekCipher = other.DekCipher;
    }

    public static Superblock Read(ReadOnlySpan<byte> page)
    {
        if (page.Length < Constants.PageSize)
        {
            throw new NuvexaException("Superblock is truncated.");
        }

        if (!page[..4].SequenceEqual(Constants.FileMagic))
        {
            throw new NuvexaException("Not a NuvexaDB .nvx file (missing NVX1 magic).");
        }

        VerifyCrc(page);

        var s = new Superblock
        {
            Version = BinaryPrimitives.ReadUInt16LittleEndian(page[4..]),
            Flags = (SuperblockFlags)BinaryPrimitives.ReadUInt16LittleEndian(page[6..]),
            PageSize = BinaryPrimitives.ReadInt32LittleEndian(page[8..]),
            PageCount = BinaryPrimitives.ReadInt64LittleEndian(page[12..]),
            FileId = page.Slice(20, 16).ToArray(),
            CatalogPageId = BinaryPrimitives.ReadInt64LittleEndian(page[36..]),
            NextPageId = BinaryPrimitives.ReadInt64LittleEndian(page[44..]),
            CommittedLsn = BinaryPrimitives.ReadInt64LittleEndian(page[52..]),
            KdfMemoryKb = BinaryPrimitives.ReadInt32LittleEndian(page[68..]),
            KdfIterations = BinaryPrimitives.ReadInt32LittleEndian(page[72..]),
            KdfParallelism = BinaryPrimitives.ReadInt16LittleEndian(page[76..]),
            Salt = page.Slice(78, 16).ToArray(),
            VerifierNonce = page.Slice(94, 12).ToArray(),
            VerifierTag = page.Slice(106, 16).ToArray(),
            VerifierCipher = page.Slice(122, 16).ToArray(),
            DekNonce = page.Slice(138, 12).ToArray(),
            DekTag = page.Slice(150, 16).ToArray(),
            DekCipher = page.Slice(166, 32).ToArray()
        };

        if (s.Version != Constants.FormatVersion)
        {
            throw new NuvexaException($"Unsupported .nvx format version {s.Version}.");
        }

        if (s.PageSize != Constants.PageSize)
        {
            throw new NuvexaException($"Unsupported page size {s.PageSize}.");
        }

        if (s.KdfParallelism < 1)
        {
            s.KdfParallelism = 2;
        }

        if (s.KdfIterations < 1)
        {
            s.KdfIterations = 3;
        }

        if (s.KdfMemoryKb < 8 * 1024)
        {
            s.KdfMemoryKb = 16 * 1024;
        }

        return s;
    }

    public void Write(Span<byte> page)
    {
        page.Clear();
        Constants.FileMagic.CopyTo(page);
        BinaryPrimitives.WriteUInt16LittleEndian(page[4..], Version);
        BinaryPrimitives.WriteUInt16LittleEndian(page[6..], (ushort)Flags);
        BinaryPrimitives.WriteInt32LittleEndian(page[8..], PageSize);
        BinaryPrimitives.WriteInt64LittleEndian(page[12..], PageCount);
        FileId.CopyTo(page[20..]);
        BinaryPrimitives.WriteInt64LittleEndian(page[36..], CatalogPageId);
        BinaryPrimitives.WriteInt64LittleEndian(page[44..], NextPageId);
        BinaryPrimitives.WriteInt64LittleEndian(page[52..], CommittedLsn);
        BinaryPrimitives.WriteInt32LittleEndian(page[68..], KdfMemoryKb);
        BinaryPrimitives.WriteInt32LittleEndian(page[72..], KdfIterations);
        BinaryPrimitives.WriteInt16LittleEndian(page[76..], (short)KdfParallelism);
        Salt.CopyTo(page[78..]);
        VerifierNonce.CopyTo(page[94..]);
        VerifierTag.CopyTo(page[106..]);
        VerifierCipher.CopyTo(page[122..]);
        DekNonce.CopyTo(page[138..]);
        DekTag.CopyTo(page[150..]);
        DekCipher.CopyTo(page[166..]);
        var crc = Crc32.Compute(page[..Constants.SuperblockCrcLength]);
        BinaryPrimitives.WriteUInt32LittleEndian(page[Constants.SuperblockCrcOffset..], crc);
    }

    public static void VerifyCrc(ReadOnlySpan<byte> page)
    {
        var stored = BinaryPrimitives.ReadUInt32LittleEndian(page[Constants.SuperblockCrcOffset..]);
        Span<byte> prefix = stackalloc byte[Constants.SuperblockCrcLength];
        page[..Constants.SuperblockCrcLength].CopyTo(prefix);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix[Constants.SuperblockCrcOffset..], 0);
        if (stored != Crc32.Compute(prefix))
        {
            throw new NuvexaIntegrityException("The database file is corrupt or has been tampered with.");
        }
    }

    public static void WriteIntegrityMac(Span<byte> page, ReadOnlySpan<byte> dek)
    {
        var mac = HMACSHA256.HashData(dek, page[..Constants.SuperblockCrcLength]);
        mac.CopyTo(page.Slice(Constants.SuperblockMacOffset, Constants.SuperblockMacSize));
    }

    public static void VerifyIntegrityMac(ReadOnlySpan<byte> page, ReadOnlySpan<byte> dek)
    {
        var stored = page.Slice(Constants.SuperblockMacOffset, Constants.SuperblockMacSize);
        if (stored.IndexOfAnyExcept((byte)0) < 0)
        {
            return;
        }

        var expected = HMACSHA256.HashData(dek, page[..Constants.SuperblockCrcLength]);
        if (!CryptographicOperations.FixedTimeEquals(stored, expected))
        {
            throw new NuvexaIntegrityException("The database file is corrupt or has been tampered with.");
        }
    }

    public static bool PeekEncrypted(ReadOnlySpan<byte> page)
    {
        if (page.Length < 8 || !page[..4].SequenceEqual(Constants.FileMagic))
        {
            throw new NuvexaException("Not a NuvexaDB .nvx file (missing NVX1 magic).");
        }

        var flags = (SuperblockFlags)BinaryPrimitives.ReadUInt16LittleEndian(page[6..]);
        return (flags & SuperblockFlags.Encrypted) != 0;
    }
}
