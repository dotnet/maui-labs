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

    /// <summary>Event streams the extension publishes. Empty for agents older than plugin support.</summary>
    [JsonPropertyName("streams")]
    public List<ExtensionStreamInfo> Streams { get; set; } = [];

    /// <summary>Short prefix MCP uses for this extension's tools, when the extension sets one.</summary>
    [JsonPropertyName("toolPrefix")]
    public string? ToolPrefix { get; set; }

    /// <summary><c>ready</c> or <c>faulted</c> for plugins; null for inline extensions.</summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }

    /// <summary>Why a plugin faulted.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class ExtensionStreamInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>Agent path for polling, e.g. <c>/api/v1/ext/org.shiny.ble/streams/events</c>.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>Agent path for the live WebSocket feed.</summary>
    [JsonPropertyName("webSocketPath")]
    public string WebSocketPath { get; set; } = "";

    /// <summary>Event fields a viewer should show as columns.</summary>
    [JsonPropertyName("columns")]
    public List<string> Columns { get; set; } = [];
}

/// <summary>One event from an extension stream.</summary>
public sealed class ExtensionStreamEvent
{
    [JsonPropertyName("seq")]
    public long Sequence { get; set; }

    [JsonPropertyName("ts")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }

    /// <summary>Events lost before this one because the agent's buffer was full.</summary>
    [JsonPropertyName("dropped")]
    public long Dropped { get; set; }
}

/// <summary>A page of events from <c>GET /api/v1/ext/{namespace}/streams/{name}</c>.</summary>
public sealed class ExtensionStreamPage
{
    [JsonPropertyName("events")]
    public List<ExtensionStreamEvent> Events { get; set; } = [];

    /// <summary>Pass as <c>since</c> on the next read.</summary>
    [JsonPropertyName("nextSequence")]
    public long NextSequence { get; set; }

    /// <summary>Events after <c>since</c> that had already left the agent's buffer.</summary>
    [JsonPropertyName("dropped")]
    public long Dropped { get; set; }
}

public sealed class ExtensionLoadErrorInfo
{
    [JsonPropertyName("extension")]
    public string Extension { get; set; } = "";

    [JsonPropertyName("error")]
    public string Error { get; set; } = "";
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

    [JsonPropertyName("extensionErrors")]
    public List<ExtensionLoadErrorInfo>? ExtensionErrors { get; set; }
}
