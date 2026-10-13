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
public partial class PlatformAgentService : MauiDevFlowAgentService
{
    public PlatformAgentService(AgentOptions? options = null) : base(options) { }

    internal PlatformAgentService(
        AgentOptions? options,
        RegisteredNativeElementRegistry nativeElementRegistry,
        IDisposable nativeElementSubscription)
        : base(options, nativeElementRegistry, nativeElementSubscription)
    {
    }

    protected override VisualTreeWalker CreateTreeWalker()
        => NativeElementRegistry is null
            ? new PlatformVisualTreeWalker()
            : new PlatformVisualTreeWalker(NativeElementRegistry);

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

    protected override IReadOnlyCollection<string>? EnumerateNativePreferenceKeys(string? sharedName)
    {
#if IOS || MACCATALYST || MACOS || ANDROID
        return NativePreferenceKeys.Enumerate(sharedName);
#elif WINDOWS
        if (Microsoft.Maui.ApplicationModel.AppInfo.PackagingModel ==
            Microsoft.Maui.ApplicationModel.AppPackagingModel.Unpackaged)
            return EnumerateUnpackagedWindowsPreferenceKeys(sharedName);

        var localSettings = global::Windows.Storage.ApplicationData.Current.LocalSettings;
        global::Windows.Storage.ApplicationDataContainer? container;
        if (string.IsNullOrWhiteSpace(sharedName))
            container = localSettings;
        else
            container = localSettings.Containers.ContainsKey(sharedName)
                ? localSettings.Containers[sharedName]
                : null;

        return container is null ? Array.Empty<string>() : new List<string>(container.Values.Keys);
#else
        return base.EnumerateNativePreferenceKeys(sharedName);
#endif
    }

#if WINDOWS
    private static IReadOnlyCollection<string>? EnumerateUnpackagedWindowsPreferenceKeys(string? sharedName)
    {
        var path = global::System.IO.Path.Combine(
            Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "..", "Settings", "preferences.dat");
        return ReadPreferenceFileKeys(path, sharedName, nestedStore: true);
    }
#endif

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

