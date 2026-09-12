namespace Nuventra.NuvexaDB.Engine;

internal sealed class BPlusTree
{
    private readonly PageStore _store;
    public long RootPageId { get; private set; }

    public BPlusTree(PageStore store, long rootPageId)
    {
        _store = store;
        RootPageId = rootPageId;
        if (RootPageId == 0)
        {
            var root = _store.Allocate(PageType.IndexLeaf);
            RootPageId = root.PageId;
        }
    }

    public bool TryFind(ReadOnlySpan<byte> key, out long dataPageId, out int slot)
    {
        var leaf = FindLeaf(key);
        var entries = IndexCodec.Read(leaf, leaf: true);
        var idx = IndexCodec.Search(entries, key);
        if (idx >= 0)
        {
            dataPageId = entries[idx].PageOrChild;
            slot = entries[idx].Slot;
            return true;
        }

        dataPageId = 0;
        slot = -1;
        return false;
    }

    public void Upsert(ReadOnlySpan<byte> key, long dataPageId, int slot)
    {
        var keyBytes = key.ToArray();
        var path = new List<Page>();
        var page = _store.Get(RootPageId);
        path.Add(page);
        while (page.Type == PageType.IndexInternal)
        {
            var internals = IndexCodec.Read(page, leaf: false);
            var childIndex = internals.Count == 0 ? 0 : IndexCodec.ChildIndex(internals, key);
            if (internals.Count == 0)
            {
                break;
            }

            childIndex = Math.Clamp(childIndex, 0, internals.Count - 1);
            page = _store.Get(internals[childIndex].PageOrChild);
            path.Add(page);
        }

        var leaf = page;
        var entries = IndexCodec.Read(leaf, leaf: true);
        var idx = IndexCodec.Search(entries, key);
        if (idx >= 0)
        {
            entries[idx] = new IndexEntry(keyBytes, dataPageId, (ushort)slot);
        }
        else
        {
            entries.Insert(~idx, new IndexEntry(keyBytes, dataPageId, (ushort)slot));
        }

        if (IndexCodec.TryWrite(leaf, entries, leaf: true))
        {
            _store.MarkDirty(leaf);
            return;
        }

        SplitLeaf(path, leaf, entries);
    }

    public bool Remove(ReadOnlySpan<byte> key)
    {
        var leaf = FindLeaf(key);
        var entries = IndexCodec.Read(leaf, leaf: true);
        var idx = IndexCodec.Search(entries, key);
        if (idx < 0)
        {
            return false;
        }

        entries.RemoveAt(idx);
        IndexCodec.TryWrite(leaf, entries, leaf: true);
        _store.MarkDirty(leaf);
        return true;
    }

    public IEnumerable<(byte[] Key, long PageId, int Slot)> Scan(byte[]? loInclusive, byte[]? hiExclusive, bool hasLo, bool hasHi)
    {
        Page leaf;
        if (hasLo && loInclusive is not null)
        {
            leaf = FindLeaf(loInclusive);
        }
        else
        {
            leaf = LeftmostLeaf();
        }

        var lo = hasLo ? loInclusive : null;
        var hi = hasHi ? hiExclusive : null;

        while (true)
        {
            var entries = IndexCodec.Read(leaf, leaf: true);
            foreach (var e in entries)
            {
                if (lo is not null && IndexEntry.Compare(e.Key, lo) < 0)
                {
                    continue;
                }

                if (hi is not null && IndexEntry.Compare(e.Key, hi) >= 0)
                {
                    yield break;
                }

                yield return (e.Key, e.PageOrChild, e.Slot);
            }

            if (leaf.RightSibling == 0)
            {
                yield break;
            }

            leaf = _store.Get(leaf.RightSibling);
        }
    }

    private Page FindLeaf(ReadOnlySpan<byte> key)
    {
        var page = _store.Get(RootPageId);
        while (page.Type == PageType.IndexInternal)
        {
            var internals = IndexCodec.Read(page, leaf: false);
            if (internals.Count == 0)
            {
                break;
            }

            var childIndex = Math.Clamp(IndexCodec.ChildIndex(internals, key), 0, internals.Count - 1);
            page = _store.Get(internals[childIndex].PageOrChild);
        }

        return page;
    }

    private Page LeftmostLeaf()
    {
        var page = _store.Get(RootPageId);
        while (page.Type == PageType.IndexInternal)
        {
            var internals = IndexCodec.Read(page, leaf: false);
            if (internals.Count == 0)
            {
                break;
            }

            page = _store.Get(internals[0].PageOrChild);
        }

        return page;
    }

    private void SplitLeaf(List<Page> path, Page leaf, List<IndexEntry> entries)
    {
        var mid = entries.Count / 2;
        var rightEntries = entries.GetRange(mid, entries.Count - mid);
        entries.RemoveRange(mid, entries.Count - mid);
        IndexCodec.TryWrite(leaf, entries, leaf: true);
        _store.MarkDirty(leaf);

        var right = _store.Allocate(PageType.IndexLeaf);
        right.RightSibling = leaf.RightSibling;
        leaf.RightSibling = right.PageId;
        IndexCodec.TryWrite(right, rightEntries, leaf: true);
        _store.MarkDirty(right);

        var separator = rightEntries[0].Key;
        InsertInternal(path, path.Count - 2, separator, right.PageId);
    }

    private void InsertInternal(List<Page> path, int parentIndex, byte[] separator, long rightChild)
    {
        if (parentIndex < 0)
        {
            var newRoot = _store.Allocate(PageType.IndexInternal);
            var leftId = RootPageId;
            var internals = new List<IndexEntry>
            {
                new(Array.Empty<byte>(), leftId),
                new(separator, rightChild)
            };
            IndexCodec.TryWrite(newRoot, internals, leaf: false);
            _store.MarkDirty(newRoot);
            RootPageId = newRoot.PageId;
            return;
        }

        var parent = path[parentIndex];
        var internals2 = IndexCodec.Read(parent, leaf: false);
        if (internals2.Count == 0)
        {
            internals2.Add(new IndexEntry(Array.Empty<byte>(), path[parentIndex + 1].PageId));
        }

        var idx = IndexCodec.Search(internals2, separator);
        var insertAt = idx >= 0 ? idx + 1 : ~idx;
        internals2.Insert(insertAt, new IndexEntry(separator, rightChild));
        if (IndexCodec.TryWrite(parent, internals2, leaf: false))
        {
            _store.MarkDirty(parent);
            return;
        }

        var mid = internals2.Count / 2;
        var rightEntries = internals2.GetRange(mid, internals2.Count - mid);
        internals2.RemoveRange(mid, internals2.Count - mid);
        IndexCodec.TryWrite(parent, internals2, leaf: false);
        _store.MarkDirty(parent);
        var right = _store.Allocate(PageType.IndexInternal);
        IndexCodec.TryWrite(right, rightEntries, leaf: false);
        _store.MarkDirty(right);
        InsertInternal(path, parentIndex - 1, rightEntries[0].Key, right.PageId);
    }
}
