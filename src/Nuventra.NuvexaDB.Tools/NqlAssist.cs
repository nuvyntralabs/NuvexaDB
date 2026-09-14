namespace Nuventra.NuvexaDB.Tools;

/// <summary>NQL keyword and field completions for Data Studio and editors.</summary>
public static class NqlAssist
{
    public static readonly string[] Keywords =
    [
        "find", "aggregate", "update", "delete", "sort", "skip", "limit", "page", "project"
    ];

    public static readonly string[] Operators =
    [
        "$eq", "$ne", "$gt", "$gte", "$lt", "$lte", "$in", "$nin", "$and", "$or",
        "$exists", "$regex", "$set", "$unset", "$inc", "$push", "$pull",
        "$match", "$project", "$sort", "$skip", "$limit", "$count", "$group", "$lookup"
    ];

    public static string PrefixBefore(string text, int caret)
    {
        if (string.IsNullOrEmpty(text) || caret <= 0)
        {
            return "";
        }

        caret = Math.Min(caret, text.Length);
        var start = caret;
        while (start > 0)
        {
            var c = text[start - 1];
            if (char.IsLetterOrDigit(c) || c is '_' or '$')
            {
                start--;
                continue;
            }

            break;
        }

        return text[start..caret];
    }

    public static IReadOnlyList<string> Completions(
        string text,
        int caret,
        IEnumerable<string>? collections = null,
        IEnumerable<string>? fields = null)
    {
        var prefix = PrefixBefore(text, caret);
        return (collections ?? [])
            .Concat(fields ?? [])
            .Concat(Keywords)
            .Concat(Operators)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .Where(s => prefix.Length == 0 || s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToList();
    }
}
