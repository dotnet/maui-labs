using System.Drawing;
using UITests.Helpers;

namespace UITests;

/// <summary>
/// Tests for the Features page - navigates to feature sub-pages.
/// Uses NavigateToPageFresh to avoid Shell same-tab no-op issue.
/// </summary>
[Collection("WPF App")]
public class FeaturesPageTests
{
    readonly WpfAppFixture _fixture;

    public FeaturesPageTests(WpfAppFixture fixture)
    {
        _fixture = fixture;
    }

    void NavigateToFeatureSubPage(string featureName)
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Features");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        bool clicked = AutomationHelper.ClickButton(root, featureName);
        Assert.True(clicked, $"Could not find feature tile '{featureName}'");
        Thread.Sleep(2000);

        proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir,
            $"feature_{featureName.Replace(" ", "_").ToLower()}");
        Assert.True(bmp.Width > 100 && bmp.Height > 100,
            $"Screenshot too small for {featureName}");
    }

    [Theory]
    [InlineData("Animations")]
    [InlineData("AppThemeBinding")]
    [InlineData("Behaviors")]
    [InlineData("Clipping")]
    [InlineData("Colors")]
    [InlineData("Gestures")]
    [InlineData("Shadow")]
    [InlineData("Triggers")]
    public void FeatureSubPage_Renders(string featureName)
    {
        NavigateToFeatureSubPage(featureName);
    }

    [Fact]
    public void FeaturesPage_HasMultipleFeatures()
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Features");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        var allTexts = AutomationHelper.GetAllTextElements(root);

        var expected = new[] { "Animation", "Behavior", "Shadow", "Gesture", "Trigger", "Color" };
        int found = expected.Count(name => allTexts.Any(t => t.Contains(name, StringComparison.OrdinalIgnoreCase)));
        Assert.True(found >= 3, $"Expected feature names, found {found}/{expected.Length}");
    }

    [Fact]
    public void FeaturesPage_Screenshot()
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Features");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "features_overview");
        Assert.True(bmp.Width > 100 && bmp.Height > 100, "Features page screenshot too small");
    }
}
