using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class ActivePageResolverTests
{
    [Fact]
    public void Resolve_UsesCurrentNavigationLeaf()
    {
        var active = new ContentPage();
        var navigation = new NavigationPage(active);
        var window = new Window(navigation);

        Assert.Same(active, ActivePageResolver.Resolve(window));
        Assert.True(ActivePageResolver.IsPageOnActivePath(navigation, active));
    }

    [Fact]
    public void IsElementInActivePageContext_RejectsRetainedPreviousPage()
    {
        var activeButton = new Button();
        var active = new ContentPage { Content = activeButton };
        var staleButton = new Button();
        _ = new ContentPage { Content = staleButton };

        Assert.True(ActivePageResolver.IsElementInActivePageContext(activeButton, active));
        Assert.False(ActivePageResolver.IsElementInActivePageContext(staleButton, active));
    }

    [Fact]
    public void ResolveVisiblePages_IncludesPresentedFlyoutAndDetail()
    {
        var flyoutButton = new Button();
        var flyout = new ContentPage { Title = "Menu", Content = flyoutButton };
        var detailButton = new Button();
        var detail = new ContentPage { Content = detailButton };
        var root = new FlyoutPage
        {
            Flyout = flyout,
            Detail = detail,
            FlyoutLayoutBehavior = FlyoutLayoutBehavior.Popover,
            IsPresented = true,
        };

        var activePages = ActivePageResolver.ResolveVisiblePages(new Window(root));

        Assert.Contains(flyout, activePages);
        Assert.Contains(detail, activePages);
        Assert.True(ActivePageResolver.IsElementInActivePageContext(flyoutButton, activePages));
        Assert.True(ActivePageResolver.IsElementInActivePageContext(detailButton, activePages));
    }

    [Fact]
    public void ResolveVisiblePages_ExcludesHiddenPopoverFlyout()
    {
        var flyout = new ContentPage { Title = "Menu" };
        var detail = new ContentPage();
        var root = new FlyoutPage
        {
            Flyout = flyout,
            Detail = detail,
            FlyoutLayoutBehavior = FlyoutLayoutBehavior.Popover,
            IsPresented = false,
        };

        var activePages = ActivePageResolver.ResolveVisiblePages(new Window(root));

        Assert.Equal([detail], activePages);
    }

    [Fact]
    public void Resolve_WhenContainerHasNoCurrentPage_FailsOpen()
    {
        var navigation = new NavigationPage();

        Assert.Null(ActivePageResolver.Resolve(new Window(navigation)));
        Assert.Empty(ActivePageResolver.ResolveVisiblePages(new Window(navigation)));
    }

    [Fact]
    public void WalkTree_MarksPageRoleAndCurrentLeaf()
    {
        var active = new ContentPage();
        var navigation = new NavigationPage(active);
        var roots = new VisualTreeWalker().WalkTree(new TestApplication(new Window(navigation)));
        var pages = VisualTreeWalker.FlattenElementInfos(roots)
            .Where(element => element.Role == "page")
            .ToList();

        Assert.Contains(pages, page => page.Type == nameof(NavigationPage) && !page.IsSelected);
        Assert.Contains(pages, page => page.Type == nameof(ContentPage) && page.IsSelected);
    }

    [Fact]
    public void SyntheticHitFiltering_RejectsToolbarAndSearchFromRetainedInactivePage()
    {
        var active = new ContentPage();
        var inactive = new ContentPage();
        var toolbarItem = new ToolbarItem { Text = "Inactive action" };
        inactive.ToolbarItems.Add(toolbarItem);
        var search = new VisualTreeWalker.SearchHandlerMarker
        {
            Handler = new SearchHandler(),
            Page = inactive,
        };
        IReadOnlyCollection<Page> activePages = [active];

        Assert.False(DevFlowAgentService.IsSyntheticInActivePageContext(toolbarItem, activePages));
        Assert.False(DevFlowAgentService.IsSyntheticInActivePageContext(search, activePages));
    }

    private sealed class TestApplication(Window window) : Application, IVisualTreeElement
    {
        IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => [window];

        IVisualTreeElement? IVisualTreeElement.GetVisualParent() => null;
    }
}