    private static void FindWinUIElementsByAutomationId(
        Microsoft.UI.Xaml.DependencyObject? parent,
        string automationId,
        ICollection<Microsoft.UI.Xaml.FrameworkElement> matches)
    {
        if (parent is null)
            return;

        if (parent is Microsoft.UI.Xaml.FrameworkElement element
            && string.Equals(
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(element),
                automationId,
                StringComparison.Ordinal))
        {
            matches.Add(element);
        }

        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            FindWinUIElementsByAutomationId(
                Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i),
                automationId,
                matches);
        }
    }

    private static Microsoft.UI.Xaml.FrameworkElement? FindWinUIRoot(IntPtr targetHwnd)
    {
        var app = Application.Current;
        if (app is null)
            return null;

        foreach (var window in app.Windows)
        {
            if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow
                || nativeWindow.Content is not Microsoft.UI.Xaml.FrameworkElement root
                || WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow) != targetHwnd)
            {
                continue;
            }

            return root;
        }

        return null;
    }

    private static bool TryInvokeWinUIElement(
        Microsoft.UI.Xaml.FrameworkElement root,
        string automationId)
    {
        var matches = new List<Microsoft.UI.Xaml.FrameworkElement>();
        if (root.XamlRoot is not null)
        {
            foreach (var popup in Microsoft.UI.Xaml.Media.VisualTreeHelper
                .GetOpenPopupsForXamlRoot(root.XamlRoot))
            {
                FindWinUIElementsByAutomationId(popup.Child, automationId, matches);
            }
        }

        FindWinUIElementsByAutomationId(root, automationId, matches);
        var uniqueMatches = new List<Microsoft.UI.Xaml.FrameworkElement>();
        foreach (var match in matches)
        {
            if (!uniqueMatches.Any(existing => ReferenceEquals(existing, match)))
                uniqueMatches.Add(match);
        }
        if (uniqueMatches.Count != 1)
            return false;

        var target = uniqueMatches[0];
        var peer =
            Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(target)
            ?? Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(target);
        if (peer?.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)
            is not Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider invokeProvider)
        {
            return false;
        }

        invokeProvider.Invoke();
        return true;
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
    private static object WorkManagerProblem(string error) => new Dictionary<string, object?>
    {
        ["platform"] = "Android",
        ["type"] = "WorkManager",
        ["supported"] = true,
        ["runSupported"] = false,
        ["error"] = error,
        ["jobs"] = Array.Empty<object>()
    };

    // JNI callers must use the app loader; the default boot loader cannot see AndroidX.
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

    private static List<object> GetScheduledWorkManagerJobs()
    {
        var scheduler = global::Android.App.Application.Context.GetSystemService(
            global::Android.Content.Context.JobSchedulerService) as global::Android.App.Job.JobScheduler
            ?? throw new InvalidOperationException("Android JobScheduler is unavailable.");
        var jobs = new List<object>();
        AddJobs(scheduler, null);
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
            AddJobs(scheduler.ForNamespace("androidx.work.systemjobscheduler"), "androidx.work.systemjobscheduler");
        return jobs;

        void AddJobs(global::Android.App.Job.JobScheduler jobScheduler, string? jobNamespace)
        {
            foreach (var job in jobScheduler.AllPendingJobs
                ?? throw new InvalidOperationException("JobScheduler returned no pending-job collection."))
            {
                if (job.Service?.ClassName != "androidx.work.impl.background.systemjob.SystemJobService")
                    continue;
                var workSpecId = job.Extras?.GetString("EXTRA_WORK_SPEC_ID");
                if (!Guid.TryParse(workSpecId, out _))
                    throw new InvalidOperationException($"WorkManager job {job.Id} has no valid EXTRA_WORK_SPEC_ID.");
                jobs.Add(new Dictionary<string, object?>
                {
                    ["workSpecId"] = workSpecId,
                    ["jobId"] = job.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["namespace"] = jobNamespace
                });
            }
        }
    }
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
            return new Dictionary<string, object?>
            {
                ["platform"] = "Android",
                ["type"] = "WorkManager",
                ["supported"] = false,
                ["runSupported"] = false,
                ["reason"] = WorkManagerMissingReason,
                ["jobs"] = Array.Empty<object>()
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
                return new Dictionary<string, object?> { ["platform"] = "Android", ["type"] = "WorkManager", ["supported"] = true, ["runSupported"] = false, ["error"] = "WorkManager not initialized", ["jobs"] = Array.Empty<object>() };

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

                jobs.Add(new Dictionary<string, object?>
                {
                    ["identifier"] = identifier,
                    ["tags"] = tags.ToArray(),
                    ["state"] = state,
                    ["runAttemptCount"] = runAttemptCount
                });
            }

            return new Dictionary<string, object?>
            {
                ["platform"] = "Android",
                ["type"] = "WorkManager",
                ["supported"] = true,
                ["runSupported"] = false,
                ["jobs"] = jobs,
                ["scheduledJobs"] = GetScheduledWorkManagerJobs()
            };
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object?> { ["platform"] = "Android", ["type"] = "WorkManager", ["supported"] = true, ["runSupported"] = false, ["error"] = ex.Message, ["jobs"] = Array.Empty<object>() };
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
                    jobs.Add(new Dictionary<string, object?>
                    {
                        ["identifier"] = req.Identifier,
                        ["type"] = type,
                        ["earliestBeginDate"] = req.EarliestBeginDate?.ToString() ?? ""
                    });
                }
                tcs.TrySetResult(new Dictionary<string, object?> { ["platform"] = "iOS", ["type"] = "BGTaskScheduler", ["supported"] = true, ["runSupported"] = IsJobRunSupported, ["jobs"] = jobs });
            });
            return await tcs.Task;
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object?> { ["platform"] = "iOS", ["type"] = "BGTaskScheduler", ["supported"] = true, ["runSupported"] = IsJobRunSupported, ["error"] = ex.Message, ["jobs"] = Array.Empty<object>() };
        }
#else
        return await base.GetPlatformJobsAsync();
#endif
    }

    protected override async Task<object?> RunPlatformJobAsync(string identifier, string? type = null)
    {
#if ANDROID
        return await Task.FromResult<object?>(new Dictionary<string, object?>
        {
            ["success"] = false,
            ["supported"] = false,
            ["identifier"] = identifier,
            ["error"] = s_workManagerAvailable.Value
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
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["identifier"] = identifier,
                    ["type"] = taskType,
                    ["error"] = $"Could not schedule BGTask '{identifier}': {submitError}"
                };
            }

            // Submitting only *schedules*; iOS decides when to launch, which organically may be
            // never. Forcing the launch is the only way to actually run the handler on demand.
            var launched = TrySimulateBgTaskLaunch(identifier, out var launchError);
            if (!launched)
            {
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["identifier"] = identifier,
                    ["type"] = taskType,
                    ["scheduled"] = true,
                    ["error"] = launchError
                };
            }

            // A handler may reschedule its identifier immediately. Pending requests cannot
            // prove either execution or completion; only acknowledge the launch request.
            return new Dictionary<string, object?>
            {
                ["success"] = true,
                ["identifier"] = identifier,
                ["type"] = taskType,
                ["launchRequested"] = true,
                ["executionVerified"] = false,
                ["completionVerified"] = false,
                ["message"] = $"Launch requested for BGTask '{identifier}'.",
                ["note"] = "This acknowledges the debug launch request, not handler execution or completion. " +
                    "Assert on the handler's own recorded outcome; a pending request may be its next scheduled run."
            };
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object?> { ["success"] = false, ["error"] = ex.Message, ["identifier"] = identifier };
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

    protected override async Task<bool> TryNativeTapFirstAsync(VisualElement ve)
    {
        await Task.CompletedTask;
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
                    var completion = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    var invocationState = 0;
                    if (!buttonBase.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (Interlocked.CompareExchange(ref invocationState, 1, 0) != 0)
                        {
                            completion.TrySetResult(false);
                            return;
                        }

                        try
                        {
                            invokeProvider.Invoke();
                            completion.TrySetResult(true);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] WinUI native invoke skipped: {ex.GetBaseException().Message}");
                            completion.TrySetResult(false);
                        }
                    }))
                    {
                        return false;
                    }

                    var winner = await Task.WhenAny(
                        completion.Task,
                        Task.Delay(TimeSpan.FromSeconds(5)));
                    if (winner == completion.Task)
                        return await completion.Task;

                    if (Interlocked.CompareExchange(ref invocationState, 2, 0) == 0)
                        return false;

                    // Invocation already started. Treat it as handled so a modal or
                    // long-running click handler does not block the automation request
                    // or trigger the managed fallback a second time.
                    return true;
                }
            }
