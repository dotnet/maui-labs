using Microsoft.Maui.Chat.Controls.Themes;
using Microsoft.Maui.Controls.Shapes;

namespace Microsoft.Maui.Chat.Controls;

/// <summary>The default view for <see cref="TextMessageContent"/>: a text bubble that updates in place while streaming.</summary>
public class ChatTextContentView : ChatBubbleView
{
    private readonly Label _label;

    /// <summary>Creates the view.</summary>
    public ChatTextContentView()
    {
        _label = new Label { LineBreakMode = LineBreakMode.WordWrap };
        BubbleContent = _label;
    }

    /// <inheritdoc />
    protected override void RefreshContent()
    {
        base.RefreshContent();

        ApplyBubbleTextStyle(_label);
        _label.Text = GetText();
    }

    /// <summary>Updates only the text, so streaming does not rebuild the bubble.</summary>
    protected override void OnContentUpdated() => _label.Text = GetText();

    /// <inheritdoc />
    protected override string GetContentDescription() => GetText();

    private string GetText() => Item?.Content is TextMessageContent text ? text.Text : string.Empty;
}

/// <summary>The default view for image <see cref="MediaMessageContent"/>: the image inside a bubble.</summary>
/// <remarks>
/// The <see cref="ImageSource"/> is created here — the model deliberately stores only a URI or a reusable
/// buffer — and is cached per content so a recycled cell does not copy the buffer again.
/// </remarks>
public class ChatMediaContentView : ChatBubbleView
{
    private readonly Image _image;
    private MediaMessageContent? _sourceContent;

    /// <summary>Creates the view.</summary>
    public ChatMediaContentView()
    {
        _image = new Image
        {
            Aspect = Aspect.AspectFit,
            MaximumHeightRequest = 260,
        };

        BubbleContent = _image;
    }

    /// <inheritdoc />
    protected override void OnItemChanged(ChatContentItem? oldItem, ChatContentItem? newItem)
    {
        base.OnItemChanged(oldItem, newItem);

        // Recycled cells must not show the previous image while the new one resolves.
        _sourceContent = null;
        _image.Source = null;
    }

    /// <inheritdoc />
    protected override void RefreshContent()
    {
        base.RefreshContent();

        if (Item?.Content is not MediaMessageContent media)
        {
            _sourceContent = null;
            _image.Source = null;
            return;
        }

        if (!ReferenceEquals(_sourceContent, media))
        {
            _sourceContent = media;
            _image.Source = CreateImageSource(media);
        }

        SemanticProperties.SetDescription(_image, media.DisplayName);
    }

    /// <inheritdoc />
    protected override string GetContentDescription() =>
        Item?.Content is MediaMessageContent media ? media.DisplayName : string.Empty;

    /// <summary>Creates an <see cref="ImageSource"/> for media addressed by URI or by a reusable buffer.</summary>
    /// <param name="media">The media content.</param>
    /// <returns>The image source, or <see langword="null"/> when the media has no usable source.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="media"/> is <see langword="null"/>.</exception>
    public static ImageSource? CreateImageSource(MediaMessageContent media)
    {
        ArgumentNullException.ThrowIfNull(media);

        if (media.Uri is { } uri)
        {
            return uri.IsAbsoluteUri
                ? ImageSource.FromUri(uri)
                : ImageSource.FromFile(uri.OriginalString);
        }

        if (!media.HasData)
            return null;

        // Copy once: the source factory must be able to hand out a fresh stream every time the cell
        // is recycled, so it can never close over a single consumable stream.
        var bytes = media.Data.ToArray();
        return ImageSource.FromStream(() => new MemoryStream(bytes, writable: false));
    }
}

/// <summary>The default view for non-image <see cref="MediaMessageContent"/>: a file card with name and size.</summary>
public class ChatFileContentView : ChatBubbleView
{
    private readonly Label _nameLabel;
    private readonly Label _detailLabel;

    /// <summary>Creates the view.</summary>
    public ChatFileContentView()
    {
        _nameLabel = new Label { LineBreakMode = LineBreakMode.MiddleTruncation };
        _nameLabel.SetDynamicResource(StyleProperty, ChatThemeKeys.FileNameStyle);

        _detailLabel = new Label { LineBreakMode = LineBreakMode.NoWrap };
        _detailLabel.SetDynamicResource(StyleProperty, ChatThemeKeys.FileDetailStyle);

        var card = new Border
        {
            Padding = new Thickness(10, 8),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children = { _nameLabel, _detailLabel },
            },
        };
        card.SetDynamicResource(StyleProperty, ChatThemeKeys.FileCardStyle);

        BubbleContent = card;
    }

    /// <inheritdoc />
    protected override void RefreshContent()
    {
        base.RefreshContent();

        var media = Item?.Content as MediaMessageContent;
        _nameLabel.Text = media?.FileName ?? media?.DisplayName ?? string.Empty;
        _detailLabel.Text = media is null ? string.Empty : DescribeFile(media);
        _detailLabel.IsVisible = _detailLabel.Text.Length > 0;
    }

    /// <inheritdoc />
    protected override string GetContentDescription() =>
        Item?.Content is MediaMessageContent media
            ? $"{media.FileName ?? media.DisplayName}, {DescribeFile(media)}"
            : string.Empty;

    private static string DescribeFile(MediaMessageContent media) =>
        media.ByteCount > 0
            ? $"{media.MediaType} · {FormatSize(media.ByteCount)}"
            : media.MediaType;

    private static string FormatSize(long bytes)
    {
        const long kilobyte = 1024;
        const long megabyte = kilobyte * 1024;

        return bytes switch
        {
            >= megabyte => $"{bytes / (double)megabyte:0.#} MB",
            >= kilobyte => $"{bytes / (double)kilobyte:0.#} KB",
            _ => $"{bytes} B",
        };
    }
}
