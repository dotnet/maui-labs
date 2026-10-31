using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Extensibility;

namespace Microsoft.Maui.DevFlow.Tests;

public class DevFlowExtensionPluginTests
{
    [Fact]
    public async Task Plugin_IsCreatedFromServices_AndItsToolsAreCallable()
    {
        var port = GetFreePort();
        var services = new ServiceCollection()
            .AddSingleton(new Thermometer { Celsius = 21.5 })
            .BuildServiceProvider();
        var options = new AgentOptions { Port = port, Services = services, RequireMutationLease = false };
        options.AddExtension<ThermometerPlugin>();

        using var service = new DevFlowAgentService(options);
        using var client = new AgentClient("localhost", port);
        service.StartServerOnly(null);

        var extensions = await client.GetExtensionsAsync();
        var (ns, descriptor) = Assert.Single(extensions);
        Assert.Equal("com.example.thermo", ns);
        Assert.Equal("ready", descriptor.State);
        Assert.Equal("temp", descriptor.ToolPrefix);

        var read = descriptor.Tools.Single(t => t.Name == "read");
        Assert.True(read.Annotations!.ReadOnly);
        using (var json = JsonDocument.Parse(await client.CallExtensionToolAsync(read.Method, read.Path, Json("""{"unit":"f"}"""))))
        {
            Assert.Equal(70.7, json.RootElement.GetProperty("value").GetDouble(), 1);
            Assert.Equal("f", json.RootElement.GetProperty("unit").GetString());
        }

        // GET tool parameters arrive as query values; booleans keep their JSON spelling.
        var query = descriptor.Tools.Single(t => t.Name == "query");
        using (var json = JsonDocument.Parse(await client.CallExtensionToolAsync(query.Method, query.Path, Json("""{"flag":true,"n":2.5}"""))))
        {
            Assert.Equal("true", json.RootElement.GetProperty("flag").GetString());
            Assert.Equal("2.5", json.RootElement.GetProperty("n").GetString());
        }

        var set = descriptor.Tools.Single(t => t.Name == "set");
        using (var json = JsonDocument.Parse(await client.CallExtensionToolAsync(set.Method, set.Path, Json("""{"celsius":30}"""))))
            Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(30, services.GetRequiredService<Thermometer>().Celsius);
    }

    [Fact]
    public async Task Plugin_WriteTools_RequireTheMutationLease()
    {
        var port = GetFreePort();
        var options = new AgentOptions { Port = port, Services = ThermometerServices() };
        options.AddExtension<ThermometerPlugin>();

        using var service = new DevFlowAgentService(options);
        using var http = new HttpClient();
        service.StartServerOnly(null);

        using var read = await http.GetAsync($"http://localhost:{port}/api/v1/ext/com.example.thermo/read");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        using var set = await http.PostAsync(
            $"http://localhost:{port}/api/v1/ext/com.example.thermo/set",
            new StringContent("""{"celsius":30}"""));
        Assert.Equal(HttpStatusCode.Conflict, set.StatusCode);
    }

