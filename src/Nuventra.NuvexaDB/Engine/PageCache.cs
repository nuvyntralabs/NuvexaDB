namespace Nuventra.NuvexaDB.Engine;

internal sealed class PageCache
{
    private readonly int _capacity;
    private readonly Dictionary<long, LinkedListNode<CacheEntry>> _map = new();
    private readonly LinkedList<CacheEntry> _lru = new();

    public PageCache(int cacheSizeMb)
    {
        var pages = Math.Max(8, cacheSizeMb * 1024 * 1024 / Constants.PageSize);
        _capacity = pages;
    }

    public int Count => _map.Count;
    public int Capacity => _capacity;

    public bool TryGet(long pageId, out Page page)
    {
        if (_map.TryGetValue(pageId, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            page = node.Value.Page;
            return true;
        }

        page = null!;
        return false;
    }

    public void Set(Page page)
    {
        if (_map.TryGetValue(page.PageId, out var existing))
        {
            existing.Value.Page = page;
            _lru.Remove(existing);
            _lru.AddFirst(existing);
            return;
        }

        while (_map.Count >= _capacity)
        {
            EvictOne();
        }

        var node = _lru.AddFirst(new CacheEntry(page));
        _map[page.PageId] = node;
    }

    public IEnumerable<Page> DirtyPages()
    {
        foreach (var node in _lru)
        {
            if (node.Page.Dirty)
            {
                yield return node.Page;
            }
        }
    }

    public void Remove(long pageId)
    {
        if (_map.Remove(pageId, out var node))
        {
            _lru.Remove(node);
        }
    }

    public void Clear()
    {
        _map.Clear();
        _lru.Clear();
    }

    private void EvictOne()
    {
        var node = _lru.Last;
        while (node is not null)
        {
            if (!node.Value.Page.Dirty)
            {
                _map.Remove(node.Value.Page.PageId);
                _lru.Remove(node);
                return;
            }

            node = node.Previous;
        }

        // All dirty — drop the least-recent dirty page from cache only (still on disk/WAL).
        var last = _lru.Last;
        if (last is not null)
        {
            _map.Remove(last.Value.Page.PageId);
            _lru.Remove(last);
        }
    }

    private sealed class CacheEntry
    {
        public Page Page;

        public CacheEntry(Page page) => Page = page;
    }
}
