using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Flows;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// End-to-end replay tests: the real <see cref="AgentClient"/> against a stateful loopback fake
/// agent. Exercises selector resolution, drive, and hard-assertion verification.
/// </summary>
public class FlowReplayTests
{
    private static MauiFlow LoginFlow() => new()
    {
        Name = "login",
        Steps =
        {
            new FlowStep
            {
                Seq = 1, Action = "tap",
                Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "submit" } },
                Asserts = new() { new FlowAssert { Kind = "exists", Selector = new FlowSelector { AutomationId = "submit" }, Verify = true } },
            },
            new FlowStep
            {
                Seq = 2, Action = "fill", Value = "hello",
                Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "name" }, Text = "hello" },
                Asserts = new() { new FlowAssert { Kind = "propEquals", Selector = new FlowSelector { AutomationId = "name" }, Name = "Text", Expected = "hello", Verify = true } },
            },
        },
    };

    [Fact]
    public async Task Replay_PassingFlow_ReportsAllPassed()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var report = await new FlowReplayer(client, pollTries: 2, pollGapMs: 10).ReplayAsync(LoginFlow());

        Assert.True(report.Ok, System.Text.Json.JsonSerializer.Serialize(report));
        Assert.Equal(2, report.Passed);
        Assert.Equal(0, report.Failed);
        Assert.Equal(new[] { "btn" }, agentSrv.Taps.ToArray());     // #submit resolved to id "btn"
        Assert.Equal("hello", agentSrv.TextOf("entry"));            // fill actually applied
        Assert.True(report.Results[0].Asserts[0].Ok);              // exists passed
        Assert.True(report.Results[1].Asserts[0].Ok);              // propEquals passed
    }

    [Fact]
    public async Task Replay_UnresolvableTarget_FailsStepAndSkipsAsserts()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Name = "bad",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1, Action = "tap",
                    Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "nope" } },
                    Asserts = new() { new FlowAssert { Kind = "exists", Selector = new FlowSelector { AutomationId = "nope" }, Verify = true } },
                },
            },
        };

        var report = await new FlowReplayer(client, pollTries: 1, pollGapMs: 0).ReplayAsync(flow);

        Assert.False(report.Ok);
        Assert.Equal(1, report.Failed);
        Assert.Contains("could not be resolved", report.Results[0].Error);
        Assert.True(report.Results[0].Asserts[0].Skipped);        // asserts not run after a failed drive
    }

    [Fact]
    public async Task Replay_PropEqualsMismatch_FailsStep()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Name = "mismatch",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1, Action = "fill",
                    Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "name" }, Text = "hello" },
                    Asserts = new() { new FlowAssert { Kind = "propEquals", Selector = new FlowSelector { AutomationId = "name" }, Name = "Text", Expected = "WRONG", Verify = true } },
                },
            },
        };

        var report = await new FlowReplayer(client, pollTries: 2, pollGapMs: 10).ReplayAsync(flow);

        Assert.False(report.Ok);
        Assert.False(report.Results[0].Asserts[0].Ok);
        Assert.Equal("hello", report.Results[0].Asserts[0].Actual);   // observed actual is reported
    }

    [Fact]
    public async Task Replay_AssertOnlyStep_RunsAssertionWithoutDriving()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Name = "assert-initial",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1, Action = "assert",
                    Asserts = new()
                    {
                        new FlowAssert { Kind = "exists", Selector = new FlowSelector { AutomationId = "submit" }, Verify = true },
                        new FlowAssert { Kind = "propEquals", Selector = new FlowSelector { AutomationId = "submit" }, Name = "Text", Expected = "Go", Verify = true },
                    },
                },
            },
        };

        var report = await new FlowReplayer(client, pollTries: 2, pollGapMs: 10).ReplayAsync(flow);

        Assert.True(report.Ok, JsonSerializer.Serialize(report));
        Assert.Empty(agentSrv.Taps);                       // an assert step drives nothing
        Assert.True(report.Results[0].Asserts[0].Ok);      // exists passed
        Assert.True(report.Results[0].Asserts[1].Ok);      // propEquals Text == "Go" passed
    }

    [Fact]
    public async Task Replay_AssertOnlyStep_FailsWhenExpectationWrong()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Name = "assert-wrong",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1, Action = "assert",
                    Asserts = new() { new FlowAssert { Kind = "propEquals", Selector = new FlowSelector { AutomationId = "submit" }, Name = "Text", Expected = "Nope", Verify = true } },
                },
            },
        };

        var report = await new FlowReplayer(client, pollTries: 2, pollGapMs: 10).ReplayAsync(flow);

        Assert.False(report.Ok);
        Assert.False(report.Results[0].Asserts[0].Ok);
    }

    [Fact]
    public async Task Replay_TargetAppearingAfterPreviousAction_IsRetried()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Name = "async-target",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = "tap",
                    Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "submit" } },
                },
                new FlowStep
                {
                    Seq = 2,
                    Action = "tap",
                    Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "late" } },
                },
            },
        };

        var report = await new FlowReplayer(client, pollTries: 6, pollGapMs: 75).ReplayAsync(flow);

        Assert.True(report.Ok, JsonSerializer.Serialize(report));
        Assert.Equal(new[] { "btn", "late-btn" }, agentSrv.Taps);
    }

    [Fact]
    public async Task Replay_ScrollWithUnresolvableSelector_FailsInsteadOfScrollingRoot()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Name = "missing-scroll-target",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = "scroll",
                    Args = new FlowStepArgs
                    {
                        Selector = new FlowSelector { AutomationId = "missing" },
                        Dy = 100,
                    },
                },
            },
        };

        var report = await new FlowReplayer(client, pollTries: 2, pollGapMs: 10).ReplayAsync(flow);

        Assert.False(report.Ok);
        Assert.Contains("scroll target could not be resolved", report.Results[0].Error);
    }

    // ── Stateful fake agent ──────────────────────────────────────────────────────
    private sealed class Element
    {
        public required string Id;
        public required string AutomationId;
        public required string Type;
        public string Text = "";
        public DateTimeOffset? VisibleAfter;
    }

    private sealed class RoutingAgent : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private readonly List<Element> _els = new()
        {
            new Element { Id = "btn", AutomationId = "submit", Type = "Button", Text = "Go" },
            new Element { Id = "entry", AutomationId = "name", Type = "Entry", Text = "" },
            new Element { Id = "late-btn", AutomationId = "late", Type = "Button", Text = "Later", VisibleAfter = DateTimeOffset.MaxValue },
        };

        public List<string> Taps { get; } = new();
        public string? TextOf(string id) => _els.FirstOrDefault(e => e.Id == id)?.Text;

        public RoutingAgent()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _loop = AcceptLoop(_cts.Token);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { break; }
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
                    var response = Route(method, path, body);
                    var payload = Encoding.UTF8.GetBytes(response ?? "{\"error\":\"not found\"}");
                    var status = response is null ? "404 Not Found" : "200 OK";
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
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
            }
            var body = headerEnd >= 0 ? text[(headerEnd + 4)..] : "";
            while (Encoding.UTF8.GetByteCount(body) < contentLength)
            {
                var n = await stream.ReadAsync(buf, ct);
                if (n <= 0) break;
                body += Encoding.UTF8.GetString(buf, 0, n);
            }
            return (method, path, body);
        }

        private string? Route(string method, string rawPath, string body)
        {
            var qIdx = rawPath.IndexOf('?');
            var path = qIdx >= 0 ? rawPath[..qIdx] : rawPath;
            var query = ParseQuery(qIdx >= 0 ? rawPath[(qIdx + 1)..] : "");

            if (method == "GET" && path == "/api/v1/agent/status")
                return "{\"running\":true}";

            if (method == "GET" && path == "/api/v1/ui/elements")
            {
                IEnumerable<Element> matches = _els.Where(e => e.VisibleAfter is null || e.VisibleAfter <= DateTimeOffset.UtcNow);
                if (query.TryGetValue("automationId", out var aid)) matches = matches.Where(e => e.AutomationId == aid);
                else if (query.TryGetValue("text", out var txt)) matches = matches.Where(e => e.Text == txt);
                else if (query.TryGetValue("type", out var ty)) matches = matches.Where(e => e.Type == ty);
                return JsonSerializer.Serialize(matches.Select(ToJson));
            }

            if (method == "GET" && path.StartsWith("/api/v1/ui/elements/") && path.Contains("/properties/"))
            {
                var (id, name) = PropPath(path);
                var el = _els.FirstOrDefault(e => e.Id == id);
                var value = el is null ? "" : name == "Text" ? el.Text : "";
                return JsonSerializer.Serialize(new { value });
            }

            if (method == "PUT" && path.Contains("/properties/"))
            {
                var (id, name) = PropPath(path);
                var el = _els.FirstOrDefault(e => e.Id == id);
                if (el is not null && name == "Text") el.Text = Field(body, "value") ?? "";
                return "{\"success\":true}";
            }

            if (method == "GET" && path.StartsWith("/api/v1/ui/elements/"))
            {
                var id = Uri.UnescapeDataString(path["/api/v1/ui/elements/".Length..]);
                var el = _els.FirstOrDefault(e => e.Id == id);
                return el is null ? "null" : JsonSerializer.Serialize(ToJson(el));
            }

            if (method == "POST" && path == "/api/v1/ui/actions/tap")
            {
                var id = Field(body, "elementId") ?? "";
                Taps.Add(id);
                if (id == "btn")
                    _els.First(e => e.Id == "late-btn").VisibleAfter = DateTimeOffset.UtcNow.AddMilliseconds(250);
                return "{\"success\":true}";
            }
            if (method == "POST" && path == "/api/v1/ui/actions/fill")
            {
                var id = Field(body, "elementId");
                var el = _els.FirstOrDefault(e => e.Id == id);
                if (el is not null) el.Text = Field(body, "text") ?? "";
                return "{\"success\":true}";
            }
            if (method == "POST" && path.StartsWith("/api/v1/ui/actions/"))
                return "{\"success\":true}";

            if (method == "PUT" && path == "/api/v1/device/app/theme")
                return "{\"theme\":\"dark\",\"requestedTheme\":\"dark\",\"effectiveTheme\":\"dark\",\"success\":true}";

            return null;
        }

        private static object ToJson(Element e) => new { id = e.Id, type = e.Type, fullType = e.Type, automationId = e.AutomationId, text = e.Text };

        private static (string Id, string Name) PropPath(string path)
        {
            // /api/v1/ui/elements/{id}/properties/{name}
            var parts = path.Split('/');
            var id = parts.Length > 5 ? Uri.UnescapeDataString(parts[5]) : "";
            var name = parts.Length > 7 ? Uri.UnescapeDataString(parts[7]) : "";
            return (id, name);
        }

        private static Dictionary<string, string> ParseQuery(string q)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var i = pair.IndexOf('=');
                if (i > 0) d[Uri.UnescapeDataString(pair[..i])] = Uri.UnescapeDataString(pair[(i + 1)..]);
            }
            return d;
        }

        private static string? Field(string body, string key)
        {
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                return doc.RootElement.TryGetProperty(key, out var v) ? v.GetString() : null;
            }
            catch { return null; }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            try { await _loop; } catch { }
            _cts.Dispose();
        }
    }
}
