#if MACOS
using AppKit;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.DevFlow.Agent.Core;
using ObjCRuntime;

namespace Microsoft.Maui.DevFlow.Agent.Native;

/// <summary>
/// AppKit backend for the native DevFlow agent.
/// </summary>
internal static partial class NativeUi
{
    public static string PlatformName => "macOS";

    public static string UiFrameworkName => "appkit";

    public static string DeviceTypeName => "physical";

    public static IAgentDispatcher CreateDispatcher() => new DelegateAgentDispatcher(
        () => !NSThread.IsMain,
        action => NSApplication.SharedApplication.InvokeOnMainThread(action));

    public static double DisplayDensity
        => NSScreen.MainScreen?.BackingScaleFactor ?? 1.0;

    public static IReadOnlyList<object> GetRoots()
    {
        var roots = new List<object>();

        foreach (var window in GetWindows())
        {
            if (window.IsVisible && window.ContentView is { } content)
                roots.Add(content);
        }

        return roots;
    }

    public static (double Width, double Height) GetWindowSize()
    {
        var window = NSApplication.SharedApplication.KeyWindow
            ?? GetWindows().FirstOrDefault(w => w.IsVisible);

        return window == null ? (0, 0) : (window.Frame.Width, window.Frame.Height);
    }

    /// <summary>
    /// Enumerates the app's windows front-to-back. .NET for macOS does not surface
    /// <c>NSApplication.windows</c> as a property, so we drive the enumeration API instead.
    /// </summary>
    private static List<NSWindow> GetWindows()
    {
        var windows = new List<NSWindow>();

        NSApplication.SharedApplication.EnumerateWindows(
            NSWindowListOptions.OrderedFrontToBack,
            (NSWindow window, ref bool stop) =>
            {
                windows.Add(window);
                stop = false;
            });

        return windows;
    }

    public static IReadOnlyList<object> GetChildren(object view)
        => view is NSView nsView ? nsView.Subviews : [];

    public static NativeViewDescriptor Describe(object viewObject)
    {
        var view = (NSView)viewObject;
        var type = view.GetType();
        var window = view.Window;

        // AppKit's origin is bottom-left; DevFlow reports top-left screen coordinates.
        var inWindow = view.ConvertRectToView(view.Bounds, null);
        var screenY = window == null
            ? inWindow.Y
            : (window.Screen ?? NSScreen.MainScreen)!.Frame.Height
              - (window.Frame.Y + inWindow.Y + inWindow.Height);
        var screenX = window == null ? inWindow.X : window.Frame.X + inWindow.X;

        var descriptor = new NativeViewDescriptor
        {
            Type = type.Name,
            FullType = type.FullName ?? type.Name,
            AutomationId = view.Identifier is { Length: > 0 } id ? id : view.AccessibilityIdentifier,
            AccessibilityLabel = SafeAccessibilityLabel(view),
            IsVisible = !view.Hidden && view.AlphaValue > 0,
            IsEnabled = view is not NSControl control || control.Enabled,
            IsFocused = window?.FirstResponder == view,
            Opacity = view.AlphaValue,
            X = screenX,
            Y = screenY,
            Width = view.Bounds.Width,
            Height = view.Bounds.Height,
            IsScrollable = view is NSScrollView,
            IsTappable = view is NSButton or NSControl,
        };

        switch (view)
        {
            case NSTextField field:
                descriptor.Text = field.StringValue;
                descriptor.Value = field.StringValue;
                descriptor.IsTextInput = field.Editable;
                descriptor.Properties = new Dictionary<string, string?> { ["placeholder"] = field.PlaceholderString };
                break;
            case NSTextView textView:
                descriptor.Text = textView.Value;
                descriptor.Value = textView.Value;
                descriptor.IsTextInput = textView.Editable;
                break;
            case NSButton button:
                descriptor.Text = button.Title;
                descriptor.IsSelected = button.State == NSCellStateValue.On;
                descriptor.Value = button.State == NSCellStateValue.On ? "true" : "false";
                break;
            case NSImageView:
                descriptor.Text = SafeAccessibilityLabel(view);
                break;
        }

        descriptor.Text ??= SafeAccessibilityLabel(view);
        return descriptor;
    }

    private static string? SafeAccessibilityLabel(NSView view)
    {
        try { return view.AccessibilityLabel; }
        catch (Exception) { return null; }
    }

    public static bool TryTap(object viewObject, double? x, double? y)
    {
        switch (viewObject)
        {
            case NSButton button when button.Enabled:
                button.PerformClick(button);
                return true;
            case NSControl control when control.Enabled:
                control.PerformClick(control);
                return true;
            default:
                return false;
        }
    }

