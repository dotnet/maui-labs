using Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class UIActionViewTests
{
    [Fact]
    public void PendingAction_ShowsRunningState()
    {
        var view = new UIActionView();

        view.ApplyContentContext(BlockFactory.MakeUIAction("Refresh"));

        Assert.Equal("Running Refresh…", GetStatus(view).Text);
    }

    [Fact]
    public void CompletedAction_ShowsCompletedState()
    {
        var view = new UIActionView();

        view.ApplyContentContext(BlockFactory.MakeUIAction("Refresh", completed: true));

        Assert.Equal("✓ Refresh", GetStatus(view).Text);
    }

    private static Label GetStatus(UIActionView view)
    {
        var border = Assert.IsType<Border>(view.Content);
        return Assert.IsType<Label>(border.Content);
    }
}
