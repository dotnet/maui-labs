using System.Text.Json.Nodes;

namespace AIExtensions.Sample.ChatPlayground.Features.Recording;

/// <summary>Versioned interaction data written by the playground.</summary>
public sealed class ChatRecording
{
    public const string FormatName = "chat-client-playground-recording";
    public const int CurrentSchemaVersion = 2;

    public string Format { get; set; } = FormatName;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<RecordedInteraction> Interactions { get; set; } = [];
}

/// <summary>One IChatClient request and its streaming updates or final response.</summary>
public sealed class RecordedInteraction
{
    public int Sequence { get; set; }
    public bool IsStreaming { get; set; }
    public JsonObject Request { get; set; } = new();
    public List<JsonObject> Updates { get; set; } = [];
    public JsonObject? Response { get; set; }
}
