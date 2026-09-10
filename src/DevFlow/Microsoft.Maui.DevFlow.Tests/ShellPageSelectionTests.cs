using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class ShellPageSelectionTests
{
    [Fact]
    public void WalkElement_HiddenTabs_ReportsTheShellCurrentPage()
    {
        var home = new ContentPage { AutomationId = "home" };
        var catalog = new ContentPage { AutomationId = "catalog" };
        var first = new ShellSection { Items = { new ShellContent { Content = home } } };
        var second = new ShellSection { Items = { new ShellContent { Content = catalog } } };
        var tabs = new TabBar { Items = { first, second } };
        var shell = new Shell { Items = { tabs }, CurrentItem = tabs };
        Shell.SetTabBarIsVisible(shell, false);
        tabs.CurrentItem = first;
        var walker = new VisualTreeWalker();

        Assert.Same(home, shell.CurrentPage);
        Assert.True(walker.WalkElement(home, null, 0, 1)!.IsSelected);
        Assert.False(walker.WalkElement(catalog, null, 0, 1)!.IsSelected);

        tabs.CurrentItem = second;

        Assert.Same(catalog, shell.CurrentPage);
        Assert.False(walker.WalkElement(home, null, 0, 1)!.IsSelected);
        Assert.True(walker.WalkElement(catalog, null, 0, 1)!.IsSelected);
    }
}
