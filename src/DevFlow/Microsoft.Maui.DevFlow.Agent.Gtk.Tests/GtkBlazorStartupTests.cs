using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Blazor.Gtk;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Controls;
using BlazorView = Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebView;

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

    [Fact]
    public void Service_DoesNotExposeSingletonCdpCompatibilityApi()
    {
        Assert.Null(typeof(GtkBlazorWebViewDebugService).GetMethod("SendCdpCommandAsync"));
    }

    [Fact]
    public void Dispose_DoesNotUnregisterReplacementBridge()
    {
        using var agent = new TestAgent();
        using var blazor = new GtkBlazorWebViewDebugService();
        blazor.WireToAgent(agent);
        var original = new GtkBlazorWebViewDebugService.GtkWebViewBridge(blazor, null!, "host")
        {
            ElementId = "native-element"
        };
        blazor.AddBridge(original);
        agent.RegisterCdpWebView(_ => Task.FromResult("{}"), () => true, "host", "native-element",
            hostKind: "blazor");

        blazor.Dispose();

        Assert.Equal(1, agent.WebViewCount);
    }

    [Fact]
    public void Registration_SameControlReplacementBeforeOldCleanup_PreservesNewBridge()
    {
        using var agent = new TestAgent();
        using var service = new GtkBlazorWebViewDebugService();
        service.WireToAgent(agent);
        var owner = new BlazorView { AutomationId = "BlazorWebView" };
        var old = new GtkBlazorWebViewDebugService.GtkWebViewBridge(service, null!, owner.AutomationId, owner: owner);
        var replacement = new GtkBlazorWebViewDebugService.GtkWebViewBridge(service, null!, owner.AutomationId, owner: owner);
        service.AddBridge(old);
        var originalIndex = Assert.Single(agent.Contexts).Index;
        service.AddBridge(replacement);

        var current = Assert.Single(agent.Contexts);
        Assert.Equal(originalIndex, current.Index);
        Assert.Same(replacement, current.CommandHandler.Target);
        Assert.True(current.Owner!.TryGetTarget(out var registeredOwner));
        Assert.Same(owner, registeredOwner);

        service.RemoveBridge(old);

        Assert.Same(replacement, Assert.Single(agent.Contexts).CommandHandler.Target);
        service.RemoveBridge(replacement);
        Assert.Empty(agent.Contexts);
    }

    [Fact]
    public void Registration_DistinctControlsWithDuplicateAutomationId_PreservesBothOwners()
    {
        using var agent = new TestAgent();
        using var service = new GtkBlazorWebViewDebugService();
        service.WireToAgent(agent);
        var first = new BlazorView { AutomationId = "duplicate" };
        var second = new BlazorView { AutomationId = "duplicate" };
        service.AddBridge(new(service, null!, first.AutomationId, owner: first));
        service.AddBridge(new(service, null!, second.AutomationId, owner: second));

        Assert.Equal(2, agent.Contexts.Length);
        Assert.NotEqual(agent.Contexts[0].Index, agent.Contexts[1].Index);
        Assert.True(agent.Contexts[0].Owner!.TryGetTarget(out var firstOwner));
        Assert.True(agent.Contexts[1].Owner!.TryGetTarget(out var secondOwner));
        Assert.Same(first, firstOwner);
        Assert.Same(second, secondOwner);
    }

    [Fact]
    public void Registration_BridgeAndRegistry_DoNotKeepControlAlive()
    {
        using var agent = new TestAgent();
        using var service = new GtkBlazorWebViewDebugService();
        service.WireToAgent(agent);
        var owner = RegisterTemporaryOwner(service);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(owner.TryGetTarget(out _));
        Assert.False(Assert.Single(service.Bridges).Owner!.TryGetTarget(out _));
        Assert.False(Assert.Single(agent.Contexts).Owner!.TryGetTarget(out _));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static WeakReference<object> RegisterTemporaryOwner(GtkBlazorWebViewDebugService service)
    {
        var owner = new BlazorView { AutomationId = "temporary" };
        service.AddBridge(new(service, null!, owner.AutomationId, owner: owner));
        return new(owner);
    }

    [Fact]
    public async Task Resolution_ReadyDetachedControls_DoNotReplacePendingActiveMain()
    {
        var activeOwner = new BlazorView { AutomationId = "BlazorWebView" };
        var app = new Application();
        typeof(Application).GetMethod("AddWindow",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(app, [new Window(new ContentPage { Content = activeOwner })]);
        using var agent = new ActiveOwnerAgent(app);
        using var service = new GtkBlazorWebViewDebugService();
        service.WireToAgent(agent);
        var oldOwner = new BlazorView { AutomationId = "BlazorWebView" };
        var rightOwner = new BlazorView { AutomationId = "BlazorRight" };
        static Task<string?> Evaluate(string script)
            => Task.FromResult<string?>(script.Contains("typeof chobitsu") ? "loaded" : "{}");
        var old = new GtkBlazorWebViewDebugService.GtkWebViewBridge(service, null!, oldOwner.AutomationId, Evaluate, owner: oldOwner);
        var right = new GtkBlazorWebViewDebugService.GtkWebViewBridge(service, null!, rightOwner.AutomationId, Evaluate, owner: rightOwner);
        var active = new GtkBlazorWebViewDebugService.GtkWebViewBridge(service, null!, activeOwner.AutomationId, Evaluate, owner: activeOwner);
        service.AddBridge(old);
        service.AddBridge(right);
        service.AddBridge(active);
        await old.InitializeAsync();
        await right.InitializeAsync();

        var mainContextId = Assert.Single(await agent.ActiveContextsAsync()).ContextId;
        var defaultContext = await agent.ResolveAsync(null);
        var namedContext = await agent.ResolveAsync(mainContextId);
        Assert.NotNull(defaultContext);
        Assert.Equal(defaultContext!.Index, namedContext!.Index);
        Assert.Same(active, defaultContext.CommandHandler.Target);
        Assert.False(defaultContext.IsReady);
        Assert.Equal(defaultContext.Index, Assert.Single(await agent.ActiveContextsAsync()).Index);

        await active.InitializeAsync();

        defaultContext = await agent.ResolveAsync(null);
        namedContext = await agent.ResolveAsync(mainContextId);
        Assert.True(defaultContext!.IsReady);
        Assert.Equal(defaultContext.Index, namedContext!.Index);
        Assert.Same(active, defaultContext.CommandHandler.Target);
        Assert.Equal(defaultContext.Index, Assert.Single(await agent.ActiveContextsAsync()).Index);
    }

    private sealed class TestAgent : DevFlowAgentService
    {
        public int WebViewCount => GetCdpWebViewsSnapshot().Length;
        public CdpWebViewInfo[] Contexts => GetCdpWebViewsSnapshot();
    }

    private sealed class ActiveOwnerAgent : MauiDevFlowAgentService
    {
        public ActiveOwnerAgent(Application app) => _app = app;
        protected override bool IsMainThreadDispatchRequired() => false;
        public async Task<CdpWebViewInfo?> ResolveAsync(string? id)
            => ResolveCdpWebView(id, await GetActiveWebViewAutomationIdsAsync());
        public async Task<CdpWebViewInfo[]> ActiveContextsAsync()
        {
            var ids = await GetActiveWebViewAutomationIdsAsync();
            return GetCdpWebViewsSnapshot().Where(context => IsActiveCdpWebView(context, ids)).ToArray();
        }
    }
}
