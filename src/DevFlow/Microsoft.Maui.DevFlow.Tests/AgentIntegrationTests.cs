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

        using var agentClient = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", _port);
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

        using var agentClient = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", _port);
        var results = await agentClient.QueryAsync(type: "Button", text: "Submit");

        Assert.Single(results);
        Assert.Equal("btn1", results[0].Id);
        Assert.Equal("Button", results[0].Type);
        Assert.Equal("Submit", results[0].Text);

        listener.Stop();
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

        using var agentClient = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", _port);
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

        using var agentClient = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", _port);
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

        using var agentClient = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", _port);
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

        using var agentClient = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", _port);
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

        using var agentClient = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", _port);
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

        using var agentClient = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", _port);
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
              "ui": { "supported": true, "features": ["tree", "tap", "batch"] },
              "webview": { "supported": true, "features": ["contexts", "evaluate"] },
              "network": { "supported": true, "features": ["list", "clear"] }
            }
            """;
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        });

        using var agentClient = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", _port);
        var capabilities = await agentClient.GetCapabilitiesAsync();

        Assert.True(capabilities.GetProperty("ui").GetProperty("supported").GetBoolean());
        Assert.Contains("batch", capabilities.GetProperty("ui").GetProperty("features").EnumerateArray().Select(x => x.GetString()));

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

        using var agentClient = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", _port);
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

        using var agentClient = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", _port);
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

        using var agentClient = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", _port);
        var result = await agentClient.GetPlatformInfoAsync("battery");

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal("missing_permission", result.GetProperty("reason").GetString());
        Assert.Equal("android.permission.BATTERY_STATS", result.GetProperty("details").GetProperty("permission").GetString());
        Assert.Equal("Android", result.GetProperty("details").GetProperty("platform").GetString());

        await acceptTask;
        listener.Stop();
    }

    public void Dispose() { }
}