    [Fact]
    public async Task Plugin_HandlerException_IsReturnedAsAnErrorEnvelope()
    {
        var port = GetFreePort();
        var options = new AgentOptions { Port = port, Services = ThermometerServices() };
        options.AddExtension<ThermometerPlugin>();

        using var service = new DevFlowAgentService(options);
        using var http = new HttpClient();
        service.StartServerOnly(null);

        using var response = await http.GetAsync($"http://localhost:{port}/api/v1/ext/com.example.thermo/broken");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("extension-error", json.RootElement.GetProperty("reason").GetString());
        Assert.Contains("sensor unplugged", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Plugin_StartsAfterTheServer_AndStopsWithTheAgent()
    {
        var port = GetFreePort();
        var plugin = new LifecyclePlugin();
        var options = new AgentOptions { Port = port };
        options.AddExtension(plugin);

        var service = new DevFlowAgentService(options);
        service.StartServerOnly(null);

        var context = await plugin.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(context.Services);
        Assert.False(string.IsNullOrWhiteSpace(context.Platform));
        Assert.NotNull(context.Logger);

        await service.StopAsync();
        Assert.True(plugin.Stopped);
        service.Dispose();
    }

    [Fact]
    public async Task Dispose_StopsPlugins_AsTheNativeAgentStopsThatWay()
    {
        var plugin = new LifecyclePlugin();
        var options = new AgentOptions { Port = GetFreePort() };
        options.AddExtension(plugin);

        var service = new DevFlowAgentService(options);
        service.StartServerOnly(null);
        await plugin.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // DevFlowAgent.Stop() in Agent.Native disposes the service without calling StopAsync.
        service.Dispose();

        Assert.True(plugin.Stopped);
        Assert.Equal(1, plugin.StopCount);
    }

    [Fact]
    public async Task Stop_WaitsForAStartInProgress_BeforeStoppingThePlugin()
    {
        var plugin = new SlowStartPlugin(TimeSpan.FromMilliseconds(300));
        var options = new AgentOptions { Port = GetFreePort() };
        options.AddExtension(plugin);

        using var service = new DevFlowAgentService(options);
        service.StartServerOnly(null);
        await plugin.Starting.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await service.StopAsync();

        Assert.Equal(["start-finished", "stop"], plugin.Log);
    }

    [Fact]
    public async Task Stop_DoesNotHang_OnAPluginThatNeverStops()
    {
        var plugin = new HangingStopPlugin();
        var options = new AgentOptions { Port = GetFreePort() };
        options.AddExtension(plugin);

        using var service = new DevFlowAgentService(options) { PluginStopTimeout = TimeSpan.FromMilliseconds(300) };
        service.StartServerOnly(null);

        var stop = service.StopAsync();
        var finished = await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(stop, finished);
        Assert.True(plugin.StopCalled);
    }

    [Fact]
    public async Task FaultedPlugin_RefusesStreamPollingAndNewSubscriptions()
    {
        var port = GetFreePort();
        var plugin = new FailingStartStreamPlugin(Task.CompletedTask);
        var options = new AgentOptions { Port = port };
        options.AddExtension(plugin);

        using var service = new DevFlowAgentService(options);
        using var client = new AgentClient("localhost", port);
        using var http = new HttpClient();
        service.StartServerOnly(null);
        await WaitUntilAsync(async () => (await client.GetExtensionsAsync())["com.example.failstream"].State == "faulted");

        using var poll = await http.GetAsync($"http://localhost:{port}/api/v1/ext/com.example.failstream/streams/events");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, poll.StatusCode);
        Assert.Contains("extension-faulted", await poll.Content.ReadAsStringAsync());

        using var ws = new System.Net.WebSockets.ClientWebSocket();
        ws.Options.CollectHttpResponseDetails = true;
        await Assert.ThrowsAsync<System.Net.WebSockets.WebSocketException>(
            () => ws.ConnectAsync(new Uri(client.GetExtensionStreamWebSocketUrl("com.example.failstream", "events")), CancellationToken.None));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ws.HttpStatusCode);
    }

    [Fact]
    public async Task FaultedPlugin_ClosesSubscriptionsThatWereAlreadyOpen()
    {
        var port = GetFreePort();
        // The test decides when the plugin faults: only once the subscription is open.
        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var plugin = new FailingStartStreamPlugin(fault.Task);
        var options = new AgentOptions { Port = port };
        options.AddExtension(plugin);

        using var service = new DevFlowAgentService(options);
        using var client = new AgentClient("localhost", port);
        service.StartServerOnly(null);

        using var ws = new System.Net.WebSockets.ClientWebSocket();
        await ws.ConnectAsync(new Uri(client.GetExtensionStreamWebSocketUrl("com.example.failstream", "events")), CancellationToken.None);
        fault.SetResult();

        var buffer = new byte[1024];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await ws.ReceiveAsync(buffer, timeout.Token);

        Assert.Equal(System.Net.WebSockets.WebSocketMessageType.Close, result.MessageType);
        Assert.Equal(System.Net.WebSockets.WebSocketCloseStatus.InternalServerError, ws.CloseStatus);
    }

