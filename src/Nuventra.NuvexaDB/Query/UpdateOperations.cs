using System.Text.Json;
using System.Text.Json.Nodes;
using Nuventra.NuvexaDB.Documents;

namespace Nuventra.NuvexaDB.Query;

internal static class UpdateOperations
{
    public static NuvexaDocument Apply(NuvexaDocument document, string updateJson)
    {
        using var doc = JsonDocument.Parse(updateJson);
        var root = document.Root;
        foreach (var op in doc.RootElement.EnumerateObject())
        {
            switch (op.Name)
            {
                case "$set":
                    foreach (var f in op.Value.EnumerateObject())
                    {
                        document[f.Name] = JsonNode.Parse(f.Value.GetRawText());
                    }

                    break;
                case "$unset":
                    foreach (var f in op.Value.EnumerateObject())
                    {
                        Unset(root, f.Name);
                    }

                    break;
                case "$inc":
                    foreach (var f in op.Value.EnumerateObject())
                    {
                        var el = document.AsElement();
                        DocumentPath.TryGet(el, f.Name, out var current);
                        var start = current.ValueKind == JsonValueKind.Number ? current.GetDouble() : 0;
                        document[f.Name] = start + f.Value.GetDouble();
                    }

                    break;
                case "$push":
                    foreach (var f in op.Value.EnumerateObject())
                    {
                        if (root[f.Name] is not JsonArray arr)
                        {
                            arr = [];
                            root[f.Name] = arr;
                        }

                        arr.Add(JsonNode.Parse(f.Value.GetRawText()));
                    }

                    break;
                case "$pull":
                    foreach (var f in op.Value.EnumerateObject())
                    {
                        if (root[f.Name] is JsonArray arr)
                        {
                            for (var i = arr.Count - 1; i >= 0; i--)
                            {
                                if (arr[i]?.ToJsonString() == f.Value.GetRawText())
                                {
                                    arr.RemoveAt(i);
                                }
                            }
                        }
                    }

                    break;
                default:
                    throw new NuvexaException($"Unsupported update operator '{op.Name}'.");
            }
        }

        document.EnsureId();
        return document;
    }

    private static void Unset(JsonNode root, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        JsonNode? current = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            current = current?[parts[i]];
        }

        if (current is JsonObject obj)
        {
            obj.Remove(parts[^1]);
        }
    }
}
