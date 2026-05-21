using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Windows;

namespace Microsoft.Maui.DevFlow.Agent.WPF;

/// <summary>
/// WPF-specific visual tree walker that provides native WPF element info.
/// </summary>
public class WpfVisualTreeWalker : VisualTreeWalker
{
    private readonly NativeWindowProbe _nativeProbe = new();
    private readonly object _nativeObjectsLock = new();
    private Dictionary<string, object> _nativeObjects = new(StringComparer.OrdinalIgnoreCase);

    protected override BoundsInfo? ResolveWindowBounds(VisualElement ve)
    {
        try
        {
            if (ve.Handler?.PlatformView is not System.Windows.UIElement element)
                return null;

            var window = System.Windows.Window.GetWindow(element);
            if (window == null) return null;

            var pt = element.TranslatePoint(new System.Windows.Point(0, 0), window);
            var size = element.RenderSize;
            return new BoundsInfo
            {
                X = pt.X,
                Y = pt.Y,
                Width = size.Width,
                Height = size.Height
            };
        }
        catch { }
        return null;
    }

    protected override void PopulateNativeInfo(ElementInfo info, VisualElement ve)
    {
        try
        {
            var platformView = ve.Handler?.PlatformView;
            if (platformView == null) return;

            info.NativeType = platformView.GetType().FullName;

            var props = new Dictionary<string, string?>();

            if (platformView is FrameworkElement fe)
            {
                if (!string.IsNullOrEmpty(fe.Name))
                    props["name"] = fe.Name;

                props["isVisible"] = (fe.Visibility == System.Windows.Visibility.Visible).ToString();
                props["isEnabled"] = fe.IsEnabled.ToString();
                props["actualWidth"] = fe.ActualWidth.ToString();
                props["actualHeight"] = fe.ActualHeight.ToString();
                props["controlType"] = fe.GetType().Name;

                if (fe.ToolTip is string tt && !string.IsNullOrEmpty(tt))
                    props["tooltip"] = tt;
            }

            if (platformView is System.Windows.Controls.Button button)
            {
                if (button.Content is string btnText)
                    props["content"] = btnText;
            }
            else if (platformView is System.Windows.Controls.CheckBox checkBox)
            {
                props["isChecked"] = (checkBox.IsChecked?.ToString() ?? "null");
                if (checkBox.Content is string cbText)
                    props["content"] = cbText;
            }
            else if (platformView is System.Windows.Controls.TextBox textBox)
            {
                props["text"] = textBox.Text;
                props["isReadOnly"] = textBox.IsReadOnly.ToString();
            }
            else if (platformView is System.Windows.Controls.PasswordBox passBox)
            {
                props["hasPassword"] = (!string.IsNullOrEmpty(passBox.Password)).ToString();
            }
            else if (platformView is System.Windows.Controls.TextBlock textBlock)
            {
                props["text"] = textBlock.Text;
            }
            else if (platformView is ToggleButton toggle)
            {
                props["isChecked"] = (toggle.IsChecked?.ToString() ?? "null");
            }
            else if (platformView is System.Windows.Controls.Slider slider)
            {
                props["value"] = slider.Value.ToString();
                props["minimum"] = slider.Minimum.ToString();
                props["maximum"] = slider.Maximum.ToString();
            }
            else if (platformView is System.Windows.Controls.ProgressBar progressBar)
            {
                props["value"] = progressBar.Value.ToString();
                props["isIndeterminate"] = progressBar.IsIndeterminate.ToString();
            }
            else if (platformView is System.Windows.Controls.ComboBox comboBox)
            {
                props["selectedIndex"] = comboBox.SelectedIndex.ToString();
                if (comboBox.SelectedItem != null)
                    props["selectedItem"] = comboBox.SelectedItem.ToString();
            }
            else if (platformView is System.Windows.Controls.ScrollViewer scroll)
            {
                props["horizontalOffset"] = scroll.HorizontalOffset.ToString();
                props["verticalOffset"] = scroll.VerticalOffset.ToString();
                props["extentWidth"] = scroll.ExtentWidth.ToString();
                props["extentHeight"] = scroll.ExtentHeight.ToString();
            }

            if (props.Count > 0)
                info.NativeProperties = props;
        }
        catch
        {
            // Native info is best-effort; don't fail the tree walk
        }
    }

    // Use a ConditionalWeakTable so we never mutate WPF's NameScope (assigning
    // FrameworkElement.Name throws InvalidOperationException once the element is
    // sealed, leaks ids into a NameScope dictionary, and can collide with user
    // names). The table is cleared automatically when the element is collected.
    private static readonly ConditionalWeakTable<FrameworkElement, string> s_stableIds = new();

