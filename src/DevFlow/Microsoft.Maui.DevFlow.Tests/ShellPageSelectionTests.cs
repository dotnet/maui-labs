using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class ShellPageSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WalkElement_ShellContentTitle_ReportsTitleAndFlyoutVisibility(bool flyoutIsPresented)
    {
        var (shell, _) = CreateShell();
        shell.FlyoutIsPresented = flyoutIsPresented;

        var elements = VisualTreeWalker.FlattenElementInfos(
            new[] { new VisualTreeWalker().WalkElement(shell, null, 0, 0)! });

        var title = Assert.Single(elements, element => element.Type == "NavBarTitle");
        Assert.Equal("Home", title.Text);
        Assert.Equal(flyoutIsPresented,
            Assert.Single(elements, element => element.Type == "FlyoutItem").IsVisible);
    }

    [Fact]
    public void WalkElement_CustomShellTitleView_DoesNotAddSyntheticTitle()
    {
        var (shell, page) = CreateShell();
        Shell.SetTitleView(page, new Label { Text = "Custom title" });

        var elements = VisualTreeWalker.FlattenElementInfos(
            new[] { new VisualTreeWalker().WalkElement(shell, null, 0, 0)! });

        Assert.DoesNotContain(elements, element => element.Type == "NavBarTitle");
    }

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

    private static (Shell Shell, ContentPage Page) CreateShell()
    {
        var page = new ContentPage();
        var content = new ShellContent { Title = "Home", Content = page };
        var section = new ShellSection { Items = { content } };
        var item = new FlyoutItem { Items = { section } };
        var shell = new Shell { Items = { item }, CurrentItem = item };
        return (shell, page);
    }
}
