namespace ChatClientPlayground.Services;

/// <summary>Metadata exposed by a configured chat-client slot.</summary>
public sealed record ChatClientDescriptor(
    string DisplayName,
    string Status,
    bool SupportsImageInput);
