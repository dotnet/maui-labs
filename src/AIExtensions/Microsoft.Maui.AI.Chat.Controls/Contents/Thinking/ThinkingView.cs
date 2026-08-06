using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.Controls.Shapes;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Renders a <see cref="ThinkingContentBlock"/> as an assistant bubble with a spinner and status text.</summary>
public sealed class ThinkingView : ContentContextView
{
    private readonly Label _label;

    public ThinkingView()
    {
        SemanticProperties.SetDescription(this, "Assistant is thinking");
        AutomationId = "AssistantThinking";

        _label = new Label
        {
            Text = "Thinking…",
            TextColor = Colors.Gray,
            VerticalOptions = LayoutOptions.Center,
        };

        Content = new Border
        {
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 4),
            HorizontalOptions = LayoutOptions.Start,
            StrokeThickness = 0,
            BackgroundColor = Color.FromArgb("#F0F0F3"),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new ActivityIndicator
                    {
                        IsRunning = true,
                        WidthRequest = 16,
                        HeightRequest = 16,
                        VerticalOptions = LayoutOptions.Center,
                    },
                    _label,
                },
            },
        };
    }

    protected override void RefreshFromContentContext()
    {
        if (ContentContext?.Block is ThinkingContentBlock thinking)
            _label.Text = thinking.Text;
    }
}
