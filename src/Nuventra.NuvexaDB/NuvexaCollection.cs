using System.Text.Json;
using Nuventra.NuvexaDB.Documents;
using Nuventra.NuvexaDB.Engine;
using Nuventra.NuvexaDB.Query;

namespace Nuventra.NuvexaDB;

/// <summary>Untyped document collection.</summary>
public sealed class NuvexaCollection
{
    private readonly NuvexaDatabase _db;
    internal CollectionMeta Meta { get; }

    internal NuvexaCollection(NuvexaDatabase db, CollectionMeta meta)
    {
        _db = db;
        Meta = meta;
    }

    public string Name => Meta.Name;
    public long Count => Meta.Count;
    private ushort WriteFormatVersion => _db.WriteFormatVersion;

    public Task<string> InsertAsync(NuvexaDocument document, CancellationToken cancellationToken = default) =>
        _db.WriteAsync(() => InsertCore(document), cancellationToken);

    public Task InsertManyAsync(IEnumerable<NuvexaDocument> documents, CancellationToken cancellationToken = default) =>
        _db.WriteAsync(() =>
        {
            foreach (var document in documents)
            {
                InsertCore(document);
            }
        }, cancellationToken);

    public Task<NuvexaDocument?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
        _db.ReadAsync(() => FindByIdCore(id), cancellationToken);

    public Task ReplaceAsync(NuvexaDocument document, CancellationToken cancellationToken = default) =>
        _db.WriteAsync(() =>
        {
            document.EnsureId();
            if (FindByIdCore(document.Id) is null)
            {
                throw new NuvexaException($"No document with _id '{document.Id}'.");
            }

            ReplaceCore(document);
        }, cancellationToken);

    public Task<bool> DeleteByIdAsync(string id, CancellationToken cancellationToken = default) =>
        _db.WriteAsync(() =>
        {
            if (FindByIdCore(id) is null)
            {
                return false;
            }

            DeleteByIdCore(id);
            return true;
        }, cancellationToken);

    public NuvexaFindFluent Find(NuvexaFilter? filter = null) => new(this, filter ?? NuvexaFilter.And());

    public NuvexaFindFluent Find(string filterJson) => new(this, NuvexaFilter.Parse(filterJson));

    public Task<long> DeleteAsync(NuvexaFilter filter, CancellationToken cancellationToken = default) =>
        _db.WriteAsync(() =>
        {
            var matches = ExecuteFindCore(filter, [], 0, 0, null, out _).ToList();
            foreach (var doc in matches)
            {
                DeleteByIdCore(doc.Id);
            }

            return (long)matches.Count;
        }, cancellationToken);

    public Task<long> UpdateAsync(NuvexaFilter filter, string updateJson, CancellationToken cancellationToken = default) =>
        _db.WriteAsync(() =>
        {
            var matches = ExecuteFindCore(filter, [], 0, 0, null, out _).ToList();
            foreach (var doc in matches)
            {
                var updated = UpdateOperations.Apply(doc, updateJson);
                ReplaceCore(updated);
            }

            return (long)matches.Count;
        }, cancellationToken);

    public Task EnsureIndexAsync(string fieldPath, string? name = null, bool unique = false, CancellationToken cancellationToken = default) =>
        _db.WriteAsync(() => EnsureIndexCore(fieldPath, name ?? fieldPath, unique), cancellationToken);

    public Task EnsureIndexAsync(IReadOnlyList<string> fieldPaths, string? name = null, bool unique = false, CancellationToken cancellationToken = default)
    {
        var stored = DocumentPath.JoinIndexPaths(fieldPaths);
        var indexName = name ?? string.Join("_", fieldPaths);
        return _db.WriteAsync(() => EnsureIndexCore(stored, indexName, unique), cancellationToken);
    }

    public Task<IReadOnlyList<NuvexaIndexInfo>> ListIndexesAsync(CancellationToken cancellationToken = default) =>
        _db.ReadAsync(() =>
        {
            var indexes = Catalog.LoadSecondary(_db.Store, Meta)
                .Select(i => new NuvexaIndexInfo(
                    i.Name,
                    DocumentPath.DisplayIndexPath(i.FieldPath),
                    i.Unique,
                    DocumentPath.SplitIndexPaths(i.FieldPath)))
                .ToList();
            indexes.Insert(0, new NuvexaIndexInfo("_id_", "_id", Unique: true, ["_id"]));
            return (IReadOnlyList<NuvexaIndexInfo>)indexes;
        }, cancellationToken);

