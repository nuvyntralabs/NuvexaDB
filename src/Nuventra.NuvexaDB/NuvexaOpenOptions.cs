namespace Nuventra.NuvexaDB;

/// <summary>Options for opening an existing <c>.nvx</c> file.</summary>
public sealed class NuvexaOpenOptions
{
    /// <summary>Encryption passphrase. Required when the file is encrypted.</summary>
    public string? EncryptionKey { get; set; }

    /// <summary>Page cache size in megabytes. Default 16.</summary>
    public int CacheSizeMb { get; set; } = 16;

    /// <summary>Open the file read-only (shared readers are not supported across processes).</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Checkpoint the WAL after this many committed pages. Default 64.</summary>
    public int CheckpointThreshold { get; set; } = 64;

    /// <summary>
    /// When true (default), Open verifies the superblock and allocated pages
    /// (CRC32, and AES-GCM when encrypted) unless the file is larger than
    /// <see cref="IntegrityScanMaxBytes"/>. A mismatch throws
    /// <see cref="NuvexaIntegrityException"/> and the file is not opened.
    /// Pages are still checked when first read.
    /// </summary>
    public bool VerifyIntegrity { get; set; } = true;

    /// <summary>
    /// Full-page integrity scan runs only when the file is this size or smaller.
    /// Default 64 MiB. Set 0 to always scan when <see cref="VerifyIntegrity"/> is true.
    /// </summary>
    public long IntegrityScanMaxBytes { get; set; } = 64L * 1024 * 1024;
}

/// <summary>Options for creating a new <c>.nvx</c> file.</summary>
public sealed class NuvexaCreateOptions
{
    /// <summary>When set, the new file is encrypted with AES-256-GCM and a wrapped DEK.</summary>
    public string? EncryptionKey { get; set; }

    /// <summary>Page cache size in megabytes. Default 16.</summary>
    public int CacheSizeMb { get; set; } = 16;

    /// <summary>Argon2id memory in KiB. Default 16384 (16 MiB, mobile-safe). Desktop explorers may use 65536.</summary>
    public int Argon2MemoryKb { get; set; } = 16 * 1024;

    /// <summary>Argon2id iterations. Default 3.</summary>
    public int Argon2Iterations { get; set; } = 3;

    /// <summary>Argon2id parallelism. Default 2.</summary>
    public int Argon2Parallelism { get; set; } = 2;

    /// <summary>Checkpoint the WAL after this many committed pages. Default 64.</summary>
    public int CheckpointThreshold { get; set; } = 64;

    /// <summary>
    /// On-disk format for a new file. Default is 2 (order-preserving numeric index keys, WAL v2 header).
    /// Set 1 only to reproduce a legacy file.
    /// </summary>
    public ushort FormatVersion { get; set; } = 2;

    /// <summary>64 MiB Argon2id memory for desktop Explorer / CLI creates. Existing files keep their stored KDF parameters.</summary>
    public static NuvexaCreateOptions ForDesktop(string? encryptionKey = null) => new()
    {
        EncryptionKey = encryptionKey,
        Argon2MemoryKb = 64 * 1024
    };
}

/// <summary>Live statistics for an open database.</summary>
public sealed class NuvexaStats
{
    public string Path { get; init; } = "";
    public long FileBytes { get; init; }
    public long WalBytes { get; init; }
    public long PageCount { get; init; }
    public long DocumentCount { get; init; }
    public int CollectionCount { get; init; }
    public int CachedPages { get; init; }
    public int CacheSizeMb { get; init; }
    public bool Encrypted { get; init; }
    public long CommittedLsn { get; init; }
    public bool CompactNeeded { get; init; }
}
