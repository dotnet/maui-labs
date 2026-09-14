#if IOS || MACCATALYST
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Handlers;
using WebKit;

namespace Microsoft.Maui.DevFlow.Blazor;

/// <summary>
/// iOS/Mac Catalyst implementation of the Blazor WebView debug service.
/// Uses WKWebView for JavaScript evaluation.
/// </summary>
public class BlazorWebViewDebugService : BlazorWebViewDebugServiceBase
{
    private readonly object _webViewGate = new();
    private readonly Dictionary<WKWebView, int> _webViewIndexes =
        new(ReferenceEqualityComparer.Instance);

    public BlazorWebViewDebugService() { }

    public override void ConfigureHandler()
    {
        Log("[BlazorDevFlow] ConfigureHandler called, appending to BlazorWebViewMapper");

        BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping("ChobitsuDebug", async (handler, view) =>
        {
            Log("[BlazorDevFlow] ChobitsuDebug mapper callback triggered");

            if (handler.PlatformView is WKWebView wkWebView)
            {
                var automationId = (handler.VirtualView as VisualElement)?.AutomationId;
                await Task.Delay(100);
                if (!ReferenceEquals(handler.PlatformView, wkWebView))
                {
                    Log($"[BlazorDevFlow] Skipping superseded WKWebView (automationId={automationId})");
                    return;
                }

                var visualElement = handler.VirtualView as VisualElement;
                EventHandler<HandlerChangingEventArgs>? handlerChanging = null;
                var idx = -1;
                void CleanupBridge()
                {
                    PostToMainThread(() =>
                    {
                        if (visualElement is not null && handlerChanging is not null)
                            visualElement.HandlerChanging -= handlerChanging;
                        lock (_webViewGate)
                            _webViewIndexes.Remove(wkWebView);
                        Log($"[BlazorDevFlow] WKWebView bridge {idx} deactivated.");
                    });
                }

                lock (_webViewGate)
                {
                    if (_webViewIndexes.TryGetValue(wkWebView, out idx))
                    {
                        Log($"[BlazorDevFlow] WKWebView already captured as bridge {idx} (automationId={automationId})");
                        return;
                    }

                    idx = AddWebViewBridge(
                        async (script) =>
                        {
                            var result = await wkWebView.EvaluateJavaScriptAsync(script);
                            return result?.ToString();
                        },
                        () => wkWebView.Reload(),
                        (url) =>
                        {
                            var request = new Foundation.NSUrlRequest(new Foundation.NSUrl(url));
                            wkWebView.LoadRequest(request);
                        },
                        automationId,
                        CleanupBridge);
                    _webViewIndexes.Add(wkWebView, idx);
                }
                Log($"[BlazorDevFlow] WKWebView captured as bridge {idx} (automationId={automationId})");

                if (visualElement is not null)
                {
                    handlerChanging = (_, args) =>
                    {
                        if (!ReferenceEquals(args.OldHandler, handler))
                            return;

                        DeactivateWebViewBridge(idx);
                    };
                    visualElement.HandlerChanging += handlerChanging;
                }

                await InitializeBridgeAsync(idx);
            }
            else
            {
                Log($"[BlazorDevFlow] PlatformView is not WKWebView: {handler.PlatformView?.GetType().Name ?? "null"}");
            }
        });
    }
}
#elif MACOS
using CoreFoundation;
using Foundation;
using Microsoft.Maui.Handlers;
using WebKit;
using MacOSBlazorHandler = Microsoft.Maui.Platforms.MacOS.Handlers.BlazorWebViewHandler;

namespace Microsoft.Maui.DevFlow.Blazor;

/// <summary>
/// macOS AppKit implementation of the Blazor WebView debug service.
/// Uses WKWebView for JavaScript evaluation via the macOS backend's BlazorWebViewHandler.
/// Overrides main thread dispatch since MainThread.InvokeOnMainThreadAsync is not available
/// on the macOS AppKit backend (uses netstandard reference assembly).
/// </summary>
public class BlazorWebViewDebugService : BlazorWebViewDebugServiceBase
{
    private readonly object _webViewGate = new();
    private readonly Dictionary<WKWebView, int> _webViewIndexes =
        new(ReferenceEqualityComparer.Instance);

