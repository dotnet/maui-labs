#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using Microsoft.Maui.DevFlow.Agent.Core;
using ObjCRuntime;
using UIKit;

namespace Microsoft.Maui.DevFlow.Agent.Native;

/// <summary>
/// UIKit backend for the native DevFlow agent. Shared by iOS and Mac Catalyst.
/// </summary>
internal static partial class NativeUi
{
#if MACCATALYST
    public static string PlatformName => "MacCatalyst";
#else
    public static string PlatformName => "iOS";
#endif

    public static string UiFrameworkName => "uikit";

#if MACCATALYST
    public static string DeviceTypeName => "physical";
#else
    // The simulator injects SIMULATOR_* into the process environment; a physical device never has it.
    public static string DeviceTypeName
        => string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SIMULATOR_DEVICE_NAME")) ? "physical" : "virtual";
#endif

    public static IAgentDispatcher CreateDispatcher() => new DelegateAgentDispatcher(
        () => !NSThread.IsMain,
        action => UIApplication.SharedApplication.InvokeOnMainThread(action));

    public static double DisplayDensity => UIScreen.MainScreen.Scale;

    public static IReadOnlyList<object> GetRoots()
    {
        var roots = new List<object>();

        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes.ToArray())
        {
            if (scene is not UIWindowScene windowScene) continue;

            foreach (var window in windowScene.Windows)
            {
                if (!window.Hidden)
                    roots.Add(window);
            }
        }

        if (roots.Count == 0)
        {
            foreach (var window in UIApplication.SharedApplication.Windows)
            {
                if (!window.Hidden)
                    roots.Add(window);
            }
        }

