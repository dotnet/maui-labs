using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Extensibility;

namespace Microsoft.Maui.DevFlow.Tests;

public class DevFlowHostPluginTests
{
    [Fact]
    public async Task HostOperations_RunWithoutAnAgent_AndRequireExplicitTargets()
    {
        var state = new HostState();
        using var services = new ServiceCollection().AddSingleton(state).AddDevFlowHostExtension<HostPlugin>()
            .AddDevFlowHostExtension<HostPlugin>().BuildServiceProvider();
        await using var host = DevFlowHostPlugins.ForServices(services);
        var providers = await host.DescribeAsync();
        var descriptor = Assert.Single(providers).Value;
        Assert.Equal("host", descriptor.ExecutionLocation);
        Assert.Equal("ready", descriptor.State);
        Assert.Equal(1, state.Starts);
        Assert.Contains(descriptor.Tools, t => t.Name == "device" && t.Target == "device");

        var response = await host.InvokeAsync("host:com.example.host", "local", null);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("host", JsonDocument.Parse(response.Body!).RootElement.GetString());

        response = await host.InvokeAsync("host:com.example.host", "device", null);
        Assert.Equal(400, response.StatusCode);
        Assert.Contains("explicitly selected device", response.Body);
        response = await host.InvokeAsync("host:com.example.host", "device", null, new(DeviceId: "chosen-device"));
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("chosen-device", state.Device);
        var page = await host.ReadStreamAsync("host:com.example.host", "events", 0, 100);
        Assert.Single(JsonDocument.Parse(page.Body!).RootElement.GetProperty("events").EnumerateArray());
    }

