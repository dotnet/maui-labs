using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Driver;
using Xunit;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// MAUI Shell keeps every tab/flyout page mounted and visible, so a tree pull contains all pages at
/// once. HtmlRenderer must drop the inactive ones (via PageFilter) so the inspector doesn't stack a
/// stale page's elements over the live one after navigation.
/// </summary>
public class InspectorPageFilterTests
{
    private static ElementInfo El(string id, string type, double x, double y, double w, double h,
        bool visible = true, bool selected = false, params ElementInfo[] children) => new()
    {
        Id = id,
        Type = type,
        IsVisible = visible,
        IsSelected = selected,
        WindowBounds = new BoundsInfo { X = x, Y = y, Width = w, Height = h },
        Children = children.Length > 0 ? children.ToList() : null,
    };

    // active page: descendants laid out down the viewport body; inactive: collapsed to the origin.
    private static ElementInfo LaidOutPage(string id, string type) =>
        El(id, type, 0, 100, 400, 800,
            children:
            [
                El(id + "-l1", "Label", 20, 200, 200, 40),
                El(id + "-l2", "Button", 20, 300, 200, 40),
                El(id + "-l3", "Entry", 20, 400, 200, 40),
            ]);

    private static ElementInfo CollapsedPage(string id, string type) =>
        El(id, type, 0, 100, 400, 800,
            children:
            [
                El(id + "-l1", "Label", 0, 0, 200, 40),
                El(id + "-l2", "Button", 0, 0, 200, 40),
            ]);

    [Fact]
    public void SingleVisiblePage_RendersEverything()
    {
        var tree = new List<ElementInfo>
        {
            El("shell", "Shell", 0, 0, 400, 900,
                children: El("sc", "ShellContent", 0, 100, 400, 800, children: LaidOutPage("p1", "HomePage"))),
        };

        var html = HtmlRenderer.RenderElements(tree);

        Assert.Contains("data-id=\"p1\"", html);
        Assert.Contains("data-id=\"p1-l1\"", html);
    }

    [Fact]
    public void InactiveTabPage_IsDroppedWithItsWrapper()
    {
        var tree = new List<ElementInfo>
        {
            El("shell", "Shell", 0, 0, 400, 900,
                children:
                [
                    El("sc-active", "ShellContent", 0, 100, 400, 800, children: LaidOutPage("active", "SubscriptionsPage")),
                    El("sc-inactive", "ShellContent", 0, 100, 400, 800, children: CollapsedPage("inactive", "InsightsPage")),
                ]),
        };

        var html = HtmlRenderer.RenderElements(tree);

        // Active page (and its wrapper) stay.
        Assert.Contains("data-id=\"active\"", html);
        Assert.Contains("data-id=\"active-l1\"", html);
        Assert.Contains("data-id=\"sc-active\"", html);
        // Inactive page, its content, and its now-empty exclusive wrapper are dropped.
        Assert.DoesNotContain("data-id=\"inactive\"", html);
        Assert.DoesNotContain("data-id=\"inactive-l1\"", html);
        Assert.DoesNotContain("data-id=\"sc-inactive\"", html);
    }

    [Fact]
    public void AmbiguousPages_DropNothing_FailSafe()
    {
        // Both pages equally laid out → no strict winner → render both rather than risk hiding the
        // page the user is looking at.
        var tree = new List<ElementInfo>
        {
            El("shell", "Shell", 0, 0, 400, 900,
                children:
                [
                    El("sc1", "ShellContent", 0, 100, 400, 800, children: LaidOutPage("pA", "PageA")),
                    El("sc2", "ShellContent", 0, 100, 400, 800, children: LaidOutPage("pB", "PageB")),
                ]),
        };

        var html = HtmlRenderer.RenderElements(tree);

        Assert.Contains("data-id=\"pA\"", html);
        Assert.Contains("data-id=\"pB\"", html);
    }

    [Fact]
    public void SelectedTab_AuthoritativelyKeepsNamedPage_EvenIfOtherIsPopulated()
    {
        // A selected Tab whose id encodes the target page type wins even when the just-left page still
        // has laid-out content (MAUI hasn't torn it down yet).
        var tree = new List<ElementInfo>
        {
            El("shell", "Shell", 0, 0, 400, 900,
                children:
                [
                    El("tab_InsightsPage", "Tab", 300, 850, 100, 50, selected: true),
                    El("sc1", "ShellContent", 0, 100, 400, 800, children: LaidOutPage("subs", "SubscriptionsPage")),
                    El("sc2", "ShellContent", 0, 100, 400, 800, children: LaidOutPage("insights", "InsightsPage")),
                ]),
        };

        var html = HtmlRenderer.RenderElements(tree);

        // The selected "insights" Tab names InsightsPage → keep it, drop the other page.
        Assert.Contains("data-id=\"insights\"", html);
        Assert.DoesNotContain("data-id=\"subs\"", html);
    }

    [Fact]
    public void SelectFrameTree_UsesOnlyTheScreenshottedPageSubtree()
    {
        var underlying = El("under", "ContentPage", 0, 0, 400, 900);
        var sheet = El("sheet", "ContentPage", 0, 32, 400, 868,
            children: El("sheet-button", "Button", 20, 80, 120, 40));
        var tree = new List<ElementInfo>
        {
            El("window", "Window", 0, 0, 400, 900, children: [underlying, sheet]),
        };

        var frameTree = InspectorServer.SelectFrameTree(tree, "sheet");
        var html = HtmlRenderer.RenderElements(frameTree, rootOffsetY: 32);

        Assert.Single(frameTree);
        Assert.Equal("sheet", frameTree[0].Id);
        Assert.DoesNotContain("data-id=\"window\"", html);
        Assert.DoesNotContain("data-id=\"under\"", html);
        Assert.Contains("data-id=\"sheet\"", html);
        Assert.DoesNotContain("top:-", html);
    }
}
