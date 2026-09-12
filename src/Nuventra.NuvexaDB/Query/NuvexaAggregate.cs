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

    private static async Task<List<NuvexaDocument>> LookupAsync(NuvexaDatabase db, List<NuvexaDocument> docs, JsonElement spec, CancellationToken cancellationToken)
    {
        var from = spec.GetProperty("from").GetString() ?? throw new NuvexaException("$lookup.from is required.");
        var local = spec.GetProperty("localField").GetString() ?? throw new NuvexaException("$lookup.localField is required.");
        var foreign = spec.GetProperty("foreignField").GetString() ?? throw new NuvexaException("$lookup.foreignField is required.");
        var asField = spec.GetProperty("as").GetString() ?? "joined";
        var foreignDocs = await db.GetCollection(from).Find().ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var doc in docs)
        {
            DocumentPath.TryGet(doc.AsElement(), local, out var localVal);
            var matches = foreignDocs.Where(f =>
            {
                DocumentPath.TryGet(f.AsElement(), foreign, out var fv);
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
