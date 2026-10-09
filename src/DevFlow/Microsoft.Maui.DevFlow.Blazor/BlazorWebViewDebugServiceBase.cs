using System.Text.Json;
using Microsoft.Maui.DevFlow.WebView;

namespace Microsoft.Maui.DevFlow.Blazor;

/// <summary>Blazor readiness and routing over the shared, host-neutral WebView CDP engine.</summary>
public abstract class BlazorWebViewDebugServiceBase : WebViewDebugService
{
    protected BlazorWebViewDebugServiceBase() { }
    protected BlazorWebViewDebugServiceBase(BlazorWebViewDebugOptions options) : base(options) { }

    protected override async Task<bool> IsHostReadyAsync(Func<string, Task<string?>> evaluate, CancellationToken token)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var ready = await evaluate("(function(){const app=document.querySelector('#app');return (!app || (app.children.length > 0 && !app.textContent.includes('Loading...'))) ? 'ready' : 'waiting';})()");
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
