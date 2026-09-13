using System.Globalization;
using System.Text.Json.Nodes;

namespace Nuventra.NuvexaDB.Tools;

/// <summary>Builds the same browse filter text <see cref="ExplorerSession.BrowsePageAsync"/> already accepts.</summary>
public static class BrowseFilterBuilder
{
    public static IReadOnlyList<string> Operators { get; } =
        ["equals", "not equals", ">", ">=", "<", "<=", "contains", "exists"];

    public static string Build(string? field, string? op, string? value)
    {
        field = (field ?? "").Trim();
        op = string.IsNullOrWhiteSpace(op) ? "equals" : op.Trim();
        value ??= "";
        if (field.Length == 0)
        {
            throw new InvalidOperationException("Choose a field for the filter.");
        }

        if (field.Any(ch => !char.IsLetterOrDigit(ch) && ch is not '_' and not '.'))
        {
            throw new InvalidOperationException("Field names may contain letters, digits, underscore, or dots.");
        }

        return op switch
        {
            "equals" or "=" or "==" => EqualsFilter(field, value),
            "not equals" or "!=" or "<>" => Wrap(field, "$ne", value),
            ">" => Wrap(field, "$gt", value),
            ">=" => Wrap(field, "$gte", value),
            "<" => Wrap(field, "$lt", value),
            "<=" => Wrap(field, "$lte", value),
            "contains" => RegexFilter(field, value),
            "exists" => "{ " + field + ": { $exists: true } }",
            _ => throw new InvalidOperationException($"Unknown filter operator '{op}'.")
        };
    }

    public static IReadOnlyList<DocumentRow> FindInPage(IEnumerable<DocumentRow> rows, string? text)
    {
        var needle = (text ?? "").Trim();
        if (needle.Length == 0)
        {
            return rows.ToList();
        }

        return rows.Where(row =>
            row.Id.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            row.Json.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            row.Cells.Keys.Any(key => row.Cells[key].Contains(needle, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static string EqualsFilter(string field, string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return "{ " + field + ": \"\" }";
        }

        return field + ": " + trimmed;
    }

    private static string Wrap(string field, string op, string value) =>
        "{ " + field + ": { " + op + ": " + JsonToken(value) + " } }";

    private static string RegexFilter(string field, string value)
    {
        var pattern = (value ?? "").Trim();
        if (pattern.Length == 0)
        {
            throw new InvalidOperationException("Enter text to match.");
        }

        return "{ " + field + ": { $regex: " + JsonValue.Create(pattern)!.ToJsonString() + " } }";
    }

    private static string JsonToken(string value)
    {
        var trimmed = value.Trim();
        if (trimmed is "true" or "false" or "null")
        {
            return trimmed;
        }

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
            double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return trimmed;
        }

        return JsonValue.Create(value)!.ToJsonString();
    }
}
