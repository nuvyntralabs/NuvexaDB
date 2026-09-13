using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nuventra.NuvexaDB.Documents;

namespace Nuventra.NuvexaDB.Query;

internal static class NuvexaAggregate
{
    public static bool TryParse(string text, out string collection, out JsonElement pipeline)
    {
        collection = "";
        pipeline = default;
        var match = Regex.Match(text.Trim(), @"^db\.([A-Za-z0-9_]+)\.aggregate\s*\(", RegexOptions.Singleline);
        if (!match.Success)
        {
            return false;
        }

        collection = match.Groups[1].Value;
        var start = match.Length - 1;
        var close = FindMatching(text.Trim(), start);
        var args = text.Trim()[(start + 1)..close].Trim();
        using var doc = JsonDocument.Parse(FilterParser.RelaxJsObject(args));
        pipeline = doc.RootElement.Clone();
        return true;
    }

    public static async Task<NuvexaQueryResult> RunAsync(NuvexaDatabase db, string collection, JsonElement pipeline, CancellationToken cancellationToken)
    {
        var docs = await db.GetCollection(collection).Find().ToListAsync(cancellationToken).ConfigureAwait(false);
        if (pipeline.ValueKind != JsonValueKind.Array)
        {
            throw new NuvexaException("Aggregation pipeline must be a JSON array.");
        }

        foreach (var stage in pipeline.EnumerateArray())
        {
            var op = stage.EnumerateObject().First();
            docs = op.Name switch
            {
                "$match" => docs.Where(d => NuvexaFilter.Parse(op.Value.GetRawText()).Matches(d.AsElement())).ToList(),
                "$project" => Project(docs, op.Value),
                "$sort" => Sort(docs, op.Value),
                "$skip" => docs.Skip(op.Value.GetInt32()).ToList(),
                "$limit" => docs.Take(op.Value.GetInt32()).ToList(),
                "$count" => Count(docs, op.Value.GetString() ?? "count"),
                "$group" => Group(docs, op.Value),
                "$lookup" => await LookupAsync(db, docs, op.Value, cancellationToken).ConfigureAwait(false),
                _ => throw new NuvexaException($"Unsupported aggregation stage '{op.Name}'.")
            };
        }

        return new NuvexaQueryResult { Collection = collection, Documents = docs };
    }

    private static List<NuvexaDocument> Project(List<NuvexaDocument> docs, JsonElement spec)
    {
        var paths = spec.EnumerateObject()
            .Where(p => p.Value.ValueKind != JsonValueKind.Number || p.Value.GetInt32() != 0)
            .Select(p => p.Name)
            .ToList();
        return docs.Select(d => NuvexaFindFluent.ProjectDocument(d, paths)).ToList();
    }

    private static List<NuvexaDocument> Sort(List<NuvexaDocument> docs, JsonElement spec)
    {
        IOrderedEnumerable<NuvexaDocument>? ordered = null;
        var first = true;
        foreach (var prop in spec.EnumerateObject())
        {
            var path = prop.Name;
            var asc = prop.Value.ValueKind != JsonValueKind.Number || prop.Value.GetInt32() >= 0;
            if (first)
            {
                ordered = docs.OrderBy(d => Key(d, path), Comparer<object?>.Create((a, b) => CompareKey(a, b, asc)));
                first = false;
            }
            else
            {
                ordered = ordered!.ThenBy(d => Key(d, path), Comparer<object?>.Create((a, b) => CompareKey(a, b, asc)));
            }
        }

        return ordered?.ToList() ?? docs;
    }

    private static object? Key(NuvexaDocument doc, string path) =>
        DocumentPath.TryGet(doc.AsElement(), path, out var el) ? el.GetRawText() : null;

    private static int CompareKey(object? a, object? b, bool asc)
    {
        var cmp = string.CompareOrdinal(a as string, b as string);
        return asc ? cmp : -cmp;
    }

    private static List<NuvexaDocument> Count(List<NuvexaDocument> docs, string field) =>
    [
        new NuvexaDocument(new JsonObject { [field] = docs.Count })
    ];

    private static List<NuvexaDocument> Group(List<NuvexaDocument> docs, JsonElement spec)
    {
        if (spec.ValueKind != JsonValueKind.Object || !spec.TryGetProperty("_id", out var idSpec))
        {
            throw new NuvexaException("$group requires an _id expression.");
        }

        var acc = spec.EnumerateObject().Where(p => p.Name != "_id").ToList();
        var buckets = new Dictionary<string, GroupBucket>(StringComparer.Ordinal);
        foreach (var doc in docs)
        {
            var key = GroupKey(doc, idSpec);
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = new GroupBucket(idSpec, key, doc);
                buckets[key] = bucket;
            }

            bucket.Add(doc, acc);
        }

