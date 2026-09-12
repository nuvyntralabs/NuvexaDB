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
