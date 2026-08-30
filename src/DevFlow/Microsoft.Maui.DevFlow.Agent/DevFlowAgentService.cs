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

    /// <summary>
    /// Where the app window sits on the physical display, in device-independent points.
    /// <para>
    /// Reported per platform because only the app can observe it, and it is the value that makes
    /// a visual-tree overlay line up with a full-screen device video frame. Returning null is
    /// always safe: consumers then assume the window fills the screen rather than guessing.
    /// </para>
    /// </summary>
    protected override (double x, double y)? GetWindowScreenOrigin(IWindow? window)
    {
        try
        {
#if IOS || MACCATALYST
            // A simulator's UIWindow frame is already in points and already relative to the
            // screen, so its origin is the answer directly.
            if (window?.Handler?.PlatformView is UIKit.UIWindow uiWindow)
            {
                var frame = uiWindow.Frame;
                return (frame.X, frame.Y);
            }
            return null;
#elif ANDROID
            if (window?.Handler?.PlatformView is global::Android.App.Activity activity)
            {
                var decor = activity.Window?.DecorView;
                var density = activity.Resources?.DisplayMetrics?.Density ?? 1.0;
                if (decor is null || density <= 0)
                    return null;

                // The DECOR view, not the content view. App-space coordinates on Android are
                // window-relative (the visual tree walker uses GetLocationInWindow), whose origin
                // is the decor view — so they already include the status bar inset. Measuring the
                // content view here would add that inset a second time and land every device tap
                // one status bar too low.
                var location = new int[2];
                decor.GetLocationOnScreen(location);
                return (location[0] / density, location[1] / density);
            }
            return null;
#elif WINDOWS || MACOS
            // Desktop windows are positioned by the user and there is no device frame to align
            // with, so the origin has no consumer.
            return null;
#else
            return base.GetWindowScreenOrigin(window);
#endif
        }
        catch
        {
            return null;
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
#elif MACOS
                    if (TryScrollMacOSView(platformView as NSView, deltaX, deltaY))
                        return Task.FromResult(true);
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
#elif MACOS
            if (TryScrollMacOSView(platformView as NSView, deltaX, deltaY))
                return true;
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

#if MACOS
    private static bool TryScrollMacOSView(NSView? view, double deltaX, double deltaY)
    {
        var scrollView = view as NSScrollView
            ?? FindMacOSDescendant<NSScrollView>(view)
            ?? FindMacOSAncestor<NSScrollView>(view);
        var documentView = scrollView?.DocumentView;
        if (scrollView is null || documentView is null)
            return false;

        var clipView = scrollView.ContentView;
        var current = clipView.Bounds.Location;
        var maxX = Math.Max(0d, (double)(documentView.Bounds.Width - clipView.Bounds.Width));
        var maxY = Math.Max(0d, (double)(documentView.Bounds.Height - clipView.Bounds.Height));
        var newX = Math.Clamp((double)current.X + deltaX, 0d, maxX);
        var yDelta = documentView.IsFlipped ? -deltaY : deltaY;
        var newY = Math.Clamp((double)current.Y + yDelta, 0d, maxY);

        clipView.ScrollToPoint(new CoreGraphics.CGPoint(newX, newY));
        scrollView.ReflectScrolledClipView(clipView);
        return true;
    }

    private static T? FindMacOSAncestor<T>(NSView? view) where T : NSView
    {
        var current = view?.Superview;
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = current.Superview;
        }
        return null;
    }

    private static T? FindMacOSDescendant<T>(NSView? view) where T : NSView
    {
        if (view is null)
            return null;
        foreach (var subview in view.Subviews)
        {
            if (subview is T match)
                return match;
            var nested = FindMacOSDescendant<T>(subview);
            if (nested is not null)
                return nested;
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
                return androidView.PerformClick();
#elif WINDOWS
            // Focus is not a tap. ButtonBase controls are handled by TryScheduleNativeTapFirst;
            // other controls must not terminate candidate fallthrough merely because they focused.
            return false;
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

    protected override bool TryScheduleNativeTapFirst(VisualElement ve)
    {
        try
        {
#if WINDOWS
            if (ve.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase buttonBase)
            {
                var peer =
                    Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(buttonBase) ??
                    Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(buttonBase);
                if (peer?.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke) is Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider invokeProvider)
                {
                    // Wrap the dispatched lambda so a stale/disabled element doesn't
                    // surface as CoreApplication.UnhandledErrorDetected and crash the
                    // host app. The TryEnqueue bool only reports whether the work
                    // item was queued, not whether the invoke itself succeeded.
                    return buttonBase.DispatcherQueue.TryEnqueue(() =>
                    {
                        try { invokeProvider.Invoke(); }
                        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                                                      or InvalidOperationException
                                                      or UnauthorizedAccessException)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] WinUI native invoke skipped: {ex.GetBaseException().Message}");
                        }
                    });
                }
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
            // Resolve the window without depending on the app being frontmost.
            // NSApplication.KeyWindow is null when the app is not the active application,
            // so prefer the NSWindow that actually owns the element being captured.
            var window = ResolveCaptureWindow(rootElement);

            // If a modal sheet is attached, capture it instead of the main window
            if (window?.AttachedSheet is NSWindow sheet)
                window = sheet;

            if (window != null)
            {
                // Primary: CGWindowListCreateImage gives a composited capture including
                // layer-backed controls and WebView content. This can return null when the
                // window is not frontmost / fully occluded (the window server may have purged
                // its backing store), so we fall through to occlusion-independent paths below.
                var pngBytes = CaptureWindowViaCG(window);
                if (pngBytes != null)
                    return pngBytes;

                var contentView = window.ContentView;
                if (contentView != null)
                {
                    // Occlusion-independent fallback: CacheDisplay re-renders the view
                    // hierarchy directly, so it works even when the window is not frontmost.
                    var cached = CaptureNSView(contentView);
                    if (cached != null)
                        return cached;

                    // Last resort: DataWithPdfInsideRect (misses layer-backed controls like NSButton, NSSlider)
                    var bounds = contentView.Bounds;
                    if (bounds.Width > 0 && bounds.Height > 0)
                    {
                        var pdfData = contentView.DataWithPdfInsideRect(bounds);
                        if (pdfData != null)
                        {
                            using var image = new NSImage(pdfData);
                            var tiffData = image.AsTiff();
                            if (tiffData != null)
                            {
                                using var bitmapRep = new NSBitmapImageRep(tiffData);
                                using var pngProperties = new NSDictionary();
                                var pngData = bitmapRep.RepresentationUsingTypeProperties(
                                    NSBitmapImageFileType.Png, pngProperties);
                                return pngData?.ToArray();
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return await base.CaptureScreenshotAsync(rootElement);
    }

    /// <summary>
    /// Resolves the NSWindow to capture without requiring the app to be frontmost.
    /// Prefers the window that owns the element being captured (via its NSView), then the
    /// MAUI application window's platform view, then key/main windows, and finally any
    /// visible on-screen window.
    /// </summary>
    private static NSWindow? ResolveCaptureWindow(VisualElement rootElement)
    {
        // 1. The NSWindow that actually hosts the element's native view (focus-independent).
        if (rootElement.Handler?.PlatformView is NSView elementView && elementView.Window is NSWindow ownerWindow)
            return ownerWindow;

        // 2. The MAUI application window's platform view.
        var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
        if (NSWindowFromPlatformView(mauiWindow?.Handler?.PlatformView) is NSWindow mauiNsWindow)
            return mauiNsWindow;

        // 3. Key / main window (only non-null when the app is active).
        var app = NSApplication.SharedApplication;
        if (app.KeyWindow is NSWindow keyWindow)
            return keyWindow;
        if (app.MainWindow is NSWindow mainWindow)
            return mainWindow;

        // 4. Any visible, on-screen window owned by the app (fall back to the first window).
        var appWindows = app.DangerousWindows;
        if (appWindows != null)
        {
            NSWindow? firstWindow = null;
            foreach (var candidate in appWindows)
            {
                if (candidate == null)
                    continue;
                firstWindow ??= candidate;
                if (candidate.IsVisible && candidate.WindowNumber > 0)
                    return candidate;
            }

            return firstWindow;
        }

        return null;
    }

    /// <summary>
    /// Extracts an NSWindow from a MAUI platform view, which may be an NSWindow directly,
    /// an NSView (use its Window), an NSWindowController, or an NSViewController.
    /// </summary>
    private static NSWindow? NSWindowFromPlatformView(object? platformView) => platformView switch
    {
        NSWindow window => window,
        NSView view => view.Window,
        NSWindowController controller => controller.Window,
        NSViewController viewController => viewController.View?.Window,
        _ => null
    };

    /// <summary>
    /// On macOS, reports an actionable cause when a screenshot fails because the app window
    /// is not the frontmost application (a common reason CGWindowListCreateImage returns null).
    /// </summary>
    protected override ScreenshotCaptureFailure? DescribeScreenshotFailure()
    {
        try
        {
            var app = NSApplication.SharedApplication;

            var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
            var nsWindow = NSWindowFromPlatformView(mauiWindow?.Handler?.PlatformView)
                ?? app.KeyWindow ?? app.MainWindow;

            var appActive = app.Active;
            var windowVisible = nsWindow == null || nsWindow.IsVisible;

            if (!appActive || !windowVisible)
            {
                return new ScreenshotCaptureFailure(
                    "Failed to capture screenshot because the app window is not frontmost (the app is not the active application). " +
                    "Bring the app to the foreground and retry.",
                    "window-not-frontmost",
                    retryable: true,
                    suggestions: new[]
                    {
                        "Bring the MAUI app window to the foreground (click it or use the app switcher / Cmd+Tab), then retry.",
                        "Ensure the app window is visible and not minimized."
                    });
            }
        }
        catch { }

        return null;
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
