using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class VisualTreeWalkerSyntheticTests
{
    [Fact]
    public void ToolbarItemId_IsStableAndIncludesAutomationId()
    {
        var item = new ToolbarItem
        {
            AutomationId = "ToolbarAction1",
            Text = "Action1"
        };

        var first = VisualTreeWalker.GetToolbarItemId(item);
        var second = VisualTreeWalker.GetToolbarItemId(item);

        Assert.Equal(first, second);
        Assert.StartsWith("ToolbarAction1_", first);
    }
}
