using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class BlazorLayoutDiagnosticsTests
{
    [Fact]
    public void AppendBlazorLayoutNodes_SeparatesDirectTextFromDescendantOverflow()
    {
        var capture = new LayoutCaptureSnapshot();
        capture.Nodes.Add(new LayoutNodeSnapshot
        {
            Element = new ElementInfo
            {
                Id = "webview",
                Type = "BlazorWebView",
                IsVisible = true
            },
            FullRegion = LayoutRegionMath.FromRect(0, 0, 100, 100),
            VisibleRegion = LayoutRegionMath.FromRect(0, 0, 100, 100),
            WindowId = "window",
            WindowScale = 1
        });
        using var document = JsonDocument.Parse(
            """
            {
              "viewport": {
                "width": 100,
                "height": 100,
                "devicePixelRatio": 1,
                "visualScale": 1
              },
              "totalElementCount": 2,
              "crossOriginFrames": 0,
              "nodes": [
                {
                  "index": 0,
                  "parentIndex": -1,
                  "tag": "div",
                  "visible": true,
                  "rect": { "x": 0, "y": 0, "width": 50, "height": 20 },
                  "clientWidth": 50,
                  "clientHeight": 20,
                  "scrollWidth": 100,
                  "scrollHeight": 20,
                  "overflowX": "hidden",
                  "overflowY": "visible",
                  "widthOverflow": true,
                  "heightOverflow": false,
                  "directText": null
                },
                {
                  "index": 1,
                  "parentIndex": 0,
                  "tag": "span",
                  "visible": true,
                  "rect": { "x": 0, "y": 0, "width": 100, "height": 20 },
                  "clientWidth": 100,
                  "clientHeight": 20,
                  "scrollWidth": 100,
                  "scrollHeight": 20,
                  "overflowX": "visible",
                  "overflowY": "visible",
                  "widthOverflow": false,
                  "heightOverflow": false,
                  "ancestorClips": [{
                    "clipperIndex": 0,
                    "kind": "ancestor-layout-clip",
                    "rect": { "x": 0, "y": 0, "width": 50, "height": 20 }
                  }],
                  "directText": {
                    "kind": "horizontal-hard-clip",
                    "truncated": true,
                    "length": 18,
                    "text": "direct clipped text",
                    "renderedLineCount": 1,
                    "contentWidth": 100,
                    "contentHeight": 20,
                    "availableWidth": 50,
                    "availableHeight": 20
                  }
                }
              ]
            }
            """);

        MauiDevFlowAgentService.AppendBlazorLayoutNodes(
            capture,
            new CdpWebViewInfo
            {
                Index = 0,
                ElementId = "webview"
            },
            document.RootElement,
            new LayoutInspectionRequest
            {
                Privacy = new LayoutPrivacyOptions { Text = "length" }
            });

        var parent = Assert.Single(
            capture.Nodes,
            node => node.Element.Id == "web-0-0");
        var text = Assert.Single(
            capture.Nodes,
            node => node.Element.Id == "web-0-1");
        Assert.Null(parent.Text);
        Assert.NotNull(parent.ContentRegion);
        Assert.True(parent.ContentRegion.Area > parent.FullRegion.Area);
        Assert.True(text.Text?.IsTruncated);
        Assert.Equal("browser-range-direct-text", text.Text?.MeasurementSource);
        Assert.Equal(18, text.Text?.TextLength);
        Assert.Null(text.Text?.Text);
        Assert.Equal(1000, text.VisibleRegion.Area);
        Assert.Equal(
            "web-0-0",
            Assert.Single(text.ClipChain).ClipperElementId);
    }
}