#endif
        }
        catch { }

        return false;
    }

    protected override async Task<string?> TryNativeElementTapAsync(string elementId, object nativeElement)
    {
#if WINDOWS
        if (nativeElement is System.Windows.Automation.AutomationElement automationElement)
        {
            try
            {
                var automationId = automationElement.Current.AutomationId;
                var targetHwnd = Windows.NativeWindowProbe.TryGetTopLevelWindowHandle(
                    automationElement);
                if (!string.IsNullOrEmpty(automationId) && targetHwnd is { } hwnd)
                {
                    var root = await DispatchAsync(() => FindWinUIRoot(hwnd));
                    if (root is not null)
                    {
                        var completion = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        var invocationState = 0;
                        if (root.DispatcherQueue.TryEnqueue(() =>
                        {
                            if (Interlocked.CompareExchange(ref invocationState, 1, 0) != 0)
                            {
                                completion.TrySetResult(false);
                                return;
                            }

                            try
                            {
                                completion.TrySetResult(
                                    TryInvokeWinUIElement(root, automationId));
                            }
                            catch (Exception ex) when (ex is
                                InvalidOperationException
                                or System.Runtime.InteropServices.COMException)
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    $"[Microsoft.Maui.DevFlow] WinUI native element invocation skipped: {ex.Message}");
                                completion.TrySetResult(false);
                            }
                            catch (Exception ex)
                            {
                                completion.TrySetException(ex);
                            }
                        }))
                        {
                            var winner = await Task.WhenAny(
                                completion.Task,
                                Task.Delay(TimeSpan.FromSeconds(5)));
                            if (winner == completion.Task)
                            {
                                if (await completion.Task)
                                    return "ok";
                            }
                            else if (Interlocked.CompareExchange(ref invocationState, 2, 0) != 0)
                            {
                                return "ok";
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is
                System.Windows.Automation.ElementNotAvailableException
                or InvalidOperationException
                or System.Runtime.InteropServices.COMException)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Microsoft.Maui.DevFlow] WinUI native element lookup skipped: {ex.Message}");
            }
        }
#endif

        return await base.TryNativeElementTapAsync(elementId, nativeElement);
    }

#if ANDROID || IOS || MACCATALYST
    private const long MaxNativeElementScreenshotPixels = 16_777_216;

#if ANDROID
    protected override Task<byte[]?> CaptureFullScreenAsync(int? windowIndex = null)
        => DispatchAsync(() => CaptureAndroidFullScreen(windowIndex));
#endif

    protected override bool SupportsNativeElementScreenshots => true;

    protected override Task<byte[]?> CaptureNativeElementScreenshotAsync(
        object nativeElement,
        ElementInfo? elementInfo)
    {
        try
        {
#if ANDROID
            var view = nativeElement switch
            {
                global::Android.Views.View androidView => androidView,
                global::Android.Views.IMenuItem menuItem => menuItem.ActionView,
                _ => null
            };
            return Task.FromResult(CaptureAndroidView(view));
#elif IOS || MACCATALYST
            if (nativeElement is UIKit.UIView uiView)
                return Task.FromResult(CaptureAppleView(uiView));

            if (nativeElement is UIKit.UIViewController viewController)
                return Task.FromResult(CaptureAppleView(viewController.View));

            if (nativeElement is UIKit.UIBarButtonItem { CustomView: { } customView })
                return Task.FromResult(CaptureAppleView(customView));

            if (nativeElement is UIKit.UIBarItem barItem && !barItem.AccessibilityFrame.IsEmpty)
            {
                UIKit.UIWindow? ownerWindow = null;
                if (elementInfo?.WindowId is int windowId
                    && _app is not null
                    && windowId >= 0
                    && windowId < _app.Windows.Count
                    && _app.Windows[windowId].Handler?.PlatformView is UIKit.UIWindow uiWindow)
                {
                    ownerWindow = uiWindow;
                }

                return Task.FromResult(CaptureAppleScreenRect(
                    barItem.AccessibilityFrame,
                    ownerWindow));
            }

            return Task.FromResult<byte[]?>(null);
#endif
        }
        catch
        {
            return Task.FromResult<byte[]?>(null);
        }
    }

