using System.Globalization;
using System.Text.Json.Nodes;

namespace Nuventra.NuvexaDB.Tools;

public sealed record TableColumnDefinition(string Name, string Type, string Default, bool Unique);

public sealed record TableDefinition(string Name, IReadOnlyList<TableColumnDefinition> Columns);

public static class TableColumnTypes
{
    public static readonly string[] All = ["TEXT", "INTEGER", "REAL", "BOOLEAN", "DATETIME"];

    public static string Normalize(string? type)
    {
        var value = (type ?? "TEXT").Trim().ToUpperInvariant();
        return All.Contains(value, StringComparer.Ordinal) ? value : "TEXT";
    }

    public static JsonNode DefaultNode(string type, string? defaultValue)
    {
        var raw = defaultValue ?? "";
        return Normalize(type) switch
        {
            "INTEGER" => JsonValue.Create(long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0L),
            "REAL" => JsonValue.Create(double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0d),
            "BOOLEAN" => JsonValue.Create(ParseBoolean(raw)),
            _ => JsonValue.Create(raw)
        };
    }

    public static JsonNode ParseCell(string type, string? text)
    {
        var raw = text ?? "";
        if (raw.Length > 0 && raw[0] is '{' or '[')
        {
            try
            {
                return JsonNode.Parse(raw) ?? DefaultNode(type, raw);
            }
            catch (System.Text.Json.JsonException)
            {
                // Treat as a scalar of the declared type.
            }
        }

        return DefaultNode(type, raw);
    }

    public static bool IsTrue(string? raw) => ParseBoolean(raw ?? "");

    public static bool TryParseDateTime(string? raw, out DateTime value) =>
        DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value)
        || DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out value);

    public static string FormatDateTime(DateTime value) =>
        value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    public static string DisplayDefault(string type, string? defaultValue)
    {
        if (!string.IsNullOrEmpty(defaultValue))
        {
            return defaultValue;
        }

        return Normalize(type) switch
        {
            "INTEGER" or "REAL" => "0",
            "BOOLEAN" => "false",
            _ => ""
        };
    }

    private static bool ParseBoolean(string raw)
    {
        if (bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return raw is "1" || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
