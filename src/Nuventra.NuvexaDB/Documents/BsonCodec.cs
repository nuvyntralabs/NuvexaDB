using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nuventra.NuvexaDB.Documents;

/// <summary>
/// BSON document payload for .nvx data pages. Public APIs stay JSON.
/// Reads legacy UTF-8 JSON pages so existing files keep working.
/// </summary>
internal static class BsonCodec
{
    private const byte Double = 0x01;
    private const byte String = 0x02;
    private const byte Document = 0x03;
    private const byte Array = 0x04;
    private const byte Binary = 0x05;
    private const byte ObjectId = 0x07;
    private const byte Boolean = 0x08;
    private const byte DateTime = 0x09;
    private const byte Null = 0x0A;
    private const byte Int32 = 0x10;
    private const byte Timestamp = 0x11;
    private const byte Int64 = 0x12;
    private const byte Decimal128 = 0x13;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static byte[] Encode(JsonNode root)
    {
        if (root is not JsonObject)
        {
            throw new NuvexaException("A NuvexaDB document must be a JSON object.");
        }

        using var stream = new MemoryStream();
        WriteDocument(stream, root.AsObject());
        return stream.ToArray();
    }

    public static JsonObject DecodeObject(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            throw new NuvexaException("Document payload is empty.");
        }

        if (LooksLikeJson(bytes))
        {
            var json = Engine.Constants.Utf8.GetString(bytes);
            var node = JsonNode.Parse(json) ?? throw new NuvexaException("Document JSON is empty.");
            if (node is not JsonObject obj)
            {
                throw new NuvexaException("A NuvexaDB document must be a JSON object.");
            }

            return obj;
        }

