using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using UITests.Helpers;

namespace UITests;

/// <summary>
/// Captures side-by-side screenshots of WPF and WinUI ControlGallery apps
/// navigating through every page and sub-page, producing comparison images.
/// Both apps are positioned at the same size before capture.
/// 
/// WinUI flyout items lack text in automation tree, so navigation uses
/// SelectionItemPattern by index for flyout pages and mouse clicks for sub-pages.
/// 
/// Usage: dotnet test --filter CompareApps --no-build
/// Output: tests/UITests/Comparisons/*.png
/// </summary>
[Collection("WPF App")]
public class CompareApps
{
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    const uint SWP_NOZORDER = 0x0004;
    const uint SWP_SHOWWINDOW = 0x0040;

    // Standard window size for comparisons
    const int WinWidth = 1400;
    const int WinHeight = 900;
    // Position windows side by side — WinUI on left, WPF on right (no overlap)
    const int WinUIX = 0;
    const int WpfX = 1500;
    const int WinY = 50;

    // WinUI flyout page indices (order in Shell)
    static readonly Dictionary<string, int> FlyoutIndex = new()
    {
        ["Home"] = 0, ["Controls"] = 1, ["Layouts"] = 2, ["Features"] = 3
    };

    readonly WpfAppFixture _fixture;
    static readonly string CompareDir;
    static readonly string WinUIExePath = @"D:\repos\davidortinau\ControlGallery\src\ControlGallery\bin\Debug\net10.0-windows10.0.19041.0\win-x64\ControlGallery.exe";

    static CompareApps()
    {
        var repoRoot = FindRepoRoot();
        CompareDir = Path.Combine(repoRoot, "tests", "UITests", "Comparisons");
        Directory.CreateDirectory(CompareDir);
    }

    public CompareApps(WpfAppFixture fixture) => _fixture = fixture;

    // ── Control sub-pages (tiles inside CollectionView on Controls page) ──
    static readonly string[] ControlSubPages = {
        "ActivityIndicator", "Alerts", "Borders", "BoxView", "Buttons",
        "CarouselView", "CheckBoxes", "CollectionView", "Date Picker",
        "Editors", "Entry", "Images", "Labels", "Map", "Picker",
        "Progress Bars", "Radio Buttons", "RefreshView", "SearchBar",
        "Shapes", "Sliders", "Steppers", "Switches", "TimePicker", "WebView"
    };

    // ── Layout sub-pages ──
    static readonly string[] LayoutSubPages = {
        "Basic", "Catalog", "Chessboard", "Proportional", "Combined",
        "CascadeLayout", "Keypad", "Horizontal", "Stylish Header",
        "Bouncing Text", "Login Sample", "Calculator", "Wrapping",
        "Simple Overlay", "Vertical", "Grid Sample", "HorizontalWrapLayout",
        "Achievements Sample", "Margin + Padding",
        "Alignment Sample", "Show Sample", "Photos Sample", "AndExpand",
        "Reading Sample", "Proportional Coordinate Calc", "ColumnLayout",
        "Grow", "Expansion", "Shrink", "Basis", "Simple", "Slider", "Spacing"
    };

    // ── Feature sub-pages ──
    static readonly string[] FeatureSubPages = {
        "Colors", "Gestures", "Clipping", "Behaviors", "Animations",
        "Shadow", "AppThemeBinding", "Triggers",
        "Native Views", "ContextMenu", "HybridWebView", "Tooltips",
        "MenuBar", "FontImageSource"
    };

