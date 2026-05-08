using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.Profiling;
using Microsoft.Maui.DevFlow.Agent.Profiling;
#if IOS || MACCATALYST
using BackgroundTasks;
using Foundation;
#endif
#if MACOS
using AppKit;
using Foundation;
using ObjCRuntime;
#endif

namespace Microsoft.Maui.DevFlow.Agent;

/// <summary>
/// Platform-specific agent service that provides native tap and screenshot
/// implementations for Android, iOS, Mac Catalyst, Windows, and macOS AppKit.
/// </summary>
public class PlatformAgentService : DevFlowAgentService
{
    public PlatformAgentService(AgentOptions? options = null) : base(options) { }

    protected override VisualTreeWalker CreateTreeWalker() => new PlatformVisualTreeWalker();

    protected override double GetWindowDisplayDensity(IWindow? window)
    {
        try
        {
#if IOS || MACCATALYST
            if (window?.Handler?.PlatformView is UIKit.UIWindow uiWindow)
                return uiWindow.Screen.Scale;
            return UIKit.UIScreen.MainScreen.Scale;
#elif ANDROID
            if (window?.Handler?.PlatformView is global::Android.App.Activity activity)
                return activity.Resources?.DisplayMetrics?.Density ?? 1.0;
            if (global::Android.App.Application.Context.Resources?.DisplayMetrics is global::Android.Util.DisplayMetrics dm)
                return dm.Density;
            return 1.0;
#elif WINDOWS
            if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window winuiWindow)
            {
                var xamlRoot = winuiWindow.Content?.XamlRoot;
                if (xamlRoot != null)
                    return xamlRoot.RasterizationScale;
            }
            return 1.0;
#elif MACOS
            if (window?.Handler?.PlatformView is AppKit.NSWindow nsWindow)
                return nsWindow.BackingScaleFactor;
            return AppKit.NSScreen.MainScreen?.BackingScaleFactor ?? 2.0;
#else
            return base.GetWindowDisplayDensity(window);
#endif
        }
        catch
        {
            return base.GetWindowDisplayDensity(window);
        }
    }

    protected override Task<bool> TryNativeScroll(VisualElement element, double deltaX, double deltaY)
    {
        try
        {
            // Walk up from the element to find a native scrollable view
            var target = element;
            while (target != null)
            {
                var platformView = target.Handler?.PlatformView;
                if (platformView != null)
                {
#if IOS || MACCATALYST
                    // Check: view itself → subviews → ancestors
                    var uiView = platformView as UIKit.UIView;
                    UIKit.UIScrollView? uiScrollView = uiView as UIKit.UIScrollView;
                    if (uiScrollView == null)
                        uiScrollView = FindNativeDescendant<UIKit.UIScrollView>(uiView);
                    if (uiScrollView == null)
                        uiScrollView = FindNativeAncestor<UIKit.UIScrollView>(uiView);
                    if (uiScrollView != null)
                    {
                        var offset = uiScrollView.ContentOffset;
                        var newX = Math.Max(0, Math.Min(offset.X + deltaX, uiScrollView.ContentSize.Width - uiScrollView.Bounds.Width));
                        var newY = Math.Max(0, Math.Min(offset.Y - deltaY, uiScrollView.ContentSize.Height - uiScrollView.Bounds.Height));
                        uiScrollView.SetContentOffset(new CoreGraphics.CGPoint(newX, newY), animated: true);
                        return Task.FromResult(true);
                    }
#elif ANDROID
                    // Check: view itself → descendants → ancestors
                    var androidView = platformView as global::Android.Views.View;
                    var recyclerView = androidView as global::AndroidX.RecyclerView.Widget.RecyclerView;
                    if (recyclerView == null)
                        recyclerView = FindNativeDescendantAndroid<global::AndroidX.RecyclerView.Widget.RecyclerView>(androidView);
                    if (recyclerView == null)
                        recyclerView = FindNativeAncestorAndroid<global::AndroidX.RecyclerView.Widget.RecyclerView>(androidView);
                    if (recyclerView != null)
                    {
                        recyclerView.ScrollBy((int)deltaX, (int)-deltaY);
                        return Task.FromResult(true);
                    }
                    var androidScrollView = androidView as global::Android.Widget.ScrollView;
                    if (androidScrollView == null)
                        androidScrollView = FindNativeDescendantAndroid<global::Android.Widget.ScrollView>(androidView);
                    if (androidScrollView == null)
                        androidScrollView = FindNativeAncestorAndroid<global::Android.Widget.ScrollView>(androidView);
                    if (androidScrollView != null)
                    {
                        androidScrollView.ScrollBy((int)deltaX, (int)-deltaY);
                        return Task.FromResult(true);
                    }
#elif WINDOWS
                    // Check: view itself → descendants → ancestors
                    var winView = platformView as Microsoft.UI.Xaml.DependencyObject;
                    var scrollViewer = winView as Microsoft.UI.Xaml.Controls.ScrollViewer;
                    if (scrollViewer == null)
                        scrollViewer = FindWinUIDescendant<Microsoft.UI.Xaml.Controls.ScrollViewer>(winView);
                    if (scrollViewer == null)
                        scrollViewer = FindWinUIScrollViewer(winView);
                    if (scrollViewer != null)
                    {
                        scrollViewer.ChangeView(
                            scrollViewer.HorizontalOffset + deltaX,
                            scrollViewer.VerticalOffset - deltaY,
                            null);
                        return Task.FromResult(true);
                    }
#endif
                }
                target = target.Parent as VisualElement;
            }
        }
        catch { }
        return Task.FromResult(false);
    }

    protected override bool TryNativeScrollOnPlatformView(object platformView, double deltaX, double deltaY)
    {
        try
        {
#if IOS || MACCATALYST
            var uiView = platformView as UIKit.UIView;
            UIKit.UIScrollView? uiScrollView = uiView as UIKit.UIScrollView;
            if (uiScrollView == null)
                uiScrollView = FindNativeDescendant<UIKit.UIScrollView>(uiView);
            if (uiScrollView == null)
                uiScrollView = FindNativeAncestor<UIKit.UIScrollView>(uiView);
            if (uiScrollView != null)
            {
                var offset = uiScrollView.ContentOffset;
                var newX = Math.Max(0, Math.Min(offset.X + deltaX, uiScrollView.ContentSize.Width - uiScrollView.Bounds.Width));
                var newY = Math.Max(0, Math.Min(offset.Y - deltaY, uiScrollView.ContentSize.Height - uiScrollView.Bounds.Height));
                uiScrollView.SetContentOffset(new CoreGraphics.CGPoint(newX, newY), animated: true);
                return true;
            }
#elif ANDROID
            var androidView = platformView as global::Android.Views.View;
            var recyclerView = androidView as global::AndroidX.RecyclerView.Widget.RecyclerView;
            if (recyclerView == null)
                recyclerView = FindNativeDescendantAndroid<global::AndroidX.RecyclerView.Widget.RecyclerView>(androidView);
            if (recyclerView == null)
                recyclerView = FindNativeAncestorAndroid<global::AndroidX.RecyclerView.Widget.RecyclerView>(androidView);
            if (recyclerView != null)
            {
                recyclerView.ScrollBy((int)deltaX, (int)-deltaY);
                return true;
            }
            var androidScrollView = androidView as global::Android.Widget.ScrollView;
            if (androidScrollView == null)
                androidScrollView = FindNativeDescendantAndroid<global::Android.Widget.ScrollView>(androidView);
            if (androidScrollView == null)
                androidScrollView = FindNativeAncestorAndroid<global::Android.Widget.ScrollView>(androidView);
            if (androidScrollView != null)
            {
                androidScrollView.ScrollBy((int)deltaX, (int)-deltaY);
                return true;
            }
#elif WINDOWS
            var winView = platformView as Microsoft.UI.Xaml.DependencyObject;
            var scrollViewer = winView as Microsoft.UI.Xaml.Controls.ScrollViewer;
            if (scrollViewer == null)
                scrollViewer = FindWinUIDescendant<Microsoft.UI.Xaml.Controls.ScrollViewer>(winView);
            if (scrollViewer == null)
                scrollViewer = FindWinUIScrollViewer(winView);
            if (scrollViewer != null)
            {
                scrollViewer.ChangeView(
                    scrollViewer.HorizontalOffset + deltaX,
                    scrollViewer.VerticalOffset - deltaY,
                    null);
                return true;
            }
#endif
        }
        catch { }
        return false;
    }

