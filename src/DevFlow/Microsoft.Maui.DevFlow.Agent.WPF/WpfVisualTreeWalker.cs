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
    private readonly Dictionary<string, object> _nativeHitObjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ElementInfo> _nativeHitInfos = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxNativeHitCacheSize = 256;

    internal override bool AreElementIdentitiesEqual(object first, object second)
        => NativeWindowProbe.SameIdentity(first, second);

    internal override object GetElementIdentity(object element)
        => element is System.Windows.Automation.AutomationElement automationElement
            ? NativeWindowProbe.GetStableIdentity(automationElement)
            : base.GetElementIdentity(element);

    internal override bool ShouldRetainElementIdentityStrongly(object identity)
        => NativeWindowProbe.IsDurableIdentity(identity)
            || base.ShouldRetainElementIdentityStrongly(identity);

    internal static byte[]? CaptureNativeElementScreenshot(object nativeElement)
        => nativeElement is System.Windows.Automation.AutomationElement automationElement
            ? NativeWindowProbe.CaptureElementScreenshot(automationElement)
            : null;

    public WpfVisualTreeWalker()
    {
    }

    internal WpfVisualTreeWalker(NativeElementRegistrationRegistry nativeElementRegistry)
        : base(nativeElementRegistry)
    {
    }

    internal override ElementInfo CreateRegisteredNativeElementInfo(
        NativeElementRegistrationSnapshot registration,
        string? ownerId)
    {
        var info = base.CreateRegisteredNativeElementInfo(registration, ownerId);
        info.Framework = "wpf-native";
        if (registration.NativeElement is not FrameworkElement element)
            return info;

        var window = System.Windows.Window.GetWindow(element);
        if (window is not null)
        {
            var point = element.TranslatePoint(new System.Windows.Point(0, 0), window);
            info.WindowBounds = new BoundsInfo
            {
                X = point.X,
                Y = point.Y,
                Width = element.ActualWidth,
                Height = element.ActualHeight
            };
            info.BoundsQuality = "exact";
        }

        info.IsVisible = element.IsVisible;
        info.IsEnabled = element.IsEnabled;
        info.IsFocused = element.IsKeyboardFocusWithin;
        info.AutomationId = System.Windows.Automation.AutomationProperties.GetAutomationId(element);
        info.Text = System.Windows.Automation.AutomationProperties.GetName(element);
        if (string.IsNullOrEmpty(info.Text))
        {
            info.Text = element switch
            {
                System.Windows.Controls.Button button => button.Content?.ToString(),
                TextBlock textBlock => textBlock.Text,
                _ => null
            };
        }

        return info;
    }

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

    public override List<ElementInfo> HitTestNativeElements(
        IReadOnlyList<IntPtr> knownWindowHandles,
        double x,
        double y)
    {
        lock (_nativeObjectsLock)
        {
            var hitObjects = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var hits = _nativeProbe.HitTest(hitObjects, knownWindowHandles, x, y);
            foreach (var hit in hits)
            {
                if (hitObjects.TryGetValue(hit.Id, out var nativeObject))
                {
                    if (!_nativeHitObjects.ContainsKey(hit.Id)
                        && _nativeHitObjects.Count >= MaxNativeHitCacheSize)
                    {
                        var expiredId = _nativeHitObjects.Keys.First();
                        _nativeHitObjects.Remove(expiredId);
                        _nativeHitInfos.Remove(expiredId);
                    }

                    _nativeHitObjects[hit.Id] = nativeObject;
                    _nativeHitInfos[hit.Id] = hit;
                }
            }

            return hits;
        }
    }

    public override object? GetNativeElementById(string id)
    {
        if (id.StartsWith("native:registered:", StringComparison.Ordinal))
            return base.GetNativeElementById(id);

        lock (_nativeObjectsLock)
        {
            if (NativeWindowProbe.TryGetAutomationElement(_nativeHitObjects, id) is { } hitElement)
                return hitElement;
            if (NativeWindowProbe.TryGetAutomationElement(_nativeObjects, id) is { } cached)
                return cached;
        }

        if (_nativeProbe.FindByRuntimeId(id) is { } runtimeElement)
            return runtimeElement;
        if (_nativeProbe.FindByHitId(id) is { } recoveredHitElement)
            return recoveredHitElement;

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
        if (id.StartsWith("native:registered:", StringComparison.Ordinal))
            return base.GetNativeElementInfoById(id);

        // Cache-first: avoid a full UIA tree walk (which calls EnumerateProcessTopLevels
        // and enumerates every same-process window) when the requested id was already
        // resolved by a recent tree/query call.
        Dictionary<string, object> cache;
        lock (_nativeObjectsLock)
        {
            if (_nativeHitInfos.TryGetValue(id, out var hitInfo))
                return hitInfo;
            cache = _nativeObjects;
        }

        if (NativeWindowProbe.TryBuildCachedElementInfo(cache, id) is { } cached)
            return cached;

        if (_nativeProbe.FindByRuntimeId(id) is { } runtimeElement)
            return NativeWindowProbe.TryBuildElementInfo(runtimeElement, id);
        if (_nativeProbe.FindByHitId(id) is { } recoveredHitElement)
            return NativeWindowProbe.TryBuildElementInfo(recoveredHitElement, id);

        var seedHwnds = NativeWindowProbe.ExtractHwndsFromId(id);
        return FlattenElementInfos(WalkNativeTree(seedHwnds))
            .FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public override string TryNativeElementTap(string elementId)
    {
        if (elementId.StartsWith("native:registered:", StringComparison.Ordinal))
            return base.TryNativeElementTap(elementId);

        var element = GetNativeAutomationElement(elementId);
        if (element is null)
            return $"Native element '{elementId}' was not found";

        return NativeWindowProbe.TryInvoke(element)
            ? "ok"
            : $"Native element '{elementId}' does not support invoke, toggle, selection, or expand/collapse";
    }

    protected internal override string TryNativeElementTap(string elementId, object nativeElement)
    {
        if (elementId.StartsWith("native:registered:", StringComparison.Ordinal))
            return base.TryNativeElementTap(elementId, nativeElement);

        return nativeElement is System.Windows.Automation.AutomationElement element
            && NativeWindowProbe.TryInvoke(element)
                ? "ok"
                : $"Native element '{elementId}' does not support invoke, toggle, selection, or expand/collapse";
    }

    public override string TryNativeElementSetValue(string elementId, string value)
    {
        if (elementId.StartsWith("native:registered:", StringComparison.Ordinal))
            return base.TryNativeElementSetValue(elementId, value);

        var element = GetNativeAutomationElement(elementId);
        if (element is null)
            return $"Native element '{elementId}' was not found";

        return NativeWindowProbe.TrySetValue(element, value)
            ? "ok"
            : $"Native element '{elementId}' does not support writable value";
    }

    protected internal override string TryNativeElementSetValue(
        string elementId,
        object nativeElement,
        string value)
    {
        if (elementId.StartsWith("native:registered:", StringComparison.Ordinal))
            return base.TryNativeElementSetValue(elementId, nativeElement, value);

        return nativeElement is System.Windows.Automation.AutomationElement element
            && NativeWindowProbe.TrySetValue(element, value)
                ? "ok"
                : $"Native element '{elementId}' does not support writable value";
    }

    public override string TryNativeElementFocus(string elementId)
    {
        if (elementId.StartsWith("native:registered:", StringComparison.Ordinal))
        {
            if (GetNativeElementById(elementId) is System.Windows.IInputElement inputElement)
            {
                System.Windows.Input.Keyboard.Focus(inputElement);
                return "ok";
            }

            return $"Native element '{elementId}' could not be focused";
        }

        var element = GetNativeAutomationElement(elementId);
        if (element is null)
            return $"Native element '{elementId}' was not found";

        return NativeWindowProbe.TryFocus(element)
            ? "ok"
            : $"Native element '{elementId}' could not be focused";
    }

    protected internal override string TryNativeElementFocus(string elementId, object nativeElement)
    {
        if (elementId.StartsWith("native:registered:", StringComparison.Ordinal))
        {
            if (nativeElement is System.Windows.IInputElement inputElement)
            {
                System.Windows.Input.Keyboard.Focus(inputElement);
                return "ok";
            }

            return $"Native element '{elementId}' could not be focused";
        }

        return nativeElement is System.Windows.Automation.AutomationElement element
            && NativeWindowProbe.TryFocus(element)
                ? "ok"
                : $"Native element '{elementId}' could not be focused";
    }

    public override string TryNativeElementScroll(string elementId, double deltaX, double deltaY)
    {
        if (elementId.StartsWith("native:registered:", StringComparison.Ordinal))
        {
            if (GetNativeElementById(elementId) is System.Windows.FrameworkElement frameworkElement)
            {
                frameworkElement.BringIntoView();
                return "ok";
            }

            return $"Native element '{elementId}' does not support scrolling";
        }

        var element = GetNativeAutomationElement(elementId);
        if (element is null)
            return $"Native element '{elementId}' was not found";

        return NativeWindowProbe.TryScroll(element, deltaX, deltaY)
            ? "ok"
            : $"Native element '{elementId}' does not support scrolling";
    }

    protected internal override string TryNativeElementScroll(
        string elementId,
        object nativeElement,
        double deltaX,
        double deltaY)
    {
        if (elementId.StartsWith("native:registered:", StringComparison.Ordinal))
        {
            if (nativeElement is System.Windows.FrameworkElement frameworkElement)
            {
                frameworkElement.BringIntoView();
                return "ok";
            }

            return $"Native element '{elementId}' does not support scrolling";
        }

        return nativeElement is System.Windows.Automation.AutomationElement element
            && NativeWindowProbe.TryScroll(element, deltaX, deltaY)
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
