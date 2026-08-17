#if ANDROID
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Agent.Native;

/// <summary>
/// Android <c>Android.Views.View</c> backend for the native DevFlow agent.
/// </summary>
internal static partial class NativeUi
{
    public static string PlatformName => "Android";

    public static string UiFrameworkName => "android-views";

    public static string DeviceTypeName =>
        Build.Fingerprint?.Contains("generic", StringComparison.OrdinalIgnoreCase) == true ? "virtual" : "physical";

    public static string? DeviceIdentifier => null;

    private static Activity? _currentActivity;

    /// <summary>
    /// The activity the agent walks. Set by <see cref="DevFlowAgent"/> at bootstrap and refreshed
    /// on every resume so the tree always reflects the foreground activity.
    /// </summary>
    /// <remarks>
    /// Read from HTTP handler threads but written on the UI thread, so access goes through
    /// Volatile.Read/Volatile.Write to keep a rebind visible on weakly ordered hardware. The
    /// activity is held strongly and replaced on each bind, so only the most recent one is retained;
    /// a strong hold is intentional so an activity collected mid-session cannot leave the agent
    /// walking an empty tree on a diagnostic build.
    /// </remarks>
    public static Activity? CurrentActivity
    {
        get => Volatile.Read(ref _currentActivity);
        set => Volatile.Write(ref _currentActivity, value);
    }

    public static IAgentDispatcher CreateDispatcher()
    {
        var handler = new Handler(Looper.MainLooper!);
        return new DelegateAgentDispatcher(
            () => Looper.MyLooper() != Looper.MainLooper,
            action => handler.Post(action));
    }

    public static double DisplayDensity
        => CurrentActivity?.Resources?.DisplayMetrics?.Density ?? 1.0;

    public static IReadOnlyList<object> GetRoots()
    {
        var activity = CurrentActivity;
        if (activity == null) return [];

        var roots = new List<object>();
        if (activity.Window?.DecorView is { } decor)
            roots.Add(decor);

        return roots;
    }

    public static (double Width, double Height) GetWindowSize()
    {
        var metrics = CurrentActivity?.Resources?.DisplayMetrics;
        if (metrics == null) return (0, 0);

        return (metrics.WidthPixels / metrics.Density, metrics.HeightPixels / metrics.Density);
    }

    public static IReadOnlyList<object> GetChildren(object view)
    {
        if (view is not ViewGroup group) return [];

        var children = new List<object>(group.ChildCount);
        for (int i = 0; i < group.ChildCount; i++)
        {
            if (group.GetChildAt(i) is { } child)
                children.Add(child);
        }

        return children;
    }

    public static NativeViewDescriptor Describe(object viewObject)
    {
        var view = (View)viewObject;
        var density = DisplayDensity;
        var type = view.GetType();

        // GetLocationInWindow (not GetLocationOnScreen) so bounds land in DevFlow's advertised
        // window-logical coordinate space — relative to this window's own origin, not the
        // screen. Using screen coordinates here would desync hit testing and Inspector overlays
        // from tap/screenshot coordinates the moment the window isn't at the screen origin (e.g.
        // multi-window/free-form mode on large-screen and ChromeOS devices).
        var location = new int[2];
        view.GetLocationInWindow(location);

        var descriptor = new NativeViewDescriptor
        {
            Type = type.Name,
            FullType = type.FullName ?? type.Name,
            AutomationId = GetAutomationId(view),
            AccessibilityLabel = view.ContentDescription,
            IsVisible = view.Visibility == ViewStates.Visible,
            IsEnabled = view.Enabled,
            IsFocused = view.IsFocused,
            IsSelected = view.Selected,
            Opacity = view.Alpha,
            X = location[0] / density,
            Y = location[1] / density,
            Width = view.Width / density,
            Height = view.Height / density,
            IsScrollable = view is ScrollView or HorizontalScrollView or AbsListView
                || IsNamedType(view, "RecyclerView") || IsNamedType(view, "NestedScrollView"),
            IsTappable = view.Clickable,
        };

        switch (view)
        {
            case EditText edit:
                descriptor.Text = edit.Text;
                descriptor.Value = edit.Text;
                descriptor.IsTextInput = true;
                descriptor.Properties = new Dictionary<string, string?>
                {
                    ["hint"] = edit.Hint,
                };
                break;
            case CompoundButton toggle:
                descriptor.Text = toggle.Text;
                descriptor.Value = toggle.Checked ? "true" : "false";
                descriptor.IsSelected = toggle.Checked;
                descriptor.Properties = new Dictionary<string, string?>
                {
                    ["checked"] = toggle.Checked ? "true" : "false",
                };
                break;
            case TextView text:
                descriptor.Text = text.Text;
                break;
            case ImageView:
                descriptor.Text = view.ContentDescription;
                break;
        }

        descriptor.Text ??= view.ContentDescription;
        return descriptor;
    }

