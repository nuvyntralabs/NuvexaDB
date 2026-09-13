using System.Globalization;
using System.Text.Json.Nodes;

namespace Nuventra.NuvexaDB.Explorer;

/// <summary>Observed field shape from a document sample (Compass-style Schema tab).</summary>
public sealed class SchemaSampleField
{
    public SchemaSampleField(string name, string types, int present, int total, string samples)
    {
        Name = name;
        Types = types;
        Present = present;
        Total = total;
        Missing = Math.Max(0, total - present);
        Coverage = total == 0 ? "0%" : ((present * 100) / total).ToString(CultureInfo.InvariantCulture) + "%";
        Samples = samples;
    }

    public string Name { get; }
    public string Types { get; }
    public int Present { get; }
    public int Missing { get; }
    public int Total { get; }
    public string Coverage { get; }
    public string Samples { get; }
}

public static class SchemaSampler
{
    public static IReadOnlyList<SchemaSampleField> FromJsonArray(string jsonArray, int sampleValues = 3)
    {
        var node = JsonNode.Parse(string.IsNullOrWhiteSpace(jsonArray) ? "[]" : jsonArray);
        if (node is not JsonArray array)
        {
            throw new InvalidOperationException("Schema sample expects a JSON array of documents.");
        }

        return FromDocuments(array.OfType<JsonObject>(), sampleValues);
    }

    public static IReadOnlyList<SchemaSampleField> FromDocuments(
        IEnumerable<JsonObject> documents,
        int sampleValues = 3)
    {
        var docs = documents.ToList();
        var fields = new Dictionary<string, FieldAcc>(StringComparer.Ordinal);
        foreach (var doc in docs)
        {
            foreach (var (key, value) in doc)
            {
                if (!fields.TryGetValue(key, out var acc))
                {
                    acc = new FieldAcc();
                    fields[key] = acc;
                }

                acc.Present++;
                acc.Types.Add(TypeName(value));
                if (acc.Samples.Count < sampleValues)
                {
                    var text = SampleText(value);
                    if (text.Length > 0 && acc.Samples.TrueForAll(s => !string.Equals(s, text, StringComparison.Ordinal)))
                    {
                        acc.Samples.Add(text);
                    }
                }
            }
        }

        return fields
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new SchemaSampleField(
                p.Key,
                p.Value.Types.Count == 0 ? "missing" : string.Join(", ", p.Value.Types.Order(StringComparer.Ordinal)),
                p.Value.Present,
                docs.Count,
                string.Join(", ", p.Value.Samples)))
            .ToList();
    }

    private static string TypeName(JsonNode? value) => value switch
    {
        null => "null",
        JsonArray => "array",
        JsonObject => "object",
        JsonValue v when v.TryGetValue<bool>(out _) => "boolean",
        JsonValue v when v.TryGetValue<string>(out _) => "string",
        JsonValue => "number",
        _ => "unknown"
    };

    private static string SampleText(JsonNode? value)
    {
        if (value is null)
        {
            return "null";
        }

        var text = value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value.ToJsonString();
        return text.Length <= 48 ? text : text[..45] + "...";
    }

    private sealed class FieldAcc
    {
        public int Present { get; set; }
        public HashSet<string> Types { get; } = new(StringComparer.Ordinal);
        public List<string> Samples { get; } = [];
    }
}
