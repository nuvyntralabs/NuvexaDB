using System.Globalization;
using System.Text.Json;

namespace Nuventra.NuvexaDB.Documents;

internal static class DocumentPath
{
    public static bool TryGet(JsonElement root, string path, out JsonElement value)
    {
        value = default;
        var current = root;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                return false;
            }
        }

        value = current;
        return true;
    }

    public static int Compare(JsonElement a, JsonElement b)
    {
        if (TryNumber(a, out var an) && TryNumber(b, out var bn))
        {
            return an.CompareTo(bn);
        }

        if (a.ValueKind == JsonValueKind.String && b.ValueKind == JsonValueKind.String)
        {
            return string.CompareOrdinal(a.GetString(), b.GetString());
        }

        if (a.ValueKind == JsonValueKind.True || a.ValueKind == JsonValueKind.False)
        {
            if (b.ValueKind == JsonValueKind.True || b.ValueKind == JsonValueKind.False)
            {
                return a.GetBoolean().CompareTo(b.GetBoolean());
            }
        }

        if (a.ValueKind == JsonValueKind.Null && b.ValueKind == JsonValueKind.Null)
        {
            return 0;
        }

        return string.CompareOrdinal(a.GetRawText(), b.GetRawText());
    }

    public static bool EqualsValue(JsonElement a, JsonElement b) => Compare(a, b) == 0;

    public static bool TryNumber(JsonElement el, out double value)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out value))
        {
            return true;
        }

        if (el.ValueKind == JsonValueKind.String &&
            double.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    internal const char CompoundSeparator = '\u001f';

    public static byte[] IndexKey(JsonElement value, string documentId) =>
        Engine.Constants.Utf8.GetBytes(IndexValuePrefix(value) + "\0" + documentId);

    public static byte[] IndexScanPrefix(JsonElement value) =>
        Engine.Constants.Utf8.GetBytes(IndexValuePrefix(value) + "\0");

    public static byte[] IndexScanPrefixSuccessor(JsonElement value)
    {
        var prefix = IndexScanPrefix(value);
        return Successor(prefix);
    }

    public static byte[] CompoundIndexKey(IReadOnlyList<JsonElement> values, string documentId) =>
        Engine.Constants.Utf8.GetBytes(CompoundValuePrefix(values) + "\0" + documentId);

    public static byte[] CompoundScanPrefix(IReadOnlyList<JsonElement> values) =>
        Engine.Constants.Utf8.GetBytes(CompoundValuePrefix(values) + "\0");

    public static byte[] CompoundScanPrefixSuccessor(IReadOnlyList<JsonElement> values) =>
        Successor(CompoundScanPrefix(values));

    /// <summary>Inclusive lower bound for the first field of a compound index.</summary>
    public static byte[] CompoundFirstFieldPrefix(JsonElement value) =>
        Engine.Constants.Utf8.GetBytes(IndexValuePrefix(value) + CompoundSeparator);

    public static byte[] CompoundFirstFieldPrefixSuccessor(JsonElement value) =>
        Successor(CompoundFirstFieldPrefix(value));

    public static string JoinIndexPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            throw new NuvexaException("A compound index needs at least one field.");
        }

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new NuvexaException("Index field paths cannot be empty.");
            }
        }

        return string.Join(CompoundSeparator, paths);
    }

    public static string[] SplitIndexPaths(string fieldPath) =>
        string.IsNullOrEmpty(fieldPath)
            ? []
            : fieldPath.Split(CompoundSeparator, StringSplitOptions.RemoveEmptyEntries);

    public static string DisplayIndexPath(string fieldPath) => string.Join(",", SplitIndexPaths(fieldPath));

    public static bool IsCompoundIndexPath(string fieldPath) => fieldPath.Contains(CompoundSeparator);

    private static string CompoundValuePrefix(IReadOnlyList<JsonElement> values)
    {
        var parts = new string[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            parts[i] = IndexValuePrefix(values[i]);
        }

        return string.Join(CompoundSeparator, parts);
    }

    private static byte[] Successor(byte[] prefix)
    {
        var next = (byte[])prefix.Clone();
        next[^1] = (byte)(next[^1] + 1);
        return next;
    }

    private static string IndexValuePrefix(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetDouble(out var n) => "n:" + n.ToString("G17", CultureInfo.InvariantCulture),
        JsonValueKind.String => "s:" + value.GetString(),
        JsonValueKind.True => "b:1",
        JsonValueKind.False => "b:0",
        JsonValueKind.Null => "z:",
        _ => "j:" + value.GetRawText()
    };

    public static byte[] IdKey(string id) => Engine.Constants.Utf8.GetBytes(id);
}