    /// <summary>
    /// Walks the base-type chain looking for a type name. Used for AndroidX widgets so this package
    /// does not have to take a hard dependency on AndroidX just to recognise them.
    /// </summary>
    private static bool IsNamedType(View view, string typeName)
    {
        for (var type = view.GetType(); type != null; type = type.BaseType)
        {
            if (type.Name == typeName) return true;
        }

        return false;
    }

    private static string? GetAutomationId(View view)
    {
        if (view.Tag?.ToString() is { Length: > 0 } tag)
            return tag;

        if (view.Id is int id and not View.NoId)
        {
            try
            {
                return view.Resources?.GetResourceEntryName(id);
            }
            catch (global::Android.Content.Res.Resources.NotFoundException)
            {
                // Generated ids have no resource entry.
            }
        }

        return null;
    }

    public static bool TryTap(object viewObject, double? x, double? y)
    {
        var view = (View)viewObject;

        if (x.HasValue && y.HasValue)
            return DispatchSyntheticTouch(view, x.Value, y.Value);

        if (view.PerformClick()) return true;
        if (view.CallOnClick()) return true;

        // Fall back to a synthesized touch at the centre so views that only wire up
        // OnTouchListener (rather than OnClickListener) still respond.
        return DispatchSyntheticTouch(view, view.Width / 2.0, view.Height / 2.0);
    }

    private static bool DispatchSyntheticTouch(View view, double localX, double localY)
    {
        var now = SystemClock.UptimeMillis();
        var down = MotionEvent.Obtain(now, now, MotionEventActions.Down, (float)localX, (float)localY, 0);
        var up = MotionEvent.Obtain(now, now + 50, MotionEventActions.Up, (float)localX, (float)localY, 0);

        try
        {
            var handled = view.DispatchTouchEvent(down);
            handled |= view.DispatchTouchEvent(up);
            return handled;
        }
        finally
        {
            down?.Recycle();
            up?.Recycle();
        }
    }

    public static bool TrySetText(object viewObject, string text)
    {
        if (viewObject is EditText edit)
        {
            edit.Text = text;
            edit.SetSelection(text.Length);
            return true;
        }

        if (viewObject is TextView textView)
        {
            textView.Text = text;
            return true;
        }

        return false;
    }

    public static bool TryFocus(object viewObject)
    {
        var view = (View)viewObject;
        view.FocusableInTouchMode = true;
        return view.RequestFocus();
    }

