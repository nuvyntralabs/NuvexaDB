using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nuventra.NuvexaDB.Query;

/// <summary>Parsed NQL (Nuvexa Query Language): find / sort / skip / limit / project.</summary>
public sealed class NuvexaQuery
{
    public string Collection { get; init; } = "";
    public NuvexaFilter Filter { get; init; } = NuvexaFilter.And();
    public List<(string Path, bool Ascending)> Sort { get; init; } = [];
    public int Skip { get; init; }
    public int Limit { get; init; }
    public List<string>? Projection { get; init; }

    /// <summary>
    /// Parses <c>db.users.find({ ... }).sort({ lastName: 1 }).limit(20)</c>.
    /// </summary>
    public static NuvexaQuery Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new NuvexaException("Query is empty.");
        }

        var trimmed = text.Trim();
        var prefix = Regex.Match(trimmed, @"^db\.([A-Za-z0-9_]+)\.find\s*\(");
        if (!prefix.Success)
        {
            throw new NuvexaException("Query must look like db.<collection>.find({ ... }).");
        }

        var collection = prefix.Groups[1].Value;
        var open = prefix.Length;
        if (open >= trimmed.Length)
        {
            throw new NuvexaException("Query find() is incomplete.");
        }

        string filterJson;
        int close;
        if (trimmed[open] == ')')
        {
            filterJson = "{}";
            close = open;
        }
        else
        {
            close = FindMatchingParen(trimmed, open - 1);
            filterJson = trimmed[(open)..close].Trim();
            if (string.IsNullOrWhiteSpace(filterJson))
            {
                filterJson = "{}";
            }
        }

        var tail = close + 1 < trimmed.Length ? trimmed[(close + 1)..] : "";
        var query = new NuvexaQuery
        {
            Collection = collection,
            Filter = NuvexaFilter.Parse(filterJson)
        };
        return ApplyTail(query, tail);
    }

    private static NuvexaQuery ApplyTail(NuvexaQuery query, string tail)
    {
        var sort = query.Sort;
        var skip = query.Skip;
        var limit = query.Limit;
        List<string>? projection = query.Projection;

        foreach (Match call in Regex.Matches(tail, @"\.(sort|skip|limit|project|page)\s*\((.*?)\)", RegexOptions.Singleline))
        {
            var name = call.Groups[1].Value;
            var arg = call.Groups[2].Value.Trim();
            switch (name)
            {
                case "sort":
                    sort = ParseSort(arg);
                    break;
                case "skip":
                    skip = int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "limit":
                    limit = int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "page":
                    ApplyPage(arg, ref skip, ref limit);
                    break;
                case "project":
                    projection = ParseProject(arg);
                    break;
            }
        }

        return new NuvexaQuery
        {
            Collection = query.Collection,
            Filter = query.Filter,
            Sort = sort,
            Skip = skip,
            Limit = limit,
            Projection = projection
        };
    }

    /// <summary>
    /// 1-based page. <c>page(2)</c> uses the current limit (or 200). <c>page(2, 50)</c> sets both skip and limit.
    /// </summary>
    private static void ApplyPage(string arg, ref int skip, ref int limit)
    {
        var parts = arg.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 2
            || !int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var page)
            || page < 1)
        {
            throw new NuvexaException("page() needs a 1-based page number, for example page(2) or page(2, 200).");
        }

        var size = limit > 0 ? limit : 200;
        if (parts.Length == 2)
        {
            if (!int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out size)
                || size < 1)
            {
                throw new NuvexaException("page(page, size) size must be a positive number.");
            }
        }

        skip = (page - 1) * size;
        limit = size;
    }

    internal static int FindMatchingParen(string text, int openIndex)
    {
        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = openIndex; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
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
                continue;
            }

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        throw new NuvexaException("Unbalanced parentheses in query.");
    }

    private static List<(string Path, bool Ascending)> ParseSort(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : FilterParser.RelaxJsObject(json));
        return doc.RootElement.EnumerateObject()
            .Select(p => (p.Name, p.Value.ValueKind == JsonValueKind.Number && p.Value.GetInt32() >= 0))
            .ToList();
    }

    private static List<string> ParseProject(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : FilterParser.RelaxJsObject(json));
        return doc.RootElement.EnumerateObject()
            .Where(p => p.Value.ValueKind != JsonValueKind.Number || p.Value.GetInt32() != 0)
            .Select(p => p.Name)
            .ToList();
    }
}

/// <summary>Query explain output.</summary>
public sealed class NuvexaExplainPlan
{
    public string Collection { get; init; } = "";
    public string Strategy { get; init; } = "COLLSCAN";
    public string? IndexName { get; init; }
    public int Examined { get; init; }
    public int Returned { get; init; }
}
