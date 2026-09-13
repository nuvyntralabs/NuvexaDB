using System.Text.Json;

namespace Nuventra.NuvexaDB.Explorer;

public sealed record SavedQuery(string Name, string Text);

internal static class SavedQueryStore
{
    private const int MaxEntries = 40;

    public static IReadOnlyList<SavedQuery> Load()
    {
        try
        {
            var path = FilePath();
            if (!File.Exists(path))
            {
                return [];
            }

            return Normalize(JsonSerializer.Deserialize<List<SavedQuery>>(File.ReadAllText(path)) ?? []);
        }
        catch
        {
            return [];
        }
    }

    public static void Save(IEnumerable<SavedQuery> items)
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

    private static List<SavedQuery> Normalize(IEnumerable<SavedQuery> items) =>
        items
            .Where(q => q is not null && !string.IsNullOrWhiteSpace(q.Name) && !string.IsNullOrWhiteSpace(q.Text))
            .Select(q => new SavedQuery(q.Name.Trim(), q.Text.Trim()))
            .GroupBy(q => q.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(MaxEntries)
            .ToList();

    private static string FilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NuvexaDB",
            "explorer-saved-queries.json");
}
