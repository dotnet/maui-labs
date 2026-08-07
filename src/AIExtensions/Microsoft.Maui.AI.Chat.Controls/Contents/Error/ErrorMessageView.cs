using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.Controls.Shapes;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Renders an <see cref="ErrorContentBlock"/> as an error-styled message bubble.</summary>
public sealed class ErrorMessageView : ContentContextView
{
    private readonly Label _label;
    private readonly Button _retry;

    public ErrorMessageView()
    {
        SemanticProperties.SetDescription(this, "Chat error");
        AutomationId = "ChatError";

        _label = new Label { TextColor = Color.FromArgb("#C75050") };
        _retry = new Button
        {
            Text = "Retry",
            AutomationId = "RetryMessageButton",
            FontSize = 12,
            Padding = new Thickness(10, 4),
            HorizontalOptions = LayoutOptions.Start,
        };
        SemanticProperties.SetDescription(_retry, "Retry failed message");
        _retry.Command = new Command(async () => await RetryAsync());

        Content = new Border
        {
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0, 4),
            MaximumWidthRequest = 340,
            HorizontalOptions = LayoutOptions.Start,
            StrokeThickness = 0,
            BackgroundColor = Color.FromArgb("#FDE8E8"),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children = { _label, _retry },
            },
        };
    }

    protected override void RefreshFromContentContext()
    {
        if (ContentContext?.Block is ErrorContentBlock error)
        {
            _label.Text = $"⚠️ {error.Message}";
            _retry.IsVisible =
                ContentContext.AgentContext.Status == ConversationStatus.Error;
        }
    }

    internal async Task RetryAsync()
    {
        if (ContentContext?.AgentContext.Status == ConversationStatus.Error)
        {
            _retry.IsEnabled = false;
            await ContentContext.AgentContext.RetryAsync();
            _retry.IsVisible =
                ContentContext.AgentContext.Status == ConversationStatus.Error;
            _retry.IsEnabled = true;
        }
    }

    internal Button RetryButton => _retry;
}
