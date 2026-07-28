#if WINDOWS_BUILD
using System.Text;
using Interop.UIAutomationClient;
#endif

namespace Microsoft.Maui.DevFlow.Driver.Windows;

/// <summary>
/// Windows UI Automation helpers using the Interop.UIAutomationClient TLB-generated types.
/// Provides dialog detection, button invocation, and accessibility tree dumping
/// analogous to the Mac AXElement wrapper.
/// </summary>
internal static class UIAutomationInterop
{
#if WINDOWS_BUILD
    private const int UIA_InvokePatternId = 10000;
    private const int UIA_ValuePatternId = 10002;
    private const int UIA_ScrollPatternId = 10004;
    private const int UIA_ScrollItemPatternId = 10017;

    private const int UIA_ProcessIdPropertyId = 30002;
    private const int UIA_ControlTypePropertyId = 30003;

    private const int UIA_WindowControlTypeId = 50032;
    private const int UIA_ButtonControlTypeId = 50000;
    private const int UIA_EditControlTypeId = 50004;
    private const int UIA_TextControlTypeId = 50020;

    private static CUIAutomationClass? _automation;

    private static CUIAutomationClass GetAutomation()
    {
        _automation ??= new CUIAutomationClass();
        return _automation;
    }

    public static string? GetName(IUIAutomationElement element)
    {
        try { return element.CurrentName; } catch { return null; }
    }

    public static int GetControlType(IUIAutomationElement element)
    {
        try { return element.CurrentControlType; } catch { return 0; }
    }

    public static string? GetLocalizedControlType(IUIAutomationElement element)
    {
        try { return element.CurrentLocalizedControlType; } catch { return null; }
    }

    public static string? GetAutomationId(IUIAutomationElement element)
    {
        try { return element.CurrentAutomationId; } catch { return null; }
    }

    public static int GetProcessId(IUIAutomationElement element)
    {
        try { return element.CurrentProcessId; } catch { return 0; }
    }

    public static IntPtr GetNativeWindowHandle(IUIAutomationElement element)
    {
        try
        {
            var handle = element.CurrentNativeWindowHandle;
            return handle == 0 ? IntPtr.Zero : new IntPtr(handle);
        }
        catch { return IntPtr.Zero; }
    }

    public static UiaRect? GetBoundingRectangle(IUIAutomationElement element)
    {
        try
        {
            var rect = element.CurrentBoundingRectangle;
            var width = Math.Max(0, rect.right - rect.left);
            var height = Math.Max(0, rect.bottom - rect.top);
            return new UiaRect(rect.left, rect.top, width, height);
        }
        catch { return null; }
    }

    public static bool IsEnabled(IUIAutomationElement element)
    {
        try { return element.CurrentIsEnabled != 0; } catch { return false; }
    }

    public static bool IsOffscreen(IUIAutomationElement element)
    {
        try { return element.CurrentIsOffscreen != 0; } catch { return false; }
    }

    public static bool SetFocus(IUIAutomationElement element)
    {
        try
        {
            element.SetFocus();
            return true;
        }
        catch { return false; }
    }

    public static bool InvokeElement(IUIAutomationElement element)
    {
        try
        {
            var pattern = (IUIAutomationInvokePattern)element.GetCurrentPattern(UIA_InvokePatternId);
            pattern.Invoke();
            return true;
        }
        catch { return false; }
    }

    public static bool SetValue(IUIAutomationElement element, string value)
    {
        try
        {
            var pattern = (IUIAutomationValuePattern)element.GetCurrentPattern(UIA_ValuePatternId);
            if (pattern.CurrentIsReadOnly != 0)
                return false;

            pattern.SetValue(value);
            return true;
        }
        catch { return false; }
    }

    public static bool ScrollIntoView(IUIAutomationElement element)
    {
        try
        {
            var pattern = (IUIAutomationScrollItemPattern)element.GetCurrentPattern(UIA_ScrollItemPatternId);
            pattern.ScrollIntoView();
            return true;
        }
        catch { return false; }
    }

