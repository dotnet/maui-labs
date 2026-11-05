using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Pages a library attaches straight to the native window (popups) must be
/// visible to tree, query, and lookup, like any other page.
/// </summary>
public class VisualTreeWalkerHostedRootTests
{
    [Fact]
    public void HostedPopupPage_IsWalkedQueriedAndFoundById()
    {
        var (app, _) = AppWithPage(new Label { Text = "Settings" });
        var confirm = new Button { Text = "Reset", AutomationId = "confirm-reset" };
        var walker = new HostingWalker(() => [new ContentPage { Content = confirm }]);

        var tree = walker.WalkTree(app);
        Assert.Contains(Flatten(tree), e => e.AutomationId == "confirm-reset");

        var hit = Assert.Single(walker.Query(app, automationId: "confirm-reset"));
        Assert.Same(confirm, walker.GetElementById(hit.Id, app));
    }

    [Fact]
    public void HostedRootAlreadyInTheWindow_IsNotWalkedTwice()
    {
        var (app, window) = AppWithPage(new Label { Text = "Settings", AutomationId = "title" });
        var walker = new HostingWalker(() => [window.Page!]);

        Assert.Single(walker.Query(app, automationId: "title"));
    }

    [Fact]
    public void FailingDiscovery_LeavesTheRegularTreeIntact()
    {
        var (app, _) = AppWithPage(new Label { Text = "Settings", AutomationId = "title" });
        var walker = new HostingWalker(() => throw new InvalidOperationException("native view gone"));

        Assert.Single(walker.Query(app, automationId: "title"));
    }

    [Fact]
    public void PopupParentedToTheWindowButNotListed_IsDetected()
    {
        // UXDivers-style: Parent points at the window so resources flow, but the
        // window does not list the popup among its visual children.
        var (_, window) = AppWithPage(new Label { Text = "Settings" });
        var popup = new ContentPage { Content = new Button { Text = "Reset" } };
        popup.Parent = window;

        Assert.Same(popup, DetachedRootProbe.Find(((ContentPage)popup).Content!, window));
    }

    [Fact]
    public void ContentOfTheWindowPage_IsNotDetached()
    {
        var label = new Label { Text = "Settings" };
        var (_, window) = AppWithPage(label);

        Assert.Null(DetachedRootProbe.Find(label, window));
    }

    sealed class DetachedRootProbe : VisualTreeWalker
    {
        public static IVisualTreeElement? Find(IVisualTreeElement element, Window window) => FindDetachedRoot(element, window);
    }

    static (Application App, Window Window) AppWithPage(View content)
    {
        var window = new Window(new ContentPage { Content = content });
        return (new WindowApp(window), window);
    }

    static IEnumerable<ElementInfo> Flatten(IEnumerable<ElementInfo> elements)
    {
        foreach (var element in elements)
        {
            yield return element;
            foreach (var child in Flatten(element.Children ?? []))
                yield return child;
        }
    }

    sealed class HostingWalker(Func<IReadOnlyList<IVisualTreeElement>> hosted) : VisualTreeWalker
    {
        protected override IReadOnlyList<IVisualTreeElement> GetHostedRoots(Window window) => hosted();
    }

    // Without a platform, Application.Windows stays empty and the walker walks the
    // application's visual children instead, so expose the window that way.
    sealed class WindowApp(Window window) : Application, IVisualTreeElement
    {
        IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => [window];
    }
}
