using System.Text.Json;
using System.Text.Json.Nodes;
using Nuventra.NuvexaDB.Documents;

namespace Nuventra.NuvexaDB;

/// <summary>A JSON document stored in a NuvexaDB collection.</summary>
public sealed class NuvexaDocument
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public JsonNode Root { get; }

    public NuvexaDocument(JsonNode root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        EnsureId();
    }

    public string Id
    {
        get => Root["_id"]?.ToString() ?? "";
        set => Root["_id"] = value;
    }

    public JsonNode? this[string path]
    {
        get
        {
            var el = Root.Deserialize<JsonElement>();
            return DocumentPath.TryGet(el, path, out var value) ? JsonNode.Parse(value.GetRawText()) : null;
        }
        set => SetPath(path, value);
    }

    public static NuvexaDocument Parse(string json)
    {
        var node = JsonNode.Parse(json) ?? throw new NuvexaException("Document JSON is empty.");
        if (node is not JsonObject)
        {
            throw new NuvexaException("A NuvexaDB document must be a JSON object.");
        }

        return new NuvexaDocument(node);
    }

    public static NuvexaDocument FromObject<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value, SerializerOptions)
                   ?? throw new NuvexaException("Unable to serialize the document.");
        if (node is not JsonObject)
        {
            throw new NuvexaException("A NuvexaDB document must be a JSON object.");
        }

        return new NuvexaDocument(node);
    }

    public T Deserialize<T>() => Root.Deserialize<T>(SerializerOptions)
                                 ?? throw new NuvexaException("Unable to deserialize the document.");

    public string ToJson() => Root.ToJsonString(SerializerOptions);

    public JsonElement AsElement() => JsonSerializer.SerializeToElement(Root);

    internal byte[] ToStorageBytes() => Documents.BsonCodec.Encode(Root);

    internal static NuvexaDocument FromStorage(ReadOnlySpan<byte> bytes) =>
        new(Documents.BsonCodec.DecodeObject(bytes));

    internal void EnsureId()
    {
        if (Root["_id"] is null || string.IsNullOrWhiteSpace(Root["_id"]?.ToString()))
        {
            Root["_id"] = NuvexaObjectId.NewId();
        }
    }

    private void SetPath(string path, JsonNode? value)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        JsonNode current = Root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (current[parts[i]] is not JsonObject next)
            {
                next = new JsonObject();
                current[parts[i]] = next;
            }

            current = next;
        }

        current[parts[^1]] = value;
    }
}