    [Fact]
    public async Task Stream_DeliversConcurrentPublishesInOrder_WithoutGaps()
    {
        var port = GetFreePort();
        var plugin = new StreamPlugin(capacity: 10_000);
        var options = new AgentOptions { Port = port };
        options.AddExtension(plugin);

        using var service = new DevFlowAgentService(options);
        using var client = new AgentClient("localhost", port);
        service.StartServerOnly(null);

        const int Publishers = 8;
        const int PerPublisher = 250;
        var received = new List<ExtensionStreamEvent>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var watch = new ExtensionOperations(client).WatchAsync("com.example.stream", "events", since: 0, evt =>
        {
            lock (received)
            {
                received.Add(evt);
                if (received.Count == Publishers * PerPublisher)
                    cts.Cancel();
            }
        }, cts.Token);

        await Task.WhenAll(Enumerable.Range(0, Publishers).Select(p => Task.Run(() =>
        {
            for (var i = 0; i < PerPublisher; i++)
                plugin.Events.Publish(new Reading($"p{p}", i));
        })));
        await watch;

        Assert.Equal(Enumerable.Range(1, Publishers * PerPublisher).Select(i => (long)i), received.Select(e => e.Sequence));
        Assert.All(received, e => Assert.Equal(0, e.Dropped));
    }