#if IOS || MACCATALYST
    private static T? FindNativeAncestor<T>(UIKit.UIView? view) where T : UIKit.UIView
    {
        var current = view;
        while (current != null)
        {
            if (current is T match) return match;
            current = current.Superview;
        }
        return null;
    }

    private static T? FindNativeDescendant<T>(UIKit.UIView? view) where T : UIKit.UIView
    {
        if (view == null) return null;
        if (view is T match) return match;
        foreach (var subview in view.Subviews)
        {
            var found = FindNativeDescendant<T>(subview);
            if (found != null) return found;
        }
        return null;
    }
#elif ANDROID
    private static T? FindNativeAncestorAndroid<T>(global::Android.Views.View? view) where T : global::Android.Views.View
    {
        var current = view;
        while (current != null)
        {
            if (current is T match) return match;
            current = current.Parent as global::Android.Views.View;
        }
        return null;
    }

    private static T? FindNativeDescendantAndroid<T>(global::Android.Views.View? view) where T : global::Android.Views.View
    {
        if (view == null) return null;
        if (view is T match) return match;
        if (view is global::Android.Views.ViewGroup vg)
        {
            for (var i = 0; i < vg.ChildCount; i++)
            {
                var found = FindNativeDescendantAndroid<T>(vg.GetChildAt(i));
                if (found != null) return found;
            }
        }
        return null;
    }
