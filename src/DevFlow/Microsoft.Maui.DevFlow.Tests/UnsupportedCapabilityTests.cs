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
    [InlineData("/api/v1/ui/elements/e1/properties", "ui.actions")]
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

    // The *ResultAsync APIs (ScreenshotResultAsync, HitTestResultAsync, and the ActionResult
    // family built on SendActionResultAsync) document reporting every failure through their
    // returned result type instead of throwing. The shared retry path in AgentClient raises
    // NotSupportedByAgentException for the uniform 501 envelope before these methods get a
    // chance to inspect the response, so each one must translate it back into a failure result
    // rather than let it leak out and break that documented contract.

    [Fact]
    public async Task ScreenshotResultAsync_ReportsNotSupported_AsFailureResult_WithoutThrowing()
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        service.StartServerOnly(dispatcher: null);

        using (var http = new HttpClient())
            await WaitForServerAsync(http, port, "/api/v1/agent/status");

        var result = await client.ScreenshotResultAsync();

        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Equal("not_supported", result.Reason);
        Assert.False(result.Retryable);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Contains("ui.screenshot", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HitTestResultAsync_ReportsNotSupported_AsFailureResult_WithoutThrowing()
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        service.StartServerOnly(dispatcher: null);

        using (var http = new HttpClient())
            await WaitForServerAsync(http, port, "/api/v1/agent/status");

        var result = await client.HitTestResultAsync(1, 1);

        Assert.False(result.Success);
        Assert.Equal((int)HttpStatusCode.NotImplemented, result.StatusCode);
        Assert.Equal("not_supported", result.Reason);
        Assert.False(result.Retryable);
        Assert.False(result.TransportFailure);
        Assert.Contains("ui.tree", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TapResultAsync_ReportsNotSupported_AsFailureResult_WithoutThrowing()
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        service.StartServerOnly(dispatcher: null);

        using (var http = new HttpClient())
            await WaitForServerAsync(http, port, "/api/v1/agent/status");

        var result = await client.TapResultAsync("some-element", captureEpoch: null, registryGeneration: null);

        Assert.False(result.Success);
        Assert.Equal((int)HttpStatusCode.NotImplemented, result.StatusCode);
        Assert.Equal("not_supported", result.Reason);
        Assert.False(result.Retryable);
        Assert.False(result.TransportFailure);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Contains("ui.actions", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TapAsync_StillThrowsNotSupportedByAgentException_PreservingConvenienceApiContract()
    {
        // Unlike the *ResultAsync APIs above, the plain bool-returning convenience wrappers
        // (TapAsync, FillAsync, etc.) never promised a non-throwing contract — the same
        // capability rejection reaches them as a NotSupportedByAgentException, same as
        // GetTreeAsync above. This proves the fix is scoped to the *ResultAsync family and does
        // not change the established behavior of the convenience APIs.
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        service.StartServerOnly(dispatcher: null);

        using (var http = new HttpClient())
            await WaitForServerAsync(http, port, "/api/v1/agent/status");

        var error = await Assert.ThrowsAsync<NotSupportedByAgentException>(
            () => client.TapAsync("some-element", captureEpoch: null, registryGeneration: null));

        Assert.Equal("ui.actions", error.Capability);
    }

    [Fact]
    public async Task JobRun_WithoutPlatformResult_ReturnsUniformNotSupportedEnvelope()
    {
        var port = GetFreePort();
        // Lease enforcement is exercised elsewhere; this test drives the endpoint with a raw
        // HttpClient (no lease identity) to assert the 501 not_supported envelope shape.
        using var service = new JobHostWithoutRunSupport(new AgentOptions
        {
            Port = port,
            RequireMutationLease = false
        });
        service.StartServerOnly(dispatcher: null);

        using var http = new HttpClient();
        using var statusResponse = await WaitForServerAsync(http, port, "/api/v1/agent/status");

        using var content = new StringContent(
            "{}",
            System.Text.Encoding.UTF8,
            "application/json");
        using var response = await http.PostAsync(
            $"http://localhost:{port}/api/v1/device/jobs/example/run",
            content);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("not_supported", json.RootElement.GetProperty("error").GetString());
        Assert.Equal("device.jobs", json.RootElement.GetProperty("capability").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("reason").GetString()));
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
    public async Task NonMauiBackend_DoesNotAdvertiseMauiCaptureSemantics()
    {
        var port = GetFreePort();
        using var service = new SupportedNonMauiAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        service.StartServerOnly(dispatcher: null);

        using (var http = new HttpClient())
            await WaitForServerAsync(http, port, "/api/v1/agent/status");

        var capabilities = (await client.GetCapabilitiesAsync()).GetProperty("capabilities");
        var tree = capabilities.GetProperty("ui.tree");
        var actions = capabilities.GetProperty("ui.actions");

        Assert.Equal(1, tree.GetProperty("version").GetInt32());
        Assert.DoesNotContain(
            tree.GetProperty("features").EnumerateArray(),
            feature => feature.GetString() == "capture-epoch");
        if (capabilities.TryGetProperty("ui.hit-test", out var hitTest))
            Assert.Equal(1, hitTest.GetProperty("version").GetInt32());
        Assert.Equal(1, actions.GetProperty("version").GetInt32());
        Assert.DoesNotContain(
            actions.GetProperty("features").EnumerateArray(),
            feature => feature.GetString() == "stale-capture-rejection");
    }

    [Fact]
    public async Task MauiBackend_AdvertisesMauiCaptureSemantics()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        ((DevFlowAgentService)service).StartServerOnly(dispatcher: null);

        using (var http = new HttpClient())
            await WaitForServerAsync(http, port, "/api/v1/agent/status");

        var capabilities = (await client.GetCapabilitiesAsync()).GetProperty("capabilities");
        var tree = capabilities.GetProperty("ui.tree");
        var hitTest = capabilities.GetProperty("ui.hit-test");
        var actions = capabilities.GetProperty("ui.actions");

        Assert.Equal(2, tree.GetProperty("version").GetInt32());
        Assert.Contains(
            tree.GetProperty("features").EnumerateArray(),
            feature => feature.GetString() == "native-owner");
        Assert.Equal(2, hitTest.GetProperty("version").GetInt32());
        Assert.Equal(2, actions.GetProperty("version").GetInt32());
        Assert.Contains(
            actions.GetProperty("features").EnumerateArray(),
            feature => feature.GetString() == "stale-capture-rejection");
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

    private sealed class SupportedNonMauiAgentService(AgentOptions options) : DevFlowAgentService(options)
    {
        protected override bool IsUiSupported => true;
        protected override bool IsScreenshotSupported => true;
    }

    private sealed class JobHostWithoutRunSupport(AgentOptions options) : DevFlowAgentService(options)
    {
        protected override bool IsJobsSupported => true;
        protected override bool IsJobRunSupported => true;
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
