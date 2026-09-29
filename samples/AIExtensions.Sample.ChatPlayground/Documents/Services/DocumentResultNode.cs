using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>One indented row in the structured document result.</summary>
public sealed class DocumentResultNode
{
    internal Func<string>? RawJsonFactory { get; init; }

    public required int Depth { get; init; }

    public required string Title { get; init; }

    public required string JsonPath { get; init; }

    public required int PageNumber { get; init; }

    public string? Subtitle { get; init; }

    public string? Metadata { get; init; }

    public DocumentBoundingRegion? BoundingRegion { get; init; }

    public DocumentRegionKind RegionKind { get; init; }

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    public bool HasMetadata => !string.IsNullOrWhiteSpace(Metadata);

    public bool HasRawJson => RawJsonFactory is not null;

    public Thickness Indent => new(Depth * 16, 2, 2, 2);

    public string GetRawJson() =>
        RawJsonFactory?.Invoke()
        ?? throw new InvalidOperationException($"'{Title}' has no provider raw JSON.");
}

public enum DocumentRegionKind
{
    Other,
    Block,
    Table,
    Cell,
    List,
    ListItem,
    Barcode,
    Image,
}
