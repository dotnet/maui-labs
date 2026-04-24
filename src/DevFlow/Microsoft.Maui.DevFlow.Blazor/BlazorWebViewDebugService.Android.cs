#if ANDROID
using global::Android.Webkit;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Handlers;
using AWebView = global::Android.Webkit.WebView;

namespace Microsoft.Maui.DevFlow.Blazor;

/// <summary>
/// Android implementation of the Blazor WebView debug service.
/// Uses Android.Webkit.WebView.EvaluateJavascript with a callback wrapper.
/// </summary>
public class BlazorWebViewDebugService : BlazorWebViewDebugServiceBase
{
    public BlazorWebViewDebugService() { }

    /// <summary>
    /// Override to give Android WebView more time to load Blazor and inject chobitsu.
    /// </summary>
    protected override int GetWebViewLoadDelayMs() => 5000;

    public override void ConfigureHandler()
    {
        Log("[BlazorDevFlow] ConfigureHandler called (Android)");

        BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping("ChobitsuDebug", async (handler, view) =>
        {
            Log("[BlazorDevFlow] ChobitsuDebug mapper callback triggered (Android)");

            if (handler.PlatformView is AWebView androidWebView)
            {
                androidWebView.Settings.JavaScriptEnabled = true;
                var automationId = (handler.VirtualView as VisualElement)?.AutomationId;
                var idx = AddWebViewBridge(
                    (script) =>
                    {
                        var tcs = new TaskCompletionSource<string?>();
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            try
                            {
                                androidWebView.EvaluateJavascript(script, new JsValueCallback(value =>
                                {
                                    if (value == "null" || value == null)
                                        tcs.TrySetResult(null);
                                    else
                                    {
                                        if (value.StartsWith("\"") && value.EndsWith("\""))
                                        {
                                            try { value = System.Text.Json.JsonSerializer.Deserialize<string>(value) ?? value; }
                                            catch { value = value[1..^1]; }
                                        }
                                        tcs.TrySetResult(value);
                                    }
                                }));
                            }
                            catch (Exception ex) { tcs.TrySetException(ex); }
                        });
                        return tcs.Task;
                    },
                    () => MainThread.BeginInvokeOnMainThread(() => androidWebView.Reload()),
                    (url) => MainThread.BeginInvokeOnMainThread(() => androidWebView.LoadUrl(url)),
                    automationId);
                Log($"[BlazorDevFlow] Android WebView captured as bridge {idx} (automationId={automationId})");

                // Install WebViewClient to detect page load completion and re-inject
                // API level 26 is required, but MAUI min target is API 24. Suppress warning as MAUI
                // practically requires a higher level and this is a dev-time tool.
#pragma warning disable CA1416
                var existingClient = androidWebView.WebViewClient;
#pragma warning restore CA1416
                androidWebView.SetWebViewClient(new DevFlowWebViewClient(this, idx, existingClient));

                await InitializeBridgeAsync(idx);
            }
            else
            {
                Log($"[BlazorDevFlow] PlatformView is not Android WebView: {handler.PlatformView?.GetType().Name ?? "null"}");
            }
        });
    }
}

/// <summary>
/// Wraps Android's IValueCallback for async JavaScript evaluation.
/// </summary>
internal class JsValueCallback : Java.Lang.Object, IValueCallback
{
    private readonly Action<string?> _callback;

    public JsValueCallback(Action<string?> callback)
    {
        _callback = callback;
    }

    public void OnReceiveValue(Java.Lang.Object? value)
    {
        _callback(value?.ToString());
    }
}

/// <summary>
/// Custom WebViewClient that detects page load completion and triggers chobitsu re-injection.
/// </summary>
internal class DevFlowWebViewClient : WebViewClient
{
    private readonly BlazorWebViewDebugServiceBase _service;
    private readonly int _bridgeIndex;
    private readonly WebViewClient? _innerClient;

    public DevFlowWebViewClient(BlazorWebViewDebugServiceBase service, int bridgeIndex, WebViewClient? innerClient)
    {
        _service = service;
        _bridgeIndex = bridgeIndex;
        _innerClient = innerClient;
    }

    public override void OnPageFinished(AWebView? view, string? url)
    {
        base.OnPageFinished(view, url);
        _innerClient?.OnPageFinished(view, url);

        if (url?.Contains("about:blank") == false)
        {
            var bridge = _bridgeIndex >= 0 && _bridgeIndex < _service.Bridges.Count
                ? _service.Bridges[_bridgeIndex]
                : null;

            // Blazor in-app route changes often keep the same JS runtime alive. If the
            // bridge is already responsive, avoid flipping it back to not-ready and let
            // the on-demand recovery path handle genuine reloads.
            if (bridge?.IsReady == true)
            {
                _service.Log($"[BlazorDevFlow] Android OnPageFinished: {url} (bridge {_bridgeIndex} already ready, skipping reset)");
                return;
            }

            _service.Log($"[BlazorDevFlow] Android OnPageFinished: {url} — re-initializing bridge {_bridgeIndex}");
            _ = Task.Run(async () =>
            {
                try
                {
                    await _service.ResetAndReinitializeBridgeAsync(_bridgeIndex);
                }
                catch (Exception ex)
                {
                    _service.LogError($"[BlazorDevFlow] Failed to reinitialize bridge {_bridgeIndex}", ex);
                }
            });
        }
    }

    public override WebResourceResponse? ShouldInterceptRequest(AWebView? view, IWebResourceRequest? request)
    {
        return _innerClient?.ShouldInterceptRequest(view, request) ?? base.ShouldInterceptRequest(view, request);
    }

    public override bool ShouldOverrideUrlLoading(AWebView? view, IWebResourceRequest? request)
    {
        return _innerClient?.ShouldOverrideUrlLoading(view, request) ?? base.ShouldOverrideUrlLoading(view, request);
    }
}
#endif
