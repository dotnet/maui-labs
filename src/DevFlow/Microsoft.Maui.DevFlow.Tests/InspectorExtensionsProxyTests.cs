using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Extensibility;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The Inspector's Extensions panel proxies (/api/extensions*, /ws/extensions) against a real agent
/// hosting a plugin, in both standalone-listener and broker (HttpListener) modes.
/// </summary>
public class InspectorExtensionsProxyTests
{
    const string Ns = "org.example.ble";

    [Fact]
    public async Task ExtensionList_IsTokenGated_AndIncludesFaultedPluginsAndLoadErrors()
    {
        await using var app = await TestApp.StartAsync(includeFaulty: true);
        using var http = new HttpClient();

        using (var forbidden = await http.PostAsync($"{app.InspectorUrl}/api/extensions", Json("{}")))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var token = await ReadTokenAsync(http, app.InspectorUrl);
        JsonElement list = default;
        await WaitUntilAsync(async () =>
        {
            list = await PostJsonAsync(http, $"{app.InspectorUrl}/api/extensions", "{}", token);
            return list.GetProperty("extensions").TryGetProperty("com.example.faulty", out var f)
                && f.GetProperty("state").GetString() == "faulted";
        });

        Assert.True(list.GetProperty("ok").GetBoolean());
        var ble = list.GetProperty("extensions").GetProperty(Ns);
        Assert.Equal("ready", ble.GetProperty("state").GetString());
        Assert.Contains(ble.GetProperty("tools").EnumerateArray(), t => t.GetProperty("name").GetString() == "status");
        var stream = Assert.Single(ble.GetProperty("streams").EnumerateArray());
        Assert.Equal(["kind", "peripheral", "detail"], stream.GetProperty("columns").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal("radio off", list.GetProperty("extensions").GetProperty("com.example.faulty").GetProperty("error").GetString());
        var loadError = Assert.Single(list.GetProperty("extensionErrors").EnumerateArray());
        Assert.Equal("no adapter", loadError.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ReadOnlyTool_PassesParametersAsQuery_AndReturnsTheAgentBody()
    {
        await using var app = await TestApp.StartAsync();
        using var http = new HttpClient();
        var token = await ReadTokenAsync(http, app.InspectorUrl);

        var result = await PostJsonAsync(http, $"{app.InspectorUrl}/api/extensions/call",
            $$$"""{"namespace":"{{{Ns}}}","tool":"peripherals","parameters":{"filter":"Polar","limit":2,"connected":true}}""", token);

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(200, result.GetProperty("status").GetInt32());
        Assert.Equal("GET", result.GetProperty("method").GetString());
        var body = result.GetProperty("body");
        Assert.Equal("Polar", body.GetProperty("filter").GetString());
        Assert.Equal("2", body.GetProperty("limit").GetString());
        Assert.Equal("true", body.GetProperty("connected").GetString());
    }

    [Fact]
    public async Task MutatingTool_ClaimsTheInspectorLease_AndReachesThePlugin()
    {
        await using var app = await TestApp.StartAsync();
        using var http = new HttpClient();
        var token = await ReadTokenAsync(http, app.InspectorUrl);

        var result = await PostJsonAsync(http, $"{app.InspectorUrl}/api/extensions/call",
            $$$"""{"namespace":"{{{Ns}}}","tool":"connect","parameters":{"uuid":"abc"}}""", token, lease: "tab-1");

        Assert.True(result.GetProperty("ok").GetBoolean(), result.GetRawText());
        Assert.Equal("abc", app.Plugin.LastConnected);

        using var client = new AgentClient("localhost", app.AgentPort);
        var status = await client.ControlMutationLeaseAsync("status", leaseId: "someone-else");
        Assert.True(status.HeldByOther);
        Assert.Equal("web-inspector", status.HolderKind);
    }

    [Fact]
    public async Task MutatingTool_IsRefused_WhileAnotherSessionHoldsTheLease()
    {
        await using var app = await TestApp.StartAsync();
        using var http = new HttpClient();
        var token = await ReadTokenAsync(http, app.InspectorUrl);
        using var other = new AgentClient("localhost", app.AgentPort);
        var claim = await other.ControlMutationLeaseAsync("claim", leaseId: "mcp-session", holderKind: "mcp", label: "Copilot");
        Assert.True(claim.YouHold);

        using var response = await AuthorizedPostAsync(http, $"{app.InspectorUrl}/api/extensions/call",
            $$$"""{"namespace":"{{{Ns}}}","tool":"connect","parameters":{"uuid":"abc"}}""", token, lease: "tab-1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("writer", json.RootElement.GetProperty("reason").GetString());
        Assert.Equal("Copilot", json.RootElement.GetProperty("label").GetString());
        Assert.Null(app.Plugin.LastConnected);

        // Read-only tools stay usable for a read-only Inspector.
        var status = await PostJsonAsync(http, $"{app.InspectorUrl}/api/extensions/call",
            $$$"""{"namespace":"{{{Ns}}}","tool":"status"}""", token, lease: "tab-1");
        Assert.True(status.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task ToolErrors_AreReportedWithTheAgentEnvelope_AndUnknownToolsAre404()
    {
        await using var app = await TestApp.StartAsync();
        using var http = new HttpClient();
        var token = await ReadTokenAsync(http, app.InspectorUrl);

        var broken = await PostJsonAsync(http, $"{app.InspectorUrl}/api/extensions/call",
            $$$"""{"namespace":"{{{Ns}}}","tool":"broken"}""", token);
        Assert.False(broken.GetProperty("ok").GetBoolean());
        Assert.Equal(500, broken.GetProperty("status").GetInt32());
        Assert.Equal("extension-error", broken.GetProperty("body").GetProperty("reason").GetString());

        using var unknown = await AuthorizedPostAsync(http, $"{app.InspectorUrl}/api/extensions/call",
            $$$"""{"namespace":"{{{Ns}}}","tool":"nope"}""", token);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var badParameters = await AuthorizedPostAsync(http, $"{app.InspectorUrl}/api/extensions/call",
            $$$"""{"namespace":"{{{Ns}}}","tool":"status","parameters":[1]}""", token);
        Assert.Equal(HttpStatusCode.BadRequest, badParameters.StatusCode);
    }

    [Fact]
    public async Task StreamEvents_CanBePolled()
    {
        await using var app = await TestApp.StartAsync();
        using var http = new HttpClient();
        var token = await ReadTokenAsync(http, app.InspectorUrl);
        app.Plugin.Events.Publish(new BleEvent("scan", "HR", "-60 dBm"));
        app.Plugin.Events.Publish(new BleEvent("connect", "HR", null));

        var page = await PostJsonAsync(http, $"{app.InspectorUrl}/api/extensions/events",
            $$$"""{"namespace":"{{{Ns}}}","stream":"events","since":1}""", token);

        var evt = Assert.Single(page.GetProperty("events").EnumerateArray());
        Assert.Equal(2, evt.GetProperty("seq").GetInt64());
        Assert.Equal("connect", evt.GetProperty("data").GetProperty("kind").GetString());
        Assert.Equal(2, page.GetProperty("nextSequence").GetInt64());
    }

    [Fact]
    public async Task StreamWebSocket_ReplaysThenStreamsLiveEvents_InStandaloneMode()
    {
        await using var app = await TestApp.StartAsync();
        using var http = new HttpClient();
        var token = await ReadTokenAsync(http, app.InspectorUrl);
        app.Plugin.Events.Publish(new BleEvent("scan", "HR", "buffered"));

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", app.InspectorUrl);
        await ws.ConnectAsync(StreamUri(app.InspectorUrl, token, since: 0), Timeout());

        await AssertFramesAsync(ws, app.Plugin, "buffered");
    }

    [Fact]
    public async Task StreamWebSocket_RejectsMissingTokens_ForeignOrigins_AndUnknownStreams()
    {
        await using var app = await TestApp.StartAsync();
        using var http = new HttpClient();
        var token = await ReadTokenAsync(http, app.InspectorUrl);

        await AssertRejectedAsync(StreamUri(app.InspectorUrl, token: null, since: 0), origin: null);
        await AssertRejectedAsync(StreamUri(app.InspectorUrl, token, since: 0), origin: "http://evil.example");
        await AssertRejectedAsync(StreamUri(app.InspectorUrl, token, since: 0, stream: "nope"), origin: null);
    }

    [Fact]
    public async Task StreamWebSocket_RelaysThroughTheBrokerListener()
    {
        await using var app = await TestApp.StartAsync();
        var brokerPort = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{brokerPort}/");
        listener.Start();
        using var cts = new CancellationTokenSource();
        var brokerLoop = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch { break; }
                _ = app.Inspector.HandleBrokerRequestAsync(context, context.Request.Url!.AbsolutePath);
            }
        });

        var brokerUrl = $"http://127.0.0.1:{brokerPort}";
        using var http = new HttpClient();
        var token = await ReadTokenAsync(http, brokerUrl);
        var list = await PostJsonAsync(http, $"{brokerUrl}/api/extensions", "{}", token);
        Assert.True(list.GetProperty("extensions").TryGetProperty(Ns, out _));

        app.Plugin.Events.Publish(new BleEvent("scan", "HR", "buffered"));
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", brokerUrl);
        await ws.ConnectAsync(StreamUri(brokerUrl, token, since: 0), Timeout());
        await AssertFramesAsync(ws, app.Plugin, "buffered");

        using var rejected = new ClientWebSocket();
        await Assert.ThrowsAnyAsync<WebSocketException>(() => rejected.ConnectAsync(StreamUri(brokerUrl, token: "wrong", since: 0), Timeout()));

        cts.Cancel();
        listener.Stop();
        await brokerLoop;
    }

    [Theory]
    [InlineData("GET", false, false)]
    [InlineData("POST", false, true)]
    [InlineData("post", true, false)]
    [InlineData("DELETE", false, true)]
    public void ExtensionToolMutates_FollowsTheAgentLeaseRule(string method, bool readOnly, bool mutates)
        => Assert.Equal(mutates, InspectorServer.ExtensionToolMutates(method, readOnly));

    [Fact]
    public void ExtensionToolUri_EncodesGetParametersAsQuery_AndLeavesBodiesAlone()
    {
        using var doc = JsonDocument.Parse("""{"name":"a b","n":3,"flag":false,"skip":null,"obj":{"x":1}}""");
        var get = InspectorServer.BuildExtensionToolUri("http://h:1", "GET", "/api/v1/ext/x/y", doc.RootElement);
        Assert.Equal("http://h:1/api/v1/ext/x/y?name=a%20b&n=3&flag=false&obj=%7B%22x%22%3A1%7D", get);
        Assert.Equal("http://h:1/api/v1/ext/x/y", InspectorServer.BuildExtensionToolUri("http://h:1", "POST", "/api/v1/ext/x/y", doc.RootElement));
        Assert.Equal("http://h:1/api/v1/ext/x/y", InspectorServer.BuildExtensionToolUri("http://h:1", "GET", "/api/v1/ext/x/y", null));
    }

    [Theory]
    [InlineData("/api/extensions")]
    [InlineData("/api/extensions/call")]
    [InlineData("/api/extensions/events")]
    public void ExtensionRoutes_AreTokenGated_ButNotBlanketMutations(string path)
    {
        Assert.True(InspectorServer.IsTokenGatedPath(path));
        // The lease is claimed per tool (only for mutating ones), not for every call.
        Assert.False(InspectorServer.IsMutation(path));
    }

    static async Task AssertFramesAsync(ClientWebSocket ws, BlePlugin plugin, string bufferedDetail)
    {
        var first = await ReceiveAsync(ws);
        Assert.Equal(1, first.GetProperty("seq").GetInt64());
        Assert.Equal(bufferedDetail, first.GetProperty("data").GetProperty("detail").GetString());
        Assert.False(string.IsNullOrEmpty(first.GetProperty("ts").GetString()));

        plugin.Events.Publish(new BleEvent("connect", "HR", "live"));
        var second = await ReceiveAsync(ws);
        Assert.Equal(2, second.GetProperty("seq").GetInt64());
        Assert.Equal("live", second.GetProperty("data").GetProperty("detail").GetString());

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", Timeout());
    }

    static async Task AssertRejectedAsync(Uri uri, string? origin)
    {
        using var ws = new ClientWebSocket();
        if (origin is not null)
            ws.Options.SetRequestHeader("Origin", origin);
        await Assert.ThrowsAnyAsync<WebSocketException>(() => ws.ConnectAsync(uri, Timeout()));
    }

    static Uri StreamUri(string baseUrl, string? token, long since, string stream = "events")
    {
        var query = $"namespace={Uri.EscapeDataString(Ns)}&stream={stream}&since={since}";
        if (token is not null)
            query += $"&token={token}";
        return new Uri($"{baseUrl.Replace("http://", "ws://")}{InspectorServer.ExtensionStreamWebSocketPath}?{query}");
    }

    static async Task<JsonElement> ReceiveAsync(ClientWebSocket ws)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await ws.ReceiveAsync(buffer, Timeout());
            Assert.NotEqual(WebSocketMessageType.Close, result.MessageType);
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return JsonDocument.Parse(message.ToArray()).RootElement.Clone();
        }
    }

    static CancellationToken Timeout() => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    static async Task<string> ReadTokenAsync(HttpClient http, string url)
    {
        var page = await http.GetStringAsync($"{url}/");
        var token = Regex.Match(page, "<meta\\s+name=\"devflow-inspector-token\"\\s+content=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return token;
    }

    static Task<HttpResponseMessage> AuthorizedPostAsync(HttpClient http, string url, string body, string token, string? lease = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = Json(body) };
        request.Headers.Add("X-DevFlow-Inspector-Token", token);
        if (lease is not null)
            request.Headers.Add("X-DevFlow-Lease", lease);
        return http.SendAsync(request);
    }

    static async Task<JsonElement> PostJsonAsync(HttpClient http, string url, string body, string token, string? lease = null)
    {
        using var response = await AuthorizedPostAsync(http, url, body, token, lease);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

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

    static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    sealed class TestApp : IAsyncDisposable
    {
        public required DevFlowAgentService Agent { get; init; }
        public required InspectorServer Inspector { get; init; }
        public required BlePlugin Plugin { get; init; }
        public required int AgentPort { get; init; }
        public required string InspectorUrl { get; init; }

        public static async Task<TestApp> StartAsync(bool includeFaulty = false)
        {
            var agentPort = FreePort();
            var plugin = new BlePlugin();
            var options = new AgentOptions { Port = agentPort };
            options.AddExtension(plugin);
            if (includeFaulty)
            {
                options.AddExtension(new FaultyPlugin());
                options.AddExtension(_ => throw new InvalidOperationException("no adapter"));
            }
            var agent = new DevFlowAgentService(options);
            agent.StartServerOnly(null);

            var inspectorPort = FreePort();
            var inspector = new InspectorServer(inspectorPort, "localhost", agentPort);
            inspector.Start();
            var url = $"http://127.0.0.1:{inspectorPort}";
            using var http = new HttpClient();
            for (var attempt = 0; attempt < 40; attempt++)
            {
                try
                {
                    using var response = await http.GetAsync($"{url}/inspector-extensions.js");
                    if (response.IsSuccessStatusCode)
                        break;
                }
                catch (HttpRequestException)
                {
                }
                await Task.Delay(25);
            }

            return new TestApp { Agent = agent, Inspector = inspector, Plugin = plugin, AgentPort = agentPort, InspectorUrl = url };
        }

        public async ValueTask DisposeAsync()
        {
            await Inspector.StopAsync();
            Inspector.Dispose();
            Agent.Dispose();
        }
    }

    public sealed record BleEvent(string Kind, string Peripheral, string? Detail);

    public sealed class BlePlugin() : DevFlowExtension(Ns, "BLE monitor", "1.2.0")
    {
        public DevFlowEventStream<BleEvent> Events { get; } = new(100);
        public string? LastConnected { get; private set; }

        protected internal override void Configure(IDevFlowExtensionBuilder builder)
        {
            builder.MapTool("status", "Adapter status.", "GET", "/status",
                _ => Task.FromResult(DevFlowResponse.Json(new { adapter = "on" })),
                annotations: new DevFlowToolAnnotations { ReadOnly = true });
            builder.MapTool("peripherals", "Known peripherals.", "GET", "/peripherals",
                request => Task.FromResult(DevFlowResponse.Json(new
                {
                    filter = request.GetParameter("filter"),
                    limit = request.GetParameter("limit"),
                    connected = request.GetParameter("connected")
                })),
                annotations: new DevFlowToolAnnotations { ReadOnly = true });
            builder.MapTool("connect", "Connects a peripheral.", "POST", "/connect", request =>
            {
                LastConnected = request.GetParameter("uuid");
                return Task.FromResult(DevFlowResponse.Ok());
            });
            builder.MapTool("broken", "Always fails.", "GET", "/broken",
                _ => throw new InvalidOperationException("adapter unplugged"),
                annotations: new DevFlowToolAnnotations { ReadOnly = true });
            builder.MapStream("events", "Adapter and peripheral events.", Events, ["kind", "peripheral", "detail"]);
        }
    }

    public sealed class FaultyPlugin() : DevFlowExtension("com.example.faulty", "Faulty")
    {
        protected internal override void Configure(IDevFlowExtensionBuilder builder)
            => builder.MapGet("/ping", _ => Task.FromResult(DevFlowResponse.Ok()));

        protected internal override async Task StartAsync(IDevFlowExtensionContext context, CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("radio off");
        }
    }
}
