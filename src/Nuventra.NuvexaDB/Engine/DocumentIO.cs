using System.Buffers.Binary;

namespace Nuventra.NuvexaDB.Engine;

internal static class DocumentIO
{
    private const byte Inline = 0;
    private const byte Overflow = 1;

    public static (long PageId, int Slot) Write(PageStore store, CollectionMeta meta, ReadOnlySpan<byte> json)
    {
        if (json.Length > Constants.MaxDocumentBytes)
        {
            throw new NuvexaException($"Document exceeds the {Constants.MaxDocumentBytes} byte limit.");
        }

        if (json.Length + 5 <= MaxInline())
        {
            return WriteInline(store, meta, json);
        }

        return WriteOverflow(store, meta, json);
    }

    public static byte[] Read(PageStore store, long pageId, int slot)
    {
        var page = store.Get(pageId);
        var data = page.GetSlot(slot);
        if (data.IsEmpty)
        {
            throw new NuvexaException("Document slot is empty.");
        }

        var kind = data[0];
        if (kind == Inline)
        {
            var len = BinaryPrimitives.ReadInt32LittleEndian(data[1..]);
            return data.Slice(5, len).ToArray();
        }

        var total = BinaryPrimitives.ReadInt32LittleEndian(data[1..]);
        var next = BinaryPrimitives.ReadInt64LittleEndian(data[5..]);
        var buffer = new byte[total];
        var copied = 0;
        var chunk = data[13..];
        chunk.CopyTo(buffer);
        copied += chunk.Length;
        while (copied < total && next != 0)
        {
            var ov = store.Get(next);
            var piece = ov.PayloadAfterHeader();
            var take = Math.Min(piece.Length, total - copied);
            piece[..take].CopyTo(buffer.AsSpan(copied));
            copied += take;
            next = ov.NextPageId;
        }

        return buffer;
    }

    public static void Delete(PageStore store, long pageId, int slot)
    {
        var page = store.Get(pageId);
        var data = page.GetSlot(slot);
        if (data.IsEmpty)
        {
            return;
        }

        if (data[0] == Overflow)
        {
            var next = BinaryPrimitives.ReadInt64LittleEndian(data[5..]);
            while (next != 0)
            {
                var ov = store.Get(next);
                var following = ov.NextPageId;
                ov.Type = PageType.Free;
                ov.ItemCount = 0;
                store.MarkDirty(ov);
                next = following;
            }
        }

        page.DeleteSlot(slot);
        store.MarkDirty(page);
        store.Superblock.Flags |= SuperblockFlags.CompactNeeded;
    }

    public static IEnumerable<(long PageId, int Slot)> EnumerateSlots(PageStore store, CollectionMeta meta)
    {
        var pageId = meta.DataHead;
        while (pageId != 0)
        {
            var page = store.Get(pageId);
            if (page.Type == PageType.Data)
            {
                for (var i = 0; i < page.ItemCount; i++)
                {
                    if (page.SlotAlive(i))
                    {
                        yield return (pageId, i);
                    }
                }
            }

            pageId = page.RightSibling;
        }
    }

    private static (long PageId, int Slot) WriteInline(PageStore store, CollectionMeta meta, ReadOnlySpan<byte> json)
    {
        var payload = new byte[5 + json.Length];
        payload[0] = Inline;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1), json.Length);
        json.CopyTo(payload.AsSpan(5));

        var page = EnsureWritableDataPage(store, meta, payload.Length);
        var slot = page.AllocSlot(payload);
        if (slot < 0)
        {
            page = AppendDataPage(store, meta);
            slot = page.AllocSlot(payload);
            if (slot < 0)
            {
                throw new NuvexaException("Unable to allocate a document slot.");
            }
        }

        store.MarkDirty(page);
        return (page.PageId, slot);
    }

    private static (long PageId, int Slot) WriteOverflow(PageStore store, CollectionMeta meta, ReadOnlySpan<byte> json)
    {
        var firstCapacity = Math.Max(0, MaxInline() - 13);
        var remainingAll = json;
        var firstLen = Math.Min(firstCapacity, remainingAll.Length);
        var remaining = remainingAll[firstLen..];

        long firstOverflow = 0;
        long prev = 0;
        while (!remaining.IsEmpty)
        {
            var ov = store.Allocate(PageType.Data);
            var chunk = Math.Min(Constants.PayloadSize - Constants.PageHeaderSize, remaining.Length);
            remaining[..chunk].CopyTo(ov.PayloadAfterHeader());
            remaining = remaining[chunk..];
            store.MarkDirty(ov);
            if (firstOverflow == 0)
            {
                firstOverflow = ov.PageId;
            }

            if (prev != 0)
            {
                var prevPage = store.Get(prev);
                prevPage.NextPageId = ov.PageId;
                store.MarkDirty(prevPage);
            }

            prev = ov.PageId;
        }

        var header = new byte[13 + firstLen];
        header[0] = Overflow;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), json.Length);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(5), firstOverflow);
        json[..firstLen].CopyTo(header.AsSpan(13));

        var page = EnsureWritableDataPage(store, meta, header.Length);
        var slot = page.AllocSlot(header);
        if (slot < 0)
        {
            page = AppendDataPage(store, meta);
            slot = page.AllocSlot(header);
        }

        store.MarkDirty(page);
        return (page.PageId, slot);
    }

    private static Page EnsureWritableDataPage(PageStore store, CollectionMeta meta, int needed)
    {
        if (meta.DataTail != 0)
        {
            var tail = store.Get(meta.DataTail);
            if (tail.Type == PageType.Data && tail.FreeSpace() >= needed + Constants.SlotSize)
            {
                return tail;
            }
        }

        if (meta.DataHead == 0)
        {
            var first = store.Allocate(PageType.Data);
            meta.DataHead = first.PageId;
            meta.DataTail = first.PageId;
            return first;
        }

        return AppendDataPage(store, meta);
    }

    private static Page AppendDataPage(PageStore store, CollectionMeta meta)
    {
        var page = store.Allocate(PageType.Data);
        if (meta.DataTail != 0)
        {
            var tail = store.Get(meta.DataTail);
            tail.RightSibling = page.PageId;
            store.MarkDirty(tail);
        }
        else
        {
            meta.DataHead = page.PageId;
        }

        meta.DataTail = page.PageId;
        return page;
    }

    private static int MaxInline() => Constants.PayloadSize - Constants.PageHeaderSize - Constants.SlotSize;
}