    public static bool TrySetText(object viewObject, string text)
    {
        switch (viewObject)
        {
            case NSTextField field:
                field.StringValue = text;
                field.SendAction(field.Action, field.Target);
                return true;
            case NSTextView textView:
                textView.Value = text;
                return true;
            case NSButton button:
                button.Title = text;
                return true;
            default:
                return false;
        }
    }

    public static bool TryFocus(object viewObject)
    {
        var view = (NSView)viewObject;
        return view.Window?.MakeFirstResponder(view) ?? false;
    }

    public static bool TryScrollBy(object viewObject, double dx, double dy)
    {
        if (viewObject is not NSScrollView scroll) return false;

        var origin = scroll.ContentView.Bounds.Location;
        scroll.ContentView.ScrollToPoint(new CGPoint(origin.X + dx, origin.Y + dy));
        scroll.ReflectScrolledClipView(scroll.ContentView);
        return true;
    }

    public static bool TryScrollIntoView(object viewObject)
    {
        var view = (NSView)viewObject;
        return view.ScrollRectToVisible(view.Bounds);
    }

    /// <summary>AppKit has no navigation stack, so back navigation is unsupported.</summary>
    public static bool TryGoBack() => false;

    public static byte[]? CaptureView(object viewObject)
    {
        var view = (NSView)viewObject;
        if (view.Bounds.Width <= 0 || view.Bounds.Height <= 0) return null;

        var rep = view.BitmapImageRepForCachingDisplayInRect(view.Bounds);
        if (rep == null) return null;

        view.CacheDisplay(view.Bounds, rep);

        using var data = rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, null);
        return data?.ToArray();
    }

    public static byte[]? CaptureScreen()
        => GetRoots().FirstOrDefault() is { } root ? CaptureView(root) : null;

    public static IReadOnlyDictionary<string, string?> GetProperties(object viewObject)
    {
        var view = (NSView)viewObject;
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        var properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["AlphaValue"] = view.AlphaValue.ToString(culture),
            ["Hidden"] = view.Hidden ? "True" : "False",
            ["Width"] = view.Bounds.Width.ToString(culture),
            ["Height"] = view.Bounds.Height.ToString(culture),
            ["Identifier"] = view.Identifier,
            ["AccessibilityLabel"] = SafeAccessibilityLabel(view),
        };

        if (view is NSControl control)
            properties["Enabled"] = control.Enabled ? "True" : "False";

        switch (view)
        {
            case NSTextField field:
                properties["StringValue"] = field.StringValue;
                properties["PlaceholderString"] = field.PlaceholderString;
                break;
            case NSTextView textView:
                properties["Value"] = textView.Value;
                break;
            case NSButton button:
                properties["Title"] = button.Title;
                properties["State"] = button.State.ToString();
                break;
        }

        return properties;
    }

    public static bool TrySetProperty(object viewObject, string name, string? value, out string? error)
    {
        error = null;
        var view = (NSView)viewObject;

        switch (name.ToLowerInvariant())
        {
            case "text":
            case "title":
            case "stringvalue":
                return TrySetText(view, value ?? string.Empty) || Fail($"'{view.GetType().Name}' has no text to set.", out error);
            case "alpha":
            case "alphavalue":
            case "opacity":
                if (!double.TryParse(value, out var alpha)) return Fail($"'{value}' is not a number.", out error);
                view.AlphaValue = (nfloat)alpha;
                return true;
            case "hidden":
                if (!bool.TryParse(value, out var hidden)) return Fail($"'{value}' is not a boolean.", out error);
                view.Hidden = hidden;
                return true;
            case "isvisible":
                if (!bool.TryParse(value, out var visible)) return Fail($"'{value}' is not a boolean.", out error);
                view.Hidden = !visible;
                return true;
            case "enabled":
            case "isenabled":
                if (view is not NSControl control) return Fail($"'{view.GetType().Name}' is not a control.", out error);
                if (!bool.TryParse(value, out var enabled)) return Fail($"'{value}' is not a boolean.", out error);
                control.Enabled = enabled;
                return true;
            case "state":
            case "ischecked":
                if (view is not NSButton button) return Fail($"'{view.GetType().Name}' is not a button.", out error);
                if (!bool.TryParse(value, out var on)) return Fail($"'{value}' is not a boolean.", out error);
                button.State = on ? NSCellStateValue.On : NSCellStateValue.Off;
                return true;
            default:
                return Fail($"Property '{name}' is not settable on '{view.GetType().Name}'.", out error);
        }
    }

    public static string? AppName
        => NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleDisplayName")?.ToString() is { Length: > 0 } display
            ? display
            : NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleName")?.ToString();

    public static string? AppPackageId => NSBundle.MainBundle.BundleIdentifier;

    public static string? AppVersion => NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString();

    public static string? AppBuild => NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleVersion")?.ToString();

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}
#endif
