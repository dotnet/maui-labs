using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.DevFlow.Extensibility;
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
    public async Task PluginToolErrors_ReachTheMcpClient_WithThePluginsMessage()
    {
        var port = GetFreePort();
        var services = new ServiceCollection().AddSingleton(new DevFlowExtensionPluginTests.Thermometer()).BuildServiceProvider();
        var agentOptions = new AgentOptions { Port = port, Services = services, RequireMutationLease = false };
        agentOptions.AddExtension<DevFlowExtensionPluginTests.ThermometerPlugin>();
        using var agent = new DevFlowAgentService(agentOptions);
        agent.StartServerOnly(null);

        await using var mcp = await McpHarness.StartAsync(new McpAgentSession { DefaultAgentPort = port });
        await mcp.Catalog.RefreshAsync(force: true);

        // An AI agent can only correct a refused call if it sees why it was refused.
        var result = await mcp.Client.CallToolAsync("ext_temp_set", new Dictionary<string, object?> { ["celsius"] = "hot" });
        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("celsius must be a number", text);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrustedHostTools_ArePublishedAndCallable_WithoutAConnectedApp(bool approvedPrefix)
    {
        var policy = new DevFlowProviderPolicy();
        if (approvedPrefix) policy.Allow("host:com.example.host", new(mcpToolPrefix: "maui_host"));
        using var services = new ServiceCollection().AddSingleton(policy).AddSingleton(new DevFlowHostPluginTests.HostState())
            .AddDevFlowHostExtension<DevFlowHostPluginTests.HostPlugin>().BuildServiceProvider();
        await using var host = DevFlowHostPlugins.ForServices(services);
        await using var mcp = await McpHarness.StartAsync(new McpAgentSession { DefaultAgentPort = GetFreePort() }, host);
        await mcp.Catalog.RefreshAsync(force: true);
        var tool = Assert.Single(await mcp.Client.ListToolsAsync(), t => t.Name == (approvedPrefix ? "maui_host_local" : "ext_host_host_local"));
        Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);
        var result = await mcp.Client.CallToolAsync(tool.Name);
        Assert.NotEqual(true, result.IsError);
        Assert.Equal("\"host\"", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task HostMutationLeaseConflict_ReachesTheMcpClient_WithoutInvokingThePlugin()
    {
        var port = GetFreePort();
        using var agent = new DevFlowAgentService(new AgentOptions { Port = port });
        agent.StartServerOnly(null);
        using var owner = new AgentClient("localhost", port) { MutationLeaseId = "other-session" };
        Assert.True((await owner.ControlMutationLeaseAsync("claim")).YouHold);
        var state = new DevFlowHostPluginTests.HostState();
        using var services = new ServiceCollection().AddSingleton(state)
            .AddDevFlowHostExtension<DevFlowHostPluginTests.HostPlugin>().BuildServiceProvider();
        await using var host = DevFlowHostPlugins.ForServices(services);
        await using var mcp = await McpHarness.StartAsync(new McpAgentSession { DefaultAgentPort = port }, host);
        await mcp.Catalog.RefreshAsync(force: true);

        var result = await mcp.Client.CallToolAsync("ext_host_host_agent");

        Assert.True(result.IsError);
        Assert.Contains("Another DevFlow session is driving this app", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Equal(0, state.Mutations);
    }

    [Fact]
    public async Task ApprovedBindings_RequireIndependentHostApproval_AndCannotBeSelfDeclared()
    {
        const string ns = "devflow.approved";
        var bindings = new DevFlowProviderBindings("/api/v1/approved", mcpToolPrefix: "maui_custom");
        var options = new AgentOptions { Port = GetFreePort() };
        options.ProviderPolicy.Allow(ns, bindings);
        var extension = options.RegisterExtension(ns, "Approved provider");
        extension.ToolPrefix = "maui_custom";
        extension.MapGet("/ping", _ => Task.FromResult(HttpResponse.Json(new { ok = true })));
        var normal = options.RegisterExtension("com.example.normal", "Not approved");
        normal.ToolPrefix = "maui";
        normal.MapGet("/ping", _ => Task.FromResult(HttpResponse.Json(new { ok = true })));
        using var agent = new DevFlowAgentService(options);
        agent.StartServerOnly(null);

        using var defaultServices = new ServiceCollection().BuildServiceProvider();
        await using var defaultHost = DevFlowHostPlugins.ForServices(defaultServices);
        await using (var unapproved = await McpHarness.StartAsync(new McpAgentSession { DefaultAgentPort = options.Port }, defaultHost))
        {
            await unapproved.Catalog.RefreshAsync(force: true);
            Assert.Contains("ext_maui_custom_get_ping", unapproved.Catalog.PublishedToolNames);
            var denied = await unapproved.Client.CallToolAsync("ext_maui_custom_get_ping");
            Assert.True(denied.IsError);
            Assert.Contains("outside the bindings approved", Assert.IsType<TextContentBlock>(Assert.Single(denied.Content)).Text);
        }

        var policy = new DevFlowProviderPolicy().Allow(ns, bindings);
        using var services = new ServiceCollection().AddSingleton(policy).BuildServiceProvider();
        await using var host = DevFlowHostPlugins.ForServices(services);
        await using var mcp = await McpHarness.StartAsync(new McpAgentSession { DefaultAgentPort = options.Port }, host);
        await mcp.Catalog.RefreshAsync(force: true);
        Assert.Contains("maui_custom_get_ping", mcp.Catalog.PublishedToolNames);
        Assert.Contains("ext_maui_get_ping", mcp.Catalog.PublishedToolNames);
        Assert.NotEqual(true, (await mcp.Client.CallToolAsync("maui_custom_get_ping")).IsError);
        var status = await mcp.Client.CallToolAsync("maui_agent_status");
        Assert.NotEqual(true, status.IsError);
        Assert.Equal("Microsoft.Maui.DevFlow.Agent", JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(status.Content)).Text)
            .RootElement.GetProperty("agent").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ApprovedMcpNameConflicts_DoNotReplaceExistingTools()
    {
        var policy = new DevFlowProviderPolicy()
            .Allow("com.example.approved", new(mcpToolPrefix: "maui"))
            .Allow("com.example.other", new(mcpToolPrefix: "maui"));
        using var services = new ServiceCollection().AddSingleton(policy).BuildServiceProvider();
        await using var host = DevFlowHostPlugins.ForServices(services);
        var existing = McpServerTool.Create(() => "original", new() { Name = "maui_status" });
        var options = new McpServerOptions { ToolCollection = [existing] };
        var catalog = new ExtensionToolCatalog(new McpAgentSession(), Options.Create(options)) { HostPlugins = host };
        Assert.Throws<InvalidOperationException>(() => catalog.Sync(new()
        {
            ["com.example.approved"] = Descriptor(null, "status")
        }));
        Assert.Same(existing, Assert.Single(options.ToolCollection));
        Assert.Empty(catalog.PublishedToolNames);
        Assert.Throws<InvalidOperationException>(() => catalog.Sync(new()
        {
            ["com.example.approved"] = Descriptor(null, "status"),
            ["com.example.other"] = Descriptor(null, "status")
        }));
        Assert.Same(existing, Assert.Single(options.ToolCollection));
    }

    [Fact]
    public void ToolNames_PreserveAllLossyLongAndConcatenationCollisions()
    {
        var catalog = new ExtensionToolCatalog(new McpAgentSession(), Options.Create(new McpServerOptions { ToolCollection = [] }));
        var providers = new Dictionary<string, ExtensionDescriptor>
        {
            ["com.example.a"] = Descriptor("a", "b_c"),
            ["com.example.ab"] = Descriptor("a_b", "c"),
            ["com.example.lossy"] = new() { Tools = [
                new() { Name = "read!" }, new() { Name = "read?" },
                new() { Name = new string('x', 90) + "a" }, new() { Name = new string('x', 90) + "b" }
            ] }
        };
        catalog.Sync(providers);
        var names = catalog.PublishedToolNames.Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(6, names.Length);
        Assert.All(names, name => Assert.InRange(name.Length, 1, 64));
        catalog.Sync(providers);
        Assert.Equal(names, catalog.PublishedToolNames.Order(StringComparer.Ordinal));
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
        });

        Assert.Equal(
            new[] { "ext_com_contoso_ble_status", ExtensionToolCatalog.BuildToolName("loc_8", "Fix Now", "com.contoso.gps\nFix Now"), "ext_org_shiny_ble_status" }.Order(StringComparer.Ordinal),
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

        public static async Task<McpHarness> StartAsync(McpAgentSession session, DevFlowHostPlugins? host = null)
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
            if (host is not null) harness.Catalog.HostPlugins = host;
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
