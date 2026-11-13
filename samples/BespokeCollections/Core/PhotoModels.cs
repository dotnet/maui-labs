using System.Text.Json.Serialization;

namespace BespokeCollections.Core;

public sealed record PhotoItem(long Id, string Caption, int PixelWidth, int PixelHeight, int Revision, string ThumbnailPath);

public sealed record PhotoPage(PhotoItem[] Items, string? NextCursor);

public sealed record PhotoFeedState(
    IReadOnlyList<PhotoItem> Items,
    long Epoch,
    string Query,
    string? NextCursor,
    bool IsLoading,
    bool HasMore,
    string? Error,
    int PagesLoaded,
    int EvictedItems)
{
    public static PhotoFeedState Empty { get; } = new([], 0, "0", null, false, true, null, 0, 0);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PhotoPage))]
[JsonSerializable(typeof(NativeMasonrySnapshot))]
[JsonSerializable(typeof(PrototypeReport))]
[JsonSerializable(typeof(PrototypeFailure))]
public partial class PhotoJsonContext : JsonSerializerContext;
