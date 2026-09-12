using System.Text.Json;

namespace Nuventra.NuvexaDB.Query;

internal static class FilterParser
{
    public static NuvexaFilter Parse(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : RelaxJsObject(json));
        return ParseObject(doc.RootElement);
    }

    internal static string RelaxJsObject(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length + 16);
        var inString = false;
        var escape = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                sb.Append(c);
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                sb.Append(c);
                continue;
            }

            if ((char.IsLetter(c) || c == '_' || c == '$') && (i == 0 || !char.IsLetterOrDigit(text[i - 1])))
            {
                var start = i;
                while (i + 1 < text.Length && (char.IsLetterOrDigit(text[i + 1]) || text[i + 1] is '_' or '$' or '.'))
                {
                    i++;
                }

                var ident = text[start..(i + 1)];
                var j = i + 1;
                while (j < text.Length && char.IsWhiteSpace(text[j]))
                {
                    j++;
                }

                if (j < text.Length && text[j] == ':')
                {
                    sb.Append('"').Append(ident).Append('"');
                    continue;
                }

                sb.Append(ident);
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static NuvexaFilter ParseObject(JsonElement obj)
    {
        var parts = new List<NuvexaFilter>();
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Name == "$and")
            {
                parts.Add(NuvexaFilter.And(prop.Value.EnumerateArray().Select(ParseObject).ToArray()));
                continue;
            }

            if (prop.Name == "$or")
            {
                parts.Add(NuvexaFilter.Or(prop.Value.EnumerateArray().Select(ParseObject).ToArray()));
                continue;
            }

            parts.Add(ParseField(prop.Name, prop.Value));
        }

        return parts.Count switch
        {
            0 => NuvexaFilter.And(),
            1 => parts[0],
            _ => NuvexaFilter.And(parts.ToArray())
        };
    }

    private static NuvexaFilter ParseField(string path, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.EnumerateObject().Any(p => p.Name.StartsWith('$')))
        {
            return NuvexaFilter.Eq(path, value.Clone());
        }

        var filters = new List<NuvexaFilter>();
        foreach (var op in value.EnumerateObject())
        {
            filters.Add(op.Name switch
            {
                "$eq" => NuvexaFilter.Eq(path, op.Value.Clone()),
                "$ne" => NuvexaFilter.Ne(path, op.Value.Clone()),
                "$gt" => NuvexaFilter.Gt(path, op.Value.Clone()),
                "$gte" => NuvexaFilter.Gte(path, op.Value.Clone()),
                "$lt" => NuvexaFilter.Lt(path, op.Value.Clone()),
                "$lte" => NuvexaFilter.Lte(path, op.Value.Clone()),
                "$in" => NuvexaFilter.In(path, op.Value.EnumerateArray().Select(v => (object)v.Clone()).ToArray()),
                "$nin" => NuvexaFilter.Nin(path, op.Value.EnumerateArray().Select(v => (object)v.Clone()).ToArray()),
                "$exists" => NuvexaFilter.Exists(path, op.Value.ValueKind != JsonValueKind.False),
                "$regex" => NuvexaFilter.Regex(path, op.Value.GetString() ?? ""),
                _ => throw new NuvexaException($"Unsupported filter operator '{op.Name}'.")
            });
        }

        return filters.Count == 1 ? filters[0] : NuvexaFilter.And(filters.ToArray());
    }
}
