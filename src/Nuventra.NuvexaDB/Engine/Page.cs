using System.Buffers.Binary;

namespace Nuventra.NuvexaDB.Engine;

/// <summary>Fixed-size slotted page. Logical payload is always <see cref="Constants.PayloadSize"/> bytes.</summary>
internal sealed class Page
{
    public byte[] Buffer { get; }
    public bool Dirty { get; set; }

    public Page()
    {
        Buffer = new byte[Constants.PayloadSize];
    }

    public PageType Type
    {
        get => (PageType)Buffer[0];
        set => Buffer[0] = (byte)value;
    }

    public ushort ItemCount
    {
        get => BinaryPrimitives.ReadUInt16LittleEndian(Buffer.AsSpan(2));
        set => BinaryPrimitives.WriteUInt16LittleEndian(Buffer.AsSpan(2), value);
    }

    public uint Checksum
    {
        get => BinaryPrimitives.ReadUInt32LittleEndian(Buffer.AsSpan(4));
        set => BinaryPrimitives.WriteUInt32LittleEndian(Buffer.AsSpan(4), value);
    }

    public long PageId
    {
        get => BinaryPrimitives.ReadInt64LittleEndian(Buffer.AsSpan(8));
        set => BinaryPrimitives.WriteInt64LittleEndian(Buffer.AsSpan(8), value);
    }

    public long Lsn
    {
        get => BinaryPrimitives.ReadInt64LittleEndian(Buffer.AsSpan(16));
        set => BinaryPrimitives.WriteInt64LittleEndian(Buffer.AsSpan(16), value);
    }

    public long NextPageId
    {
        get => BinaryPrimitives.ReadInt64LittleEndian(Buffer.AsSpan(24));
        set => BinaryPrimitives.WriteInt64LittleEndian(Buffer.AsSpan(24), value);
    }

    public long RightSibling
    {
        get => BinaryPrimitives.ReadInt64LittleEndian(Buffer.AsSpan(32));
        set => BinaryPrimitives.WriteInt64LittleEndian(Buffer.AsSpan(32), value);
    }

    public static Page Create(long pageId, PageType type)
    {
        var page = new Page
        {
            Type = type,
            PageId = pageId,
            Dirty = true
        };
        return page;
    }

    public void Seal()
    {
        Checksum = 0;
        Checksum = Crc32.Compute(Buffer);
    }

    public void VerifyChecksum()
    {
        var stored = Checksum;
        Checksum = 0;
        var computed = Crc32.Compute(Buffer);
        Checksum = stored;
        if (stored != computed)
        {
            throw new NuvexaIntegrityException($"The database file is corrupt or has been tampered with (page {PageId}).");
        }
    }

    public int FreeSpace()
    {
        var dataEnd = DataEnd();
        var slotStart = Constants.PayloadSize - (ItemCount * Constants.SlotSize);
        return Math.Max(0, slotStart - dataEnd);
    }

    public int DataEnd()
    {
        var end = Constants.PageHeaderSize;
        var count = ItemCount;
        for (var i = 0; i < count; i++)
        {
            GetSlotMeta(i, out var offset, out var length);
            if (length == 0)
            {
                continue;
            }

            end = Math.Max(end, offset + length);
        }

        return end;
    }

    public int AllocSlot(ReadOnlySpan<byte> data)
    {
        if (data.Length > ushort.MaxValue)
        {
            throw new NuvexaException("Slot payload exceeds 64 KiB. Documents larger than a page are chained.");
        }

        if (FreeSpace() < data.Length + Constants.SlotSize)
        {
            return -1;
        }

        var offset = (ushort)DataEnd();
        var length = (ushort)data.Length;
        data.CopyTo(Buffer.AsSpan(offset, length));
        var index = ItemCount;
        ItemCount = (ushort)(index + 1);
        SetSlotMeta(index, offset, length);
        Dirty = true;
        return index;
    }

    public bool TryUpdateSlot(int index, ReadOnlySpan<byte> data)
    {
        GetSlotMeta(index, out _, out var length);
        if (data.Length <= length)
        {
            GetSlotMeta(index, out var offset, out _);
            data.CopyTo(Buffer.AsSpan(offset, data.Length));
            if (data.Length < length)
            {
                Buffer.AsSpan(offset + data.Length, length - data.Length).Clear();
            }

            SetSlotMeta(index, (ushort)offset, (ushort)data.Length);
            Dirty = true;
            return true;
        }

        DeleteSlot(index);
        var newIndex = AllocSlot(data);
        if (newIndex < 0)
        {
            return false;
        }

        if (newIndex != index)
        {
            // Move the new slot into the original index so locators stay stable.
            GetSlotMeta(newIndex, out var off, out var len);
            GetSlotMeta(index, out _, out _);
            SetSlotMeta(index, off, len);
            SetSlotMeta(newIndex, 0, 0);
            ItemCount--;
        }

        Dirty = true;
        return true;
    }

    public void DeleteSlot(int index)
    {
        SetSlotMeta(index, 0, 0);
        Dirty = true;
    }

    public ReadOnlySpan<byte> GetSlot(int index)
    {
        GetSlotMeta(index, out var offset, out var length);
        if (length == 0)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        return Buffer.AsSpan(offset, length);
    }

    public bool SlotAlive(int index)
    {
        GetSlotMeta(index, out _, out var length);
        return length > 0;
    }

    public void GetSlotMeta(int index, out ushort offset, out ushort length)
    {
        var pos = Constants.PayloadSize - ((index + 1) * Constants.SlotSize);
        offset = BinaryPrimitives.ReadUInt16LittleEndian(Buffer.AsSpan(pos));
        length = BinaryPrimitives.ReadUInt16LittleEndian(Buffer.AsSpan(pos + 2));
    }

    private void SetSlotMeta(int index, ushort offset, ushort length)
    {
        var pos = Constants.PayloadSize - ((index + 1) * Constants.SlotSize);
        BinaryPrimitives.WriteUInt16LittleEndian(Buffer.AsSpan(pos), offset);
        BinaryPrimitives.WriteUInt16LittleEndian(Buffer.AsSpan(pos + 2), length);
    }

    public Span<byte> PayloadAfterHeader() => Buffer.AsSpan(Constants.PageHeaderSize);
}
