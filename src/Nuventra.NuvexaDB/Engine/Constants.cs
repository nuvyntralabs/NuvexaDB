using System.Text;

namespace Nuventra.NuvexaDB.Engine;

internal static class Constants
{
    public const int PageSize = 8192;
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int CipherOverhead = NonceSize + TagSize;
    public const int PayloadSize = PageSize - CipherOverhead;
    public const int PageHeaderSize = 40;
    public const int SlotSize = 4;
    public const int MaxDocumentBytes = 16 * 1024 * 1024;
    public const int MaxKeyBytes = 1024;
    public const int MaxCollectionName = 120;
    /// <summary>On-disk format written by this build. Format 1 is deprecated and read-only.</summary>
    public const ushort FormatVersion = 2;
    /// <summary>Oldest format still accepted on open. Do not write this version.</summary>
    public const ushort MinFormatVersion = 1;
    public const int WalHeaderV1Size = 22;
    public const int WalHeaderV2Size = 32;
    public const long SuperblockPageId = 0;
    public const long CatalogPageId = 1;
    public const long FirstAllocPageId = 2;
    public const int SuperblockCrcOffset = 198;
    public const int SuperblockCrcLength = 400;
    public const int SuperblockMacOffset = 400;
    public const int SuperblockMacSize = 32;

    public static ReadOnlySpan<byte> FileMagic => "NVX1"u8;
    public static ReadOnlySpan<byte> WalMagic => "NVXW"u8;
    public static ReadOnlySpan<byte> VerifierPlaintext => "NVEXA-OK-VERIFY!"u8;

    public static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
}

internal enum PageType : byte
{
    Free = 0,
    Super = 1,
    Catalog = 2,
    Data = 3,
    IndexLeaf = 4,
    IndexInternal = 5,
    SecondaryIndexCatalog = 6
}

[Flags]
internal enum SuperblockFlags : ushort
{
    None = 0,
    Encrypted = 1,
    CompactNeeded = 2,
    IntegrityProtected = 4
}

internal enum WalRecordType : byte
{
    Page = 1,
    Commit = 2
}
