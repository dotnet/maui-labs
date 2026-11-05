using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class InspectorLayoutDiagnosticsTests
{
    [Fact]
    public void Render_IncludesDiagnosticsPanelAndOverlayHost()
    {
        var html = HtmlRenderer.Render(
        [
            new ElementInfo
            {
                Id = "root",
                Type = "Grid",
                IsVisible = true,
                IsEnabled = true,
                Bounds = new BoundsInfo { Width = 100, Height = 100 }
            }
        ],
        hasScreenshot: false);

        Assert.Contains("id=\"df-diagnostics-pane\"", html);
        Assert.Contains("id=\"diagnostic-overlays\"", html);
        Assert.Contains("id=\"df-tab-layout\"", html);
        Assert.Contains("data-tab=\"layout\"", html);
    }
}
