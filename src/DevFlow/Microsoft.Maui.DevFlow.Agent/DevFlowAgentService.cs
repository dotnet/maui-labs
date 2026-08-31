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

#if ANDROID
    /// <summary>
    /// Whether androidx.work is actually on the app's classpath. WorkManager is an opt-in
    /// AndroidX dependency, not part of the platform, so an app that never referenced it has
    /// no jobs capability at all — reporting "supported" for such an app would be a lie that
    /// looks identical to "supported, but you have no jobs scheduled".
    /// Probed once; the classpath cannot change while the process is alive.
    /// </summary>
    private static readonly Lazy<bool> s_workManagerAvailable = new(() => FindAndroidClass("androidx.work.WorkManager") != null);

    /// <summary>
    /// Resolves a Java class by name using the *application's* class loader.
    ///
    /// <para>
    /// <c>Java.Lang.Class.ForName(string)</c> must not be used here. The single-argument
    /// overload resolves against the calling class's loader, and when the call originates
    /// from mono over JNI that is the boot class loader — which cannot see anything the app
    /// or its AndroidX dependencies contribute. It therefore throws ClassNotFoundException
    /// for <c>androidx.work.WorkManager</c> even in an app that plainly uses WorkManager.
    /// </para>
    /// </summary>
    /// <summary>
    /// Re-wraps a Java object as a bound interface.
    ///
    /// <para>
    /// <c>Java.Lang.Reflect.Method.Invoke</c> returns a plain <see cref="Java.Lang.Object"/>
    /// wrapper regardless of the real runtime type, so <c>as Java.Util.IList</c> and
    /// <c>is Java.Util.ICollection</c> always fail against it. Because those casts fail
    /// quietly, the caller reads an empty collection instead of an error — which is exactly
    /// how the jobs list came back empty while WorkManager plainly had work scheduled.
    /// Re-wrapping by JNI handle produces the correctly typed proxy.
    /// </para>
    /// </summary>
    private static T? AsJavaInterface<T>(Java.Lang.Object? value) where T : class, global::Android.Runtime.IJavaObject
    {
        if (value == null || value.Handle == IntPtr.Zero) return null;

        if (value is T alreadyTyped) return alreadyTyped;

        try
        {
            return Java.Lang.Object.GetObject<T>(value.Handle, global::Android.Runtime.JniHandleOwnership.DoNotTransfer);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Microsoft.Maui.DevFlow] Could not re-wrap {value.Class?.Name} as {typeof(T).Name}: {ex.GetBaseException().Message}");
            return null;
        }
    }

    /// <summary>
    /// WorkManager is present but the query could not be completed. Reported as supported with
    /// an explicit error, never as an empty job list — an empty list means "no work scheduled".
    /// </summary>
    private static object WorkManagerProblem(string error) => new
    {
        platform = "Android",
        type = "WorkManager",
        supported = true,
        runSupported = false,
        error,
        jobs = Array.Empty<object>()
    };

    private static Java.Lang.Class? FindAndroidClass(string className)
    {
        try
        {
            var loader = global::Android.App.Application.Context?.ClassLoader;
            if (loader == null) return null;
            return Java.Lang.Class.ForName(className, initialize: false, loader);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Microsoft.Maui.DevFlow] Java class '{className}' not resolvable: {ex.GetBaseException().Message}");
            return null;
        }
    }

    private const string WorkManagerMissingReason =
        "androidx.work.WorkManager is not on the app's classpath. Add the Xamarin.AndroidX.Work.Runtime " +
        "package (or a library that depends on it) to enable background job inspection.";
