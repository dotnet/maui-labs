namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// The user's message: attachments as right-aligned thumbnails and chips, then the text in a right-aligned bubble
/// sized to the text.
/// </summary>
internal sealed class UserMessageView : RowView<UserMessageRow>
{
    private readonly VerticalStackLayout _attachments = new() { Spacing = 6, HorizontalOptions = LayoutOptions.End, IsVisible = false };
    private readonly Label _text = ChatVisuals.Label(AIChatThemeKeys.UserTextStyle);
    private readonly Border _bubble;
    private AIContent[] _media = [];

    public UserMessageView()
    {
        AutomationId = "AIChatUserMessage";
        _bubble = new Border { Content = _text, IsVisible = false }.Styled(AIChatThemeKeys.UserBubbleStyle);
        _bubble.PropertyChanged += OnBubblePropertyChanged;
        Content = new VerticalStackLayout { Children = { _attachments, _bubble } };
    }

    internal VerticalStackLayout Attachments => _attachments;

    internal Border Bubble => _bubble;

    internal Label TextLabel => _text;

    protected override void Render(UserMessageRow row, bool isNewRow)
    {
        // Rows are refreshed when the conversation's status changes, too. Only touch the views when what they show
        // changed: replacing them while the row is off screen leaves them unmeasured when it scrolls back.
        var media = row.Blocks.OfType<MediaContentBlock>().Select(static block => block.Content).ToArray();
        if (!media.SequenceEqual(_media, ReferenceEqualityComparer.Instance))
        {
            _media = media;
            _attachments.Children.Clear();
            foreach (var content in media)
                _attachments.Children.Add(ChatVisuals.CreateMediaView(content, alignEnd: true));
            _attachments.IsVisible = media.Length > 0;
        }

        var text = string.Join(
            "\n",
            row.Blocks.OfType<RichContentBlock>().Select(static block => block.RawText).Where(static text => !string.IsNullOrWhiteSpace(text)));
        if (_text.Text != text)
            _text.Text = text;
        _bubble.IsVisible = text.Length > 0;
    }

    // The attachments line up with the bubble: the same side insets, and the bubble's top margin above whichever
    // comes first (between them, it spaces the bubble from the attachments).
    private void OnBubblePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == MarginProperty.PropertyName)
            _attachments.Margin = new Thickness(_bubble.Margin.Left, _bubble.Margin.Top, _bubble.Margin.Right, 0);
    }
}

/// <summary>Shown while the assistant works and nothing visible has streamed yet.</summary>
internal sealed class ThinkingView : RowView<ThinkingRow>
{
    public ThinkingView()
    {
        AutomationId = "AIChatThinking";

        var indicator = new ActivityIndicator
        {
            IsRunning = true,
            WidthRequest = 16,
            HeightRequest = 16,
            VerticalOptions = LayoutOptions.Center,
        };
        indicator.SetDynamicResource(ActivityIndicator.ColorProperty, AIChatThemeKeys.SecondaryTextColor);

        Content = new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { indicator, ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle, "Thinking…") },
        };
        SemanticProperties.SetDescription(this, "The assistant is thinking");
    }

    protected override void Render(ThinkingRow row, bool isNewRow)
    {
    }
}

/// <summary>A subtle marker under a response the user stopped. The partial output above it stays.</summary>
internal sealed class TurnStoppedView : RowView<TurnStoppedRow>
{
    internal const string Message = "Stopped";

    public TurnStoppedView()
    {
        AutomationId = "AIChatStopped";
        Content = ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle, Message);
        SemanticProperties.SetDescription(this, "The response was stopped");
    }

    protected override void Render(TurnStoppedRow row, bool isNewRow)
    {
    }
}

/// <summary>
/// Marks an earlier turn that failed and was followed by another message instead of a retry. The assistant never
/// got it, so it can no longer be retried and has no response footer.
/// </summary>
internal sealed class TurnNotSentView : RowView<TurnNotSentRow>
{
    internal const string Message = "Not sent";

    public TurnNotSentView()
    {
        AutomationId = "AIChatNotSent";
        Content = ChatVisuals.Label(AIChatThemeKeys.ErrorTextStyle, Message);
        SemanticProperties.SetDescription(this, "Not sent. This message failed, so the assistant didn't get it");
    }

    protected override void Render(TurnNotSentRow row, bool isNewRow)
    {
    }
}

/// <summary>Shown under a failed turn, with a retry action.</summary>
internal sealed class TurnErrorView : RowView<TurnErrorRow>
{
    internal const string Message = "Something went wrong. Please try again.";

    private readonly Button _retry;

    public TurnErrorView()
    {
        AutomationId = "AIChatTurnError";

        _retry = new Button
        {
            Text = "Retry",
            AutomationId = "AIChatRetryButton",
            HorizontalOptions = LayoutOptions.Start,
            Command = new Command(async () => await RetryAsync()),
        }.Styled(AIChatThemeKeys.PrimaryButtonStyle);
        SemanticProperties.SetDescription(_retry, "Retry the last message");

        Content = new Border
        {
            HorizontalOptions = LayoutOptions.Start,
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children = { ChatVisuals.Label(AIChatThemeKeys.ErrorTextStyle, Message), _retry },
            },
        }.Styled(AIChatThemeKeys.ErrorCardStyle);
    }

    internal Button RetryButton => _retry;

    protected override void Render(TurnErrorRow row, bool isNewRow) => _retry.IsEnabled = true;

    internal async Task RetryAsync()
    {
        if (Row is not { Session: { Status: ConversationStatus.Error } session })
            return;

        _retry.IsEnabled = false;
        try
        {
            await session.RetryAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or ObjectDisposedException)
        {
            // The session moved on (cleared, retried elsewhere, or disposed); the list re-renders from its state.
        }
        finally
        {
            _retry.IsEnabled = true;
        }
    }
}
