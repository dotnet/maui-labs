using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Runs a real MCP server and client over in-memory pipes against a real agent with a plugin, so the
/// test covers what an MCP client actually sees: plugin tools in tools/list, list_changed
/// notifications, and calls routed to the plugin.
/// </summary>
public class DevFlowExtensionMcpToolTests
{
    [Fact]
    public async Task PluginTools_AppearInToolsList_AndAreCallable()
    {
        var port = GetFreePort();
        var services = new ServiceCollection()
            .AddSingleton(new DevFlowExtensionPluginTests.Thermometer { Celsius = 100 })
            .BuildServiceProvider();
        var agentOptions = new AgentOptions { Port = port, Services = services };
        agentOptions.AddExtension<DevFlowExtensionPluginTests.ThermometerPlugin>();
        agentOptions.AddExtension(new DevFlowExtensionPluginTests.StreamPlugin(capacity: 10));
        using var agent = new DevFlowAgentService(agentOptions);
        agent.StartServerOnly(null);

        await using var mcp = await McpHarness.StartAsync(new McpAgentSession { DefaultAgentPort = port });
        var listChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = mcp.Client.RegisterNotificationHandler(
            NotificationMethods.ToolListChangedNotification,
            (_, _) => { listChanged.TrySetResult(); return ValueTask.CompletedTask; });

        Assert.DoesNotContain(await mcp.Client.ListToolsAsync(), t => t.Name.StartsWith(ExtensionToolCatalog.ToolNamePrefix, StringComparison.Ordinal));

        await mcp.Catalog.RefreshAsync(force: true);
        await listChanged.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var tools = await mcp.Client.ListToolsAsync();
        var read = Assert.Single(tools, t => t.Name == "ext_temp_read");
        Assert.True(read.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.Contains("Reads the temperature.", read.Description);
        Assert.Contains(tools, t => t.Name == "ext_temp_set");
        Assert.Contains(tools, t => t.Name == "ext_stream_events");

        var result = await mcp.Client.CallToolAsync("ext_temp_read", new Dictionary<string, object?> { ["unit"] = "f" });
        Assert.NotEqual(true, result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        using var json = JsonDocument.Parse(text);
        Assert.Equal(212, json.RootElement.GetProperty("value").GetDouble(), 1);
    }

    [Fact]
    public async Task PluginTools_AreRemoved_WhenTheAgentGoesAway()
    {
        var port = GetFreePort();
        var agentOptions = new AgentOptions { Port = port };
        agentOptions.AddExtension(new DevFlowExtensionPluginTests.StreamPlugin(capacity: 10));
        var agent = new DevFlowAgentService(agentOptions);
        agent.StartServerOnly(null);

        await using var mcp = await McpHarness.StartAsync(new McpAgentSession { DefaultAgentPort = port });
        await mcp.Catalog.RefreshAsync(force: true);
        Assert.Contains("ext_stream_events", mcp.Catalog.PublishedToolNames);

        await agent.StopAsync();
        agent.Dispose();

        await mcp.Catalog.RefreshAsync(force: true);
        Assert.Empty(mcp.Catalog.PublishedToolNames);
        Assert.DoesNotContain(await mcp.Client.ListToolsAsync(), t => t.Name == "ext_stream_events");
    }

    [Fact]
    public async Task SelectingAnAgent_RefreshesImmediately_AfterAFailedRefresh()
    {
        var port = GetFreePort();
        var agentOptions = new AgentOptions { Port = port };
        agentOptions.AddExtension(new DevFlowExtensionPluginTests.StreamPlugin(capacity: 10));
        using var agent = new DevFlowAgentService(agentOptions);
        agent.StartServerOnly(null);

        // Startup with no reachable target (in real use: several apps, none selected) fails...
        var session = new McpAgentSession { DefaultAgentPort = GetFreePort() };
        await using var mcp = await McpHarness.StartAsync(session);
        await mcp.Catalog.RefreshAsync(force: true);
        Assert.Empty(mcp.Catalog.PublishedToolNames);

        // ...and maui_select_agent follows within the refresh throttle. It must not be skipped.
        session.DefaultAgentPort = port;
        await mcp.Catalog.RefreshAsync();
        Assert.Contains("ext_stream_events", mcp.Catalog.PublishedToolNames);
    }

    [Fact]
    public void ToolNames_UsePrefixes_AndFallBackToNamespacesOnCollision()
    {
        var catalog = new ExtensionToolCatalog(new McpAgentSession(), Options.Create(new McpServerOptions { ToolCollection = [] }));

        catalog.Sync(new Dictionary<string, ExtensionDescriptor>
        {
            ["org.shiny.ble"] = Descriptor(prefix: null, "status"),
            ["com.contoso.ble"] = Descriptor(prefix: null, "status"),
            ["com.contoso.gps"] = Descriptor(prefix: "Loc-8", "Fix Now"),
            ["com.contoso.broken"] = Descriptor(prefix: null, "status", state: "faulted")
        }, hash: "h", agentUrl: "http://localhost:1");

        Assert.Equal(
            ["ext_com_contoso_ble_status", "ext_loc_8_fix_now", "ext_org_shiny_ble_status"],
            catalog.PublishedToolNames.Order(StringComparer.Ordinal));

        Assert.Equal(64, ExtensionToolCatalog.BuildToolName(new string('a', 80), "tool").Length);
    }

    static ExtensionDescriptor Descriptor(string? prefix, string tool, string? state = null) => new()
    {
        Version = "1.0.0",
        ToolPrefix = prefix,
        State = state,
        Tools = [new ExtensionToolInfo { Name = tool, Method = "GET", Path = $"/api/v1/ext/x/{tool}" }]
    };

    static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    sealed class McpHarness : IAsyncDisposable
    {
        readonly CancellationTokenSource _cts = new();
        McpServer _server = null!;
        Task _serverTask = Task.CompletedTask;

        public McpClient Client { get; private set; } = null!;
        public ExtensionToolCatalog Catalog { get; private set; } = null!;

        public static async Task<McpHarness> StartAsync(McpAgentSession session)
        {
            var harness = new McpHarness();
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            var options = new McpServerOptions
            {
                ServerInfo = new() { Name = "maui-test", Version = "1.0.0" },
                ToolCollection = []
            };

            harness.Catalog = new ExtensionToolCatalog(session, Options.Create(options));
            harness._server = McpServer.Create(
                new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()),
                options);
            harness._serverTask = harness._server.RunAsync(harness._cts.Token);
            harness.Client = await McpClient.CreateAsync(
                new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()));
            return harness;
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            _cts.Cancel();
            try { await _serverTask; } catch (OperationCanceledException) { }
            await _server.DisposeAsync();
            _cts.Dispose();
        }
    }
}