#endif

    protected override bool IsJobsSupported
    {
        get
        {
#if ANDROID
            // Gate on the real dependency rather than on the platform.
            return s_workManagerAvailable.Value;
#elif IOS || MACCATALYST
            // BGTaskScheduler is part of the OS, so the capability always exists —
            // whether any identifiers are registered is a separate question.
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
            // Triggering a BGTask needs a private selector that is compiled out of Release
            // builds (see TrySimulateBgTaskLaunch), so a Release agent can list and schedule
            // but never run. Advertising "run" there would hand callers a feature-detect that
            // is guaranteed to fail.
            return BgTaskRunSupported;
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
        // Distinguish "the app has no WorkManager" from "WorkManager returned no jobs".
        // Both produce an empty array, and conflating them hides a missing dependency
        // behind what looks like a healthy, idle queue.
        if (!s_workManagerAvailable.Value)
        {
            return new
            {
                platform = "Android",
                type = "WorkManager",
                supported = false,
                runSupported = false,
                reason = WorkManagerMissingReason,
                jobs = Array.Empty<object>()
            };
        }

        try
        {
            var context = global::Android.App.Application.Context;
            var wmClass = FindAndroidClass("androidx.work.WorkManager")!;
            var getInstanceMethod = wmClass.GetMethod("getInstance", Java.Lang.Class.FromType(typeof(global::Android.Content.Context)));
            var wm = getInstanceMethod?.Invoke(null, context);
            // Present but not initialized is a configuration problem, not a missing capability,
            // so this stays "supported" — the distinction tells you which thing to go fix.
            if (wm == null)
                return new { platform = "Android", type = "WorkManager", supported = true, runSupported = false, error = "WorkManager not initialized", jobs = Array.Empty<object>() };

            // Query every terminal and non-terminal state — WorkQuery requires at least one
            // filter, and the union of all states is the closest thing to "everything".
            var queryBuilderClass = FindAndroidClass("androidx.work.WorkQuery$Builder")!;
            var stateClass = FindAndroidClass("androidx.work.WorkInfo$State")!;

            var stateFields = new[] { "ENQUEUED", "RUNNING", "SUCCEEDED", "FAILED", "BLOCKED", "CANCELLED" };
            var stateList = new Java.Util.ArrayList();
            foreach (var fieldName in stateFields)
            {
                var state = stateClass.GetField(fieldName)?.Get(null);
                if (state != null)
                    stateList.Add(state);
            }

            if (stateList.Size() == 0)
                return WorkManagerProblem("Could not read any androidx.work.WorkInfo$State enum values");

            // The parameter type must be java.util.List. Class.FromType(typeof(Java.Util.IList))
            // does not reliably resolve to it, so look the Java type up by name.
            var listClass = FindAndroidClass("java.util.List");
            if (listClass == null)
                return WorkManagerProblem("Could not resolve java.util.List");

            var fromStatesMethod = queryBuilderClass.GetMethod("fromStates", listClass);
            var builder = fromStatesMethod?.Invoke(null, stateList);
            if (builder == null)
                return WorkManagerProblem("WorkQuery.Builder.fromStates returned null");

            var query = builder.Class.GetMethod("build")?.Invoke(builder);
            if (query == null)
                return WorkManagerProblem("WorkQuery.Builder.build returned null");

            var getWorkInfosMethod = wm.Class.GetMethod("getWorkInfos", FindAndroidClass("androidx.work.WorkQuery")!);
            var future = getWorkInfosMethod?.Invoke(wm, query);
            if (future == null)
                return WorkManagerProblem("WorkManager.getWorkInfos returned null");

            // ListenableFuture.get() blocks. Bound it here rather than via the
            // get(long, TimeUnit) overload, whose primitive `long` parameter cannot be
            // resolved with Class.FromType, and so a wedged future cannot hang the request.
            var getMethod = future.Class.GetMethod("get");
            if (getMethod == null)
                return WorkManagerProblem("ListenableFuture has no get() method");

            Java.Lang.Object? resultObject;
            try
            {
                resultObject = await Task.Run(() => getMethod.Invoke(future))
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                return WorkManagerProblem("Timed out after 5s waiting for WorkManager.getWorkInfos");
            }

            // Method.Invoke hands back a plain Java.Lang.Object wrapper, so `as Java.Util.IList`
            // always fails and the list silently reads as empty. Re-wrap by handle instead.
            var result = AsJavaInterface<Java.Util.IList>(resultObject);
            if (result == null)
                return WorkManagerProblem(
                    $"Could not read the work-info list (got {resultObject?.Class?.Name ?? "null"})");

            var jobs = new List<object>();
            var iterator = result.Iterator();
            while (iterator?.HasNext == true)
            {
                var info = iterator.Next();
                if (info is not Java.Lang.Object infoObj) continue;
                var infoClass = infoObj.Class;

                var identifier = infoClass.GetMethod("getId")?.Invoke(infoObj)?.ToString() ?? "";
                var state = infoClass.GetMethod("getState")?.Invoke(infoObj)?.ToString() ?? "";

                var tags = new List<string>();
                var tagSet = AsJavaInterface<Java.Util.ICollection>(infoClass.GetMethod("getTags")?.Invoke(infoObj));
                if (tagSet != null)
                {
                    var tagIter = tagSet.Iterator();
                    while (tagIter?.HasNext == true)
                        tags.Add(tagIter.Next()?.ToString() ?? "");
                }

                // Boxed primitives come back as opaque wrappers too, so parse the string form.
                var runAttemptCount = 0;
                var countValue = infoClass.GetMethod("getRunAttemptCount")?.Invoke(infoObj)?.ToString();
                if (countValue != null) int.TryParse(countValue, out runAttemptCount);

                jobs.Add(new
                {
                    identifier,
                    tags = tags.ToArray(),
                    state,
                    runAttemptCount
                });
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
                tcs.TrySetResult(new { platform = "iOS", type = "BGTaskScheduler", supported = true, runSupported = IsJobRunSupported, jobs });
            });
            return await tcs.Task;
        }
        catch (Exception ex)
        {
            return new { platform = "iOS", type = "BGTaskScheduler", supported = true, runSupported = IsJobRunSupported, error = ex.Message, jobs = Array.Empty<object>() };
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
            error = s_workManagerAvailable.Value
                ? $"Running job '{identifier}' is not supported on Android because the original WorkManager worker type and request parameters cannot be reconstructed safely from the listed identifier or tags."
                : WorkManagerMissingReason
        });
