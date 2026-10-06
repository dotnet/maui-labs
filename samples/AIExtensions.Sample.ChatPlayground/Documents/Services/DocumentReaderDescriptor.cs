namespace AIExtensions.Sample.ChatPlayground;

/// <summary>UI label and ID for one registered document reader.</summary>
public sealed record DocumentReaderDescriptor(
    string Id,
    string DisplayName,
    string Description,
    bool IsCloud = false);
