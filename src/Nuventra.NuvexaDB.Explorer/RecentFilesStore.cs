using System.Text.Json;

namespace Nuventra.NuvexaDB.Explorer;

internal static class RecentFilesStore
{
    private const int MaxEntries = 12;

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
            return Normalize(items);
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

            File.WriteAllText(path, JsonSerializer.Serialize(Normalize(items)));
        }
        catch
        {
            // Preference file is optional.
        }
    }

    private static List<string> Normalize(IEnumerable<string> items) =>
        items
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s =>
            {
                try
                {
                    return Path.GetFullPath(s.Trim());
                }
                catch
                {
                    return s.Trim();
                }
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxEntries)
            .ToList();

    private static string FilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NuvexaDB",
            "explorer-recent-files.json");
}
