using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

/// <summary>
/// Tap-side coverage for the portable protocol client, run on both the .NET Framework and modern
/// target families so the two cannot drift in how an action request is shaped or how the agent's
/// answer is interpreted.
/// </summary>
public class AgentClientTapTests
{
    [Fact]
    public async Task TapAsync_PostsElementIdAndReportsSuccess()
    {
        using var agent = FakeAgent.StartJson("""{ "success": true }""");
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var tapped = await client.TapAsync("el-1");

        Assert.True(tapped);
        var request = Assert.Single(agent.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/api/v1/ui/actions/tap", request.Path);
        Assert.StartsWith("application/json", request.Headers["Content-Type"]);

        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("el-1", body.RootElement.GetProperty("elementId").GetString());
        Assert.False(body.RootElement.TryGetProperty("captureEpoch", out _));
    }

    [Fact]
    public async Task TapAsync_IncludesCaptureMetadataWhenSupplied()
    {
        using var agent = FakeAgent.StartJson("""{ "success": true }""");
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        await client.TapAsync("el-9", captureEpoch: 12, registryGeneration: 5);

        var request = Assert.Single(agent.Requests);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("el-9", body.RootElement.GetProperty("elementId").GetString());
        Assert.Equal(12, body.RootElement.GetProperty("captureEpoch").GetInt64());
        Assert.Equal(5, body.RootElement.GetProperty("registryGeneration").GetInt64());
    }

    [Fact]
    public async Task TapAsync_AgentReportedFailureIsFalse()
    {
        using var agent = FakeAgent.StartJson("""{ "success": false }""");
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        Assert.False(await client.TapAsync("missing"));
    }

    [Fact]
    public async Task TapAsync_HttpErrorIsFalse()
    {
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json(
            """{ "error": "element_not_found" }""", statusCode: 404));
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        Assert.False(await client.TapAsync("missing"));
    }

    [Fact]
    public async Task TapAsync_UnreachableAgentIsFalseRatherThanThrowing()
    {
        int port;
        using (var agent = FakeAgent.StartJson("""{ "success": true }"""))
            port = agent.Port; // Disposed immediately: nothing is listening on this port anymore.

        using var client = new AgentClient("localhost", port) { AutoAcquireMutationLease = false };

        Assert.False(await client.TapAsync("el-1"));
    }

    [Fact]
    public async Task TapResultAsync_SurfacesStructuredFailure()
    {
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json(
            """{ "error": "not_supported", "capability": "ui.tap", "reason": "Backend has no tap support." }""",
            statusCode: 501));
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var result = await client.TapResultAsync("el-1", captureEpoch: null, registryGeneration: null);

        Assert.False(result.Success);
        Assert.Equal(501, result.StatusCode);
        Assert.False(result.TransportFailure);
        Assert.Equal("not_supported", result.Reason);
    }

    [Fact]
    public async Task TapResultAsync_SuccessCarriesNoFailureDetail()
    {
        using var agent = FakeAgent.StartJson("""{ "success": true }""");
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var result = await client.TapResultAsync("el-1", captureEpoch: null, registryGeneration: null);

        Assert.True(result.Success);
        Assert.False(result.TransportFailure);
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task FillAsync_PostsTextPayload()
    {
        using var agent = FakeAgent.StartJson("""{ "success": true }""");
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        Assert.True(await client.FillAsync("entry-1", "hello world"));

        var request = Assert.Single(agent.Requests);
        Assert.Equal("/api/v1/ui/actions/fill", request.Path);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("entry-1", body.RootElement.GetProperty("elementId").GetString());
        Assert.Equal("hello world", body.RootElement.GetProperty("text").GetString());
    }
}
