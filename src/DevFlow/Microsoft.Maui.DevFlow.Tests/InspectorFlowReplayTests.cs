using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.DevFlow.Flows;
using Microsoft.Maui.Cli.DevFlow.Inspector;

namespace Microsoft.Maui.DevFlow.Tests;

public class InspectorFlowReplayTests
{
    [Fact]
    public async Task WorkflowFiles_ListAndLoad_UsesProjectMauiTests()
    {
        var root = Path.Combine(Path.GetTempPath(), $"devflow-workflows-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "maui-tests"));
        var project = Path.Combine(root, "App.csproj");
        var markdown = FlowMarkdown.Serialize(ValidFlow());
        await File.WriteAllTextAsync(project, "<Project />");
        await File.WriteAllTextAsync(Path.Combine(root, "maui-tests", "smoke.md"), markdown);
        try
        {
            await using var agent = new ReplayAgent(recording: false);
            await using var inspector = await StartInspectorAsync(agent.Port, project);
            using var http = new HttpClient();
            var token = await ReadInspectorTokenAsync(http, inspector.Url);

            var list = await AuthorizedPostAsync(http, $"{inspector.Url}/api/flows/files/list", "{}", token);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            var tests = listJson.RootElement.GetProperty("tests");
            Assert.Single(tests.EnumerateArray());
            Assert.Equal("smoke.md", tests[0].GetProperty("name").GetString());

            var load = await AuthorizedPostAsync(
                http,
                $"{inspector.Url}/api/flows/files/load",
                "{\"name\":\"smoke.md\"}",
                token);
            Assert.Equal(HttpStatusCode.OK, load.StatusCode);
            using var loadJson = JsonDocument.Parse(await load.Content.ReadAsStringAsync());
            Assert.Equal("smoke.md", loadJson.RootElement.GetProperty("name").GetString());
            Assert.Equal(1, loadJson.RootElement.GetProperty("steps").GetInt32());
            Assert.Equal(markdown, loadJson.RootElement.GetProperty("markdown").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WorkflowFiles_AreTokenGatedAndRejectUnsafeOrInvalidFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"devflow-workflows-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "maui-tests"));
        var project = Path.Combine(root, "App.csproj");
        await File.WriteAllTextAsync(project, "<Project />");
        await File.WriteAllTextAsync(Path.Combine(root, "maui-tests", "invalid.md"), "not a flow");
        await File.WriteAllBytesAsync(
            Path.Combine(root, "maui-tests", "large.md"),
            new byte[1_048_577]);
        try
        {
            await using var agent = new ReplayAgent(recording: false);
            await using var inspector = await StartInspectorAsync(agent.Port, project);
            using var http = new HttpClient();

            var forbidden = await http.PostAsync($"{inspector.Url}/api/flows/files/list", Json("{}"));
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            var token = await ReadInspectorTokenAsync(http, inspector.Url);
            var traversal = await AuthorizedPostAsync(
                http,
                $"{inspector.Url}/api/flows/files/load",
                "{\"name\":\"../outside.md\"}",
                token);
            Assert.Equal(HttpStatusCode.BadRequest, traversal.StatusCode);

            var invalid = await AuthorizedPostAsync(
                http,
                $"{inspector.Url}/api/flows/files/load",
                "{\"name\":\"invalid.md\"}",
                token);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var invalidJson = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
            Assert.Contains(
                "maui-test",
                invalidJson.RootElement.GetProperty("error").GetString(),
                StringComparison.OrdinalIgnoreCase);

            var large = await AuthorizedPostAsync(
                http,
                $"{inspector.Url}/api/flows/files/load",
                "{\"name\":\"large.md\"}",
                token);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Replay_InvalidFlow_ReturnsBadRequest()
    {
        await using var agent = new ReplayAgent(recording: false);
        await using var inspector = await StartInspectorAsync(agent.Port);
        using var http = new HttpClient();
        var flow = new MauiFlow
        {
            Name = "invalid",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.SetProperty,
                    Args = new FlowStepArgs
                    {
                        Selector = new FlowSelector { AutomationId = "label" },
                        Value = "hello",
                    },
                },
            },
        };

        var response = await http.PostAsync(
            $"{inspector.Url}/api/flows/replay",
            Json(JsonSerializer.Serialize(new { markdown = FlowMarkdown.Serialize(flow) })));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Flow failed validation", body);
        Assert.Contains("setProperty requires a property name", body);
    }

    [Fact]
    public async Task Replay_WhileRecording_ReturnsConflict()
    {
        await using var agent = new ReplayAgent(recording: true);
        await using var inspector = await StartInspectorAsync(agent.Port);
        using var http = new HttpClient();

        var response = await http.PostAsync(
            $"{inspector.Url}/api/flows/replay",
            Json(JsonSerializer.Serialize(new { markdown = FlowMarkdown.Serialize(ValidFlow()) })));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Stop the active recording", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"markdown\":123}")]
    public async Task Replay_InvalidJsonShape_ReturnsBadRequest(string body)
    {
        await using var agent = new ReplayAgent(recording: false);
        await using var inspector = await StartInspectorAsync(agent.Port);
        using var http = new HttpClient();

        var response = await http.PostAsync(
            $"{inspector.Url}/api/flows/replay",
            Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RecordStep_Assert_ForwardsSyntheticObservation()
    {
        await using var agent = new ReplayAgent(recording: true);
        await using var inspector = await StartInspectorAsync(agent.Port);
        using var http = new HttpClient();
        const string assertsJson =
            "[{\"kind\":\"propEquals\",\"selector\":{\"automationId\":\"TodoDescription\"},\"name\":\"Text\",\"expected\":\"Hello\",\"verify\":true}]";

        var response = await http.PostAsync(
            $"{inspector.Url}/api/flows/record/step",
            Json(JsonSerializer.Serialize(new
            {
                recordingId = "recording",
                action = FlowActions.Assert.ToUpperInvariant(),
                assertsJson
            })));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, responseJson.RootElement.GetProperty("seq").GetInt32());
        Assert.Equal(2, responseJson.RootElement.GetProperty("stepCount").GetInt32());

        using var forwarded = JsonDocument.Parse(Assert.IsType<string>(agent.LastRecordingRequestBody));
        Assert.Equal("observe", forwarded.RootElement.GetProperty("action").GetString());
        var observation = forwarded.RootElement.GetProperty("observation");
        Assert.Equal(FlowActions.Assert, observation.GetProperty("action").GetString());
        Assert.Equal(assertsJson, observation.GetProperty("assertsJson").GetString());
    }

    [Fact]
    public async Task Replay_RacingRecordingStart_ReturnsConflict()
    {
        await using var agent = new ReplayAgent(recording: false, blockRecordingStart: true);
        await using var inspector = await StartInspectorAsync(agent.Port);
        using var http = new HttpClient();

        var startTask = http.PostAsync(
            $"{inspector.Url}/api/flows/record/start",
            Json("{\"name\":\"race\"}"));
        await agent.RecordingStartEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var replayTask = http.PostAsync(
            $"{inspector.Url}/api/flows/replay",
            Json(JsonSerializer.Serialize(new { markdown = FlowMarkdown.Serialize(ValidFlow()) })));

        await Task.Delay(100);
        Assert.False(replayTask.IsCompleted);

        agent.AllowRecordingStart.TrySetResult();
        var start = await startTask;
        var replay = await replayTask;

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Contains("Stop the active recording", await replay.Content.ReadAsStringAsync());
    }

    private static MauiFlow ValidFlow() => new()
    {
        Name = "valid",
        Steps =
        {
            new FlowStep
            {
                Seq = 1,
                Action = FlowActions.Assert,
                Asserts = new()
                {
                    new FlowAssert
                    {
                        Kind = "exists",
                        Verify = false,
                        Selector = new FlowSelector { AutomationId = "label" },
                    },
                },
            },
        },
    };

    private static async Task<RunningInspector> StartInspectorAsync(int agentPort, string? project = null)
    {
        var port = FreePort();
        var inspector = project is null
            ? new InspectorServer(port, "127.0.0.1", agentPort)
            : new InspectorServer(
                port,
                "127.0.0.1",
                agentPort,
                embedToken: null,
                agentId: "agent",
                appName: "App",
                platform: "test",
                project,
                sessionId: null);
        inspector.Start();
        using var http = new HttpClient();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var response = await http.GetAsync($"http://127.0.0.1:{port}/devflow.js");
                if (response.IsSuccessStatusCode)
                    return new RunningInspector(inspector, $"http://127.0.0.1:{port}");
            }
            catch (HttpRequestException)
            {
            }
            await Task.Delay(25);
        }

        await inspector.StopAsync();
        inspector.Dispose();
        throw new InvalidOperationException("Inspector did not start.");
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<string> ReadInspectorTokenAsync(HttpClient http, string url)
    {
        var page = await http.GetStringAsync($"{url}/");
        var token = Regex.Match(
            page,
            "<meta\\s+name=\"devflow-inspector-token\"\\s+content=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return token;
    }

    private static Task<HttpResponseMessage> AuthorizedPostAsync(
        HttpClient http,
        string url,
        string body,
        string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = Json(body)
        };
        request.Headers.Add("X-DevFlow-Inspector-Token", token);
        return http.SendAsync(request);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class RunningInspector : IAsyncDisposable
    {
        private readonly InspectorServer _inspector;

        public RunningInspector(InspectorServer inspector, string url)
        {
            _inspector = inspector;
            Url = url;
        }

        public string Url { get; }

        public async ValueTask DisposeAsync()
        {
            await _inspector.StopAsync();
            _inspector.Dispose();
        }
    }

    private sealed class ReplayAgent : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private bool _recording;
        private readonly bool _blockRecordingStart;

        public ReplayAgent(bool recording, bool blockRecordingStart = false)
        {
            _recording = recording;
            _blockRecordingStart = blockRecordingStart;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _loop = AcceptLoopAsync(_cts.Token);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public TaskCompletionSource RecordingStartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowRecordingStart { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? LastRecordingRequestBody { get; private set; }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }
                _ = HandleAsync(client, ct);
            }
        }

        private async Task HandleAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var (method, path, requestBody) = await ReadRequestAsync(stream, ct);
                    var (status, body) = await RouteAsync(method, path, requestBody, ct);
                    var payload = Encoding.UTF8.GetBytes(body);
                    var header =
                        $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
                    await stream.WriteAsync(payload, ct);
                    await stream.FlushAsync(ct);
                }
                catch
                {
                }
            }
        }

        private async Task<(string Status, string Body)> RouteAsync(
            string method,
            string path,
            string body,
            CancellationToken ct)
        {
            if (method == "GET" && path == "/api/v1/agent/status")
            {
                return ("200 OK",
                    "{\"running\":true,\"app\":{\"name\":\"Fake\"},\"device\":{\"platform\":\"WinUI\"}}");
            }
            if (method == "POST" && path == "/api/v1/agent/lease")
            {
                return ("200 OK",
                    "{\"ok\":true,\"allowed\":true,\"youHold\":true,\"heldByOther\":false,\"authority\":\"broker\"}");
            }
            if (method == "POST" && path == "/api/v1/agent/recording")
            {
                LastRecordingRequestBody = body;
                string? action = null;
                try
                {
                    using var document = JsonDocument.Parse(body);
                    action = document.RootElement.TryGetProperty("action", out var value)
                        ? value.GetString()
                        : null;
                }
                catch (JsonException)
                {
                }

                if (action == "start")
                {
                    if (_blockRecordingStart)
                    {
                        RecordingStartEntered.TrySetResult();
                        await AllowRecordingStart.Task.WaitAsync(ct);
                    }
                    _recording = true;
                    return ("200 OK",
                        "{\"ok\":true,\"recording\":true,\"recordingId\":\"recording\",\"name\":\"race\",\"steps\":0}");
                }
                if (action == "observe")
                {
                    return ("200 OK",
                        "{\"ok\":true,\"recording\":true,\"recordingId\":\"recording\",\"steps\":2,\"seq\":2}");
                }
                if (action is "stop" or "cancel")
                    _recording = false;
                return ("200 OK",
                    $"{{\"ok\":true,\"recording\":{_recording.ToString().ToLowerInvariant()},\"steps\":1}}");
            }
            return ("404 Not Found", "{\"error\":\"not found\"}");
        }

        private static async Task<(string Method, string Path, string Body)> ReadRequestAsync(
            NetworkStream stream,
            CancellationToken ct)
        {
            var buffer = new byte[4096];
            var text = new StringBuilder();
            var headerEnd = -1;
            while ((headerEnd = text.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal)) < 0)
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read <= 0)
                    break;
                text.Append(Encoding.UTF8.GetString(buffer, 0, read));
            }

            var request = text.ToString();
            var parts = request.Split("\r\n", 2)[0].Split(' ');
            var contentLength = 0;
            foreach (var line in request.Split("\r\n"))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
            }

            var body = headerEnd >= 0 ? request[(headerEnd + 4)..] : "";
            while (Encoding.UTF8.GetByteCount(body) < contentLength)
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read <= 0)
                    break;
                body += Encoding.UTF8.GetString(buffer, 0, read);
            }

            return (
                parts.Length > 0 ? parts[0] : "",
                parts.Length > 1 ? parts[1].Split('?', 2)[0] : "",
                body);
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            try
            {
                await _loop;
            }
            catch
            {
            }
            _cts.Dispose();
        }
    }
}
