namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Retains the structured JSON returned by a deployed Foundry model.</summary>
public sealed record FoundryModelDocumentRawReference(
    string Path,
    string Json);
