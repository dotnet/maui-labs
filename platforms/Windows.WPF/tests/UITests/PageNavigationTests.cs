using System.Drawing;
using System.Windows.Automation;
using UITests.Helpers;

namespace UITests;

/// <summary>
/// Tests that navigate to each flyout page and verify content renders.
/// All navigation uses UIAutomation InvokePattern - works even with locked screen.
/// </summary>
[Collection("WPF App")]
public class PageNavigationTests
{
    readonly WpfAppFixture _fixture;

    static readonly string[] FlyoutPages = ["Home", "Controls", "Layouts", "Features", "Tips & Tricks"];

    public PageNavigationTests(WpfAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void FlyoutHeader_ShowsBotImage()
    {
        var proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "flyout_header");
        var headerColor = ScreenshotHelper.GetAverageColor(bmp, new Rectangle(50, 10, 250, 170));

        int pixelVariance = 0;
        for (int x = 80; x < Math.Min(350, bmp.Width); x += 10)
        {
            for (int y = 20; y < Math.Min(170, bmp.Height); y += 10)
            {
                var pixel = bmp.GetPixel(x, y);
                var diff = Math.Abs(pixel.R - headerColor.R) + Math.Abs(pixel.G - headerColor.G) + Math.Abs(pixel.B - headerColor.B);
                if (diff > 30) pixelVariance++;
            }
        }
        Assert.True(pixelVariance > 3, $"Expected visual content in header, variance={pixelVariance}");
    }

    [Fact]
    public void FlyoutFooter_HasAppearancePicker()
    {
        var proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        Assert.True(AutomationHelper.FindByName(root, "Appearance").Count > 0, "Expected 'Appearance' label");
        Assert.True(AutomationHelper.FindAll(root, ControlType.ComboBox).Count > 0, "Expected ComboBox");
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("Controls")]
    [InlineData("Layouts")]
    [InlineData("Features")]
    public void NavigateTo_Page_RendersContent(string pageName)
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPage(proc, pageName);
        proc = _fixture.GetProcess();

        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, $"page_{pageName.Replace(" ", "_").ToLower()}");
        Assert.True(bmp.Width > 100 && bmp.Height > 100, $"Screenshot too small: {bmp.Width}x{bmp.Height}");
    }

    [Fact]
    public void AllFlyoutItems_ArePresent()
    {
        var proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        foreach (var pageName in FlyoutPages)
        {
            Assert.True(AutomationHelper.FindByName(root, pageName).Count > 0, $"Missing flyout item '{pageName}'");
        }
    }

    [Fact]
    public void FlyoutBehavior_IsLocked()
    {
        var proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        var flyoutColor = ScreenshotHelper.GetAverageColor(bmp, new Rectangle(0, 200, 300, 400));
        Assert.NotEqual(Color.Empty, flyoutColor);

        var root = AutomationHelper.GetRoot(proc);
        Assert.True(AutomationHelper.FindByName(root, "Home").Count > 0, "Flyout should be locked open");
    }

    [Fact]
    public void NavigateTo_Home_ShowsExpectedContent()
    {
        var proc = _fixture.GetProcess();
        var root = AutomationHelper.NavigateToPage(proc, "Home");
        Assert.True(AutomationHelper.ElementExists(root, "Quick Links"), "Expected 'Quick Links'");
        Assert.True(AutomationHelper.ElementExists(root, "About .NET MAUI"), "Expected 'About .NET MAUI'");
    }

    [Fact]
    public void NavigateTo_Controls_ShowsControlNames()
    {
        var proc = _fixture.GetProcess();
        var root = AutomationHelper.NavigateToPage(proc, "Controls");
        var allTexts = AutomationHelper.GetAllTextElements(root);
        var expected = new[] { "Buttons", "Labels", "Entry", "Sliders" };
        int found = expected.Count(name => allTexts.Any(t => t.Contains(name, StringComparison.OrdinalIgnoreCase)));
        Assert.True(found >= 2, $"Expected control names, found {found}/{expected.Length}. Texts: {string.Join(", ", allTexts.Take(20))}");
    }

    [Fact]
    public void NavigateTo_Layouts_ShowsLayoutNames()
    {
        var proc = _fixture.GetProcess();
        var root = AutomationHelper.NavigateToPage(proc, "Layouts");
        var allTexts = AutomationHelper.GetAllTextElements(root);
        var expected = new[] { "AbsoluteLayout", "FlexLayout", "Grid", "StackLayout" };
        int found = expected.Count(name => allTexts.Any(t => t.Contains(name, StringComparison.OrdinalIgnoreCase)));
        Assert.True(found >= 2, $"Expected layout names, found {found}. Texts: {string.Join(", ", allTexts.Take(20))}");
    }

    [Fact]
    public void NavigateTo_Features_ShowsFeatureNames()
    {
        var proc = _fixture.GetProcess();
        var root = AutomationHelper.NavigateToPage(proc, "Features");
        var allTexts = AutomationHelper.GetAllTextElements(root);
        var expected = new[] { "Animation", "Behaviors", "Shadow", "Gestures" };
        int found = expected.Count(name => allTexts.Any(t => t.Contains(name, StringComparison.OrdinalIgnoreCase)));
        Assert.True(found >= 2, $"Expected feature names, found {found}. Texts: {string.Join(", ", allTexts.Take(20))}");
    }
}