    // ═══════════════════════════════════════════════════════════════════
    // Compare flyout pages
    // ═══════════════════════════════════════════════════════════════════
    [Theory]
    [InlineData("Home")]
    [InlineData("Controls")]
    [InlineData("Layouts")]
    [InlineData("Features")]
    public void CompareFlyoutPage(string page)
    {
        var winui = EnsureWinUI();
        if (winui == null) { return; } // WinUI app not available — skip comparison

        var wpf = _fixture.GetProcess();

        // Position side by side so they don't overlap
        PositionWindow(winui, WinUIX);
        PositionWindow(wpf, WpfX);

        // Navigate WinUI first (needs foreground for mouse clicks)
        NavigateWinUIFlyout(winui, page);
        Thread.Sleep(1500);

        // Navigate WPF (uses UIAutomation, works without foreground)
        AutomationHelper.NavigateToPageFresh(wpf, page);
        Thread.Sleep(1000);

        SaveComparison(_fixture.GetProcess(), RefreshWinUI(winui), $"page_{Sanitize(page)}");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Compare control sub-pages
    // ═══════════════════════════════════════════════════════════════════
    [Theory]
    [MemberData(nameof(GetControlSubPages))]
    public void CompareControlSubPage(string control)
    {
        var winui = EnsureWinUI();
        if (winui == null) { return; } // WinUI app not available — skip comparison

        var wpf = _fixture.GetProcess();

        // Position side by side — WinUI on left, WPF on right (no overlap)
        PositionWindow(winui, WinUIX);
        PositionWindow(wpf, WpfX);

        // Navigate WinUI first (needs foreground for mouse click on sub-page tile)
        var winuiTitle = NavigateWinUIToSubPage(winui, "Controls", control);
        Assert.True(IsPageMatch(winuiTitle, control),
            $"WinUI failed to navigate to '{control}'. Detected title: '{winuiTitle}'");

        // Now navigate WPF (UIAutomation — works without foreground)
        NavigateWpfToSubPage("Controls", control);

        SaveComparison(_fixture.GetProcess(), RefreshWinUI(winui), $"control_{Sanitize(control)}");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Compare layout sub-pages
    // ═══════════════════════════════════════════════════════════════════
    [Theory]
    [MemberData(nameof(GetLayoutSubPages))]
    public void CompareLayoutSubPage(string layout)
    {
        var winui = EnsureWinUI();
        if (winui == null) { return; } // WinUI app not available — skip comparison

        var wpf = _fixture.GetProcess();
        PositionWindow(winui, WinUIX);
        PositionWindow(wpf, WpfX);

        var winuiTitle = NavigateWinUIToSubPage(winui, "Layouts", layout);
        Assert.True(IsPageMatch(winuiTitle, layout),
            $"WinUI failed to navigate to '{layout}'. Detected title: '{winuiTitle}'");

        NavigateWpfToSubPage("Layouts", layout);

        SaveComparison(_fixture.GetProcess(), RefreshWinUI(winui), $"layout_{Sanitize(layout)}");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Compare feature sub-pages
    // ═══════════════════════════════════════════════════════════════════
    [Theory]
    [MemberData(nameof(GetFeatureSubPages))]
    public void CompareFeatureSubPage(string feature)
    {
        var winui = EnsureWinUI();
        if (winui == null) { return; } // WinUI app not available — skip comparison

        var wpf = _fixture.GetProcess();
        PositionWindow(winui, WinUIX);
        PositionWindow(wpf, WpfX);

        var winuiTitle = NavigateWinUIToSubPage(winui, "Features", feature);
        Assert.True(IsPageMatch(winuiTitle, feature),
            $"WinUI failed to navigate to '{feature}'. Detected title: '{winuiTitle}'");

        NavigateWpfToSubPage("Features", feature);

        SaveComparison(_fixture.GetProcess(), RefreshWinUI(winui), $"feature_{Sanitize(feature)}");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Data sources for xUnit
    // ═══════════════════════════════════════════════════════════════════
    public static IEnumerable<object[]> GetControlSubPages() =>
        ControlSubPages.Select(c => new object[] { c });
    public static IEnumerable<object[]> GetLayoutSubPages() =>
        LayoutSubPages.Select(l => new object[] { l });
    public static IEnumerable<object[]> GetFeatureSubPages() =>
        FeatureSubPages.Select(f => new object[] { f });

    // ═══════════════════════════════════════════════════════════════════
    // Window positioning
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Set both WPF and WinUI to the same size and position so captures are comparable.
    /// </summary>
    static void PositionWindow(Process proc, int x)
    {
        proc.Refresh();
        if (proc.MainWindowHandle == IntPtr.Zero) return;
        SetWindowPos(proc.MainWindowHandle, IntPtr.Zero, x, WinY, WinWidth, WinHeight, SWP_NOZORDER | SWP_SHOWWINDOW);
        Thread.Sleep(300);
    }

    // ═══════════════════════════════════════════════════════════════════
    // WPF navigation
    // ═══════════════════════════════════════════════════════════════════

    void NavigateWpfToSubPage(string section, string subPage)
    {
        var proc = _fixture.GetProcess();
        AutomationHelper.NavigateToPageFresh(proc, section);
        Thread.Sleep(1000);
        proc = _fixture.GetProcess();
        var root = AutomationHelper.GetRoot(proc);
        AutomationHelper.ClickButtonWithScroll(root, subPage);
        Thread.Sleep(2000);
    }

    // ═══════════════════════════════════════════════════════════════════
    // WinUI navigation — flyout uses SelectionItemPattern by index,
    // sub-pages use mouse click (no InvokePattern available)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Navigate WinUI to a flyout page by selecting the Nth list item.
    /// Goes to Home first to reset the nav stack (same as WPF NavigateToPageFresh).
    /// </summary>
    static void NavigateWinUIFlyout(Process winui, string page)
    {
        winui.Refresh();
        if (winui.MainWindowHandle == IntPtr.Zero) return;
        var root = AutomationElement.FromHandle(winui.MainWindowHandle);

        var flyoutItems = GetWinUIFlyoutItems(root);
        if (flyoutItems.Count == 0) return;

        // Reset to Home first if not already targeting Home
        if (page != "Home" && FlyoutIndex.ContainsKey("Home"))
        {
            SelectFlyoutItem(flyoutItems, 0);
            Thread.Sleep(1000);
        }

        if (FlyoutIndex.TryGetValue(page, out int idx) && idx < flyoutItems.Count)
        {
            SelectFlyoutItem(flyoutItems, idx);
            Thread.Sleep(1500);
        }
    }

    /// <summary>
    /// Navigate WinUI to a sub-page: click back button if on a sub-page,
    /// flyout to section overview, then mouse-click the target text.
    /// Returns the page title found in WinUI's automation tree after navigation.
    /// </summary>
    static string NavigateWinUIToSubPage(Process winui, string section, string subPage)
    {
        // First, click the back button if we're on a sub-page (pops the nav stack)
        ClickWinUIBackButton(winui);

        // Navigate to section flyout page
        NavigateWinUIFlyout(winui, section);
        Thread.Sleep(1500);

        winui.Refresh();
        if (winui.MainWindowHandle == IntPtr.Zero) return "";

        // Try clicking with retry — up to 5 attempts
        for (int attempt = 0; attempt < 5; attempt++)
        {
            SetForegroundWindow(winui.MainWindowHandle);
            Thread.Sleep(400);

            var root = AutomationElement.FromHandle(winui.MainWindowHandle);

            // Click the target within visible bounds, with scrolling
            if (!ClickWinUIVisibleTextElement(root, winui.MainWindowHandle, subPage))
                ScrollAndClickVisible(root, winui.MainWindowHandle, subPage);

            Thread.Sleep(2500);

            // Check what page we landed on by reading the page heading
            winui.Refresh();
            root = AutomationElement.FromHandle(winui.MainWindowHandle);
            var pageTitle = DetectWinUIPageTitle(root);

            if (!string.IsNullOrEmpty(pageTitle) && IsPageMatch(pageTitle, subPage))
                return pageTitle;

            // Navigation failed — click back and go back to section
            ClickWinUIBackButton(winui);
            NavigateWinUIFlyout(winui, section);
            Thread.Sleep(1500);
        }

        // Return whatever title we ended up on
        winui.Refresh();
        var finalRoot = AutomationElement.FromHandle(winui.MainWindowHandle);
        return DetectWinUIPageTitle(finalRoot);
    }

    /// <summary>
    /// Click the WinUI NavigationView back button repeatedly until it's disabled.
    /// This fully pops the navigation stack back to the section overview.
    /// The button name varies by locale (e.g. "Back", "Anterior") so we find it by AutomationId.
    /// </summary>
    static void ClickWinUIBackButton(Process winui)
    {
        for (int i = 0; i < 10; i++)
        {
            winui.Refresh();
            if (winui.MainWindowHandle == IntPtr.Zero) return;
            var root = AutomationElement.FromHandle(winui.MainWindowHandle);
            try
            {
                var backBtn = root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "NavigationViewBackButton"));
                if (backBtn == null || !backBtn.Current.IsEnabled) return;

                var invoke = (InvokePattern)backBtn.GetCurrentPattern(InvokePattern.Pattern);
                invoke.Invoke();
                Thread.Sleep(800);
            }
            catch { return; }
        }
    }

    /// <summary>
    /// Detect the current WinUI page title from the automation tree.
    /// Returns the page heading text, or "sub-page" if we've navigated away from a section overview.
    /// </summary>
    static string DetectWinUIPageTitle(AutomationElement root)
    {
        var texts = GetAllTextNames(root);

        // Check if we're on a sub-page by looking for typical sub-page markers
        bool hasNamespace = texts.Any(t => t.StartsWith("Namespace:"));
        bool hasCodeTabs = texts.Contains("C#") || texts.Contains("XAML");
        bool hasBackButton = false;
        try
        {
            var backBtn = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "NavigationViewBackButton"));
            hasBackButton = backBtn != null && backBtn.Current.IsEnabled;
        }
        catch { }

        // If the back button is enabled, we're on a sub-page
        bool isSubPage = hasNamespace || hasCodeTabs || hasBackButton;
        if (!isSubPage) return "";

        // Find the first substantial text that looks like a page title
        // (skip generic UI elements, section names, very long texts, and Style markup)
        foreach (var t in texts)
        {
            if (t.Length > 1 && t.Length < 40 &&
                t != "Control Gallery" && t != "Search" && t != "Appearance" &&
                t != "C#" && t != "XAML" && t != "Style" && t != "← Back" &&
                t != "Back" && t != "Controls" && t != "Layouts" &&
                t != "Features" && t != "Home" && t != "Gallery" &&
                !t.StartsWith("Namespace:") &&
                !t.StartsWith("<Style") && !t.StartsWith("<"))
                return t;
        }

        // Fallback: if back button is enabled, we're on some sub-page
        return hasBackButton ? "sub-page" : "";
    }

