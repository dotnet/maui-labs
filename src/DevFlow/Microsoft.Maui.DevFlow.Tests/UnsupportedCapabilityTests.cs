using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Covers how the agent behaves when a backend does not implement a capability.
///
/// A plain .NET Android/iOS/Mac Catalyst/macOS app only answers the endpoints its backend
/// implements — theme, storage, sensors and background jobs need the optional Essentials add-on.
/// The bare <see cref="DevFlowAgentService"/> stands in for "a backend that implements nothing",
/// which is exactly the contract every partial backend degrades toward.
/// </summary>
public class UnsupportedCapabilityTests
{
    [Theory]
    [InlineData("/api/v1/ui/tree", "ui.tree")]
    [InlineData("/api/v1/ui/hit-test?x=1&y=1", "ui.tree")]
    [InlineData("/api/v1/ui/screenshot", "ui.screenshot")]
    [InlineData("/api/v1/device/app/theme", "app.theme")]
    public async Task UnsupportedEndpoints_Return501NotSupportedEnvelope(string path, string capability)
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        service.StartServerOnly(dispatcher: null);

        using var http = new HttpClient();
        using var response = await WaitForServerAsync(http, port, path);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("not_supported", json.RootElement.GetProperty("error").GetString());
        Assert.Equal(capability, json.RootElement.GetProperty("capability").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task Driver_TranslatesNotSupportedEnvelope_IntoTypedException()
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        service.StartServerOnly(dispatcher: null);

        using (var http = new HttpClient())
            await WaitForServerAsync(http, port, "/api/v1/agent/status");

        var error = await Assert.ThrowsAsync<NotSupportedByAgentException>(() => client.GetTreeAsync());

        Assert.Equal("ui.tree", error.Capability);
        Assert.False(string.IsNullOrWhiteSpace(error.Reason));
        Assert.Contains("ui.tree", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_ReportsTheBackendFramework()
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        service.StartServerOnly(dispatcher: null);

        using var http = new HttpClient();
        using var response = await WaitForServerAsync(http, port, "/api/v1/agent/status");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var agent = json.RootElement.GetProperty("agent");

        // The neutral base is not MAUI, so it must not claim to be.
        Assert.Equal("native", agent.GetProperty("frameworkId").GetString());
        Assert.NotEqual("maui-controls", agent.GetProperty("uiFramework").GetString());
    }

    [Fact]
    public async Task Capabilities_ListUnsupportedGroupsWithAReason()
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        service.StartServerOnly(dispatcher: null);

        using (var http = new HttpClient())
            await WaitForServerAsync(http, port, "/api/v1/agent/status");

        var capabilities = (await client.GetCapabilitiesAsync()).GetProperty("capabilities");
        var ui = capabilities.GetProperty("ui.tree");

        // Unsupported groups stay listed so clients can discover them instead of guessing.
        Assert.False(ui.GetProperty("supported").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(ui.GetProperty("reason").GetString()));
    }

    [Fact]
    public void BrokerRegistration_IsStampedWithTheBackendFramework()
    {
        using var service = new DevFlowAgentService(new AgentOptions { Port = GetFreePort() });
        var registration = new BrokerRegistration("proj.csproj", "net10.0-ios", "iOS", "SampleApp");

        service.SetBrokerRegistration(registration);

        Assert.Equal("native", registration.Framework);
        Assert.False(string.IsNullOrWhiteSpace(registration.UiFramework));
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
