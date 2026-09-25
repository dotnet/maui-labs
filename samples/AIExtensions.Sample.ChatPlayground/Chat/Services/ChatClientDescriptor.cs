namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Metadata exposed by a configured chat-client slot.</summary>
public sealed record ChatClientDescriptor(
    string Id,
    string DisplayName,
    string Description,
    bool SupportsImageInput = false,
    bool SupportsReasoningSummary = false,
    bool SupportsImageGeneration = false,
    bool IsReplay = false,
    bool SupportsToolCalling = false);