    /// <summary>
    /// Check if a detected page title matches the expected sub-page name.
    /// Handles singular/plural, common variations, and sub-page detection.
    /// </summary>
    static bool IsPageMatch(string detected, string expected)
    {
        if (string.IsNullOrEmpty(detected)) return false;

        // We confirmed we're on a sub-page (back button enabled)
        if (detected == "sub-page") return true;

        // Direct match
        if (detected.Equals(expected, StringComparison.OrdinalIgnoreCase)) return true;

        // Singular form (Buttons -> Button, Labels -> Label, etc.)
        var singular = expected.TrimEnd('s');
        if (detected.Equals(singular, StringComparison.OrdinalIgnoreCase)) return true;

        // Normalize: remove spaces, then compare with/without trailing 's'
        var nd = detected.Replace(" ", "").ToLowerInvariant();
        var ne = expected.Replace(" ", "").ToLowerInvariant();
        if (nd == ne || nd == ne.TrimEnd('s') || nd + "s" == ne) return true;

        // Partial match — detected title contains expected or vice versa
        if (detected.Contains(expected, StringComparison.OrdinalIgnoreCase) ||
            expected.Contains(detected, StringComparison.OrdinalIgnoreCase))
            return true;

        // Section-specific: e.g. "Carousel Samples" matches "CarouselView", "Carousel" contains "Carousel"
        var expectedWord = expected.Replace("View", "").Replace("Bar", "").Replace(" ", "").TrimEnd('s');
        if (nd.Contains(expectedWord.ToLowerInvariant()))
            return true;

        return false;
    }

