using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class InspectorServerCaptureMetadataTests
{
    [Theory]
    [InlineData(
        "/api/tap",
        "/api/v1/ui/actions/tap",
        """{"elementId":"element-42","captureEpoch":42,"registryGeneration":7}""")]
    [InlineData(
        "/api/fill",
        "/api/v1/ui/actions/fill",
        """{"elementId":"element-42","text":"updated","captureEpoch":42,"registryGeneration":7}""")]
    [InlineData(
        "/api/key",
        "/api/v1/ui/actions/key",
        """{"elementId":"element-42","key":"Enter","captureEpoch":42,"registryGeneration":7}""")]
    public async Task ElementIdAction_ForwardsOriginatingCaptureMetadata(
        string inspectorPath,
        string agentPath,
        string requestJson)
    {
        await using var agent = new MockAgentServer();
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var request = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}{inspectorPath}",
                request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var forwarded = Assert.Single(
                agent.RecordedRequests,
                recorded => recorded.Path == agentPath);
            using var forwardedJson = JsonDocument.Parse(forwarded.Body!);
            Assert.Equal(42, forwardedJson.RootElement.GetProperty("captureEpoch").GetInt64());
            Assert.Equal(7, forwardedJson.RootElement.GetProperty("registryGeneration").GetInt64());
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Theory]
    [InlineData("/api/tap", """{"elementId":"element-42"}""")]
    [InlineData("/api/fill", """{"elementId":"element-42","text":"updated"}""")]
    [InlineData("/api/key", """{"elementId":"element-42","key":"Enter"}""")]
    public async Task ElementIdAction_WithoutCaptureEpoch_IsRejected(
        string inspectorPath,
        string requestJson)
    {
        await using var agent = new MockAgentServer();
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var request = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}{inspectorPath}",
                request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.DoesNotContain(
                agent.RecordedRequests,
                recorded => recorded.Path.StartsWith("/api/v1/ui/actions/", StringComparison.Ordinal));
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Theory]
    [InlineData("/api/tap", "/api/v1/ui/actions/tap", """{"elementId":"element-42"}""")]
    [InlineData("/api/fill", "/api/v1/ui/actions/fill", """{"elementId":"element-42","text":"updated"}""")]
    [InlineData("/api/key", "/api/v1/ui/actions/key", """{"elementId":"element-42","key":"Enter"}""")]
    public async Task ElementIdAction_WithoutCaptureEpoch_ForwardsToLegacyAgent(
        string inspectorPath,
        string agentPath,
        string requestJson)
    {
        await using var agent = new MockAgentServer(supportsCaptureEpoch: false);
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var request = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}{inspectorPath}",
                request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var forwarded = Assert.Single(
                agent.RecordedRequests,
                recorded => recorded.Path == agentPath);
            using var forwardedJson = JsonDocument.Parse(forwarded.Body!);
            Assert.False(forwardedJson.RootElement.TryGetProperty("captureEpoch", out _));
            Assert.False(forwardedJson.RootElement.TryGetProperty("registryGeneration", out _));
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task CoordinateTap_WhenFirstCandidateRejects_RehitTestsBeforeParentFallback()
    {
        await using var agent = new MockAgentServer(
            failFirstHitTestCandidate: true,
            changeHitTestCandidates: true);
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var request = new StringContent("""{"x":10,"y":20}""", Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/tap",
                request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var hitTests = agent.RecordedRequests
                .Where(recorded => recorded.Path == "/api/v1/ui/hit-test")
                .ToArray();
            Assert.Equal(2, hitTests.Length);

            var taps = agent.RecordedRequests
                .Where(recorded => recorded.Path == "/api/v1/ui/actions/tap")
                .ToArray();
            Assert.Equal(2, taps.Length);
            using var firstTap = JsonDocument.Parse(taps[0].Body!);
            using var secondTap = JsonDocument.Parse(taps[1].Body!);
            Assert.Equal("hit-child", firstTap.RootElement.GetProperty("elementId").GetString());
            Assert.Equal("hit-parent-refreshed", secondTap.RootElement.GetProperty("elementId").GetString());
            Assert.NotEqual(
                firstTap.RootElement.GetProperty("captureEpoch").GetInt64(),
                secondTap.RootElement.GetProperty("captureEpoch").GetInt64());
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task CoordinateTap_WhenCaptureIsStale_RetriesSameCandidateWithFreshEpoch()
    {
        await using var agent = new MockAgentServer(staleFirstHitTestCandidate: true);
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var request = new StringContent("""{"x":10,"y":20}""", Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/tap",
                request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var taps = agent.RecordedRequests
                .Where(recorded => recorded.Path == "/api/v1/ui/actions/tap")
                .ToArray();
            Assert.Equal(2, taps.Length);
            using var firstTap = JsonDocument.Parse(taps[0].Body!);
            using var secondTap = JsonDocument.Parse(taps[1].Body!);
            Assert.Equal("hit-child", firstTap.RootElement.GetProperty("elementId").GetString());
            Assert.Equal("hit-child", secondTap.RootElement.GetProperty("elementId").GetString());
            Assert.NotEqual(
                firstTap.RootElement.GetProperty("captureEpoch").GetInt64(),
                secondTap.RootElement.GetProperty("captureEpoch").GetInt64());
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task CoordinateTap_WhenNativeProbeIsTemporarilyBusy_RetriesHitTest()
    {
        await using var agent = new MockAgentServer(nativeProbeBusyHitTestCount: 1);
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var request = new StringContent("""{"x":10,"y":20}""", Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/tap",
                request);

            response.EnsureSuccessStatusCode();
            Assert.Equal(
                2,
                agent.RecordedRequests.Count(request => request.Path == "/api/v1/ui/hit-test"));
            Assert.Single(
                agent.RecordedRequests,
                request => request.Path == "/api/v1/ui/actions/tap");
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task CoordinateTap_WhenNativeProbeRemainsBusy_PropagatesConflict()
    {
        await using var agent = new MockAgentServer(nativeProbeBusyHitTestCount: 100);
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var request = new StringContent("""{"x":10,"y":20}""", Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/tap",
                request);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.InRange(
                agent.RecordedRequests.Count(request => request.Path == "/api/v1/ui/hit-test"),
                4,
                20);
            Assert.DoesNotContain(
                agent.RecordedRequests,
                request => request.Path == "/api/v1/ui/actions/tap");
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task CoordinateTap_WhenAgentReturnsServerError_DoesNotRepeatMutation()
    {
        await using var agent = new MockAgentServer(failTapWithServerError: true);
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var request = new StringContent("""{"x":10,"y":20}""", Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/tap",
                request);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Single(
                agent.RecordedRequests,
                recorded => recorded.Path == "/api/v1/ui/actions/tap");
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Theory]
    [InlineData("/api/tap", """{"elementId":"element-42","captureEpoch":42,"registryGeneration":7}""")]
    [InlineData("/api/key", """{"elementId":"element-42","key":"Enter","captureEpoch":42,"registryGeneration":7}""")]
    public async Task ElementAction_WhenAgentRejectsStaleCapture_PropagatesConflict(
        string path,
        string requestBody)
    {
        await using var agent = new MockAgentServer(
            staleFirstElementTap: path == "/api/tap",
            staleFirstKey: path == "/api/key");
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var request = new StringContent(requestBody, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}{path}",
                request);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(
                "stale-capture-epoch",
                responseJson.RootElement.GetProperty("reason").GetString());
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task CoordinateScroll_PrefersScrollableHitTestCandidate()
    {
        await using var agent = new MockAgentServer(useScrollableHitTestCandidate: true);
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var request = new StringContent(
                """{"x":10,"y":20,"deltaX":0,"deltaY":100}""",
                Encoding.UTF8,
                "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/scroll",
                request);

            response.EnsureSuccessStatusCode();
            var forwarded = Assert.Single(
                agent.RecordedRequests,
                recorded => recorded.Path == "/api/v1/ui/actions/scroll");
            using var forwardedJson = JsonDocument.Parse(forwarded.Body!);
            Assert.Equal(
                "hit-scroll",
                forwardedJson.RootElement.GetProperty("elementId").GetString());
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task Fill_WhenAgentRejectsStaleCapture_PropagatesConflict()
    {
        await using var agent = new MockAgentServer(staleFirstFill: true);
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var request = new StringContent(
                """{"elementId":"element-42","text":"updated","captureEpoch":42,"registryGeneration":7}""",
                Encoding.UTF8,
                "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/fill",
                request);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(
                "stale-capture-epoch",
                responseJson.RootElement.GetProperty("reason").GetString());
            Assert.True(responseJson.RootElement.GetProperty("retryable").GetBoolean());
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task StateScreenshotUrl_AfterActionInvalidatesCaptureCache_ServesCapturedSnapshot()
    {
        await using var agent = new MockAgentServer(failScreenshotsAfterFirst: true);
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var stateResponse = await client.GetAsync(
                $"http://localhost:{inspectorPort}/api/state");
            stateResponse.EnsureSuccessStatusCode();
            using var state = JsonDocument.Parse(await stateResponse.Content.ReadAsStringAsync());
            var screenshotUrl = state.RootElement.GetProperty("screenshotUrl").GetString();
            Assert.NotNull(screenshotUrl);

            using var backResponse = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/back",
                content: null);
            backResponse.EnsureSuccessStatusCode();

            using var screenshotResponse = await client.GetAsync(
                $"http://localhost:{inspectorPort}/{screenshotUrl}");
            screenshotResponse.EnsureSuccessStatusCode();
            Assert.Equal("image/png", screenshotResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal(
                MockAgentResponses.ScreenshotPng,
                await screenshotResponse.Content.ReadAsByteArrayAsync());
            Assert.Single(
                agent.RecordedRequests,
                request => request.Path == "/api/v1/ui/screenshot");
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task State_WithDetachedNativeRoot_RequestsFullscreenScreenshot()
    {
        await using var agent = new MockAgentServer(includeDetachedNativeRoot: true);
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(
                $"http://localhost:{inspectorPort}/api/state");
            response.EnsureSuccessStatusCode();

            var screenshotRequest = Assert.Single(
                agent.RecordedRequests,
                request => request.Path == "/api/v1/ui/screenshot");
            Assert.Contains("fullscreen=true", screenshotRequest.QueryString);
            Assert.Contains("captureEpoch=42", screenshotRequest.QueryString);
            Assert.Contains("registryGeneration=7", screenshotRequest.QueryString);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task Screenshot_WithUnknownSnapshotId_ReturnsNotFoundWithoutRecapture()
    {
        await using var agent = new MockAgentServer();
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(
                $"http://localhost:{inspectorPort}/screenshot.png?t=999");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain(
                agent.RecordedRequests,
                request => request.Path == "/api/v1/ui/screenshot");
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task Root_UsesExactScreenshotSnapshotUrl()
    {
        await using var agent = new MockAgentServer();
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            var html = await client.GetStringAsync($"http://localhost:{inspectorPort}/");
            var urlMatch = System.Text.RegularExpressions.Regex.Match(
                html,
                "id=\"screenshot\" src=\"(screenshot\\.png\\?t=\\d+)\"");
            Assert.True(urlMatch.Success);

            using var screenshot = await client.GetAsync(
                $"http://localhost:{inspectorPort}/{urlMatch.Groups[1].Value}");
            screenshot.EnsureSuccessStatusCode();
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task State_WhenCurrentScreenshotCaptureFails_ReturnsServiceUnavailable()
    {
        await using var agent = new MockAgentServer(failScreenshotsAfterFirst: true);
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var initialState = await client.GetAsync(
                $"http://localhost:{inspectorPort}/api/state");
            initialState.EnsureSuccessStatusCode();

            using var backResponse = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/back",
                content: null);
            backResponse.EnsureSuccessStatusCode();

            using var failedState = await client.GetAsync(
                $"http://localhost:{inspectorPort}/api/state");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failedState.StatusCode);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/api/state")]
    public async Task TreeEndpoint_WhenAgentReturnsNoTree_ReturnsServiceUnavailable(string path)
    {
        await using var agent = new MockAgentServer(returnEmptyTree: true);
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(
                $"http://localhost:{inspectorPort}{path}");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(
                3,
                agent.RecordedRequests.Count(request => request.Path == "/api/v1/ui/tree"));
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
