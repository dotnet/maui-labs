using System.Drawing;
using System.Windows.Automation;
using UITests.Helpers;

namespace UITests;

/// <summary>
/// Interaction tests - verify control pages render with expected content.
/// MAUI controls are wrapped in LayoutPanels and may not expose standard
/// UIAutomation control types, so we verify element count and visual content.
/// </summary>
[Collection("WPF App")]
public class ControlInteractionTests
{
    readonly WpfAppFixture _fixture;

    public ControlInteractionTests(WpfAppFixture fixture)
    {
        _fixture = fixture;
    }

    AutomationElement NavigateToControl(string controlName)
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, "Controls");
        Thread.Sleep(1000);
        proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        AutomationHelper.ClickButtonWithScroll(root, controlName);
        Thread.Sleep(2000);
        proc = _fixture.GetProcess();
        return AutomationHelper.GetRoot(proc);
    }

    [Fact]
    public void ButtonPage_HasClickableButtons()
    {
        var root = NavigateToControl("Buttons");
        var buttons = AutomationHelper.FindAll(root, ControlType.Button);
        int invokable = 0;
        foreach (AutomationElement btn in buttons)
        {
            try { btn.GetCurrentPattern(InvokePattern.Pattern); invokable++; } catch { }
        }
        // Flyout has ~5 buttons, page should have more
        Assert.True(invokable >= 7, $"Expected at least 7 invokable buttons, found {invokable}");

        var proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "interaction_buttons");
    }

    [Fact]
    public void EntryPage_HasInputContent()
    {
        var root = NavigateToControl("Entry");
        // Entry page should have visible text elements (labels + entry hints)
        var allTexts = AutomationHelper.GetAllTextElements(root);
        // Filter out flyout items
        var pageTexts = allTexts.Where(t => t != "Home" && t != "Controls" && t != "Layouts" 
            && t != "Features" && t != "Tips & Tricks" && t != "Appearance").ToList();
        Assert.True(pageTexts.Count >= 3, $"Expected page text content, found {pageTexts.Count}: {string.Join(", ", pageTexts.Take(10))}");

        var proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "interaction_entry");
    }

    [Fact]
    public void SliderPage_HasVisualContent()
    {
        var root = NavigateToControl("Sliders");
        var allTexts = AutomationHelper.GetAllTextElements(root);
        // Should have labels showing slider values or descriptions
        Assert.True(allTexts.Count >= 8, $"Expected text elements on Slider page, found {allTexts.Count}");

        var proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "interaction_slider");
        // Verify non-blank content area
        var contentColor = ScreenshotHelper.SampleContentBackground(bmp);
        Assert.NotEqual(Color.Empty, contentColor);
    }

    [Fact]
    public void CheckBoxPage_HasVisualContent()
    {
        var root = NavigateToControl("CheckBoxes");
        var allTexts = AutomationHelper.GetAllTextElements(root);
        Assert.True(allTexts.Count >= 8, $"Expected text on CheckBox page, found {allTexts.Count}");

        var proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "interaction_checkbox");
    }

    [Fact]
    public void PickerPage_HasComboBox()
    {
        var root = NavigateToControl("Picker");
        // At minimum the theme picker ComboBox should exist
        var combos = AutomationHelper.FindAll(root, ControlType.ComboBox);
        Assert.True(combos.Count >= 1, $"Expected at least 1 ComboBox, found {combos.Count}");

        var proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "interaction_picker");
    }

    [Fact]
    public void ProgressBarPage_HasContent()
    {
        var root = NavigateToControl("Progress Bars");
        var allTexts = AutomationHelper.GetAllTextElements(root);
        Assert.True(allTexts.Count >= 8, $"Expected text on ProgressBar page, found {allTexts.Count}");

        var proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "interaction_progressbar");
    }

    [Fact]
    public void LabelPage_HasManyTextElements()
    {
        var root = NavigateToControl("Labels");
        var texts = AutomationHelper.FindAll(root, ControlType.Text);
        Assert.True(texts.Count >= 10, $"Expected many text elements on Labels page, found {texts.Count}");

        var proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "interaction_labels");
    }

    [Fact]
    public void SearchBarPage_HasContent()
    {
        var root = NavigateToControl("SearchBar");
        var allTexts = AutomationHelper.GetAllTextElements(root);
        Assert.True(allTexts.Count >= 7, $"Expected text on SearchBar page, found {allTexts.Count}");

        var proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "interaction_searchbar");
    }

    [Fact]
    public void ImagePage_HasVisualContent()
    {
        var root = NavigateToControl("Images");
        
        var proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "interaction_images");

        // Image page should have text content (labels describing images)
        var allTexts = AutomationHelper.GetAllTextElements(root);
        Assert.True(allTexts.Count >= 7, $"Expected text elements on Images page, found {allTexts.Count}");
        Assert.True(bmp.Width > 100 && bmp.Height > 100, "Images page should render");
    }

    [Fact]
    public void StepperPage_HasButtons()
    {
        var root = NavigateToControl("Steppers");
        var buttons = AutomationHelper.FindAll(root, ControlType.Button);
        // Stepper has +/- buttons plus flyout buttons
        Assert.True(buttons.Count >= 7, $"Expected stepper buttons, found {buttons.Count}");

        var proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "interaction_steppers");
    }

    [Fact]
    public void ShapesPage_HasVisualContent()
    {
        var root = NavigateToControl("Shapes");

        var proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "interaction_shapes");

        // Verify the page rendered (has text elements or visual content)
        var allTexts = AutomationHelper.GetAllTextElements(root);
        Assert.True(bmp.Width > 100 && bmp.Height > 100, "Shapes page should render");
        // Shapes page should at least have flyout text + some page content
        Assert.True(allTexts.Count >= 5, $"Expected text content on Shapes page, found {allTexts.Count}");
    }

    [Fact]
    public void RadioButtonPage_HasContent()
    {
        var root = NavigateToControl("Radio Buttons");
        var allTexts = AutomationHelper.GetAllTextElements(root);
        Assert.True(allTexts.Count >= 8, $"Expected text on RadioButton page, found {allTexts.Count}");

        var proc = _fixture.GetProcess();
        using var bmp = ScreenshotHelper.CaptureWindow(proc);
        ScreenshotHelper.SaveScreenshot(bmp, _fixture.Launcher.ScreenshotDir, "interaction_radiobuttons");
    }
}
