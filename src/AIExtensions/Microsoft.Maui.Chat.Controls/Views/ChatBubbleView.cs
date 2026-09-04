using Microsoft.Maui.Chat.Controls.Themes;
using Microsoft.Maui.Controls.Shapes;

namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// The default chat bubble: avatar, participant name, a content slot, and a timestamp and status
/// footer, aligned by direction and styled from <see cref="ChatAppearance"/> and the theme.
/// </summary>
/// <remarks>
/// <para>
/// Derive from this class to reuse the bubble chrome for a custom content type: build your visual once,
/// assign it to <see cref="BubbleContent"/>, and describe it in <see cref="GetContentDescription"/> so
/// screen readers announce something meaningful.
/// </para>
/// <para>
/// Grouping follows the row flags: the avatar and name appear on the first row of a participant run and
/// the timestamp and status on the last, which keeps consecutive messages visually connected.
/// </para>
/// </remarks>
public abstract class ChatBubbleView : ChatContentView
{
    private readonly Grid _root;
    private readonly ColumnDefinition _leadingColumn;
    private readonly ColumnDefinition _trailingColumn;
    private readonly Border _avatar;
    private readonly Label _avatarLabel;
    private readonly Image _avatarImage;
    private readonly VerticalStackLayout _stack;
    private readonly Label _nameLabel;
    private readonly Border _bubble;
    private readonly ContentView _contentHost;
    private readonly ContentView _bareContentHost;
    private readonly Label _metadataLabel;
    private readonly RoundRectangle _bubbleShape;
    private View? _bubbleContent;
    private bool _usesStandardBubble = true;

    /// <summary>Builds the bubble chrome. Derived types fill <see cref="BubbleContent"/>.</summary>
    protected ChatBubbleView()
    {
        _leadingColumn = new ColumnDefinition(GridLength.Auto);
        _trailingColumn = new ColumnDefinition(GridLength.Auto);

        _avatarLabel = new Label
        {
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
        };
        _avatarLabel.SetDynamicResource(StyleProperty, ChatThemeKeys.AvatarTextStyle);

        _avatarImage = new Image
        {
            Aspect = Aspect.AspectFill,
            IsVisible = false,
        };

        _avatar = new Border
        {
            Padding = 0,
            StrokeThickness = 0,
            VerticalOptions = LayoutOptions.End,
            StrokeShape = new RoundRectangle { CornerRadius = 100 },
            Content = new Grid { Children = { _avatarImage, _avatarLabel } },
        };
        _avatar.SetDynamicResource(StyleProperty, ChatThemeKeys.AvatarStyle);

        _nameLabel = new Label { LineBreakMode = LineBreakMode.TailTruncation };
        _nameLabel.SetDynamicResource(StyleProperty, ChatThemeKeys.ParticipantNameStyle);

        _contentHost = new ContentView();
        _bareContentHost = new ContentView { IsVisible = false };

        _bubbleShape = new RoundRectangle { CornerRadius = 18 };
        _bubble = new Border
        {
            Padding = new Thickness(12, 8),
            StrokeThickness = 0,
            StrokeShape = _bubbleShape,
            Content = _contentHost,
        };
        _bubble.SetDynamicResource(StyleProperty, ChatThemeKeys.IncomingBubbleStyle);

        _metadataLabel = new Label { LineBreakMode = LineBreakMode.NoWrap };
        _metadataLabel.SetDynamicResource(StyleProperty, ChatThemeKeys.MetadataStyle);

        _stack = new VerticalStackLayout
        {
            Spacing = 2,
            Children = { _nameLabel, _bubble, _bareContentHost, _metadataLabel },
        };

        _root = new Grid
        {
            ColumnDefinitions = { _leadingColumn, new ColumnDefinition(GridLength.Star), _trailingColumn },
            ColumnSpacing = 6,
        };
        _root.Add(_avatar);
        _root.Add(_stack);
        Grid.SetColumn(_stack, 1);

        Content = _root;
    }

    /// <summary>
    /// Gets or sets the message body. Depending on <see cref="MessageContent.Presentation"/>, the body
    /// is rendered inside the standard bubble or bare inside the surrounding message chrome.
    /// </summary>
    protected View? BubbleContent
    {
        get => _bubbleContent;
        set
        {
            _bubbleContent = value;
            UpdateContentHost();
        }
    }

