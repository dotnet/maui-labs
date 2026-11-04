using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class InspectorServerCapabilitiesTests
{
    [Fact]
    public async Task CapabilitiesEndpoint_ExposesPolicyInputsWithoutExtensionMetadata()
    {
        await using var agent = new MockAgentServer();
        await agent.StartAsync();

        using var inspector = new InspectorServer(GetFreePort(), "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(
                $"http://localhost:{inspector.Port}/api/capabilities");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.Equal("maui", root.GetProperty("agent").GetProperty("framework").GetString());
            Assert.True(root.GetProperty("capabilities").TryGetProperty("ui.actions", out _));
            Assert.False(root.TryGetProperty("status", out _));
            Assert.False(root.TryGetProperty("extensions", out _));
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task CapabilitiesEndpoint_WhenDiscoveryFails_ReturnsServiceUnavailable()
    {
        await using var agent = new MockAgentServer(capabilitiesErrorResponseCount: 1);
        await agent.StartAsync();

        using var inspector = new InspectorServer(GetFreePort(), "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(
                $"http://localhost:{inspector.Port}/api/capabilities");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
