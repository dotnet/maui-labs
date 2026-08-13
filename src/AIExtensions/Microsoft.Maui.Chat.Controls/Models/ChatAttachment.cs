namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// A named attachment picked in the composer: file metadata plus either a reusable byte buffer or a
/// <see cref="Uri"/>.
/// </summary>
/// <remarks>
/// Attachments never hold an open stream so the same instance can be previewed, sent, and rendered
/// repeatedly. <see cref="ToContent"/> converts an attachment into the
/// <see cref="MediaMessageContent"/> stored on a message.
/// </remarks>
public class ChatAttachment
{
    /// <summary>Creates an attachment backed by a reusable buffer.</summary>
    /// <param name="fileName">The display file name.</param>
    /// <param name="mediaType">The MIME type, for example <c>image/png</c>.</param>
    /// <param name="data">The file bytes. Must not be empty.</param>
    /// <exception cref="ArgumentException">A required argument is blank, or <paramref name="data"/> is empty.</exception>
    public ChatAttachment(string fileName, string mediaType, ReadOnlyMemory<byte> data)
        : this(fileName, mediaType, data, altText: null)
    {
    }

    /// <summary>Creates an attachment backed by a reusable buffer.</summary>
    /// <param name="fileName">The display file name.</param>
    /// <param name="mediaType">The MIME type, for example <c>image/png</c>.</param>
    /// <param name="data">The file bytes. Must not be empty.</param>
    /// <param name="altText">An optional accessibility description.</param>
    /// <exception cref="ArgumentException">A required argument is blank, or <paramref name="data"/> is empty.</exception>
    public ChatAttachment(
        string fileName,
        string mediaType,
        ReadOnlyMemory<byte> data,
        string? altText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        if (data.IsEmpty)
            throw new ArgumentException("An attachment requires either a URI or a non-empty buffer.", nameof(data));

        FileName = fileName;
        MediaType = mediaType;
        Data = data;
        AltText = altText;
    }

    /// <summary>Creates an attachment that points at a URI.</summary>
    /// <param name="fileName">The display file name.</param>
    /// <param name="mediaType">The MIME type, for example <c>image/png</c>.</param>
    /// <param name="uri">The location of the file.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A required argument is blank.</exception>
    public ChatAttachment(string fileName, string mediaType, Uri uri)
        : this(fileName, mediaType, uri, altText: null)
    {
    }

    /// <summary>Creates an attachment that points at a URI.</summary>
    /// <param name="fileName">The display file name.</param>
    /// <param name="mediaType">The MIME type, for example <c>image/png</c>.</param>
    /// <param name="uri">The location of the file.</param>
    /// <param name="altText">An optional accessibility description.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A required argument is blank.</exception>
    public ChatAttachment(string fileName, string mediaType, Uri uri, string? altText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentNullException.ThrowIfNull(uri);

        FileName = fileName;
        MediaType = mediaType;
        Uri = uri;
        AltText = altText;
    }

    /// <summary>Gets the display file name.</summary>
    public string FileName { get; }

    /// <summary>Gets the MIME type.</summary>
    public string MediaType { get; }

    /// <summary>Gets the reusable buffer, or an empty buffer when the attachment is addressed by <see cref="Uri"/>.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets the location of the file, or <see langword="null"/> when the bytes are in <see cref="Data"/>.</summary>
    public Uri? Uri { get; }

    /// <summary>Gets the optional accessibility description.</summary>
    public string? AltText { get; }

    /// <summary>Gets the size of <see cref="Data"/> in bytes, or <c>0</c> for URI-addressed attachments.</summary>
    public long ByteCount => Data.Length;

    /// <summary>Gets whether <see cref="MediaType"/> denotes an image.</summary>
    public bool IsImage => MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates the <see cref="MediaMessageContent"/> that represents this attachment in a message.</summary>
    /// <returns>New media content carrying this attachment's source and metadata.</returns>
    public MediaMessageContent ToContent()
    {
        var content = Uri is not null
            ? new MediaMessageContent(Uri, MediaType)
            : new MediaMessageContent(Data, MediaType);

        content.FileName = FileName;
        content.AltText = AltText;
        return content;
    }
}