#if ANDROID
    private byte[]? CaptureAndroidFullScreen(int? windowIndex)
    {
        var window = _app is null || _app.Windows.Count == 0
            ? null
            : windowIndex is int index && index >= 0 && index < _app.Windows.Count
                ? _app.Windows[index]
                : _app.Windows[0];
        var activity = window?.Handler?.PlatformView as global::Android.App.Activity
            ?? Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var rootView = activity?.Window?.DecorView?.RootView;
        if (rootView is null
            || !rootView.IsAttachedToWindow
            || rootView.Width <= 0
            || rootView.Height <= 0
            || (long)rootView.Width * rootView.Height > MaxNativeElementScreenshotPixels)
        {
            return null;
        }

        using var bitmap = global::Android.Graphics.Bitmap.CreateBitmap(
            rootView.Width,
            rootView.Height,
            global::Android.Graphics.Bitmap.Config.Argb8888!);
        using var canvas = new global::Android.Graphics.Canvas(bitmap);
        rootView.Draw(canvas);

        var rootLocation = new int[2];
        rootView.GetLocationOnScreen(rootLocation);
        var overlayRoots = new List<global::Android.Views.View>();
        foreach (var registration in NativeElementRegistry?.GetSnapshot()
            ?? Array.Empty<NativeElementRegistrationSnapshot>())
        {
            if (!registration.Role.Equals("Dialog", StringComparison.Ordinal)
                && !registration.Role.Equals("ShellTabOverflow", StringComparison.Ordinal))
            {
                continue;
            }

            if (registration.NativeElement is not global::Android.Views.View registeredView)
                continue;
            if (!ReferenceEquals(FindAndroidActivity(registeredView.Context), activity))
                continue;

            var overlayRoot = registeredView.RootView;
            if (overlayRoot is null
                || ReferenceEquals(overlayRoot, rootView)
                || !overlayRoot.IsAttachedToWindow
                || !overlayRoot.IsShown
                || overlayRoot.Width <= 0
                || overlayRoot.Height <= 0
                || overlayRoots.Any(existing => ReferenceEquals(existing, overlayRoot)))
            {
                continue;
            }

            overlayRoots.Add(overlayRoot);
        }

        if (overlayRoots.Count > 0)
        {
            canvas.DrawColor(global::Android.Graphics.Color.Argb(80, 0, 0, 0));
            foreach (var overlayRoot in overlayRoots)
            {
                var location = new int[2];
                overlayRoot.GetLocationOnScreen(location);
                var saveCount = canvas.Save();
                canvas.Translate(
                    location[0] - rootLocation[0],
                    location[1] - rootLocation[1]);
                overlayRoot.Draw(canvas);
                canvas.RestoreToCount(saveCount);
            }
        }

        return EncodeAndroidBitmap(bitmap);
    }

    private static global::Android.App.Activity? FindAndroidActivity(
        global::Android.Content.Context? context)
    {
        while (context is global::Android.Content.ContextWrapper wrapper)
        {
            if (context is global::Android.App.Activity activity)
                return activity;

            var baseContext = wrapper.BaseContext;
            if (ReferenceEquals(baseContext, context))
                break;
            context = baseContext;
        }

        return context as global::Android.App.Activity;
    }

    private static byte[]? CaptureAndroidView(global::Android.Views.View? view)
    {
        if (view is null
            || !view.IsAttachedToWindow
            || view.Width <= 0
            || view.Height <= 0
            || (long)view.Width * view.Height > MaxNativeElementScreenshotPixels)
        {
            return null;
        }

        using var bitmap = global::Android.Graphics.Bitmap.CreateBitmap(
            view.Width,
            view.Height,
            global::Android.Graphics.Bitmap.Config.Argb8888!);
        using var canvas = new global::Android.Graphics.Canvas(bitmap);
        view.Draw(canvas);
        return EncodeAndroidBitmap(bitmap);
    }

    private static byte[]? EncodeAndroidBitmap(global::Android.Graphics.Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        return bitmap.Compress(
            global::Android.Graphics.Bitmap.CompressFormat.Png!,
            quality: 100,
            stream)
                ? stream.ToArray()
                : null;
    }
