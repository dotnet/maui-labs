using System.Net.Http;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

public class AgentClientOperationTests
{
    [Theory]
    [InlineData("GET", "/api/v1/ext/example/items/a%2Fb?limit=2")]
    [InlineData("POST", "/api/v1/ext/example/items/a%2Fb")]
    public async Task CallExtensionToolAsync_BindsAndEscapesRouteParameters(string method, string target)
    {
        using var agent = FakeAgent.Start(request => FakeAgent.Response.Json(
            request.Path == "/api/v1/agent/lease" ? """{"youHold":true}""" : "{}"));
        using var client = new AgentClient("localhost", agent.Port);
        using var args = JsonDocument.Parse("""{"id":"a/b","limit":2}""");

        await client.CallExtensionToolAsync(method, "/api/v1/ext/example/items/{id}", args.RootElement);

        Assert.Equal(target, Assert.Single(agent.Requests, request => request.Path != "/api/v1/agent/lease").Target);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"id":42}""")]
    [InlineData("""{"id":"."}""")]
    [InlineData("""{"id":".."}""")]
    public async Task CallExtensionToolAsync_InvalidRouteParameters_AreRejectedBeforeSending(string parameters)
    {
        using var agent = FakeAgent.StartJson("{}");
        using var client = new AgentClient("localhost", agent.Port);
        using var args = JsonDocument.Parse(parameters);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CallExtensionToolAsync("GET", "/api/v1/ext/example/items/{id}", args.RootElement));

        Assert.Empty(agent.Requests);
    }

    [Fact]
    public async Task GetOperationProvidersAsync_OlderAgentFallsBackToCapabilities()
    {
        using var agent = FakeAgent.Start(request => request.Path == "/api/v1/operations"
            ? FakeAgent.Response.Json("""{"error":"not found"}""", 404)
            : FakeAgent.Response.Json("""{"extensions":{"com.example.legacy":{"version":"1.0.0"}}}"""));
        using var client = new AgentClient("localhost", agent.Port);

        Assert.Equal("com.example.legacy", Assert.Single(await client.GetOperationProvidersAsync()).Key);
        Assert.Equal(new[] { "/api/v1/operations", "/api/v1/agent/capabilities" }, agent.Requests.Select(r => r.Path));
    }

    [Fact]
    public async Task GetOperationProvidersAsync_ServerFailureDoesNotFallBack()
    {
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json("""{"error":"catalog failed"}""", 500));
        using var client = new AgentClient("localhost", agent.Port);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetOperationProvidersAsync());
        Assert.Contains("catalog failed", error.Message);
        Assert.Single(agent.Requests);
    }

    [Fact]
    public async Task GetExtensionEventsAsync_PluginFailureIsNotAnEmptySuccessPage()
    {
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json("""{"error":"plugin still starting"}""", 503));
        using var client = new AgentClient("localhost", agent.Port);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetExtensionEventsAsync("com.example.events", "events"));
        Assert.Contains("plugin still starting", error.Message);
    }

    [Fact]
    public async Task StreamDescriptor_UsesAdvertisedPollingAndWebSocketBindings()
    {
        using var agent = FakeAgent.StartJson("""{"events":[],"nextSequence":12,"dropped":0}""");
        using var client = new AgentClient("localhost", agent.Port);
        var stream = new ExtensionStreamInfo { Path = "/api/v1/readings/stream", WebSocketPath = "/ws/v1/readings/stream" };

        Assert.Equal(12, (await client.GetExtensionEventsAsync(stream, 10, 25)).NextSequence);
        Assert.Equal("/api/v1/readings/stream?since=10&limit=25", Assert.Single(agent.Requests).Target);
        Assert.Equal($"ws://localhost:{agent.Port}/ws/v1/readings/stream?since=10", client.GetExtensionStreamWebSocketUrl(stream, 10));
    }

    [Fact]
    public async Task GetExtensionEventsAsync_NullPageIsRejected()
    {
        using var agent = FakeAgent.StartJson("null");
        using var client = new AgentClient("localhost", agent.Port);

        await Assert.ThrowsAsync<JsonException>(() => client.GetExtensionEventsAsync("com.example.events", "events"));
    }

    [Theory]
    [InlineData("/../escape")]
    [InlineData("/%2f..%2fescape")]
    [InlineData("/%5cescape")]
    public async Task StreamDescriptor_RejectsTraversalBeforeSendingRequests(string suffix)
    {
        using var agent = FakeAgent.StartJson("""{"events":[]}""");
        using var client = new AgentClient("localhost", agent.Port);
        var stream = new ExtensionStreamInfo
        {
            Path = "/api/v1/readings" + suffix,
            WebSocketPath = "/ws/v1/readings" + suffix
        };

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetExtensionEventsAsync(stream));
        Assert.Throws<ArgumentException>(() => client.GetExtensionStreamWebSocketUrl(stream));
        Assert.Empty(agent.Requests);
    }
}
