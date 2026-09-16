using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.Controls.Shapes;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Renders model reasoning as an initially collapsed assistant-side disclosure.</summary>
public sealed class ReasoningView : ContentContextView
{
    private readonly Label _header;
    private readonly Label _text;
    private bool _expanded;

    public ReasoningView()
    {
        AutomationId = "AssistantReasoning";
        SemanticProperties.SetDescription(this, "Assistant thought process");

        _header = new Label
        {
            Text = "💡 Thought process ›",
            FontAttributes = FontAttributes.Bold,
            FontSize = 12,
            TextColor = Colors.Gray,
        };
        _text = new Label
        {
            IsVisible = false,
            FontSize = 12,
            TextColor = Colors.Gray,
            LineBreakMode = LineBreakMode.WordWrap,
        };

        var border = new Border
        {
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 4),
            HorizontalOptions = LayoutOptions.Start,
            StrokeThickness = 0,
            BackgroundColor = Color.FromArgb("#F0F0F3"),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children = { _header, _text },
            },
        };
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(ToggleExpanded),
        });
        Content = border;
    }

    protected override void RefreshFromContentContext()
    {
        if (ContentContext?.Block is not ReasoningContentBlock reasoning)
            return;

        if (reasoning.IsEncrypted)
        {
            _header.Text = "🔒 Protected reasoning";
            _text.Text = string.Empty;
            _text.IsVisible = false;
            return;
        }

        _text.Text = reasoning.Text;
        _header.Text = _expanded ? "💡 Thought process ⌄" : "💡 Thought process ›";
        _text.IsVisible = _expanded && reasoning.Text.Length > 0;
    }

    private void ToggleExpanded()
    {
        if (ContentContext?.Block is not ReasoningContentBlock { IsEncrypted: false } reasoning
            || reasoning.Text.Length == 0)
        {
            return;
        }

        _expanded = !_expanded;
        _header.Text = _expanded ? "💡 Thought process ⌄" : "💡 Thought process ›";
        _text.IsVisible = _expanded;
    }
}
