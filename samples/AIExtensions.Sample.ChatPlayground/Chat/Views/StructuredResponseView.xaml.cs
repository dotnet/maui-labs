using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Shows a structured JSON response as a card, or any other JSON formatted.</summary>
public partial class StructuredResponseView : BlockView
{
    private bool _showJson;

    /// <summary>Initializes the view.</summary>
    public StructuredResponseView() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnBlockChanged() => Render();

    private void OnJsonToggleClicked(object? sender, EventArgs e)
    {
        _showJson = !_showJson;
        Render();
    }

    private void Render()
    {
        if (SummaryLabel is null)
            return;

        var block = Block as RichContentBlock;
        var text = block?.RawText ?? string.Empty;
        var formatted = StructuredResponseFormatter.TryFormat(text);

        if (!StructuredResponseFormatter.TryReadResponse(text, out var response))
        {
            // Incomplete JSON is still streaming (or was stopped); complete JSON outside the schema is shown as is.
            var streaming = formatted is null && block?.LifecycleState != BlockLifecycleState.Inactive;
            HeadingLabel.Text = formatted is not null ? "JSON"
                : streaming ? "RECEIVING STRUCTURED RESPONSE\u2026"
                : "INCOMPLETE JSON";
            JsonToggle.IsVisible = false;
            ResponsePanel.IsVisible = false;
            JsonPanel.IsVisible = true;
            JsonLabel.Text = formatted ?? text.Trim();
            SemanticProperties.SetDescription(this, HeadingLabel.Text);
            return;
        }

        HeadingLabel.Text = "STRUCTURED RESPONSE";
        JsonToggle.IsVisible = true;
        JsonToggle.Text = _showJson ? "Hide JSON" : "Show JSON";
        SemanticProperties.SetDescription(JsonToggle, _showJson ? "Show the response card" : "Show the response JSON");
        ResponsePanel.IsVisible = !_showJson;
        JsonPanel.IsVisible = _showJson;
        JsonLabel.Text = formatted;

        SummaryLabel.Text = response.Summary;
        BindableLayout.SetItemsSource(KeyPointsLayout, response.KeyPoints);
        KeyPointsLayout.IsVisible = response.KeyPoints.Count > 0;

        CategoryLabel.Text = response.Category;
        CategoryChip.IsVisible = !string.IsNullOrWhiteSpace(response.Category);
        SentimentLabel.Text = response.Sentiment.ToString();
        SentimentDot.SetDynamicResource(BoxView.ColorProperty, response.Sentiment switch
        {
            PlaygroundSentiment.Positive => AIChatThemeKeys.SuccessColor,
            PlaygroundSentiment.Negative => AIChatThemeKeys.ErrorColor,
            PlaygroundSentiment.Mixed => AIChatThemeKeys.AccentColor,
            _ => AIChatThemeKeys.SecondaryTextColor,
        });
        SemanticProperties.SetDescription(CategoryChip, $"Category: {response.Category}");
        SemanticProperties.SetDescription(SentimentChip, $"Sentiment: {response.Sentiment}");
        SemanticProperties.SetDescription(this, "Structured response");
    }
}
