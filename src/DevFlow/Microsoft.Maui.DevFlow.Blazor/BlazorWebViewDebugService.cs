using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
#if MACOS
using BlazorHandler = Microsoft.Maui.Platforms.MacOS.Handlers.BlazorWebViewHandler;
#elif ANDROID || IOS || MACCATALYST || WINDOWS
using BlazorHandler = Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebViewHandler;
#endif

namespace Microsoft.Maui.DevFlow.Blazor;

/// <summary>Captures Blazor handlers without replacing their native navigation/message infrastructure.</summary>
public class BlazorWebViewDebugService : BlazorWebViewDebugServiceBase
{
    private static int _configured;
    public BlazorWebViewDebugService() { }
    public BlazorWebViewDebugService(BlazorWebViewDebugOptions options) : base(options) { }

    public override void ConfigureHandler() => ConfigureHandlers();

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
}
