// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.AI;

/// <summary>
/// Minimal RFC 6902 JSON Patch utility.
/// Supports "add", "remove", "replace" operations on a <see cref="JsonNode"/> document.
/// </summary>
public static class JsonPatch
{
    /// <summary>
    /// Applies a list of patch operations to a JSON document.
    /// Returns the modified document (root may change for root-level operations).
    /// </summary>
    public static JsonNode? Apply(JsonNode? root, IEnumerable<JsonPatchOperation> operations)
    {
        foreach (var op in operations)
        {
            root = ApplyOperation(root, op);
        }
        return root;
    }

    /// <summary>
    /// Applies a list of patch operations from a JSON byte array.
    /// </summary>
    public static JsonNode? Apply(JsonNode? root, ReadOnlySpan<byte> patchBytes)
    {
        var ops = JsonSerializer.Deserialize<JsonPatchOperation[]>(patchBytes);
        if (ops is null) return root;
        return Apply(root, ops);
    }

    private static JsonNode? ApplyOperation(JsonNode? root, JsonPatchOperation operation)
    {
        var path = operation.Path;
        if (string.IsNullOrEmpty(path)) path = "";

        var op = operation.Op?.ToLowerInvariant();
        var valueNode = operation.Value.HasValue
            ? JsonNode.Parse(operation.Value.Value.GetRawText())
            : null;

        // Root-level replace
        if (path is "" or "/")
        {
            return op switch
            {
                "replace" or "add" => valueNode,
                "remove" => null,
                _ => root
            };
        }

        if (root is null)
        {
            if (op is "add")
            {
                root = new JsonObject();
            }
            else
            {
                return root;
            }
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parent = NavigateToParent(root, segments);
        if (parent is null) return root;

        var lastSegment = segments[^1];

        switch (op)
        {
            case "add":
            case "replace":
                SetValue(parent, lastSegment, valueNode);
                break;

            case "remove":
                RemoveValue(parent, lastSegment);
                break;
        }

        return root;
    }

    private static JsonNode? NavigateToParent(JsonNode root, string[] segments)
    {
        JsonNode? current = root;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (current is null) return null;

            var segment = segments[i];
            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(segment, out var next))
                {
                    // Auto-create intermediate objects
                    var newObj = new JsonObject();
                    obj[segment] = newObj;
                    current = newObj;
                }
                else
                {
                    current = next;
                }
            }
            else if (current is JsonArray arr && int.TryParse(segment, out var index) && index >= 0 && index < arr.Count)
            {
                current = arr[index];
            }
            else
            {
                return null;
            }
        }
        return current;
    }

    private static void SetValue(JsonNode parent, string segment, JsonNode? value)
    {
        if (parent is JsonObject obj)
        {
            obj[segment] = value;
        }
        else if (parent is JsonArray arr && int.TryParse(segment, out var index))
        {
            if (segment == "-" || index >= arr.Count)
                arr.Add(value);
            else if (index >= 0 && index < arr.Count)
                arr[index] = value;
        }
    }

    private static void RemoveValue(JsonNode parent, string segment)
    {
        if (parent is JsonObject obj)
        {
            obj.Remove(segment);
        }
        else if (parent is JsonArray arr && int.TryParse(segment, out var index) && index >= 0 && index < arr.Count)
        {
            arr.RemoveAt(index);
        }
    }
}
