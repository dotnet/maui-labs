using System.Drawing;
using System.Windows.Automation;
using UITests.Helpers;

namespace UITests;

/// <summary>
/// Tests for the Controls page - navigates to each control sub-page.
/// Uses NavigateToPageFresh to avoid Shell same-tab no-op issue.
/// </summary>
[Collection("WPF App")]
public class ControlsPageTests
{
    readonly WpfAppFixture _fixture;

    public ControlsPageTests(WpfAppFixture fixture)
    {
        _fixture = fixture;
    }

    void NavigateToControlSubPage(string controlName)
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Controls");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        bool clicked = AutomationHelper.ClickButtonWithScroll(root, controlName);
        Assert.True(clicked, $"Could not find control tile '{controlName}'");
        Thread.Sleep(2000);

        proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir,
            $"control_{controlName.Replace(" ", "_").ToLower()}");
        Assert.True(bmp.Width > 100 && bmp.Height > 100,
            $"Screenshot too small for {controlName}: {bmp.Width}x{bmp.Height}");
    }

    [Theory]
    [InlineData("ActivityIndicator")]
    [InlineData("Borders")]
    [InlineData("Buttons")]
    [InlineData("CheckBoxes")]
    [InlineData("CollectionView")]
    [InlineData("Editors")]
    [InlineData("Entry")]
    [InlineData("Images")]
    [InlineData("Labels")]
    [InlineData("Picker")]
    [InlineData("Progress Bars")]
    [InlineData("Radio Buttons")]
    [InlineData("SearchBar")]
    [InlineData("Shapes")]
    [InlineData("Sliders")]
    [InlineData("Steppers")]
    public void ControlSubPage_Renders(string controlName)
    {
        NavigateToControlSubPage(controlName);
    }

    [Fact]
    public void ControlsPage_HasButtons()
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Controls");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        int buttonCount = AutomationHelper.CountElements(root, ControlType.Button);
        Assert.True(buttonCount >= 5, $"Expected at least 5 buttons, found {buttonCount}");
    }

    [Fact]
    public void ControlsPage_Screenshot()
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Controls");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "controls_overview");
        var contentColor = ScreenshotHelper.SampleContentBackground(bmp);
        Assert.NotEqual(Color.Empty, contentColor);
    }

    [Fact]
    public void ControlsPage_ShowsMultipleControlNames()
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Controls");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        var allTexts = AutomationHelper.GetAllTextElements(root);

        var expected = new[] { "Buttons", "Labels", "Entry", "Sliders", "Images", "Shapes", "Borders" };
        int found = expected.Count(name => allTexts.Any(t => t == name));
        Assert.True(found >= 3, $"Expected at least 3 control names, found {found}. Texts: {string.Join(", ", allTexts.Take(30))}");
    }
}