#elif IOS || MACCATALYST
    private static byte[]? CaptureAppleView(UIKit.UIView? view)
    {
        if (view?.Window is not { } window
            || view.Hidden
            || view.Alpha <= 0
            || view.Bounds.Width <= 0
            || view.Bounds.Height <= 0
            || !IsAppleCaptureSizeSupported(view.Bounds.Size, window.Screen.Scale))
        {
            return null;
        }

        using var format = new UIKit.UIGraphicsImageRendererFormat
        {
            Scale = window.Screen.Scale,
            Opaque = view.Opaque
        };
        using var renderer = new UIKit.UIGraphicsImageRenderer(
            new CoreGraphics.CGRect(0, 0, view.Bounds.Width, view.Bounds.Height),
            format);
        using var image = renderer.CreateImage(context =>
        {
            context.CGContext.TranslateCTM(-view.Bounds.X, -view.Bounds.Y);
            if (!view.DrawViewHierarchy(view.Bounds, afterScreenUpdates: false))
                view.Layer.RenderInContext(context.CGContext);
        });
        using var pngData = image.AsPNG();
        return pngData?.ToArray();
    }

    private static byte[]? CaptureAppleScreenRect(
        CoreGraphics.CGRect screenFrame,
        UIKit.UIWindow? ownerWindow)
    {
        ownerWindow ??= FindAppleWindow(screenFrame);
        var windowScene = ownerWindow?.WindowScene;
        if (ownerWindow is null
            || screenFrame.IsEmpty
            || !IsAppleCaptureSizeSupported(screenFrame.Size, ownerWindow.Screen.Scale))
        {
            return null;
        }

        var ownerFrame = ownerWindow.ConvertRectFromCoordinateSpace(
            screenFrame,
            ownerWindow.Screen.CoordinateSpace);
        if (!ownerWindow.Bounds.IntersectsWith(ownerFrame))
            return null;

        var windows = windowScene?.Windows;
        if (windows is null)
        {
#pragma warning disable CA1422 // Legacy AppDelegate apps can have no UIWindowScene on current iOS versions.
            windows = UIKit.UIApplication.SharedApplication.Windows;
#pragma warning restore CA1422
        }

        var visibleWindows = windows
            .Where(window => !window.Hidden && window.Alpha > 0)
            .Where(window => ReferenceEquals(window.Screen, ownerWindow.Screen))
            .OrderBy(window => (double)window.WindowLevel)
            .ToList();
        if (visibleWindows.Count == 0)
            return null;

        using var format = new UIKit.UIGraphicsImageRendererFormat
        {
            Scale = ownerWindow.Screen.Scale
        };
        using var renderer = new UIKit.UIGraphicsImageRenderer(
            new CoreGraphics.CGRect(0, 0, screenFrame.Width, screenFrame.Height),
            format);
        using var image = renderer.CreateImage(context =>
        {
            context.CGContext.SaveState();
            context.CGContext.TranslateCTM(-screenFrame.X, -screenFrame.Y);
            foreach (var window in visibleWindows)
            {
                context.CGContext.SaveState();
                context.CGContext.TranslateCTM(window.Frame.X, window.Frame.Y);
                window.DrawViewHierarchy(window.Bounds, afterScreenUpdates: false);
                context.CGContext.RestoreState();
            }
            context.CGContext.RestoreState();
        });
        using var pngData = image.AsPNG();
        return pngData?.ToArray();
    }

    private static UIKit.UIWindow? FindAppleWindow(CoreGraphics.CGRect screenFrame)
    {
        UIKit.UIWindow? fallback = null;
        foreach (var scene in UIKit.UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is not UIKit.UIWindowScene windowScene)
                continue;

            foreach (var window in windowScene.Windows)
            {
                if (window.Hidden || window.Alpha <= 0)
                    continue;

                fallback ??= window;
                var windowFrame = window.ConvertRectFromCoordinateSpace(
                    screenFrame,
                    window.Screen.CoordinateSpace);
                if (window.Bounds.IntersectsWith(windowFrame))
                    return window;
            }
        }

#pragma warning disable CA1422 // Legacy AppDelegate apps can have no UIWindowScene on current iOS versions.
        foreach (var window in UIKit.UIApplication.SharedApplication.Windows)
#pragma warning restore CA1422
        {
            if (window.Hidden || window.Alpha <= 0)
                continue;

            fallback ??= window;
            var windowFrame = window.ConvertRectFromCoordinateSpace(
                screenFrame,
                window.Screen.CoordinateSpace);
            if (window.Bounds.IntersectsWith(windowFrame))
                return window;
        }

        return fallback;
    }

    private static bool IsAppleCaptureSizeSupported(
        CoreGraphics.CGSize logicalSize,
        double scale)
        => logicalSize.Width > 0
            && logicalSize.Height > 0
            && scale > 0
            && logicalSize.Width * scale * logicalSize.Height * scale
                <= MaxNativeElementScreenshotPixels;
#endif
#endif

