using System.Diagnostics;
using System.Drawing;
using System.Windows.Automation;

namespace UITests.Helpers;

/// <summary>
/// UIAutomation helper for finding and interacting with WPF/WinUI controls.
/// All interactions use UIAutomation patterns (not mouse events) so they work
/// even when the screen is locked or the window is not in the foreground.
/// </summary>
public static class AutomationHelper
{
    /// <summary>
    /// Get the automation root for a process's main window.
    /// </summary>
    public static AutomationElement GetRoot(Process proc)
    {
        proc.Refresh();
        if (proc.MainWindowHandle == IntPtr.Zero)
            throw new InvalidOperationException($"Process {proc.Id} has no main window");
        return AutomationElement.FromHandle(proc.MainWindowHandle);
    }

    /// <summary>
    /// Find all elements matching a control type.
    /// </summary>
    public static AutomationElementCollection FindAll(AutomationElement root, ControlType controlType)
    {
        return root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, controlType));
    }

    /// <summary>
    /// Find elements by name (exact match).
    /// </summary>
    public static AutomationElementCollection FindByName(AutomationElement root, string name)
    {
        return root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, name));
    }

    /// <summary>
    /// Find first element by name and control type.
    /// </summary>
    public static AutomationElement? FindFirst(AutomationElement root, string name, ControlType? controlType = null)
    {
        Condition cond = controlType != null
            ? new AndCondition(
                new PropertyCondition(AutomationElement.NameProperty, name),
                new PropertyCondition(AutomationElement.ControlTypeProperty, controlType))
            : new PropertyCondition(AutomationElement.NameProperty, name);
        return root.FindFirst(TreeScope.Descendants, cond);
    }

    /// <summary>
    /// Click a flyout item by its text label using UIAutomation InvokePattern.
    /// Walks up from the text element to find an invokable parent (Button).
    /// Returns true if found and invoked.
    /// </summary>
    public static bool ClickFlyoutItem(AutomationElement root, string itemName)
    {
        var texts = FindByName(root, itemName);
        foreach (AutomationElement t in texts)
        {
            var rect = t.Current.BoundingRectangle;
            if (rect.IsEmpty || rect.Width <= 0) continue;

            // Walk up to find invokable or selectable parent
            var walker = TreeWalker.ControlViewWalker;
            var current = t;
            for (int depth = 0; depth < 10; depth++)
            {
                current = walker.GetParent(current);
                if (current == null || current == root) break;
                try
                {
                    var invoke = (InvokePattern)current.GetCurrentPattern(InvokePattern.Pattern);
                    invoke.Invoke();
                    return true;
                }
                catch (InvalidOperationException) { }
                // Try SelectionItemPattern (CollectionView ListBoxItems)
                try
                {
                    var select = (SelectionItemPattern)current.GetCurrentPattern(SelectionItemPattern.Pattern);
                    select.Select();
                    return true;
                }
                catch (InvalidOperationException) { }
            }

            // Fallback: try the element itself
            try
            {
                var invoke = (InvokePattern)t.GetCurrentPattern(InvokePattern.Pattern);
                invoke.Invoke();
                return true;
            }
            catch (InvalidOperationException) { }

            // Last resort: mouse click (won't work with locked screen)
            ClickAt(rect);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Click any button by its content text using InvokePattern.
    /// </summary>
    public static bool ClickButton(AutomationElement root, string buttonText)
    {
        // First try finding by Name directly on Button type
        var buttons = root.FindAll(TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.NameProperty, buttonText)));
        foreach (AutomationElement btn in buttons)
        {
            try
            {
                var invoke = (InvokePattern)btn.GetCurrentPattern(InvokePattern.Pattern);
                invoke.Invoke();
                return true;
            }
            catch { }
        }

        // Fallback: find text and walk up to button
        return ClickFlyoutItem(root, buttonText);
    }

    /// <summary>
    /// Select a value in a ComboBox by expanding it and choosing an item.
    /// Works via UIAutomation patterns — no mouse input needed.
    /// </summary>
    public static bool SelectComboBoxItem(AutomationElement root, string itemName)
    {
        var combos = FindAll(root, ControlType.ComboBox);
        foreach (AutomationElement combo in combos)
        {
            try
            {
                var expand = (ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern);
                expand.Expand();
                Thread.Sleep(500);

                var items = FindAll(combo, ControlType.ListItem);
                foreach (AutomationElement item in items)
                {
                    if (item.Current.Name == itemName)
                    {
                        var sel = (SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern);
                        sel.Select();
                        expand.Collapse();
                        return true;
                    }
                }
                expand.Collapse();
            }
            catch { }
        }
        return false;
    }

    /// <summary>
    /// Get all text element names in a bounding region (for verifying content).
    /// </summary>
    public static List<string> GetTextElementsInRegion(AutomationElement root, System.Windows.Rect region)
    {
        var result = new List<string>();
        var texts = FindAll(root, ControlType.Text);
        foreach (AutomationElement t in texts)
        {
            var r = t.Current.BoundingRectangle;
            if (r.IsEmpty) continue;
            if (r.Left >= region.Left && r.Right <= region.Right &&
                r.Top >= region.Top && r.Bottom <= region.Bottom)
            {
                if (!string.IsNullOrWhiteSpace(t.Current.Name))
                    result.Add(t.Current.Name);
            }
        }
        return result;
    }

    /// <summary>
    /// Get all visible text element names from the automation tree.
    /// </summary>
    public static List<string> GetAllTextElements(AutomationElement root)
    {
        var result = new List<string>();
        var texts = FindAll(root, ControlType.Text);
        foreach (AutomationElement t in texts)
        {
            var name = t.Current.Name;
            if (!string.IsNullOrWhiteSpace(name))
                result.Add(name);
        }
        return result;
    }

    /// <summary>
    /// Count elements of a given control type.
    /// </summary>
    public static int CountElements(AutomationElement root, ControlType controlType)
    {
        return FindAll(root, controlType).Count;
    }

    /// <summary>
    /// Check whether an element with the given name exists.
    /// </summary>
    public static bool ElementExists(AutomationElement root, string name)
    {
        return FindByName(root, name).Count > 0;
    }

    /// <summary>
    /// Click at a screen position using mouse_event.
    /// This is a fallback — prefer InvokePattern when possible.
    /// </summary>
    public static void ClickAt(System.Windows.Rect rect)
    {
        var centerX = (int)(rect.X + rect.Width / 2);
        var centerY = (int)(rect.Y + rect.Height / 2);
        System.Windows.Forms.Cursor.Position = new Point(centerX, centerY);
        Thread.Sleep(100);
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        Thread.Sleep(50);
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
    }

    /// <summary>
    /// Wait for a specific text element to appear.
    /// </summary>
    public static bool WaitForText(AutomationElement root, string text, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var elements = FindByName(root, text);
                if (elements.Count > 0) return true;
            }
            catch { }
            Thread.Sleep(500);
        }
        return false;
    }

    /// <summary>
    /// Wait for any of the given text elements to appear.
    /// </summary>
    public static bool WaitForAnyText(AutomationElement root, string[] texts, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                foreach (var text in texts)
                {
                    if (FindByName(root, text).Count > 0) return true;
                }
            }
            catch { }
            Thread.Sleep(500);
        }
        return false;
    }

    /// <summary>
    /// Navigate to a flyout page and wait for content to load.
    /// Returns the refreshed automation root.
    /// </summary>
    public static AutomationElement NavigateToPage(Process proc, string pageName, int waitMs = 2000)
    {
        var root = GetRoot(proc);
        ClickFlyoutItem(root, pageName);
        Thread.Sleep(waitMs);
        // Re-get root after navigation
        proc.Refresh();
        return GetRoot(proc);
    }

    /// <summary>
    /// Navigate to a flyout page with a forced reset via Home first.
    /// This is needed because Shell ignores clicks on the already-selected flyout tab.
    /// If we're on a sub-page pushed from Layouts, clicking Layouts won't pop back.
    /// </summary>
    public static AutomationElement NavigateToPageFresh(Process proc, string pageName, int waitMs = 2000)
    {
        var root = GetRoot(proc);
        // First navigate to Home to clear the stack, then to the target
        if (pageName != "Home")
        {
            ClickFlyoutItem(root, "Home");
            Thread.Sleep(1000);
            proc.Refresh();
            root = GetRoot(proc);
        }
        ClickFlyoutItem(root, pageName);
        Thread.Sleep(waitMs);
        proc.Refresh();
        return GetRoot(proc);
    }

    /// <summary>
    /// Scroll a scrollable element to bring an item into view.
    /// Searches for a ScrollViewer or ListBox, then scrolls down until the target text appears.
    /// </summary>
    public static bool ScrollToElement(AutomationElement root, string targetText, int maxScrolls = 10)
    {
        // Find scrollable containers
        var scrollables = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List));
        
        foreach (AutomationElement scrollable in scrollables)
        {
            try
            {
                var scroll = (ScrollPattern)scrollable.GetCurrentPattern(ScrollPattern.Pattern);
                for (int i = 0; i < maxScrolls; i++)
                {
                    // Check if target is now visible
                    var found = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.NameProperty, targetText));
                    if (found != null && !found.Current.BoundingRectangle.IsEmpty)
                        return true;

                    if (scroll.Current.VerticalScrollPercent >= 100) break;
                    scroll.SetScrollPercent(ScrollPattern.NoScroll, 
                        Math.Min(100, scroll.Current.VerticalScrollPercent + 20));
                    Thread.Sleep(300);
                }
                // Reset scroll position
                scroll.SetScrollPercent(ScrollPattern.NoScroll, 0);
            }
            catch { }
        }
        
        // Also try ScrollViewer
        var scrollViewers = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ClassNameProperty, "ScrollViewer"));
        foreach (AutomationElement sv in scrollViewers)
        {
            try
            {
                var scroll = (ScrollPattern)sv.GetCurrentPattern(ScrollPattern.Pattern);
                for (int i = 0; i < maxScrolls; i++)
                {
                    var found = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.NameProperty, targetText));
                    if (found != null && !found.Current.BoundingRectangle.IsEmpty)
                        return true;

                    if (scroll.Current.VerticalScrollPercent >= 100) break;
                    scroll.SetScrollPercent(ScrollPattern.NoScroll,
                        Math.Min(100, scroll.Current.VerticalScrollPercent + 20));
                    Thread.Sleep(300);
                }
                scroll.SetScrollPercent(ScrollPattern.NoScroll, 0);
            }
            catch { }
        }
        
        return FindByName(root, targetText).Count > 0;
    }

    /// <summary>
    /// Find and click a button, scrolling if necessary to find it.
    /// </summary>
    public static bool ClickButtonWithScroll(AutomationElement root, string buttonText)
    {
        // Try without scrolling first
        if (ClickButton(root, buttonText))
            return true;

        // Scroll to find it, then try again
        if (ScrollToElement(root, buttonText))
            return ClickButton(root, buttonText);
        
        return false;
    }
}

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
}
