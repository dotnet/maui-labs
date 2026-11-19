using AIExtensions.Sample.ChatPlayground;

namespace Microsoft.Maui.AI.Chat.Tests;

public class PlaygroundLayoutMetricsTests
{
    [Theory]
    [InlineData(1200, false, false, 330, 390, 1)]
    [InlineData(1199, false, true, 330, 390, 2)]
    [InlineData(760, false, true, 330, 390, 2)]
    [InlineData(759, true, true, 330, 390, 3)]
    [InlineData(434, true, true, 330, 390, 3)]
    [InlineData(433, true, true, 330, 389, 3)]
    [InlineData(320, true, true, 320, 276, 3)]
    [InlineData(44, true, true, 44, 0, 3)]
    [InlineData(20, true, true, 20, 0, 3)]
    [InlineData(0, false, true, 330, 0, 2)]
    [InlineData(-1, false, true, 330, 0, 2)]
    public void Metrics_AtResponsiveBoundaries_ReserveOverlayDismissArea(
        double width, bool compact, bool overlay, double settingsWidth, double diagnosticsWidth, int openSpan)
    {
        var layout = new PlaygroundLayoutMetrics(width);

        Assert.Equal(compact, layout.IsCompact);
        Assert.Equal(overlay, layout.DiagnosticsOverlay);
        Assert.Equal(settingsWidth, layout.SettingsPanelWidth);
        Assert.Equal(diagnosticsWidth, layout.DiagnosticsPanelWidth);
        Assert.Equal(openSpan, layout.MainColumnSpan(diagnosticsVisible: true));
        Assert.Equal(compact ? 3 : 2, layout.MainColumnSpan(diagnosticsVisible: false));
    }
}
