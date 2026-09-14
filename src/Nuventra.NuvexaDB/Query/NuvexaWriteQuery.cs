using System.Text.RegularExpressions;

namespace Nuventra.NuvexaDB.Query;

/// <summary>Parsed NQL write: <c>db.col.update({ filter }, { $set: … })</c> or <c>db.col.delete({ filter })</c>.</summary>
public sealed class NuvexaWriteQuery
{
    public required string Collection { get; init; }
    public required NuvexaFilter Filter { get; init; }
    public string? UpdateJson { get; init; }
    public bool IsDelete => UpdateJson is null;

    public static bool TryParse(string text, out NuvexaWriteQuery query)
    {
        query = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var update = Regex.Match(trimmed, @"^db\.([A-Za-z0-9_]+)\.update\s*\(");
        if (update.Success)
        {
            var collection = update.Groups[1].Value;
            var open = update.Length - 1;
            var close = NuvexaQuery.FindMatchingParen(trimmed, open);
            var args = trimmed[(open + 1)..close].Trim();
            SplitTwoJsonArgs(args, out var filterJson, out var updateJson);
            query = new NuvexaWriteQuery
            {
                Collection = collection,
                Filter = NuvexaFilter.Parse(string.IsNullOrWhiteSpace(filterJson) ? "{}" : filterJson),
                UpdateJson = FilterParser.RelaxJsObject(string.IsNullOrWhiteSpace(updateJson) ? "{}" : updateJson)
            };
            return true;
        }

        var delete = Regex.Match(trimmed, @"^db\.([A-Za-z0-9_]+)\.delete\s*\(");
        if (!delete.Success)
        {
            return false;
        }

        var delCollection = delete.Groups[1].Value;
        var delOpen = delete.Length - 1;
        var delClose = NuvexaQuery.FindMatchingParen(trimmed, delOpen);
        var filter = trimmed[(delOpen + 1)..delClose].Trim();
        query = new NuvexaWriteQuery
        {
            Collection = delCollection,
            Filter = NuvexaFilter.Parse(string.IsNullOrWhiteSpace(filter) ? "{}" : filter)
        };
        return true;
    }

    private static void SplitTwoJsonArgs(string args, out string first, out string second)
    {
        first = args;
        second = "{}";
        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = 0; i < args.Length; i++)
        {
            var c = args[i];
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

            if (c is '{' or '[')
            {
                depth++;
            }
            else if (c is '}' or ']')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                first = args[..i].Trim();
                second = args[(i + 1)..].Trim();
                return;
            }
        }
    }
}