    [Fact]
    public async Task FailedHostRegistrationBatch_DoesNotPartiallyInstall_AndCanBeRetried()
    {
        var state = new HostState();
        var fail = true;
        using var services = new ServiceCollection()
            .AddSingleton(new DevFlowHostExtensionRegistration(typeof(HostPlugin), _ => new HostPlugin(state)))
            .AddSingleton(new DevFlowHostExtensionRegistration(typeof(RecoveredHostPlugin), _ =>
                fail ? throw new InvalidOperationException("constructor failed") : new RecoveredHostPlugin()))
            .BuildServiceProvider();
        await using var host = DevFlowHostPlugins.ForServices(services);

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.DescribeAsync());
        Assert.False(host.HasPlugins);
        Assert.Equal(0, state.Starts);
        fail = false;
        Assert.Equal(2, (await host.DescribeAsync()).Count);
        Assert.Equal(1, state.Starts);
    }

    [Fact]
    public async Task DuplicateHostRegistrationBatch_DoesNotPublishEarlierProviders()
    {
        var state = new HostState();
        using var services = new ServiceCollection()
            .AddSingleton(new DevFlowHostExtensionRegistration(typeof(HostPlugin), _ => new HostPlugin(state)))
            .AddSingleton(new DevFlowHostExtensionRegistration(typeof(RecoveredHostPlugin), _ => new HostPlugin(state)))
            .BuildServiceProvider();
        await using var host = DevFlowHostPlugins.ForServices(services);

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.DescribeAsync());
        Assert.False(host.HasPlugins);
        Assert.Equal(0, state.Starts);
    }

    [Fact]
    public async Task AgentTargetedHostMutation_UsesTheSameLease_AndRejectsMismatchedTargets()
    {
        var port = FreePort();
        using var agent = new DevFlowAgentService(new AgentOptions { Port = port });
        agent.StartServerOnly(null);
        var state = new HostState();
        using var services = new ServiceCollection().AddSingleton(state).AddDevFlowHostExtension<HostPlugin>().BuildServiceProvider();
        await using var host = DevFlowHostPlugins.ForServices(services);
        using var owner = new AgentClient("localhost", port) { MutationLeaseId = "owner" };
        using var contender = new AgentClient("localhost", port) { MutationLeaseId = "contender" };
        Assert.True((await owner.ControlMutationLeaseAsync("claim")).YouHold);
        var read = Assert.Single((await host.DescribeAsync())["host:com.example.host"].Tools, t => t.Name == "agent-read");
        Assert.True(read.Annotations!.ReadOnly);
        Assert.Equal(200, (await host.InvokeAsync("host:com.example.host", "agent-read", null,
            new(AgentHost: "localhost", AgentPort: port), contender)).StatusCode);
        Assert.True((await owner.ControlMutationLeaseAsync("status")).YouHold);

        await Assert.ThrowsAsync<MutationLeaseException>(() =>
            host.InvokeAsync("host:com.example.host", "agent", null, new(AgentHost: "localhost", AgentPort: port), contender));
        Assert.Equal(0, state.Mutations);
        var wrong = await host.InvokeAsync("host:com.example.host", "agent", null,
            new(AgentHost: "localhost", AgentPort: port + 1), owner);
        Assert.Equal(400, wrong.StatusCode);
        Assert.Equal(0, state.Mutations);

        var result = await host.InvokeAsync("host:com.example.host", "agent", null,
            new(AgentHost: "localhost", AgentPort: port), owner);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(1, state.Mutations);
    }

    [Fact]
    public async Task HostPlugin_IsNotActivatedByAnApp_AndStopsExactlyOnceInItsHost()
    {
        var state = new HostState();
        var options = new AgentOptions { Port = FreePort() };
        options.AddExtension(new HostPlugin(state));
        using var agent = new DevFlowAgentService(options);
        agent.StartServerOnly(null);
        Assert.Contains("Host plugins must be installed", Assert.Single(agent.ExtensionLoadErrors).Error);
        Assert.Equal(0, state.Starts);

        using var services = new ServiceCollection().AddSingleton(state).AddDevFlowHostExtension<HostPlugin>().BuildServiceProvider();
        var host = DevFlowHostPlugins.ForServices(services);
        await host.DescribeAsync();
        await host.DisposeAsync();
        await host.DisposeAsync();
        Assert.Equal(1, state.Stops);
    }

    [Fact]
    public async Task HostShutdown_WaitsForLateStartup_AndSharesCleanupAcrossCallers()
    {
        var state = new PendingHostState();
        using var services = new ServiceCollection().AddSingleton(state).AddDevFlowHostExtension<PendingHostPlugin>()
            .BuildServiceProvider();
        var host = DevFlowHostPlugins.ForServices(services);
        using var cancellation = new CancellationTokenSource();
        var starting = host.DescribeAsync(cancellation.Token);
        try
        {
            await state.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
            Assert.Equal("starting", Assert.Single(await host.DescribeAsync()).Value.State);
            Assert.Equal(503, (await host.InvokeAsync("host:com.example.pending", "read", null)).StatusCode);
            var firstStop = host.DisposeAsync().AsTask();
            var secondStop = host.DisposeAsync().AsTask();
            Assert.False(firstStop.IsCompleted);
            Assert.Same(firstStop, secondStop);
            Assert.Equal(0, state.Stops);
            state.Release.TrySetResult();
            await Task.WhenAll(firstStop, secondStop).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, state.Stops);
        }
        finally
        {
            state.Release.TrySetResult();
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task Catalog_BindsLegacyRouteValues_AndHandlesNullStreamArguments()
    {
        var catalog = new DevFlowOperationCatalog();
        catalog.MapTool("lookup", "Lookup", "GET", "/things/{id}", request =>
        {
            Assert.Equal("/things/a%2Fb", request.Path);
            Assert.Equal("a/b", request.RouteValues["id"]);
            Assert.False(request.Query.ContainsKey("id"));
            Assert.Equal("2", request.Query["limit"]);
            return Task.FromResult(DevFlowResponse.Ok());
        });
        var events = new DevFlowEventStream();
        events.Publish(12);
        catalog.MapStream("events", "Events", events);
        catalog.Freeze();
        Assert.Throws<InvalidOperationException>(() => catalog.MapGet("/late", _ => Task.FromResult(DevFlowResponse.Ok())));
        await catalog.InvokeAsync("lookup", new(JsonDocument.Parse("""{"id":"a/b","limit":2}""").RootElement));
        var page = await catalog.InvokeAsync("events", new(JsonDocument.Parse("null").RootElement));
        Assert.Single(JsonDocument.Parse(page.Body!).RootElement.GetProperty("events").EnumerateArray());
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.InvokeAsync("lookup", new(JsonDocument.Parse("[]").RootElement)));
    }

    [Fact]
    public async Task ApprovedAgentBindings_MoveToolsAndStreams_WithoutBypassingMutationLeases()
    {
        var options = new AgentOptions { Port = FreePort() };
        options.ProviderPolicy.Allow("com.example.thermo", new("/api/v1/thermometer"));
        options.ProviderPolicy.Allow("com.example.stream", new("/api/v1/readings", "/ws/v1/readings"));
        var thermometer = new DevFlowExtensionPluginTests.Thermometer();
        var plugin = new DevFlowExtensionPluginTests.StreamPlugin(10);
        options.AddExtension(new DevFlowExtensionPluginTests.ThermometerPlugin(thermometer));
        options.AddExtension(plugin);
        using var agent = new DevFlowAgentService(options);
        agent.StartServerOnly(null);
        using var client = new AgentClient("localhost", options.Port);
        var providers = await client.GetOperationProvidersAsync();
        var set = providers["com.example.thermo"].Tools.Single(t => t.Name == "set");
        Assert.Equal("/api/v1/thermometer/set", set.Path);
        using var http = new HttpClient();
        using var denied = await http.PostAsync($"http://localhost:{options.Port}{set.Path}",
            new StringContent("""{"celsius":12}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        await client.CallExtensionToolAsync(set.Method, set.Path, JsonDocument.Parse("""{"celsius":12}""").RootElement);
        Assert.Equal(12, thermometer.Celsius);
        var stream = Assert.Single(providers["com.example.stream"].Streams);
        Assert.Equal("/api/v1/readings/streams/events", stream.Path);
        Assert.Contains("/ws/v1/readings/streams/events?since=0", client.GetExtensionStreamWebSocketUrl(stream, 0));
        plugin.Events.Publish(new("reading", 12));
        Assert.Single((await client.GetExtensionEventsAsync(stream)).Events);
        var operations = new ExtensionOperations(client, options.ProviderPolicy);
        Assert.Single((await operations.ReadEventsAsync("com.example.stream", "events", 0, 100)).Events);
        Assert.Throws<InvalidOperationException>(() => options.ProviderPolicy.Allow("com.example.late", new("/api/v1/late")));
    }

    [Theory]
    [InlineData("/status")]
    [InlineData("/{name}")]
    public async Task ApprovedRouteConflicts_DoNotReplaceBuiltIns_OrPartiallyInstallRoutes(string route)
    {
        var options = new AgentOptions { Port = FreePort() };
        options.ProviderPolicy.Allow("com.example.conflict", new("/api/v1/agent"));
        options.AddExtension(new ConflictingPlugin(route));
        using var agent = new DevFlowAgentService(options);
        agent.StartServerOnly(null);
        using var client = new AgentClient("localhost", options.Port);
        var descriptor = (await client.GetExtensionsAsync())["com.example.conflict"];
        Assert.Equal("faulted", descriptor.State);
        Assert.Contains("conflicts with an existing route", descriptor.Error);
        Assert.Equal("Microsoft.Maui.DevFlow.Agent", (await client.GetStatusAsync())!.Agent!.Name);
        using var http = new HttpClient();
        using var unmapped = await http.GetAsync($"http://localhost:{options.Port}/api/v1/agent/not-installed");
        Assert.Equal(HttpStatusCode.NotFound, unmapped.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/ext/com.example.safe/../agent/status")]
    [InlineData("/api/v1/ext/com.example.safe/%2e%2e/status")]
    [InlineData("/api/v1/ext/com.example.safe/%2f..%2fstatus")]
    [InlineData("/api/v1/ext/com.example.safe_other/status")]
    [InlineData("/api/v1/agent/status")]
    public void ProviderPolicy_DefaultScopeCannotBeEscaped(string path)
        => Assert.False(new DevFlowProviderPolicy().AllowsPath("com.example.safe", path));

    [Fact]
    public async Task SharedDiscovery_IncludesBuiltInsAndActions_WithoutChangingLegacyDiscoveryOrInvocation()
    {
        var port = FreePort();
        var options = new AgentOptions { Port = port, RequireMutationLease = false };
        options.RegisterExtension("com.example.inline", "Inline").MapGet("/ping", _ => Task.FromResult(HttpResponse.Json(new { ok = true })));
        using var agent = new DevFlowAgentService(options);
        agent.StartServerOnly(null);
        using var client = new AgentClient("localhost", port);
        Assert.Single(await client.GetExtensionsAsync());
        var providers = await client.GetOperationProvidersAsync();
        Assert.Equal("/api/v1/agent/status", providers["devflow.agent"].Tools.Single(t => t.Name == "status").Path);
        var action = Assert.Single(providers["devflow.actions"].Tools, t => t.Name == "plugin_catalog_echo");
        var reply = await client.CallExtensionToolAsync(action.Method, action.Path,
            JsonDocument.Parse("""{"args":["catalog"]}""").RootElement);
        Assert.Equal("catalog", JsonDocument.Parse(reply).RootElement.GetProperty("returnValue").GetString());
    }

    [DevFlowAction("plugin_catalog_echo", Description = "Echo for shared catalog coverage")]
    public static string Echo(string value) => value;

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public sealed class HostState
    {
        public int Starts;
        public int Stops;
        public int Mutations;
        public string? Device;
    }

    public sealed class ConflictingPlugin(string route) : DevFlowExtension("com.example.conflict", "Conflicting routes")
    {
        protected internal override void Configure(IDevFlowExtensionBuilder builder)
        {
            builder.MapGet("/not-installed", _ => Task.FromResult(DevFlowResponse.Text("partially installed")));
            builder.MapGet(route, _ => Task.FromResult(DevFlowResponse.Text("shadowed")));
        }
    }

    public sealed class PendingHostState
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Stops;
    }

    [Fact]
    public async Task ProviderRoutes_LiteralAndParameterizedEndpoints_CanCoexist()
    {
        var options = new AgentOptions { Port = FreePort() };
        var provider = options.RegisterExtension("com.example.routes", "Route precedence");
        provider.MapGet("/items/{id}", request => Task.FromResult(HttpResponse.Json(request.RouteParams["id"])));
        provider.MapGet("/items/status", _ => Task.FromResult(HttpResponse.Json("status")));
        using var agent = new DevFlowAgentService(options);
        agent.StartServerOnly(null);
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{options.Port}") };

        Assert.Equal("\"status\"", await http.GetStringAsync("/api/v1/ext/com.example.routes/items/status"));
        Assert.Equal("\"42\"", await http.GetStringAsync("/api/v1/ext/com.example.routes/items/42"));
    }

    public sealed class PendingHostPlugin(PendingHostState state) : DevFlowHostExtension("com.example.pending", "Pending startup")
    {
        protected internal override void Configure(IDevFlowExtensionBuilder builder)
            => builder.MapOperation("read", "Read", _ => Task.FromResult(DevFlowResponse.Ok()));
        protected internal override Task StartAsync(IDevFlowExtensionContext context, CancellationToken cancellationToken)
        {
            state.Started.TrySetResult();
            return state.Release.Task;
        }
        protected internal override Task StopAsync(CancellationToken cancellationToken)
        {
            state.Stops++;
            return Task.CompletedTask;
        }
    }

    public sealed class RecoveredHostPlugin() : DevFlowHostExtension("com.example.recovered", "Recovered batch")
    {
        protected internal override void Configure(IDevFlowExtensionBuilder builder)
            => builder.MapOperation("read", "Read", _ => Task.FromResult(DevFlowResponse.Ok()));
    }

    public sealed class HostPlugin(HostState state) : DevFlowHostExtension("com.example.host", "Host test plugin")
    {
        public DevFlowEventStream Events { get; } = new();
        protected internal override void Configure(IDevFlowExtensionBuilder builder)
        {
            builder.MapOperation("local", "Local read", _ => Task.FromResult(DevFlowResponse.Json("host")),
                annotations: new() { ReadOnly = true });
            builder.MapOperation("device", "Device operation", request =>
            {
                state.Device = request.Target!.DeviceId;
                return Task.FromResult(DevFlowResponse.Ok());
            }, target: DevFlowTargetRequirement.Device);
            builder.MapOperation("agent", "Agent mutation", _ =>
            {
                state.Mutations++;
                return Task.FromResult(DevFlowResponse.Ok());
            }, target: DevFlowTargetRequirement.Agent);
            ((DevFlowOperationCatalog)builder).Register(new("agent-read", "Agent read",
                _ => Task.FromResult(DevFlowResponse.Ok()), Target: DevFlowTargetRequirement.Agent, Method: "GET",
                Annotations: new() { ReadOnly = false }));
            builder.MapStream("events", "Host events", Events);
        }
        protected internal override Task StartAsync(IDevFlowExtensionContext context, CancellationToken cancellationToken)
        {
            state.Starts++;
            Events.Publish("started");
            return Task.CompletedTask;
        }
        protected internal override Task StopAsync(CancellationToken cancellationToken)
        {
            state.Stops++;
            return Task.CompletedTask;
        }
    }
}