    public Task DropIndexAsync(string name, CancellationToken cancellationToken = default) =>
        _db.WriteAsync(() =>
        {
            var indexes = Catalog.LoadSecondary(_db.Store, Meta);
            indexes.RemoveAll(i => i.Name == name);
            Catalog.SaveSecondary(_db.Store, Meta, indexes);
            _db.PersistCatalog();
        }, cancellationToken);

    internal Task<List<NuvexaDocument>> ExecuteFindAsync(
        NuvexaFilter filter,
        List<(string Path, bool Ascending)> sort,
        int skip,
        int limit,
        List<string>? projection,
        bool explain,
        CancellationToken cancellationToken) =>
        _db.ReadAsync(() =>
        {
            var rows = ExecuteFindCore(filter, sort, skip, limit, projection, out _);
            return rows;
        }, cancellationToken);

    internal Task<NuvexaExplainPlan> ExplainAsync(
        NuvexaFilter filter,
        List<(string Path, bool Ascending)> sort,
        int skip,
        int limit,
        CancellationToken cancellationToken) =>
        _db.ReadAsync(() =>
        {
            ExecuteFindCore(filter, sort, skip, limit, null, out var plan);
            return plan;
        }, cancellationToken);

    private string InsertCore(NuvexaDocument document)
    {
        document.EnsureId();
        var tree = new BPlusTree(_db.Store, Meta.IndexRoot);
        var key = DocumentPath.IdKey(document.Id);
        if (tree.TryFind(key, out _, out _))
        {
            throw new NuvexaException($"A document with _id '{document.Id}' already exists.");
        }

        var payload = document.ToStorageBytes();
        var (pageId, slot) = DocumentIO.Write(_db.Store, Meta, payload);
        tree.Upsert(key, pageId, slot);
        Meta.IndexRoot = tree.RootPageId;
        UpdateSecondary(document, pageId, slot, removeOld: null);
        Meta.Count++;
        _db.PersistCatalog();
        return document.Id;
    }

    private NuvexaDocument? FindByIdCore(string id)
    {
        var tree = new BPlusTree(_db.Store, Meta.IndexRoot);
        if (!tree.TryFind(DocumentPath.IdKey(id), out var pageId, out var slot))
        {
            return null;
        }

        var bytes = DocumentIO.Read(_db.Store, pageId, slot);
        return NuvexaDocument.FromStorage(bytes);
    }

    private void DeleteByIdCore(string id)
    {
        var tree = new BPlusTree(_db.Store, Meta.IndexRoot);
        var key = DocumentPath.IdKey(id);
        if (!tree.TryFind(key, out var pageId, out var slot))
        {
            return;
        }

        var bytes = DocumentIO.Read(_db.Store, pageId, slot);
        var existing = NuvexaDocument.FromStorage(bytes);
        UpdateSecondary(null, pageId, slot, existing);
        DocumentIO.Delete(_db.Store, pageId, slot);
        tree.Remove(key);
        Meta.IndexRoot = tree.RootPageId;
        Meta.Count = Math.Max(0, Meta.Count - 1);
        _db.PersistCatalog();
    }

    private void ReplaceCore(NuvexaDocument document)
    {
        var tree = new BPlusTree(_db.Store, Meta.IndexRoot);
        var key = DocumentPath.IdKey(document.Id);
        NuvexaDocument? old = null;
        if (tree.TryFind(key, out var oldPage, out var oldSlot))
        {
            var bytes = DocumentIO.Read(_db.Store, oldPage, oldSlot);
            old = NuvexaDocument.FromStorage(bytes);
            DocumentIO.Delete(_db.Store, oldPage, oldSlot);
            tree.Remove(key);
        }

        var payload = document.ToStorageBytes();
        var (pageId, slot) = DocumentIO.Write(_db.Store, Meta, payload);
        tree.Upsert(key, pageId, slot);
        Meta.IndexRoot = tree.RootPageId;
        UpdateSecondary(document, pageId, slot, old);
        if (old is null)
        {
            Meta.Count++;
        }

        _db.PersistCatalog();
    }

