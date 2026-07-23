using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Integration tests for the Agent HTTP server (standalone, no MAUI runtime needed).
/// Tests the HTTP server routing, request/response handling directly.
/// </summary>
public class AgentHttpServerTests : IDisposable
{
    private readonly int _port;

    public AgentHttpServerTests()
    {
        // Find a free port
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
    }

    [Fact]
    public async Task Start_ListensOnPort()
    {
        // We test the server independently using the Driver's AgentClient
        // Create a simple mock server to verify HTTP handling works
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("GET /api/v1/agent/status", request);

            var body = """{"agent":{"name":"test","version":"1.0"},"device":{"platform":"Test"},"app":{"name":"Sample"},"running":true}""";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
            client.Close();
        });

        using var agentClient = CreateUncoordinatedClient();
        var status = await agentClient.GetStatusAsync();

        Assert.NotNull(status);
        Assert.Equal("test", status.Agent?.Name);
        Assert.True(status.Running);

        listener.Stop();
    }

    [Fact]
    public async Task QueryEndpoint_ParsesQueryString()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("type=Button", request);
            Assert.Contains("text=Submit", request);

            var body = """[{"id":"btn1","type":"Button","fullType":"Microsoft.Maui.Controls.Button","text":"Submit","isVisible":true,"isEnabled":true}]""";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
            client.Close();
        });

        using var agentClient = CreateUncoordinatedClient();
        var results = await agentClient.QueryAsync(type: "Button", text: "Submit");

        Assert.Single(results);
        Assert.Equal("btn1", results[0].Id);
        Assert.Equal("Button", results[0].Type);
        Assert.Equal("Submit", results[0].Text);

        listener.Stop();
    }

    [Fact]
    public async Task ForcedLeaseTakeover_WaitsForAdmittedMutation()
    {
        using var server = new AgentHttpServer(_port);
        var mutationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMutation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.MutationLeaseValidator = request => Task.FromResult(new MutationLeaseStatus
        {
            Ok = true,
            Allowed = true,
            YouHold = true,
            LeaseId = request.Headers.TryGetValue("X-DevFlow-Lease", out var lease) ? lease : null,
        });
        server.MapPost("/api/v1/ui/mutate", async _ =>
        {
            mutationEntered.TrySetResult();
            await releaseMutation.Task;
            return HttpResponse.Json(new { ok = true });
        });
        server.MapPost("/api/v1/agent/lease", _ => Task.FromResult(HttpResponse.Json(new { ok = true })), requiresMutationLease: false);
        server.Start();
        try
        {
            using var mutationClient = new HttpClient();
            using var mutationRequest = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{_port}/api/v1/ui/mutate");
            mutationRequest.Headers.Add("X-DevFlow-Lease", "A");
            mutationRequest.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            var mutationTask = mutationClient.SendAsync(mutationRequest);
            await mutationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            using var takeoverClient = new HttpClient();
            var takeoverTask = takeoverClient.PostAsync(
                $"http://localhost:{_port}/api/v1/agent/lease",
                new StringContent("{\"action\":\"claim\",\"leaseId\":\"B\",\"force\":true}", Encoding.UTF8, "application/json"));

            await Task.Delay(100);
            Assert.False(takeoverTask.IsCompleted);

            releaseMutation.TrySetResult();
            Assert.True((await mutationTask).IsSuccessStatusCode);
            Assert.True((await takeoverTask).IsSuccessStatusCode);
        }
        finally
        {
            releaseMutation.TrySetResult();
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task StopAsync_CancelsMutationQueuedBehindAdmissionGate()
    {
        using var server = new AgentHttpServer(_port);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerCalls = 0;
        server.MutationLeaseValidator = _ => Task.FromResult(new MutationLeaseStatus
        {
            Ok = true,
            Allowed = true,
            YouHold = true,
        });
        server.MapPost("/api/v1/ui/mutate", async _ =>
        {
            if (Interlocked.Increment(ref handlerCalls) == 1)
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task;
            }
            return HttpResponse.Json(new { ok = true });
        });
        server.Start();
        try
        {
            using var client = new HttpClient();
            var first = client.PostAsync(
                $"http://localhost:{_port}/api/v1/ui/mutate",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = client.PostAsync(
                $"http://localhost:{_port}/api/v1/ui/mutate",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            var stop = server.StopAsync();
            releaseFirst.TrySetResult();
            await stop;
            try { await first; } catch { }
            try { await second; } catch { }

            Assert.Equal(1, handlerCalls);
        }
        finally
        {
            releaseFirst.TrySetResult();
        }
    }

    [Fact]
    public async Task BrowserOrigin_NonLoopback_IsRejectedBeforeRouting()
    {
        using var server = new AgentHttpServer(_port);
        var routed = false;
        server.MapGet("/api/v1/sensitive", _ =>
        {
            routed = true;
            return Task.FromResult(HttpResponse.Json(new { secret = "value" }));
        });
        server.Start();
        try
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{_port}/api/v1/sensitive");
            request.Headers.Add("Origin", "https://attacker.example");

            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.False(routed);
            Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task BrowserOrigin_SameAgentOrigin_IsAllowedAndReflectedExactly()
    {
        using var server = new AgentHttpServer(_port);
        server.MapGet("/api/v1/local", _ => Task.FromResult(HttpResponse.Json(new { ok = true })));
        server.Start();
        try
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{_port}/api/v1/local");
            var origin = $"http://127.0.0.1:{_port}";
            request.Headers.Add("Origin", origin);

            using var response = await client.SendAsync(request);

            Assert.True(response.IsSuccessStatusCode);
            Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://localhost:3000")]
    [InlineData("null")]
    [InlineData("file:///tmp/index.html")]
    public async Task BrowserOrigin_OtherLoopbackOrOpaqueOrigin_IsRejected(string origin)
    {
        using var server = new AgentHttpServer(_port);
        server.MapGet("/api/v1/local", _ => Task.FromResult(HttpResponse.Json(new { ok = true })));
        server.Start();
        try
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{_port}/api/v1/local");
            request.Headers.TryAddWithoutValidation("Origin", origin);

            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task NoOrigin_LocalClient_RemainsAllowedWithoutCorsHeader()
    {
        using var server = new AgentHttpServer(_port);
        server.MapGet("/api/v1/local", _ => Task.FromResult(HttpResponse.Json(new { ok = true })));
        server.Start();
        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync($"http://localhost:{_port}/api/v1/local");

            Assert.True(response.IsSuccessStatusCode);
            Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task ReadRequest_OversizedDeclaredBody_Returns413WithoutWaitingForBody()
    {
        using var server = new AgentHttpServer(_port);
        server.MapPost("/api/v1/echo", _ => Task.FromResult(HttpResponse.Json(new { ok = true })), requiresMutationLease: false);
        server.Start();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _port);
            using var stream = client.GetStream();
            var request = Encoding.ASCII.GetBytes(
                "POST /api/v1/echo HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Content-Type: application/json\r\n" +
                "Content-Length: 1048577\r\n" +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(request);
            await stream.FlushAsync();

            using var reader = new StreamReader(stream, Encoding.ASCII);
            var response = await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.StartsWith("HTTP/1.1 413", response, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task TapEndpoint_SendsPost()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("POST /api/v1/ui/actions/tap", request);
            Assert.Contains("elementId", request);

            var body = """{"success":true,"message":"Tapped"}""";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
            client.Close();
        });

        using var agentClient = CreateUncoordinatedClient();
        var result = await agentClient.TapAsync("btn1");
        Assert.True(result);

        listener.Stop();
    }

    [Fact]
    public async Task FillEndpoint_SendsPostWithText()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("POST /api/v1/ui/actions/fill", request);
            Assert.Contains("hello world", request);

            var body = """{"success":true,"message":"Text set"}""";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
            client.Close();
        });

        using var agentClient = CreateUncoordinatedClient();
        var result = await agentClient.FillAsync("entry1", "hello world");
        Assert.True(result);

        listener.Stop();
    }

    [Fact]
    public async Task WebViewNavigateEndpoint_SendsPostWithUrl()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("POST /api/v1/webview/navigate", request);
            Assert.Contains("https://example.com", request);
            Assert.Contains("BlazorMain", request);

            var body = """{"success":true}""";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        });

        using var agentClient = CreateUncoordinatedClient();
        var result = await agentClient.NavigateWebViewAsync("https://example.com", "BlazorMain");

        Assert.True(result);

        await acceptTask;
        listener.Stop();
    }

    [Fact]
    public async Task WebViewClickEndpoint_SendsSelector()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("POST /api/v1/webview/input/click", request);
            Assert.Contains("#submit", request);

            var body = """{"success":true}""";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        });

        using var agentClient = CreateUncoordinatedClient();
        var result = await agentClient.ClickWebViewAsync("#submit");

        Assert.True(result);

        await acceptTask;
        listener.Stop();
    }

    [Fact]
    public async Task WebViewFillEndpoint_SendsSelectorAndText()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("POST /api/v1/webview/input/fill", request);
            Assert.Contains("#email", request);
            Assert.Contains("user@example.com", request);

            var body = """{"success":true}""";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        });

        using var agentClient = CreateUncoordinatedClient();
        var result = await agentClient.FillWebViewAsync("#email", "user@example.com");

        Assert.True(result);

        await acceptTask;
        listener.Stop();
    }

    [Fact]
    public async Task WebViewTextEndpoint_SendsText()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("POST /api/v1/webview/input/text", request);
            Assert.Contains("hello from tests", request);

            var body = """{"success":true}""";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        });

        using var agentClient = CreateUncoordinatedClient();
        var result = await agentClient.InsertWebViewTextAsync("hello from tests");

        Assert.True(result);

        await acceptTask;
        listener.Stop();
    }

    [Fact]
    public async Task CapabilitiesEndpoint_ReturnsStructuredCapabilities()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("GET /api/v1/agent/capabilities", request);

            var body = """
            {
              "agent": { "name": "Microsoft.Maui.DevFlow.Agent", "version": "1.0.0", "framework": "maui", "frameworkVersion": "10.0.0" },
              "capabilities": {
                "ui.actions": { "version": 1, "features": ["tap", "batch"] },
                "webview": { "version": 1, "features": ["contexts", "evaluate"] },
                "network": { "version": 1, "features": ["list", "clear"] }
              }
            }
            """;
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        });

        using var agentClient = CreateUncoordinatedClient();
        var capabilities = await agentClient.GetCapabilitiesAsync();

        Assert.Equal("maui", capabilities.GetProperty("agent").GetProperty("framework").GetString());
        Assert.Contains("batch", capabilities.GetProperty("capabilities").GetProperty("ui.actions").GetProperty("features").EnumerateArray().Select(x => x.GetString()));

        await acceptTask;
        listener.Stop();
    }

    [Fact]
    public async Task JobsEndpoints_UseV1PathsAndParseResponses()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var buffer = new byte[8192];
                var read = await stream.ReadAsync(buffer);
                var request = Encoding.UTF8.GetString(buffer, 0, read);

                if (request.Contains("GET /api/v1/device/jobs", StringComparison.Ordinal))
                {
                    var body = """
                    {
                      "platform": "iOS",
                      "type": "BGTaskScheduler",
                      "supported": true,
                      "runSupported": true,
                      "jobs": [
                        {
                          "identifier": "com.example.refresh",
                          "type": "refresh",
                          "earliestBeginDate": ""
                        }
                      ]
                    }
                    """;
                    var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
                    continue;
                }

                if (request.Contains("POST /api/v1/device/jobs/com.example.refresh/run", StringComparison.Ordinal))
                {
                    Assert.Contains("\"type\":\"refresh\"", request);

                    var body = """{"success":true,"identifier":"com.example.refresh","type":"refresh"}""";
                    var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
                    continue;
                }

                throw new InvalidOperationException($"Unexpected request: {request}");
            }
        });

        using var agentClient = CreateUncoordinatedClient();

        var jobs = await agentClient.GetJobsAsync();
        Assert.True(jobs.GetProperty("supported").GetBoolean());
        Assert.True(jobs.GetProperty("runSupported").GetBoolean());
        Assert.Equal("com.example.refresh", jobs.GetProperty("jobs")[0].GetProperty("identifier").GetString());

        var result = await agentClient.RunJobAsync("com.example.refresh", "refresh");
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal("refresh", result.GetProperty("type").GetString());

        await acceptTask;
        listener.Stop();
    }

    [Fact]
    public async Task BatchEndpoint_SendsV1BatchPayload()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("POST /api/v1/ui/actions/batch", request);
            Assert.Contains("\"continueOnError\":true", request);
            Assert.Contains("\"type\":\"tap\"", request);
            Assert.Contains("\"elementId\":\"btn1\"", request);

            var body = """{"success":true,"results":[{"success":true}]}""";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        });

        using var agentClient = CreateUncoordinatedClient();
        var result = await agentClient.BatchAsync(
            [
                new JsonObject
                {
                    ["type"] = "tap",
                    ["elementId"] = "btn1"
                }
            ],
            continueOnError: true);

        Assert.True(result.GetProperty("success").GetBoolean());

        await acceptTask;
        listener.Stop();
    }

    [Fact]
    public async Task TreeEndpoint_ParsesNestedElements()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);

            var body = """
            [{
                "id": "page1", "type": "ContentPage", "fullType": "Microsoft.Maui.Controls.ContentPage",
                "isVisible": true, "isEnabled": true,
                "children": [{
                    "id": "layout1", "parentId": "page1", "type": "VerticalStackLayout",
                    "fullType": "Microsoft.Maui.Controls.VerticalStackLayout",
                    "isVisible": true, "isEnabled": true,
                    "children": [{
                        "id": "btn1", "parentId": "layout1", "type": "Button",
                        "fullType": "Microsoft.Maui.Controls.Button",
                        "text": "Click Me", "isVisible": true, "isEnabled": true
                    }]
                }]
            }]
            """;
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
            client.Close();
        });

        using var agentClient = CreateUncoordinatedClient();
        var tree = await agentClient.GetTreeAsync();

        Assert.Single(tree);
        Assert.Equal("ContentPage", tree[0].Type);
        Assert.NotNull(tree[0].Children);
        Assert.Single(tree[0].Children!);
        Assert.Equal("VerticalStackLayout", tree[0].Children![0].Type);
        Assert.NotNull(tree[0].Children![0].Children);
        Assert.Single(tree[0].Children![0].Children!);
        Assert.Equal("Click Me", tree[0].Children![0].Children![0].Text);

        listener.Stop();
    }

    [Fact]
    public void HttpResponseError_IncludesReasonAndDetails_WhenProvided()
    {
        var response = HttpResponse.Error(
            "Failed to get battery info",
            403,
            "missing_permission",
            new Dictionary<string, object?>
            {
                ["permission"] = "android.permission.BATTERY_STATS",
                ["platform"] = "Android"
            });

        Assert.Equal(403, response.StatusCode);
        Assert.Equal("Forbidden", response.StatusText);
        Assert.NotNull(response.Body);

        var json = JsonSerializer.Deserialize<JsonElement>(response.Body!);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("Failed to get battery info", json.GetProperty("error").GetString());
        Assert.Equal("missing_permission", json.GetProperty("reason").GetString());
        Assert.Equal("android.permission.BATTERY_STATS", json.GetProperty("details").GetProperty("permission").GetString());
        Assert.Equal("Android", json.GetProperty("details").GetProperty("platform").GetString());
    }

    [Fact]
    public async Task GetPlatformInfoAsync_ReturnsStructuredErrorBody_OnNonSuccessResponse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("GET /api/v1/device/battery", request);

            var body = """
            {
              "success": false,
              "error": "Failed to get battery info: You need to declare using the permission: `android.permission.BATTERY_STATS` in your AndroidManifest.xml",
              "reason": "missing_permission",
              "details": {
                "permission": "android.permission.BATTERY_STATS",
                "platform": "Android"
              }
            }
            """;
            var response = $"HTTP/1.1 403 Forbidden\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        });

        using var agentClient = CreateUncoordinatedClient();
        var result = await agentClient.GetPlatformInfoAsync("battery");

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal("missing_permission", result.GetProperty("reason").GetString());
        Assert.Equal("android.permission.BATTERY_STATS", result.GetProperty("details").GetProperty("permission").GetString());
        Assert.Equal("Android", result.GetProperty("details").GetProperty("platform").GetString());

        await acceptTask;
        listener.Stop();
    }

    [Fact]
    public async Task ListStorageRootsAsync_UsesV1StorageRootsRoute()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("GET /api/v1/storage/roots", request);

            var body = """{"roots":[{"id":"appData","displayName":"App data","kind":"appData","isWritable":true,"isReadOnly":false,"isPersistent":true,"isBackedUp":true,"mayBeClearedBySystem":false,"isUserVisible":false,"supportedOperations":["list","download","upload","delete"]}]}""";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        });

        using var agentClient = CreateUncoordinatedClient();
        var result = await agentClient.ListStorageRootsAsync();

        Assert.Equal("appData", result.GetProperty("roots")[0].GetProperty("id").GetString());

        await acceptTask;
        listener.Stop();
    }

    [Fact]
    public async Task ListFilesAsync_UsesV1StorageFilesRouteAndEscapesPath()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("GET /api/v1/storage/files?path=logs%2Ftoday", request);

            var body = """{"path":"logs/today","entries":[{"name":"app.log","type":"file","size":4,"lastModified":"2026-04-01T12:00:00Z"}]}""";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        });

        using var agentClient = CreateUncoordinatedClient();
        var result = await agentClient.ListFilesAsync("logs/today");

        Assert.Equal("logs/today", result.GetProperty("path").GetString());
        Assert.Equal("app.log", result.GetProperty("entries")[0].GetProperty("name").GetString());

        await acceptTask;
        listener.Stop();
    }

    [Fact]
    public async Task ListFilesAsync_WithRoot_AppendsRootQuery()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("GET /api/v1/storage/files?path=logs%2Ftoday&root=appData", request);

            var body = """{"root":"appData","path":"logs/today","entries":[]}""";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        });

        using var agentClient = CreateUncoordinatedClient();
        var result = await agentClient.ListFilesAsync("logs/today", "appData");

        Assert.Equal("appData", result.GetProperty("root").GetString());

        await acceptTask;
        listener.Stop();
    }

    [Fact]
    public async Task UploadFileAsync_SendsPutWithBase64Payload()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("PUT /api/v1/storage/files/logs%2Fapp.txt", request);
            Assert.Contains("\"contentBase64\":\"aGVsbG8=\"", request);

            var body = """{"success":true,"path":"logs/app.txt","size":5,"lastModified":"2026-04-01T12:00:00Z"}""";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        });

        using var agentClient = CreateUncoordinatedClient();
        var result = await agentClient.UploadFileAsync("logs/app.txt", "aGVsbG8=");

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal("logs/app.txt", result.GetProperty("path").GetString());

        await acceptTask;
        listener.Stop();
    }

    [Fact]
    public async Task DownloadFileAsync_WithRoot_AppendsRootQuery()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("GET /api/v1/storage/files/logs%2Fapp.txt?root=appData", request);

            var body = """{"root":"appData","path":"logs/app.txt","size":5,"lastModified":"2026-04-01T12:00:00Z","contentBase64":"aGVsbG8="}""";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        });

        using var agentClient = CreateUncoordinatedClient();
        var result = await agentClient.DownloadFileAsync("logs/app.txt", "appData");

        Assert.Equal("appData", result.GetProperty("root").GetString());

        await acceptTask;
        listener.Stop();
    }

    [Fact]
    public async Task DeleteFileAsync_ReturnsFalseOnNotFound()
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        var acceptTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            Assert.Contains("DELETE /api/v1/storage/files/missing.txt", request);

            var body = """{"success":false,"error":"File not found: missing.txt"}""";
            var response = $"HTTP/1.1 404 Not Found\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        });

        using var agentClient = CreateUncoordinatedClient();
        var result = await agentClient.DeleteFileAsync("missing.txt");

        Assert.False(result);

        await acceptTask;
        listener.Stop();
    }

    // ── Regression: multi-byte UTF-8 request bodies must not hang the parser ──────
    // HTTP Content-Length counts bytes. These tests drive the real AgentHttpServer end-to-end to
    // prove non-ASCII bodies parse promptly and round-trip byte-for-byte.

    [Fact]
    public async Task ReadRequest_MultibyteUtf8Body_RoundTripsWithoutHang()
    {
        const string json = "{\"value\":\"Monthly spend \u2713\"}"; // ✓ = U+2713, 3 bytes / 1 char
        var received = await EchoRoundTripAsync(json);
        Assert.Equal(json, received);
    }

    [Fact]
    public async Task ReadRequest_AsciiBody_RoundTrips()
    {
        const string json = "{\"value\":\"Monthly spend\"}";
        var received = await EchoRoundTripAsync(json);
        Assert.Equal(json, received);
    }

    [Fact]
    public async Task ReadRequest_LargeMultibyteBodyBeyondFirstRead_ReadsFullContentLength()
    {
        // ~40 KB of multi-byte content far exceeds the 8 KB initial read buffer and exercises the
        // byte-accurate "read the remaining Content-Length" loop.
        var json = "{\"value\":\"" + new string('\u00e9', 20000) + "\"}"; // é = U+00E9, 2 bytes each
        var received = await EchoRoundTripAsync(json);
        Assert.Equal(json, received);
    }

    /// <summary>
    /// Drives the REAL <see cref="AgentHttpServer"/>: PUTs the given JSON body to an echo route and
    /// returns the body the server parsed. If the server hangs on the body (the pre-fix bug), the
    /// bounded read cancels and this returns null, failing the caller's assertion.
    /// </summary>
    private async Task<string?> EchoRoundTripAsync(string json)
    {
        using var server = new AgentHttpServer(_port);
        server.MapPut("/api/v1/echo", req => Task.FromResult(new HttpResponse
        {
            StatusCode = 200,
            StatusText = "OK",
            ContentType = "application/json",
            Body = req.Body,
        }));
        server.Start();

        var bodyBytes = Encoding.UTF8.GetBytes(json);
        var head = "PUT /api/v1/echo HTTP/1.1\r\n"
                   + "Host: localhost\r\n"
                   + "Content-Type: application/json\r\n"
                   + $"Content-Length: {bodyBytes.Length}\r\n"
                   + "Connection: close\r\n\r\n";
        var requestBytes = Encoding.UTF8.GetBytes(head).Concat(bodyBytes).ToArray();

        // 8s ceiling: the fixed server responds in milliseconds; the buggy server blocks until this
        // cancels, turning the hang into a deterministic null (assertion failure) instead of a stall.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _port, cts.Token);
            using var stream = client.GetStream();
            await stream.WriteAsync(requestBytes, cts.Token);
            await stream.FlushAsync(cts.Token);

            using var ms = new MemoryStream();
            var buf = new byte[4096];
            while (true)
            {
                int r;
                try { r = await stream.ReadAsync(buf, cts.Token); }
                catch (OperationCanceledException) { return null; } // server hung → regression
                if (r == 0) break; // server sent Connection: close and closed the socket
                ms.Write(buf, 0, r);
            }

            var raw = Encoding.UTF8.GetString(ms.ToArray());
            var idx = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            return idx >= 0 ? raw[(idx + 4)..] : null;
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private Microsoft.Maui.DevFlow.Driver.AgentClient CreateUncoordinatedClient()
        => new("localhost", _port) { AutoAcquireMutationLease = false };

    public void Dispose() { }
}
