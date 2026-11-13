using BespokeCollections.Core;

namespace BespokeCollections.Fixture;

public static class FixtureCatalog
{
    public const int Count = 100_000;

    public static PhotoItem Item(int index, string query)
    {
        if (index is < 0 or >= Count || query is not ("0" or "1"))
            throw new ArgumentOutOfRangeException(nameof(index));
        var widths = new[] { 480, 640, 720, 800 };
        var heights = new[] { 320, 480, 720, 960, 1120, 600, 400 };
        var id = index + 1L + (query == "1" ? Count : 0);
        return new(id, $"Photo {id:000000} - original geometric fixture", widths[index % widths.Length],
            heights[index % heights.Length], 1, $"/thumbnails/{id}");
    }

    public static PhotoPage Page(string query, string? cursor, int limit)
    {
        if (limit is < 1 or > 100 || query is not ("0" or "1"))
            throw new ArgumentException("The fixture query or page size is invalid.");
        var offset = 0;
        if (!string.IsNullOrEmpty(cursor))
        {
            var parts = cursor.Split(':');
            if (parts.Length != 2 || parts[0] != query || !int.TryParse(parts[1], out offset) || offset is < 0 or >= Count)
                throw new ArgumentException("The fixture cursor is invalid for this query.");
        }
        var size = Math.Min(limit, Count - offset);
        return new(Enumerable.Range(offset, size).Select(index => Item(index, query)).ToArray(),
            offset + size < Count ? $"{query}:{offset + size}" : null);
    }
}