#elif WINDOWS
    private static Microsoft.UI.Xaml.Controls.ScrollViewer? FindWinUIScrollViewer(Microsoft.UI.Xaml.DependencyObject? obj)
    {
        if (obj == null) return null;
        if (obj is Microsoft.UI.Xaml.Controls.ScrollViewer sv) return sv;
        // Walk up the visual tree
        var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(obj);
        while (parent != null)
        {
            if (parent is Microsoft.UI.Xaml.Controls.ScrollViewer scrollViewer)
                return scrollViewer;
            parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
        }
        // Also search children (CollectionView wraps a ScrollViewer internally)
        return FindWinUIDescendant<Microsoft.UI.Xaml.Controls.ScrollViewer>(obj);
    }

    private static T? FindWinUIDescendant<T>(Microsoft.UI.Xaml.DependencyObject? parent) where T : Microsoft.UI.Xaml.DependencyObject
    {
        if (parent == null) return null;
        if (parent is T match) return match;
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var descendant = FindWinUIDescendant<T>(child);
            if (descendant != null) return descendant;
        }
        return null;
    }
#endif

    protected override IProfilerCollector CreateProfilerCollector()
    {
#if ANDROID || IOS || WINDOWS || MACCATALYST
        return new RuntimeProfilerCollector(NativeFrameStatsProviderFactory.Create());
#else
        return base.CreateProfilerCollector();
#endif
    }

    protected override bool IsJobsSupported
    {
        get
        {
#if ANDROID || IOS || MACCATALYST
            return true;
#else
            return base.IsJobsSupported;
#endif
        }
    }

    protected override bool IsJobRunSupported
    {
        get
        {
#if IOS || MACCATALYST
            return true;
#elif ANDROID
            return false;
#else
            return base.IsJobRunSupported;
#endif
        }
    }

    protected override async Task<object?> GetPlatformJobsAsync()
    {
#if ANDROID
        try
        {
            var context = global::Android.App.Application.Context;
            var wmClass = Java.Lang.Class.ForName("androidx.work.WorkManager");
            var getInstanceMethod = wmClass.GetMethod("getInstance", Java.Lang.Class.FromType(typeof(global::Android.Content.Context)));
            var wm = getInstanceMethod?.Invoke(null, context);
            if (wm == null)
                return new { platform = "Android", type = "WorkManager", supported = true, runSupported = false, error = "WorkManager not initialized", jobs = Array.Empty<object>() };

            // Build WorkQuery for all states
            var queryBuilderClass = Java.Lang.Class.ForName("androidx.work.WorkQuery$Builder");
            var stateClass = Java.Lang.Class.ForName("androidx.work.WorkInfo$State");

            var stateFields = new[] { "ENQUEUED", "RUNNING", "SUCCEEDED", "FAILED", "BLOCKED", "CANCELLED" };
            var stateList = new Java.Util.ArrayList();
            foreach (var fieldName in stateFields)
            {
                var field = stateClass.GetField(fieldName);
                var state = field?.Get(null);
                if (state != null)
                    stateList.Add(state);
            }

            var fromStatesMethod = queryBuilderClass.GetMethod("fromStates", Java.Lang.Class.FromType(typeof(Java.Util.IList)));
            var builder = fromStatesMethod?.Invoke(null, stateList);
            if (builder == null)
                return new { platform = "Android", type = "WorkManager", supported = true, runSupported = false, error = "Failed to create WorkQuery", jobs = Array.Empty<object>() };

            var buildMethod = builder.Class.GetMethod("build");
            var query = buildMethod?.Invoke(builder);

            var getWorkInfosMethod = wm.Class.GetMethod("getWorkInfos", Java.Lang.Class.ForName("androidx.work.WorkQuery"));
            var future = getWorkInfosMethod?.Invoke(wm, query!) as Java.Lang.Object;

            // ListenableFuture.get()
            var getMethod = future?.Class.GetMethod("get");
            var result = getMethod?.Invoke(future) as Java.Util.IList;

            var jobs = new List<object>();
            if (result != null)
            {
                var iterator = result.Iterator();
                while (iterator.HasNext)
                {
                    var info = iterator.Next()!;
                    var infoClass = info.Class;

                    var getId = infoClass.GetMethod("getId");
                    var getTags = infoClass.GetMethod("getTags");
                    var getState = infoClass.GetMethod("getState");
                    var getRunAttemptCount = infoClass.GetMethod("getRunAttemptCount");

                    var identifier = getId?.Invoke(info)?.ToString() ?? "";
                    var tags = new List<string>();
                    if (getTags?.Invoke(info) is Java.Util.ICollection tagSet)
                    {
                        var tagIter = tagSet.Iterator();
                        while (tagIter.HasNext)
                            tags.Add(tagIter.Next()?.ToString() ?? "");
                    }
                    var state = getState?.Invoke(info)?.ToString() ?? "";
                    var runAttemptCount = 0;
                    if (getRunAttemptCount?.Invoke(info) is Java.Lang.Integer countObj)
                        runAttemptCount = countObj.IntValue();

                    jobs.Add(new
                    {
                        identifier,
                        tags = tags.ToArray(),
                        state,
                        runAttemptCount
                    });
                }
            }

            return new { platform = "Android", type = "WorkManager", supported = true, runSupported = false, jobs };
        }
        catch (Exception ex)
        {
            return new { platform = "Android", type = "WorkManager", supported = true, runSupported = false, error = ex.Message, jobs = Array.Empty<object>() };
        }
#elif IOS || MACCATALYST
        try
        {
            var tcs = new TaskCompletionSource<object?>();
            BGTaskScheduler.Shared.GetPending((requests) =>
            {
                var jobs = new List<object>();
                foreach (var req in requests)
                {
                    var type = req is BGProcessingTaskRequest ? "processing" : "refresh";
                    jobs.Add(new
                    {
                        identifier = req.Identifier,
                        type,
                        earliestBeginDate = req.EarliestBeginDate?.ToString() ?? ""
                    });
                }
                tcs.TrySetResult(new { platform = "iOS", type = "BGTaskScheduler", supported = true, runSupported = true, jobs });
            });
            return await tcs.Task;
        }
        catch (Exception ex)
        {
            return new { platform = "iOS", type = "BGTaskScheduler", supported = true, runSupported = true, error = ex.Message, jobs = Array.Empty<object>() };
        }
#else
        return await base.GetPlatformJobsAsync();
#endif
    }

    protected override async Task<object?> RunPlatformJobAsync(string identifier, string? type = null)
    {
#if ANDROID
        return await Task.FromResult<object?>(new
        {
            success = false,
            supported = false,
            identifier,
            error = $"Running job '{identifier}' is not supported on Android because the original WorkManager worker type and request parameters cannot be reconstructed safely from the listed identifier or tags."
        });
#elif IOS || MACCATALYST
        try
        {
            var taskType = await ResolveBgTaskRequestTypeAsync(identifier, type);
            BGTaskRequest taskRequest = taskType.Equals("refresh", StringComparison.OrdinalIgnoreCase)
                ? new BGAppRefreshTaskRequest(identifier)
                : new BGProcessingTaskRequest(identifier);

            taskRequest.EarliestBeginDate = null;

            BGTaskScheduler.Shared.Submit(taskRequest, out var error);
            if (error != null)
                return new { success = false, error = error.LocalizedDescription, identifier };

            return await Task.FromResult<object?>(new
            {
                success = true,
                message = $"BGTask '{identifier}' submitted",
                identifier,
                type = taskType
            });
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message, identifier };
        }
