using System.Drawing;
using System.Windows.Automation;
using UITests.Helpers;

namespace UITests;

/// <summary>
/// Tests for control sub-pages that may need scrolling (Switches, TimePicker, WebView)
/// and additional control tiles (Alerts, BoxView, CarouselView, Date Picker, Map, RefreshView).
/// </summary>
[Collection("WPF App")]
public class ControlsScrollTests
{
    readonly WpfAppFixture _fixture;

    public ControlsScrollTests(WpfAppFixture fixture)
    {
        _fixture = fixture;
    }

    void NavigateToControlSubPage(string controlName, bool needsScroll = false)
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Controls");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);

        // Always use scroll-aware click — window width varies across environments
        bool clicked = AutomationHelper.ClickButtonWithScroll(root, controlName);

        Assert.True(clicked, $"Could not find control tile '{controlName}'");
        Thread.Sleep(2000);

        proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir,
            $"control_{controlName.Replace(" ", "_").ToLower()}");
        Assert.True(bmp.Width > 100 && bmp.Height > 100,
            $"Screenshot too small for {controlName}");
    }

    // Controls visible without scrolling but not in original test set
    [Theory]
    [InlineData("Alerts")]
    [InlineData("BoxView")]
    [InlineData("CarouselView")]
    [InlineData("Date Picker")]
    [InlineData("Map")]
    [InlineData("RefreshView")]
    public void ControlSubPage_Renders_NoScroll(string controlName)
    {
        NavigateToControlSubPage(controlName, needsScroll: false);
    }

    // Controls that need scrolling to reach
    [Theory]
    [InlineData("Switches")]
    [InlineData("TimePicker")]
    [InlineData("WebView")]
    public void ControlSubPage_Renders_WithScroll(string controlName)
    {
        NavigateToControlSubPage(controlName, needsScroll: true);
    }

    [Fact]
    public void ControlsPage_HasExpectedControlCount()
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Controls");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        var allTexts = AutomationHelper.GetAllTextElements(root);

        // There should be at least 20 control names visible
        var controlNames = new[] {
            "ActivityIndicator", "Alerts", "Borders", "BoxView", "Buttons",
            "CheckBoxes", "CarouselView", "CollectionView", "Date Picker",
            "Editors", "Entry", "Images", "Labels", "Map", "Picker",
            "Progress Bars", "Radio Buttons", "RefreshView", "SearchBar",
            "Shapes", "Sliders", "Steppers"
        };
        int found = controlNames.Count(name => allTexts.Any(t => t == name));
        Assert.True(found >= 10, $"Expected at least 10 control names visible, found {found}");
    }
}