    public static bool ScrollElement(IUIAutomationElement element, ScrollAmount horizontalAmount, ScrollAmount verticalAmount)
    {
        try
        {
            var pattern = (IUIAutomationScrollPattern)element.GetCurrentPattern(UIA_ScrollPatternId);
            pattern.Scroll(horizontalAmount, verticalAmount);
            return true;
        }
        catch { return false; }
    }

    public static IUIAutomationElement? ElementFromHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return null;

        try { return GetAutomation().ElementFromHandle(hwnd); }
        catch { return null; }
    }

    public static List<IUIAutomationElement> FindWindowsByProcessId(int processId)
    {
        var uia = GetAutomation();
        var root = uia.GetRootElement();
        var condition = uia.CreatePropertyCondition(UIA_ProcessIdPropertyId, processId);
        var results = new List<IUIAutomationElement>();

        try
        {
            var array = root.FindAll(TreeScope.TreeScope_Children, condition);
            if (array != null)
                for (int i = 0; i < array.Length; i++)
                    results.Add(array.GetElement(i));
        }
        catch { }

        return results;
    }

    public static List<IUIAutomationElement> FindWindowsByTitle(string title, bool exact = false)
    {
        var results = new List<IUIAutomationElement>();
        if (string.IsNullOrWhiteSpace(title))
            return results;

        foreach (var window in FindTopLevelWindows())
        {
            var name = GetName(window);
            if (name is null)
                continue;

            var matches = exact
                ? name.Equals(title, StringComparison.OrdinalIgnoreCase)
                : name.Contains(title, StringComparison.OrdinalIgnoreCase);
            if (matches)
                results.Add(window);
        }

        return results;
    }

    public static List<IUIAutomationElement> FindTopLevelWindows()
    {
        var uia = GetAutomation();
        var root = uia.GetRootElement();
        var condition = uia.CreatePropertyCondition(UIA_ControlTypePropertyId, UIA_WindowControlTypeId);
        return FindAll(root, TreeScope.TreeScope_Children, condition);
    }

    public static List<(IUIAutomationElement element, string name)> FindButtons(IUIAutomationElement root)
    {
        var uia = GetAutomation();
        var condition = uia.CreatePropertyCondition(UIA_ControlTypePropertyId, UIA_ButtonControlTypeId);
        var results = new List<(IUIAutomationElement, string)>();

        try
        {
            var array = root.FindAll(TreeScope.TreeScope_Descendants, condition);
            if (array != null)
                for (int i = 0; i < array.Length; i++)
                {
                    var el = array.GetElement(i);
                    var name = GetName(el) ?? "";
                    if (name.Length > 0)
                        results.Add((el, name));
                }
        }
        catch { }

        return results;
    }

    public static List<(IUIAutomationElement element, string name)> FindNamedButtons(IUIAutomationElement root, IReadOnlySet<string> names)
    {
        var buttons = FindButtons(root);
        if (names.Count == 0)
            return buttons;

        return buttons
            .Where(b => names.Contains(NormalizeLabel(b.name)))
            .ToList();
    }

    public static List<string> FindTexts(IUIAutomationElement root)
    {
        var uia = GetAutomation();
        var condition = uia.CreatePropertyCondition(UIA_ControlTypePropertyId, UIA_TextControlTypeId);
        var texts = new List<string>();

        try
        {
            var array = root.FindAll(TreeScope.TreeScope_Descendants, condition);
            if (array != null)
                for (int i = 0; i < array.Length; i++)
                {
                    var name = GetName(array.GetElement(i)) ?? "";
                    if (name.Length > 0)
                        texts.Add(name);
                }
        }
        catch { }

        return texts;
    }

    public static List<IUIAutomationElement> FindEdits(IUIAutomationElement root)
    {
        var uia = GetAutomation();
        var condition = uia.CreatePropertyCondition(UIA_ControlTypePropertyId, UIA_EditControlTypeId);
        return FindAll(root, TreeScope.TreeScope_Descendants, condition);
    }

    public static List<IUIAutomationElement> FindChildWindows(IUIAutomationElement parent)
    {
        var uia = GetAutomation();
        var condition = uia.CreatePropertyCondition(UIA_ControlTypePropertyId, UIA_WindowControlTypeId);
        return FindAll(parent, TreeScope.TreeScope_Descendants, condition);
    }

    public static IUIAutomationElement? FindFirstByAutomationIdOrName(IEnumerable<IUIAutomationElement> roots, string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName))
            return null;

        foreach (var root in roots)
        {
            if (MatchesIdOrName(root, idOrName))
                return root;

            foreach (var element in FindAll(root, TreeScope.TreeScope_Descendants, GetAutomation().CreateTrueCondition()))
            {
                if (MatchesIdOrName(element, idOrName))
                    return element;
            }
        }

        return null;
    }

    public static List<IUIAutomationElement> FindAllByProcessId(int processId)
    {
        var uia = GetAutomation();
        var root = uia.GetRootElement();
        var condition = uia.CreatePropertyCondition(UIA_ProcessIdPropertyId, processId);
        return FindAll(root, TreeScope.TreeScope_Descendants, condition);
    }

    public static string DumpTree(IUIAutomationElement element, int maxDepth = 8)
    {
        var sb = new StringBuilder();
        var walker = GetAutomation().RawViewWalker;
        DumpTreeRecursive(walker, element, sb, 0, maxDepth);
        return sb.ToString();
    }

    private static void DumpTreeRecursive(IUIAutomationTreeWalker walker, IUIAutomationElement element, StringBuilder sb, int depth, int maxDepth)
    {
        if (depth >= maxDepth) return;

        try
        {
            var indent = new string(' ', depth * 2);
            var controlType = GetLocalizedControlType(element) ?? "?";
            var name = GetName(element) ?? "";
            var automationId = GetAutomationId(element) ?? "";
            var bounds = GetBoundingRectangle(element);

            sb.Append(indent).Append(controlType);
            if (name.Length > 0) sb.Append($" \"{name}\"");
            if (automationId.Length > 0) sb.Append($" [{automationId}]");
            if (bounds is not null) sb.Append($" ({bounds.Value.X},{bounds.Value.Y},{bounds.Value.Width}x{bounds.Value.Height})");
            sb.AppendLine();

            var child = walker.GetFirstChildElement(element);
            while (child != null)
            {
                DumpTreeRecursive(walker, child, sb, depth + 1, maxDepth);
                child = walker.GetNextSiblingElement(child);
            }
        }
        catch { }
    }

    private static List<IUIAutomationElement> FindAll(IUIAutomationElement root, TreeScope scope, IUIAutomationCondition condition)
    {
        var results = new List<IUIAutomationElement>();

        try
        {
            var array = root.FindAll(scope, condition);
            if (array != null)
                for (int i = 0; i < array.Length; i++)
                    results.Add(array.GetElement(i));
        }
        catch { }

        return results;
    }

    private static bool MatchesIdOrName(IUIAutomationElement element, string idOrName)
    {
        var automationId = GetAutomationId(element);
        if (automationId?.Equals(idOrName, StringComparison.OrdinalIgnoreCase) == true)
            return true;

        var name = GetName(element);
        return name?.Equals(idOrName, StringComparison.OrdinalIgnoreCase) == true;
    }

    public static string NormalizeLabel(string label)
        => label
            .Trim()
            .Replace('\u2019', '\'')
            .Replace("&", string.Empty)
            .Replace("_", string.Empty)
            .ToUpperInvariant();

    public readonly record struct UiaRect(double X, double Y, double Width, double Height);
#endif
}
