using System.Xml.Linq;
using AIExtensions.Sample.ChatPlayground;

namespace Microsoft.Maui.AI.Chat.Tests;

public class PlaygroundLayoutTests
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
        if (overlay && width >= 44)
            Assert.True(width - layout.DiagnosticsPanelWidth >= 44);
    }

    [Fact]
    public void Metrics_ResizingAcrossBreakpoints_DoNotRetainPreviousColumnSpan()
    {
        double[] widths = [1200, 1199, 760, 759, 433, 434, 759, 760, 1199, 1200];
        int[] spans = [1, 2, 2, 3, 3, 3, 3, 2, 2, 1];

        Assert.Equal(spans, widths.Select(width =>
            new PlaygroundLayoutMetrics(width).MainColumnSpan(diagnosticsVisible: true)));
    }

    [Fact]
    public void Xaml_OverlaysAreFullPageSiblings_OutsidePaddedContent()
    {
        var document = LoadLayout();
        var outer = Assert.Single(document.Root!.Elements());
        Assert.Equal("Grid", outer.Name.LocalName);
        Assert.Equal("None", (string?)outer.Attribute("SafeAreaEdges"));
        Assert.Null(outer.Attribute("Padding"));
        Assert.Equal(
            ["RootGrid", "SettingsBackdrop", "SettingsPanel", "DiagnosticsBackdrop", "DiagnosticsPanel"],
            outer.Elements().Select(element => (string?)element.Attribute(Xaml + "Name")));

        var root = Named(document, "RootGrid");
        Assert.Equal("12", (string?)root.Attribute("Padding"));
        Assert.Equal("Container", (string?)root.Attribute("SafeAreaEdges"));
        Assert.Equal("PlaygroundContentGrid", (string?)root.Attribute("AutomationId"));
        Assert.Equal("PlaygroundMainPanel", (string?)Named(document, "MainPanel").Attribute("AutomationId"));

        foreach (var name in new[] { "SettingsBackdrop", "SettingsPanel", "DiagnosticsBackdrop", "DiagnosticsPanel" })
        {
            var element = Named(document, name);
            Assert.Same(outer, element.Parent);
            Assert.Null(element.Attribute("Grid.Column"));
            Assert.Null(element.Attribute("Grid.ColumnSpan"));
            Assert.Null(element.Attribute("HeightRequest"));
            Assert.Null(element.Attribute("VerticalOptions"));
        }

        AssertPanel(document, "SettingsPanel", "Start", "330", "10");
        AssertPanel(document, "DiagnosticsPanel", "End", "390", "20");
        AssertScrim(document, "SettingsBackdrop", "SettingsDismissOverlay", "CloseSettingsClicked", "9");
        AssertScrim(document, "DiagnosticsBackdrop",
            "{Binding DiagnosticsAutomationPrefix, Source={x:Reference PlaygroundLayout}, StringFormat='{0}LogsDismissOverlay'}",
            "CloseDiagnosticsClicked", "19");
    }

    [Fact]
    public void Xaml_DismissControlsAndDiagnosticsBindings_ArePreservedWithoutExtraLogsClose()
    {
        var document = LoadLayout();
        Assert.Equal("SettingsSidebar", (string?)Named(document, "SettingsPanel").Attribute("AutomationId"));
        var close = Named(document, "CloseSettingsButton");
        Assert.Equal("CloseSettingsButton", (string?)close.Attribute("AutomationId"));
        Assert.Equal("CloseSettingsClicked", (string?)close.Attribute("Clicked"));
        Assert.Equal("Close settings", (string?)close.Attribute("SemanticProperties.Description"));
        Assert.Equal("OpenSettingsButton", (string?)Named(document, "OpenSettingsButton").Attribute("AutomationId"));

        var panel = Named(document, "DiagnosticsPanel");
        Assert.Equal(
            "{Binding DiagnosticsAutomationPrefix, Source={x:Reference PlaygroundLayout}, StringFormat='{0}LogsSidebar'}",
            (string?)panel.Attribute("AutomationId"));
        var logs = Assert.Single(panel.Elements());
        Assert.Equal("ChatLogsView", logs.Name.LocalName);
        Assert.Equal("{Binding Diagnostics, Source={x:Reference PlaygroundLayout}}", (string?)logs.Attribute("BindingContext"));
        Assert.Equal("{Binding DiagnosticsAutomationPrefix, Source={x:Reference PlaygroundLayout}}",
            (string?)logs.Attribute("AutomationPrefix"));
        var toggle = Assert.Single(Named(document, "DiagnosticsToggle").Elements(),
            element => element.Name.LocalName == "ImageButton");
        Assert.Equal("{Binding Diagnostics.ToggleCommand, Source={x:Reference PlaygroundLayout}}",
            (string?)toggle.Attribute("Command"));
        Assert.Equal(
            "{Binding DiagnosticsAutomationPrefix, Source={x:Reference PlaygroundLayout}, StringFormat='Toggle{0}LogsButton'}",
            (string?)toggle.Attribute("AutomationId"));
        Assert.Single(document.Descendants(), element => (string?)element.Attribute("Source") == "close.png");
    }

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2009/xaml";

    private static XDocument LoadLayout()
    {
        using var stream = typeof(PlaygroundLayoutTests).Assembly.GetManifestResourceStream("PlaygroundPageLayout.xaml")!;
        return XDocument.Load(stream);
    }

    private static XElement Named(XDocument document, string name) =>
        Assert.Single(document.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == name);

    private static void AssertPanel(XDocument document, string name, string alignment, string width, string zIndex)
    {
        var panel = Named(document, name);
        Assert.Equal("12", (string?)panel.Attribute("Margin"));
        Assert.Equal(alignment, (string?)panel.Attribute("HorizontalOptions"));
        Assert.Equal(width, (string?)panel.Attribute("WidthRequest"));
        Assert.Equal(zIndex, (string?)panel.Attribute("ZIndex"));
        Assert.Equal("{StaticResource PlaygroundPanel}", (string?)panel.Attribute("Style"));
    }

    private static void AssertScrim(XDocument document, string name, string automationId, string handler, string zIndex)
    {
        var scrim = Named(document, name);
        Assert.Null(scrim.Attribute("Margin"));
        Assert.Equal("False", (string?)scrim.Attribute("IsVisible"));
        Assert.Equal(zIndex, (string?)scrim.Attribute("ZIndex"));
        Assert.Equal(automationId, (string?)scrim.Attribute("AutomationId"));
        Assert.Equal(
            "{AppThemeBinding Light={StaticResource OverlayScrim}, Dark={StaticResource OverlayScrimDark}}",
            (string?)scrim.Attribute("Color"));
        var tap = Assert.Single(scrim.Descendants(), element => element.Name.LocalName == "TapGestureRecognizer");
        Assert.Equal(handler, (string?)tap.Attribute("Tapped"));
    }
}