#if MACOS
    protected override bool SupportsNativeElementScreenshots => true;

    protected override async Task<byte[]?> CaptureScreenshotAsync(VisualElement rootElement)
    {
        try
        {
            // Resolve the window without depending on the app being frontmost.
            // NSApplication.KeyWindow is null when the app is not the active application,
            // so prefer the NSWindow that actually owns the element being captured.
            var window = ResolveCaptureWindow(rootElement);
            var mauiWindow = rootElement.Window
                ?? Microsoft.Maui.Controls.Application.Current?.Windows
                    .FirstOrDefault(candidate => ReferenceEquals(candidate.Page, rootElement));
            var ownerWindow = NSWindowFromPlatformView(mauiWindow?.Handler?.PlatformView);
            var parentWindow = window is { IsSheet: true, SheetParent: { } sheetParent }
                ? sheetParent
                : ownerWindow ?? window;

            // Capture the parent and its attached sheet as one WindowServer image.
            // The private alert content may be layer-backed and cannot be reproduced
            // reliably by rendering either NSView hierarchy in isolation.
            var attachedSheet = parentWindow?.AttachedSheet
                ?? parentWindow?.Sheets.LastOrDefault()
                ?? FindRegisteredDialogWindow(parentWindow);
            if (parentWindow != null && attachedSheet != null)
            {
                var composited = CaptureWindowsViaCG(parentWindow, attachedSheet);
                if (composited != null)
                    return composited;

                window = attachedSheet;
            }
            else
            {
                window = parentWindow;
            }

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

    NSWindow? FindRegisteredDialogWindow(NSWindow? parentWindow)
    {
        if (NativeElementRegistry == null)
            return null;

        foreach (var registration in NativeElementRegistry.GetSnapshot().Reverse())
        {
            if (!registration.Role.Equals("Dialog", StringComparison.Ordinal))
                continue;

            var candidate = registration.NativeElement switch
            {
                NSWindow nativeWindow => nativeWindow,
                NSView nativeView => nativeView.Window,
                _ => null
            };
            var registrationOwnerWindow = FindAppKitRegistrationOwnerWindow(
                registration.Owner);
            if (candidate != null
                && candidate.WindowNumber > 0
                && candidate.IsVisible
                && candidate.Handle != parentWindow?.Handle
                && (parentWindow == null
                    || registrationOwnerWindow?.Handle == parentWindow.Handle)
                && (candidate.SheetParent == null
                    || candidate.SheetParent.Handle == parentWindow?.Handle))
            {
                return candidate;
            }
        }

        return null;
    }

    private static NSWindow? FindAppKitRegistrationOwnerWindow(object owner)
    {
        for (var element = owner as Element; element is not null; element = element.Parent)
        {
            if (element is Page page)
                return NSWindowFromPlatformView(page.Window?.Handler?.PlatformView);
        }

        return null;
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

    protected override Task<byte[]?> CaptureNativeElementScreenshotAsync(
        object nativeElement,
        ElementInfo? elementInfo)
    {
        try
        {
            var view = nativeElement switch
            {
                NSView nsView => nsView,
                NSSearchToolbarItem searchItem => searchItem.SearchField,
                NSToolbarItem toolbarItem => toolbarItem.View,
                _ => null
            };
            if (view is not null)
                return Task.FromResult<byte[]?>(CaptureNSView(view));
        }
        catch { }

        return Task.FromResult<byte[]?>(null);
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

    [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    static extern IntPtr CGWindowListCreateImageFromArray(
        CoreGraphics.CGRect screenBounds,
        IntPtr windowArray,
        uint imageOption);

    private static byte[]? CaptureWindowViaCG(NSWindow window)
    {
        try
        {
            using var bitmapRep = CaptureWindowBitmapViaCG(window);
            return EncodeBitmapRepresentation(bitmapRep);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? CaptureWindowsViaCG(
        NSWindow parentWindow,
        NSWindow sheetWindow)
    {
        try
        {
            var windows = new[] { sheetWindow, parentWindow };
            var windowNumbers = windows
                .Where(window => window.WindowNumber > 0)
                .Select(window => new NSNumber((uint)window.WindowNumber))
                .ToArray();
            if (windowNumbers.Length == 0)
                return null;

            using var windowArray = NSArray.FromNSObjects(windowNumbers);
            foreach (var windowNumber in windowNumbers)
                windowNumber.Dispose();

            // kCGWindowImageBoundsIgnoreFraming = 0x01,
            // kCGWindowImageBestResolution = 0x08.
            var cgImagePtr = CGWindowListCreateImageFromArray(
                CoreGraphics.CGRect.Null,
                windowArray.Handle,
                0x09);
            if (cgImagePtr != IntPtr.Zero)
            {
                using var cgImage = ObjCRuntime.Runtime.GetINativeObject<CoreGraphics.CGImage>(
                    cgImagePtr,
                    owns: true);
                if (cgImage != null)
                {
                    using var bitmapRep = new NSBitmapImageRep(cgImage);
                    return EncodeBitmapRepresentation(bitmapRep);
                }
            }

            return CaptureWindowPairViaCG(parentWindow, sheetWindow);
        }
        catch
        {
            return null;
        }
    }

    private static NSBitmapImageRep? CaptureWindowBitmapViaCG(NSWindow window)
    {
        // kCGWindowListOptionIncludingWindow = 0x08,
        // kCGWindowImageBoundsIgnoreFraming = 0x01,
        // kCGWindowImageBestResolution = 0x08.
        var cgImagePtr = CGWindowListCreateImage(
            CoreGraphics.CGRect.Null,
            0x08,
            (uint)window.WindowNumber,
            0x09);
        if (cgImagePtr == IntPtr.Zero)
            return null;

        using var cgImage = ObjCRuntime.Runtime.GetINativeObject<CoreGraphics.CGImage>(
            cgImagePtr,
            owns: true);
        return cgImage == null
            ? null
            : new NSBitmapImageRep(cgImage);
    }

    private static byte[]? CaptureWindowPairViaCG(
        NSWindow parentWindow,
        NSWindow sheetWindow)
    {
        using var parentRep = CaptureWindowBitmapViaCG(parentWindow);
        using var sheetRep = CaptureWindowBitmapViaCG(sheetWindow);
        if (parentRep == null || sheetRep == null)
            return null;

        var logicalSize = parentWindow.Frame.Size;
        using var compositedRep = new NSBitmapImageRep(
            IntPtr.Zero,
            (int)parentRep.PixelsWide,
            (int)parentRep.PixelsHigh,
            8,
            4,
            true,
            false,
            NSColorSpace.DeviceRGB,
            0,
            0);
        compositedRep.Size = logicalSize;

        using var parentImage = new NSImage(logicalSize);
        parentImage.AddRepresentation(parentRep);
        using var sheetImage = new NSImage(sheetWindow.Frame.Size);
        sheetImage.AddRepresentation(sheetRep);

        NSGraphicsContext.GlobalSaveGraphicsState();
        try
        {
            var context = NSGraphicsContext.FromBitmap(compositedRep);
            if (context == null)
                return null;

            NSGraphicsContext.CurrentContext = context;
            parentImage.Draw(
                new CoreGraphics.CGRect(CoreGraphics.CGPoint.Empty, logicalSize),
                new CoreGraphics.CGRect(CoreGraphics.CGPoint.Empty, parentImage.Size),
                NSCompositingOperation.SourceOver,
                1,
                respectContextIsFlipped: false,
                hints: null);

            var sheetDestination = new CoreGraphics.CGRect(
                sheetWindow.Frame.X - parentWindow.Frame.X,
                sheetWindow.Frame.Y - parentWindow.Frame.Y,
                sheetWindow.Frame.Width,
                sheetWindow.Frame.Height);
            sheetImage.Draw(
                sheetDestination,
                new CoreGraphics.CGRect(CoreGraphics.CGPoint.Empty, sheetImage.Size),
                NSCompositingOperation.SourceOver,
                1,
                respectContextIsFlipped: false,
                hints: null);
        }
        finally
        {
            NSGraphicsContext.GlobalRestoreGraphicsState();
        }

        return EncodeBitmapRepresentation(compositedRep);
    }

    private static byte[]? EncodeBitmapRepresentation(NSBitmapImageRep? bitmapRep)
    {
        if (bitmapRep == null)
            return null;

        var pngData = bitmapRep.RepresentationUsingTypeProperties(
            NSBitmapImageFileType.Png,
            new NSDictionary());
        return pngData?.ToArray();
    }
#elif IOS || MACCATALYST
    protected override async Task<byte[]?> CaptureScreenshotAsync(VisualElement rootElement)
    {
        var pngBytes = await DispatchAsync(() => CaptureAllWindowsComposited());
        if (pngBytes != null)
            return pngBytes;
        return await base.CaptureScreenshotAsync(rootElement);
    }

    protected override Task<byte[]?> CaptureFullScreenAsync(int? windowIndex = null)
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
    protected override bool SupportsNativeElementScreenshots => true;

    protected override async Task<byte[]?> CaptureNativeElementScreenshotAsync(
        object nativeElement,
        ElementInfo? elementInfo)
    {
        if (nativeElement is System.Windows.Automation.AutomationElement automationElement)
            return await Task.Run(() => Windows.NativeWindowProbe.CaptureElementScreenshot(automationElement));

        if (nativeElement is not Microsoft.UI.Xaml.UIElement uiElement)
            return null;

        try
        {
            var renderTarget = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await renderTarget.RenderAsync(uiElement);
            if (renderTarget.PixelWidth <= 0 || renderTarget.PixelHeight <= 0)
                return null;

            var pixelBuffer = await renderTarget.GetPixelsAsync();
            var pixels = new byte[checked((int)pixelBuffer.Length)];
            using (var reader = global::Windows.Storage.Streams.DataReader.FromBuffer(pixelBuffer))
                reader.ReadBytes(pixels);

            using var bitmap = new SkiaSharp.SKBitmap(
                renderTarget.PixelWidth,
                renderTarget.PixelHeight,
                SkiaSharp.SKColorType.Bgra8888,
                SkiaSharp.SKAlphaType.Premul);
            System.Runtime.InteropServices.Marshal.Copy(
                pixels,
                0,
                bitmap.GetPixels(),
                pixels.Length);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            return png.ToArray();
        }
        catch
        {
            return null;
        }
    }

    protected override async Task<byte[]?> CaptureFullScreenAsync(int? windowIndex = null)
    {
        var target = await DispatchAsync(() =>
        {
            var index = windowIndex ?? 0;
            var platformWindow = Application.Current?.Windows.ElementAtOrDefault(index)?
                .Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            return (
                Hwnd: platformWindow is null
                    ? IntPtr.Zero
                    : WinRT.Interop.WindowNative.GetWindowHandle(platformWindow),
                Root: platformWindow?.Content);
        });
        if (target.Hwnd == IntPtr.Zero || target.Root is null)
            return null;

        var baseScreenshot = await Task.Run(
            () => Windows.NativeWindowProbe.CaptureCompositedWindowScreenshot(target.Hwnd));
        if (baseScreenshot is null)
            return null;

        try
        {
            return await DispatchAsync<byte[]>(
                () => CompositeWinUiPopupsAsync(baseScreenshot, target.Root))
                ?? baseScreenshot;
        }
        catch
        {
            return baseScreenshot;
        }
    }

    private static async Task<byte[]?> CompositeWinUiPopupsAsync(
        byte[] baseScreenshot,
        Microsoft.UI.Xaml.UIElement root)
    {
        if (root.XamlRoot is null)
            return baseScreenshot;

        using var rootBitmap = SkiaSharp.SKBitmap.Decode(baseScreenshot);
        if (rootBitmap is null)
            return baseScreenshot;

        using var canvas = new SkiaSharp.SKCanvas(rootBitmap);
        var rootScaleX = root is Microsoft.UI.Xaml.FrameworkElement rootElement
            && rootElement.ActualWidth > 0
                ? rootBitmap.Width / rootElement.ActualWidth
                : 1d;
        var rootScaleY = root is Microsoft.UI.Xaml.FrameworkElement rootElementForHeight
            && rootElementForHeight.ActualHeight > 0
                ? rootBitmap.Height / rootElementForHeight.ActualHeight
                : 1d;

        using var popupCaptureCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var popupCount = 0;
        foreach (var popup in Microsoft.UI.Xaml.Media.VisualTreeHelper
            .GetOpenPopupsForXamlRoot(root.XamlRoot))
        {
            if (popupCaptureCts.IsCancellationRequested || popupCount >= 8)
                break;
            if (!popup.IsOpen || popup.Child is not Microsoft.UI.Xaml.UIElement popupChild)
                continue;
            popupCount++;

            SkiaSharp.SKBitmap? renderedPopup;
            try
            {
                renderedPopup = await RenderWinUiBitmapAsync(
                    popupChild,
                    popupCaptureCts.Token);
            }
            catch
            {
                continue;
            }

            using var popupBitmap = renderedPopup;
            if (popupBitmap is null)
                continue;

            global::Windows.Foundation.Point origin;
            try
            {
                origin = popupChild.TransformToVisual(root)
                    .TransformPoint(new global::Windows.Foundation.Point(0, 0));
            }
            catch
            {
                origin = new global::Windows.Foundation.Point(
                    popup.HorizontalOffset,
                    popup.VerticalOffset);
            }

            canvas.DrawBitmap(
                popupBitmap,
                (float)(origin.X * rootScaleX),
                (float)(origin.Y * rootScaleY));
        }

        canvas.Flush();
        using var image = SkiaSharp.SKImage.FromBitmap(rootBitmap);
        using var png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static async Task<SkiaSharp.SKBitmap?> RenderWinUiBitmapAsync(
        Microsoft.UI.Xaml.UIElement element,
        CancellationToken cancellationToken)
    {
        using var captureCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        captureCts.CancelAfter(TimeSpan.FromSeconds(2));

        var renderTarget = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await renderTarget.RenderAsync(element)
            .AsTask(captureCts.Token);
        if (renderTarget.PixelWidth <= 0 || renderTarget.PixelHeight <= 0)
            return null;

        const long MaxCapturePixels = 16_777_216;
        var pixelCount = (long)renderTarget.PixelWidth * renderTarget.PixelHeight;
        if (pixelCount > MaxCapturePixels)
            return null;

        var pixelBuffer = await renderTarget.GetPixelsAsync()
            .AsTask(captureCts.Token);
        if (pixelBuffer.Length > int.MaxValue
            || pixelBuffer.Length > MaxCapturePixels * 4)
        {
            return null;
        }

        var pixels = new byte[(int)pixelBuffer.Length];
        using (var reader = global::Windows.Storage.Streams.DataReader.FromBuffer(pixelBuffer))
            reader.ReadBytes(pixels);

        var bitmap = new SkiaSharp.SKBitmap(
            renderTarget.PixelWidth,
            renderTarget.PixelHeight,
            SkiaSharp.SKColorType.Bgra8888,
            SkiaSharp.SKAlphaType.Premul);
        System.Runtime.InteropServices.Marshal.Copy(
            pixels,
            0,
            bitmap.GetPixels(),
            pixels.Length);
        return bitmap;
    }

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
