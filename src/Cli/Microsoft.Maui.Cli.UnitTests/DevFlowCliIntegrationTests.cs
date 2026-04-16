using System.Text.Json;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class DevFlowCliIntegrationTests
{
    private static async Task<(MockAgentServer server, CliTestHarness cli)> CreateFixturesAsync()
    {
        var server = new MockAgentServer();
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);
        return (server, cli);
    }

    [Fact]
    public async Task UiStatus_UsesV1AgentStatusRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "ui", "status", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.True(json.TryGetProperty("agent", out _));
        Assert.True(json.GetProperty("running").GetBoolean());

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/agent/status");
        Assert.Equal("GET", request.Method);
    }

    [Fact]
    public async Task UiTree_WithDepth_UsesV1TreeRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "ui", "tree", "--depth", "2", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        Assert.NotEmpty(server.RecordedRequests);

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/tree");
        Assert.Contains("depth=2", request.QueryString);
    }

    [Fact]
    public async Task UiQuery_ByAutomationId_UsesV1ElementsRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "ui", "query", "--automationId", "ClickMeButton", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.Equal(JsonValueKind.Array, json.ValueKind);

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/elements");
        Assert.Contains("automationId=ClickMeButton", request.QueryString);
    }

    [Fact]
    public async Task UiTap_UsesV1ActionRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "ui", "tap", "el-1", "--json");

        Assert.Equal(0, result.ExitCode);

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/actions/tap");
        Assert.Equal("POST", request.Method);
        Assert.Contains("el-1", request.Body);
    }

    [Fact]
    public async Task StoragePreferencesSet_UsesPutV1Route()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "storage", "preferences", "set", "theme", "dark", "--json");

        Assert.Equal(0, result.ExitCode);

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/preferences/theme");
        Assert.Equal("PUT", request.Method);
        Assert.Contains("dark", request.Body);
    }

    [Fact]
    public async Task DeviceInfo_UsesV1DeviceEndpoint()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "device", "device-info", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.Equal("Apple", json.GetProperty("manufacturer").GetString());

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/device/info");
        Assert.Equal("GET", request.Method);
    }

    [Fact]
    public async Task WebViewBrowserGetVersion_UsesV1EvaluateEndpoint()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "webview", "Browser", "getVersion", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("protocolVersion", result.StdOut);

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
        Assert.Equal("POST", request.Method);
        Assert.Contains("Browser.getVersion", request.Body);
    }
}
