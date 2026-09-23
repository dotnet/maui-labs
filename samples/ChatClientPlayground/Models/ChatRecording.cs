namespace ChatClientPlayground.Models;

/// <summary>Versioned interaction data written by the playground.</summary>
public sealed class ChatRecording
{
    public const string FormatName = "chat-client-playground-recording";
    public const int CurrentSchemaVersion = 1;

    public string Format { get; set; } = FormatName;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<RecordedInteraction> Interactions { get; set; } = [];
}
