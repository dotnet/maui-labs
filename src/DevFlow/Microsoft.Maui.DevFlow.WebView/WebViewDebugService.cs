using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using AgentService = Microsoft.Maui.DevFlow.Agent.Core.DevFlowAgentService;

namespace Microsoft.Maui.DevFlow.WebView;

/// <summary>Native host attachment for standard MAUI WebView and HybridWebView.</summary>
public class WebViewDebugService : WebViewDebugServiceBase
{
    private static int _mapperConfigured;
    private readonly ConditionalWeakTable<object, Attachment> _attachments = new();
    public WebViewDebugOptions Options { get; }

    public WebViewDebugService() : this(new WebViewDebugOptions()) { }
    public WebViewDebugService(WebViewDebugOptions options) => Options = options;

    public override void ConfigureHandler() => ConfigureHandlers();

    internal static void ConfigureHandlers()
    {
        if (Interlocked.Exchange(ref _mapperConfigured, 1) != 0) return;
#if ANDROID || IOS || MACCATALYST || WINDOWS
        WebViewHandler.Mapper.AppendToMapping("DevFlow.WebView", (handler, view) =>
            Capture(handler, view as VisualElement, "webview"));
        HybridWebViewHandler.Mapper.AppendToMapping("DevFlow.HybridWebView", (handler, view) =>
            Capture(handler, view as VisualElement, "hybrid"));
#elif MACOS
        Microsoft.Maui.Platforms.MacOS.Handlers.WebViewHandler.Mapper.AppendToMapping("DevFlow.WebView", (handler, view) =>
            Capture(handler, view as VisualElement, "webview"));
#endif
    }

    private static void Capture(IViewHandler handler, VisualElement? owner, string hostKind)
    {
        if (owner is null || handler.PlatformView is null) return;
        handler.MauiContext?.Services.GetService<WebViewDebugService>()
            ?.AttachNativeWebView(handler, owner, handler.PlatformView, hostKind);
    }