    private List<NuvexaDocument> ExecuteFindCore(
        NuvexaFilter filter,
        List<(string Path, bool Ascending)> sort,
        int skip,
        int limit,
        List<string>? projection,
        out NuvexaExplainPlan plan)
    {
        var examined = 0;
        var strategy = "COLLSCAN";
        string? indexName = null;
        var docs = new List<NuvexaDocument>();
        var earlyStop = sort.Count == 0;
        var skipped = 0;

        IEnumerable<NuvexaDocument> source;
        if (filter.Kind == NuvexaFilterKind.Eq && filter.Path is "_id")
        {
            strategy = "ID";
            indexName = "_id_";
            var id = filter.Values[0].ToString()?.Trim('"') ?? "";
            var found = FindByIdCore(id);
            source = found is null ? [] : [found];
        }
        else if (TrySecondaryScan(filter, out var scanned, out indexName))
        {
            strategy = "IXSCAN";
            source = scanned;
        }
        else
        {
            source = EnumerateCollection();
        }

        foreach (var doc in source)
        {
            examined++;
            if (!filter.Matches(doc.AsElement()))
            {
                continue;
            }

            if (earlyStop)
            {
                if (skipped < skip)
                {
                    skipped++;
                    continue;
                }

                docs.Add(doc);
                if (limit > 0 && docs.Count >= limit)
                {
                    break;
                }
            }
            else
            {
                docs.Add(doc);
            }
        }

        if (!earlyStop)
        {
            if (sort.Count > 0)
            {
                docs.Sort((a, b) => CompareSort(a, b, sort));
            }

            if (skip > 0)
            {
                docs = docs.Skip(skip).ToList();
            }

            if (limit > 0)
            {
                docs = docs.Take(limit).ToList();
            }
        }

        if (projection is { Count: > 0 })
        {
            docs = docs.Select(d => NuvexaFindFluent.ProjectDocument(d, projection)).ToList();
        }

        plan = new NuvexaExplainPlan
        {
            Collection = Name,
            Strategy = strategy,
            IndexName = indexName,
            Examined = examined,
            Returned = docs.Count
        };
        return docs;
    }

    internal IEnumerable<NuvexaDocument> EnumerateAll() => EnumerateCollection();

    private IEnumerable<NuvexaDocument> EnumerateCollection()
    {
        foreach (var (pageId, slot) in DocumentIO.EnumerateSlots(_db.Store, Meta))
        {
            var bytes = DocumentIO.Read(_db.Store, pageId, slot);
            yield return NuvexaDocument.FromStorage(bytes);
        }
    }

    private bool TrySecondaryScan(NuvexaFilter filter, out IEnumerable<NuvexaDocument> docs, out string? indexName)
    {
        docs = [];
        indexName = null;
        if (!TryResolveIndexedPredicate(filter, out var predicate, out var match) || match.RootPageId == 0)
        {
            return false;
        }

        indexName = match.Name;
        var tree = new BPlusTree(_db.Store, match.RootPageId);
        docs = EnumerateIndexed(tree, predicate, match);
        return true;
    }

