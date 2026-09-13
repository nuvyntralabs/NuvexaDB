using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nuventra.NuvexaDB.Query;

namespace Nuventra.NuvexaDB;

/// <summary>Fluent find builder (sort / skip / limit / project).</summary>
public sealed class NuvexaFindFluent
{
    private readonly NuvexaCollection _collection;
    private readonly NuvexaFilter _filter;
    private readonly List<(string Path, bool Ascending)> _sort = [];
    private int _skip;
    private int _limit;
    private List<string>? _projection;

    internal NuvexaFindFluent(NuvexaCollection collection, NuvexaFilter filter)
    {
        _collection = collection;
        _filter = filter;
    }

    public NuvexaFindFluent Sort(string path, bool ascending = true)
    {
        _sort.Add((path, ascending));
        return this;
    }

    public NuvexaFindFluent Skip(int skip)
    {
        _skip = skip;
        return this;
    }

    public NuvexaFindFluent Limit(int limit)
    {
        _limit = limit;
        return this;
    }

    public NuvexaFindFluent Project(params string[] paths)
    {
        _projection = paths.ToList();
        return this;
    }

    public Task<List<NuvexaDocument>> ToListAsync(CancellationToken cancellationToken = default) =>
        _collection.ExecuteFindAsync(_filter, _sort, _skip, _limit, _projection, explain: false, cancellationToken);

    /// <summary>
    /// Yields matching documents in pages so callers can stream without one giant list.
    /// Existing <see cref="ToListAsync"/> behavior is unchanged.
    /// </summary>
    public async IAsyncEnumerable<NuvexaDocument> ToAsyncEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const int batch = NuvexaLimits.CompactBatchSize;
        var skip = _skip;
        var remaining = _limit;
        while (true)
        {
            var take = remaining == 0 ? batch : Math.Min(batch, remaining);
            var chunk = await _collection.ExecuteFindAsync(
                _filter, _sort, skip, take, _projection, explain: false, cancellationToken).ConfigureAwait(false);
            if (chunk.Count == 0)
            {
                yield break;
            }

            foreach (var doc in chunk)
            {
                yield return doc;
            }

            skip += chunk.Count;
            if (remaining > 0)
            {
                remaining -= chunk.Count;
                if (remaining <= 0)
                {
                    yield break;
                }
            }

            if (chunk.Count < take)
            {
                yield break;
            }
        }
    }

    public Task<NuvexaExplainPlan> ExplainAsync(CancellationToken cancellationToken = default) =>
        _collection.ExplainAsync(_filter, _sort, _skip, _limit, cancellationToken);

    internal static NuvexaDocument ProjectDocument(NuvexaDocument document, List<string>? projection)
    {
        if (projection is null || projection.Count == 0)
        {
            return document;
        }

        var obj = new JsonObject { ["_id"] = document.Id };
        var el = document.AsElement();
        foreach (var path in projection)
        {
            if (Documents.DocumentPath.TryGet(el, path, out var value))
            {
                obj[path] = JsonNode.Parse(value.GetRawText());
            }
        }

        return new NuvexaDocument(obj);
    }
}
