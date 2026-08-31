using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// End-to-end integration tests for the shared Inspector's rich-property endpoints:
/// an HTTP client hits InspectorServer's /api/getProperty and /api/setProperty, which proxy to a
/// loopback fake agent. Verifies the full route → proxy → agent path the canvas/VS Code shells use.
/// </summary>
public class InspectorPropertyEndpointTests
{
    [Fact]
    public void EventSupport_DistinguishesSupportedUnsupportedAndTransientDiscovery()
    {
        using var supported = JsonDocument.Parse("""
            { "capabilities": { "ui.events": { "version": 1, "features": ["stream", "subscribe"] } } }
            """);
        using var unsupported = JsonDocument.Parse("""{ "capabilities": { "ui.actions": { "version": 1, "features": ["tap"] } } }""");

        Assert.True(InspectorServer.SupportsUiEvents(supported.RootElement));
        Assert.False(InspectorServer.SupportsUiEvents(unsupported.RootElement));
        Assert.Null(InspectorServer.SupportsUiEvents(default));
    }

    [Fact]
    public async Task SetThenGetProperty_ProxiesToAgent()
    {
        await using var agent = new FakeAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = new HttpClient();

            var set = await http.PostAsync($"http://127.0.0.1:{port}/api/setProperty",
                Json("{\"elementId\":\"e1\",\"name\":\"Text\",\"value\":\"hello\"}"));
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
            Assert.Contains("\"ok\":true", await set.Content.ReadAsStringAsync());
            Assert.Equal("hello", agent.Get("e1", "Text"));

            var get = await http.PostAsync($"http://127.0.0.1:{port}/api/getProperty",
                Json("{\"elementId\":\"e1\",\"name\":\"Text\"}"));
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            Assert.Contains("\"value\":\"hello\"", await get.Content.ReadAsStringAsync());
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task GetProperty_MissingFields_Returns400()
    {
        await using var agent = new FakeAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = new HttpClient();
            var res = await http.PostAsync($"http://127.0.0.1:{port}/api/getProperty",
                Json("{\"elementId\":\"e1\"}"));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task GetProperties_EnrichesPersistabilityAndSupportsOlderAgents()
    {
        const string descriptors = """
            {
              "id": "e1",
              "properties": [
                { "name": "Text", "kind": "text", "value": "Hello", "writable": true },
                { "name": "WidthRequest", "kind": "number", "value": "100", "writable": true }
              ]
            }
            """;
        await using var currentAgent = new FakeAgent(propertyDescriptorsResponse: descriptors);
        var currentPort = FreePort();
        var currentInspector = new InspectorServer(currentPort, "127.0.0.1", currentAgent.Port);
        currentInspector.Start();
        try
        {
            using var http = new HttpClient();
            var response = await http.PostAsync(
                $"http://127.0.0.1:{currentPort}/api/getProperties",
                Json("{\"elementId\":\"e1\"}"));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var properties = body.RootElement.GetProperty("properties");

            Assert.True(body.RootElement.GetProperty("supported").GetBoolean());
            Assert.True(properties[0].GetProperty("persistable").GetBoolean());
            Assert.False(properties[1].GetProperty("persistable").GetBoolean());
        }
        finally
        {
            await currentInspector.StopAsync();
        }

        await using var oldAgent = new FakeAgent();
        var oldPort = FreePort();
        var oldInspector = new InspectorServer(oldPort, "127.0.0.1", oldAgent.Port);
        oldInspector.Start();
        try
        {
            using var http = new HttpClient();
            var response = await http.PostAsync(
                $"http://127.0.0.1:{oldPort}/api/getProperties",
                Json("{\"elementId\":\"e1\"}"));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.False(body.RootElement.GetProperty("supported").GetBoolean());
        }
        finally
        {
            await oldInspector.StopAsync();
        }
    }

    [Fact]
    public async Task SetProperty_NonScalarValue_Returns400()
    {
        await using var agent = new FakeAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = new HttpClient();
            var res = await http.PostAsync($"http://127.0.0.1:{port}/api/setProperty",
                Json("{\"elementId\":\"e1\",\"name\":\"Text\",\"value\":null}"));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Null(agent.Get("e1", "Text")); // rejected → not written
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task SetProperty_NumberValue_ProxiesInvariantString()
    {
        await using var agent = new FakeAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = new HttpClient();
            var res = await http.PostAsync($"http://127.0.0.1:{port}/api/setProperty",
                Json("{\"elementId\":\"e1\",\"name\":\"Opacity\",\"value\":0.5}"));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal("0.5", agent.Get("e1", "Opacity")); // JSON number → invariant "0.5"
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task State_WhenAgentIsUnavailable_Returns503()
    {
        var inspectorPort = FreePort();
        var unavailableAgentPort = FreePort();
        var inspector = new InspectorServer(inspectorPort, "127.0.0.1", unavailableAgentPort);
        inspector.Start();
        try
        {
            using var http = new HttpClient();

            var response = await http.GetAsync($"http://127.0.0.1:{inspectorPort}/api/state");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("agent is unavailable", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task Control_WhenAgentIsUnavailable_Returns503()
    {
        var inspectorPort = FreePort();
        var unavailableAgentPort = FreePort();
        var inspector = new InspectorServer(inspectorPort, "127.0.0.1", unavailableAgentPort);
        inspector.Start();
        try
        {
            using var http = new HttpClient();

            var response = await http.PostAsync(
                $"http://127.0.0.1:{inspectorPort}/api/control",
                Json("{\"action\":\"status\"}"));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("agent is unavailable", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task StopEmptyRecording_CancelsSharedRecording()
    {
        var actions = new List<string>();
        await using var agent = new FakeAgent(recordingResponse: action =>
        {
            actions.Add(action);
            return action switch
            {
                "stop" => (400, "{\"ok\":false,\"empty\":true,\"error\":\"Recording has no steps.\"}"),
                "cancel-if-empty" => (200, "{\"ok\":true,\"recording\":false,\"empty\":true}"),
                _ => (200, "{\"ok\":true,\"recording\":false}")
            };
        });
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = new HttpClient();

            var response = await http.PostAsync(
                $"http://127.0.0.1:{port}/api/flows/record/stop",
                Json("{}"));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("\"empty\":true", body);
            Assert.Equal(["stop", "cancel-if-empty"], actions);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task CancelRecording_ProxiesSharedCancellation()
    {
        var actions = new List<string>();
        await using var agent = new FakeAgent(recordingResponse: action =>
        {
            actions.Add(action);
            return (200, "{\"ok\":true,\"recording\":false}");
        });
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = new HttpClient();

            var response = await http.PostAsync(
                $"http://127.0.0.1:{port}/api/flows/record/cancel",
                Json("{}"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(["cancel"], actions);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task HitTest_ReturnsMostSpecificCandidateFirst()
    {
        await using var agent = new FakeAgent(hitTestResponse: """
            {
              "elements": [
                { "id": "button", "type": "Button", "automationId": "AddButton", "text": "Add" },
                { "id": "grid", "type": "Grid" }
              ]
            }
            """);
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = new HttpClient();

            var response = await http.PostAsync(
                $"http://127.0.0.1:{port}/api/hitTest",
                Json("{\"x\":10,\"y\":20}"));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("button", body.RootElement.GetProperty("elementId").GetString());
            var candidates = body.RootElement.GetProperty("candidates");
            Assert.Equal(2, candidates.GetArrayLength());
            Assert.Equal("AddButton", candidates[0].GetProperty("automationId").GetString());
            Assert.Equal("grid", candidates[1].GetProperty("id").GetString());
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task Tap_WithRenderedElementId_DoesNotTryGeometricCandidates()
    {
        await using var agent = new FakeAgent(
            hitTestResponse: """
                {
                  "elements": [
                    { "id": "stale-scroll", "type": "ScrollView" },
                    { "id": "active-button", "type": "Button" }
                  ]
                }
                """,
            tapResponse: id => id == "active-button");
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = new HttpClient();

            var response = await http.PostAsync(
                $"http://127.0.0.1:{port}/api/tap",
                Json("""{"x":10,"y":20,"elementId":"active-button"}"""));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(body.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("active-button", body.RootElement.GetProperty("elementId").GetString());
            Assert.Equal(["active-button"], agent.TapIds);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task Tap_WhenElementRejects_ReturnsFailureWithoutTryingAnotherCandidate()
    {
        await using var agent = new FakeAgent(
            hitTestResponse: """{"elements":[{"id":"other-button","type":"Button"}]}""",
            tapResponse: _ => false);
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = new HttpClient();

            var response = await http.PostAsync(
                $"http://127.0.0.1:{port}/api/tap",
                Json("""{"x":10,"y":20,"elementId":"selected-button"}"""));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(body.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(["selected-button"], agent.TapIds);
            Assert.Contains("did not accept", body.RootElement.GetProperty("reason").GetString());
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task PersistProperty_RuntimeRejectsValue_DoesNotWriteSource()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "devflow-persist-endpoint", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var sourcePath = Path.Combine(tempRoot, "MainPage.xaml");
        const string xaml = """
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Label Text="Original" FontSize="28" />
            </ContentPage>
            """;
        await File.WriteAllTextAsync(Path.Combine(tempRoot, "TestApp.csproj"), "<Project />");
        await File.WriteAllTextAsync(sourcePath, xaml);
        var labelOffset = xaml.IndexOf("<Label", StringComparison.Ordinal);
        var lineStart = xaml.LastIndexOf('\n', labelOffset) + 1;
        var line = xaml[..labelOffset].Count(c => c == '\n') + 1;

        await using var agent = new FakeAgent(
            new ElementInfo
            {
                Id = "e1",
                SourceFile = sourcePath,
                SourceLine = line,
                SourceColumn = labelOffset - lineStart + 2,
                SourceHash = XamlSourcePropertyEditor.ComputeSourceHash(xaml),
            },
            rejectProperty: static (name, value) => name == "FontSize" && value.Length == 0,
            initialProperties: new Dictionary<string, string> { ["e1|FontSize"] = "28" });
        var port = FreePort();
        var inspector = new InspectorServer(
            port,
            "127.0.0.1",
            agent.Port,
            embedToken: null,
            agentId: "agent",
            appName: "TestApp",
            platform: "windows",
            project: Path.Combine(tempRoot, "TestApp.csproj"),
            sessionId: null);
        inspector.Start();
        try
        {
            using var http = new HttpClient();
            var page = await http.GetStringAsync($"http://127.0.0.1:{port}/");
            var token = Regex.Match(page, "<meta\\s+name=\"devflow-inspector-token\"\\s+content=\"([^\"]+)\"").Groups[1].Value;
            Assert.NotEmpty(token);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/api/persistProperty")
            {
                Content = Json("{\"elementId\":\"e1\",\"name\":\"FontSize\",\"value\":\"\"}")
            };
            request.Headers.Add("X-DevFlow-Inspector-Token", token);
            var response = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal(xaml, await File.ReadAllTextAsync(sourcePath));
        }
        finally
        {
            await inspector.StopAsync();
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData("Changed", "Text=\"Changed\"", HttpStatusCode.Conflict)]
    [InlineData("Original", "Text=\"{Binding Title}\"", HttpStatusCode.UnprocessableEntity)]
    public async Task PersistProperty_InvalidSource_DoesNotMutateRuntime(
        string currentRuntimeValue,
        string sourceAttribute,
        HttpStatusCode expectedStatus)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "devflow-persist-endpoint", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var sourcePath = Path.Combine(tempRoot, "MainPage.xaml");
        const string buildXaml = """
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Label Text="Original" />
            </ContentPage>
            """;
        var currentXaml = buildXaml.Replace("Text=\"Original\"", sourceAttribute, StringComparison.Ordinal);
        var sourceHash = expectedStatus == HttpStatusCode.Conflict
            ? XamlSourcePropertyEditor.ComputeSourceHash(buildXaml)
            : XamlSourcePropertyEditor.ComputeSourceHash(currentXaml);
        await File.WriteAllTextAsync(Path.Combine(tempRoot, "TestApp.csproj"), "<Project />");
        await File.WriteAllTextAsync(sourcePath, currentXaml);
        var labelOffset = buildXaml.IndexOf("<Label", StringComparison.Ordinal);
        var lineStart = buildXaml.LastIndexOf('\n', labelOffset) + 1;
        var line = buildXaml[..labelOffset].Count(c => c == '\n') + 1;

        await using var agent = new FakeAgent(
            new ElementInfo
            {
                Id = "e1",
                SourceFile = sourcePath,
                SourceLine = line,
                SourceColumn = labelOffset - lineStart + 2,
                SourceHash = sourceHash,
            },
            initialProperties: new Dictionary<string, string> { ["e1|Text"] = currentRuntimeValue });
        var port = FreePort();
        var inspector = new InspectorServer(
            port,
            "127.0.0.1",
            agent.Port,
            embedToken: null,
            agentId: "agent",
            appName: "TestApp",
            platform: "windows",
            project: Path.Combine(tempRoot, "TestApp.csproj"),
            sessionId: null);
        inspector.Start();
        try
        {
            using var http = new HttpClient();
            var page = await http.GetStringAsync($"http://127.0.0.1:{port}/");
            var token = Regex.Match(page, "<meta\\s+name=\"devflow-inspector-token\"\\s+content=\"([^\"]+)\"").Groups[1].Value;
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/api/persistProperty")
            {
                Content = Json("{\"elementId\":\"e1\",\"name\":\"Text\",\"value\":\"New\"}")
            };
            request.Headers.Add("X-DevFlow-Inspector-Token", token);

            var response = await http.SendAsync(request);

            Assert.Equal(expectedStatus, response.StatusCode);
            Assert.Equal(currentRuntimeValue, agent.Get("e1", "Text"));
            Assert.Equal(currentXaml, await File.ReadAllTextAsync(sourcePath));
        }
        finally
        {
            await inspector.StopAsync();
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task PersistProperty_SourceChangesAfterPreflight_RestoresRuntimeValue()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "devflow-persist-endpoint", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var sourcePath = Path.Combine(tempRoot, "MainPage.xaml");
        const string xaml = """
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Label Text="Original" />
            </ContentPage>
            """;
        await File.WriteAllTextAsync(Path.Combine(tempRoot, "TestApp.csproj"), "<Project />");
        await File.WriteAllTextAsync(sourcePath, xaml);
        var labelOffset = xaml.IndexOf("<Label", StringComparison.Ordinal);
        var lineStart = xaml.LastIndexOf('\n', labelOffset) + 1;
        var line = xaml[..labelOffset].Count(c => c == '\n') + 1;

        await using var agent = new FakeAgent(
            new ElementInfo
            {
                Id = "e1",
                SourceFile = sourcePath,
                SourceLine = line,
                SourceColumn = labelOffset - lineStart + 2,
                SourceHash = XamlSourcePropertyEditor.ComputeSourceHash(xaml),
            },
            initialProperties: new Dictionary<string, string> { ["e1|Text"] = "Original" },
            propertyAccepted: (name, value) =>
            {
                if (name == "Text" && value == "New")
                    File.AppendAllText(sourcePath, Environment.NewLine + "<!-- concurrent save -->");
            });
        var port = FreePort();
        var inspector = new InspectorServer(
            port,
            "127.0.0.1",
            agent.Port,
            embedToken: null,
            agentId: "agent",
            appName: "TestApp",
            platform: "windows",
            project: Path.Combine(tempRoot, "TestApp.csproj"),
            sessionId: null);
        inspector.Start();
        try
        {
            using var http = new HttpClient();
            var page = await http.GetStringAsync($"http://127.0.0.1:{port}/");
            var token = Regex.Match(page, "<meta\\s+name=\"devflow-inspector-token\"\\s+content=\"([^\"]+)\"").Groups[1].Value;
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/api/persistProperty")
            {
                Content = Json("{\"elementId\":\"e1\",\"name\":\"Text\",\"value\":\"New\"}")
            };
            request.Headers.Add("X-DevFlow-Inspector-Token", token);

            var response = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("Original", agent.Get("e1", "Text"));
            Assert.Contains("concurrent save", await File.ReadAllTextAsync(sourcePath));
        }
        finally
        {
            await inspector.StopAsync();
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData("tok-abc", "tok-abc", true)]   // exact match → trusted local shell
    [InlineData("tok-abc", "tok-xyz", false)]  // wrong token → not trusted (stays DENY)
    [InlineData("tok-abc", "", false)]         // no token supplied
    [InlineData("tok-abc", null, false)]       // no token supplied
    [InlineData("", "tok-abc", false)]         // broker issued no token → never trusted
    [InlineData(null, "tok-abc", false)]
    public void IsTrustedEmbed_OnlyExactTokenMatchIsTrusted(string? embedToken, string? requestEmbed, bool expected)
    {
        Assert.Equal(expected, InspectorServer.IsTrustedEmbed(embedToken, requestEmbed));
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static int FreePort() => TestPorts.Reserve();

    /// <summary>Minimal loopback agent answering GET/PUT /api/v1/ui/elements/{id}/properties/{name}.</summary>
    private sealed class FakeAgent : IAsyncDisposable
    {
        private static readonly Regex PropRoute = new("/api/v1/ui/elements/([^/]+)/properties/([^/?]+)", RegexOptions.Compiled);
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private readonly Dictionary<string, string> _props = new(StringComparer.Ordinal);
        private readonly ElementInfo? _element;
        private readonly Func<string, string, bool>? _rejectProperty;
        private readonly Action<string, string>? _propertyAccepted;
        private readonly Func<string, (int StatusCode, string Body)>? _recordingResponse;
        private readonly string? _hitTestResponse;
        private readonly string? _propertyDescriptorsResponse;
        private readonly Func<string, bool>? _tapResponse;

        public FakeAgent(
            ElementInfo? element = null,
            Func<string, string, bool>? rejectProperty = null,
            IReadOnlyDictionary<string, string>? initialProperties = null,
            Action<string, string>? propertyAccepted = null,
            Func<string, (int StatusCode, string Body)>? recordingResponse = null,
            string? hitTestResponse = null,
            string? propertyDescriptorsResponse = null,
            Func<string, bool>? tapResponse = null)
        {
            _element = element;
            _rejectProperty = rejectProperty;
            _propertyAccepted = propertyAccepted;
            _recordingResponse = recordingResponse;
            _hitTestResponse = hitTestResponse;
            _propertyDescriptorsResponse = propertyDescriptorsResponse;
            _tapResponse = tapResponse;
            if (initialProperties is not null)
            {
                foreach (var property in initialProperties)
                    _props[property.Key] = property.Value;
            }
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _loop = Loop(_cts.Token);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public List<string> TapIds { get; } = [];
        public string? Get(string id, string name) => _props.TryGetValue($"{id}|{name}", out var v) ? v : null;

        private async Task Loop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(ct); }
                catch { break; }
                _ = Handle(client, ct);
            }
        }

        private async Task Handle(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var (method, path, body) = await ReadRequest(stream, ct);

                    string json = "{\"error\":\"not found\"}";
                    var status = "404 Not Found";
                    if (method == "GET" &&
                        _element is not null &&
                        path.Equals($"/api/v1/ui/elements/{_element.Id}", StringComparison.Ordinal))
                    {
                        json = JsonSerializer.Serialize(_element);
                        status = "200 OK";
                    }

                    if (method == "POST" &&
                        path.Equals("/api/v1/agent/lease", StringComparison.Ordinal))
                    {
                        using var leaseRequest = JsonDocument.Parse(body);
                        var transactionId = leaseRequest.RootElement.TryGetProperty("transactionId", out var transactionElement)
                            && transactionElement.ValueKind == JsonValueKind.String
                                ? transactionElement.GetString()
                                : null;
                        json = JsonSerializer.Serialize(new
                        {
                            ok = true,
                            allowed = true,
                            youHold = true,
                            heldByOther = false,
                            transactionId
                        });
                        status = "200 OK";
                    }

                    if (method == "POST" &&
                        path.Equals("/api/v1/agent/recording", StringComparison.Ordinal) &&
                        _recordingResponse is not null)
                    {
                        using var doc = JsonDocument.Parse(body);
                        var action = doc.RootElement.GetProperty("action").GetString() ?? "";
                        var response = _recordingResponse(action);
                        json = response.Body;
                        status = response.StatusCode == 200 ? "200 OK" : "400 Bad Request";
                    }

                    if (method == "GET" &&
                        path.StartsWith("/api/v1/ui/hit-test?", StringComparison.Ordinal) &&
                        _hitTestResponse is not null)
                    {
                        json = _hitTestResponse;
                        status = "200 OK";
                    }

                    if (method == "POST" &&
                        path.Equals("/api/v1/ui/actions/tap", StringComparison.Ordinal) &&
                        _tapResponse is not null)
                    {
                        using var tapRequest = JsonDocument.Parse(body);
                        var elementId = tapRequest.RootElement.GetProperty("elementId").GetString() ?? "";
                        TapIds.Add(elementId);
                        json = JsonSerializer.Serialize(new { success = _tapResponse(elementId) });
                        status = "200 OK";
                    }

                    if (method == "GET" &&
                        path.Equals("/api/v1/ui/elements/e1/properties", StringComparison.Ordinal) &&
                        _propertyDescriptorsResponse is not null)
                    {
                        json = _propertyDescriptorsResponse;
                        status = "200 OK";
                    }

                    var m = PropRoute.Match(path);
                    if (m.Success)
                    {
                        var key = $"{m.Groups[1].Value}|{m.Groups[2].Value}";
                        if (method == "PUT")
                        {
                            using var doc = JsonDocument.Parse(body);
                            var value = doc.RootElement.GetProperty("value").GetString() ?? "";
                            if (_rejectProperty?.Invoke(m.Groups[2].Value, value) == true)
                            {
                                json = "{\"error\":\"invalid property value\"}";
                                status = "400 Bad Request";
                            }
                            else
                            {
                                _props[key] = value;
                                _propertyAccepted?.Invoke(m.Groups[2].Value, value);
                                json = "{\"success\":true}";
                                status = "200 OK";
                            }
                        }
                        else if (method == "GET")
                        {
                            var value = _props.TryGetValue(key, out var v) ? v : null;
                            json = $"{{\"value\":{JsonSerializer.Serialize(value)}}}";
                            status = "200 OK";
                        }
                    }

                    var payload = Encoding.UTF8.GetBytes(json);
                    var header = $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
                    await stream.WriteAsync(payload, ct);
                    await stream.FlushAsync(ct);
                }
                catch { /* connection torn down — irrelevant */ }
            }
        }

        private static async Task<(string Method, string Path, string Body)> ReadRequest(NetworkStream stream, CancellationToken ct)
        {
            var buf = new byte[8192];
            var sb = new StringBuilder();
            int headerEnd;
            while ((headerEnd = sb.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal)) < 0)
            {
                var n = await stream.ReadAsync(buf, ct);
                if (n <= 0) break;
                sb.Append(Encoding.UTF8.GetString(buf, 0, n));
            }
            var text = sb.ToString();
            var firstLine = text.Split("\r\n", 2)[0].Split(' ');
            var method = firstLine.Length > 0 ? firstLine[0] : "";
            var path = firstLine.Length > 1 ? firstLine[1] : "";

            var contentLength = 0;
            foreach (var line in text.Split("\r\n"))
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);

            var body = headerEnd >= 0 ? text[(headerEnd + 4)..] : "";
            while (Encoding.UTF8.GetByteCount(body) < contentLength)
            {
                var n = await stream.ReadAsync(buf, ct);
                if (n <= 0) break;
                body += Encoding.UTF8.GetString(buf, 0, n);
            }
            return (method, path, body);
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            try { await _loop; } catch { }
        }
    }
}
