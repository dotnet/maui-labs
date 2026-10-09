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
    public async Task ContextSelection_AllRoutes_RejectAliasesWithoutDispatchAndUseCanonicalTarget(string method, string route, string? body)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        var calls = 0;
        var wrongCalls = 0;
        var first = service.RegisterCdpWebView(_ => { wrongCalls++; return Task.FromResult("{}"); },
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
        }, () => true, "duplicate", "element-second", hostKind: "custom-hybrid");
        Assert.NotEqual(first, second);
        service.StartServerOnly(null);
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        using var lease = await http.PostAsync("/api/v1/agent/lease",
            new StringContent("""{"action":"claim","leaseId":"contract-test","holderKind":"test"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, lease.StatusCode);
        http.DefaultRequestHeaders.Add("X-DevFlow-Lease", "contract-test");

        foreach (var selection in new[] { "contextId=", "contextId=%20", "contextId=%09", $"contextId={second}",
                     "contextId=duplicate", "contextId=element-second", $"contextId=webview-0{second}",
                     $"contextId=WebView-{second}", "contextId=webview--1", "contextId=webview-999",
                     $"webview=webview-{second}", "webview=", $"webview=webview-{first}&contextId=webview-{second}" })
        {
            using var rejected = await SendAsync(selection);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        Assert.Equal(0, calls);
        Assert.Equal(0, wrongCalls);
        using var selected = await SendAsync($"contextId=webview-{second}");
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        Assert.Equal(1, calls);

        using var defaultSelected = await SendAsync("");
        Assert.Equal(HttpStatusCode.OK, defaultSelected.StatusCode);
        Assert.Equal(2, calls);
        if (route == "evaluate")
        {
            var rawBody = JsonNode.Parse(body!)!.AsObject();
            rawBody["contextId"] = $"webview-{first}";
            rawBody["params"]!["contextId"] = $"webview-{first}";
            body = rawBody.ToJsonString();
            using var rawDefault = await SendAsync("");
            Assert.Equal(HttpStatusCode.OK, rawDefault.StatusCode);
            Assert.Equal(3, calls);
            using var rawQuery = await SendAsync($"contextId=webview-{second}");
            Assert.Equal(HttpStatusCode.OK, rawQuery.StatusCode);
            Assert.Equal(4, calls);
        }
        if (body is not null && route != "evaluate")
        {
            var typedBody = JsonNode.Parse(body)!.AsObject();
            foreach (var invalid in new[] { "", " ", "\t", second.ToString(), "duplicate", "element-second", $"webview-0{second}" })
            {
                typedBody["contextId"] = invalid;
                body = typedBody.ToJsonString();
                using var rejected = await SendAsync("");
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            }
            Assert.Equal(2, calls);
            typedBody["contextId"] = null;
            body = typedBody.ToJsonString();
            using var nullSelected = await SendAsync("");
            Assert.Equal(HttpStatusCode.OK, nullSelected.StatusCode);
            Assert.Equal(3, calls);
            typedBody["contextId"] = $"webview-{second}";
            body = typedBody.ToJsonString();
            using var bodySelected = await SendAsync("");
            Assert.Equal(HttpStatusCode.OK, bodySelected.StatusCode);
            Assert.Equal(4, calls);
            typedBody["contextId"] = $"webview-{first}";
            body = typedBody.ToJsonString();
            using var querySelected = await SendAsync($"contextId=webview-{second}");
            Assert.Equal(HttpStatusCode.OK, querySelected.StatusCode);
            Assert.Equal(5, calls);
            using var blankQuery = await SendAsync("contextId=");
            Assert.Equal(HttpStatusCode.BadRequest, blankQuery.StatusCode);
            Assert.Equal(5, calls);
        }
        Assert.Equal(0, wrongCalls);
        using var contexts = JsonDocument.Parse(await http.GetStringAsync("/api/v1/webview/contexts"));
        var entries = contexts.RootElement.GetProperty("webviews").EnumerateArray().ToArray();
        Assert.Equal(2, entries.Select(entry => entry.GetProperty("id").GetString()).Distinct().Count());
        foreach (var context in entries)
        {
            Assert.Equal($"webview-{context.GetProperty("index").GetInt32()}", context.GetProperty("id").GetString());
            Assert.True(context.GetProperty("ready").GetBoolean());
            Assert.False(context.TryGetProperty("isReady", out _));
        }
        Assert.Equal("custom-hybrid", entries[1].GetProperty("hostKind").GetString());

        async Task<HttpResponseMessage> SendAsync(string selection)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/v1/webview/{route}?{selection}");
            if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return await http.SendAsync(request);
        }
    }

    [Fact]
    public void RegistryMetadata_SerializesCanonicalIdAndOnlyReady()
    {
        var info = new CdpWebViewInfo { Index = 12, ReadyCheck = () => true };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(info));
        Assert.Equal("webview-12", document.RootElement.GetProperty("id").GetString());
        Assert.True(document.RootElement.GetProperty("ready").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("isReady", out _));
    }
}
