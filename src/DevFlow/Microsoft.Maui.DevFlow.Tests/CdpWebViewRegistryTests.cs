using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class CdpWebViewRegistryTests
{
    [Fact]
    public async Task RegisterCdpWebView_ConcurrentUniqueBridges_AssignsUniqueIndexes()
    {
        using var service = new DevFlowAgentService();

        var indexes = await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() =>
            service.RegisterCdpWebView(
                _ => Task.FromResult("{}"),
                () => true,
                automationId: $"webview-{i}"))));

        Assert.Equal(100, indexes.Distinct().Count());
    }

    [Fact]
    public async Task RegisterCdpWebView_SameOwner_ReusesSlotAndUpdatesDelegates()
    {
        using var service = new RegistryService();
        var owner = new object();

        var first = service.RegisterCdpWebView(
            _ => Task.FromResult("first"),
            () => false,
            automationId: "BlazorWebView", owner: owner);
        var second = service.RegisterCdpWebView(
            _ => Task.FromResult("second"),
            () => true,
            automationId: "BlazorWebView", owner: owner);

        Assert.Equal(first, second);
        var registered = Assert.Single(service.Snapshot());
        Assert.Equal("second", await registered.CommandHandler("{}"));
        Assert.True(registered.IsReady);
    }

    [Fact]
    public void RegisterCdpWebView_DifferentOwnersWithDuplicateAutomationId_KeepsBothContexts()
    {
        using var service = new RegistryService();
        var firstOwner = new object();
        var secondOwner = new object();

        var first = service.RegisterCdpWebView(
            _ => Task.FromResult("{}"), () => true, "duplicate", null, null, "webview", firstOwner);
        var second = service.RegisterCdpWebView(
            _ => Task.FromResult("{}"), () => true, "duplicate", null, null, "hybrid", secondOwner);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task UnregisterCdpWebView_OldDelegate_DoesNotRemoveReplacement()
    {
        using var service = new RegistryService();
        var owner = new object();
        Func<string, Task<string>> oldHandler = _ => Task.FromResult("old");
        Func<string, Task<string>> newHandler = _ => Task.FromResult("new");
        var index = service.RegisterCdpWebView(oldHandler, () => true, null, null, null, "hybrid", owner);
        var replacement = service.RegisterCdpWebView(newHandler, () => true, null, null, null, "hybrid", owner);

        service.UnregisterCdpWebView(index, oldHandler);

        Assert.Equal(index, replacement);
        Assert.Equal("new", await Assert.Single(service.Snapshot()).CommandHandler("{}"));
        service.UnregisterCdpWebView(index, newHandler);
        Assert.Empty(service.Snapshot());
    }

    private sealed class RegistryService : DevFlowAgentService
    {
        public CdpWebViewInfo[] Snapshot() => GetCdpWebViewsSnapshot();
    }
}
