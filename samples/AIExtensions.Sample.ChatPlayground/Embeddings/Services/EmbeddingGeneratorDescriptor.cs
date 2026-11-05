namespace AIExtensions.Sample.ChatPlayground;

/// <summary>UI label and ID for one registered embedding generator.</summary>
public sealed record EmbeddingGeneratorDescriptor(
    string Id,
    string DisplayName,
    string Description);
