using System.Text.Json;
using System.Text.RegularExpressions;
using Nuventra.NuvexaDB.Documents;

namespace Nuventra.NuvexaDB.Query;

public enum NuvexaFilterKind
{
    Eq, Ne, Gt, Gte, Lt, Lte, In, Nin, Exists, Regex, And, Or
}

/// <summary>Mongo-inspired filter tree. Prefer factory methods or <see cref="Parse"/>.</summary>
public sealed class NuvexaFilter
{
    public NuvexaFilterKind Kind { get; }
    public string? Path { get; }
    public JsonElement[] Values { get; }
    public NuvexaFilter[] Children { get; }
    public string? Pattern { get; }

    private NuvexaFilter(NuvexaFilterKind kind, string? path, JsonElement[] values, NuvexaFilter[] children, string? pattern)
    {
        Kind = kind;
        Path = path;
        Values = values;
        Children = children;
        Pattern = pattern;
    }

    public static NuvexaFilter Eq(string path, object? value) => Cmp(NuvexaFilterKind.Eq, path, value);
    public static NuvexaFilter Ne(string path, object? value) => Cmp(NuvexaFilterKind.Ne, path, value);
    public static NuvexaFilter Gt(string path, object value) => Cmp(NuvexaFilterKind.Gt, path, value);
    public static NuvexaFilter Gte(string path, object value) => Cmp(NuvexaFilterKind.Gte, path, value);
    public static NuvexaFilter Lt(string path, object value) => Cmp(NuvexaFilterKind.Lt, path, value);
    public static NuvexaFilter Lte(string path, object value) => Cmp(NuvexaFilterKind.Lte, path, value);
    public static NuvexaFilter Exists(string path, bool exists = true) =>
        new(NuvexaFilterKind.Exists, path, [ToElement(exists)], [], null);

    public static NuvexaFilter Regex(string path, string pattern) =>
        new(NuvexaFilterKind.Regex, path, [], [], pattern);

    public static NuvexaFilter In(string path, params object?[] values) =>
        new(NuvexaFilterKind.In, path, values.Select(ToElement).ToArray(), [], null);

    public static NuvexaFilter Nin(string path, params object?[] values) =>
        new(NuvexaFilterKind.Nin, path, values.Select(ToElement).ToArray(), [], null);

    public static NuvexaFilter And(params NuvexaFilter[] filters) =>
        new(NuvexaFilterKind.And, null, [], filters, null);

    public static NuvexaFilter Or(params NuvexaFilter[] filters) =>
        new(NuvexaFilterKind.Or, null, [], filters, null);

    public static NuvexaFilter Parse(string json) => FilterParser.Parse(json);

    public bool Matches(JsonElement document)
    {
        return Kind switch
        {
            NuvexaFilterKind.And => Children.All(c => c.Matches(document)),
            NuvexaFilterKind.Or => Children.Any(c => c.Matches(document)),
            NuvexaFilterKind.Exists => DocumentPath.TryGet(document, Path!, out _) == Values[0].GetBoolean(),
            NuvexaFilterKind.Regex => MatchRegex(document),
            NuvexaFilterKind.In => MatchIn(document, not: false),
            NuvexaFilterKind.Nin => MatchIn(document, not: true),
            _ => MatchCompare(document)
        };
    }

    public string? EqualityPath => Kind == NuvexaFilterKind.Eq ? Path : null;

    public JsonElement? EqualityValue => Kind == NuvexaFilterKind.Eq && Values.Length > 0 ? Values[0] : null;

    private bool MatchCompare(JsonElement document)
    {
        if (!DocumentPath.TryGet(document, Path!, out var actual))
        {
            return Kind == NuvexaFilterKind.Ne;
        }

        var cmp = DocumentPath.Compare(actual, Values[0]);
        return Kind switch
        {
            NuvexaFilterKind.Eq => cmp == 0,
            NuvexaFilterKind.Ne => cmp != 0,
            NuvexaFilterKind.Gt => cmp > 0,
            NuvexaFilterKind.Gte => cmp >= 0,
            NuvexaFilterKind.Lt => cmp < 0,
            NuvexaFilterKind.Lte => cmp <= 0,
            _ => false
        };
    }

    private bool MatchIn(JsonElement document, bool not)
    {
        if (!DocumentPath.TryGet(document, Path!, out var actual))
        {
            return not;
        }

        var found = Values.Any(v => DocumentPath.EqualsValue(actual, v));
        return not ? !found : found;
    }

    private bool MatchRegex(JsonElement document)
    {
        if (!DocumentPath.TryGet(document, Path!, out var actual) || actual.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(actual.GetString() ?? "", Pattern ?? "", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static NuvexaFilter Cmp(NuvexaFilterKind kind, string path, object? value) =>
        new(kind, path, [ToElement(value)], [], null);

    internal static JsonElement ToElement(object? value)
    {
        if (value is JsonElement el)
        {
            return el.Clone();
        }

        return JsonSerializer.SerializeToElement(value).Clone();
    }
}
