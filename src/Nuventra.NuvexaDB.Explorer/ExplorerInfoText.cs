using Nuventra.NuvexaDB;

namespace Nuventra.NuvexaDB.Explorer;

public static class ExplorerInfoText
{
    public static string FormatDatabaseProperties(NuvexaStats stats) =>
        $"""
        Path: {stats.Path}
        Encrypted: {stats.Encrypted}
        Format: {stats.FormatVersion}{(stats.FormatVersion == 1 ? " (deprecated, still readable)" : "")}
        Collections: {stats.CollectionCount}
        Documents: {stats.DocumentCount}
        File size: {stats.FileBytes} bytes
        WAL: {stats.WalBytes} bytes
        Pages: {stats.PageCount}
        Cache: {stats.CachedPages} pages ({stats.CacheSizeMb} MB)
        Committed LSN: {stats.CommittedLsn}
        Compact needed: {stats.CompactNeeded}
        """;
}
