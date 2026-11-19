using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.DevFlow.Tests;

public class LayoutCaptureScopeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CaptureLayoutSnapshot_ScopedRoot_CollectsOnlyItsPathAndRequestedDescendants(bool includeDescendants)
    {
        var child = new Label { AutomationId = "child", Frame = new Rect(0, 0, 80, 20) };
        var target = new Grid { AutomationId = "target", Frame = new Rect(0, 0, 80, 40), Children = { child } };
        var unrelated = new Grid { AutomationId = "unrelated", Frame = new Rect(0, 0, 80, 40) };
        for (var index = 0; index < 100; index++)
            unrelated.Children.Add(new Label { AutomationId = $"unrelated-{index}" });
        var parent = new Grid
        {
            AutomationId = "parent",
            IsClippedToBounds = true,
            Frame = new Rect(0, 0, 40, 40),
            Children = { target, unrelated }
        };
        var page = new ContentPage { Content = parent, Frame = new Rect(0, 0, 200, 200) };
        var app = new Application();
        typeof(Application).GetMethod("AddWindow",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(app, [new Window(page)]);
        var walker = new RecordingWalker();
        var request = new LayoutInspectionRequest
        {
            Scope = new LayoutInspectionScope { RootElementId = "target", IncludeDescendants = includeDescendants }
        };

        var capture = walker.CaptureLayoutSnapshot(app, request);
        walker.ApplyLayoutScope(capture, request.Scope);

        Assert.DoesNotContain(walker.Collected, id => id?.StartsWith("unrelated", StringComparison.Ordinal) == true);
        Assert.Contains("parent", walker.Collected);
        Assert.Contains("target", walker.Collected);
        Assert.Equal(includeDescendants, walker.Collected.Contains("child"));
        Assert.Equal(includeDescendants ? 2 : 1, capture.Nodes.Count);
        var root = Assert.Single(capture.Nodes, node => node.Element.Id == "target");
        Assert.Contains(root.ClipChain, clip => clip.ClipperElementId == "parent");
        Assert.Equal(40, root.VisibleRegion.Bounds.Width);
    }

    [Fact]
    public void CaptureLayoutSnapshot_MissingManagedRoot_PreservesCaptureUntilNativeEnrichment()
    {
        var label = new Label { AutomationId = "managed" };
        var app = new Application();
        typeof(Application).GetMethod("AddWindow",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(app, [new Window(new ContentPage { Content = label })]);
        var walker = new RecordingWalker();

        var capture = walker.CaptureLayoutSnapshot(app, new LayoutInspectionRequest
        {
            Scope = new LayoutInspectionScope { RootElementId = "native:root" }
        });

        Assert.Contains(capture.Nodes, node => node.Element.Id == "managed");
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    [InlineData(false, 2)]
    public void Analyze_ScopedRoot_RetainsParentOverflowWithoutEvaluatingAncestors(
        bool includeDescendants, double scale)
    {
        var target = new Grid
        {
            AutomationId = "target",
            Frame = new Rect(90, 0, 40, 40),
            MinimumWidthRequest = 0,
            Children = { new Label { AutomationId = "child", Frame = new Rect(0, 0, 10, 10), MinimumWidthRequest = 0 } }
        };
        var parent = new Grid
        {
            AutomationId = "parent",
            Frame = new Rect(0, 0, 100, 100),
            MinimumWidthRequest = 120,
            Children = { target }
        };
        var app = new Application();
        typeof(Application).GetMethod("AddWindow",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(app, [new Window(new ContentPage { Content = parent, Frame = new Rect(0, 0, 200, 200) })]);
        var walker = new RecordingWalker { Scale = scale };
        var request = new LayoutInspectionRequest
        {
            Scope = new LayoutInspectionScope { RootElementId = "target", IncludeDescendants = includeDescendants },
            Rules = [LayoutDiagnosticRules.ChildOutsideParent, LayoutDiagnosticRules.ConstraintViolation],
            MinimumSeverity = "info"
        };
        var capture = walker.CaptureLayoutSnapshot(app, request);
        var whole = LayoutDiagnosticsEngine.Analyze(capture, request, "test", true, null, walker.GetLayoutRuleSupport());
        var wholeOverflow = Assert.Single(whole.Findings, finding =>
            finding.RuleId == LayoutDiagnosticRules.ChildOutsideParent && finding.Element.Id == "target");
        Assert.Contains(whole.Findings, finding =>
            finding.RuleId == LayoutDiagnosticRules.ConstraintViolation && finding.Element.Id == "parent");

        walker.ApplyLayoutScope(capture, request.Scope);
        var scoped = LayoutDiagnosticsEngine.Analyze(capture, request, "test", true, null, walker.GetLayoutRuleSupport());

        var overflow = Assert.Single(scoped.Findings);
        Assert.Equal(wholeOverflow.RuleId, overflow.RuleId);
        Assert.Equal("target", overflow.Element.Id);
        Assert.Equal("parent", Assert.Single(overflow.RelatedElements).Element.Id);
        Assert.Equal(30 * scale, overflow.Evidence!.LayoutOverflowInsetsPhysicalPixels!.Right);
        Assert.Equal(wholeOverflow.Evidence!.LayoutOverflowInsetsPhysicalPixels!.Right,
            overflow.Evidence.LayoutOverflowInsetsPhysicalPixels.Right);
        Assert.Equal(includeDescendants ? 2 : 1, scoped.Snapshot.NodeCount);
        Assert.Equal(includeDescendants ? 3 : 1, scoped.Summary.Passes);
        Assert.Equal(0, scoped.Summary.NotApplicable);
        Assert.Equal(0, scoped.Summary.Violations);

        parent.Frame = new Rect(0, 0, 120, 100);
        var resized = walker.CaptureLayoutSnapshot(app, request);
        walker.ApplyLayoutScope(resized, request.Scope);
        Assert.Equal(capture.GeometryHash, resized.GeometryHash);
        Assert.NotEqual(capture.StabilityHash, resized.StabilityHash);
        Assert.NotEqual(capture.DiagnosticsHash, resized.DiagnosticsHash);
        var resizedReport = LayoutDiagnosticsEngine.Analyze(
            resized, request, "test", true, null, walker.GetLayoutRuleSupport());
        Assert.Equal(10 * scale, Assert.Single(resizedReport.Findings)
            .Evidence!.LayoutOverflowInsetsPhysicalPixels!.Right);
    }

    private sealed class RecordingWalker : VisualTreeWalker
    {
        public List<string?> Collected { get; } = [];
        public double Scale { get; init; } = 1;

        protected override double ResolveWindowScale(Window window) => Scale;

        protected override void PopulatePlatformLayoutMetrics(
            LayoutPlatformMetrics metrics, VisualElement element, ElementInfo info, LayoutInspectionRequest request)
            => Collected.Add(element.AutomationId);
    }
}
