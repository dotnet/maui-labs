#if WINDOWS_BUILD
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
#endif

namespace Microsoft.Maui.DevFlow.Driver.Windows;

/// <summary>
/// Windows UI Automation helpers using source-generated (NativeAOT-compatible) COM interop.
/// Provides dialog detection, button invocation, and accessibility tree dumping
/// analogous to the Mac AXElement wrapper.
/// </summary>
internal static partial class UIAutomationInterop
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

    private const uint CLSCTX_INPROC_SERVER = 0x1;
    private const uint COINIT_MULTITHREADED = 0x0;

    private static readonly StrategyBasedComWrappers s_comWrappers = new();

    private static IUIAutomation? _automation;

    private static IUIAutomation GetAutomation()
    {
        // StrategyBasedComWrappers (unlike built-in COM interop) does not marshal a cached pointer
        // between apartments, so create the process-wide singleton on a thread-pool thread, which is
        // always in the MTA, no matter which apartment the first caller happens to be in.
        _automation ??= Task.Run(CreateAutomation).GetAwaiter().GetResult();
        return _automation;
    }

    private static IUIAutomation CreateAutomation()
    {
        // Built-in COM interop initialized COM on the calling thread implicitly; CoCreateInstance does not.
        // S_FALSE (already initialized) and RPC_E_CHANGED_MODE (STA thread) are both fine here.
        _ = CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED);

        var clsid = UIAutomationClsids.CUIAutomation;
        var iid = UIAutomationClsids.IUIAutomation;
        Marshal.ThrowExceptionForHR(CoCreateInstance(in clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, in iid, out var unknown));

        try
        {
            return (IUIAutomation)s_comWrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, in Guid riid, out IntPtr ppv);

    // Returns null when the element does not support the pattern (UIA returns S_OK with a null pointer).
    private static T? GetPattern<T>(IUIAutomationElement element, int patternId) where T : class
    {
        var unknown = element.GetCurrentPattern(patternId);
        if (unknown == IntPtr.Zero)
            return null;

        try
        {
            return (T)s_comWrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    public static string? GetName(IUIAutomationElement element)
    {
        try { return element.GetCurrentName(); } catch { return null; }
    }

    public static int GetControlType(IUIAutomationElement element)
    {
        try { return element.GetCurrentControlType(); } catch { return 0; }
    }

    public static string? GetLocalizedControlType(IUIAutomationElement element)
    {
        try { return element.GetCurrentLocalizedControlType(); } catch { return null; }
    }

    public static string? GetAutomationId(IUIAutomationElement element)
    {
        try { return element.GetCurrentAutomationId(); } catch { return null; }
    }

    public static int GetProcessId(IUIAutomationElement element)
    {
        try { return element.GetCurrentProcessId(); } catch { return 0; }
    }

    public static IntPtr GetNativeWindowHandle(IUIAutomationElement element)
    {
        try
        {
            var handle = element.GetCurrentNativeWindowHandle();
            return handle == 0 ? IntPtr.Zero : new IntPtr(handle);
        }
        catch { return IntPtr.Zero; }
    }

    public static UiaRect? GetBoundingRectangle(IUIAutomationElement element)
    {
        try
        {
            var rect = element.GetCurrentBoundingRectangle();
            var width = Math.Max(0, rect.right - rect.left);
            var height = Math.Max(0, rect.bottom - rect.top);
            return new UiaRect(rect.left, rect.top, width, height);
        }
        catch { return null; }
    }

    public static bool IsEnabled(IUIAutomationElement element)
    {
        try { return element.GetCurrentIsEnabled() != 0; } catch { return false; }
    }

    public static bool IsOffscreen(IUIAutomationElement element)
    {
        try { return element.GetCurrentIsOffscreen() != 0; } catch { return false; }
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
            var pattern = GetPattern<IUIAutomationInvokePattern>(element, UIA_InvokePatternId);
            if (pattern is null)
                return false;

            pattern.Invoke();
            return true;
        }
        catch { return false; }
    }

    public static bool SetValue(IUIAutomationElement element, string value)
    {
        try
        {
            var pattern = GetPattern<IUIAutomationValuePattern>(element, UIA_ValuePatternId);
            if (pattern is null || pattern.GetCurrentIsReadOnly() != 0)
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
            var pattern = GetPattern<IUIAutomationScrollItemPattern>(element, UIA_ScrollItemPatternId);
            if (pattern is null)
                return false;

            pattern.ScrollIntoView();
            return true;
        }
        catch { return false; }
    }

    public static bool ScrollElement(IUIAutomationElement element, ScrollAmount horizontalAmount, ScrollAmount verticalAmount)
    {
        try
        {
            var pattern = GetPattern<IUIAutomationScrollPattern>(element, UIA_ScrollPatternId);
            if (pattern is null)
                return false;

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
        var condition = uia.CreatePropertyCondition(UIA_ProcessIdPropertyId, UiaVariant.FromInt32(processId));
        var results = new List<IUIAutomationElement>();

        try
        {
            var array = root.FindAll(TreeScope.TreeScope_Children, condition);
            if (array != null)
                for (int i = 0; i < array.GetLength(); i++)
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
        var condition = uia.CreatePropertyCondition(UIA_ControlTypePropertyId, UiaVariant.FromInt32(UIA_WindowControlTypeId));
        return FindAll(root, TreeScope.TreeScope_Children, condition);
    }

    public static List<(IUIAutomationElement element, string name)> FindButtons(IUIAutomationElement root)
    {
        var uia = GetAutomation();
        var condition = uia.CreatePropertyCondition(UIA_ControlTypePropertyId, UiaVariant.FromInt32(UIA_ButtonControlTypeId));
        var results = new List<(IUIAutomationElement, string)>();

        try
        {
            var array = root.FindAll(TreeScope.TreeScope_Descendants, condition);
            if (array != null)
                for (int i = 0; i < array.GetLength(); i++)
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

    public static List<string> FindTexts(IUIAutomationElement root)
    {
        var uia = GetAutomation();
        var condition = uia.CreatePropertyCondition(UIA_ControlTypePropertyId, UiaVariant.FromInt32(UIA_TextControlTypeId));
        var texts = new List<string>();

        try
        {
            var array = root.FindAll(TreeScope.TreeScope_Descendants, condition);
            if (array != null)
                for (int i = 0; i < array.GetLength(); i++)
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
        var condition = uia.CreatePropertyCondition(UIA_ControlTypePropertyId, UiaVariant.FromInt32(UIA_EditControlTypeId));
        return FindAll(root, TreeScope.TreeScope_Descendants, condition);
    }

    public static List<IUIAutomationElement> FindChildWindows(IUIAutomationElement parent)
    {
        var uia = GetAutomation();
        var condition = uia.CreatePropertyCondition(UIA_ControlTypePropertyId, UiaVariant.FromInt32(UIA_WindowControlTypeId));
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
        var condition = uia.CreatePropertyCondition(UIA_ProcessIdPropertyId, UiaVariant.FromInt32(processId));
        return FindAll(root, TreeScope.TreeScope_Descendants, condition);
    }

    public static string DumpTree(IUIAutomationElement element, int maxDepth = 8)
    {
        var sb = new StringBuilder();
        var walker = GetAutomation().GetRawViewWalker();
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
                for (int i = 0; i < array.GetLength(); i++)
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
