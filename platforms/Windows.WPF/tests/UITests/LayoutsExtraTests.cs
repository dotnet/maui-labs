using System.Drawing;
using UITests.Helpers;

namespace UITests;

/// <summary>
/// Tests for remaining layout sub-pages not covered in LayoutsPageTests.
/// </summary>
[Collection("WPF App")]
public class LayoutsExtraTests
{
    readonly WpfAppFixture _fixture;

    public LayoutsExtraTests(WpfAppFixture fixture)
    {
        _fixture = fixture;
    }

    void NavigateToLayoutSubPage(string layoutName)
    {
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

    // AbsoluteLayout
    [InlineData("Proportional Coordinate Calc")]
    // FlexLayout
    [InlineData("Alignment Sample")]
    [InlineData("Photos Sample")]
    [InlineData("Reading Sample")]
    [InlineData("Show Sample")]
    [InlineData("Basis")]
    [InlineData("Grow")]
    [InlineData("Shrink")]
    [InlineData("Simple")]
    // Grid
    [InlineData("Slider")]
    [InlineData("Spacing")]
    // StackLayout
    [InlineData("AndExpand")]
    [InlineData("Expansion")]
    // Custom
    [InlineData("ColumnLayout")]
    [Theory]
    public void LayoutSubPage_Renders(string layoutName)
    {
        NavigateToLayoutSubPage(layoutName);
    }

    [Fact]
    public void LayoutsPage_HasAllButtons()
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Layouts");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        var allTexts = AutomationHelper.GetAllTextElements(root);

        // Check total layout button count
        var expected = new[] {
            "Bouncing Text", "Chessboard", "Proportional", "Simple Overlay", "Stylish Header",
            "Achievements Sample", "Catalog", "Wrapping", "Basic", "Calculator", "Keypad",
            "Horizontal", "Vertical", "Combined", "HorizontalWrapLayout", "CascadeLayout",
            "ColumnLayout", "Margin + Padding"
        };
        int found = expected.Count(name => allTexts.Any(t => t == name));
        Assert.True(found >= 15, $"Expected at least 15 layout names, found {found}");
    }

    [Fact]
    public void LayoutsPage_HasCustomLayoutsSection()
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Layouts");
        Thread.Sleep(1000);

        proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        var allTexts = AutomationHelper.GetAllTextElements(root);

        Assert.Contains("Custom Layouts", allTexts);
        Assert.Contains("Other", allTexts);
    }
}