        var offset = 0;
        var decoded = ReadDocument(bytes, ref offset);
        return decoded;
    }

    internal static bool LooksLikeJson(ReadOnlySpan<byte> bytes)
    {
        var i = 0;
        while (i < bytes.Length && bytes[i] is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t')
        {
            i++;
        }

        if (i >= bytes.Length || bytes[i] != (byte)'{')
        {
            return false;
        }

        if (i + 1 >= bytes.Length)
        {
            return true;
        }

        var next = bytes[i + 1];
        return next is (byte)'"' or (byte)'}' or (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t';
    }

    private static void WriteDocument(Stream stream, JsonObject obj)
    {
        var start = stream.Position;
        stream.Write(stackalloc byte[4]);
        foreach (var property in obj)
        {
            WriteElement(stream, property.Key, property.Value);
        }

        stream.WriteByte(0);
        var end = stream.Position;
        var size = (int)(end - start);
        stream.Position = start;
        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, size);
        stream.Write(header);
        stream.Position = end;
    }

    private static void WriteArray(Stream stream, JsonArray array)
    {
        var start = stream.Position;
        stream.Write(stackalloc byte[4]);
        for (var i = 0; i < array.Count; i++)
        {
            WriteElement(stream, i.ToString(CultureInfo.InvariantCulture), array[i]);
        }

        stream.WriteByte(0);
        var end = stream.Position;
        var size = (int)(end - start);
        stream.Position = start;
        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, size);
        stream.Write(header);
        stream.Position = end;
    }

    private static void WriteElement(Stream stream, string name, JsonNode? value)
    {
        var typePos = stream.Position;
        stream.WriteByte(0);
        WriteCString(stream, name);
        var type = WriteValue(stream, value);
        var resume = stream.Position;
        stream.Position = typePos;
        stream.WriteByte(type);
        stream.Position = resume;
    }

    private static byte WriteValue(Stream stream, JsonNode? value)
    {
        if (value is null)
        {
            return Null;
        }

        switch (value)
        {
            case JsonObject obj:
                WriteDocument(stream, obj);
                return Document;
            case JsonArray array:
                WriteArray(stream, array);
                return Array;
            case JsonValue jsonValue:
                if (jsonValue.TryGetValue<bool>(out var b))
                {
                    stream.WriteByte(b ? (byte)1 : (byte)0);
                    return Boolean;
                }

                if (jsonValue.TryGetValue<string>(out var s))
                {
                    WriteBsonString(stream, s ?? "");
                    return String;
                }

                if (TryWriteNumber(stream, jsonValue, out var numberType))
                {
                    return numberType;
                }

                WriteBsonString(stream, jsonValue.ToJsonString());
                return String;
            default:
                WriteBsonString(stream, value.ToJsonString());
                return String;
        }
    }

    private static bool TryWriteNumber(Stream stream, JsonValue value, out byte type)
    {
        if (value.TryGetValue<int>(out var i32))
        {
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buf, i32);
            stream.Write(buf);
            type = Int32;
            return true;
        }

        if (value.TryGetValue<long>(out var i64))
        {
            if (i64 is >= int.MinValue and <= int.MaxValue)
            {
                Span<byte> small = stackalloc byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(small, (int)i64);
                stream.Write(small);
                type = Int32;
                return true;
            }

            Span<byte> wide = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(wide, i64);
            stream.Write(wide);
            type = Int64;
            return true;
        }

        if (value.TryGetValue<double>(out var d))
        {
            if (double.IsInteger(d) && d is >= int.MinValue and <= int.MaxValue)
            {
                Span<byte> small = stackalloc byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(small, (int)d);
                stream.Write(small);
                type = Int32;
                return true;
            }

            Span<byte> bits = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(bits, BitConverter.DoubleToInt64Bits(d));
            stream.Write(bits);
            type = Double;
            return true;
        }

        var raw = value.ToJsonString();
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong))
        {
            return TryWriteNumber(stream, JsonValue.Create(parsedLong)!, out type);
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble))
        {
            return TryWriteNumber(stream, JsonValue.Create(parsedDouble)!, out type);
        }

        type = 0;
        return false;
    }

    private static void WriteBsonString(Stream stream, string value)
    {
        var utf8 = Utf8.GetBytes(value);
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(len, utf8.Length + 1);
        stream.Write(len);
        stream.Write(utf8);
        stream.WriteByte(0);
    }

    private static void WriteCString(Stream stream, string value)
    {
        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new NuvexaException("BSON field names cannot contain a NUL byte.");
        }

        stream.Write(Utf8.GetBytes(value));
        stream.WriteByte(0);
    }

    private static JsonObject ReadDocument(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset + 4 > data.Length)
        {
            throw new NuvexaException("BSON document is truncated.");
        }

        var size = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
        if (size < 5 || offset + size > data.Length)
        {
            throw new NuvexaException("BSON document length is invalid.");
        }

        var end = offset + size;
        offset += 4;
        var obj = new JsonObject();
        while (offset < end)
        {
            var type = data[offset++];
            if (type == 0)
            {
                if (offset != end)
                {
                    throw new NuvexaException("BSON document terminator is not at the declared end.");
                }

                return obj;
            }

            var name = ReadCString(data, ref offset);
            obj[name] = ReadValue(type, data, ref offset);
        }

        throw new NuvexaException("BSON document is missing a terminator.");
    }

    private static JsonArray ReadArray(ReadOnlySpan<byte> data, ref int offset)
    {
        var obj = ReadDocument(data, ref offset);
        var array = new JsonArray();
        var index = 0;
        while (obj.TryGetPropertyValue(index.ToString(CultureInfo.InvariantCulture), out var item))
        {
            obj.Remove(index.ToString(CultureInfo.InvariantCulture));
            array.Add(item is null ? null : item.DeepClone());
            index++;
        }

        foreach (var leftover in obj)
        {
            array.Add(leftover.Value is null ? null : leftover.Value.DeepClone());
        }

        return array;
    }

    private static JsonNode? ReadValue(byte type, ReadOnlySpan<byte> data, ref int offset) => type switch
    {
        Double => JsonValue.Create(ReadDouble(data, ref offset)),
        String => JsonValue.Create(ReadBsonString(data, ref offset)),
        Document => ReadDocument(data, ref offset),
        Array => ReadArray(data, ref offset),
        Binary => ReadBinaryAsBase64(data, ref offset),
        ObjectId => JsonValue.Create(Convert.ToHexString(ReadExact(data, ref offset, 12)).ToLowerInvariant()),
        Boolean => JsonValue.Create(ReadByte(data, ref offset) != 0),
        DateTime => JsonValue.Create(DateTimeOffset.FromUnixTimeMilliseconds(ReadInt64(data, ref offset)).UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
        Null => null,
        Int32 => JsonValue.Create(ReadInt32(data, ref offset)),
        Timestamp => JsonValue.Create(ReadInt64(data, ref offset)),
        Int64 => JsonValue.Create(ReadInt64(data, ref offset)),
        Decimal128 => JsonValue.Create(Convert.ToHexString(ReadExact(data, ref offset, 16))),
        _ => throw new NuvexaException($"Unsupported BSON type 0x{type:X2}.")
    };

    private static JsonValue ReadBinaryAsBase64(ReadOnlySpan<byte> data, ref int offset)
    {
        var len = ReadInt32(data, ref offset);
        _ = ReadByte(data, ref offset);
        var payload = ReadExact(data, ref offset, len);
        return JsonValue.Create(Convert.ToBase64String(payload));
    }

    private static string ReadBsonString(ReadOnlySpan<byte> data, ref int offset)
    {
        var len = ReadInt32(data, ref offset);
        if (len < 1 || offset + len > data.Length)
        {
            throw new NuvexaException("BSON string length is invalid.");
        }

        var text = Utf8.GetString(data.Slice(offset, len - 1));
        offset += len;
        return text;
    }

    private static string ReadCString(ReadOnlySpan<byte> data, ref int offset)
    {
        var start = offset;
        while (offset < data.Length && data[offset] != 0)
        {
            offset++;
        }

        if (offset >= data.Length)
        {
            throw new NuvexaException("BSON field name is not NUL-terminated.");
        }

        var name = Utf8.GetString(data[start..offset]);
        offset++;
        return name;
    }

    private static byte ReadByte(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset >= data.Length)
        {
            throw new NuvexaException("BSON value is truncated.");
        }

        return data[offset++];
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset + 4 > data.Length)
        {
            throw new NuvexaException("BSON value is truncated.");
        }

        var value = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
        offset += 4;
        return value;
    }

    private static long ReadInt64(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset + 8 > data.Length)
        {
            throw new NuvexaException("BSON value is truncated.");
        }

        var value = BinaryPrimitives.ReadInt64LittleEndian(data[offset..]);
        offset += 8;
        return value;
    }

    private static double ReadDouble(ReadOnlySpan<byte> data, ref int offset)
    {
        var bits = ReadInt64(data, ref offset);
        return BitConverter.Int64BitsToDouble(bits);
    }

    private static byte[] ReadExact(ReadOnlySpan<byte> data, ref int offset, int count)
    {
        if (count < 0 || offset + count > data.Length)
        {
            throw new NuvexaException("BSON value is truncated.");
        }

        var copy = data.Slice(offset, count).ToArray();
        offset += count;
        return copy;
    }
}
