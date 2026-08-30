using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Driver;
using Xunit;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// MAUI Shell keeps every tab/flyout page mounted and visible, so a tree pull contains all pages at
/// once. The canonical activeVisual projection must drop inactive pages before any Inspector
/// surface renders or serializes the tree.
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

        var projected = PageFilter.ProjectActiveVisualInPlace(tree);
        var html = HtmlRenderer.RenderElements(projected);

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

        var projected = PageFilter.ProjectActiveVisualInPlace(tree);
        var html = HtmlRenderer.RenderElements(projected);

        // Active page (and its wrapper) stay.
        Assert.Contains("data-id=\"active\"", html);
        Assert.Contains("data-id=\"active-l1\"", html);
        Assert.Contains("data-id=\"sc-active\"", html);
        // Inactive page, its content, and its now-empty exclusive wrapper are dropped.
        Assert.DoesNotContain("data-id=\"inactive\"", html);
        Assert.DoesNotContain("data-id=\"inactive-l1\"", html);
        Assert.DoesNotContain("data-id=\"sc-inactive\"", html);
        Assert.DoesNotContain(projected[0].Children!, child => child.Id == "sc-inactive");
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

        var projected = PageFilter.ProjectActiveVisualInPlace(tree);
        var html = HtmlRenderer.RenderElements(projected);

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

        var projected = PageFilter.ProjectActiveVisualInPlace(tree);
        var html = HtmlRenderer.RenderElements(projected);

        // The selected "insights" Tab names InsightsPage → keep it, drop the other page.
        Assert.Contains("data-id=\"insights\"", html);
        Assert.DoesNotContain("data-id=\"subs\"", html);
    }

    [Fact]
    public void CustomNamedCurrentPage_DropsCollapsedPageSiblings()
    {
        var tree = new List<ElementInfo>
        {
            El("shell", "Shell", 0, 0, 400, 900,
                children:
                [
                    CollapsedPage("home", "MainPage"),
                    El("goals", "GoalsPage", 0, 0, 400, 800,
                        children: El("stale-scroll", "ScrollView", 0, 0, 380, 700,
                            children: El("stale-label", "Label", 20, 180, 200, 40))),
                    LaidOutPage("subtraction", "Subtraction"),
                ]),
        };

        var projected = PageFilter.ProjectActiveVisualInPlace(tree);
        var html = HtmlRenderer.RenderElements(projected);

        Assert.Contains("data-id=\"subtraction\"", html);
        Assert.DoesNotContain("data-id=\"home\"", html);
        Assert.DoesNotContain("data-id=\"goals\"", html);
        Assert.DoesNotContain("data-id=\"stale-scroll\"", html);
    }

    [Fact]
    public void ExplicitCurrentPage_KeepsNavigationAncestor()
    {
        var active = LaidOutPage("active", "Lesson");
        active.Role = "page";
        active.IsSelected = true;
        var stale = LaidOutPage("stale", "PreviousLesson");
        stale.Role = "page";
        var navigation = El("navigation", "NavigationPage", 0, 0, 400, 900,
            children: [stale, active]);
        navigation.Role = "page";
        var tree = new List<ElementInfo> { navigation };

        var projected = PageFilter.ProjectActiveVisualInPlace(tree);
        var html = HtmlRenderer.RenderElements(projected);

        Assert.Contains("data-id=\"navigation\"", html);
        Assert.Contains("data-id=\"active\"", html);
        Assert.DoesNotContain("data-id=\"stale\"", html);
    }

    [Fact]
    public void ProjectActiveVisualInPlace_MutatesOnlyTheProvidedFrameTree()
    {
        var inactive = CollapsedPage("inactive", "InsightsPage");
        var active = LaidOutPage("active", "SubscriptionsPage");
        var tree = new List<ElementInfo>
        {
            El("shell", "Shell", 0, 0, 400, 900,
                children:
                [
                    El("active-wrapper", "ShellContent", 0, 100, 400, 800, children: active),
                    El("inactive-wrapper", "ShellContent", 0, 100, 400, 800, children: inactive),
                ]),
        };

        var projected = PageFilter.ProjectActiveVisualInPlace(tree);

        Assert.Same(tree, projected);
        Assert.DoesNotContain(projected[0].Children!, child => child.Id == "inactive-wrapper");
        Assert.Equal("inactive", inactive.Id);
        Assert.NotNull(inactive.Children);
    }

    [Fact]
    public void InspectSnapshotPayload_UsesRevisionedActiveVisualContractWithoutTransportDetails()
    {
        var roots = new List<ElementInfo>
        {
            El("root", "ContentPage", 0, 0, 400, 800),
        };

        var payload = InspectorSnapshotService.Create(
            "snapshot-1",
            DateTime.UnixEpoch,
            "screenshot.png?frame=snapshot-1",
            roots,
            400,
            800,
            0,
            20,
            "agent-1",
            "Sample",
            "windows");
        using var json = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            }));
        var root = json.RootElement;

        Assert.Equal("activeVisual", root.GetProperty("projection").GetString());
        Assert.Equal("snapshot-1", root.GetProperty("revision").GetString());
        Assert.Equal("agent-1", root.GetProperty("target").GetProperty("agentId").GetString());
        Assert.False(root.GetProperty("target").TryGetProperty("port", out _));
        Assert.False(root.GetProperty("target").TryGetProperty("agentInstanceId", out _));
        Assert.Equal(20, root.GetProperty("viewport").GetProperty("rootOffsetY").GetDouble());
        Assert.Equal("root", root.GetProperty("roots")[0].GetProperty("id").GetString());
    }

    [Fact]
    public void FilterActiveMatches_RemovesCandidatesOutsideTheCanonicalProjection()
    {
        var active = new List<ElementInfo>
        {
            El("root", "ContentPage", 0, 0, 400, 800,
                children: El("active", "Button", 20, 20, 100, 40)),
        };
        var candidates = new[]
        {
            El("active", "Button", 20, 20, 100, 40),
            El("inactive", "Button", 20, 80, 100, 40),
        };

        var matches = InspectorSnapshotService.FilterActiveMatches(active, candidates);

        Assert.Single(matches);
        Assert.Equal("active", matches[0].Id);
    }

    [Fact]
    public void InspectorQueryResponse_DeclaresItsCanonicalProjection()
    {
        var response = new InspectorQueryResponse
        {
            Projection = "activeVisual",
            SnapshotId = "snapshot-1",
            Revision = "snapshot-1",
            Elements = [],
        };

        Assert.Equal("activeVisual", response.Projection);
        Assert.Equal(response.SnapshotId, response.Revision);
    }

    [Fact]
    public void TrimDepth_CapsCanonicalSnapshotsWithoutChangingTheRoot()
    {
        var roots = new List<ElementInfo>
        {
            El("root", "ContentPage", 0, 0, 400, 800,
                children: El("layout", "Grid", 0, 0, 400, 800,
                    children: El("button", "Button", 20, 20, 100, 40))),
        };

        InspectorSnapshotService.TrimDepth(roots, 2);

        Assert.Equal("root", roots[0].Id);
        Assert.Single(roots[0].Children!);
        Assert.Null(roots[0].Children![0].Children);
    }

    [Theory]
    [InlineData(null, null, null, true, false, 0, 0, "x and y")]
    [InlineData("list", null, null, true, true, 0, 10, "coordinate scrolling")]
    [InlineData(null, -1, null, false, false, 0, 0, "itemIndex")]
    [InlineData(null, null, "middle", false, false, 0, 0, "scrollToPosition")]
    [InlineData(null, null, null, false, false, 1000001, 0, "deltas")]
    public void ValidateInspectScrollArguments_InvalidModes_ReturnActionableError(
        string? elementId,
        int? itemIndex,
        string? position,
        bool hasX,
        bool hasY,
        double deltaX,
        double deltaY,
        string expected)
    {
        var error = InspectorServer.ValidateInspectScrollArguments(
            elementId,
            itemIndex,
            position,
            hasX,
            hasY,
            deltaX,
            deltaY);

        Assert.Contains(expected, error);
    }

    [Theory]
    [InlineData("list", 4, "Center", false, false, 0, 0)]
    [InlineData(null, null, null, true, true, 0, 120)]
    [InlineData(null, null, null, false, false, 0, -240)]
    public void ValidateInspectScrollArguments_ValidModes_AreAccepted(
        string? elementId,
        int? itemIndex,
        string? position,
        bool hasX,
        bool hasY,
        double deltaX,
        double deltaY)
    {
        Assert.Null(InspectorServer.ValidateInspectScrollArguments(
            elementId,
            itemIndex,
            position,
            hasX,
            hasY,
            deltaX,
            deltaY));
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

    [Fact]
    public void InspectorLiveEventHandler_OnlyRefreshesFramesForVisualEventTypes()
    {
        var assembly = typeof(InspectorServer).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "Microsoft.Maui.Cli.DevFlow.Inspector.Web.devflow.js");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var script = reader.ReadToEnd();

        Assert.Contains("message = JSON.parse(event.data);", script);
        Assert.Contains("type = message.type || null;", script);
        Assert.Contains(
            "['treeChange', 'navigation', 'lifecycle', 'themeChange', 'alert'].includes(type)",
            script);
        Assert.DoesNotContain("if (!document.hidden && !replaying) scheduleRefresh(150);", script);
    }
}