#else
        return await base.RunPlatformJobAsync(identifier, type);
#endif
    }

#if IOS || MACCATALYST
    private static async Task<string> ResolveBgTaskRequestTypeAsync(string identifier, string? requestedType)
    {
        if (!string.IsNullOrWhiteSpace(requestedType))
        {
            if (requestedType.Equals("processing", StringComparison.OrdinalIgnoreCase))
                return "processing";
            if (requestedType.Equals("refresh", StringComparison.OrdinalIgnoreCase))
                return "refresh";

            throw new ArgumentException("BGTask type must be 'processing' or 'refresh'.", nameof(requestedType));
        }

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        BGTaskScheduler.Shared.GetPending((requests) =>
        {
            var pending = requests.FirstOrDefault(r =>
                string.Equals(r.Identifier, identifier, StringComparison.Ordinal));
            tcs.TrySetResult(pending is null ? null : pending is BGProcessingTaskRequest ? "processing" : "refresh");
        });

        return await tcs.Task ?? "processing";
    }
#endif

    protected override bool TryNativeTap(VisualElement ve)
    {
        try
        {
            var platformView = ve.Handler?.PlatformView;
            if (platformView == null) return false;

#if IOS || MACCATALYST
            if (platformView is UIKit.UIControl control)
            {
                control.SendActionForControlEvents(UIKit.UIControlEvent.TouchUpInside);
                return true;
            }
#elif ANDROID
            if (platformView is global::Android.Views.View androidView && androidView.Clickable)
            {
                androidView.PerformClick();
                return true;
            }
#elif MACOS
            if (platformView is NSButton button)
            {
                button.PerformClick(button);
                return true;
            }
            if (platformView is NSControl nsControl && nsControl.Action is Selector action)
            {
                nsControl.SendAction(action, nsControl.Target!);
                return true;
            }
#endif
        }
        catch { }
        return false;
    }