    /// <summary>Reuse native transports for additional hosts without introducing another CDP engine.</summary>
    protected void AttachNativeWebView(IViewHandler handler, VisualElement owner, object platformView, string hostKind)
    {
        if (!Options.Enabled) return;
#if ANDROID || IOS || MACCATALYST || MACOS || WINDOWS
        if (_attachments.TryGetValue(platformView, out var existing) && existing.Bridge.IsActive) return;
        var index = -1;
        Action? cleanupNative = null;
        EventHandler<HandlerChangingEventArgs>? changing = null;
        EventHandler<WebNavigatedEventArgs>? navigated = null;
        EventHandler<WebViewInitializedEventArgs>? initialized = null;
        Action cleanup = () => PostToMainThread(() =>
        {
            try
            {
                owner.HandlerChanging -= changing;
                if (owner is Microsoft.Maui.Controls.WebView webView) webView.Navigated -= navigated;
                if (owner is HybridWebView hybrid) hybrid.WebViewInitialized -= initialized;
                cleanupNative?.Invoke();
            }
            catch (Exception ex) { LogError("Native attachment cleanup failed.", ex); }
            finally
            {
                if (_attachments.TryGetValue(platformView, out var attachment) &&
                    ReferenceEquals(attachment.Bridge, Bridges[index]))
                    _attachments.Remove(platformView);
            }
        });

        void Reinitialize()
        {
            if (index >= 0 && Bridges[index].IsActive)
                _ = ResetAndReinitializeBridgeAsync(index);
        }

        Func<string, Task<string?>> evaluate;
        Action reload;
        Action<string> navigate;
#if IOS || MACCATALYST || MACOS
        if (platformView is not WebKit.WKWebView native) return;
        evaluate = async script =>
        {
            if (script == "document.readyState" && native.IsLoading)
                return "loading";
            var value = await native.EvaluateJavaScriptAsync(script);
            return value is null or Foundation.NSNull ? null : value.ToString();
        };
        reload = () => native.Reload();
        navigate = url => native.LoadRequest(new Foundation.NSUrlRequest(new Foundation.NSUrl(url)));
        if (Options.EnableWebViewInspection)
        {
#if MACOS
            if (OperatingSystem.IsMacOSVersionAtLeast(13, 3)) native.Inspectable = true;
#elif IOS
            if (OperatingSystem.IsIOSVersionAtLeast(16, 4)) native.Inspectable = true;
#else
            if (OperatingSystem.IsMacCatalystVersionAtLeast(16, 4)) native.Inspectable = true;
#endif
        }
        // KVO observes the existing navigation delegate; never replace the app/MAUI delegate.
        var loading = native.AddObserver("loading", Foundation.NSKeyValueObservingOptions.New, _ =>
        {
            PostToMainThread(() => { if (!native.IsLoading) Reinitialize(); });
        });
        cleanupNative = loading.Dispose;
#elif ANDROID
        if (platformView is not global::Android.Webkit.WebView native) return;
        evaluate = script =>
        {
            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            native.EvaluateJavascript(script, new JavaScriptValueCallback(value => completion.TrySetResult(value)));
            return completion.Task;
        };
        reload = native.Reload;
        navigate = native.LoadUrl;
        if (Options.EnableWebViewInspection) global::Android.Webkit.WebView.SetWebContentsDebuggingEnabled(true);
        // The shared document monitor observes reloads without wrapping Android's WebViewClient,
        // which also owns Hybrid resource interception and raw-message transport.
#elif WINDOWS
        if (platformView is not Microsoft.UI.Xaml.Controls.WebView2 native) return;
        evaluate = async script =>
        {
            var core = native.CoreWebView2 ?? throw new InvalidOperationException("WebView2 is not initialized.");
            return await core.ExecuteScriptAsync(script);
        };
        reload = () => (native.CoreWebView2 ?? throw new InvalidOperationException("WebView2 is not initialized.")).Reload();
        navigate = url => (native.CoreWebView2 ?? throw new InvalidOperationException("WebView2 is not initialized.")).Navigate(url);
        global::Windows.Foundation.TypedEventHandler<Microsoft.UI.Xaml.Controls.WebView2,
            Microsoft.UI.Xaml.Controls.CoreWebView2InitializedEventArgs> coreInitialized = (_, args) =>
        {
            if (args.Exception is null)
            {
                if (native.CoreWebView2 is { } core) core.Settings.AreDevToolsEnabled = Options.EnableWebViewInspection;
                Reinitialize();
            }
            else LogError("WebView2 initialization failed.", args.Exception);
        };
        global::Windows.Foundation.TypedEventHandler<Microsoft.UI.Xaml.Controls.WebView2,
            Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs> completed = (_, _) => Reinitialize();
        native.CoreWebView2Initialized += coreInitialized;
        native.NavigationCompleted += completed;
        if (native.CoreWebView2 is { } existingCore) existingCore.Settings.AreDevToolsEnabled = Options.EnableWebViewInspection;
        cleanupNative = () =>
        {
            native.CoreWebView2Initialized -= coreInitialized;
            native.NavigationCompleted -= completed;
        };
#endif
        index = AddWebViewBridge(evaluate, reload, navigate, owner.AutomationId, cleanup, hostKind, owner);
        _attachments.Remove(platformView);
        _attachments.Add(platformView, new(Bridges[index]));
        changing = (_, args) =>
        {
            if (ReferenceEquals(args.OldHandler, handler)) DeactivateWebViewBridge(index);
        };
        owner.HandlerChanging += changing;
        if (owner is Microsoft.Maui.Controls.WebView standard)
        {
            navigated = (_, args) => { if (args.Result == WebNavigationResult.Success) Reinitialize(); };
            standard.Navigated += navigated;
        }
        if (owner is HybridWebView hybridView)
        {
            initialized = (_, _) => Reinitialize();
            hybridView.WebViewInitialized += initialized;
        }
        if (handler.MauiContext?.Services.GetService<AgentService>() is { } agent) ConnectAgent(agent);
        _ = InitializeBridgeAsync(index);
#endif
    }

#if MACOS
    protected override Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> func)
    {
        if (Foundation.NSThread.Current.IsMainThread) return func();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(async () =>
        {
            try { completion.TrySetResult(await func()); }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        return completion.Task;
    }
    protected override Task<T> RunOnMainThreadAsync<T>(Func<T> func)
        => RunOnMainThreadAsync(() => Task.FromResult(func()));
    protected override Task RunOnMainThreadAsync(Func<Task> func)
        => RunOnMainThreadAsync(async () => { await func(); return true; });
    protected override Task RunOnMainThreadAsync(Action action)
        => RunOnMainThreadAsync(() => { action(); return true; });
    protected override void PostToMainThread(Action action)
    {
        if (Foundation.NSThread.Current.IsMainThread) action();
        else CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(action);
    }
#endif
    private sealed record Attachment(WebViewBridge Bridge);
}

#if ANDROID
internal sealed class JavaScriptValueCallback(Action<string?> callback) : Java.Lang.Object, global::Android.Webkit.IValueCallback
{
    public void OnReceiveValue(Java.Lang.Object? value) => callback(value?.ToString());
}
#endif
