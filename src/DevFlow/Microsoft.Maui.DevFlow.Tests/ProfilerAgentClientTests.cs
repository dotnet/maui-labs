using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.Network;
using Microsoft.Maui.DevFlow.Agent.Core.Profiling;
using Microsoft.Maui.DevFlow.Driver;

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
                      "stopToken": "stop-token-1",
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
                      "sampleMetadata": {
                        "oldestCursor": 1,
                        "latestCursor": 3,
                        "lostCount": 0,
                        "availableCount": 3
                      },
                      "markerMetadata": {
                        "oldestCursor": 1,
                        "latestCursor": 1,
                        "lostCount": 0,
                        "availableCount": 1
                      },
                      "spanMetadata": {
                        "oldestCursor": 1,
                        "latestCursor": 1,
                        "lostCount": 0,
                        "availableCount": 1
                      },
                      "isActive": true
                    }
                    """;
                    await WriteJsonResponseAsync(stream, body);
                    continue;
                }

                if (request.Contains("DELETE /api/v1/profiler/sessions/s-1", StringComparison.Ordinal))
                {
                    var requestLine = request.Split("\r\n", StringSplitOptions.None)[0];
                    Assert.DoesNotContain("stop-token-1", requestLine, StringComparison.Ordinal);
                    Assert.Contains(
                        "X-DevFlow-Profiler-Stop-Token: stop-token-1",
                        request,
                        StringComparison.OrdinalIgnoreCase);
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

        using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", port)
        {
            AutoAcquireMutationLease = false
        };

        var started = await client.StartProfilerAsync(500);
        Assert.NotNull(started);
        Assert.Equal("s-1", started.SessionId);
        Assert.Equal("stop-token-1", started.StopToken);
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
        Assert.Equal(1, batch.SampleMetadata.OldestCursor);
        Assert.Equal(3, batch.SampleMetadata.LatestCursor);
        Assert.Equal(3, batch.SampleMetadata.AvailableCount);

        var stopped = await client.StopProfilerAsync();
        Assert.NotNull(stopped);
        Assert.False(stopped.IsActive);

        await serverTask;
    }

    [Fact]
    public async Task LegacyProfilerStop_RejectsSessionsNotCreatedByThisClient()
    {
        using var client = new AgentClient("localhost", 1);

        var profilerError = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.StopProfilerAsync("external-session"));
        Assert.Contains("did not create", profilerError.Message, StringComparison.OrdinalIgnoreCase);

        var performanceError = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.StopPerformanceSessionAsync("external-session"));
        Assert.Contains("did not create", performanceError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LegacyProfilerStart_StopsTheSessionAndRequestsAnAgentUpgrade()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using (var client = await listener.AcceptTcpClientAsync())
            using (var stream = client.GetStream())
            {
                var request = await ReadRequestAsync(stream);
                Assert.Contains("POST /api/v1/profiler/sessions", request, StringComparison.Ordinal);
                await WriteJsonResponseAsync(stream, """
                    {
                      "session": {
                        "sessionId": "legacy-session",
                        "startedAtUtc": "2026-01-01T00:00:00Z",
                        "sampleIntervalMs": 500,
                        "isActive": true
                      }
                    }
                    """);
            }

            using (var client = await listener.AcceptTcpClientAsync())
            using (var stream = client.GetStream())
            {
                var request = await ReadRequestAsync(stream);
                Assert.Contains(
                    "DELETE /api/v1/profiler/sessions/legacy-session",
                    request,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "X-DevFlow-Profiler-Stop-Token",
                    request,
                    StringComparison.OrdinalIgnoreCase);
                await WriteJsonResponseAsync(stream, """
                    {
                      "session": {
                        "sessionId": "legacy-session",
                        "startedAtUtc": "2026-01-01T00:00:00Z",
                        "sampleIntervalMs": 500,
                        "isActive": false
                      }
                    }
                    """);
            }
        });

        using var client = new AgentClient("localhost", port)
        {
            AutoAcquireMutationLease = false
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.StartProfilerAsync(500));

        Assert.Contains("legacy profiler protocol", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("was stopped", error.Message, StringComparison.OrdinalIgnoreCase);
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
        var currentSessionBody = currentSessionResponse.Body
            ?? throw new InvalidOperationException("Profiler samples response did not include a body.");
        Assert.Contains(session.SessionId, currentSessionBody);
        Assert.Contains("\"sampleMetadata\"", currentSessionBody);
        Assert.Contains("\"oldestCursor\"", currentSessionBody);
        var batch = DriverJson.Deserialize<Microsoft.Maui.DevFlow.Driver.ProfilerBatch>(currentSessionBody);
        Assert.NotNull(batch);
        Assert.Equal(session.SessionId, batch.SessionId);
        Assert.Equal(0, batch.SampleMetadata.LatestCursor);
    }

    [Fact]
    public async Task ProfilerCapabilities_UseCompatibleCamelCaseContract()
    {
        using var service = new DevFlowAgentService(new AgentOptions { Enabled = false, EnableProfiler = true });
        var method = typeof(DevFlowAgentService).GetMethod(
            "HandleProfilerCapabilities",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var response = await ((Task<HttpResponse>)method.Invoke(service, [new HttpRequest()])!);

        Assert.Equal(200, response.StatusCode);
        Assert.Contains("\"available\"", response.Body);
        Assert.Contains("\"fpsSupported\"", response.Body);
        Assert.DoesNotContain("\"Available\"", response.Body);
        Assert.DoesNotContain("\"FpsSupported\"", response.Body);
    }

    [Fact]
    public async Task ProfilerStop_RevalidatesExpectedSessionInsideLifecycleGate()
    {
        using var service = new DevFlowAgentService(
            new AgentOptions { Enabled = false, EnableProfiler = true });
        var storeField = typeof(DevFlowAgentService).GetField(
            "_profilerSessions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(storeField);
        var store = Assert.IsType<ProfilerSessionStore>(storeField.GetValue(service));
        var first = store.Start(250);
        store.Stop();
        var replacement = store.Start(250);

        var method = typeof(DevFlowAgentService).GetMethod(
            "StopProfilerAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(
            method.Invoke(service, [first.SessionId, first.StopToken, 100, 10, false]));
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task);

        Assert.Null(result);
        Assert.True(store.IsActive);
        Assert.Equal(replacement.SessionId, store.CurrentSession!.SessionId);
    }

    [Fact]
    public void CapturedNetworkRequest_UsesRequestTimestampAsProfilerSpanStart()
    {
        using var service = new DevFlowAgentService(new AgentOptions { Enabled = false, EnableProfiler = true });
        var storeField = typeof(DevFlowAgentService).GetField("_profilerSessions", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(storeField);
        var store = Assert.IsType<ProfilerSessionStore>(storeField.GetValue(service));
        store.Start(250);

        var timestamp = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var entry = new NetworkRequestEntry
        {
            Timestamp = timestamp,
            Method = "GET",
            Url = "https://example.test/api",
            Path = "/api",
            DurationMs = 125
        };
        var handler = typeof(DevFlowAgentService).GetMethod(
            "HandleCapturedNetworkRequest",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(handler);

        handler.Invoke(service, [entry]);

        var batch = store.GetBatch(sampleCursor: 0, markerCursor: 0, spanCursor: 0, limit: 10);
        var span = Assert.Single(batch.Spans);
        Assert.Equal(timestamp.UtcDateTime, span.StartTsUtc);
        Assert.Equal(timestamp.UtcDateTime.AddMilliseconds(125), span.EndTsUtc);
        Assert.Equal(125, span.DurationMs);
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
