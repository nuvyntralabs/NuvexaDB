using System.Text.Json.Nodes;

namespace Nuventra.NuvexaDB.Tools;

/// <summary>Read-only tree for a selected document JSON pane.</summary>
public sealed class JsonDocumentNode
{
    public JsonDocumentNode(string name, string value, IReadOnlyList<JsonDocumentNode>? children = null)
    {
        Name = name;
        Value = value;
        Children = children ?? [];
    }

    public string Name { get; }
    public string Value { get; }
    public IReadOnlyList<JsonDocumentNode> Children { get; }
    public string Caption => string.IsNullOrEmpty(Value) ? Name : $"{Name}: {Value}";
}

public static class JsonDocumentTree
{
    public static IReadOnlyList<JsonDocumentNode> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var node = JsonNode.Parse(json);
            return node is null ? [] : [FromNode("(root)", node)];
        }
        catch (System.Text.Json.JsonException)
        {
            return [new JsonDocumentNode("(invalid JSON)", json.Trim())];
        }
    }

    private static JsonDocumentNode FromNode(string name, JsonNode node) => node switch
    {
        JsonObject obj => new JsonDocumentNode(name, "", obj
            .Select(p => p.Value is null
                ? new JsonDocumentNode(p.Key, "null")
                : FromNode(p.Key, p.Value))
            .ToList()),
        JsonArray array => new JsonDocumentNode(name, $"[{array.Count}]", array
            .Select((item, i) => item is null
                ? new JsonDocumentNode($"[{i}]", "null")
                : FromNode($"[{i}]", item))
            .ToList()),
        JsonValue value => new JsonDocumentNode(name, ValueText(value)),
        _ => new JsonDocumentNode(name, node.ToJsonString())
    };

    private static string ValueText(JsonValue value)
    {
        if (value.TryGetValue<bool>(out var flag))
        {
            return flag ? "true" : "false";
        }

        if (value.TryGetValue<string>(out var text))
        {
            return text;
        }

        return value.ToJsonString();
    }
}