    public static bool TrySendKey(object? viewObject, string? key, string? text, out string? error)
    {
        error = null;

        var view = (viewObject ?? CurrentActivity?.CurrentFocus) as View;
        if (view == null)
        {
            // Parity with the MAUI agent, whose key handler returns "ok" for a null element: a key
            // with no target is a no-op success, not a client error.
            if (viewObject == null) return true;

            error = "No target view: pass elementId or focus a view first";
            return false;
        }

        view.FocusableInTouchMode = true;
        view.RequestFocus();

        if (view is EditText edit)
        {
            var insertion = !string.IsNullOrEmpty(text)
                ? text
                : key is { Length: 1 } ? key : null;

            if (insertion != null)
            {
                InsertText(edit, insertion);
                return true;
            }

            if (string.Equals(key, "Backspace", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                DeleteBeforeCursor(edit);
                return true;
            }

            if (string.Equals(key, "Enter", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "Return", StringComparison.OrdinalIgnoreCase))
            {
                InsertText(edit, "\n");
                return true;
            }
        }

        if (!TryMapKeyCode(key, out var keyCode))
        {
            error = $"Key '{key}' is not mapped on Android";
            return false;
        }

        var now = SystemClock.UptimeMillis();
        using var down = new KeyEvent(now, now, KeyEventActions.Down, keyCode, 0);
        using var up = new KeyEvent(now, now + 50, KeyEventActions.Up, keyCode, 0);
        var handled = view.DispatchKeyEvent(down);
        handled |= view.DispatchKeyEvent(up);
        if (!handled)
            error = $"Key '{key}' was not handled by the target view";
        return handled;
    }

    public static bool TryGesture(object? viewObject, string? type, string? direction, double distance, int durationMs, out string? error)
    {
        error = null;
        var normalizedType = string.IsNullOrWhiteSpace(type) ? "swipe" : type.Trim().ToLowerInvariant();
        if (normalizedType == "tap")
        {
            if (viewObject == null)
            {
                error = "elementId is required to tap";
                return false;
            }

            return TryTap(viewObject, null, null);
        }

        if (normalizedType is not ("swipe" or "pan" or "scroll"))
        {
            error = $"Gesture '{type}' is not supported on Android";
            return false;
        }

        var logicalDistance = distance > 0 ? distance : 120;
        var pixelDistance = logicalDistance * DisplayDensity;
        var (dx, dy) = (direction ?? "up").Trim().ToLowerInvariant() switch
        {
            "down" => (0d, pixelDistance),
            "left" => (-pixelDistance, 0d),
            "right" => (pixelDistance, 0d),
            _ => (0d, -pixelDistance),
        };

        if (viewObject is View view && DispatchSyntheticSwipe(view, dx, dy, durationMs))
            return true;

        return (direction ?? "up").Trim().ToLowerInvariant() switch
        {
            "down" => TryScrollBy(viewObject, 0, -logicalDistance),
            "left" => TryScrollBy(viewObject, logicalDistance, 0),
            "right" => TryScrollBy(viewObject, -logicalDistance, 0),
            _ => TryScrollBy(viewObject, 0, logicalDistance),
        };
    }

    private static void InsertText(EditText edit, string text)
    {
        var current = edit.Text ?? string.Empty;
        var start = Math.Clamp(edit.SelectionStart, 0, current.Length);
        var end = Math.Clamp(edit.SelectionEnd, 0, current.Length);
        if (end < start) (start, end) = (end, start);

        var updated = current.Remove(start, end - start).Insert(start, text);
        edit.Text = updated;
        edit.SetSelection(start + text.Length);
    }

    private static void DeleteBeforeCursor(EditText edit)
    {
        var current = edit.Text ?? string.Empty;
        var start = Math.Clamp(edit.SelectionStart, 0, current.Length);
        var end = Math.Clamp(edit.SelectionEnd, 0, current.Length);
        if (end < start) (start, end) = (end, start);

        if (start == end && start > 0)
            start--;

        var updated = current.Remove(start, end - start);
        edit.Text = updated;
        edit.SetSelection(start);
    }

    private static bool TryMapKeyCode(string? key, out Keycode keyCode)
    {
        keyCode = Keycode.Unknown;
        if (string.IsNullOrWhiteSpace(key)) return false;

        keyCode = key.Trim().ToLowerInvariant() switch
        {
            "enter" or "return" => Keycode.Enter,
            "tab" => Keycode.Tab,
            "space" => Keycode.Space,
            "escape" or "esc" => Keycode.Escape,
            "backspace" or "delete" => Keycode.Del,
            _ => Keycode.Unknown,
        };

        if (keyCode != Keycode.Unknown) return true;

        if (key.Length == 1 && char.IsLetter(key[0])
            && Enum.TryParse<Keycode>(char.ToUpperInvariant(key[0]).ToString(), out var letter))
        {
            keyCode = letter;
            return true;
        }

        if (key.Length == 1 && char.IsDigit(key[0])
            && Enum.TryParse<Keycode>($"Num{key[0]}", out var digit))
        {
            keyCode = digit;
            return true;
        }

        return false;
    }

    private static bool DispatchSyntheticSwipe(View view, double deltaX, double deltaY, int durationMs)
    {
        if (view.Width <= 1 || view.Height <= 1) return false;

        var startX = ClampToView(view.Width / 2.0 - deltaX / 2.0, view.Width);
        var startY = ClampToView(view.Height / 2.0 - deltaY / 2.0, view.Height);
        var endX = ClampToView(view.Width / 2.0 + deltaX / 2.0, view.Width);
        var endY = ClampToView(view.Height / 2.0 + deltaY / 2.0, view.Height);
        var now = SystemClock.UptimeMillis();
        var duration = Math.Max(1, durationMs);
        var handled = false;

        using (var down = MotionEvent.Obtain(now, now, MotionEventActions.Down, (float)startX, (float)startY, 0))
            handled |= view.DispatchTouchEvent(down);

        const int steps = 4;
        for (var i = 1; i <= steps; i++)
        {
            var t = i / (double)(steps + 1);
            using var move = MotionEvent.Obtain(
                now,
                now + (long)(duration * t),
                MotionEventActions.Move,
                (float)(startX + (endX - startX) * t),
                (float)(startY + (endY - startY) * t),
                0);
            handled |= view.DispatchTouchEvent(move);
        }

        using (var up = MotionEvent.Obtain(now, now + duration, MotionEventActions.Up, (float)endX, (float)endY, 0))
            handled |= view.DispatchTouchEvent(up);

        return handled;
    }

    private static double ClampToView(double value, int size)
        => Math.Clamp(value, 1, Math.Max(1, size - 1));

    public static bool TryScrollBy(object? viewObject, double dx, double dy)
    {
        if (ResolveScrollTarget(viewObject) is not { } view) return false;

        var density = DisplayDensity;
        var px = (int)Math.Round(dx * density);
        var py = (int)Math.Round(dy * density);

        switch (view)
        {
            case ScrollView scroll:
                scroll.SmoothScrollBy(px, py);
                return true;
            case HorizontalScrollView horizontal:
                horizontal.SmoothScrollBy(px, py);
                return true;
            case AbsListView list:
                list.SmoothScrollBy(py, 250);
                return true;
            default:
                // AndroidX RecyclerView / NestedScrollView both expose SmoothScrollBy(int, int).
                var smoothScroll = view.GetType().GetMethod("SmoothScrollBy", [typeof(int), typeof(int)]);
                if (smoothScroll != null)
                {
                    smoothScroll.Invoke(view, [px, py]);
                    return true;
                }

                view.ScrollBy(px, py);
                return true;
        }
    }

    /// <summary>
    /// Resolves the view a scroll/swipe should act on: a real scrolling container if one is the
    /// target or encloses it, else — when no view was named — the first scroller on screen. Mirrors
    /// the MAUI agent's <c>FindAncestor&lt;ItemsView&gt;</c> so scrolling by naming a list's child works.
    /// Falls back to the named view so the generic <see cref="View.ScrollBy"/> path still applies.
    /// </summary>
    static View? ResolveScrollTarget(object? viewObject)
        => (FindSelfOrAncestor(viewObject, IsScrollable, static candidate => (candidate as View)?.Parent as View)
            ?? viewObject) as View;

    static bool IsScrollable(object candidate)
        => candidate is ScrollView or HorizontalScrollView or AbsListView
            // AndroidX RecyclerView / NestedScrollView are not in this binding's type universe.
            || candidate.GetType().GetMethod("SmoothScrollBy", [typeof(int), typeof(int)]) != null;

    public static bool TryScrollIntoView(object viewObject)
    {
        var view = (View)viewObject;
        return view.RequestRectangleOnScreen(new Rect(0, 0, view.Width, view.Height), true);
    }

    public static bool TryGoBack()
    {
        var activity = CurrentActivity;
        if (activity == null) return false;

        // Activity.OnBackPressed is obsoleted from API 33 in favour of AndroidX's
        // ComponentActivity.OnBackPressedDispatcher. This package deliberately takes no AndroidX
        // dependency — it has to work in plain .NET Android apps whose activity may be a bare
        // Android.App.Activity — and the platform offers no non-AndroidX way to request back.
        // The API is deprecated, not removed, and still dispatches back on current Android.
#pragma warning disable CA1422
        activity.OnBackPressed();
#pragma warning restore CA1422
        return true;
    }

    public static byte[]? CaptureView(object viewObject)
    {
        var view = (View)viewObject;
        if (view.Width <= 0 || view.Height <= 0) return null;

        using var bitmap = Bitmap.CreateBitmap(view.Width, view.Height, Bitmap.Config.Argb8888!);
        using var canvas = new Canvas(bitmap);

        if (view.Background is { } background)
            background.Draw(canvas);
        else
            canvas.DrawColor(global::Android.Graphics.Color.White);

        view.Draw(canvas);

        using var stream = new MemoryStream();
        bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);
        return stream.ToArray();
    }

