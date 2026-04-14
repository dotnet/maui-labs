using System.Drawing;
using UITests.Helpers;

namespace UITests;

/// <summary>
/// Tests for Animation sub-pages (Fade, Rotate, Scale, Translate).
/// Navigates Features → Animations → sub-page.
/// </summary>
[Collection("WPF App")]
public class AnimationTests
{
    readonly WpfAppFixture _fixture;

    public AnimationTests(WpfAppFixture fixture)
    {
        _fixture = fixture;
    }

    void NavigateToAnimationSubPage(string animName)
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Features");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        bool clicked = AutomationHelper.ClickButton(root, "Animations");
        Assert.True(clicked, "Could not find 'Animations' tile");
        Thread.Sleep(2000);

        // Now on Animations page — find the sub-page button
        proc = _fixture.GetProcess();
        root = AutomationHelper.GetRoot(proc);
        clicked = AutomationHelper.ClickButton(root, animName);
        Assert.True(clicked, $"Could not find animation '{animName}'");
        Thread.Sleep(2000);

        proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir,
            $"anim_{animName.Replace(" ", "_").ToLower()}");
        Assert.True(bmp.Width > 100 && bmp.Height > 100,
            $"Screenshot too small for animation {animName}");
    }

    [Theory]
    [InlineData("Fade")]
    [InlineData("Rotate")]
    [InlineData("Scale")]
    [InlineData("Translate")]
    public void AnimationSubPage_Renders(string animName)
    {
        NavigateToAnimationSubPage(animName);
    }

    [Fact]
    public void AnimationsPage_HasSubPages()
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Features");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        AutomationHelper.ClickButton(root, "Animations");
        Thread.Sleep(2000);

        proc = _fixture.GetProcess();
        root = AutomationHelper.GetRoot(proc);
        var allTexts = AutomationHelper.GetAllTextElements(root);

        var expected = new[] { "Fade", "Rotate", "Scale", "Translate" };
        int found = expected.Count(name => allTexts.Any(t => t.Contains(name, StringComparison.OrdinalIgnoreCase)));
        Assert.True(found >= 3, $"Expected animation sub-pages, found {found}. Texts: {string.Join(", ", allTexts.Take(20))}");
    }

    [Fact]
    public void AnimationsPage_Screenshot()
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Features");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        AutomationHelper.ClickButton(root, "Animations");
        Thread.Sleep(2000);

        proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "animations_overview");
        Assert.True(bmp.Width > 100 && bmp.Height > 100, "Animations page screenshot too small");
    }
}
