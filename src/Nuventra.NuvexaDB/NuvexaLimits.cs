namespace Nuventra.NuvexaDB;

/// <summary>Documented engine limits. Changing these does not change on-disk format version 2.</summary>
public static class NuvexaLimits
{
    /// <summary>Maximum stored document payload (BSON or legacy JSON).</summary>
    public const int MaxDocumentBytes = Engine.Constants.MaxDocumentBytes;

    /// <summary>Maximum collection name length.</summary>
    public const int MaxCollectionName = Engine.Constants.MaxCollectionName;

    /// <summary>
    /// Default cap for <c>$lookup</c> / <c>$count</c> source collections.
    /// Override per handle with <see cref="NuvexaDatabase.LookupMaxDocuments"/> (0 = unlimited).
    /// </summary>
    public const int DefaultLookupMaxDocuments = 100_000;

    /// <summary>Documents copied per batch during compact.</summary>
    public const int CompactBatchSize = 256;
}
