using System.Buffers.Binary;

namespace Nuventra.NuvexaDB.Engine;

internal readonly struct IndexEntry
{
    public readonly byte[] Key;
    public readonly long PageOrChild;
    public readonly ushort Slot;

    public IndexEntry(byte[] key, long pageOrChild, ushort slot = 0)
    {
        Key = key;
        PageOrChild = pageOrChild;
        Slot = slot;
    }

    public int EncodedSize(bool leaf) => 2 + Key.Length + 8 + (leaf ? 2 : 0);

    public static int Compare(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) => a.SequenceCompareTo(b);
}

internal static class IndexCodec
{
    public static List<IndexEntry> Read(Page page, bool leaf)
    {
        var list = new List<IndexEntry>(page.ItemCount);
        var span = page.PayloadAfterHeader();
        var offset = 0;
        for (var i = 0; i < page.ItemCount; i++)
        {
            var keyLen = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
            offset += 2;
            var key = span.Slice(offset, keyLen).ToArray();
            offset += keyLen;
            var pageId = BinaryPrimitives.ReadInt64LittleEndian(span[offset..]);
            offset += 8;
            ushort slot = 0;
            if (leaf)
            {
                slot = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
                offset += 2;
            }

            list.Add(new IndexEntry(key, pageId, slot));
        }

        return list;
    }

    public static bool TryWrite(Page page, IReadOnlyList<IndexEntry> entries, bool leaf)
    {
        var needed = 0;
        foreach (var e in entries)
        {
            needed += e.EncodedSize(leaf);
        }

        if (needed > Constants.PayloadSize - Constants.PageHeaderSize)
        {
            return false;
        }

        page.PayloadAfterHeader().Clear();
        var span = page.PayloadAfterHeader();
        var offset = 0;
        foreach (var e in entries)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], (ushort)e.Key.Length);
            offset += 2;
            e.Key.CopyTo(span[offset..]);
            offset += e.Key.Length;
            BinaryPrimitives.WriteInt64LittleEndian(span[offset..], e.PageOrChild);
            offset += 8;
            if (leaf)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], e.Slot);
                offset += 2;
            }
        }

        page.ItemCount = (ushort)entries.Count;
        page.Dirty = true;
        return true;
    }

    public static int Search(IReadOnlyList<IndexEntry> entries, ReadOnlySpan<byte> key)
    {
        var lo = 0;
        var hi = entries.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var cmp = IndexEntry.Compare(entries[mid].Key, key);
            if (cmp == 0)
            {
                return mid;
            }

            if (cmp < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return ~lo;
    }

    public static int ChildIndex(IReadOnlyList<IndexEntry> internals, ReadOnlySpan<byte> key)
    {
        // Internal keys are the first key of the right sibling. Descend left when key < entry.Key.
        var i = 0;
        while (i < internals.Count && IndexEntry.Compare(key, internals[i].Key) >= 0)
        {
            i++;
        }

        return Math.Max(0, i - 1);
    }
}
