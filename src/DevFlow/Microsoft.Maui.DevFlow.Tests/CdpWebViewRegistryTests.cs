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
}