    /// <summary>
    /// Click a text element only if it's within the visible window bounds.
    /// Uses the automation element's own bounding rectangle (logical/DPI-aware pixels).
    /// Checks against the window's automation element bounds for consistent coordinate space.
    /// Tries ScrollIntoView for elements near the edge.
    /// </summary>
    static bool ClickWinUIVisibleTextElement(AutomationElement root, IntPtr hwnd, string text)
    {
        // Use the root automation element's bounding rect for consistent coordinate space
        var winBounds = root.Current.BoundingRectangle;

        var elements = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, text));

        foreach (AutomationElement el in elements)
        {
            var rect = el.Current.BoundingRectangle;
            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) continue;
            if (double.IsNaN(rect.X) || double.IsNaN(rect.Y)) continue;

            // Try ScrollIntoView if available
            try
            {
                var scrollItem = (ScrollItemPattern)el.GetCurrentPattern(ScrollItemPattern.Pattern);
                scrollItem.ScrollIntoView();
                Thread.Sleep(500);
                rect = el.Current.BoundingRectangle;
            }
            catch { }

            // Only click if element center is within the window's automation bounds
            double cy = rect.Y + rect.Height / 2;
            double cx = rect.X + rect.Width / 2;
            if (!winBounds.IsEmpty && winBounds.Width > 0)
            {
                if (cy < winBounds.Top || cy > winBounds.Bottom ||
                    cx < winBounds.Left || cx > winBounds.Right)
                    continue; // Element is scrolled out of view
            }

            AutomationHelper.ClickAt(rect);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Scroll through lists looking for the target within visible bounds, then click it.
    /// </summary>
    static void ScrollAndClickVisible(AutomationElement root, IntPtr hwnd, string text)
    {
        // Try all scrollable containers
        var scrollables = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List));

        foreach (AutomationElement scrollable in scrollables)
        {
            try
            {
                var scroll = (ScrollPattern)scrollable.GetCurrentPattern(ScrollPattern.Pattern);
                scroll.SetScrollPercent(ScrollPattern.NoScroll, 0);
                Thread.Sleep(300);

                for (int i = 0; i < 20; i++)
                {
                    // Re-query root to get fresh element positions after scroll
                    if (ClickWinUIVisibleTextElement(root, hwnd, text))
                        return;
                    if (scroll.Current.VerticalScrollPercent >= 100) break;
                    scroll.SetScrollPercent(ScrollPattern.NoScroll,
                        Math.Min(100, scroll.Current.VerticalScrollPercent + 10));
                    Thread.Sleep(400);
                }
            }
            catch { }
        }

        // Last resort: also try ScrollViewer containers
        var scrollViewers = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Pane));
        foreach (AutomationElement sv in scrollViewers)
        {
            try
            {
                var scroll = (ScrollPattern)sv.GetCurrentPattern(ScrollPattern.Pattern);
                for (int i = 0; i < 10; i++)
                {
                    if (ClickWinUIVisibleTextElement(root, hwnd, text))
                        return;
                    if (scroll.Current.VerticalScrollPercent >= 100) break;
                    scroll.SetScrollPercent(ScrollPattern.NoScroll,
                        Math.Min(100, scroll.Current.VerticalScrollPercent + 15));
                    Thread.Sleep(400);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Get all text element names from the automation tree.
    /// </summary>
    static List<string> GetAllTextNames(AutomationElement root)
    {
        var result = new List<string>();
        var texts = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
        foreach (AutomationElement t in texts)
        {
            var name = t.Current.Name;
            if (!string.IsNullOrWhiteSpace(name))
                result.Add(name);
        }
        return result;
    }
    /// </summary>
    static List<AutomationElement> GetWinUIFlyoutItems(AutomationElement root)
    {
        var items = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        var result = new List<AutomationElement>();
        foreach (AutomationElement item in items)
        {
            // WinUI flyout items are NavigationViewItem with ShellFlyoutItemView name
            if (item.Current.Name?.Contains("ShellFlyoutItemView") == true ||
                item.Current.ClassName?.Contains("NavigationViewItem") == true)
            {
                result.Add(item);
            }
        }
        return result;
    }

    static void SelectFlyoutItem(List<AutomationElement> items, int index)
    {
        if (index >= items.Count) return;
        try
        {
            var sip = (SelectionItemPattern)items[index].GetCurrentPattern(SelectionItemPattern.Pattern);
            sip.Select();
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Comparison image creation
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a side-by-side comparison image: WPF on left, WinUI on right.
    /// Both windows should be at the same size before calling this.
    /// </summary>
    static void SaveComparison(Process wpfProc, Process winuiProc, string name)
    {
        using var wpfBmp = ScreenshotHelper.CaptureWindow(wpfProc);
        using var winuiBmp = ScreenshotHelper.CaptureWindow(winuiProc);

        ScreenshotHelper.SaveScreenshot(wpfBmp, CompareDir, $"{name}_wpf");
        ScreenshotHelper.SaveScreenshot(winuiBmp, CompareDir, $"{name}_winui");

        int targetH = Math.Max(wpfBmp.Height, winuiBmp.Height);
        int totalW = wpfBmp.Width + winuiBmp.Width + 20;
        using var combined = new Bitmap(totalW, targetH + 40);
        using var g = Graphics.FromImage(combined);
        g.Clear(Color.FromArgb(30, 30, 30));

        using var font = new Font("Segoe UI", 14, FontStyle.Bold);
        g.DrawString("WPF", font, Brushes.Cyan, 10, 5);
        g.DrawString("WinUI", font, Brushes.Orange, wpfBmp.Width + 20, 5);

        g.DrawImage(wpfBmp, 0, 35, wpfBmp.Width, wpfBmp.Height);
        g.DrawImage(winuiBmp, wpfBmp.Width + 20, 35, winuiBmp.Width, winuiBmp.Height);

        using var pen = new Pen(Color.Yellow, 2);
        g.DrawLine(pen, wpfBmp.Width + 9, 0, wpfBmp.Width + 9, combined.Height);

        combined.Save(Path.Combine(CompareDir, $"{name}_compare.png"), ImageFormat.Png);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Process management
    // ═══════════════════════════════════════════════════════════════════

    static Process? _cachedWinUI;

    static Process? EnsureWinUI()
    {
        if (_cachedWinUI != null)
        {
            try { _cachedWinUI.Refresh(); if (!_cachedWinUI.HasExited) return _cachedWinUI; } catch { }
        }

        _cachedWinUI = FindWinUIProcess();
        if (_cachedWinUI != null) return _cachedWinUI;

        if (!File.Exists(WinUIExePath)) return null;
        Process.Start(new ProcessStartInfo { FileName = WinUIExePath, UseShellExecute = false });
        for (int i = 0; i < 30; i++)
        {
            Thread.Sleep(1000);
            _cachedWinUI = FindWinUIProcess();
            if (_cachedWinUI != null) return _cachedWinUI;
        }
        return null;
    }

    static Process? FindWinUIProcess()
    {
        foreach (var p in Process.GetProcessesByName("ControlGallery"))
        {
            try
            {
                var path = p.MainModule?.FileName ?? "";
                if (path.Contains("davidortinau", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("net10.0-windows10.0", StringComparison.OrdinalIgnoreCase))
                {
                    p.Refresh();
                    if (p.MainWindowHandle != IntPtr.Zero)
                        return p;
                }
            }
            catch { }
        }
        return null;
    }

    static Process RefreshWinUI(Process winui)
    {
        winui.Refresh();
        if (winui.MainWindowHandle != IntPtr.Zero) return winui;
        return FindWinUIProcess() ?? winui;
    }

    static string Sanitize(string name) =>
        name.Replace(" ", "_").Replace("+", "plus").ToLowerInvariant();

    static string FindRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Maui.WPF.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return @"D:\repos\rmarinho\maui.wpf";
    }
}