    internal bool UsesStandardBubble => _usesStandardBubble;

    internal ChatContentPresentation? PresentationOverride { get; set; }

    /// <summary>
    /// Gets a short description of the content for accessibility, for example the message text or the
    /// alternative text of an image.
    /// </summary>
    /// <returns>The description announced by screen readers, or an empty string.</returns>
    protected abstract string GetContentDescription();

    /// <summary>Applies the appearance and grouping of the current row to the bubble chrome.</summary>
    protected override void RefreshContent()
    {
        var item = Item;
        if (item is null)
        {
            _root.IsVisible = false;
            return;
        }

        _root.IsVisible = true;

        var appearance = item.Appearance;
        var outgoing = item.IsOutgoing;
        SetUsesStandardBubble(ResolveUsesStandardBubble(item));

        ApplyAvatar(item, appearance, outgoing);
        ApplyName(item, appearance, outgoing);
        ApplyBubble(appearance, outgoing);
        ApplyMetadata(item, appearance, outgoing);

        _stack.HorizontalOptions = outgoing ? LayoutOptions.End : LayoutOptions.Start;
        _stack.Spacing = 2;
        Margin = new Thickness(
            0,
            0,
            0,
            item.IsLastFromParticipant ? appearance.GroupSpacing : appearance.ContentSpacing);

        SemanticProperties.SetDescription(this, BuildSemanticDescription(item, appearance));
    }

    /// <summary>
    /// Applies the bubble text style and any <see cref="ChatAppearance"/> colour override to a label
    /// inside the bubble.
    /// </summary>
    /// <param name="label">The label to style.</param>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is <see langword="null"/>.</exception>
    protected void ApplyBubbleTextStyle(Label label)
    {
        ArgumentNullException.ThrowIfNull(label);

        var item = Item;
        var outgoing = item?.IsOutgoing ?? false;
        var appearance = Appearance;

        label.SetDynamicResource(
            StyleProperty,
            outgoing ? ChatThemeKeys.OutgoingTextStyle : ChatThemeKeys.IncomingTextStyle);

        var overrideColor = outgoing ? appearance.OutgoingTextColor : appearance.IncomingTextColor;
        if (overrideColor is not null)
            label.TextColor = overrideColor;
        else
            label.ClearValue(Label.TextColorProperty);
    }

    /// <summary>Determines whether the current content uses the standard bubble.</summary>
    /// <param name="item">The row being rendered.</param>
    /// <returns><see langword="true"/> to use the standard bubble.</returns>
    protected virtual bool ResolveUsesStandardBubble(ChatContentItem item) =>
        (PresentationOverride ?? item.Content.Presentation) ==
            ChatContentPresentation.Bubble;

    private void ApplyAvatar(ChatContentItem item, ChatAppearance appearance, bool outgoing)
    {
        var reserve = appearance.ShowAvatars ? new GridLength(appearance.AvatarSize) : new GridLength(0);
        _leadingColumn.Width = outgoing ? new GridLength(0) : reserve;
        _trailingColumn.Width = outgoing ? reserve : new GridLength(0);

        Grid.SetColumn(_avatar, outgoing ? 2 : 0);

        var show = appearance.ShowAvatars && item.IsFirstFromParticipant;
        _avatar.IsVisible = show;
        _avatar.WidthRequest = appearance.AvatarSize;
        _avatar.HeightRequest = appearance.AvatarSize;

        if (!show)
            return;

        var participant = item.Participant;
        if (participant.Avatar is { } avatar)
        {
            _avatarImage.Source = avatar;
            _avatarImage.IsVisible = true;
            _avatarLabel.IsVisible = false;
        }
        else
        {
            _avatarImage.Source = null;
            _avatarImage.IsVisible = false;
            _avatarLabel.IsVisible = true;
            _avatarLabel.Text = participant.Initials;
        }
    }

    private void ApplyName(ChatContentItem item, ChatAppearance appearance, bool outgoing)
    {
        var show = appearance.ShowParticipantNames && item.IsFirstFromParticipant;
        _nameLabel.IsVisible = show;
        _nameLabel.Text = show ? item.Participant.DisplayName : string.Empty;
        _nameLabel.HorizontalTextAlignment = outgoing ? TextAlignment.End : TextAlignment.Start;
        _nameLabel.HorizontalOptions = outgoing ? LayoutOptions.End : LayoutOptions.Start;
    }

