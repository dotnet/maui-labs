#if WINDOWS
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Agent.Windows;

/// <summary>
/// Discovers native Windows UI Automation elements that are not reliably represented
/// in the MAUI visual tree, including modal dialogs and dialog-like popups.
/// </summary>
public sealed class NativeWindowProbe
{
    private const int DefaultMaxDepth = 10;
    private const int MaxNodesPerWindow = 256;
    // Bounds the descendant scan during dialog discovery to avoid runaway UIA tree
    // walks on large WinUI apps (where TreeScope.Descendants can return thousands of
    // nodes via cross-process COM marshaling).
    private const int MaxDialogScanNodes = 512;
    private const int MaxDialogScanDepth = 8;
    private static readonly int CurrentProcessId = Environment.ProcessId;

    private static readonly HashSet<string> CommonDialogButtonLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "OK", "CANCEL", "YES", "NO", "CLOSE", "DISMISS", "RETRY", "ABORT", "IGNORE", "CONTINUE", "ALLOW", "DON'T ALLOW", "DELETE", "KEEP"
    };

    public void AppendNativeWindows(
        List<ElementInfo> roots,
        Dictionary<string, object> nativeObjects,
        IEnumerable<IntPtr> knownHwnds,
        int? maxDepth = null)
    {
        var known = knownHwnds.Where(h => h != IntPtr.Zero).Distinct().ToArray();
        AppendKnownWindowDialogSubtrees(roots, nativeObjects, known, maxDepth);
        AppendForeignTopLevelWindows(roots, nativeObjects, known, maxDepth);
    }

    public void AppendForeignTopLevelWindows(
        List<ElementInfo> roots,
        Dictionary<string, object> nativeObjects,
        IEnumerable<IntPtr> knownHwnds,
        int? maxDepth = null)
    {
        var depth = maxDepth is > 0 ? maxDepth.Value : DefaultMaxDepth;
        var known = new HashSet<long>(knownHwnds.Select(h => h.ToInt64()));
        IReadOnlyList<AutomationElement> windows;
        try
        {
            windows = EnumerateProcessTopLevels();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ElementNotAvailableException)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] NativeWindowProbe enumeration failed: {ex.Message}");
            return;
        }

        var rootIndex = roots.Count;
        foreach (var window in windows)
        {
            IntPtr hwnd;
            try
            {
                // Zero-extend the int handle: AutomationElement.NativeWindowHandle is a
                // signed 32-bit value, but valid HWNDs above 0x7FFFFFFF are negative
                // when reinterpreted as int. new IntPtr(int) sign-extends, producing
                // 0xFFFFFFFF_AABB0001 instead of 0x00000000_AABB0001 on 64-bit, so
                // .ToInt64() comparisons against the MAUI-supplied handles miss.
                hwnd = new IntPtr(unchecked((long)(uint)window.Current.NativeWindowHandle));
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }

            if (hwnd == IntPtr.Zero || known.Contains(hwnd.ToInt64()))
                continue;

            var prefix = $"native:hwnd:0x{hwnd.ToInt64():X}";
            var info = WalkAutomationElement(window, prefix, [rootIndex++], nativeObjects, 0, depth, isRoot: true);
            if (info is null)
                continue;

            info.Traits ??= [];
            if (!info.Traits.Contains("dialog"))
                info.Traits.Add("dialog");
            roots.Add(info);
        }
    }

    public static AutomationElement? TryGetAutomationElement(IReadOnlyDictionary<string, object> nativeObjects, string id)
        => nativeObjects.TryGetValue(id, out var native) && native is AutomationElement element ? element : null;

    /// <summary>
    /// Parses HWND seeds out of a DevFlow native element id of the form
    /// <c>native:hwnd:0x{HEX}[:dialog:{N}...]</c>. Returns an empty array when the
    /// id doesn't carry an embedded HWND. Used to keep ID generation stable across
    /// cache-miss re-walks (without a seed, <c>AppendKnownWindowDialogSubtrees</c>
    /// would never run and dialog-scoped ids would never be regenerated).
    /// </summary>
    public static IReadOnlyList<IntPtr> ExtractHwndsFromId(string id)
    {
        if (string.IsNullOrEmpty(id))
            return Array.Empty<IntPtr>();

        const string prefix = "native:hwnd:0x";
        var start = id.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
            return Array.Empty<IntPtr>();

        var hexStart = start + prefix.Length;
        var hexEnd = hexStart;
        while (hexEnd < id.Length && IsHexDigit(id[hexEnd]))
            hexEnd++;

        if (hexEnd == hexStart)
            return Array.Empty<IntPtr>();

        var hex = id.AsSpan(hexStart, hexEnd - hexStart);
        if (!long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var hwndValue))
            return Array.Empty<IntPtr>();

        return new[] { new IntPtr(hwndValue) };

        static bool IsHexDigit(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }

    /// <summary>
    /// Rebuilds an <see cref="ElementInfo"/> for a previously-cached <see cref="AutomationElement"/>
    /// without performing a fresh process-wide window enumeration. Returns <c>null</c> when the cached
    /// element is no longer available (e.g. dialog closed).
    /// </summary>
    public static ElementInfo? TryBuildCachedElementInfo(
        IReadOnlyDictionary<string, object> nativeObjects,
        string id,
        int? maxDepth = null)
    {
        if (TryGetAutomationElement(nativeObjects, id) is not { } element)
            return null;

        var depth = maxDepth is > 0 ? maxDepth.Value : DefaultMaxDepth;
        // Build a throwaway nativeObjects map so child walks don't leak into the
        // caller's cache. The returned ElementInfo's root id is rewritten to match
        // the supplied id so it round-trips with the request that produced it.
        var scratch = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var info = WalkAutomationElement(element, id, [0], scratch, 0, depth, isRoot: true);
        if (info is not null)
            info.Id = id;
        return info;
    }

    public static bool TryInvoke(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePattern) && invokePattern is InvokePattern invoke)
            {
                invoke.Invoke();
                return true;
            }

            if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var togglePattern) && togglePattern is TogglePattern toggle)
            {
                toggle.Toggle();
                return true;
            }

            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectionPattern) && selectionPattern is SelectionItemPattern selection)
            {
                selection.Select();
                return true;
            }

            if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expandPattern) && expandPattern is ExpandCollapsePattern expand)
            {
                if (expand.Current.ExpandCollapseState == ExpandCollapseState.Collapsed)
                    expand.Expand();
                else
                    expand.Collapse();
                return true;
            }
        }
        catch (Exception ex) when (ex is ElementNotEnabledException or ElementNotAvailableException or InvalidOperationException or COMException)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] NativeWindowProbe.TryInvoke failed: {ex.Message}");
        }

        return false;
    }

    public static bool TrySetValue(AutomationElement element, string value)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) &&
                pattern is ValuePattern valuePattern &&
                !valuePattern.Current.IsReadOnly)
            {
                valuePattern.SetValue(value);
                return true;
            }
        }
        catch (Exception ex) when (ex is ElementNotEnabledException or ElementNotAvailableException or InvalidOperationException or COMException)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] NativeWindowProbe.TrySetValue failed: {ex.Message}");
        }

        return false;
    }

    public static bool TryFocus(AutomationElement element)
    {
        try
        {
            element.SetFocus();
            return true;
        }
        catch (Exception ex) when (ex is ElementNotEnabledException or ElementNotAvailableException or InvalidOperationException or COMException)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] NativeWindowProbe.TryFocus failed: {ex.Message}");
        }

        return false;
    }

    public static bool TryScroll(AutomationElement element, double deltaX, double deltaY)
    {
        try
        {
            if ((deltaX != 0 || deltaY != 0) &&
                element.TryGetCurrentPattern(ScrollPattern.Pattern, out var scrollPattern) &&
                scrollPattern is ScrollPattern scroll)
            {
                scroll.Scroll(ToScrollAmount(deltaX), ToScrollAmount(deltaY));
                return true;
            }

            if (element.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var itemPattern) &&
                itemPattern is ScrollItemPattern item)
            {
                item.ScrollIntoView();
                return true;
            }
        }
        catch (Exception ex) when (ex is ElementNotEnabledException or ElementNotAvailableException or InvalidOperationException or COMException)
        {
            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] NativeWindowProbe.TryScroll failed: {ex.Message}");
        }

        return false;
    }

    private static ScrollAmount ToScrollAmount(double delta)
    {
        if (delta > 0)
            return ScrollAmount.LargeIncrement;
        if (delta < 0)
            return ScrollAmount.LargeDecrement;
        return ScrollAmount.NoAmount;
    }

    private void AppendKnownWindowDialogSubtrees(
        List<ElementInfo> roots,
        Dictionary<string, object> nativeObjects,
        IReadOnlyList<IntPtr> knownHwnds,
        int? maxDepth)
    {
        var depth = maxDepth is > 0 ? maxDepth.Value : DefaultMaxDepth;
        var rootIndex = roots.Count;

        foreach (var hwnd in knownHwnds)
        {
            AutomationElement? root;
            try
            {
                root = AutomationElement.FromHandle(hwnd);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException or ArgumentException)
            {
                continue;
            }

            if (root is null)
                continue;

            var dialogIndex = 0;
            foreach (var candidate in FindDialogCandidates(root, hwnd))
            {
                var prefix = $"native:hwnd:0x{hwnd.ToInt64():X}:dialog:{dialogIndex++}";
                var info = WalkAutomationElement(candidate, prefix, [rootIndex++], nativeObjects, 0, depth, isRoot: true);
                if (info is null)
                    continue;

                info.Traits ??= [];
                if (!info.Traits.Contains("dialog"))
                    info.Traits.Add("dialog");
                roots.Add(info);
            }
        }
    }

    private static IReadOnlyList<AutomationElement> FindDialogCandidates(AutomationElement root, IntPtr rootHwnd)
    {
        // Walk the subtree breadth-first via TreeScope.Children rather than calling
        // FindAll(TreeScope.Descendants) which eagerly materializes the entire UIA
        // subtree (potentially thousands of cross-process COM marshalled nodes).
        // We cap both total nodes visited and depth to keep dialog discovery bounded.
        var candidates = new List<AutomationElement>();
        var queue = new Queue<(AutomationElement Element, int Depth)>();
        queue.Enqueue((root, 0));
        var scanned = 0;

        while (queue.Count > 0 && scanned < MaxDialogScanNodes)
        {
            var (current, depth) = queue.Dequeue();
            scanned++;

            // Skip the root window itself - only its descendants are dialog candidates.
            if (current != root && IsDialogCandidate(current, rootHwnd))
            {
                // Once we've identified a dialog candidate we don't need to keep
                // descending into its subtree.
                candidates.Add(current);
                continue;
            }

            if (depth >= MaxDialogScanDepth)
                continue;

            AutomationElementCollection children;
            try
            {
                children = current.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
            {
                continue;
            }

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child is not null)
                    queue.Enqueue((child, depth + 1));
            }
        }

        return candidates;
    }

    private static bool IsDialogCandidate(AutomationElement element, IntPtr rootHwnd)
    {
        AutomationElement.AutomationElementInformation current;
        try
        {
            current = element.Current;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }

        // Zero-extend the int NativeWindowHandle the same way EnumerateProcessTopLevels
        // does, so this comparison matches valid HWNDs above 0x7FFFFFFF on 64-bit.
        var nativeHandle = unchecked((long)(uint)current.NativeWindowHandle);
        if (nativeHandle != 0 && nativeHandle == rootHwnd.ToInt64())
            return false;

        if (TryGetIsModal(element) == true)
            return true;

        var className = current.ClassName ?? string.Empty;
        var localizedType = current.ControlType?.LocalizedControlType ?? string.Empty;
        var name = current.Name ?? string.Empty;
        var looksDialogLike =
            className.Contains("Dialog", StringComparison.OrdinalIgnoreCase) ||
            localizedType.Contains("dialog", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("dialog", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("alert", StringComparison.OrdinalIgnoreCase);

        return looksDialogLike && HasCommonDialogButton(element);
    }

    private static bool IsAncestor(AutomationElement ancestor, AutomationElement descendant)
    {
        var walker = TreeWalker.RawViewWalker;
        AutomationElement? current;
        try
        {
            current = walker.GetParent(descendant);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }

        while (current is not null)
        {
            if (SameElement(current, ancestor))
                return true;

            try
            {
                current = walker.GetParent(current);
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool SameElement(AutomationElement first, AutomationElement second)
    {
        try
        {
            return first.Equals(second) || first.GetRuntimeId().SequenceEqual(second.GetRuntimeId());
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool HasCommonDialogButton(AutomationElement root)
    {
        try
        {
            var buttons = root.FindAll(
                TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            for (var i = 0; i < buttons.Count; i++)
            {
                var name = buttons[i]?.Current.Name;
                if (!string.IsNullOrWhiteSpace(name) &&
                    CommonDialogButtonLabels.Contains(NormalizeLabel(name)))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
        {
        }

        return false;
    }

    private static IReadOnlyList<AutomationElement> EnumerateProcessTopLevels()
    {
        var hwnds = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            GetWindowThreadProcessId(hwnd, out var pid);
            if ((int)pid == CurrentProcessId)
                hwnds.Add(hwnd);

            return true;
        }, IntPtr.Zero);

        var result = new List<AutomationElement>(hwnds.Count);
        foreach (var hwnd in hwnds)
        {
            AutomationElement? element;
            try
            {
                element = AutomationElement.FromHandle(hwnd);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException or ArgumentException)
            {
                continue;
            }

            if (element is not null)
                result.Add(element);
        }

        return result;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    private static ElementInfo? WalkAutomationElement(
        AutomationElement element,
        string prefix,
        IReadOnlyList<int> path,
        Dictionary<string, object> nativeObjects,
        int depth,
        int maxDepth,
        bool isRoot)
    {
        if (depth > maxDepth)
            return null;

        AutomationElement.AutomationElementInformation current;
        try
        {
            current = element.Current;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }

        var id = BuildId(current, prefix, path);
        // Sanitize() collapses non-alphanumerics to '_', so siblings with names that
        // sanitize identically (e.g. "Don't Allow" and "Don_t Allow") would otherwise
        // overwrite each other in nativeObjects and a later action-by-id would
        // invoke the wrong element. Disambiguate by appending the tree path.
        if (nativeObjects.ContainsKey(id))
            id = $"{id}:path:{string.Join(".", path)}";
        nativeObjects[id] = element;
        var info = Map(element, current, id, isRoot);

        AutomationElementCollection children;
        try
        {
            children = element.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
        {
            return info;
        }

        var index = 0;
        for (var i = 0; i < children.Count; i++)
        {
            if (info.Children?.Count >= MaxNodesPerWindow)
                break;

            var child = children[i];
            if (child is null)
                continue;

            var childInfo = WalkAutomationElement(child, prefix, [.. path, index++], nativeObjects, depth + 1, maxDepth, isRoot: false);
            if (childInfo is not null)
            {
                info.Children ??= [];
                info.Children.Add(childInfo);
            }
        }

        return info;
    }

    private static ElementInfo Map(AutomationElement element, AutomationElement.AutomationElementInformation current, string id, bool isRoot)
    {
        var controlType = current.ControlType;
        var type = controlType?.LocalizedControlType ?? "element";
        var traits = new List<string>();
        if (isRoot)
            traits.Add("dialog");
        if (HasActionPattern(element))
        {
            traits.Add("actionable");
            traits.Add("interactive");
        }

        if (CanFocus(current, element))
            traits.Add("focusable");
        if (CanScroll(element))
            traits.Add("scrollable");

        var properties = new Dictionary<string, string?>
        {
            ["controlType"] = controlType?.ProgrammaticName,
            ["className"] = string.IsNullOrWhiteSpace(current.ClassName) ? null : current.ClassName,
            ["nativeWindowHandle"] = current.NativeWindowHandle == 0 ? null : $"0x{current.NativeWindowHandle:X}",
            ["processId"] = current.ProcessId.ToString(),
            ["isModal"] = TryGetIsModal(element)?.ToString(),
            ["framework"] = string.IsNullOrWhiteSpace(current.FrameworkId) ? null : current.FrameworkId,
            ["isOffscreen"] = current.IsOffscreen.ToString(),
            ["hasKeyboardFocus"] = current.HasKeyboardFocus.ToString()
        };

        return new ElementInfo
        {
            Id = id,
            Framework = "windows-native",
            Type = NormalizeType(type),
            FullType = controlType?.ProgrammaticName ?? string.Empty,
            AutomationId = string.IsNullOrWhiteSpace(current.AutomationId) ? null : current.AutomationId,
            Text = string.IsNullOrWhiteSpace(current.Name) ? null : current.Name,
            Value = TryGetValue(element),
            Role = controlType?.LocalizedControlType,
            Traits = traits.Count > 0 ? traits : null,
            Bounds = MapBounds(current.BoundingRectangle),
            WindowBounds = MapBounds(current.BoundingRectangle),
            IsVisible = !current.IsOffscreen,
            IsEnabled = current.IsEnabled,
            IsFocused = current.HasKeyboardFocus,
            NativeType = controlType?.ProgrammaticName,
            NativeProperties = properties
        };
    }

    private static bool CanFocus(AutomationElement.AutomationElementInformation current, AutomationElement element)
    {
        if (current.IsKeyboardFocusable)
            return true;

        return HasActionPattern(element) || TryGetValue(element) is not null;
    }

    private static BoundsInfo? MapBounds(System.Windows.Rect rect)
    {
        if (rect.IsEmpty || double.IsInfinity(rect.Width) || double.IsInfinity(rect.Height))
            return null;

        return new BoundsInfo
        {
            X = rect.X,
            Y = rect.Y,
            Width = Math.Max(0, rect.Width),
            Height = Math.Max(0, rect.Height)
        };
    }

    private static bool? TryGetIsModal(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(WindowPattern.Pattern, out var pattern) && pattern is WindowPattern windowPattern)
                return windowPattern.Current.IsModal;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
        }

        return null;
    }

    private static string? TryGetValue(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern valuePattern)
                return valuePattern.Current.Value;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
        }

        return null;
    }

    private static bool HasActionPattern(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(InvokePattern.Pattern, out _) ||
                   element.TryGetCurrentPattern(TogglePattern.Pattern, out _) ||
                   element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _) ||
                   element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return false;
        }
    }

    private static bool CanScroll(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(ScrollPattern.Pattern, out _) ||
                   element.TryGetCurrentPattern(ScrollItemPattern.Pattern, out _);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return false;
        }
    }

    private static string BuildId(AutomationElement.AutomationElementInformation current, string prefix, IReadOnlyList<int> path)
    {
        var stable = !string.IsNullOrWhiteSpace(current.AutomationId)
            ? $"automation:{Sanitize(current.AutomationId)}"
            : !string.IsNullOrWhiteSpace(current.Name)
                ? $"name:{Sanitize(current.Name)}"
                : $"path:{string.Join(".", path)}";
        return $"{prefix}:{stable}";
    }

    private static string Sanitize(string value)
        => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or ':' ? ch : '_'));

    private static string NormalizeType(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Element";

        return string.Concat(raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string NormalizeLabel(string label)
        => label
            .Trim()
            .Replace('\u2019', '\'')
            .Replace("&", string.Empty)
            .Replace("_", string.Empty)
            .ToUpperInvariant();
}
#endif