        return buckets.Values.Select(b => b.ToDocument(acc)).ToList();
    }

    private static string GroupKey(NuvexaDocument doc, JsonElement idSpec)
    {
        if (idSpec.ValueKind == JsonValueKind.Null)
        {
            return "";
        }

        if (idSpec.ValueKind == JsonValueKind.String)
        {
            var s = idSpec.GetString() ?? "";
            if (s.StartsWith('$') && DocumentPath.TryGet(doc.AsElement(), s[1..], out var el))
            {
                return el.GetRawText();
            }

            return s;
        }

        return idSpec.GetRawText();
    }

    private sealed class GroupBucket
    {
        private readonly JsonElement _idSpec;
        private readonly string _key;
        private readonly NuvexaDocument _first;
        private int _count;
        private readonly Dictionary<string, double> _sum = new(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _min = new(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _max = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _avgN = new(StringComparer.Ordinal);

        public GroupBucket(JsonElement idSpec, string key, NuvexaDocument first)
        {
            _idSpec = idSpec;
            _key = key;
            _first = first;
        }

        public void Add(NuvexaDocument doc, List<JsonProperty> acc)
        {
            _count++;
            foreach (var prop in acc)
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var op = prop.Value.EnumerateObject().First();
                if (op.Name is "$sum" or "$avg" or "$min" or "$max")
                {
                    var n = AccumulatorNumber(doc, op.Value);
                    switch (op.Name)
                    {
                        case "$sum":
                            _sum[prop.Name] = _sum.GetValueOrDefault(prop.Name) + n;
                            break;
                        case "$avg":
                            _sum[prop.Name] = _sum.GetValueOrDefault(prop.Name) + n;
                            _avgN[prop.Name] = _avgN.GetValueOrDefault(prop.Name) + 1;
                            break;
                        case "$min":
                            _min[prop.Name] = _min.TryGetValue(prop.Name, out var mn) ? Math.Min(mn, n) : n;
                            break;
                        case "$max":
                            _max[prop.Name] = _max.TryGetValue(prop.Name, out var mx) ? Math.Max(mx, n) : n;
                            break;
                    }
                }
            }
        }

        public NuvexaDocument ToDocument(List<JsonProperty> acc)
        {
            JsonNode? idNode;
            if (_idSpec.ValueKind == JsonValueKind.Null)
            {
                idNode = null;
            }
            else if (_idSpec.ValueKind == JsonValueKind.String && (_idSpec.GetString() ?? "").StartsWith('$') &&
                     DocumentPath.TryGet(_first.AsElement(), _idSpec.GetString()![1..], out var idEl))
            {
                idNode = JsonNode.Parse(idEl.GetRawText());
            }
            else
            {
                idNode = JsonNode.Parse(_idSpec.GetRawText());
            }

            var obj = new JsonObject { ["_id"] = idNode };
            foreach (var prop in acc)
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var op = prop.Value.EnumerateObject().First();
                obj[prop.Name] = op.Name switch
                {
                    "$sum" => _sum.GetValueOrDefault(prop.Name),
                    "$min" => _min.GetValueOrDefault(prop.Name),
                    "$max" => _max.GetValueOrDefault(prop.Name),
                    "$avg" => _avgN.TryGetValue(prop.Name, out var n) && n > 0
                        ? _sum.GetValueOrDefault(prop.Name) / n
                        : 0,
                    "$first" => FieldNode(_first, op.Value),
                    _ => throw new NuvexaException($"Unsupported $group accumulator '{op.Name}'.")
                };
            }

            _ = _count;
            _ = _key;
            return new NuvexaDocument(obj);
        }
    }

    private static JsonNode? FieldNode(NuvexaDocument doc, JsonElement spec)
    {
        if (spec.ValueKind == JsonValueKind.String && (spec.GetString() ?? "").StartsWith('$') &&
            DocumentPath.TryGet(doc.AsElement(), spec.GetString()![1..], out var el))
        {
            return JsonNode.Parse(el.GetRawText());
        }

        return JsonNode.Parse(spec.GetRawText());
    }

    private static double AccumulatorNumber(NuvexaDocument doc, JsonElement spec)
    {
        if (spec.ValueKind == JsonValueKind.Number && spec.TryGetDouble(out var literal))
        {
            return literal;
        }

        if (spec.ValueKind == JsonValueKind.String)
        {
            var s = spec.GetString() ?? "";
            if (s.StartsWith('$') && DocumentPath.TryGet(doc.AsElement(), s[1..], out var el) &&
                DocumentPath.TryNumber(el, out var n))
            {
                return n;
            }

            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return 0;
    }

    private static async Task<List<NuvexaDocument>> LookupAsync(NuvexaDatabase db, List<NuvexaDocument> docs, JsonElement spec, CancellationToken cancellationToken)
    {
        var from = spec.GetProperty("from").GetString() ?? throw new NuvexaException("$lookup.from is required.");
        var local = spec.GetProperty("localField").GetString() ?? throw new NuvexaException("$lookup.localField is required.");
        var foreignField = spec.GetProperty("foreignField").GetString() ?? throw new NuvexaException("$lookup.foreignField is required.");
        var asField = spec.GetProperty("as").GetString() ?? "joined";
        var foreignCol = db.GetCollection(from);
        if (db.LookupMaxDocuments > 0 && foreignCol.Count > db.LookupMaxDocuments)
        {
            throw new NuvexaException(
                $"$lookup from '{from}' has {foreignCol.Count} documents; the cap is {db.LookupMaxDocuments}. Set LookupMaxDocuments to 0 to disable.");
        }

        var foreignDocs = await foreignCol.Find().ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var doc in docs)
        {
            DocumentPath.TryGet(doc.AsElement(), local, out var localVal);
            var matches = foreignDocs.Where(f =>
            {
                DocumentPath.TryGet(f.AsElement(), foreignField, out var fv);
                return localVal.ValueKind != JsonValueKind.Undefined && DocumentPath.EqualsValue(localVal, fv);
            }).Select(f => JsonNode.Parse(f.ToJson())).ToArray();
            var arr = new JsonArray();
            foreach (var node in matches)
            {
                arr.Add(node);
            }

            doc.Root[asField] = arr;
        }

        return docs;
    }

    private static int FindMatching(string text, int openIndex)
    {
        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = openIndex; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') { inString = false; }
                continue;
            }

            if (c == '"') { inString = true; continue; }
            if (c == '(') { depth++; }
            else if (c == ')')
            {
                depth--;
                if (depth == 0) { return i; }
            }
        }

        throw new NuvexaException("Unbalanced parentheses in aggregate().");
    }
}
