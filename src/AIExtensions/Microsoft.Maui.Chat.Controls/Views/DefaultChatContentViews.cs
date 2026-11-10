using Microsoft.Maui.Chat.Controls.Themes;
using Microsoft.Maui.Controls.Shapes;
using Plugin.Maui.Audio;

namespace Microsoft.Maui.Chat.Controls;

/// <summary>The default view for <see cref="TextMessageContent"/>: a text bubble that updates in place while streaming.</summary>
public class ChatTextContentView : ChatBubbleView
{
    private readonly Label _label;
    private ChatContentPresentation _presentation;

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

        _presentation = Item?.Content.Presentation ?? ChatContentPresentation.Bubble;
        ApplyBubbleTextStyle(_label);
        _label.Text = GetText();
    }

    /// <summary>Updates streamed text cheaply, refreshing chrome only when presentation changed.</summary>
    protected override void OnContentUpdated()
    {
        var presentation = Item?.Content.Presentation ?? ChatContentPresentation.Bubble;
        if (presentation != _presentation)
        {
            RefreshContent();
            return;
        }

        _label.Text = GetText();
    }

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
    private readonly Button _playButton;
    private IAudioPlayer? _audioPlayer;
    private Stream? _audioStream;
    private MediaMessageContent? _audioContent;

    /// <summary>Creates the view.</summary>
    public ChatFileContentView()
    {
        _nameLabel = new Label { LineBreakMode = LineBreakMode.MiddleTruncation };
        _nameLabel.SetDynamicResource(StyleProperty, ChatThemeKeys.FileNameStyle);

        _detailLabel = new Label { LineBreakMode = LineBreakMode.NoWrap };
        _detailLabel.SetDynamicResource(StyleProperty, ChatThemeKeys.FileDetailStyle);

        _playButton = new Button
        {
            Text = "Play",
            AutomationId = "ChatAudioPlaybackButton",
            VerticalOptions = LayoutOptions.Center,
        };
        _playButton.SetDynamicResource(
            StyleProperty,
            ChatThemeKeys.AudioPlaybackButtonStyle);
        _playButton.Clicked += OnPlayClicked;

        var labels = new VerticalStackLayout
        {
            Spacing = 2,
            Children = { _nameLabel, _detailLabel },
        };
        var cardContent = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 8,
        };
        cardContent.Add(labels);
        cardContent.Add(_playButton, 1);

        var card = new Border
        {
            Padding = new Thickness(10, 8),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = cardContent,
        };
        card.SetDynamicResource(StyleProperty, ChatThemeKeys.FileCardStyle);

        BubbleContent = card;
    }

    /// <inheritdoc />
    protected override void RefreshContent()
    {
        base.RefreshContent();

        var media = Item?.Content as MediaMessageContent;
        if (!ReferenceEquals(_audioContent, media))
            DisposeAudioPlayer();
        _nameLabel.Text = media?.FileName ?? media?.DisplayName ?? string.Empty;
        _detailLabel.Text = media is null ? string.Empty : DescribeFile(media);
        _detailLabel.IsVisible = _detailLabel.Text.Length > 0;
        var canPlay = media is not null && CanPlayAudio(media);
        var isPlaying =
            canPlay
            && ReferenceEquals(_audioContent, media)
            && _audioPlayer?.IsPlaying == true;
        _playButton.IsVisible = canPlay;
        _playButton.Text = isPlaying ? "Pause" : "Play";
        SemanticProperties.SetDescription(
            _playButton,
            media is null
                ? string.Empty
                : $"{(isPlaying ? "Pause" : "Play")} {media.DisplayName}");
    }

    /// <inheritdoc />
    protected override void OnItemChanged(
        ChatContentItem? oldItem,
        ChatContentItem? newItem)
    {
        DisposeAudioPlayer();
        base.OnItemChanged(oldItem, newItem);
    }

    /// <inheritdoc />
    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (args.NewHandler is null)
            DisposeAudioPlayer();
        base.OnHandlerChanging(args);
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

    private void OnPlayClicked(object? sender, EventArgs e)
    {
        if (Item?.Content is not MediaMessageContent media
            || !CanPlayAudio(media))
            return;

        if (_audioPlayer?.IsPlaying == true)
        {
            _audioPlayer.Pause();
            _playButton.Text = "Play";
            return;
        }

        if (!ReferenceEquals(_audioContent, media))
        {
            DisposeAudioPlayer();
            _audioContent = media;
            if (media.HasData)
            {
                _audioStream = new MemoryStream(media.Data.ToArray(), writable: false);
                _audioPlayer = AudioManager.Current.CreatePlayer(_audioStream);
            }
            else
            {
                _audioPlayer = AudioManager.Current.CreatePlayer(
                    GetAudioFilePath(media)!);
            }
            _audioPlayer.PlaybackEnded += OnPlaybackEnded;
            _audioPlayer.Error += OnPlaybackEnded;
        }

        _audioPlayer?.Play();
        _playButton.Text = "Pause";
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        var dispatcher =
            Application.Current?.Dispatcher
            ?? Microsoft.Maui.Dispatching.Dispatcher.GetForCurrentThread();
        if (dispatcher is { IsDispatchRequired: true })
        {
            dispatcher.Dispatch(() => ResetPlayback(sender));
            return;
        }

        ResetPlayback(sender);
    }

    private void ResetPlayback(object? sender)
    {
        if (ReferenceEquals(sender, _audioPlayer))
            _audioPlayer?.Stop();
        _playButton.Text = "Play";
    }

    private void DisposeAudioPlayer()
    {
        if (_audioPlayer is not null)
        {
            _audioPlayer.PlaybackEnded -= OnPlaybackEnded;
            _audioPlayer.Error -= OnPlaybackEnded;
            _audioPlayer.Stop();
            _audioPlayer.Dispose();
            _audioPlayer = null;
        }

        _audioStream?.Dispose();
        _audioStream = null;
        _audioContent = null;
    }

    private static bool CanPlayAudio(MediaMessageContent media) =>
        media.IsAudio
        && (media.HasData || GetAudioFilePath(media) is not null);

    private static string? GetAudioFilePath(MediaMessageContent media)
    {
        if (media.Uri is not { } uri)
            return null;

        var path = uri.IsAbsoluteUri && uri.IsFile
            ? uri.LocalPath
            : !uri.IsAbsoluteUri
                ? uri.OriginalString
                : null;
        return path is not null && File.Exists(path)
            ? path
            : null;
    }
}
