namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class RichSuggestionTests
{
    [Fact]
    public void Suggestions_DefaultButton_UsesIconLabelAndSemanticDescription()
    {
        var layout = new FlexLayout();
        var control = new SuggestionsHostView();
        control.AttachSuggestions(layout);
        control.Suggestions.Add(new ChatSuggestion("Show products", "List all products")
        {
            Icon = "🌱",
        });

        var button = Assert.IsType<Button>(Assert.Single(layout.Children));
        Assert.Equal("🌱 Show products", button.Text);
        Assert.Equal(
            "Suggested prompt: Show products",
            SemanticProperties.GetDescription(button));
    }

    [Fact]
    public void SuggestionTemplate_ReceivesSuggestionAsBindingContext()
    {
        var layout = new FlexLayout();
        var suggestion = new ChatSuggestion("Weather", "Show weather");
        var control = new SuggestionsHostView
        {
            SuggestionTemplate = new DataTemplate(() => new Label()),
        };
        control.AttachSuggestions(layout);
        control.Suggestions.Add(suggestion);

        var label = Assert.IsType<Label>(Assert.Single(layout.Children));
        Assert.Same(suggestion, label.BindingContext);
    }

    [Fact]
    public void LegacySuggestionPrompts_RemainSupported()
    {
        var layout = new FlexLayout();
        var control = new SuggestionsHostView();
        control.AttachSuggestions(layout);
        control.SuggestionPrompts.Add("Tell me a joke");

        Assert.Equal("Tell me a joke", Assert.IsType<Button>(
            Assert.Single(layout.Children)).Text);
    }

    [Fact]
    public void LegacySuggestionPrompts_BlankEntries_AreIgnored()
    {
        var layout = new FlexLayout();
        var control = new SuggestionsHostView();
        control.AttachSuggestions(layout);
        control.SuggestionPrompts.Add(" ");
        control.SuggestionPrompts.Add("Valid");

        Assert.Equal("Valid", Assert.IsType<Button>(
            Assert.Single(layout.Children)).Text);
    }

    private sealed class SuggestionsHostView : CopilotChatView
    {
        public void AttachSuggestions(Layout layout) => AttachSuggestionsPart(layout);
    }
}
