namespace BespokeCollections.Core;

public readonly record struct MasonryRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Intersects(MasonryRect other) =>
        Right > other.X && other.Right > X && Bottom > other.Y && other.Bottom > Y;
}

public sealed class MasonryGeometry
{
    readonly MasonryRect[] _frames;
    readonly int[][] _columns;

    MasonryGeometry(MasonryRect[] frames, int[][] columns, double width, double height, double captionHeight)
    {
        _frames = frames;
        _columns = columns;
        Width = width;
        Height = height;
        CaptionHeight = captionHeight;
    }

    public int Count => _frames.Length;
    public int ColumnCount => _columns.Length;
    public double Width { get; }
    public double Height { get; }
    public double CaptionHeight { get; }
    public MasonryRect this[int index] => _frames[index];

    public static MasonryGeometry Create(
        IReadOnlyList<PhotoItem> items,
        double width,
        int columns,
        double gap = 8,
        double padding = 8,
        double captionHeight = 48)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (!double.IsFinite(width) || width <= 0 || columns is < 1 or > 12 ||
            !double.IsFinite(gap) || gap < 0 || !double.IsFinite(padding) || padding < 0 ||
            !double.IsFinite(captionHeight) || captionHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Masonry constraints must be finite and usable.");

        var itemWidth = (width - padding * 2 - gap * (columns - 1)) / columns;
        if (itemWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "The columns and insets exceed the viewport.");

        var frames = new MasonryRect[items.Count];
        var offsets = Enumerable.Repeat(padding, columns).ToArray();
        var indices = Enumerable.Range(0, columns).Select(_ => new List<int>()).ToArray();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.PixelWidth is <= 0 or > 16384 || item.PixelHeight is <= 0 or > 16384)
                throw new ArgumentException("Photo dimensions must be positive and bounded.", nameof(items));
            var column = 0;
            for (var candidate = 1; candidate < columns; candidate++)
                if (offsets[candidate] < offsets[column])
                    column = candidate;
            var height = itemWidth * item.PixelHeight / item.PixelWidth + captionHeight;
            frames[index] = new(padding + column * (itemWidth + gap), offsets[column], itemWidth, height);
            offsets[column] += height + gap;
            indices[column].Add(index);
        }

        var extent = items.Count == 0 ? 0 : offsets.Max() - gap + padding;
        return new(frames, indices.Select(column => column.ToArray()).ToArray(), width, extent, captionHeight);
    }

    public int[] Intersecting(MasonryRect viewport, out int candidatesVisited)
    {
        if (!double.IsFinite(viewport.X) || !double.IsFinite(viewport.Y) ||
            !double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height) ||
            viewport.Width < 0 || viewport.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(viewport));

        candidatesVisited = 0;
        var visible = new List<int>();
        foreach (var column in _columns)
        {
            var low = 0;
            var high = column.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                candidatesVisited++;
                if (_frames[column[middle]].Bottom <= viewport.Y)
                    low = middle + 1;
                else
                    high = middle;
            }
            for (var position = low; position < column.Length; position++)
            {
                var index = column[position];
                candidatesVisited++;
                if (_frames[index].Y >= viewport.Bottom)
                    break;
                if (_frames[index].Intersects(viewport))
                    visible.Add(index);
            }
        }
        visible.Sort();
        return visible.ToArray();
    }
}
