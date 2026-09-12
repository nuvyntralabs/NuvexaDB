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

    public Task<IReadOnlyList<NuvexaIndexInfo>> ListIndexesAsync(CancellationToken cancellationToken = default) =>
        _db.ReadAsync(() =>
        {
            var indexes = Catalog.LoadSecondary(_db.Store, Meta)
                .Select(i => new NuvexaIndexInfo(i.Name, i.FieldPath, i.Unique))
                .ToList();
            indexes.Insert(0, new NuvexaIndexInfo("_id_", "_id", Unique: true));
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

        var json = Engine.Constants.Utf8.GetBytes(document.ToJson());
        var (pageId, slot) = DocumentIO.Write(_db.Store, Meta, json);
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
        return NuvexaDocument.Parse(Engine.Constants.Utf8.GetString(bytes));
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
        var existing = NuvexaDocument.Parse(Engine.Constants.Utf8.GetString(bytes));
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
            old = NuvexaDocument.Parse(Engine.Constants.Utf8.GetString(bytes));
            DocumentIO.Delete(_db.Store, oldPage, oldSlot);
            tree.Remove(key);
        }

        var json = Engine.Constants.Utf8.GetBytes(document.ToJson());
        var (pageId, slot) = DocumentIO.Write(_db.Store, Meta, json);
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

        if (filter.Kind == NuvexaFilterKind.Eq && filter.Path is "_id")
        {
            strategy = "ID";
            indexName = "_id_";
            var id = filter.Values[0].ToString()?.Trim('"') ?? "";
            var found = FindByIdCore(id);
            if (found is not null)
            {
                examined = 1;
                docs.Add(found);
            }
        }
        else if (TrySecondaryScan(filter, out var scanned, out indexName))
        {
            strategy = "IXSCAN";
            foreach (var doc in scanned)
            {
                examined++;
                if (filter.Matches(doc.AsElement()))
                {
                    docs.Add(doc);
                }
            }
        }
        else
        {
            foreach (var (pageId, slot) in DocumentIO.EnumerateSlots(_db.Store, Meta))
            {
                examined++;
                var bytes = DocumentIO.Read(_db.Store, pageId, slot);
                var doc = NuvexaDocument.Parse(Engine.Constants.Utf8.GetString(bytes));
                if (filter.Matches(doc.AsElement()))
                {
                    docs.Add(doc);
                }
            }
        }

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

    private bool TrySecondaryScan(NuvexaFilter filter, out List<NuvexaDocument> docs, out string? indexName)
    {
        docs = [];
        indexName = null;
        if (filter.Kind != NuvexaFilterKind.Eq && filter.Kind != NuvexaFilterKind.Gte && filter.Kind != NuvexaFilterKind.Gt
            && filter.Kind != NuvexaFilterKind.Lte && filter.Kind != NuvexaFilterKind.Lt)
        {
            return false;
        }

        var indexes = Catalog.LoadSecondary(_db.Store, Meta);
        var match = indexes.FirstOrDefault(i => i.FieldPath == filter.Path);
        if (match is null || match.RootPageId == 0)
        {
            return false;
        }

        indexName = match.Name;
        var tree = new BPlusTree(_db.Store, match.RootPageId);
        foreach (var (_, pageId, slot) in tree.Scan(null, null, hasLo: false, hasHi: false))
        {
            var bytes = DocumentIO.Read(_db.Store, pageId, slot);
            docs.Add(NuvexaDocument.Parse(Engine.Constants.Utf8.GetString(bytes)));
        }

        return true;
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
            var doc = NuvexaDocument.Parse(Engine.Constants.Utf8.GetString(bytes));
            if (DocumentPath.TryGet(doc.AsElement(), fieldPath, out var value))
            {
                var key = DocumentPath.IndexKey(value, doc.Id);
                if (unique && tree.TryFind(key, out _, out _))
                {
                    throw new NuvexaException($"Unique index '{name}' would be violated.");
                }

                tree.Upsert(key, pageId, slot);
            }
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
            if (removeOld is not null && DocumentPath.TryGet(removeOld.AsElement(), idx.FieldPath, out var oldVal))
            {
                tree.Remove(DocumentPath.IndexKey(oldVal, removeOld.Id));
            }

            if (next is not null && DocumentPath.TryGet(next.AsElement(), idx.FieldPath, out var newVal))
            {
                var key = DocumentPath.IndexKey(newVal, next.Id);
                if (idx.Unique && tree.TryFind(key, out _, out _))
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
}
