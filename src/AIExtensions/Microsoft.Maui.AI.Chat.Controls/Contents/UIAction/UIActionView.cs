using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.Controls.Shapes;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Renders an automatically executed client-side action.</summary>
public sealed class UIActionView : ContentContextView
{
    private readonly Label _status;

    public UIActionView()
    {
        AutomationId = "UIAction";
        _status = new Label
        {
            FontSize = 12,
            TextColor = Colors.Gray,
        };
        Content = new Border
        {
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 2),
            HorizontalOptions = LayoutOptions.Start,
            StrokeThickness = 0,
            BackgroundColor = Color.FromArgb("#F0F0F3"),
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = _status,
        };
    }

    protected override void RefreshFromContentContext()
    {
        if (ContentContext?.Block is not UIActionBlock action)
            return;

        var name = action.ToolName ?? "action";
        _status.Text = action.IsComplete
            ? $"✓ {name}"
            : $"Running {name}…";
        SemanticProperties.SetDescription(
            this,
            action.IsComplete
                ? $"Client action {name} completed"
                : $"Client action {name} is running");
    }
}
