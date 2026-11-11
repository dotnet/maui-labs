namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>The user's message: media and text in one right-aligned bubble.</summary>
internal sealed class UserMessageView : RowView<UserMessageRow>
{
    private readonly VerticalStackLayout _stack = new() { Spacing = 8 };

    public UserMessageView()
    {
        AutomationId = "AIChatUserMessage";
        Content = new Border { Content = _stack }.Styled(AIChatThemeKeys.UserBubbleStyle);
    }

    internal VerticalStackLayout Stack => _stack;

    protected override void Render(UserMessageRow row, bool isNewRow)
    {
        _stack.Children.Clear();

        foreach (var media in row.Blocks.OfType<MediaContentBlock>())
        {
            foreach (var item in media.Items)
                _stack.Children.Add(ChatVisuals.CreateMediaView(item, alignEnd: true));
        }

        var text = string.Join(
            "\n",
            row.Blocks.OfType<RichContentBlock>().Select(static block => block.RawText).Where(static text => !string.IsNullOrWhiteSpace(text)));
        if (text.Length > 0)
            _stack.Children.Add(ChatVisuals.Label(AIChatThemeKeys.UserTextStyle, text));
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

/// <summary>Shown under a failed turn, with a retry action.</summary>
internal sealed class TurnErrorView : RowView<TurnErrorRow>
{
    internal const string Message = "Something went wrong. Please try again.";

    private readonly Button _retry;

    public TurnErrorView()
    {
        AutomationId = "AIChatError";

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
