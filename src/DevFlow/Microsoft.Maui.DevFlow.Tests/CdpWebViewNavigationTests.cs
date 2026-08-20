using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Covers the registered CDP WebView metadata that <c>GET /api/v1/webview/contexts</c> reports.
/// The handlers resolve WebViews through a defensive snapshot copy, so anything that mutates
/// WebView state has to write back to the registry rather than only to the copy it was handed.
/// </summary>
public class CdpWebViewNavigationTests
{
    [Fact]
    public async Task Navigate_UpdatesTheRegisteredUrl_SoContextsReportThePostNavigationUrl()
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        service.StartServerOnly(dispatcher: null);

        service.RegisterCdpWebView(
            commandHandler: _ => Task.FromResult("""{"id":99995,"result":{"frameId":"frame-1"}}"""),
            readyCheck: () => true,
            automationId: "TestWebView",
            elementId: "element-1",
            url: "https://example.test/before");

        using var http = new HttpClient();
        using (var ready = await WaitForServerAsync(http, port, "/api/v1/agent/status"))
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        using var body = new StringContent(
            """{"url":"https://example.test/after"}""",
            Encoding.UTF8,
            "application/json");
        using var navigate = await http.PostAsync(
            $"http://localhost:{port}/api/v1/webview/navigate",
            body);

        Assert.Equal(HttpStatusCode.OK, navigate.StatusCode);

        using var contexts = await http.GetAsync($"http://localhost:{port}/api/v1/webview/contexts");
        using var json = JsonDocument.Parse(await contexts.Content.ReadAsStringAsync());

        var urls = json.RootElement
            .GetProperty("webviews")
            .EnumerateArray()
            .Select(webView => webView.GetProperty("url").GetString())
            .ToList();

        Assert.Contains("https://example.test/after", urls);
        Assert.DoesNotContain("https://example.test/before", urls);
    }

    private static async Task<HttpResponseMessage> WaitForServerAsync(HttpClient http, int port, string path)
    {
        HttpResponseMessage? last = null;

        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                last?.Dispose();
                last = await http.GetAsync($"http://localhost:{port}{path}");
                return last;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(50);
            }
        }

        return last ?? throw new InvalidOperationException($"Agent never answered {path} on port {port}.");
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