    public static byte[]? CaptureScreen()
    {
        var decor = CurrentActivity?.Window?.DecorView;
        return decor == null ? null : CaptureView(decor);
    }

    public static IReadOnlyDictionary<string, string?> GetProperties(object viewObject)
    {
        var view = (View)viewObject;
        var density = DisplayDensity;

        var properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Alpha"] = view.Alpha.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Enabled"] = view.Enabled ? "True" : "False",
            ["Visibility"] = view.Visibility.ToString(),
            ["Width"] = (view.Width / density).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Height"] = (view.Height / density).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ScrollX"] = (view.ScrollX / density).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ScrollY"] = (view.ScrollY / density).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ContentDescription"] = view.ContentDescription,
            ["Clickable"] = view.Clickable ? "True" : "False",
            ["Focused"] = view.IsFocused ? "True" : "False",
        };

        if (view is TextView text)
        {
            properties["Text"] = text.Text;
            properties["TextSize"] = (text.TextSize / density).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (view is CompoundButton toggle)
            properties["Checked"] = toggle.Checked ? "True" : "False";

        AddCanonicalAliases(
            properties,
            isVisible: view.Visibility == ViewStates.Visible,
            isEnabled: view.Enabled,
            opacity: view.Alpha,
            text: (view as TextView)?.Text,
            isChecked: (view as CompoundButton)?.Checked,
            accessibilityIdentifier: GetAutomationId(view),
            placeholder: (view as EditText)?.Hint);

        return properties;
    }

    public static bool TrySetProperty(object viewObject, string name, string? value, out string? error)
    {
        error = null;
        var view = (View)viewObject;

        switch (name.ToLowerInvariant())
        {
            case "text":
                return TrySetText(view, value ?? string.Empty) || Fail($"'{view.GetType().Name}' has no text to set.", out error);
            case "alpha":
            case "opacity":
                if (!float.TryParse(value, out var alpha)) return Fail($"'{value}' is not a number.", out error);
                view.Alpha = alpha;
                return true;
            case "enabled":
            case "isenabled":
                if (!bool.TryParse(value, out var enabled)) return Fail($"'{value}' is not a boolean.", out error);
                view.Enabled = enabled;
                return true;
            case "visibility":
            case "isvisible":
                if (bool.TryParse(value, out var visible))
                {
                    view.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
                    return true;
                }

                if (Enum.TryParse<ViewStates>(value, ignoreCase: true, out var state))
                {
                    view.Visibility = state;
                    return true;
                }

                return Fail($"'{value}' is not a visibility value.", out error);
            case "checked":
                if (view is not CompoundButton toggle) return Fail($"'{view.GetType().Name}' is not checkable.", out error);
                if (!bool.TryParse(value, out var isChecked)) return Fail($"'{value}' is not a boolean.", out error);
                toggle.Checked = isChecked;
                return true;
            default:
                return Fail($"Property '{name}' is not settable on '{view.GetType().Name}'.", out error);
        }
    }

    public static string? AppName
    {
        get
        {
            var context = CurrentActivity ?? (Context?)Application.Context;
            var info = context?.ApplicationInfo;
            return info == null ? null : context!.PackageManager?.GetApplicationLabel(info);
        }
    }

    public static string? AppPackageId => (CurrentActivity ?? (Context?)Application.Context)?.PackageName;

    public static string? AppVersion => GetPackageInfo()?.VersionName;

    public static string? AppBuild
    {
        get
        {
            var info = GetPackageInfo();
            if (info == null) return null;
            return OperatingSystem.IsAndroidVersionAtLeast(28)
                ? info.LongVersionCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
#pragma warning disable CA1422 // VersionCode is the only option below API 28
                : info.VersionCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
#pragma warning restore CA1422
        }
    }

    private static global::Android.Content.PM.PackageInfo? GetPackageInfo()
    {
        var context = CurrentActivity ?? (Context?)Application.Context;
        if (context?.PackageName is not { } package) return null;

        try
        {
            return context.PackageManager?.GetPackageInfo(package, 0);
        }
        catch (global::Android.Content.PM.PackageManager.NameNotFoundException)
        {
            return null;
        }
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}
#endif
