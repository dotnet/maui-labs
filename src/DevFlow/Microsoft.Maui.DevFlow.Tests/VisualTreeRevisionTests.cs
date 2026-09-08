using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class VisualTreeRevisionTests
{
    [Fact]
    public void TreeAndLayoutCapture_UseTheSameAuthoritativeRevision()
    {
        var child = Element("child", 10, 20, 30, 40);
        var root = Element("root", 0, 0, 100, 200);
        root.Children = [child];

        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = root,
            TreeOrder = 0
        });
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = child,
            TreeOrder = 1
        });

        Assert.Equal(
            VisualTreeRevision.ComputeTree([root]),
            capture.GeometryHash);
    }

    [Fact]
    public void Revision_ExcludesBlazorNodesOnBothSurfaces()
    {
        var root = Element("root", 0, 0, 100, 200);
        var blazor = Element("web-node", 10, 10, 50, 20);
        blazor.Framework = "blazor";
        root.Children = [blazor];

        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = root,
            TreeOrder = 0
        });
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = blazor,
            TreeOrder = 1
        });

        Assert.Equal(
            VisualTreeRevision.ComputeTree([root]),
            capture.GeometryHash);
    }

    [Fact]
    public void Revision_ChangesWhenFindingRelevantTreeStateChanges()
    {
        var element = Element("label", 0, 0, 100, 20);
        element.Text = "Before";
        element.IsVisible = true;
        var before = VisualTreeRevision.ComputeTree([element]);

        element.Text = "After";
        element.IsVisible = false;
        var after = VisualTreeRevision.ComputeTree([element]);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void TreeRevision_DoesNotHashTextContent()
    {
        var element = Element("label", 0, 0, 100, 20);
        element.Text = "Before";
        var before = VisualTreeRevision.ComputeTree([element]);

        element.Text = "After";

        Assert.Equal(before, VisualTreeRevision.ComputeTree([element]));
    }

    [Fact]
    public void DiagnosticsRevision_IncludesBlazorGeometryAndTextEvidence()
    {
        var blazor = Element("web-node", 0, 0, 50, 20);
        blazor.Framework = "blazor";
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = blazor,
            FullRegion = LayoutRegionMath.FromRect(0, 0, 50, 20),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 50, 20),
            Text = new LayoutTextEvidence
            {
                IsTruncated = false,
                TextLength = 10
            }
        });
        var before = capture.DiagnosticsHash;

        capture.Nodes[0].Text!.IsTruncated = true;
        capture.Nodes[0].VisibleRegion =
            LayoutRegionMath.FromRect(0, 0, 25, 20);

        Assert.NotEqual(before, capture.DiagnosticsHash);
    }

    private static ElementInfo Element(
        string id,
        double x,
        double y,
        double width,
        double height)
        => new()
        {
            Id = id,
            Type = "Grid",
            Framework = "maui",
            WindowBounds = new BoundsInfo
            {
                X = x,
                Y = y,
                Width = width,
                Height = height
            }
        };
}