#if MACOS
    protected override async Task<byte[]?> CaptureScreenshotAsync(VisualElement rootElement)
    {
        try
        {
            // Get the window - try KeyWindow first, then find any visible window via MAUI
            var window = NSApplication.SharedApplication.KeyWindow;
            if (window == null)
            {
                var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
                if (mauiWindow?.Handler?.PlatformView is NSWindow nsWindow)
                    window = nsWindow;
            }

            // If a modal sheet is attached, capture it instead of the main window
            if (window?.AttachedSheet is NSWindow sheet)
                window = sheet;

            // Use CGWindowListCreateImage for composited capture including layer-backed controls
            if (window != null)
            {
                var pngBytes = CaptureWindowViaCG(window);
                if (pngBytes != null)
                    return pngBytes;
            }

            // Fallback: DataWithPdfInsideRect (misses layer-backed controls like NSButton, NSSlider)
            var contentView = window?.ContentView;
            if (contentView != null)
            {
                var bounds = contentView.Bounds;
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    var pdfData = contentView.DataWithPdfInsideRect(bounds);
                    if (pdfData != null)
                    {
                        var image = new NSImage(pdfData);
                        var tiffData = image.AsTiff();
                        if (tiffData != null)
                        {
                            var bitmapRep = new NSBitmapImageRep(tiffData);
                            var pngData = bitmapRep.RepresentationUsingTypeProperties(
                                NSBitmapImageFileType.Png, new NSDictionary());
                            return pngData?.ToArray();
                        }
                    }
                }
            }
        }
        catch { }

        return await base.CaptureScreenshotAsync(rootElement);
    }

    protected override Task<byte[]?> CaptureElementScreenshotAsync(VisualElement element)
    {
        try
        {
            if (element.Handler?.PlatformView is NSView nsView)
            {
                var pngBytes = CaptureNSView(nsView);
                if (pngBytes != null)
                    return Task.FromResult<byte[]?>(pngBytes);
            }
        }
        catch { }

        return base.CaptureElementScreenshotAsync(element);
    }

    private static byte[]? CaptureNSView(NSView view)
    {
        var bounds = view.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        var scale = view.Window?.BackingScaleFactor ?? 2.0;
        var pixelWidth = (int)(bounds.Width * scale);
        var pixelHeight = (int)(bounds.Height * scale);

        var rep = new NSBitmapImageRep(
            IntPtr.Zero,
            pixelWidth,
            pixelHeight,
            8,       // bits per sample
            4,       // samples per pixel (RGBA)
            true,    // has alpha
            false,   // is planar
            NSColorSpace.DeviceRGB,
            0,       // bytes per row (auto)
            0);      // bits per pixel (auto)

        if (rep == null)
            return null;

        rep.Size = new CoreGraphics.CGSize(bounds.Width, bounds.Height);

        NSGraphicsContext.GlobalSaveGraphicsState();
        try
        {
            var context = NSGraphicsContext.FromBitmap(rep);
            if (context == null)
                return null;

            NSGraphicsContext.CurrentContext = context;
            view.CacheDisplay(bounds, rep);
        }
        finally
        {
            NSGraphicsContext.GlobalRestoreGraphicsState();
        }

        var pngData = rep.RepresentationUsingTypeProperties(
            NSBitmapImageFileType.Png, new NSDictionary());
        return pngData?.ToArray();
    }

    [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    static extern IntPtr CGWindowListCreateImage(
        CoreGraphics.CGRect screenBounds,
        uint listOption,
        uint windowID,
        uint imageOption);

    private static byte[]? CaptureWindowViaCG(NSWindow window)
    {
        try
        {
            // kCGWindowListOptionIncludingWindow = 0x08, kCGWindowImageBoundsIgnoreFraming = 0x01
            var cgImagePtr = CGWindowListCreateImage(
                CoreGraphics.CGRect.Null, 0x08, (uint)window.WindowNumber, 0x01);

            if (cgImagePtr == IntPtr.Zero)
                return null;

            var cgImage = ObjCRuntime.Runtime.GetINativeObject<CoreGraphics.CGImage>(
                cgImagePtr, owns: true);
            if (cgImage == null)
                return null;

            var bitmapRep = new NSBitmapImageRep(cgImage);
            var pngData = bitmapRep.RepresentationUsingTypeProperties(
                NSBitmapImageFileType.Png, new NSDictionary());
            return pngData?.ToArray();
        }
        catch
        {
            return null;
        }
    }
#elif IOS || MACCATALYST
    protected override async Task<byte[]?> CaptureScreenshotAsync(VisualElement rootElement)
    {
        var pngBytes = await DispatchAsync(() => CaptureAllWindowsComposited());
        if (pngBytes != null)
            return pngBytes;
        return await base.CaptureScreenshotAsync(rootElement);
    }

    protected override Task<byte[]?> CaptureFullScreenAsync()
        => DispatchAsync(() => CaptureAllWindowsComposited());

    /// <summary>
    /// Composites all visible UIWindows in the active UIWindowScene into a single PNG.
    /// This captures native overlays such as UIAlertController dialogs that live in
    /// their own UIWindow at an elevated WindowLevel, which VisualDiagnostics misses.
    /// </summary>
    private static byte[]? CaptureAllWindowsComposited()
    {
        // Find the foreground UIWindowScene (the one the user is interacting with)
        UIKit.UIWindowScene? windowScene = null;
        foreach (var scene in UIKit.UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is UIKit.UIWindowScene ws &&
                ws.ActivationState == UIKit.UISceneActivationState.ForegroundActive)
            {
                windowScene = ws;
                break;
            }
        }

        // Fall back to any connected window scene if no active foreground scene found
        if (windowScene == null)
        {
            foreach (var scene in UIKit.UIApplication.SharedApplication.ConnectedScenes)
            {
                if (scene is UIKit.UIWindowScene ws)
                {
                    windowScene = ws;
                    break;
                }
            }
        }

        if (windowScene == null)
            return null;

        var screen = windowScene.Screen;
        var screenBounds = screen.Bounds;

        // Collect all visible windows sorted by WindowLevel ascending (back → front)
        // so that alert/dialog windows (WindowLevel ~2000) are drawn on top of the app window (level 0)
        var windows = new System.Collections.Generic.List<UIKit.UIWindow>();
        foreach (var w in windowScene.Windows)
        {
            if (!w.Hidden && w.Alpha > 0f)
                windows.Add(w);
        }
        windows.Sort((a, b) => ((double)a.WindowLevel).CompareTo((double)b.WindowLevel));

        if (windows.Count == 0)
            return null;

        using var format = new UIKit.UIGraphicsImageRendererFormat { Scale = screen.Scale };
        using var renderer = new UIKit.UIGraphicsImageRenderer(screenBounds, format);

        using var image = renderer.CreateImage(ctx =>
        {
            foreach (var window in windows)
            {
                // Translate the graphics context to the window's screen origin so that
                // DrawViewHierarchy (which draws in local/Bounds coordinates) is composited
                // at the correct position. Using window.Frame here would pass screen coordinates
                // as the draw rect, which can shift/crop non-fullscreen windows.
                ctx.CGContext.TranslateCTM(window.Frame.X, window.Frame.Y);
                window.DrawViewHierarchy(window.Bounds, afterScreenUpdates: false);
                ctx.CGContext.TranslateCTM(-window.Frame.X, -window.Frame.Y);
            }
        });

        using var pngData = image.AsPNG();
        return pngData?.ToArray();
    }
