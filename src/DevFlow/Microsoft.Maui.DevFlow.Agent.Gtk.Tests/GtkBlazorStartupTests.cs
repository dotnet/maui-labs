using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Blazor.Gtk;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;

namespace Microsoft.Maui.DevFlow.Agent.Gtk.Tests;

public class GtkBlazorStartupTests
{
    [Fact]
    public async Task DelayedStartup_WiresExistingAndFutureBridges_Once()
    {
        using var agent = new TestAgent();
        using var blazor = new GtkBlazorWebViewDebugService();
        var first = new GtkBlazorWebViewDebugService.GtkWebViewBridge(blazor, null!, "first");
        blazor.AddBridge(first);
        var builder = MauiApp.CreateBuilder();
        builder.Services.AddSingleton<DevFlowAgentService>(agent);
        builder.Services.AddSingleton(blazor);
        IServiceProvider? services = null;
        GtkBlazorDevFlowExtensions.ConfigureStartup(builder, () => services!);

        // The old builder-time, one-shot callback had already given up at this point.
        await Task.Delay(2100);
        Assert.Equal(0, agent.WebViewCount);
        using var app = builder.Build();
        services = app.Services;
        var lifecycle = services.GetRequiredService<ILifecycleEventService>();
        lifecycle.InvokeEvents<GtkWindowCreated>(nameof(GtkWindowCreated), callback => callback(null!));
        Assert.Equal(1, agent.WebViewCount);

        blazor.AddBridge(new GtkBlazorWebViewDebugService.GtkWebViewBridge(blazor, null!, "second"));
        lifecycle.InvokeEvents<GtkWindowCreated>(nameof(GtkWindowCreated), callback => callback(null!));
        blazor.WireToAgent(agent);
        blazor.AddBridge(first);
        Assert.Equal(2, agent.WebViewCount);
        Assert.NotNull(blazor.WebViewLogCallback);

        blazor.Dispose();
        Assert.Equal(0, agent.WebViewCount);
    }

    [Fact]
    public void WireToAgent_BeforeDiscovery_RegistersNewBridgeImmediately()
    {
        using var agent = new TestAgent();
        using var blazor = new GtkBlazorWebViewDebugService();
        blazor.WireToAgent(agent);
        blazor.AddBridge(new GtkBlazorWebViewDebugService.GtkWebViewBridge(blazor, null!, "late"));
        Assert.Equal(1, agent.WebViewCount);
    }

    private sealed class TestAgent : DevFlowAgentService
    {
        public int WebViewCount => GetCdpWebViewsSnapshot().Length;
    }
}
