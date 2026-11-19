namespace AIExtensions.Sample.ChatPlayground;

public sealed record GeneratedImage(
    byte[]? Bytes,
    Uri? Uri,
    string MediaType);
