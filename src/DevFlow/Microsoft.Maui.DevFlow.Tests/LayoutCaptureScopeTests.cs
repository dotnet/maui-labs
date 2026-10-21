using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.DevFlow.Tests;

[Collection("Layout capture application")]
public class LayoutCaptureScopeTests : IDisposable
{
    private readonly Application? _previousApplication = Application.Current;

    public void Dispose() => Application.Current = _previousApplication;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CaptureLayoutSnapshot_LiveClientBounds_PreserveNativeAndDomClipsBeyondRequestedWindow(bool includeDom)
    {
        var nativeChild = new Button { AutomationId = "native-child", Frame = new Rect(0, 0, 80, 80) };
        var clip = new Grid
        {
            AutomationId = "native-clip", Frame = new Rect(1290, 86, 40, 40),
            IsClippedToBounds = true, Children = { nativeChild }
        };
        var edgeChild = new Button { AutomationId = "window-edge", Frame = new Rect(1290, 200, 80, 80) };
        var webView = new Microsoft.Maui.Controls.WebView { AutomationId = "webview", Frame = new Rect(0, 134, 1330, 755) };
        var window = new Window(new ContentPage
        {
            Frame = new Rect(0, 0, 1330, 889),
            Content = new Grid { Frame = new Rect(0, 0, 1330, 889), Children = { clip, edgeChild, webView } }
        }) { Width = 1024, Height = 684 };
        var app = WithWindows(window);
        var walker = new RecordingWalker { Scale = 2 };
        walker.ClientBounds[window] = new LayoutRectInfo { Width = 1330, Height = 889 };
        walker.WindowBounds[nativeChild] = new BoundsInfo { X = 1290, Y = 86, Width = 80, Height = 80 };
        var request = new LayoutInspectionRequest
        {
            Profile = "strict",
            Rules = [LayoutDiagnosticRules.ElementClipped],
            Scope = new LayoutInspectionScope { IncludeWebViewElements = includeDom }
        };

        var capture = walker.CaptureLayoutSnapshot(app, request);
        var nativeRevision = capture.GeometryHash;
        var windowInfo = Assert.Single(capture.Windows);
        Assert.Equal(1330, windowInfo.Bounds!.Width);
        Assert.Equal(889, windowInfo.Bounds.Height);
        Assert.Equal(2, windowInfo.Scale);
        Assert.Equal(1024, window.Width);
        Assert.Equal(684, window.Height);
        Assert.Equal(1330 * 755, Assert.Single(capture.Nodes, node => node.Element.Id == "webview").VisibleRegion.Area);
        if (includeDom)
        {
            using var snapshot = JsonDocument.Parse("""
                {
                  "viewport":{"width":1330,"height":755},
                  "nodes":[
                    {"index":0,"parentIndex":-1,"tag":"div","visible":true,
                     "rect":{"x":1274,"y":16,"width":40,"height":40}},
                    {"index":1,"parentIndex":0,"tag":"button","visible":true,"interactive":true,
                     "rect":{"x":1274,"y":16,"width":80,"height":80},
                     "ancestorClips":[{"clipperIndex":0,"kind":"ancestor-layout-clip",
                                      "rect":{"x":1274,"y":16,"width":40,"height":40}}]}
                  ]
                }
                """);
            MauiDevFlowAgentService.AppendBlazorLayoutNodes(capture,
                new CdpWebViewInfo { Index = 7, Owner = new WeakReference<object>(webView) },
                snapshot.RootElement, request);
        }
        Assert.Equal(nativeRevision, capture.GeometryHash);
        walker.ApplyLayoutScope(capture, request.Scope);
        var result = LayoutDiagnosticsEngine.Analyze(capture, request, "test", true, null, walker.GetLayoutRuleSupport());
        var native = Assert.Single(result.Findings, finding => finding.Element.Id == "native-child");
        Assert.Equal(0.75, native.Evidence!.LostAreaRatio);
        Assert.Contains(native.RelatedElements, related => related.Element.Id == "native-clip");
        var edge = Assert.Single(capture.Nodes, node => node.Element.Id == "window-edge");
        Assert.Equal(40 * 80, edge.VisibleRegion.Area);
        Assert.Contains(edge.ClipChain, contribution => contribution.Kind == "window-edge" && contribution.LostAreaRatio == 0.5);
        if (includeDom)
        {
            var dom = Assert.Single(result.Findings, finding => finding.Element.Id == "web-7-1");
            Assert.Equal(0.75, dom.Evidence!.LostAreaRatio);
            Assert.Equal(1274, dom.Evidence.FullRegion!.Bounds.X);
            Assert.Equal(150, dom.Evidence.FullRegion.Bounds.Y);
            Assert.Equal("webview", Assert.Single(capture.Nodes, node => node.Element.Id == "web-7-0").Element.ParentId);
        }
        else
        {
            Assert.DoesNotContain(capture.Nodes, node => node.Element.Framework == "webview");
            Assert.DoesNotContain(result.Findings, finding => finding.Element.Id.StartsWith("web-", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void CaptureLayoutSnapshot_LiveClientResize_UsesCurrentWindowWithoutExpandingToFitNodes()
    {
        var child = new Button { AutomationId = "edge", Frame = new Rect(1290, 86, 80, 80) };
        var window = new Window(new ContentPage
        {
            Frame = new Rect(0, 0, 1330, 889),
            Content = child
        }) { Width = 1024, Height = 684 };
        var walker = new RecordingWalker();
        var app = WithWindows(window);
        var request = new LayoutInspectionRequest();
        walker.ClientBounds[window] = new LayoutRectInfo { Width = 1330, Height = 889 };
        var grown = walker.CaptureLayoutSnapshot(app, request);
        Assert.Equal(40 * 80, Assert.Single(grown.Nodes, node => node.Element.Id == "edge").VisibleRegion.Area);

        walker.ClientBounds[window] = new LayoutRectInfo { Width = 1000, Height = 650 };
        var shrunk = walker.CaptureLayoutSnapshot(app, request);
        Assert.Equal(1000, Assert.Single(shrunk.Windows).Bounds!.Width);
        Assert.Equal(650, Assert.Single(shrunk.Windows).Bounds!.Height);
        var outside = Assert.Single(shrunk.Nodes, node => node.Element.Id == "edge");
        Assert.Equal(0, outside.VisibleRegion.Area);
        Assert.Contains(outside.ClipChain, contribution => contribution.Kind == "window-edge" && contribution.LostAreaRatio == 1);
        Assert.Equal(1024, window.Width);
        Assert.Equal(684, window.Height);
    }

    [Fact]
    public void CaptureLayoutSnapshot_SelectedWindow_UsesOnlyItsLiveClientBounds()
    {
        var first = new Window(new ContentPage());
        var second = new Window(new ContentPage()) { Width = 1024, Height = 684 };
        var walker = new RecordingWalker();
        walker.ClientBounds[first] = new LayoutRectInfo { Width = 400, Height = 300 };
        walker.ClientBounds[second] = new LayoutRectInfo { Width = 1330, Height = 889 };
        var capture = walker.CaptureLayoutSnapshot(WithWindows(first, second),
            new LayoutInspectionRequest { Scope = new LayoutInspectionScope { Window = 1 } });

        var window = Assert.Single(capture.Windows);
        Assert.Equal("window-1", window.Id);
        Assert.Equal(1330, window.Bounds!.Width);
        Assert.Equal(889, window.Bounds.Height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void CaptureLayoutSnapshot_UnavailableClientBounds_RetainsManagedFallback(double nativeWidth)
    {
        var window = new Window(new ContentPage()) { Width = 1024, Height = 684 };
        var walker = new RecordingWalker();
        walker.ClientBounds[window] = new LayoutRectInfo { Width = nativeWidth, Height = 889 };
        var capture = walker.CaptureLayoutSnapshot(WithWindows(window), new LayoutInspectionRequest());

        var bounds = Assert.Single(capture.Windows).Bounds!;
        Assert.Equal(1024, bounds.Width);
        Assert.Equal(684, bounds.Height);
    }

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
        public Dictionary<Window, LayoutRectInfo> ClientBounds { get; } = [];
        public Dictionary<VisualElement, BoundsInfo> WindowBounds { get; } = [];

        protected override LayoutRectInfo? ResolveWindowClientBounds(Window window)
            => ClientBounds.GetValueOrDefault(window);

        protected override BoundsInfo? ResolveWindowBounds(VisualElement element)
            => WindowBounds.GetValueOrDefault(element) ?? base.ResolveWindowBounds(element);

        protected override double ResolveWindowScale(Window window) => Scale;

        protected override void PopulatePlatformLayoutMetrics(
            LayoutPlatformMetrics metrics, VisualElement element, ElementInfo info, LayoutInspectionRequest request)
            => Collected.Add(element.AutomationId);
    }

    private static Application WithWindows(params Window[] windows)
    {
        var app = new Application();
        var addWindow = typeof(Application).GetMethod("AddWindow",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        foreach (var window in windows)
            addWindow.Invoke(app, [window]);
        return app;
    }
}

[CollectionDefinition("Layout capture application", DisableParallelization = true)]
public class LayoutCaptureApplicationCollection
{
}