    [Fact]
    public void EventStream_PublishElement_CopiesThePayload()
    {
        var stream = new DevFlowEventStream(capacity: 10);
        using (var doc = JsonDocument.Parse("""{"kind":"scan"}"""))
            stream.PublishElement(doc.RootElement);

        Assert.Equal("scan", stream.Read().Events.Single().Data.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Stream_IsReadableByPolling_AndReportsDroppedEvents()
    {
        var port = GetFreePort();
        var plugin = new StreamPlugin(capacity: 3);
        var options = new AgentOptions { Port = port };
        options.AddExtension(plugin);

        using var service = new DevFlowAgentService(options);
        using var client = new AgentClient("localhost", port);
        service.StartServerOnly(null);

        for (var i = 1; i <= 5; i++)
            plugin.Events.Publish(new Reading("scan", i));

        var descriptor = (await client.GetExtensionsAsync())["com.example.stream"];
        var stream = Assert.Single(descriptor.Streams);
        Assert.Equal("events", stream.Name);
        Assert.Equal("/ws/v1/ext/com.example.stream/streams/events", stream.WebSocketPath);
        Assert.Equal(["kind", "value"], stream.Columns);
        Assert.Contains(descriptor.Tools, t => t.Name == "events" && t.Annotations!.ReadOnly);

        var page = await client.GetExtensionEventsAsync("com.example.stream", "events");
        Assert.Equal(2, page.Dropped);
        Assert.Equal([3L, 4L, 5L], page.Events.Select(e => e.Sequence));
        Assert.Equal("scan", page.Events[0].Data.GetProperty("kind").GetString());
        Assert.Equal(5, page.NextSequence);

        var next = await client.GetExtensionEventsAsync("com.example.stream", "events", since: page.NextSequence);
        Assert.Empty(next.Events);
        Assert.Equal(0, next.Dropped);
        Assert.Equal(5, next.NextSequence);
    }

    [Fact]
    public async Task Stream_IsWatchableOverWebSocket_ThroughTheCliOperations()
    {
        var port = GetFreePort();
        var plugin = new StreamPlugin(capacity: 100);
        var options = new AgentOptions { Port = port };
        options.AddExtension(plugin);

        using var service = new DevFlowAgentService(options);
        using var client = new AgentClient("localhost", port);
        service.StartServerOnly(null);

        plugin.Events.Publish(new Reading("buffered", 1));

        var operations = new ExtensionOperations(client);
        var (stream, error) = await operations.FindStreamAsync("com.example.stream", "events");
        Assert.Null(error);
        Assert.NotNull(stream);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = new List<ExtensionStreamEvent>();
        var watch = operations.WatchAsync("com.example.stream", "events", since: 0, evt =>
        {
            lock (received)
            {
                received.Add(evt);
                if (received.Count == 3)
                    cts.Cancel();
            }
        }, cts.Token);

        // Live events arrive after the replayed one.
        await WaitUntilAsync(() => { lock (received) return received.Count >= 1; });
        plugin.Events.Publish(new Reading("live", 2));
        plugin.Events.Publish(new Reading("live", 3));
        await watch;

        Assert.Equal([1L, 2L, 3L], received.Select(e => e.Sequence));
        Assert.Equal(["buffered", "live", "live"], received.Select(e => e.Data.GetProperty("kind").GetString()));

        var line = DevFlowCommands.FormatStreamEvent(received[1], stream!.Columns);
        Assert.Contains("kind=live", line);
        Assert.Contains("value=2", line);
    }

    [Fact]
    public async Task Stream_StaysOpen_WhenTheClientSendsKeepAlivePongs()
    {
        var port = GetFreePort();
        var plugin = new StreamPlugin(capacity: 10);
        var options = new AgentOptions { Port = port };
        options.AddExtension(plugin);

        using var service = new DevFlowAgentService(options);
        using var client = new AgentClient("localhost", port);
        service.StartServerOnly(null);

        // ClientWebSocket answers pings with pongs and sends unsolicited pongs as keep-alives. The agent
        // used to read any pong as a close, dropping every stream at its first 15-second ping.
        using var ws = new System.Net.WebSockets.ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromMilliseconds(50);
        await ws.ConnectAsync(new Uri(client.GetExtensionStreamWebSocketUrl("com.example.stream", "events")), CancellationToken.None);
        await Task.Delay(500);

        plugin.Events.Publish(new Reading("after-pongs", 1));
        var buffer = new byte[4096];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await ws.ReceiveAsync(buffer, timeout.Token);

        Assert.Equal(System.Net.WebSockets.WebSocketMessageType.Text, result.MessageType);
        using var json = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
        Assert.Equal("after-pongs", json.RootElement.GetProperty("data").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task FindStream_ReportsAvailableStreams_WhenTheNameIsWrong()
    {
        var port = GetFreePort();
        var options = new AgentOptions { Port = port };
        options.AddExtension(new StreamPlugin(capacity: 10));

        using var service = new DevFlowAgentService(options);
        using var client = new AgentClient("localhost", port);
        service.StartServerOnly(null);

        var (stream, error) = await new ExtensionOperations(client).FindStreamAsync("com.example.stream", "nope");
        Assert.Null(stream);
        Assert.Contains("Available streams: events", error);
    }

    [Fact]
    public async Task FaultedPlugins_AreReported_AndDoNotBreakTheAgent()
    {
        var port = GetFreePort();
        var options = new AgentOptions { Port = port };
        options.AddExtension(new FailingStartPlugin());
        options.AddExtension(new FailingConfigurePlugin());
        options.AddExtension(_ => throw new InvalidOperationException("no bluetooth radio"));
        options.AddExtension(new StreamPlugin(capacity: 10));

        using var service = new DevFlowAgentService(options);
        using var client = new AgentClient("localhost", port);
        using var http = new HttpClient();
        service.StartServerOnly(null);

        Dictionary<string, ExtensionDescriptor> extensions = [];
        await WaitUntilAsync(async () =>
        {
            extensions = await client.GetExtensionsAsync();
            return extensions.TryGetValue("com.example.failstart", out var e) && e.State == "faulted";
        });

        Assert.Equal("start failed", extensions["com.example.failstart"].Error);
        Assert.Equal("faulted", extensions["com.example.failconfigure"].State);
        Assert.Equal("configure failed", extensions["com.example.failconfigure"].Error);
        Assert.Equal("ready", extensions["com.example.stream"].State);

        var loadError = Assert.Single(await client.GetExtensionErrorsAsync());
        Assert.Equal("no bluetooth radio", loadError.Error);

        using var response = await http.GetAsync($"http://localhost:{port}/api/v1/ext/com.example.failstart/ping");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("extension-faulted", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void PluginNamespace_ThatCollidesWithAnInlineExtension_IsReportedNotThrown()
    {
        var options = new AgentOptions { Port = GetFreePort() };
        options.RegisterExtension("com.example.stream", "Inline");
        options.AddExtension(new StreamPlugin(capacity: 10));

        using var service = new DevFlowAgentService(options);
        service.StartServerOnly(null);

        var error = Assert.Single(service.ExtensionLoadErrors);
        Assert.Contains("Duplicate extension namespace", error.Error);
    }

    [Fact]
    public void EventStream_KeepsTheNewestEvents_AndResetsAStaleCursor()
    {
        var stream = new DevFlowEventStream<int>(capacity: 2);
        Assert.Equal(0, stream.LastSequence);
        Assert.Empty(stream.Read().Events);

        stream.Publish(1);
        stream.Publish(2);
        stream.Publish(3);

        var page = stream.Read();
        Assert.Equal([2L, 3L], page.Events.Select(e => e.Sequence));
        Assert.Equal(1, page.Dropped);

        // A cursor from a previous app run (newer than this stream) reads from the start.
        var reset = stream.Read(since: 99);
        Assert.Equal([2L, 3L], reset.Events.Select(e => e.Sequence));

        var limited = stream.Read(since: 0, limit: 1);
        Assert.Equal([2L], limited.Events.Select(e => e.Sequence));
        Assert.Equal(2, limited.NextSequence);

        stream.Clear();
        Assert.Empty(stream.Read().Events);
        stream.Publish(4);
        Assert.Equal(4, stream.Read().Events.Single().Sequence);
    }

    [Fact]
    public void EventStream_Subscribers_SeeNewEventsUntilDisposed_AndCannotBreakThePublisher()
    {
        var stream = new DevFlowEventStream(capacity: 10);
        var seen = new List<long>();
        stream.Subscribe(_ => throw new InvalidOperationException("bad observer"));
        var subscription = stream.Subscribe(evt => seen.Add(evt.Sequence));

        stream.Publish("a");
        subscription.Dispose();
        stream.Publish("b");

        Assert.Equal([1L], seen);
        Assert.Equal(2, stream.LastSequence);
    }

    [Fact]
    public void DevFlowRequest_GetParameter_ReadsRouteQueryAndBody()
    {
        var request = new DevFlowRequest(
            "POST",
            "/api/v1/ext/com.example/x",
            query: new Dictionary<string, string> { ["q"] = "query" },
            routeValues: new Dictionary<string, string> { ["id"] = "route" },
            body: """{"Name":"body","count":3,"nested":{"a":1}}""");

        Assert.Equal("route", request.GetParameter("id"));
        Assert.Equal("query", request.GetParameter("q"));
        Assert.Equal("body", request.GetParameter("name"));
        Assert.Equal("3", request.GetParameter("count"));
        Assert.Equal("""{"a":1}""", request.GetParameter("nested"));
        Assert.Null(request.GetParameter("missing"));
        Assert.Equal(3, request.ReadJson<CountBody>()!.Count);
    }

    sealed record CountBody(int Count);

    static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    static IServiceProvider ThermometerServices()
        => new ServiceCollection().AddSingleton(new Thermometer()).BuildServiceProvider();

    static async Task WaitUntilAsync(Func<bool> condition) => await WaitUntilAsync(() => Task.FromResult(condition()));

    static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await condition())
                return;
            await Task.Delay(50);
        }
        Assert.Fail("Condition was not met within 5 seconds.");
    }

    static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public sealed class Thermometer
    {
        public double Celsius { get; set; }
    }

    public sealed class ThermometerPlugin(Thermometer thermometer)
        : DevFlowExtension("com.example.thermo", "Thermometer", "1.0.0")
    {
        public override string? ToolPrefix => "temp";

        protected internal override void Configure(IDevFlowExtensionBuilder builder)
        {
            builder.MapTool("read", "Reads the temperature.", "GET", "/read", request =>
            {
                var unit = request.GetParameter("unit") ?? "c";
                var value = unit == "f" ? thermometer.Celsius * 9 / 5 + 32 : thermometer.Celsius;
                return Task.FromResult(DevFlowResponse.Json(new { value, unit }));
            }, annotations: new DevFlowToolAnnotations { ReadOnly = true });

            builder.MapTool("set", "Sets the temperature.", "POST", "/set", request =>
            {
                thermometer.Celsius = double.Parse(request.GetParameter("celsius")!, System.Globalization.CultureInfo.InvariantCulture);
                return Task.FromResult(DevFlowResponse.Ok());
            });

            builder.MapGet("/broken", _ => throw new InvalidOperationException("sensor unplugged"));

            builder.MapTool("query", "Echoes its query string.", "GET", "/query",
                request => Task.FromResult(DevFlowResponse.Json(request.Query)),
                annotations: new DevFlowToolAnnotations { ReadOnly = true });
        }
    }

    public sealed record Reading(string Kind, int Value);

    public sealed class StreamPlugin(int capacity) : DevFlowExtension("com.example.stream", "Stream")
    {
        public DevFlowEventStream<Reading> Events { get; } = new(capacity);

        protected internal override void Configure(IDevFlowExtensionBuilder builder)
            => builder.MapStream("events", "Readings.", Events, ["kind", "value"]);
    }

    public sealed class LifecyclePlugin() : DevFlowExtension("com.example.lifecycle", "Lifecycle")
    {
        public TaskCompletionSource<IDevFlowExtensionContext> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Stopped { get; private set; }
        public int StopCount { get; private set; }

        protected internal override void Configure(IDevFlowExtensionBuilder builder)
            => builder.MapGet("/ping", _ => Task.FromResult(DevFlowResponse.Ok()));

        protected internal override Task StartAsync(IDevFlowExtensionContext context, CancellationToken cancellationToken)
        {
            Started.TrySetResult(context);
            return Task.CompletedTask;
        }

        protected internal override Task StopAsync(CancellationToken cancellationToken)
        {
            Stopped = true;
            StopCount++;
            return Task.CompletedTask;
        }
    }

    public sealed class SlowStartPlugin(TimeSpan startDelay) : DevFlowExtension("com.example.slowstart", "Slow start")
    {
        public TaskCompletionSource Starting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Log { get; } = [];

        protected internal override void Configure(IDevFlowExtensionBuilder builder)
            => builder.MapGet("/ping", _ => Task.FromResult(DevFlowResponse.Ok()));

        protected internal override async Task StartAsync(IDevFlowExtensionContext context, CancellationToken cancellationToken)
        {
            Starting.TrySetResult();
            // Ignores cancellation on purpose, like a plugin awaiting a slow native call.
            await Task.Delay(startDelay, CancellationToken.None);
            lock (Log) Log.Add("start-finished");
        }

        protected internal override Task StopAsync(CancellationToken cancellationToken)
        {
            lock (Log) Log.Add("stop");
            return Task.CompletedTask;
        }
    }

    public sealed class HangingStopPlugin() : DevFlowExtension("com.example.hangingstop", "Never stops")
    {
        public bool StopCalled { get; private set; }

        protected internal override void Configure(IDevFlowExtensionBuilder builder)
            => builder.MapGet("/ping", _ => Task.FromResult(DevFlowResponse.Ok()));

        protected internal override Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalled = true;
            return new TaskCompletionSource().Task; // never completes and ignores the token
        }
    }

    public sealed class FailingStartStreamPlugin(Task failWhen) : DevFlowExtension("com.example.failstream", "Stream whose start fails")
    {
        public DevFlowEventStream<Reading> Events { get; } = new(10);

        protected internal override void Configure(IDevFlowExtensionBuilder builder)
            => builder.MapStream("events", "Readings.", Events);

        protected internal override async Task StartAsync(IDevFlowExtensionContext context, CancellationToken cancellationToken)
        {
            Events.Publish(new Reading("before-fault", 1));
            await failWhen.WaitAsync(cancellationToken);
            throw new InvalidOperationException("radio lost");
        }
    }

    public sealed class FailingStartPlugin() : DevFlowExtension("com.example.failstart", "Fails to start")
    {
        protected internal override void Configure(IDevFlowExtensionBuilder builder)
            => builder.MapGet("/ping", _ => Task.FromResult(DevFlowResponse.Ok()));

        protected internal override async Task StartAsync(IDevFlowExtensionContext context, CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("start failed");
        }
    }

    public sealed class FailingConfigurePlugin() : DevFlowExtension("com.example.failconfigure", "Fails to configure")
    {
        protected internal override void Configure(IDevFlowExtensionBuilder builder)
            => throw new InvalidOperationException("configure failed");
    }
}
