using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.Network;

namespace Microsoft.Maui.DevFlow.Tests;

public class WebViewNetworkCapabilityTests
{
    private const string UnsupportedReason =
        "Browser-network capture is not implemented. Use /api/v1/network/requests for native .NET HTTP traffic.";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WebViewNetwork_ReturnsUnsupportedWithoutReadingNativeTraffic(bool registerWebView)
    {
        using var service = CreateService(out var port);
        if (registerWebView)
            service.RegisterCdpWebView(_ => throw new InvalidOperationException("Network capture must not invoke CDP."), () => true);
        var entry = AddNativeRequest(service);
        service.StartServerOnly(dispatcher: null);
        using var http = CreateClient(port);

        using var response = await http.GetAsync("/api/v1/webview/network?host=native.test&contextId=0");

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = document.RootElement;
        Assert.Equal(3, body.EnumerateObject().Count());
        Assert.Equal("not_supported", body.GetProperty("error").GetString());
        Assert.Equal("webview.network", body.GetProperty("capability").GetString());
        Assert.Equal(UnsupportedReason, body.GetProperty("reason").GetString());
        Assert.Same(entry, service.NetworkStore.GetById(entry.Id));
        Assert.Equal(1, service.NetworkStore.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capabilities_NeverAdvertiseBrowserNetworkAndPreserveNativeNetwork(bool registerWebView)
    {
        using var service = CreateService(out var port);
        if (registerWebView)
            service.RegisterCdpWebView(_ => Task.FromResult("{}"), () => true);
        service.StartServerOnly(dispatcher: null);
        using var http = CreateClient(port);
        using var document = JsonDocument.Parse(await http.GetStringAsync("/api/v1/agent/capabilities"));
        var capabilities = document.RootElement.GetProperty("capabilities");
        var webView = capabilities.GetProperty("webview");

        Assert.Equal(registerWebView, webView.GetProperty("supported").GetBoolean());
        var features = webView.GetProperty("features").EnumerateArray().Select(feature => feature.GetString()).ToArray();
        Assert.DoesNotContain("network", features);
        if (registerWebView)
            Assert.Equal(new[] { "evaluate", "contexts", "source", "dom", "dom-query", "console", "screenshot" }, features);
        else
            Assert.Empty(features);
        var network = capabilities.GetProperty("network");
        Assert.True(network.GetProperty("supported").GetBoolean());
        Assert.Equal(new[] { "list", "detail", "clear", "stream" },
            network.GetProperty("features").EnumerateArray().Select(feature => feature.GetString()));
    }

    [Fact]
    public async Task NativeNetwork_StillReturnsFilteredSummariesAndRequestDetails()
    {
        using var service = CreateService(out var port);
        var entry = AddNativeRequest(service);
        service.NetworkStore.Add(new NetworkRequestEntry
        {
            Id = "other-request", Host = "other.test", Method = "POST", StatusCode = 201
        });
        service.StartServerOnly(dispatcher: null);
        using var http = CreateClient(port);

        using var response = await http.GetAsync("/api/v1/network/requests?host=native.test&method=GET&status=200&limit=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var summaries = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var summary = Assert.Single(summaries.RootElement.EnumerateArray());
        Assert.Equal(entry.Id, summary.GetProperty("id").GetString());
        Assert.Equal(entry.Url, summary.GetProperty("url").GetString());
        Assert.Equal(200, summary.GetProperty("statusCode").GetInt32());
        Assert.False(summary.TryGetProperty("responseBody", out _));

        using var detailResponse = await http.GetAsync($"/api/v1/network/requests/{entry.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal(entry.ResponseBody, detail.RootElement.GetProperty("responseBody").GetString());
        Assert.Equal(2, service.NetworkStore.Count);
    }

    private static NetworkRequestEntry AddNativeRequest(DevFlowAgentService service)
    {
        var entry = new NetworkRequestEntry
        {
            Id = "native-request",
            Timestamp = DateTimeOffset.UtcNow,
            Host = "native.test",
            Url = "https://native.test/data",
            Method = "GET",
            StatusCode = 200,
            ResponseBody = "native response"
        };
        service.NetworkStore.Add(entry);
        return entry;
    }

    private static DevFlowAgentService CreateService(out int port)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return new DevFlowAgentService(new AgentOptions { Port = port, EnableNetworkMonitoring = false });
    }

    private static HttpClient CreateClient(int port)
        => new() { BaseAddress = new Uri($"http://localhost:{port}") };
}
