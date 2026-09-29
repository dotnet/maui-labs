namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Retains a JSON fragment from a Foundry Mistral OCR response.</summary>
public sealed record FoundryMistralOcrRawReference(
    string Path,
    string Json);
