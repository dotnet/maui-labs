// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.AI;

/// <summary>
/// Represents a single JSON Patch operation (RFC 6902) for state delta updates.
/// </summary>
public class JsonPatchOperation
{
    /// <summary>The operation type (e.g., "replace", "add", "remove").</summary>
    [JsonPropertyName("op")]
    public required string Op { get; set; }

    /// <summary>The JSON pointer path (e.g., "/steps/0/status").</summary>
    [JsonPropertyName("path")]
    public required string Path { get; set; }

    /// <summary>The value to apply (for "replace" and "add" operations).</summary>
    [JsonPropertyName("value")]
    public JsonElement? Value { get; set; }

    /// <summary>Source path for "move" and "copy" operations.</summary>
    [JsonPropertyName("from")]
    public string? From { get; set; }
}
