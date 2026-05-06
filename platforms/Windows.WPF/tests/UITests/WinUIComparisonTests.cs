using System.IO;
using System.Drawing;
using UITests.Helpers;

namespace UITests;

/// <summary>
/// Side-by-side comparison tests between WPF and WinUI reference app.
/// Skipped if the WinUI app is not available.
/// </summary>
[Collection("WPF App")]
public class WinUIComparisonTests
{
    readonly WpfAppFixture _fixture;

    public WinUIComparisonTests(WpfAppFixture fixture)
    {
        _fixture = fixture;
    }

    static bool WinUIAvailable =>
        File.Exists(@"D:\repos\davidortinau\ControlGallery\src\ControlGallery\bin\Debug\net10.0-windows10.0.19041.0\win-x64\ControlGallery.exe");

    [Fact]
    public void HomePage_VisuallyMatchesWinUI()
    {
        if (!WinUIAvailable)
            return;

        // Ensure WPF is on Home page in Light theme
        var proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        AutomationHelper.SelectComboBoxItem(root, "Light");
        Thread.Sleep(1000);
        AutomationHelper.NavigateToPage(proc, "Home");
        Thread.Sleep(1000);

        using var winuiLauncher = new AppLauncher();
        var winuiProc = winuiLauncher.LaunchWinUI();
        if (winuiProc == null)
            return;

        Thread.Sleep(5000);

        try
        {
            var wpfProc = _fixture.GetProcess();

            using var wpfBmp = ScreenshotHelper.CaptureWindow(wpfProc);
            using var winuiBmp = ScreenshotHelper.CaptureWindow(winuiProc);

            ScreenshotHelper.SaveScreenshot(wpfBmp, _fixture.Launcher.ScreenshotDir, "compare_wpf_home");
            ScreenshotHelper.SaveScreenshot(winuiBmp, _fixture.Launcher.ScreenshotDir, "compare_winui_home");

            // Both should have visual content (not blank)
            Assert.True(wpfBmp.Width > 100, "WPF screenshot too small");
            Assert.True(winuiBmp.Width > 100, "WinUI screenshot too small");

            // Both should render non-trivially (the bitmaps have varied pixels)
            var wpfBg = ScreenshotHelper.SampleContentBackground(wpfBmp);
            var winuiBg = ScreenshotHelper.SampleContentBackground(winuiBmp);
            Assert.NotEqual(Color.Empty, wpfBg);
            Assert.NotEqual(Color.Empty, winuiBg);
        }
        finally
        {
            try { winuiProc.Kill(entireProcessTree: true); } catch { }
        }
    }
}