    public BlazorWebViewDebugService() { }

    protected override Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> func)
    {
        if (NSThread.Current.IsMainThread)
            return func();

        var tcs = new TaskCompletionSource<T>();
        DispatchQueue.MainQueue.DispatchAsync(async () =>
        {
            try { tcs.SetResult(await func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    protected override Task<T> RunOnMainThreadAsync<T>(Func<T> func)
    {
        if (NSThread.Current.IsMainThread)
            return Task.FromResult(func());

        var tcs = new TaskCompletionSource<T>();
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    protected override Task RunOnMainThreadAsync(Func<Task> func)
    {
        if (NSThread.Current.IsMainThread)
            return func();

        var tcs = new TaskCompletionSource();
        DispatchQueue.MainQueue.DispatchAsync(async () =>
        {
            try { await func(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    protected override Task RunOnMainThreadAsync(Action action)
    {
        if (NSThread.Current.IsMainThread)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource();
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    protected override void PostToMainThread(Action action)
    {
        if (NSThread.Current.IsMainThread)
            action();
        else
            DispatchQueue.MainQueue.DispatchAsync(action);
    }

    public override void ConfigureHandler()
    {
        Log("[BlazorDevFlow] ConfigureHandler called, appending to macOS BlazorWebViewHandler mapper");

        MacOSBlazorHandler.Mapper.AppendToMapping("ChobitsuDebug", async (handler, view) =>
        {
            Log("[BlazorDevFlow] ChobitsuDebug mapper callback triggered");

            if (handler.PlatformView is WKWebView wkWebView)
            {
                var automationId = (handler.VirtualView as VisualElement)?.AutomationId;
                await Task.Delay(100);
                if (!ReferenceEquals(handler.PlatformView, wkWebView))
                {
                    Log($"[BlazorDevFlow] Skipping superseded WKWebView (automationId={automationId})");
                    return;
                }

                var visualElement = handler.VirtualView as VisualElement;
                EventHandler<HandlerChangingEventArgs>? handlerChanging = null;
                Action<WKWebView>? platformViewDisconnected = null;
                var idx = -1;
                void CleanupBridge()
                {
                    PostToMainThread(() =>
                    {
                        if (visualElement is not null && handlerChanging is not null)
                            visualElement.HandlerChanging -= handlerChanging;
                        if (platformViewDisconnected is not null)
                            handler.PlatformViewDisconnected -= platformViewDisconnected;
                        lock (_webViewGate)
                            _webViewIndexes.Remove(wkWebView);
                        Log($"[BlazorDevFlow] WKWebView bridge {idx} deactivated.");
                    });
                }

                lock (_webViewGate)
                {
                    if (_webViewIndexes.TryGetValue(wkWebView, out idx))
                    {
                        Log($"[BlazorDevFlow] WKWebView already captured as bridge {idx} (automationId={automationId})");
                        return;
                    }

                    idx = AddWebViewBridge(
                        async (script) =>
                        {
                            var result = await wkWebView.EvaluateJavaScriptAsync(script);
                            return result?.ToString();
                        },
                        () => wkWebView.Reload(),
                        (url) =>
                        {
                            var request = new NSUrlRequest(new NSUrl(url));
                            wkWebView.LoadRequest(request);
                        },
                        automationId,
                        CleanupBridge);
                    _webViewIndexes.Add(wkWebView, idx);
                }
                Log($"[BlazorDevFlow] WKWebView captured as bridge {idx} (automationId={automationId})");

                if (visualElement is not null)
                {
                    handlerChanging = (_, args) =>
                    {
                        if (!ReferenceEquals(args.OldHandler, handler))
                            return;

                        DeactivateWebViewBridge(idx);
                    };
                    visualElement.HandlerChanging += handlerChanging;
                }

                platformViewDisconnected = disconnectedView =>
                {
                    if (ReferenceEquals(disconnectedView, wkWebView))
                        DeactivateWebViewBridge(idx);
                };
                handler.PlatformViewDisconnected += platformViewDisconnected;

                await InitializeBridgeAsync(idx);
            }
            else
            {
                Log($"[BlazorDevFlow] PlatformView is not WKWebView: {handler.PlatformView?.GetType().Name ?? "null"}");
            }
        });
    }
}
#endif
