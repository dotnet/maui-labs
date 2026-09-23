using System.Text.Json.Nodes;

namespace ChatClientPlayground.Models;

/// <summary>One IChatClient request and its streaming updates or final response.</summary>
public sealed class RecordedInteraction
{
    public int Sequence { get; set; }
    public bool IsStreaming { get; set; }
    public JsonObject Request { get; set; } = new();
    public List<JsonObject> Updates { get; set; } = [];
    public JsonObject? Response { get; set; }
}