#elif WINDOWS
    protected override async Task<byte[]?> CaptureScreenshotAsync(VisualElement rootElement)
    {
        // MAUI's VisualDiagnostics doesn't capture WebView2 GPU-rendered content on Windows.
        // When a WebView2 is present, use CoreWebView2.CapturePreviewAsync instead.
        try
        {
            var wv2 = FindPlatformWebView2(rootElement);
            if (wv2?.CoreWebView2 != null)
            {
                using var ras = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
                await wv2.CoreWebView2.CapturePreviewAsync(
                    Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, ras);
                var reader = new global::Windows.Storage.Streams.DataReader(ras.GetInputStreamAt(0));
                await reader.LoadAsync((uint)ras.Size);
                var bytes = new byte[ras.Size];
                reader.ReadBytes(bytes);
                return bytes;
            }
        }
        catch { }

        return await base.CaptureScreenshotAsync(rootElement);
    }

    private static Microsoft.UI.Xaml.Controls.WebView2? FindPlatformWebView2(Element element)
    {
        if (element is View view && view.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 wv2)
            return wv2;
        // Shell doesn't expose pages via Content/Children — use CurrentPage
        if (element is Shell shell && shell.CurrentPage != null)
        {
            var found = FindPlatformWebView2(shell.CurrentPage);
            if (found != null) return found;
        }
        if (element is ContentPage page && page.Content != null)
        {
            var found = FindPlatformWebView2(page.Content);
            if (found != null) return found;
        }
        if (element is Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child is Element childElement)
                {
                    var found = FindPlatformWebView2(childElement);
                    if (found != null) return found;
                }
            }
        }
        return null;
    }
#endif
}
