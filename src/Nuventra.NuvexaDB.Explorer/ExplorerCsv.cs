using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Nuventra.NuvexaDB.Explorer;

/// <summary>Spreadsheet CSV for the desktop IDE. Does not change engine import (JSON array).</summary>
public static class ExplorerCsv
{
    public static string FromJsonArray(string jsonArray)
    {
        var node = JsonNode.Parse(string.IsNullOrWhiteSpace(jsonArray) ? "[]" : jsonArray);
        if (node is not JsonArray array)
        {
            throw new InvalidOperationException("CSV export expects a JSON array of documents.");
        }

        return FromDocuments(array.OfType<JsonObject>());
    }

    public static string FromDocuments(IEnumerable<JsonObject> documents)
    {
        var rows = documents.Select(Flatten).ToList();
        var headers = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in rows.SelectMany(r => r.Keys))
        {
            if (seen.Add(key))
            {
                headers.Add(key);
            }
        }

        if (headers.Count == 0)
        {
            return "";
        }

        if (seen.Remove("_id"))
        {
            headers.Remove("_id");
            headers.Insert(0, "_id");
        }

        var text = new StringBuilder();
        text.AppendLine(string.Join(",", headers.Select(Quote)));
        foreach (var row in rows)
        {
            text.AppendLine(string.Join(",", headers.Select(h => Quote(row.GetValueOrDefault(h, "")))));
        }

        return text.ToString();
    }

    public static string ToJsonArray(string csv)
    {
        var table = Parse(csv);
        if (table.Headers.Count == 0)
        {
            return "[]";
        }

        var array = new JsonArray();
        foreach (var row in table.Rows)
        {
            var obj = new JsonObject();
            for (var i = 0; i < table.Headers.Count; i++)
            {
                var header = table.Headers[i];
                if (string.IsNullOrWhiteSpace(header))
                {
                    continue;
                }

                var raw = i < row.Count ? row[i] : "";
                obj[header] = InferNode(raw);
            }

            array.Add(obj);
        }

        return array.ToJsonString();
    }

    public static CsvTable Parse(string csv)
    {
        var lines = SplitRecords(csv ?? "");
        if (lines.Count == 0)
        {
            return new CsvTable([], []);
        }

        var headers = lines[0];
        var rows = lines.Skip(1).Where(r => r.Exists(c => c.Length > 0)).ToList();
        return new CsvTable(headers, rows);
    }

    private static Dictionary<string, string> Flatten(JsonObject document)
    {
        var cells = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in document)
        {
            cells[key] = value is null ? "" : value is JsonValue simple
                ? simple.ToString()
                : value.ToJsonString();
        }

        return cells;
    }

    private static JsonNode? InferNode(string raw)
    {
        var value = raw.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (value is "true" or "false")
        {
            return JsonValue.Create(value == "true");
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
        {
            return JsonValue.Create(whole);
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
        {
            return JsonValue.Create(real);
        }

        if ((value.StartsWith('{') && value.EndsWith('}')) || (value.StartsWith('[') && value.EndsWith(']')))
        {
            try
            {
                return JsonNode.Parse(value);
            }
            catch (System.Text.Json.JsonException)
            {
                // Keep as text when the cell is not valid JSON.
            }
        }

        return JsonValue.Create(raw);
    }

    private static string Quote(string? value)
    {
        var text = value ?? "";
        if (text.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return text;
        }

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static List<List<string>> SplitRecords(string csv)
    {
        var records = new List<List<string>>();
        var field = new StringBuilder();
        var row = new List<string>();
        var quoted = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    records.Add(row);
                    row = [];
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (quoted)
        {
            throw new InvalidOperationException("CSV has an unclosed quote.");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            records.Add(row);
        }

        return records;
    }
}

public sealed record CsvTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);
