using System.Buffers.Binary;

namespace Nuventra.NuvexaDB.Engine;

internal sealed class CollectionMeta
{
    public string Name { get; set; } = "";
    public long DataHead { get; set; }
    public long DataTail { get; set; }
    public long IndexRoot { get; set; }
    public long Count { get; set; }
    public long SecondaryCatalogPage { get; set; }
}

internal sealed class SecondaryIndexMeta
{
    public string Name { get; set; } = "";
    public string FieldPath { get; set; } = "";
    public bool Unique { get; set; }
    public long RootPageId { get; set; }
}

internal static class Catalog
{
    public static List<CollectionMeta> Load(PageStore store)
    {
        var list = new List<CollectionMeta>();
        var pageId = store.Superblock.CatalogPageId;
        while (pageId != 0)
        {
            var page = store.Get(pageId);
            var span = page.PayloadAfterHeader();
            var offset = 0;
            for (var i = 0; i < page.ItemCount; i++)
            {
                var nameLen = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
                offset += 2;
                var name = Constants.Utf8.GetString(span.Slice(offset, nameLen));
                offset += nameLen;
                var meta = new CollectionMeta
                {
                    Name = name,
                    DataHead = BinaryPrimitives.ReadInt64LittleEndian(span[offset..]),
                    DataTail = BinaryPrimitives.ReadInt64LittleEndian(span[(offset + 8)..]),
                    IndexRoot = BinaryPrimitives.ReadInt64LittleEndian(span[(offset + 16)..]),
                    Count = BinaryPrimitives.ReadInt64LittleEndian(span[(offset + 24)..]),
                    SecondaryCatalogPage = BinaryPrimitives.ReadInt64LittleEndian(span[(offset + 32)..])
                };
                offset += 40;
                list.Add(meta);
            }

            pageId = page.NextPageId;
        }

        return list;
    }

    public static void Save(PageStore store, IReadOnlyList<CollectionMeta> collections)
    {
        var pages = new List<Page>();
        var current = store.Get(store.Superblock.CatalogPageId);
        current.Type = PageType.Catalog;
        current.ItemCount = 0;
        current.NextPageId = 0;
        current.PayloadAfterHeader().Clear();
        pages.Add(current);

        foreach (var meta in collections)
        {
            var nameBytes = Constants.Utf8.GetBytes(meta.Name);
            var size = 2 + nameBytes.Length + 40;
            var page = pages[^1];
            var remaining = Constants.PayloadSize - Constants.PageHeaderSize - UsedCatalogBytes(page);
            if (remaining < size + 8)
            {
                var next = store.Allocate(PageType.Catalog);
                page.NextPageId = next.PageId;
                store.MarkDirty(page);
                next.ItemCount = 0;
                pages.Add(next);
                page = next;
            }

            var span = page.PayloadAfterHeader();
            var offset = UsedCatalogBytes(page);
            BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], (ushort)nameBytes.Length);
            offset += 2;
            nameBytes.CopyTo(span[offset..]);
            offset += nameBytes.Length;
            BinaryPrimitives.WriteInt64LittleEndian(span[offset..], meta.DataHead);
            BinaryPrimitives.WriteInt64LittleEndian(span[(offset + 8)..], meta.DataTail);
            BinaryPrimitives.WriteInt64LittleEndian(span[(offset + 16)..], meta.IndexRoot);
            BinaryPrimitives.WriteInt64LittleEndian(span[(offset + 24)..], meta.Count);
            BinaryPrimitives.WriteInt64LittleEndian(span[(offset + 32)..], meta.SecondaryCatalogPage);
            page.ItemCount++;
            store.MarkDirty(page);
        }

        foreach (var page in pages)
        {
            store.MarkDirty(page);
        }
    }

    public static List<SecondaryIndexMeta> LoadSecondary(PageStore store, CollectionMeta collection)
    {
        var list = new List<SecondaryIndexMeta>();
        if (collection.SecondaryCatalogPage == 0)
        {
            return list;
        }

        var page = store.Get(collection.SecondaryCatalogPage);
        var span = page.PayloadAfterHeader();
        var offset = 0;
        for (var i = 0; i < page.ItemCount; i++)
        {
            var nameLen = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
            offset += 2;
            var name = Constants.Utf8.GetString(span.Slice(offset, nameLen));
            offset += nameLen;
            var pathLen = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
            offset += 2;
            var path = Constants.Utf8.GetString(span.Slice(offset, pathLen));
            offset += pathLen;
            var unique = span[offset] != 0;
            offset += 1;
            var root = BinaryPrimitives.ReadInt64LittleEndian(span[offset..]);
            offset += 8;
            list.Add(new SecondaryIndexMeta { Name = name, FieldPath = path, Unique = unique, RootPageId = root });
        }

        return list;
    }

    public static void SaveSecondary(PageStore store, CollectionMeta collection, IReadOnlyList<SecondaryIndexMeta> indexes)
    {
        Page page;
        if (collection.SecondaryCatalogPage == 0)
        {
            page = store.Allocate(PageType.SecondaryIndexCatalog);
            collection.SecondaryCatalogPage = page.PageId;
        }
        else
        {
            page = store.Get(collection.SecondaryCatalogPage);
        }

        page.Type = PageType.SecondaryIndexCatalog;
        page.ItemCount = 0;
        page.PayloadAfterHeader().Clear();
        var span = page.PayloadAfterHeader();
        var offset = 0;
        foreach (var idx in indexes)
        {
            var name = Constants.Utf8.GetBytes(idx.Name);
            var path = Constants.Utf8.GetBytes(idx.FieldPath);
            BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], (ushort)name.Length);
            offset += 2;
            name.CopyTo(span[offset..]);
            offset += name.Length;
            BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], (ushort)path.Length);
            offset += 2;
            path.CopyTo(span[offset..]);
            offset += path.Length;
            span[offset] = idx.Unique ? (byte)1 : (byte)0;
            offset += 1;
            BinaryPrimitives.WriteInt64LittleEndian(span[offset..], idx.RootPageId);
            offset += 8;
            page.ItemCount++;
        }

        store.MarkDirty(page);
    }

    private static int UsedCatalogBytes(Page page)
    {
        var span = page.PayloadAfterHeader();
        var offset = 0;
        for (var i = 0; i < page.ItemCount; i++)
        {
            var nameLen = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
            offset += 2 + nameLen + 40;
        }

        return offset;
    }
}
