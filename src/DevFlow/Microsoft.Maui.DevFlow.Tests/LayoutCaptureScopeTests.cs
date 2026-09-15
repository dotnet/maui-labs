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

    private sealed class RecordingWalker : VisualTreeWalker
    {
        public List<string?> Collected { get; } = [];

        protected override void PopulatePlatformLayoutMetrics(
            LayoutPlatformMetrics metrics, VisualElement element, ElementInfo info, LayoutInspectionRequest request)
            => Collected.Add(element.AutomationId);
    }
}
