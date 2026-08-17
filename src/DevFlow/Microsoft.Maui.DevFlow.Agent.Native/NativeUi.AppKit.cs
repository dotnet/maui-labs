#if MACOS
using System.Runtime.InteropServices;
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

        // ConvertRectToView(bounds, null) yields AppKit's window base coordinates: bottom-left
        // origin, already relative to the containing window (not the screen). DevFlow's
        // ui.hit-test capability advertises window-logical coordinates — top-left origin,
        // relative to that same window — so flip against the window's own frame height rather
        // than the screen's, or hit testing and Inspector overlays go stale the moment the
        // window moves away from the screen origin.
        var inWindow = view.ConvertRectToView(view.Bounds, null);
        var windowHeight = window?.Frame.Height ?? inWindow.Y + inWindow.Height;
        var (windowX, windowY) = FlipAppKitWindowBaseToTopLeft(inWindow.X, inWindow.Y, inWindow.Height, windowHeight);

        var descriptor = new NativeViewDescriptor
        {
            Type = type.Name,
            FullType = type.FullName ?? type.Name,
            AutomationId = view.Identifier is { Length: > 0 } id ? id : view.AccessibilityIdentifier,
            AccessibilityLabel = SafeAccessibilityLabel(view),
            IsVisible = !view.Hidden && view.AlphaValue > 0,
            IsEnabled = view is not NSControl control || control.Enabled,
            IsFocused = window?.FirstResponder == view
                || view is NSTextField { CurrentEditor: { } editor } && window?.FirstResponder == editor,
            Opacity = view.AlphaValue,
            X = windowX,
            Y = windowY,
            Width = view.Bounds.Width,
            Height = view.Bounds.Height,
            IsScrollable = view is NSScrollView,
            IsTappable = view is NSButton
                || view is NSControl { Enabled: true, Action: not null },
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
                SendActionIfAvailable(field);
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

    private static void SendActionIfAvailable(NSControl control)
    {
        if (control.Action is { } action)
            control.SendAction(action, control.Target ?? control);
    }

    public static bool TrySendKey(object? viewObject, string? key, string? text, out string? error)
    {
        error = null;
        var keyValue = key ?? text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(keyValue))
            return Fail("key or text is required", out error);

        var target = viewObject ?? NSApplication.SharedApplication.KeyWindow?.FirstResponder;
        var normalizedKey = keyValue.Trim().ToLowerInvariant();
        var textToInsert = text ?? (keyValue.Length == 1 ? keyValue : null);

        switch (target)
        {
            case NSTextField field:
                if (normalizedKey is "enter" or "return")
                {
                    SendActionIfAvailable(field);
                    return true;
                }

                if (normalizedKey is "backspace" or "delete")
                {
                    field.StringValue = field.StringValue.Length > 0 ? field.StringValue[..^1] : string.Empty;
                    SendActionIfAvailable(field);
                    return true;
                }

                if (!string.IsNullOrEmpty(textToInsert))
                {
                    field.StringValue += textToInsert;
                    SendActionIfAvailable(field);
                    return true;
                }

                return Fail($"Unsupported key '{keyValue}' for NSTextField.", out error);

            case NSTextView textView:
                if (normalizedKey is "enter" or "return")
                {
                    textView.Value += Environment.NewLine;
                    return true;
                }

                if (normalizedKey is "backspace" or "delete")
                {
                    textView.Value = textView.Value?.Length > 0 ? textView.Value[..^1] : string.Empty;
                    return true;
                }

                if (!string.IsNullOrEmpty(textToInsert))
                {
                    textView.Value += textToInsert;
                    return true;
                }

                return Fail($"Unsupported key '{keyValue}' for NSTextView.", out error);

            case NSButton button when normalizedKey is "enter" or "return" or "space" or " ":
                button.PerformClick(button);
                return true;

            case null:
                // Parity with the MAUI agent, whose key handler returns "ok" for a null element: a
                // key with no target is a no-op success, not a client error.
                return true;

            default:
                return Fail($"Element '{target.GetType().Name}' does not accept keyboard input.", out error);
        }
    }

    public static bool TryGesture(object? viewObject, string? type, string? direction, double distance, int durationMs, out string? error)
    {
        error = null;
        var normalizedType = string.IsNullOrWhiteSpace(type) ? "swipe" : type.Trim().ToLowerInvariant();

        if (normalizedType is "tap" or "longpress" or "long-press")
        {
            if (viewObject == null)
            {
                error = "elementId is required to tap";
                return false;
            }

            if (TryTap(viewObject, null, null)) return true;
            error = $"Gesture '{type}' is not handled by this element";
            return false;
        }

        if (normalizedType is not ("swipe" or "pan" or "scroll"))
        {
            error = $"Gesture '{type}' is not supported on AppKit";
            return false;
        }

        var logicalDistance = distance > 0 ? distance : 120;
        var (dx, dy) = (direction ?? "up").Trim().ToLowerInvariant() switch
        {
            "down" => (0d, logicalDistance),
            "left" => (-logicalDistance, 0d),
            "right" => (logicalDistance, 0d),
            _ => (0d, -logicalDistance),
        };

        if (TryScrollBy(viewObject, dx, dy)) return true;
        error = "Element is not scrollable";
        return false;
    }

    public static bool TryScrollBy(object? viewObject, double dx, double dy)
    {
        if (FindScrollView(viewObject) is not { } scroll) return false;

        var origin = scroll.ContentView.Bounds.Location;
        scroll.ContentView.ScrollToPoint(new CGPoint(origin.X + dx, origin.Y + dy));
        scroll.ReflectScrolledClipView(scroll.ContentView);
        return true;
    }

    /// <summary>
    /// Resolves the scroll view a scroll/swipe should act on: the view itself, else its nearest
    /// scrolling ancestor, else — when no view was named — the first scroll view on screen.
    /// </summary>
    static NSScrollView? FindScrollView(object? viewObject)
        => FindSelfOrAncestor(
            viewObject,
            static candidate => candidate is NSScrollView,
            static candidate => (candidate as NSView)?.Superview) as NSScrollView;

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
        return CaptureViewViaCacheDisplay(view) ?? CaptureViewViaPdf(view);
    }

    public static byte[]? CaptureScreen()
    {
        var window = NSApplication.SharedApplication.KeyWindow
            ?? GetWindows().FirstOrDefault(w => w.IsVisible);

        if (window?.AttachedSheet is NSWindow sheet)
            window = sheet;

        if (window != null)
        {
            var windowPng = CaptureWindowViaCG(window);
            if (windowPng != null)
                return windowPng;

            if (window.ContentView is { } content)
                return CaptureView(content);
        }

        return GetRoots().FirstOrDefault() is { } root ? CaptureView(root) : null;
    }

    private static byte[]? CaptureViewViaCacheDisplay(NSView view)
    {
        var bounds = view.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;

        view.LayoutSubtreeIfNeeded();
        view.DisplayIfNeeded();

        var scale = view.Window?.BackingScaleFactor ?? NSScreen.MainScreen?.BackingScaleFactor ?? 1.0;
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));

        using var rep = new NSBitmapImageRep(
            IntPtr.Zero,
            pixelWidth,
            pixelHeight,
            8,
            4,
            true,
            false,
            NSColorSpace.DeviceRGB,
            0,
            0);

        rep.Size = new CGSize(bounds.Width, bounds.Height);

        NSGraphicsContext.GlobalSaveGraphicsState();
        try
        {
            var context = NSGraphicsContext.FromBitmap(rep);
            if (context == null) return null;

            NSGraphicsContext.CurrentContext = context;
            view.CacheDisplay(bounds, rep);
        }
        finally
        {
            NSGraphicsContext.GlobalRestoreGraphicsState();
        }

        using var data = rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary());
        return data?.ToArray();
    }

    private static byte[]? CaptureViewViaPdf(NSView view)
    {
        var bounds = view.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;

        using var pdfData = view.DataWithPdfInsideRect(bounds);
        if (pdfData == null) return null;

        using var image = new NSImage(pdfData);
        using var tiffData = image.AsTiff();
        if (tiffData == null) return null;

        using var rep = new NSBitmapImageRep(tiffData);
        using var data = rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary());
        return data?.ToArray();
    }

    private static byte[]? CaptureWindowViaCG(NSWindow window)
    {
        var cgImagePtr = CGWindowListCreateImage(CGRect.Null, 0x08, (uint)window.WindowNumber, 0x01);
        if (cgImagePtr == IntPtr.Zero) return null;

        using var cgImage = Runtime.GetINativeObject<CGImage>(cgImagePtr, owns: true);
        if (cgImage == null) return null;

        using var rep = new NSBitmapImageRep(cgImage);
        using var data = rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary());
        return data?.ToArray();
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGWindowListCreateImage(
        CGRect screenBounds,
        uint listOption,
        uint windowID,
        uint imageOption);

    public static IReadOnlyDictionary<string, string?> GetProperties(object viewObject)
    {
        var view = (NSView)viewObject;
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        var properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["AlphaValue"] = view.AlphaValue.ToString(culture),
            ["Opacity"] = view.AlphaValue.ToString(culture),
            ["Hidden"] = view.Hidden ? "True" : "False",
            // Cross-framework aliases. The setter already accepts Text/IsVisible/IsEnabled/IsChecked,
            // and UIKit publishes the same names, so reads have to match or callers can write a
            // property they cannot read back.
            ["IsVisible"] = (!view.Hidden && view.AlphaValue > 0).ToString(),
            ["Width"] = view.Bounds.Width.ToString(culture),
            ["Height"] = view.Bounds.Height.ToString(culture),
            ["Identifier"] = view.Identifier,
            ["AccessibilityIdentifier"] = view.Identifier,
            ["AccessibilityLabel"] = SafeAccessibilityLabel(view),
        };

        if (view is NSControl control)
        {
            properties["Enabled"] = control.Enabled ? "True" : "False";
            properties["IsEnabled"] = control.Enabled ? "True" : "False";
        }

        switch (view)
        {
            case NSTextField field:
                properties["StringValue"] = field.StringValue;
                properties["Text"] = field.StringValue;
                properties["Value"] = field.StringValue;
                properties["PlaceholderString"] = field.PlaceholderString;
                properties["Placeholder"] = field.PlaceholderString;
                break;
            case NSTextView textView:
                properties["Value"] = textView.Value;
                properties["Text"] = textView.Value;
                break;
            case NSButton button:
                properties["Title"] = button.Title;
                properties["Text"] = button.Title;
                properties["State"] = button.State.ToString();
                // Checkboxes and switches are both NSButton on AppKit (SetButtonType(Switch)), so
                // the checked state doubles as the button's value.
                properties["IsChecked"] = (button.State == NSCellStateValue.On).ToString();
                properties["On"] = (button.State == NSCellStateValue.On).ToString();
                properties["Value"] = button.Title;
                break;
        }

        AddCanonicalAliases(properties, opacity: view.AlphaValue);

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
