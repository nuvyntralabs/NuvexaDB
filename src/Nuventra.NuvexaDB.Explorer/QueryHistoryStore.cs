using System.Text.Json;

namespace Nuventra.NuvexaDB.Explorer;

internal static class QueryHistoryStore
{
    private const int MaxEntries = 20;

    public static IReadOnlyList<string> Load()
    {
        try
        {
            var path = FilePath();
            if (!File.Exists(path))
            {
                return [];
            }

            var items = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? [];
            return items
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(MaxEntries)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static void Save(IEnumerable<string> items)
    {
        try
        {
            var path = FilePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var list = items
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(MaxEntries)
                .ToList();
            File.WriteAllText(path, JsonSerializer.Serialize(list));
        }
        catch
        {
            // Preference file is optional.
        }
    }

    private static string FilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NuvexaDB",
            "explorer-query-history.json");
}
