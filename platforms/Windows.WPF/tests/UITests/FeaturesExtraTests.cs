using System.Drawing;
using UITests.Helpers;

namespace UITests;

/// <summary>
/// Tests for remaining feature sub-pages not covered in FeaturesPageTests.
/// </summary>
[Collection("WPF App")]
public class FeaturesExtraTests
{
    readonly WpfAppFixture _fixture;

    public FeaturesExtraTests(WpfAppFixture fixture)
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
    [InlineData("ContextMenu")]
    [InlineData("FontImageSource")]
    [InlineData("MenuBar")]
    [InlineData("Native Views")]
    [InlineData("Tooltips")]
    [InlineData("HybridWebView")]
    public void FeatureSubPage_Renders(string featureName)
    {
        NavigateToFeatureSubPage(featureName);
    }

    [Fact]
    public void FeaturesPage_HasAllFeatureButtons()
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Features");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        var allTexts = AutomationHelper.GetAllTextElements(root);

        var expected = new[] {
            "Animations", "AppThemeBinding", "Behaviors", "Clipping", "Colors",
            "ContextMenu", "FontImageSource", "Gestures", "MenuBar",
            "Native Views", "Shadow", "Tooltips", "Triggers", "HybridWebView"
        };
        int found = expected.Count(name => allTexts.Any(t => t == name));
        Assert.True(found >= 12, $"Expected at least 12 feature names, found {found}. Texts: {string.Join(", ", allTexts.Take(30))}");
    }
}
