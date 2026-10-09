using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class WebViewContractTests
{
    [Theory]
    [InlineData("GET", "source", null)]
    [InlineData("GET", "dom", null)]
    [InlineData("GET", "screenshot", null)]
    [InlineData("POST", "evaluate", """{"method":"Runtime.evaluate","params":{"expression":"1 + 1"}}""")]
    [InlineData("POST", "dom/query", """{"selector":"button"}""")]
    [InlineData("POST", "navigate", """{"url":"/next"}""")]
    [InlineData("POST", "input/click", """{"selector":"button"}""")]
    [InlineData("POST", "input/fill", """{"selector":"input","text":"filled"}""")]
    [InlineData("POST", "input/text", """{"text":"typed"}""")]
    public async Task ContextSelection_AllRoutes_UsesCanonicalIdsAndRejectsAliases(string method, string route, string? body)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        var calls = 0;
        var first = service.RegisterCdpWebView(_ => throw new InvalidOperationException("Wrong context selected"),
            () => true, "duplicate", "element-first");
        var second = service.RegisterCdpWebView(command =>
        {
            calls++;
            using var request = JsonDocument.Parse(command);
            var cdpMethod = request.RootElement.GetProperty("method").GetString();
            if (cdpMethod == "Page.captureScreenshot")
                return Task.FromResult("""{"result":{"data":"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="}}""");
            if (cdpMethod is "Page.navigate" or "Input.insertText")
                return Task.FromResult("""{"result":{}}""");
            var expression = request.RootElement.GetProperty("params").GetProperty("expression").GetString()!;
            var value = expression == "document.documentElement.outerHTML"
                ? "<html>selected</html>"
                : expression.Contains(".map(", StringComparison.Ordinal) ? "[]" : """{"success":true}""";
            return Task.FromResult(JsonSerializer.Serialize(new { result = new { result = new { value } } }));
        }, () => true, "duplicate", "element-second", hostKind: "hybrid");
        Assert.NotEqual(first, second);
        service.StartServerOnly(null);
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        using var lease = await http.PostAsync("/api/v1/agent/lease",
            new StringContent("""{"action":"claim","leaseId":"contract-test","holderKind":"test"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, lease.StatusCode);
        http.DefaultRequestHeaders.Add("X-DevFlow-Lease", "contract-test");

        foreach (var selection in new[] { "contextId=", "contextId=%20", $"contextId={second}", "contextId=duplicate", "contextId=element-second",
                     $"contextId=webview-0{second}", $"contextId=WebView-{second}", $"webview=webview-{second}",
                     $"webview=webview-{first}&contextId=webview-{second}" })
        {
            using var rejected = await SendAsync(selection);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        Assert.Equal(0, calls);
        using var selected = await SendAsync($"contextId=webview-{second}");
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        Assert.Equal(1, calls);
        if (body is not null && route != "evaluate")
        {
            var typedBody = JsonNode.Parse(body)!.AsObject();
            typedBody["contextId"] = $"webview-{second}";
            body = typedBody.ToJsonString();
            using var bodySelected = await SendAsync("");
            Assert.Equal(HttpStatusCode.OK, bodySelected.StatusCode);
            Assert.Equal(2, calls);

            typedBody["contextId"] = $"webview-{first}";
            body = typedBody.ToJsonString();
            using var querySelected = await SendAsync($"contextId=webview-{second}");
            Assert.Equal(HttpStatusCode.OK, querySelected.StatusCode);
            Assert.Equal(3, calls);
        }
        using var contexts = JsonDocument.Parse(await http.GetStringAsync("/api/v1/webview/contexts"));
        foreach (var context in contexts.RootElement.GetProperty("webviews").EnumerateArray())
        {
            Assert.Equal($"webview-{context.GetProperty("index").GetInt32()}", context.GetProperty("id").GetString());
            Assert.True(context.GetProperty("ready").GetBoolean());
            Assert.False(context.TryGetProperty("isReady", out _));
        }

        async Task<HttpResponseMessage> SendAsync(string selection)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/v1/webview/{route}?{selection}");
            if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return await http.SendAsync(request);
        }
    }

    [Fact]
    public void LayoutScope_OnlyCanonicalOption_IsSerializedAndControlsDomInclusion()
    {
        var scope = new LayoutInspectionScope { IncludeWebViewElements = false };
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(scope));
        Assert.False(serialized.RootElement.GetProperty("includeWebViewElements").GetBoolean());
        Assert.False(serialized.RootElement.TryGetProperty("includeBlazorElements", out _));
        Assert.True(JsonSerializer.Deserialize<LayoutInspectionScope>("{}")!.IncludeWebViewElements);
    }
}
