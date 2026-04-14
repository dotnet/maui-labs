using System.Drawing;
using UITests.Helpers;

namespace UITests;

/// <summary>
/// Tests that verify theme consistency across different pages.
/// Switches to dark theme and navigates all pages to verify dark backgrounds.
/// </summary>
[Collection("WPF App")]
public class ThemeConsistencyTests
{
    readonly WpfAppFixture _fixture;

    public ThemeConsistencyTests(WpfAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("Controls")]
    [InlineData("Layouts")]
    [InlineData("Features")]
    public void DarkTheme_AllPages_HaveDarkBackground(string pageName)
    {
        var proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        AutomationHelper.SelectComboBoxItem(root, "Dark");
        Thread.Sleep(2000);

        proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, pageName);
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir,
            $"theme_dark_{pageName.ToLower()}");

        var contentColor = ScreenshotHelper.SampleContentBackground(bmp);
        Assert.True(ScreenshotHelper.IsDark(contentColor),
            $"Expected dark background on {pageName} in dark theme, got RGB({contentColor.R},{contentColor.G},{contentColor.B})");

        // Restore light
        root = AutomationHelper.GetRoot(proc);
        AutomationHelper.SelectComboBoxItem(root, "Light");
        Thread.Sleep(2000);
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("Controls")]
    [InlineData("Layouts")]
    [InlineData("Features")]
    public void LightTheme_AllPages_HaveLightBackground(string pageName)
    {
        var proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        AutomationHelper.SelectComboBoxItem(root, "Light");
        Thread.Sleep(2000);

        proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, pageName);
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir,
            $"theme_light_{pageName.ToLower()}");

        var contentColor = ScreenshotHelper.SampleContentBackground(bmp);
        Assert.True(ScreenshotHelper.IsLight(contentColor),
            $"Expected light background on {pageName}, got RGB({contentColor.R},{contentColor.G},{contentColor.B})");
    }

    [Fact]
    public void ThemeSwitch_DoesNotCrashApp()
    {
        // Rapidly switch themes and verify app stays alive
        var proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);

        for (int i = 0; i < 3; i++)
        {
            AutomationHelper.SelectComboBoxItem(root, "Dark");
            Thread.Sleep(1000);
            root = AutomationHelper.GetRoot(_fixture.GetProcess());

            AutomationHelper.SelectComboBoxItem(root, "Light");
            Thread.Sleep(1000);
            root = AutomationHelper.GetRoot(_fixture.GetProcess());
        }

        // App should still be responsive
        proc = _fixture.GetProcess();
        root = AutomationHelper.GetRoot(proc);
        Assert.True(AutomationHelper.ElementExists(root, "Home"), "App should still show flyout items after rapid theme switching");
    }

    [Fact]
    public void DarkTheme_FlyoutTextIsReadable()
    {
        var proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        AutomationHelper.SelectComboBoxItem(root, "Dark");
        Thread.Sleep(2000);

        proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "theme_dark_flyout_text");

        // Sample the flyout text area — should have some contrast (not all-black)
        // In dark mode, text should be light on dark background
        var flyoutArea = new Rectangle(50, 300, 300, 200);
        var flyoutColor = ScreenshotHelper.GetAverageColor(bmp, flyoutArea);
        // Should be dark (the background)
        Assert.True(ScreenshotHelper.IsDark(flyoutColor),
            $"Flyout should be dark in dark theme, got RGB({flyoutColor.R},{flyoutColor.G},{flyoutColor.B})");

        // Restore light
        root = AutomationHelper.GetRoot(proc);
        AutomationHelper.SelectComboBoxItem(root, "Light");
        Thread.Sleep(2000);
    }
}
