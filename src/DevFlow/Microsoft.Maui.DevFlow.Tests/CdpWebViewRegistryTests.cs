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
    public async Task RegisterCdpWebView_SameAutomationId_ReusesSlotAndUpdatesDelegates()
    {
        using var service = new DevFlowAgentService();

        var first = service.RegisterCdpWebView(
            _ => Task.FromResult("first"),
            () => false,
            automationId: "BlazorWebView");
        var second = service.RegisterCdpWebView(
            _ => Task.FromResult("second"),
            () => true,
            automationId: "BlazorWebView");

        Assert.Equal(first, second);
        Assert.NotNull(service.CdpCommandHandler);
        Assert.Equal("second", await service.CdpCommandHandler!("{}"));
        Assert.True(service.CdpReadyCheck!());
    }

    [Fact]
    public void RegisterCdpWebView_DifferentOwnersWithDuplicateAutomationId_KeepsBothContexts()
    {
        using var service = new DevFlowAgentService();
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
        using var service = new DevFlowAgentService();
        var owner = new object();
        Func<string, Task<string>> oldHandler = _ => Task.FromResult("old");
        Func<string, Task<string>> newHandler = _ => Task.FromResult("new");
        var index = service.RegisterCdpWebView(oldHandler, () => true, null, null, null, "hybrid", owner);
        var replacement = service.RegisterCdpWebView(newHandler, () => true, null, null, null, "hybrid", owner);

        service.UnregisterCdpWebView(index, oldHandler);

        Assert.Equal(index, replacement);
        Assert.Equal("new", await service.CdpCommandHandler!("{}"));
        service.UnregisterCdpWebView(index, newHandler);
        Assert.Null(service.CdpCommandHandler);
    }
}