#elif IOS || MACCATALYST
        try
        {
            var taskType = await ResolveBgTaskRequestTypeAsync(identifier, type);

            // The request must be pending before it can be launched: iOS launches a *submitted*
            // task, and simulating a launch for an unscheduled identifier is a no-op.
            var wasPending = await IsBgTaskPendingAsync(identifier);
            string? submitError = null;
            if (!wasPending)
            {
                BGTaskRequest taskRequest = taskType.Equals("refresh", StringComparison.OrdinalIgnoreCase)
                    ? new BGAppRefreshTaskRequest(identifier)
                    : new BGProcessingTaskRequest(identifier);
                taskRequest.EarliestBeginDate = null;

                BGTaskScheduler.Shared.Submit(taskRequest, out var error);
                if (error != null)
                    submitError = $"{error.LocalizedDescription} {DescribeBgTaskSchedulerError(error, identifier)}".Trim();
            }

            if (submitError != null)
            {
                return new
                {
                    success = false,
                    identifier,
                    type = taskType,
                    error = $"Could not schedule BGTask '{identifier}': {submitError}"
                };
            }

            // Submitting only *schedules*; iOS decides when to launch, which organically may be
            // never. Forcing the launch is the only way to actually run the handler on demand.
            var launched = TrySimulateBgTaskLaunch(identifier, out var launchError);
            if (!launched)
            {
                return new
                {
                    success = false,
                    identifier,
                    type = taskType,
                    scheduled = true,
                    error = launchError
                };
            }

            // The scheduler consumes the pending request when it launches it, so the request
            // disappearing is the observable evidence that a launch really happened.
            await Task.Delay(1500);
            var stillPending = await IsBgTaskPendingAsync(identifier);

            return new
            {
                success = !stillPending,
                identifier,
                type = taskType,
                launched = true,
                consumedPendingRequest = !stillPending,
                message = stillPending
                    ? $"BGTask '{identifier}' launch was requested but the request is still pending; the handler may not be registered."
                    : $"BGTask '{identifier}' was launched and its pending request was consumed.",
                note = "iOS does not expose a task's result; assert on what the handler itself records."
            };
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
    /// <summary>
    /// Whether this build can actually trigger a BGTask. The only way to run one on demand is a
    /// private selector, which is compiled out of Release builds, so the advertised capability
    /// and <see cref="TrySimulateBgTaskLaunch"/> must be gated on the same condition.
    /// </summary>
#if DEBUG
    private const bool BgTaskRunSupported = true;
#else
    private const bool BgTaskRunSupported = false;
#endif

    /// <summary>
    /// Turns a BGTaskSchedulerErrorDomain code into the thing you actually need to go fix.
    /// The three codes have completely different causes and conflating them sends people
    /// looking in the wrong place.
    /// </summary>
    private static string DescribeBgTaskSchedulerError(NSError error, string identifier)
    {
        if (error.Domain != "BGTaskSchedulerErrorDomain")
            return string.Empty;

        return error.Code switch
        {
            // BGTaskSchedulerErrorCodeUnavailable
            1 => "Background task scheduling is unavailable. BGTaskScheduler does not work on the " +
                 "iOS Simulator — use a physical device — and Background App Refresh must be enabled.",
            // BGTaskSchedulerErrorCodeTooManyPendingTaskRequests
            2 => "Too many pending task requests; cancel some before submitting another.",
            // BGTaskSchedulerErrorCodeNotPermitted
            3 => $"'{identifier}' is not permitted. Add it to BGTaskSchedulerPermittedIdentifiers " +
                 "in Info.plist and register a launch handler for it before the app finishes launching.",
            _ => string.Empty
        };
    }

    private static async Task<bool> IsBgTaskPendingAsync(string identifier)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        BGTaskScheduler.Shared.GetPending(requests =>
            tcs.TrySetResult(requests.Any(r => string.Equals(r.Identifier, identifier, StringComparison.Ordinal))));
        return await tcs.Task;
    }

