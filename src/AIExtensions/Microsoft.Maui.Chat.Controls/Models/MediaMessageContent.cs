namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// Media content addressed either by <see cref="Uri"/> or by a reusable in-memory
/// <see cref="ReadOnlyMemory{T}"/> buffer, plus the metadata views need to render and describe it.
/// </summary>
/// <remarks>
/// <para>
/// Exactly one source is required. Buffers are stored instead of streams so a view can build a fresh
/// <see cref="ImageSource"/> every time a cell is recycled. Converting to an
/// <see cref="ImageSource"/> is a view concern and deliberately not done here.
/// </para>
/// <para>Instances are single-thread affine and are not thread-safe.</para>
/// </remarks>
public class MediaMessageContent : MessageContent
{
    /// <summary>Backing property for <see cref="FileName"/>.</summary>
    public static readonly BindableProperty FileNameProperty =
        BindableProperty.Create(
            nameof(FileName),
            typeof(string),
            typeof(MediaMessageContent),
            propertyChanged: static (bindable, _, _) => ((MediaMessageContent)bindable).RaiseContentChanged());

    /// <summary>Backing property for <see cref="AltText"/>.</summary>
    public static readonly BindableProperty AltTextProperty =
        BindableProperty.Create(
            nameof(AltText),
            typeof(string),
            typeof(MediaMessageContent),
            propertyChanged: static (bindable, _, _) => ((MediaMessageContent)bindable).RaiseContentChanged());

    /// <summary>Backing property for <see cref="PixelWidth"/>.</summary>
    public static readonly BindableProperty PixelWidthProperty =
        BindableProperty.Create(nameof(PixelWidth), typeof(int), typeof(MediaMessageContent), 0);

    /// <summary>Backing property for <see cref="PixelHeight"/>.</summary>
    public static readonly BindableProperty PixelHeightProperty =
        BindableProperty.Create(nameof(PixelHeight), typeof(int), typeof(MediaMessageContent), 0);

    /// <summary>Creates media content that points at a URI.</summary>
    /// <param name="uri">The absolute or relative location of the media.</param>
    /// <param name="mediaType">The MIME type, for example <c>image/png</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="mediaType"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public MediaMessageContent(Uri uri, string mediaType)
        : this(uri, mediaType, id: null)
    {
    }

    /// <summary>Creates media content that points at a URI.</summary>
    /// <param name="uri">The absolute or relative location of the media.</param>
    /// <param name="mediaType">The MIME type, for example <c>image/png</c>.</param>
    /// <param name="id">A stable identifier. When <see langword="null"/>, a new unique identifier is generated.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="mediaType"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public MediaMessageContent(Uri uri, string mediaType, string? id)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        Uri = uri;
        MediaType = mediaType;
    }

    /// <summary>Creates media content backed by a reusable in-memory buffer.</summary>
    /// <param name="data">The media bytes. Must not be empty.</param>
    /// <param name="mediaType">The MIME type, for example <c>image/png</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty, or <paramref name="mediaType"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public MediaMessageContent(ReadOnlyMemory<byte> data, string mediaType)
        : this(data, mediaType, id: null)
    {
    }

    /// <summary>Creates media content backed by a reusable in-memory buffer.</summary>
    /// <param name="data">The media bytes. Must not be empty.</param>
    /// <param name="mediaType">The MIME type, for example <c>image/png</c>.</param>
    /// <param name="id">A stable identifier. When <see langword="null"/>, a new unique identifier is generated.</param>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty, or <paramref name="mediaType"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public MediaMessageContent(ReadOnlyMemory<byte> data, string mediaType, string? id)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        if (data.IsEmpty)
            throw new ArgumentException("Media content requires either a URI or a non-empty buffer.", nameof(data));

        Data = data;
        MediaType = mediaType;
    }

    /// <summary>Gets the location of the media, or <see langword="null"/> when the media is in <see cref="Data"/>.</summary>
    public Uri? Uri { get; }

    /// <summary>Gets the reusable media buffer. Empty when the media is addressed by <see cref="Uri"/>.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets whether this content carries an in-memory buffer.</summary>
    public bool HasData => !Data.IsEmpty;

    /// <summary>Gets the MIME type of the media. Never <see langword="null"/> or blank.</summary>
    public string MediaType { get; }

    /// <summary>Gets the size of <see cref="Data"/> in bytes, or <c>0</c> for URI-addressed media.</summary>
    public long ByteCount => Data.Length;

    /// <summary>Gets whether <see cref="MediaType"/> denotes an image, which the default templates render inline.</summary>
    public bool IsImage => MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets or sets the optional display file name.</summary>
    public string? FileName
    {
        get => (string?)GetValue(FileNameProperty);
        set => SetValue(FileNameProperty, value);
    }

    /// <summary>Gets or sets the accessibility description used as the semantic description of the rendered media.</summary>
    public string? AltText
    {
        get => (string?)GetValue(AltTextProperty);
        set => SetValue(AltTextProperty, value);
    }

    /// <summary>Gets or sets the intrinsic pixel width, or <c>0</c> when unknown.</summary>
    public int PixelWidth
    {
        get => (int)GetValue(PixelWidthProperty);
        set => SetValue(PixelWidthProperty, value);
    }

    /// <summary>Gets or sets the intrinsic pixel height, or <c>0</c> when unknown.</summary>
    public int PixelHeight
    {
        get => (int)GetValue(PixelHeightProperty);
        set => SetValue(PixelHeightProperty, value);
    }

    /// <summary>
    /// Gets the best available label for this media: <see cref="AltText"/>, then <see cref="FileName"/>,
    /// then <see cref="MediaType"/>.
    /// </summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(AltText) ? AltText!
        : !string.IsNullOrWhiteSpace(FileName) ? FileName!
        : MediaType;
}
