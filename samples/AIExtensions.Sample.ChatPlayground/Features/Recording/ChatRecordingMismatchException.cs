using System.Text.Json.Nodes;

namespace AIExtensions.Sample.ChatPlayground.Features.Recording;

/// <summary>Reports the first difference between a replay request and the tape.</summary>
public sealed class ChatRecordingMismatchException : InvalidOperationException
{
    public ChatRecordingMismatchException(int interaction, string path, JsonNode? expected, JsonNode? actual)
        : base($"Chat playground recording interaction {interaction + 1} mismatched at {path}. " +
            $"Expected {expected?.ToJsonString() ?? "null"}; actual {actual?.ToJsonString() ?? "null"}.")
    {
        Interaction = interaction;
        Path = path;
    }

    public int Interaction { get; }
    public string Path { get; }
}
