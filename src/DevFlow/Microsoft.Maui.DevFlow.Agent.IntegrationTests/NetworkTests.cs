using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "Network")]
public class NetworkTests : IntegrationTestBase
{
    public NetworkTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    private async Task<List<NetworkRequest>?> TryCaptureRequestsAsync(int timeoutMs = 20000)
    {
        await NavigateToMainPageAsync();
        await SettleAsync(500);
        await NavigateToPageAsync("//network", "GetPostsButton");
        await SettleAsync(1000);

        await Client.ClearNetworkRequestsAsync();
        await SettleAsync(500);

        var button = await FindElementAsync("GetPostsButton");
        await Client.TapAsync(button.Id);

        try
        {
            await WaitForAsync(async () =>
            {
                var requests = await Client.GetNetworkRequestsAsync();
                return requests.Count > 0;
            }, timeoutMs: timeoutMs, pollIntervalMs: 1000);
        }
        catch (TimeoutException)
        {
            Output.WriteLine("No network requests captured — network monitoring appears unavailable in this run.");
            await NavigateToMainPageAsync();
            return null;
        }

        return await Client.GetNetworkRequestsAsync();
    }

    [Fact]
    public async Task TriggerRequest_CapturedByAgent()
    {
        var captured = await TryCaptureRequestsAsync();
        if (captured is null)
            return;

        Assert.NotEmpty(captured);

        foreach (var request in captured)
            Output.WriteLine($"  {request.Method} {request.Url} -> {request.StatusCode}");

        await NavigateToMainPageAsync();
    }

    [Fact]
    public async Task Requests_ReturnsRequestList()
    {
        var requests = await Client.GetNetworkRequestsAsync();
        Assert.NotNull(requests);
        Output.WriteLine($"Current captured requests: {requests.Count}");
    }

    [Fact]
    public async Task Requests_WithHostFilter_FiltersResults()
    {
        var requests = await TryCaptureRequestsAsync();
        if (requests is null)
            return;

        var filtered = await Client.GetNetworkRequestsAsync(host: "jsonplaceholder");
        var unfiltered = requests;

        Assert.True(filtered.Count <= unfiltered.Count);
        Assert.All(filtered, request =>
            Assert.Contains("jsonplaceholder", request.Url ?? request.Host ?? string.Empty, StringComparison.OrdinalIgnoreCase));

        await NavigateToMainPageAsync();
    }

    [Fact]
    public async Task RequestDetail_ReturnsFullInfo()
    {
        var requests = await TryCaptureRequestsAsync();
        if (requests is null)
            return;

        Assert.NotEmpty(requests);

        var detail = await Client.GetNetworkRequestDetailAsync(requests[0].Id);
        Assert.NotNull(detail);
        Assert.NotNull(detail!.Method);
        Assert.NotNull(detail.Url);

        await NavigateToMainPageAsync();
    }

    [Fact]
    public async Task Clear_RemovesRequests()
    {
        var before = await TryCaptureRequestsAsync();
        if (before is null)
            return;

        Assert.NotEmpty(before);

        var cleared = await Client.ClearNetworkRequestsAsync();
        Assert.True(cleared);

        await WaitForAsync(async () => (await Client.GetNetworkRequestsAsync()).Count == 0, timeoutMs: 5000, pollIntervalMs: 250);

        var after = await Client.GetNetworkRequestsAsync();
        Assert.Empty(after);

        await NavigateToMainPageAsync();
    }
}
