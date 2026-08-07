using Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class ReasoningViewTests
{
    [Fact]
    public void VisibleReasoning_StartsCollapsedAndCanExpand()
    {
        var view = new ReasoningView();
        view.ApplyContentContext(BlockFactory.MakeReasoning("Check the catalog."));
        var (border, header, text) = GetParts(view);

        Assert.Equal("💡 Thought process ›", header.Text);
        Assert.Equal("Check the catalog.", text.Text);
        Assert.False(text.IsVisible);

        var tap = Assert.IsType<TapGestureRecognizer>(
            Assert.Single(border.GestureRecognizers));
        Assert.NotNull(tap.Command);
        tap.Command.Execute(null);

        Assert.Equal("💡 Thought process ⌄", header.Text);
        Assert.True(text.IsVisible);
    }

    [Fact]
    public void ProtectedReasoning_DoesNotExposeOrExpandProtectedData()
    {
        var view = new ReasoningView();
        view.ApplyContentContext(BlockFactory.MakeReasoning(
            text: null,
            protectedData: "secret-provider-data"));
        var (border, header, text) = GetParts(view);

        Assert.Equal("🔒 Protected reasoning", header.Text);
        Assert.Equal(string.Empty, text.Text);
        Assert.False(text.IsVisible);

        var tap = Assert.IsType<TapGestureRecognizer>(
            Assert.Single(border.GestureRecognizers));
        Assert.NotNull(tap.Command);
        tap.Command.Execute(null);

        Assert.False(text.IsVisible);
    }

    private static (Border Border, Label Header, Label Text) GetParts(
        ReasoningView view)
    {
        var border = Assert.IsType<Border>(view.Content);
        var stack = Assert.IsType<VerticalStackLayout>(border.Content);
        Assert.Equal(2, stack.Children.Count);
        return (
            border,
            Assert.IsType<Label>(stack.Children[0]),
            Assert.IsType<Label>(stack.Children[1]));
    }
}