    private IEnumerable<NuvexaDocument> EnumerateIndexed(
        BPlusTree tree,
        NuvexaFilter predicate,
        SecondaryIndexMeta match)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var doc in ScanFormat(tree, predicate, match, Constants.FormatVersion))
        {
            if (seen.Add(doc.Id))
            {
                yield return doc;
            }
        }

        if (!IsNumericPredicate(predicate))
        {
            yield break;
        }

        foreach (var doc in ScanFormat(tree, predicate, match, Constants.MinFormatVersion))
        {
            if (seen.Add(doc.Id))
            {
                yield return doc;
            }
        }
    }

    private IEnumerable<NuvexaDocument> ScanFormat(
        BPlusTree tree,
        NuvexaFilter predicate,
        SecondaryIndexMeta match,
        ushort formatVersion)
    {
        GetIndexBounds(predicate, match, formatVersion, out var lo, out var hi, out var hasLo, out var hasHi);
        foreach (var (_, pageId, slot) in tree.Scan(lo, hi, hasLo, hasHi))
        {
            yield return NuvexaDocument.FromStorage(DocumentIO.Read(_db.Store, pageId, slot));
        }
    }

    private static bool IsNumericPredicate(NuvexaFilter predicate)
    {
        if (predicate.Kind == NuvexaFilterKind.And)
        {
            return predicate.Children.Any(IsNumericPredicate);
        }

        return predicate.Values.Length > 0 && DocumentPath.IsNumeric(predicate.Values[0]);
    }

    private bool TryResolveIndexedPredicate(
        NuvexaFilter filter,
        out NuvexaFilter predicate,
        out SecondaryIndexMeta match)
    {
        predicate = filter;
        match = null!;
        var indexes = Catalog.LoadSecondary(_db.Store, Meta);
        if (indexes.Count == 0)
        {
            return false;
        }

        if (filter.Kind == NuvexaFilterKind.And)
        {
            if (TryMatchCompoundAnd(filter, indexes, out match))
            {
                predicate = filter;
                return true;
            }

            NuvexaFilter? best = null;
            SecondaryIndexMeta? bestMatch = null;
            var bestRank = int.MaxValue;
            foreach (var child in filter.Children)
            {
                if (!TryMatchIndex(child, indexes, out var found))
                {
                    continue;
                }

                var rank = IndexPreference(child);
                if (rank >= bestRank)
                {
                    continue;
                }

                best = child;
                bestMatch = found;
                bestRank = rank;
            }

            if (best is null || bestMatch is null)
            {
                return false;
            }

            predicate = best;
            match = bestMatch;
            return true;
        }

        return TryMatchIndex(filter, indexes, out match);
    }

    private static bool TryMatchIndex(
        NuvexaFilter filter,
        IReadOnlyList<SecondaryIndexMeta> indexes,
        out SecondaryIndexMeta match)
    {
        match = null!;
        if (filter.Path is null || !IsIndexableKind(filter.Kind))
        {
            return false;
        }

        var found = indexes.FirstOrDefault(i => i.FieldPath == filter.Path)
            ?? indexes.FirstOrDefault(i =>
                DocumentPath.SplitIndexPaths(i.FieldPath) is { Length: > 0 } paths && paths[0] == filter.Path);
        if (found is null)
        {
            return false;
        }

        match = found;
        return true;
    }

    private static bool TryMatchCompoundAnd(
        NuvexaFilter filter,
        IReadOnlyList<SecondaryIndexMeta> indexes,
        out SecondaryIndexMeta match)
    {
        match = null!;
        var eq = filter.Children.Where(c => c.Kind == NuvexaFilterKind.Eq && c.Path is not null).ToList();
        if (eq.Count == 0)
        {
            return false;
        }

        foreach (var idx in indexes)
        {
            var paths = DocumentPath.SplitIndexPaths(idx.FieldPath);
            if (paths.Length < 2)
            {
                continue;
            }

            if (paths.All(p => eq.Any(c => c.Path == p)))
            {
                match = idx;
                return true;
            }
        }

        return false;
    }

    private static bool IsIndexableKind(NuvexaFilterKind kind) =>
        kind is NuvexaFilterKind.Eq or NuvexaFilterKind.Gt or NuvexaFilterKind.Gte
            or NuvexaFilterKind.Lt or NuvexaFilterKind.Lte;

    // Equality uses a tight prefix. String ranges keep byte order. Writes use v2
    // numeric keys (bounded IXSCAN). Format 1 n: keys are still read.
    private static int IndexPreference(NuvexaFilter filter)
    {
        if (filter.Kind == NuvexaFilterKind.Eq)
        {
            return 0;
        }

        return 1;
    }

    private static void GetIndexBounds(
        NuvexaFilter predicate,
        SecondaryIndexMeta index,
        ushort formatVersion,
        out byte[]? lo,
        out byte[]? hi,
        out bool hasLo,
        out bool hasHi)
    {
        lo = hi = null;
        hasLo = hasHi = false;
        var paths = DocumentPath.SplitIndexPaths(index.FieldPath);
        if (DocumentPath.IsCompoundIndexPath(index.FieldPath) &&
            predicate.Kind == NuvexaFilterKind.And)
        {
            var values = new List<JsonElement>(paths.Length);
            foreach (var path in paths)
            {
                var child = predicate.Children.FirstOrDefault(c =>
                    c.Kind == NuvexaFilterKind.Eq && c.Path == path && c.Values.Length > 0);
                if (child is null)
                {
                    return;
                }

                values.Add(child.Values[0]);
            }

            lo = DocumentPath.CompoundScanPrefix(values, formatVersion);
            hi = DocumentPath.CompoundScanPrefixSuccessor(values, formatVersion);
            hasLo = hasHi = true;
            return;
        }

        if (predicate.Values.Length == 0)
        {
            return;
        }

        var value = predicate.Values[0];
        if (formatVersion < 2 && DocumentPath.IsNumeric(value) && predicate.Kind != NuvexaFilterKind.Eq)
        {
            lo = DocumentPath.LegacyNumericScanPrefix();
            hi = DocumentPath.LegacyNumericScanPrefixSuccessor();
            hasLo = hasHi = true;
            return;
        }

        byte[] prefix;
        byte[] successor;
        if (DocumentPath.IsCompoundIndexPath(index.FieldPath) && paths[0] == predicate.Path)
        {
            prefix = DocumentPath.CompoundFirstFieldPrefix(value, formatVersion);
            successor = DocumentPath.CompoundFirstFieldPrefixSuccessor(value, formatVersion);
        }
        else
        {
            prefix = DocumentPath.IndexScanPrefix(value, formatVersion);
            successor = DocumentPath.IndexScanPrefixSuccessor(value, formatVersion);
        }

        switch (predicate.Kind)
        {
            case NuvexaFilterKind.Eq:
                lo = prefix;
                hi = successor;
                hasLo = hasHi = true;
                break;
            case NuvexaFilterKind.Gte:
                lo = prefix;
                hasLo = true;
                break;
            case NuvexaFilterKind.Gt:
                lo = successor;
                hasLo = true;
                break;
            case NuvexaFilterKind.Lt:
                hi = prefix;
                hasHi = true;
                break;
            case NuvexaFilterKind.Lte:
                hi = successor;
                hasHi = true;
                break;
        }
    }

    private void EnsureIndexCore(string fieldPath, string name, bool unique)
    {
        var indexes = Catalog.LoadSecondary(_db.Store, Meta);
        if (indexes.Any(i => i.Name == name || i.FieldPath == fieldPath))
        {
            return;
        }

        var tree = new BPlusTree(_db.Store, 0);
        foreach (var (pageId, slot) in DocumentIO.EnumerateSlots(_db.Store, Meta))
        {
            var bytes = DocumentIO.Read(_db.Store, pageId, slot);
            var doc = NuvexaDocument.FromStorage(bytes);
            if (!TryIndexKey(fieldPath, doc, WriteFormatVersion, out var key))
            {
                continue;
            }

            if (unique && tree.TryFind(key, out _, out _))
            {
                throw new NuvexaException($"Unique index '{name}' would be violated.");
            }

            tree.Upsert(key, pageId, slot);
        }

        indexes.Add(new SecondaryIndexMeta
        {
            Name = name,
            FieldPath = fieldPath,
            Unique = unique,
            RootPageId = tree.RootPageId
        });
        Catalog.SaveSecondary(_db.Store, Meta, indexes);
        _db.PersistCatalog();
    }

    private void UpdateSecondary(NuvexaDocument? next, long pageId, int slot, NuvexaDocument? removeOld)
    {
        var indexes = Catalog.LoadSecondary(_db.Store, Meta);
        foreach (var idx in indexes)
        {
            var tree = new BPlusTree(_db.Store, idx.RootPageId);
            if (removeOld is not null)
            {
                foreach (var oldKey in IndexKeysForRead(idx.FieldPath, removeOld))
                {
                    tree.Remove(oldKey);
                }
            }

            if (next is not null && TryIndexKey(idx.FieldPath, next, WriteFormatVersion, out var key))
            {
                if (idx.Unique && (tree.TryFind(key, out _, out _) || LegacyUniqueHit(tree, idx.FieldPath, next, key)))
                {
                    throw new NuvexaException($"Unique index '{idx.Name}' would be violated.");
                }

                tree.Upsert(key, pageId, slot);
            }

            idx.RootPageId = tree.RootPageId;
        }

        if (indexes.Count > 0)
        {
            Catalog.SaveSecondary(_db.Store, Meta, indexes);
        }
    }

    private static int CompareSort(NuvexaDocument a, NuvexaDocument b, List<(string Path, bool Ascending)> sort)
    {
        foreach (var (path, asc) in sort)
        {
            DocumentPath.TryGet(a.AsElement(), path, out var av);
            DocumentPath.TryGet(b.AsElement(), path, out var bv);
            var cmp = DocumentPath.Compare(av, bv);
            if (cmp != 0)
            {
                return asc ? cmp : -cmp;
            }
        }

        return 0;
    }

    private static IEnumerable<byte[]> IndexKeysForRead(string fieldPath, NuvexaDocument document)
    {
        byte[]? current = null;
        if (TryIndexKey(fieldPath, document, Constants.FormatVersion, out var written))
        {
            current = written;
            yield return written;
        }

        if (TryIndexKey(fieldPath, document, Constants.MinFormatVersion, out var legacy) &&
            (current is null || !legacy.AsSpan().SequenceEqual(current)))
        {
            yield return legacy;
        }
    }

    private static bool LegacyUniqueHit(BPlusTree tree, string fieldPath, NuvexaDocument document, byte[] writtenKey)
    {
        if (!TryIndexKey(fieldPath, document, Constants.MinFormatVersion, out var legacy) ||
            legacy.AsSpan().SequenceEqual(writtenKey))
        {
            return false;
        }

        return tree.TryFind(legacy, out _, out _);
    }

    private static bool TryIndexKey(string fieldPath, NuvexaDocument document, ushort formatVersion, out byte[] key)
    {
        var paths = DocumentPath.SplitIndexPaths(fieldPath);
        if (paths.Length == 1)
        {
            if (!DocumentPath.TryGet(document.AsElement(), paths[0], out var value))
            {
                key = [];
                return false;
            }

            key = DocumentPath.IndexKey(value, document.Id, formatVersion);
            return true;
        }

        var values = new List<JsonElement>(paths.Length);
        foreach (var path in paths)
        {
            if (!DocumentPath.TryGet(document.AsElement(), path, out var value))
            {
                key = [];
                return false;
            }

            values.Add(value);
        }

        key = DocumentPath.CompoundIndexKey(values, document.Id, formatVersion);
        return true;
    }
}

