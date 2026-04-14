using System.Drawing;
using UITests.Helpers;

namespace UITests;

/// <summary>
/// Screenshot regression tests - captures every page and checks for blank rendering.
/// Also verifies theme switching propagates to all pages.
/// </summary>
[Collection("WPF App")]
public class ScreenshotRegressionTests
{
    readonly WpfAppFixture _fixture;

    public ScreenshotRegressionTests(WpfAppFixture fixture)
    {
        _fixture = fixture;
    }

    static bool HasVisualContent(Bitmap bmp)
    {
        var firstPixel = bmp.GetPixel(bmp.Width / 4, bmp.Height / 4);
        int diffCount = 0;
        for (int x = 50; x < bmp.Width - 50; x += bmp.Width / 10)
        {
            for (int y = 50; y < bmp.Height - 50; y += bmp.Height / 10)
            {
                var px = bmp.GetPixel(x, y);
                var diff = Math.Abs(px.R - firstPixel.R) + Math.Abs(px.G - firstPixel.G) + Math.Abs(px.B - firstPixel.B);
                if (diff > 20) diffCount++;
            }
        }
        return diffCount > 5;
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("Controls")]
    [InlineData("Layouts")]
    [InlineData("Features")]
    public void Page_HasVisualContent(string pageName)
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPage(proc, pageName);
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir,
            $"regression_{pageName.ToLower()}");
        Assert.True(HasVisualContent(bmp), $"{pageName} page appears blank");
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("Controls")]
    public void Page_DarkTheme_HasVisualContent(string pageName)
    {
        var proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        AutomationHelper.SelectComboBoxItem(root, "Dark");
        Thread.Sleep(2000);

        proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPage(proc, pageName);
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir,
            $"regression_{pageName.ToLower()}_dark");

        var contentColor = ScreenshotHelper.SampleContentBackground(bmp);
        Assert.True(ScreenshotHelper.IsDark(contentColor),
            $"Expected dark content on {pageName}, got RGB({contentColor.R},{contentColor.G},{contentColor.B})");

        // Restore light
        root = AutomationHelper.GetRoot(proc);
        AutomationHelper.SelectComboBoxItem(root, "Light");
        Thread.Sleep(2000);
    }

    [Fact]
    public void AllPages_ScreenshotSweep()
    {
        var pages = new[] { "Home", "Controls", "Layouts", "Features" };
        foreach (var page in pages)
        {
            var proc = _fixture.GetProcess();
            AutomationHelper.NavigateToPage(proc, page);
            Thread.Sleep(1000);

            proc = _fixture.GetProcess();
            using var bmp = ScreenshotHelper.CaptureWindow(proc);
            ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir,
                $"sweep_{page.ToLower()}");
            Assert.True(bmp.Width > 100, $"{page} screenshot failed");
        }
    }

    [Fact]
    public void Window_HasReasonableSize()
    {
        var proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        Assert.True(bmp.Width >= 600, $"Window too narrow: {bmp.Width}px");
        Assert.True(bmp.Height >= 400, $"Window too short: {bmp.Height}px");
    }

    [Fact]
    public void Window_HasTitle()
    {
        var proc = _fixture.GetProcess();
        // Window title changes per page but should never be empty
        Assert.False(string.IsNullOrWhiteSpace(proc.MainWindowTitle),
            "Window should have a non-empty title");
    }
}
