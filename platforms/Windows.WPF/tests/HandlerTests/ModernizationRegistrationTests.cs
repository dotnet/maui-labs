#if !NET10_0
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;

namespace HandlerTests;

public class ModernizationRegistrationTests
{
    [Fact]
    public void UseMauiAppWpf_RegistersPublicGestureFactory()
    {
        using var app = MauiApp.CreateBuilder()
            .UseMauiAppWPF<Microsoft.Maui.Controls.Application>()
            .Build();

        Assert.IsType<WPFGesturePlatformManagerFactory>(
            app.Services.GetRequiredService<IGesturePlatformManagerFactory>());
        Assert.Single(app.Services.GetServices<Microsoft.Maui.Dispatching.IDispatcherProvider>());
        Assert.Single(app.Services.GetServices<IFontRegistrar>());
        Assert.Single(app.Services.GetServices<IEmbeddedFontLoader>());
        Assert.Single(app.Services.GetServices<IFontManager>());
    }

    [Fact]
    public void UseMauiAppWpf_RegistersWpfBlazorServicesForPlatformHandler()
    {
        using var app = MauiApp.CreateBuilder()
            .UseMauiAppWPF<Microsoft.Maui.Controls.Application>()
            .Build();

        // Wpf.BlazorWebView throws on template apply unless AddWpfBlazorWebView's marker is registered.
        var markerType = typeof(Microsoft.AspNetCore.Components.WebView.Wpf.BlazorWebView).Assembly
            .GetType("Microsoft.AspNetCore.Components.WebView.Wpf.WpfBlazorMarkerService", throwOnError: true)!;
        Assert.NotNull(app.Services.GetService(markerType));
    }

    [Fact]
    public void BlazorHandler_ImplementsStandardPublicContract()
    {
        var handler = new Microsoft.AspNetCore.Components.WebView.Maui.WPF.BlazorWebViewHandler();

        Assert.IsAssignableFrom<IBlazorWebViewHandler>(handler);
    }
}
#endif
