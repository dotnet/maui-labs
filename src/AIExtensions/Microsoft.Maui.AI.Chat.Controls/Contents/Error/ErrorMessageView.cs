using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.Controls.Shapes;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Renders an <see cref="ErrorContentBlock"/> as an error-styled message bubble.</summary>
public sealed class ErrorMessageView : ContentContextView
{
    private readonly Label _label;

    public ErrorMessageView()
    {
        SemanticProperties.SetDescription(this, "Chat error");
        AutomationId = "ChatError";

        _label = new Label { TextColor = Color.FromArgb("#C75050") };

        Content = new Border
        {
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0, 4),
            MaximumWidthRequest = 340,
            HorizontalOptions = LayoutOptions.Start,
            StrokeThickness = 0,
            BackgroundColor = Color.FromArgb("#FDE8E8"),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = _label,
        };
    }

    protected override void RefreshFromContentContext()
    {
        if (ContentContext?.Block is ErrorContentBlock error)
            _label.Text = $"⚠️ {error.Message}";
    }
}