/// <summary>Typed collection wrapper.</summary>
public sealed class NuvexaCollection<T> where T : class
{
    private readonly NuvexaCollection _inner;

    internal NuvexaCollection(NuvexaCollection inner) => _inner = inner;

    public Task<string> InsertAsync(T document, CancellationToken cancellationToken = default) =>
        _inner.InsertAsync(NuvexaDocument.FromObject(document), cancellationToken);

    public Task InsertManyAsync(IEnumerable<T> documents, CancellationToken cancellationToken = default) =>
        _inner.InsertManyAsync(documents.Select(NuvexaDocument.FromObject), cancellationToken);

    public async Task<T?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var doc = await _inner.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return doc?.Deserialize<T>();
    }

    public NuvexaFindFluent Find(NuvexaFilter? filter = null) => _inner.Find(filter);

    public NuvexaFindFluent Find(System.Linq.Expressions.Expression<Func<T, bool>> predicate) =>
        _inner.Find(Query.ExpressionFilter.From(predicate));

    public NuvexaFindFluent Where(System.Linq.Expressions.Expression<Func<T, bool>> predicate) => Find(predicate);

    public async Task<List<T>> ToListAsync(System.Linq.Expressions.Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var rows = await Find(predicate).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => r.Deserialize<T>()).ToList();
    }

    public async Task<List<T>> ToListAsync(NuvexaFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var rows = await _inner.Find(filter).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => r.Deserialize<T>()).ToList();
    }

    public Task EnsureIndexAsync(string fieldPath, CancellationToken cancellationToken = default) =>
        _inner.EnsureIndexAsync(fieldPath, cancellationToken: cancellationToken);

    public Task EnsureIndexAsync(IReadOnlyList<string> fieldPaths, CancellationToken cancellationToken = default) =>
        _inner.EnsureIndexAsync(fieldPaths, cancellationToken: cancellationToken);
}