    private void ApplyBubble(ChatAppearance appearance, bool outgoing)
    {
        _bubble.SetDynamicResource(
            StyleProperty,
            outgoing ? ChatThemeKeys.OutgoingBubbleStyle : ChatThemeKeys.IncomingBubbleStyle);

        _bubbleShape.CornerRadius = appearance.BubbleCornerRadius;
        _bubble.StrokeThickness = appearance.BubbleStrokeThickness;
        _bubble.MaximumWidthRequest = appearance.MaxBubbleWidth;
        _bubble.HorizontalOptions = outgoing ? LayoutOptions.End : LayoutOptions.Start;
        _bareContentHost.MaximumWidthRequest = appearance.MaxBubbleWidth;
        _bareContentHost.HorizontalOptions = outgoing ? LayoutOptions.End : LayoutOptions.Start;

        var background = outgoing ? appearance.OutgoingBubbleColor : appearance.IncomingBubbleColor;
        if (background is not null)
            _bubble.BackgroundColor = background;
        else
            _bubble.ClearValue(BackgroundColorProperty);

        if (appearance.BubbleStrokeColor is { } stroke)
            _bubble.Stroke = stroke;
        else
            _bubble.ClearValue(Border.StrokeProperty);
    }

    private void SetUsesStandardBubble(bool value)
    {
        if (_usesStandardBubble == value)
            return;

        _usesStandardBubble = value;
        UpdateContentHost();
    }

    private void UpdateContentHost()
    {
        _contentHost.Content = null;
        _bareContentHost.Content = null;

        if (_usesStandardBubble)
            _contentHost.Content = _bubbleContent;
        else
            _bareContentHost.Content = _bubbleContent;

        _bubble.IsVisible = _usesStandardBubble;
        _bareContentHost.IsVisible = !_usesStandardBubble;
    }

    private void ApplyMetadata(ChatContentItem item, ChatAppearance appearance, bool outgoing)
    {
        var text = BuildMetadataText(item, appearance, outgoing);
        _metadataLabel.IsVisible = item.IsLastFromParticipant && text.Length > 0;
        _metadataLabel.Text = text;
        _metadataLabel.HorizontalTextAlignment = outgoing ? TextAlignment.End : TextAlignment.Start;
        _metadataLabel.HorizontalOptions = outgoing ? LayoutOptions.End : LayoutOptions.Start;
    }

    private static string BuildMetadataText(ChatContentItem item, ChatAppearance appearance, bool outgoing)
    {
        var timestamp = appearance.ShowTimestamps ? appearance.FormatTimestamp(item.Timestamp) : string.Empty;
        var status = outgoing && appearance.ShowMessageStatus
            ? GetStatusGlyph(item.Message.Status)
            : string.Empty;

        if (timestamp.Length == 0)
            return status;

        return status.Length == 0 ? timestamp : $"{timestamp} {status}";
    }

    private static string GetStatusGlyph(ConversationMessageStatus status) => status switch
    {
        ConversationMessageStatus.Sending => "⋯",
        ConversationMessageStatus.Sent => "✓",
        ConversationMessageStatus.Delivered => "✓✓",
        ConversationMessageStatus.Read => "✓✓",
        ConversationMessageStatus.Failed => "!",
        _ => string.Empty,
    };

    private string BuildSemanticDescription(ChatContentItem item, ChatAppearance appearance)
    {
        var parts = new List<string>(4) { item.Participant.DisplayName };

        var content = GetContentDescription();
        if (!string.IsNullOrWhiteSpace(content))
            parts.Add(content);

        if (appearance.ShowTimestamps)
        {
            var timestamp = appearance.FormatTimestamp(item.Timestamp);
            if (timestamp.Length > 0)
                parts.Add(timestamp);
        }

        if (item.IsOutgoing && appearance.ShowMessageStatus && item.Message.Status != ConversationMessageStatus.Draft)
            parts.Add(item.Message.Status.ToString());

        return string.Join(", ", parts);
    }
}
