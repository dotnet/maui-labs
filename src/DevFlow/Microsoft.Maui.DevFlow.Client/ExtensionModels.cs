using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Driver;

public sealed class ExtensionDescriptor
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("tools")]
    public List<ExtensionToolInfo> Tools { get; set; } = [];
}

public sealed class ExtensionToolInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("method")]
    public string Method { get; set; } = "GET";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("parameters")]
    public JsonElement? Parameters { get; set; }

    [JsonPropertyName("returns")]
    public JsonElement? Returns { get; set; }

    [JsonPropertyName("annotations")]
    public ExtensionToolAnnotationsInfo? Annotations { get; set; }
}

public sealed class ExtensionToolAnnotationsInfo
{
    [JsonPropertyName("readOnly")]
    public bool ReadOnly { get; set; }

    [JsonPropertyName("idempotent")]
    public bool Idempotent { get; set; }

    [JsonPropertyName("destructive")]
    public bool Destructive { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }
}

public sealed class ExtensionsMarker
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";
}

public sealed class AgentCapabilitiesResponse
{
    [JsonPropertyName("extensions")]
    public Dictionary<string, ExtensionDescriptor>? Extensions { get; set; }
}