#if DEBUG
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendSimulateLaunch(IntPtr receiver, IntPtr selector, IntPtr identifier);
#endif

    /// <summary>
    /// Forces a submitted BGTask to run via <c>_simulateLaunchForTaskWithIdentifier:</c> — the
    /// selector Apple documents for triggering background tasks from LLDB, invoked here in-process.
    ///
    /// <para>
    /// <b>Debug builds only.</b> This is a private selector, and the agent compiles into the host
    /// app; shipping it would expose consumers to App Store review rejection. There is no public
    /// API that runs a BGTask on demand, so release builds can schedule but not trigger.
    /// </para>
    /// </summary>
    private static bool TrySimulateBgTaskLaunch(string identifier, out string? error)
    {
#if DEBUG
        try
        {
            var selector = new ObjCRuntime.Selector("_simulateLaunchForTaskWithIdentifier:");
            var scheduler = BGTaskScheduler.Shared;

            if (!scheduler.RespondsToSelector(selector))
            {
                error = "BGTaskScheduler does not respond to _simulateLaunchForTaskWithIdentifier: on this OS version.";
                return false;
            }

            using var nsIdentifier = new NSString(identifier);
            SendSimulateLaunch(scheduler.Handle, selector.Handle, nsIdentifier.Handle);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to launch BGTask: {ex.GetBaseException().Message}";
            return false;
        }
#else
        error = "Triggering a BGTask requires a private API and is only available in Debug builds of the DevFlow agent. " +
                "The request has been scheduled; iOS will launch it at its own discretion.";
        return false;
#endif
    }

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

    protected override BleMonitor CreateBleMonitor()
    {
#if ANDROID
        return new Ble.AndroidBleMonitor();
#elif IOS || MACCATALYST
        return new Ble.AppleBleMonitor();
#elif WINDOWS
        return new Ble.WindowsBleMonitor();
#elif MACOS
        return new Ble.MacOsBleMonitor();
#else
        return base.CreateBleMonitor();
#endif
    }

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
