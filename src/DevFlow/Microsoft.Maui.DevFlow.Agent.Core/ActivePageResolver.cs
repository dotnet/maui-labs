using Microsoft.Maui.Controls;

namespace Microsoft.Maui.DevFlow.Agent.Core;

internal static class ActivePageResolver
{
    public static Page? Resolve(Window? window) => ResolveVisiblePages(window).FirstOrDefault();

    public static IReadOnlyList<Page> ResolveVisiblePages(Window? window)
    {
        if (window is null)
            return [];

        var modal = window.Navigation?.ModalStack?.LastOrDefault();
        if (modal is not null)
            return ResolveLeaf(modal) is { } modalLeaf ? [modalLeaf] : [];

        var active = new List<Page>();
        if (ResolveLeaf(window.Page) is not { } detailLeaf)
            return active;

        active.Add(detailLeaf);
        for (Element? current = detailLeaf; current is not null; current = current.Parent)
        {
            if (current is not FlyoutPage flyoutPage ||
                !ShouldIncludeFlyout(flyoutPage) ||
                ResolveLeaf(flyoutPage.Flyout) is not { } flyoutLeaf ||
                active.Contains(flyoutLeaf, ReferenceEqualityComparer.Instance))
            {
                continue;
            }

            active.Add(flyoutLeaf);
        }

        return active;
    }

    public static Page? ResolveLeaf(Page? page)
    {
        var visited = new HashSet<Page>(ReferenceEqualityComparer.Instance);
        while (page is not null && visited.Add(page))
        {
            switch (page)
            {
                case Shell shell:
                    page = shell.CurrentPage;
                    break;
                case NavigationPage navigationPage:
                    page = navigationPage.CurrentPage;
                    break;
                case TabbedPage tabbedPage:
                    page = tabbedPage.CurrentPage;
                    break;
                case FlyoutPage flyoutPage:
                    page = flyoutPage.Detail;
                    break;
                default:
                    return page;
            }
        }

        return page;
    }

    public static bool IsElementInActivePageContext(Element element, Page activePage)
        => IsElementInActivePageContext(element, [activePage]);

    public static bool IsElementInActivePageContext(
        Element element,
        IReadOnlyCollection<Page> activePages)
    {
        if (activePages.Count == 0)
            return true;

        for (Element? current = element; current is not null; current = current.Parent)
        {
            if (current is Page page)
                return activePages.Any(activePage => IsPageOnActivePath(page, activePage));
        }

        return true;
    }

    public static bool IsPageOnActivePath(Page page, Page activePage)
    {
        for (Element? current = activePage; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, page))
                return true;
        }

        return false;
    }

    private static bool ShouldIncludeFlyout(FlyoutPage flyoutPage) =>
        flyoutPage.IsPresented ||
        flyoutPage.FlyoutLayoutBehavior == FlyoutLayoutBehavior.Split ||
        (flyoutPage.FlyoutLayoutBehavior == FlyoutLayoutBehavior.SplitOnLandscape &&
            flyoutPage.Width > flyoutPage.Height) ||
        (flyoutPage.FlyoutLayoutBehavior == FlyoutLayoutBehavior.SplitOnPortrait &&
            flyoutPage.Height >= flyoutPage.Width);
}
