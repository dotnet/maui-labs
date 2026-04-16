using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.Profiling;

namespace Microsoft.Maui.DevFlow.Tests;

public class ProfilerAgentClientTests
{
    [Fact]
    public async Task Profiler_StartStopAndPollFlow_WorksThroughAgentClient()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            for (var i = 0; i < 3; i++)
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var request = await ReadRequestAsync(stream);

                if (request.Contains("POST /api/v1/profiler/sessions", StringComparison.Ordinal))
                {
                    var body = """
                    {
                      "session": {
                        "sessionId": "s-1",
                        "startedAtUtc": "2026-01-01T00:00:00Z",
                        "sampleIntervalMs": 500,
                        "isActive": true
                      },
                      "capabilities": {
                        "available": true
                      }
                    }
                    """;
                    await WriteJsonResponseAsync(stream, body);
                    continue;
                }

                if (request.Contains("GET /api/v1/profiler/sessions/s-1/samples", StringComparison.Ordinal))
                {
                    var body = """
                    {
                      "sessionId": "s-1",
                      "samples": [
                        {
                          "tsUtc": "2026-01-01T00:00:00.500Z",
                          "fps": 60.0,
                          "frameTimeMsP50": 16.6,
                          "frameTimeMsP95": 20.1,
                          "worstFrameTimeMs": 48.2,
                          "managedBytes": 2048,
                          "nativeMemoryBytes": 8192,
                          "nativeMemoryKind": "android.native-heap-allocated",
                          "gc0": 1,
                          "gc1": 0,
                          "gc2": 0,
                          "cpuPercent": 12.5,
                          "threadCount": 8,
                          "jankFrameCount": 3,
                          "uiThreadStallCount": 1,
                          "frameSource": "native.android.choreographer",
                          "frameQuality": "estimated"
                        }
                      ],
                      "markers": [
                        {
                          "tsUtc": "2026-01-01T00:00:00.300Z",
                          "type": "navigation.start",
                          "name": "//native"
                        }
                      ],
                      "spans": [
                        {
                          "spanId": "sp-1",
                          "startTsUtc": "2026-01-01T00:00:00.300Z",
                          "endTsUtc": "2026-01-01T00:00:00.340Z",
                          "durationMs": 40.0,
                          "kind": "ui.operation",
                          "name": "action.scroll",
                          "status": "ok",
                          "threadId": 12
                        }
                      ],
                      "sampleCursor": 1,
                      "markerCursor": 1,
                      "spanCursor": 1,
                      "isActive": true
                    }
                    """;
                    await WriteJsonResponseAsync(stream, body);
                    continue;
                }

                if (request.Contains("DELETE /api/v1/profiler/sessions/s-1", StringComparison.Ordinal))
                {
                    var body = """
                    {
                      "session": {
                        "sessionId": "s-1",
                        "startedAtUtc": "2026-01-01T00:00:00Z",
                        "sampleIntervalMs": 500,
                        "isActive": false
                      }
                    }
                    """;
                    await WriteJsonResponseAsync(stream, body);
                    continue;
                }

                throw new InvalidOperationException($"Unexpected request: {request}");
            }
        });

        using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", port);

        var started = await client.StartProfilerAsync(500);
        Assert.NotNull(started);
        Assert.Equal("s-1", started.SessionId);
        Assert.True(started.IsActive);

        var batch = await client.GetProfilerSamplesAsync(started.SessionId);
        Assert.NotNull(batch);
        Assert.Equal("s-1", batch.SessionId);
        Assert.Single(batch.Samples);
        Assert.Single(batch.Markers);
        Assert.Single(batch.Spans);
        Assert.Equal("native.android.choreographer", batch.Samples[0].FrameSource);
        Assert.Equal(3, batch.Samples[0].JankFrameCount);
        Assert.Equal(8192, batch.Samples[0].NativeMemoryBytes);
        Assert.Equal("android.native-heap-allocated", batch.Samples[0].NativeMemoryKind);
        Assert.Equal(1, batch.SampleCursor);
        Assert.Equal(1, batch.MarkerCursor);
        Assert.Equal(1, batch.SpanCursor);

        var stopped = await client.StopProfilerAsync(started.SessionId);
        Assert.NotNull(stopped);
        Assert.False(stopped.IsActive);

        await serverTask;
    }

    [Fact]
    public async Task Profiler_SessionHandlers_RejectUnknownSessionIds()
    {
        using var service = new DevFlowAgentService(new AgentOptions { Enabled = false, EnableProfiler = true });

        var storeField = typeof(DevFlowAgentService).GetField("_profilerSessions", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(storeField);

        var store = Assert.IsType<ProfilerSessionStore>(storeField.GetValue(service));
        var session = store.Start(250);

        var method = typeof(DevFlowAgentService).GetMethod("HandleProfilerSamples", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var missingSessionRequest = new HttpRequest
        {
            RouteParams = new Dictionary<string, string> { ["id"] = "other-session" },
            QueryParams = new Dictionary<string, string>()
        };

        var missingSessionResponse = await ((Task<HttpResponse>)method.Invoke(service, [missingSessionRequest])!);
        Assert.Equal(404, missingSessionResponse.StatusCode);
        Assert.Contains("other-session", missingSessionResponse.Body);

        var currentSessionRequest = new HttpRequest
        {
            RouteParams = new Dictionary<string, string> { ["id"] = session.SessionId },
            QueryParams = new Dictionary<string, string>()
        };

        var currentSessionResponse = await ((Task<HttpResponse>)method.Invoke(service, [currentSessionRequest])!);
        Assert.Equal(200, currentSessionResponse.StatusCode);
        Assert.Contains(session.SessionId, currentSessionResponse.Body);
    }

    private static async Task<string> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[8192];
        var read = await stream.ReadAsync(buffer);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static async Task WriteJsonResponseAsync(NetworkStream stream, string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var headers = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
        var headerBytes = Encoding.UTF8.GetBytes(headers);
        await stream.WriteAsync(headerBytes);
        await stream.WriteAsync(bodyBytes);
    }
}
