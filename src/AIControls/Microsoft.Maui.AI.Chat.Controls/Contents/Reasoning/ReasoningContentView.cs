using Microsoft.AspNetCore.Components.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Renders <see cref="ReasoningContentBlock"/> as an expandable "thinking" section.
/// </summary>
public class ReasoningContentView : ContentContextView
{
    private Label? _headerLabel;
    private Label? _contentLabel;
    private Border? _container;
    private bool _isExpanded;

    public ReasoningContentView()
    {
        var layout = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(12, 8) };

        _headerLabel = new Label
        {
            Text = "💭 Thinking...",
            FontSize = 12,
            TextColor = Color.FromArgb("#6B7280"),
            FontAttributes = FontAttributes.Italic,
        };

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += OnHeaderTapped;
        _headerLabel.GestureRecognizers.Add(tapGesture);

        _container = new Border
        {
            BackgroundColor = Color.FromArgb("#F9FAFB"),
            Padding = new Thickness(12, 8),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Stroke = Color.FromArgb("#E5E7EB"),
            IsVisible = false,
        };

        _contentLabel = new Label
        {
            FontSize = 11,
            TextColor = Color.FromArgb("#6B7280"),
            LineBreakMode = LineBreakMode.WordWrap,
        };
        _container.Content = _contentLabel;

        layout.Children.Add(_headerLabel);
        layout.Children.Add(_container);

        Content = layout;
    }

    protected override void RefreshFromContentContext()
    {
        if (ContentContext?.Block is not ReasoningContentBlock rcb)
            return;

        var isComplete = rcb.LifecycleState == BlockLifecycleState.Inactive;
        _headerLabel!.Text = isComplete ? "💭 Thought" : "💭 Thinking...";

        if (_isExpanded)
        {
            _contentLabel!.Text = rcb.IsEncrypted ? "[encrypted reasoning]" : rcb.Text;
        }
    }

    private void OnHeaderTapped(object? sender, TappedEventArgs e)
    {
        _isExpanded = !_isExpanded;
        _container!.IsVisible = _isExpanded;

        if (_isExpanded && ContentContext?.Block is ReasoningContentBlock rcb)
        {
            _contentLabel!.Text = rcb.IsEncrypted ? "[encrypted reasoning]" : rcb.Text;
        }
    }
}
