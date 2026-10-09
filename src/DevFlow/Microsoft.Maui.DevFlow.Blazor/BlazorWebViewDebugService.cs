using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.WebView;
using Microsoft.Maui.Handlers;
#if MACOS
using BlazorHandler = Microsoft.Maui.Platforms.MacOS.Handlers.BlazorWebViewHandler;
#elif ANDROID || IOS || MACCATALYST || WINDOWS
using BlazorHandler = Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebViewHandler;
#endif

namespace Microsoft.Maui.DevFlow.Blazor;

/// <summary>Captures Blazor handlers without replacing their native navigation/message infrastructure.</summary>
public class BlazorWebViewDebugService : WebViewDebugService
{
    private static int _configured;
    public BlazorWebViewDebugService() { }
    public BlazorWebViewDebugService(BlazorWebViewDebugOptions options) : base(options) { }

    internal static void ConfigureHandlers()
    {
        if (Interlocked.Exchange(ref _configured, 1) != 0) return;
#if MACOS
        BlazorHandler.Mapper.AppendToMapping("DevFlow.BlazorWebView", (handler, view) => Capture(handler));
#elif ANDROID || IOS || MACCATALYST || WINDOWS
        BlazorHandler.BlazorWebViewMapper.AppendToMapping("DevFlow.BlazorWebView", (handler, view) => Capture(handler));
#endif
    }

    private static void Capture(IViewHandler handler)
    {
        if (handler.VirtualView is not VisualElement owner || handler.PlatformView is null) return;
        handler.MauiContext?.Services.GetService<BlazorWebViewDebugService>()
            ?.AttachNativeWebView(handler, owner, handler.PlatformView, "blazor");
    }

    protected override async Task<bool> IsHostReadyAsync(Func<string, Task<string?>> evaluate, CancellationToken token)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var ready = await evaluate("(function(){const app=document.querySelector('#app');return (app && app.children.length > 0 && !app.textContent.includes('Loading...')) ? 'ready' : 'waiting';})()");
            if (ready == "ready") return true;
            await Task.Delay(250, token);
        }
        return false;
    }

    protected override async Task NavigateAsync(WebViewBridge bridge, string url)
    {
        if (HasExplicitUriScheme(url))
        {
            await base.NavigateAsync(bridge, url);
            return;
        }
        await bridge.EvaluateAsync($"(function(){{const target={JsonSerializer.Serialize(url)};if(window.Blazor && typeof Blazor.navigateTo === 'function'){{Blazor.navigateTo(target);}}else{{location.href=target;}}}})()");
    }
}