    protected override string? EnsurePlatformStableId(object platformObj)
    {
        try
        {
            if (platformObj is FrameworkElement fe)
            {
                // Prefer the developer-assigned XAML Name when present.
                if (!string.IsNullOrEmpty(fe.Name))
                    return fe.Name;

                return s_stableIds.GetValue(
                    fe,
                    static _ => "_mauidevflow_" + Guid.NewGuid().ToString("N").Substring(0, 12));
            }
        }
        catch { }
        return null;
    }

    public override bool SupportsNativeElements => true;

    public override IReadOnlyList<IntPtr> GetKnownNativeWindowHandles(Microsoft.Maui.Controls.Application app, int? windowIndex = null)
    {
        var handles = new List<IntPtr>();

        if (windowIndex is not null)
        {
            var window = windowIndex.Value >= 0 && windowIndex.Value < app.Windows.Count
                ? app.Windows[windowIndex.Value]
                : null;
            var handle = GetWindowHandle(window);
            if (handle != IntPtr.Zero)
                handles.Add(handle);
            return handles;
        }

        foreach (var window in app.Windows)
        {
            var handle = GetWindowHandle(window);
            if (handle != IntPtr.Zero)
                handles.Add(handle);
        }

        return handles;
    }

    public override List<ElementInfo> WalkNativeTree(IReadOnlyList<IntPtr> knownWindowHandles, int maxDepth = 0)
    {
        var roots = new List<ElementInfo>();
        var nativeObjects = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        _nativeProbe.AppendNativeWindows(roots, nativeObjects, knownWindowHandles, maxDepth);

        lock (_nativeObjectsLock)
            _nativeObjects = nativeObjects;

        return roots;
    }

    public override object? GetNativeElementById(string id)
    {
        lock (_nativeObjectsLock)
        {
            if (NativeWindowProbe.TryGetAutomationElement(_nativeObjects, id) is { } cached)
                return cached;
        }

        // Preserve native ID stability on cache miss: re-walk with the same HWND
        // the id was originally produced under. A plain Array.Empty<IntPtr>() walk
        // would skip AppendKnownWindowDialogSubtrees entirely, so ":dialog:{n}"
        // prefixes from a previous tree/query would never be regenerated.
        var seedHwnds = NativeWindowProbe.ExtractHwndsFromId(id);
        WalkNativeTree(seedHwnds);
        lock (_nativeObjectsLock)
            return NativeWindowProbe.TryGetAutomationElement(_nativeObjects, id);
    }

    public override ElementInfo? GetNativeElementInfoById(string id)
    {
        // Cache-first: avoid a full UIA tree walk (which calls EnumerateProcessTopLevels
        // and enumerates every same-process window) when the requested id was already
        // resolved by a recent tree/query call.
        Dictionary<string, object> cache;
        lock (_nativeObjectsLock)
            cache = _nativeObjects;

        if (NativeWindowProbe.TryBuildCachedElementInfo(cache, id) is { } cached)
            return cached;

        var seedHwnds = NativeWindowProbe.ExtractHwndsFromId(id);
        return FlattenElementInfos(WalkNativeTree(seedHwnds))
            .FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public override string TryNativeElementTap(string elementId)
    {
        var element = GetNativeAutomationElement(elementId);
        if (element is null)
            return $"Native element '{elementId}' was not found";

        return NativeWindowProbe.TryInvoke(element)
            ? "ok"
            : $"Native element '{elementId}' does not support invoke, toggle, selection, or expand/collapse";
    }

    public override string TryNativeElementSetValue(string elementId, string value)
    {
        var element = GetNativeAutomationElement(elementId);
        if (element is null)
            return $"Native element '{elementId}' was not found";

        return NativeWindowProbe.TrySetValue(element, value)
            ? "ok"
            : $"Native element '{elementId}' does not support writable value";
    }

    public override string TryNativeElementFocus(string elementId)
    {
        var element = GetNativeAutomationElement(elementId);
        if (element is null)
            return $"Native element '{elementId}' was not found";

        return NativeWindowProbe.TryFocus(element)
            ? "ok"
            : $"Native element '{elementId}' could not be focused";
    }

    public override string TryNativeElementScroll(string elementId, double deltaX, double deltaY)
    {
        var element = GetNativeAutomationElement(elementId);
        if (element is null)
            return $"Native element '{elementId}' was not found";

        return NativeWindowProbe.TryScroll(element, deltaX, deltaY)
            ? "ok"
            : $"Native element '{elementId}' does not support scrolling";
    }

    private System.Windows.Automation.AutomationElement? GetNativeAutomationElement(string id)
        => GetNativeElementById(id) as System.Windows.Automation.AutomationElement;

    private static IntPtr GetWindowHandle(Microsoft.Maui.Controls.Window? window)
    {
        if (window?.Handler?.PlatformView is not FrameworkElement frameworkElement)
            return IntPtr.Zero;

        var nativeWindow = System.Windows.Window.GetWindow(frameworkElement);
        return nativeWindow is null ? IntPtr.Zero : new WindowInteropHelper(nativeWindow).Handle;
    }
}