        return roots;
    }

    public static (double Width, double Height) GetWindowSize()
    {
        if (GetRoots().FirstOrDefault() is UIWindow window)
            return (window.Bounds.Width, window.Bounds.Height);

        var bounds = UIScreen.MainScreen.Bounds;
        return (bounds.Width, bounds.Height);
    }

    public static IReadOnlyList<object> GetChildren(object view)
        => view is UIView uiView ? uiView.Subviews : [];

    public static NativeViewDescriptor Describe(object viewObject)
    {
        var view = (UIView)viewObject;
        var type = view.GetType();
        var frame = view.ConvertRectToView(view.Bounds, null);

        var descriptor = new NativeViewDescriptor
        {
            Type = type.Name,
            FullType = type.FullName ?? type.Name,
            AutomationId = view.AccessibilityIdentifier is { Length: > 0 } id ? id : view.RestorationIdentifier,
            AccessibilityLabel = view.AccessibilityLabel,
            IsVisible = !view.Hidden && view.Alpha > 0,
            IsEnabled = view is not UIControl control || control.Enabled,
            IsFocused = view.IsFirstResponder,
            IsSelected = view is UIControl { Selected: true },
            Opacity = view.Alpha,
            X = frame.X,
            Y = frame.Y,
            Width = frame.Width,
            Height = frame.Height,
            IsScrollable = view is UIScrollView,
            IsTappable = view is UIControl || view.GestureRecognizers?.Any(g => g is UITapGestureRecognizer) == true,
        };

        switch (view)
        {
            case UITextField field:
                descriptor.Text = field.Text;
                descriptor.Value = field.Text;
                descriptor.IsTextInput = true;
                descriptor.Properties = new Dictionary<string, string?> { ["placeholder"] = field.Placeholder };
                break;
            case UITextView textView:
                descriptor.Text = textView.Text;
                descriptor.Value = textView.Text;
                descriptor.IsTextInput = true;
                break;
            case UIButton button:
                descriptor.Text = button.CurrentTitle;
                break;
            case UILabel label:
                descriptor.Text = label.Text;
                break;
            case UISwitch @switch:
                descriptor.Value = @switch.On ? "true" : "false";
                descriptor.IsSelected = @switch.On;
                descriptor.Properties = new Dictionary<string, string?> { ["on"] = @switch.On ? "true" : "false" };
                break;
            case UIImageView:
                descriptor.Text = view.AccessibilityLabel;
                break;
        }

        descriptor.Text ??= view.AccessibilityLabel;
        return descriptor;
    }

    public static bool TryTap(object viewObject, double? x, double? y)
    {
        var view = (UIView)viewObject;

        if (view is UISwitch @switch && @switch.Enabled)
        {
            @switch.SetState(!@switch.On, animated: false);
            @switch.SendActionForControlEvents(UIControlEvent.ValueChanged);
            return true;
        }

        if (view is UIControl control && control.Enabled)
        {
            control.SendActionForControlEvents(UIControlEvent.TouchUpInside);
            return true;
        }

        var recognizers = view.GestureRecognizers;
        if (recognizers != null)
        {
            foreach (var recognizer in recognizers)
            {
                if (recognizer is UITapGestureRecognizer { Enabled: true } tap)
                {
                    // UIKit offers no public API to fire a recognizer directly, so invoke the
                    // registered target/action pairs the recognizer would have called.
                    if (TryInvokeRecognizerTargets(tap)) return true;
                }
            }
        }

        return false;
    }

    private static bool TryInvokeRecognizerTargets(UIGestureRecognizer recognizer)
    {
        // `_targets` is the documented-by-convention ivar every UIGestureRecognizer keeps.
        // Reading it is the only way to replay a tap without a real touch stream.
        var targets = recognizer.ValueForKey(new NSString("targets")) as NSArray;
        if (targets == null || targets.Count == 0) return false;

        for (nuint i = 0; i < targets.Count; i++)
        {
            var entry = targets.GetItem<NSObject>(i);
            var target = entry.ValueForKey(new NSString("target"));
            var action = entry.ValueForKey(new NSString("action"));
            if (target == null || action == null) continue;

            var selector = new Selector(action.ToString());
            if (target.RespondsToSelector(selector))
                target.PerformSelector(selector, recognizer, 0);
        }

        return true;
    }

    public static bool TrySetText(object viewObject, string text)
    {
        switch (viewObject)
        {
            case UITextField field:
                field.Text = text;
                field.SendActionForControlEvents(UIControlEvent.EditingChanged);
                return true;
            case UITextView textView:
                textView.Text = text;
                textView.Delegate?.Changed(textView);
                return true;
            case UIButton button:
                button.SetTitle(text, UIControlState.Normal);
                return true;
            case UILabel label:
                label.Text = text;
                return true;
            default:
                return false;
        }
    }

    public static bool TryFocus(object viewObject) => ((UIView)viewObject).BecomeFirstResponder();

    public static bool TrySendKey(object? viewObject, string? key, string? text, out string? error)
    {
        error = null;
        var keyValue = key ?? text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(keyValue))
        {
            error = "key or text is required";
            return false;
        }

        var normalizedKey = keyValue.Trim().ToLowerInvariant();
        var textToInsert = text ?? (keyValue.Length == 1 ? keyValue : null);

        switch (viewObject)
        {
            case UITextField field:
                if (normalizedKey is "enter" or "return")
                {
                    field.SendActionForControlEvents(UIControlEvent.EditingDidEndOnExit);
                    field.ResignFirstResponder();
                    return true;
                }

                if (normalizedKey is "backspace" or "delete")
                {
                    var current = field.Text ?? string.Empty;
                    field.Text = current.Length > 0 ? current[..^1] : string.Empty;
                    field.SendActionForControlEvents(UIControlEvent.EditingChanged);
                    return true;
                }

                if (!string.IsNullOrEmpty(textToInsert))
                {
                    field.Text = (field.Text ?? string.Empty) + textToInsert;
                    field.SendActionForControlEvents(UIControlEvent.EditingChanged);
                    return true;
                }

                error = $"Unsupported key '{keyValue}' for UITextField.";
                return false;

            case UITextView textView:
                if (normalizedKey is "backspace" or "delete")
                {
                    var current = textView.Text ?? string.Empty;
                    textView.Text = current.Length > 0 ? current[..^1] : string.Empty;
                    textView.Delegate?.Changed(textView);
                    return true;
                }

                var insertion = normalizedKey is "enter" or "return" ? "\n" : textToInsert;
                if (!string.IsNullOrEmpty(insertion))
                {
                    textView.Text = (textView.Text ?? string.Empty) + insertion;
                    textView.Delegate?.Changed(textView);
                    return true;
                }

                error = $"Unsupported key '{keyValue}' for UITextView.";
                return false;

            case UIButton button when normalizedKey is "enter" or "return" or "space" or " ":
                button.SendActionForControlEvents(UIControlEvent.TouchUpInside);
                return true;

            case null:
                error = "No target view: pass elementId to send a key.";
                return false;

            default:
                error = $"Element '{viewObject.GetType().Name}' does not accept keyboard input.";
                return false;
        }
    }

    public static bool TryGesture(object viewObject, string? type, string? direction, double distance, int durationMs, out string? error)
    {
        error = null;
        var normalizedType = string.IsNullOrWhiteSpace(type) ? "swipe" : type.Trim().ToLowerInvariant();

        if (normalizedType is "tap" or "longpress" or "long-press")
        {
            if (TryTap(viewObject, null, null)) return true;
            error = $"Gesture '{type}' is not handled by this element";
            return false;
        }

        if (normalizedType is not ("swipe" or "pan" or "scroll"))
        {
            error = $"Gesture '{type}' is not supported on UIKit";
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

    public static bool TryScrollBy(object viewObject, double dx, double dy)
    {
        if (viewObject is not UIScrollView scroll) return false;

        var offset = scroll.ContentOffset;
        scroll.SetContentOffset(new CGPoint(offset.X + dx, offset.Y + dy), animated: true);
        return true;
    }

    public static bool TryScrollIntoView(object viewObject)
    {
        var view = (UIView)viewObject;

        for (var parent = view.Superview; parent != null; parent = parent.Superview)
        {
            if (parent is not UIScrollView scroll) continue;

            scroll.ScrollRectToVisible(view.ConvertRectToView(view.Bounds, scroll), animated: true);
            return true;
        }

        return false;
    }

    public static bool TryGoBack()
    {
        var controller = GetRoots().FirstOrDefault() is UIWindow window ? window.RootViewController : null;

        while (controller != null)
        {
            if (controller is UINavigationController { ViewControllers.Length: > 1 } navigation)
            {
                navigation.PopViewController(animated: true);
                return true;
            }

            if (controller.PresentedViewController != null)
            {
                controller.DismissViewController(animated: true, completionHandler: null);
                return true;
            }

            controller = controller.ChildViewControllers.FirstOrDefault();
        }

        return false;
    }

    public static byte[]? CaptureView(object viewObject)
    {
        var view = (UIView)viewObject;
        if (view.Bounds.Width <= 0 || view.Bounds.Height <= 0) return null;

        var renderer = new UIGraphicsImageRenderer(view.Bounds.Size, new UIGraphicsImageRendererFormat
        {
            Opaque = false,
            Scale = UIScreen.MainScreen.Scale,
        });

        using var image = renderer.CreateImage(context =>
            view.DrawViewHierarchy(view.Bounds, afterScreenUpdates: true));

        using var data = image.AsPNG();
        return data?.ToArray();
    }

    public static byte[]? CaptureScreen()
        => GetRoots().FirstOrDefault() is { } window ? CaptureView(window) : null;

    public static IReadOnlyDictionary<string, string?> GetProperties(object viewObject)
    {
        var view = (UIView)viewObject;
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        var properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Alpha"] = view.Alpha.ToString(culture),
            ["Opacity"] = view.Alpha.ToString(culture),
            ["Hidden"] = view.Hidden ? "True" : "False",
            ["IsVisible"] = (!view.Hidden && view.Alpha > 0).ToString(),
            ["Width"] = view.Bounds.Width.ToString(culture),
            ["Height"] = view.Bounds.Height.ToString(culture),
            ["AccessibilityIdentifier"] = view.AccessibilityIdentifier,
            ["AccessibilityLabel"] = view.AccessibilityLabel,
            ["IsFirstResponder"] = view.IsFirstResponder ? "True" : "False",
        };

        if (view is UIControl control)
        {
            properties["Enabled"] = control.Enabled ? "True" : "False";
            properties["IsEnabled"] = control.Enabled ? "True" : "False";
            properties["IsSelected"] = control.Selected ? "True" : "False";
        }

        switch (view)
        {
            case UILabel label:
                properties["Text"] = label.Text;
                properties["Value"] = label.Text;
                break;
            case UITextField field:
                properties["Text"] = field.Text;
                properties["Value"] = field.Text;
                properties["Placeholder"] = field.Placeholder;
                break;
            case UITextView textView:
                properties["Text"] = textView.Text;
                properties["Value"] = textView.Text;
                break;
            case UIButton button:
                properties["Title"] = button.CurrentTitle;
                properties["Text"] = button.CurrentTitle;
                properties["Value"] = button.CurrentTitle;
                break;
            case UISwitch @switch:
                properties["On"] = @switch.On ? "True" : "False";
                properties["Value"] = @switch.On ? "True" : "False";
                properties["IsSelected"] = @switch.On ? "True" : "False";
                break;
        }

        return properties;
    }

    public static bool TrySetProperty(object viewObject, string name, string? value, out string? error)
    {
        error = null;
        var view = (UIView)viewObject;

        switch (name.ToLowerInvariant())
        {
            case "text":
            case "title":
                return TrySetText(view, value ?? string.Empty) || Fail($"'{view.GetType().Name}' has no text to set.", out error);
            case "alpha":
            case "opacity":
                if (!float.TryParse(value, out var alpha)) return Fail($"'{value}' is not a number.", out error);
                view.Alpha = alpha;
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
                if (view is not UIControl control) return Fail($"'{view.GetType().Name}' is not a control.", out error);
                if (!bool.TryParse(value, out var enabled)) return Fail($"'{value}' is not a boolean.", out error);
                control.Enabled = enabled;
                return true;
            case "on":
            case "ischecked":
                if (view is not UISwitch @switch) return Fail($"'{view.GetType().Name}' is not a switch.", out error);
                if (!bool.TryParse(value, out var on)) return Fail($"'{value}' is not a boolean.", out error);
                @switch.SetState(on, animated: false);
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
