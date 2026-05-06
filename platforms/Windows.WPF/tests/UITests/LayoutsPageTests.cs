using System.Drawing;
using UITests.Helpers;

namespace UITests;

/// <summary>
/// Tests for the Layouts page - navigates to layout sub-pages.
/// Uses NavigateToPageFresh to avoid Shell same-tab no-op issue.
/// </summary>
[Collection("WPF App")]
public class LayoutsPageTests
{
    readonly WpfAppFixture _fixture;

    public LayoutsPageTests(WpfAppFixture fixture)
    {
        _fixture = fixture;
    }

    void NavigateToLayoutSubPage(string layoutName)
    {
        // Use Fresh navigation to ensure we're on Layouts (not stuck on sub-page)
        var proc = _fixture.GetProcess();
        var root = AutomationHelper.NavigateToPageFresh(proc, "Layouts");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        root = AutomationHelper.GetRoot(proc);
        bool clicked = AutomationHelper.ClickButton(root, layoutName);
        Assert.True(clicked, $"Could not find layout tile '{layoutName}'");
        Thread.Sleep(2000);

        proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir,
            $"layout_{layoutName.Replace(" ", "_").ToLower()}");
        Assert.True(bmp.Width > 100 && bmp.Height > 100,
            $"Screenshot too small for {layoutName}");
    }

    // AbsoluteLayout samples
    [Theory]
    [InlineData("Bouncing Text")]
    [InlineData("Chessboard")]
    [InlineData("Proportional")]
    [InlineData("Simple Overlay")]
    [InlineData("Stylish Header")]
    // FlexLayout samples
    [InlineData("Achievements Sample")]
    [InlineData("Grid Sample")]
    [InlineData("Login Sample")]
    [InlineData("Catalog")]
    [InlineData("Wrapping")]
    // Grid samples
    [InlineData("Basic")]
    [InlineData("Calculator")]
    [InlineData("Keypad")]
    // StackLayout samples
    [InlineData("Horizontal")]
    [InlineData("Vertical")]
    [InlineData("Combined")]
    // Custom
    [InlineData("HorizontalWrapLayout")]
    [InlineData("CascadeLayout")]
    // Other
    [InlineData("Margin + Padding")]
    public void LayoutSubPage_Renders(string layoutName)
    {
        NavigateToLayoutSubPage(layoutName);
    }

    [Fact]
    public void LayoutsPage_HasSectionHeaders()
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Layouts");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        var allTexts = AutomationHelper.GetAllTextElements(root);

        Assert.Contains("AbsoluteLayout", allTexts);
        Assert.Contains("FlexLayout", allTexts);
        Assert.Contains("Grid", allTexts);
        Assert.True(allTexts.Any(t => t.Contains("StackLayout")), "Missing StackLayout section");
    }

    [Fact]
    public void LayoutsPage_Screenshot()
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Layouts");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "layouts_overview");
        Assert.True(bmp.Width > 100 && bmp.Height > 100, "Layouts page screenshot too small");
    }
}
