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

    public static byte[] IndexKey(JsonElement value, string documentId)
    {
        var prefix = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var n) => "n:" + n.ToString("G17", CultureInfo.InvariantCulture),
            JsonValueKind.String => "s:" + value.GetString(),
            JsonValueKind.True => "b:1",
            JsonValueKind.False => "b:0",
            JsonValueKind.Null => "z:",
            _ => "j:" + value.GetRawText()
        };
        return Engine.Constants.Utf8.GetBytes(prefix + "\0" + documentId);
    }

    public static byte[] IdKey(string id) => Engine.Constants.Utf8.GetBytes(id);
}
