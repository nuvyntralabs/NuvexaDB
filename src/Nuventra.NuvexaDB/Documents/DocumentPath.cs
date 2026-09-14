using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;

namespace Nuventra.NuvexaDB.Documents;

internal static class DocumentPath
{
    public static bool TryGet(JsonElement root, string path, out JsonElement value)
    {
        value = default;
        var current = root;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                return false;
            }
        }

        value = current;
        return true;
    }

    public static int Compare(JsonElement a, JsonElement b)
    {
        if (TryNumber(a, out var an) && TryNumber(b, out var bn))
        {
            return an.CompareTo(bn);
        }

        if (a.ValueKind == JsonValueKind.String && b.ValueKind == JsonValueKind.String)
        {
            return string.CompareOrdinal(a.GetString(), b.GetString());
        }

        if (a.ValueKind == JsonValueKind.True || a.ValueKind == JsonValueKind.False)
        {
            if (b.ValueKind == JsonValueKind.True || b.ValueKind == JsonValueKind.False)
            {
                return a.GetBoolean().CompareTo(b.GetBoolean());
            }
        }

        if (a.ValueKind == JsonValueKind.Null && b.ValueKind == JsonValueKind.Null)
        {
            return 0;
        }

        return string.CompareOrdinal(a.GetRawText(), b.GetRawText());
    }

    public static bool EqualsValue(JsonElement a, JsonElement b) => Compare(a, b) == 0;

    public static bool TryNumber(JsonElement el, out double value)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out value))
        {
            return true;
        }

        if (el.ValueKind == JsonValueKind.String &&
            double.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    internal const char CompoundSeparator = '\u001f';

    public static byte[] IndexKey(JsonElement value, string documentId, ushort formatVersion) =>
        Concat(EncodeIndexValue(value, formatVersion), (byte)0, Engine.Constants.Utf8.GetBytes(documentId));

    public static byte[] IndexScanPrefix(JsonElement value, ushort formatVersion) =>
        Concat(EncodeIndexValue(value, formatVersion), (byte)0);

    public static byte[] IndexScanPrefixSuccessor(JsonElement value, ushort formatVersion) =>
        Successor(IndexScanPrefix(value, formatVersion));

    public static byte[] CompoundIndexKey(IReadOnlyList<JsonElement> values, string documentId, ushort formatVersion) =>
        Concat(EncodeCompoundValue(values, formatVersion), (byte)0, Engine.Constants.Utf8.GetBytes(documentId));

    public static byte[] CompoundScanPrefix(IReadOnlyList<JsonElement> values, ushort formatVersion) =>
        Concat(EncodeCompoundValue(values, formatVersion), (byte)0);

    public static byte[] CompoundScanPrefixSuccessor(IReadOnlyList<JsonElement> values, ushort formatVersion) =>
        Successor(CompoundScanPrefix(values, formatVersion));

    /// <summary>Inclusive lower bound for the first field of a compound index.</summary>
    public static byte[] CompoundFirstFieldPrefix(JsonElement value, ushort formatVersion) =>
        Concat(EncodeIndexValue(value, formatVersion), (byte)CompoundSeparator);

    public static byte[] CompoundFirstFieldPrefixSuccessor(JsonElement value, ushort formatVersion) =>
        Successor(CompoundFirstFieldPrefix(value, formatVersion));

    public static string JoinIndexPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            throw new NuvexaException("A compound index needs at least one field.");
        }

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new NuvexaException("Index field paths cannot be empty.");
            }
        }

        return string.Join(CompoundSeparator, paths);
    }

    public static string[] SplitIndexPaths(string fieldPath) =>
        string.IsNullOrEmpty(fieldPath)
            ? []
            : fieldPath.Split(CompoundSeparator, StringSplitOptions.RemoveEmptyEntries);

    public static string DisplayIndexPath(string fieldPath) => string.Join(",", SplitIndexPaths(fieldPath));

    public static bool IsCompoundIndexPath(string fieldPath) => fieldPath.Contains(CompoundSeparator);

    private static byte[] EncodeCompoundValue(IReadOnlyList<JsonElement> values, ushort formatVersion)
    {
        var parts = new byte[values.Count][];
        var total = 0;
        for (var i = 0; i < values.Count; i++)
        {
            parts[i] = EncodeIndexValue(values[i], formatVersion);
            total += parts[i].Length;
            if (i > 0)
            {
                total++;
            }
        }

        var dest = new byte[total];
        var offset = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                dest[offset++] = (byte)CompoundSeparator;
            }

            parts[i].CopyTo(dest, offset);
            offset += parts[i].Length;
        }

        return dest;
    }

    private static byte[] Successor(byte[] prefix)
    {
        var next = (byte[])prefix.Clone();
        next[^1] = (byte)(next[^1] + 1);
        return next;
    }

    private static byte[] Concat(byte[] left, byte mid, byte[]? right = null)
    {
        var dest = new byte[left.Length + 1 + (right?.Length ?? 0)];
        left.CopyTo(dest, 0);
        dest[left.Length] = mid;
        right?.CopyTo(dest, left.Length + 1);
        return dest;
    }

    /// <summary>
    /// v1 numbers are <c>n:</c> + G17 text (not numeric-order-preserving).
    /// v2 numbers are <c>d:</c> + 8 IEEE754 sortable bytes (lex order = numeric order).
    /// </summary>
    private static byte[] EncodeIndexValue(JsonElement value, ushort formatVersion)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n))
        {
            if (formatVersion >= 2)
            {
                return EncodeSortableDouble(n);
            }

            return Engine.Constants.Utf8.GetBytes("n:" + n.ToString("G17", CultureInfo.InvariantCulture));
        }

        return Engine.Constants.Utf8.GetBytes(value.ValueKind switch
        {
            JsonValueKind.String => "s:" + value.GetString(),
            JsonValueKind.True => "b:1",
            JsonValueKind.False => "b:0",
            JsonValueKind.Null => "z:",
            _ => "j:" + value.GetRawText()
        });
    }

    internal static byte[] EncodeSortableDouble(double value)
    {
        var bits = (ulong)BitConverter.DoubleToInt64Bits(value);
        var sortable = (bits & 0x8000_0000_0000_0000UL) != 0
            ? ~bits
            : bits | 0x8000_0000_0000_0000UL;
        var buf = new byte[10];
        buf[0] = (byte)'d';
        buf[1] = (byte)':';
        BinaryPrimitives.WriteUInt64BigEndian(buf.AsSpan(2), sortable);
        return buf;
    }

    public static byte[] IdKey(string id) => Engine.Constants.Utf8.GetBytes(id);
}
