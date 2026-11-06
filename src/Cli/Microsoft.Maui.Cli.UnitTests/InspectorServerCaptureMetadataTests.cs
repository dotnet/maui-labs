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
    private const string OffsetVisualTree = """
        [
          {
            "id": "window-root",
            "type": "Window",
            "captureEpoch": 42,
            "registryGeneration": 7,
            "windowId": 0,
            "isVisible": true,
            "isEnabled": true,
            "windowBounds": { "x": 0, "y": 0, "width": 100, "height": 100 },
            "children": [
              {
                "id": "page-root",
                "type": "ContentPage",
                "captureEpoch": 42,
                "registryGeneration": 7,
                "windowId": 0,
                "isVisible": true,
                "isEnabled": true,
                "windowBounds": { "x": 10, "y": 20, "width": 100, "height": 100 },
                "children": []
              }
            ]
          }
        ]
        """;

    private const string RevisionlessOffsetVisualTree = """
        [
          {
            "id": "window-root",
            "type": "Window",
            "isVisible": true,
            "isEnabled": true,
            "windowBounds": { "x": 0, "y": 0, "width": 100, "height": 100 },
            "children": [
              {
                "id": "page-root",
                "type": "ContentPage",
                "isVisible": true,
                "isEnabled": true,
                "windowBounds": { "x": 10, "y": 20, "width": 100, "height": 100 },
                "children": []
              }
            ]
          }
        ]
        """;

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ElementIdActions_DetectCaptureEpochSupportOncePerInspectorConnection(
        bool supportsCaptureEpoch)
    {
        await using var agent = new MockAgentServer(supportsCaptureEpoch: supportsCaptureEpoch);
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            var requestJson = supportsCaptureEpoch
                ? """{"elementId":"element-42","captureEpoch":42,"registryGeneration":7}"""
                : """{"elementId":"element-42"}""";

            for (var i = 0; i < 3; i++)
            {
                using var request = new StringContent(requestJson, Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(
                    $"http://localhost:{inspectorPort}/api/tap",
                    request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            Assert.Equal(
                1,
                agent.RecordedRequests.Count(
                    recorded => recorded.Path == "/api/v1/agent/capabilities"));
            Assert.Equal(
                3,
                agent.RecordedRequests.Count(
                    recorded => recorded.Path == "/api/v1/ui/actions/tap"));
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task ElementIdActions_RetryCapabilityDetectionAfterJsonErrorResponse()
    {
        await using var agent = new MockAgentServer(capabilitiesErrorResponseCount: 1);
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();

            using (var firstRequest = new StringContent(
                """{"elementId":"element-42"}""",
                Encoding.UTF8,
                "application/json"))
            using (var firstResponse = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/tap",
                firstRequest))
            {
                Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            }

            using (var secondRequest = new StringContent(
                """{"elementId":"element-42"}""",
                Encoding.UTF8,
                "application/json"))
            using (var secondResponse = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/tap",
                secondRequest))
            {
                Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);
            }

            using (var thirdRequest = new StringContent(
                """{"elementId":"element-42","captureEpoch":42,"registryGeneration":7}""",
                Encoding.UTF8,
                "application/json"))
            using (var thirdResponse = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/tap",
                thirdRequest))
            {
                Assert.Equal(HttpStatusCode.OK, thirdResponse.StatusCode);
            }

            Assert.Equal(
                2,
                agent.RecordedRequests.Count(
                    recorded => recorded.Path == "/api/v1/agent/capabilities"));
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
            Assert.Contains("targetScale=1", screenshotRequest.QueryString);
            Assert.Contains("maxPixels=8388608", screenshotRequest.QueryString);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task State_DifferentResolutionBucket_CreatesDistinctAtomicFrame()
    {
        await using var agent = new MockAgentServer();
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var defaultResponse = await client.GetAsync(
                $"http://localhost:{inspectorPort}/api/state");
            defaultResponse.EnsureSuccessStatusCode();
            using var defaultState = JsonDocument.Parse(await defaultResponse.Content.ReadAsStringAsync());

            using var highDpiResponse = await client.GetAsync(
                $"http://localhost:{inspectorPort}/api/state?targetScale=2");
            highDpiResponse.EnsureSuccessStatusCode();
            using var highDpiState = JsonDocument.Parse(await highDpiResponse.Content.ReadAsStringAsync());

            Assert.NotEqual(
                defaultState.RootElement.GetProperty("frameId").GetString(),
                highDpiState.RootElement.GetProperty("frameId").GetString());
            Assert.Equal(
                2,
                highDpiState.RootElement
                    .GetProperty("frame")
                    .GetProperty("requestedCaptureScale")
                    .GetDouble());
            Assert.True(highDpiState.RootElement.GetProperty("screenshotResolutionSupported").GetBoolean());

            var screenshotRequests = agent.RecordedRequests
                .Where(request => request.Path == "/api/v1/ui/screenshot")
                .ToArray();
            Assert.Equal(2, screenshotRequests.Length);
            Assert.Contains("targetScale=1", screenshotRequests[0].QueryString);
            Assert.Contains("maxPixels=8388608", screenshotRequests[0].QueryString);
            Assert.Contains("targetScale=2", screenshotRequests[1].QueryString);
            Assert.Contains("maxPixels=8388608", screenshotRequests[1].QueryString);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task State_LegacyAgent_DoesNotSendResolutionHints()
    {
        await using var agent = new MockAgentServer(supportsCaptureEpoch: false);
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
            Assert.DoesNotContain("targetScale", screenshotRequest.QueryString);
            Assert.DoesNotContain("maxPixelWidth", screenshotRequest.QueryString);
            Assert.DoesNotContain("maxPixelHeight", screenshotRequest.QueryString);
            Assert.DoesNotContain("maxPixels", screenshotRequest.QueryString);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task Root_SupportedAgent_AppliesInitialPixelBudget()
    {
        await using var agent = new MockAgentServer
        {
            VisualTreeResponse = """
                [
                  {
                    "id": "window-root",
                    "type": "Window",
                    "isVisible": true,
                    "isEnabled": true,
                    "children": [
                      {
                        "id": "page-root",
                        "type": "ContentPage",
                        "isVisible": true,
                        "isEnabled": true,
                        "bounds": { "x": 0, "y": 0, "width": 4096, "height": 2160 },
                        "windowBounds": { "x": 0, "y": 0, "width": 4096, "height": 2160 },
                        "children": []
                      }
                    ]
                  }
                ]
                """
        };
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(
                $"http://localhost:{inspectorPort}/");
            response.EnsureSuccessStatusCode();

            var screenshotRequest = Assert.Single(
                agent.RecordedRequests,
                request => request.Path == "/api/v1/ui/screenshot");
            Assert.Contains("targetScale=1", screenshotRequest.QueryString);
            Assert.Contains("maxPixelWidth=4096", screenshotRequest.QueryString);
            Assert.Contains("maxPixelHeight=2160", screenshotRequest.QueryString);
            Assert.Contains("maxPixels=8388608", screenshotRequest.QueryString);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task State_InvalidResolutionHint_IsRejectedBeforeCapture()
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
                $"http://localhost:{inspectorPort}/api/state?targetScale=NaN");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.DoesNotContain(
                agent.RecordedRequests,
                request => request.Path == "/api/v1/ui/tree"
                    || request.Path == "/api/v1/ui/screenshot");
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
                $"http://localhost:{inspectorPort}/screenshot.png?frame=00000000000000000000000000000000");

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
                "id=\"screenshot\" src=\"(screenshot\\.png\\?frame=[0-9a-f]+)\"");
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
    public async Task State_ExposesVersionedFrameDescriptorAndLegacyAliases()
    {
        await using var agent = new MockAgentServer
        {
            VisualTreeResponse = OffsetVisualTree
        };
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var firstResponse = await client.GetAsync(
                $"http://localhost:{inspectorPort}/api/state");
            firstResponse.EnsureSuccessStatusCode();
            using var firstState = JsonDocument.Parse(
                await firstResponse.Content.ReadAsStringAsync());
            var firstFrame = firstState.RootElement.GetProperty("frame");

            Assert.Equal(firstState.RootElement.GetProperty("frameId").GetString(), firstFrame.GetProperty("frameId").GetString());
            Assert.Equal(firstState.RootElement.GetProperty("viewportWidth").GetDouble(), firstFrame.GetProperty("logicalWidth").GetDouble());
            Assert.Equal(firstState.RootElement.GetProperty("viewportHeight").GetDouble(), firstFrame.GetProperty("logicalHeight").GetDouble());
            Assert.Equal(100, firstFrame.GetProperty("logicalWidth").GetDouble());
            Assert.Equal(100, firstFrame.GetProperty("logicalHeight").GetDouble());
            Assert.Equal(1, firstFrame.GetProperty("pixelWidth").GetInt32());
            Assert.Equal(1, firstFrame.GetProperty("pixelHeight").GetInt32());
            Assert.Equal(10, firstFrame.GetProperty("rootOffsetX").GetDouble());
            Assert.Equal(20, firstFrame.GetProperty("rootOffsetY").GetDouble());
            Assert.Equal(0.01, firstFrame.GetProperty("captureScale").GetDouble(), precision: 6);
            Assert.Equal(42, firstFrame.GetProperty("captureEpoch").GetInt64());
            Assert.Equal(7, firstFrame.GetProperty("registryGeneration").GetInt64());
            Assert.Equal(0, firstFrame.GetProperty("windowId").GetInt32());
            Assert.Equal(1, firstFrame.GetProperty("frameRevision").GetInt64());
            Assert.False(string.IsNullOrWhiteSpace(firstFrame.GetProperty("streamId").GetString()));
            Assert.True(firstFrame.GetProperty("createdAt").TryGetDateTime(out _));

            var firstStreamId = firstFrame.GetProperty("streamId").GetString();
            var firstFrameId = firstFrame.GetProperty("frameId").GetString();
            using var backResponse = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/back",
                content: null);
            backResponse.EnsureSuccessStatusCode();
            using var secondResponse = await client.GetAsync(
                $"http://localhost:{inspectorPort}/api/state");
            secondResponse.EnsureSuccessStatusCode();
            using var secondState = JsonDocument.Parse(
                await secondResponse.Content.ReadAsStringAsync());
            var secondFrame = secondState.RootElement.GetProperty("frame");
            Assert.Equal(firstStreamId, secondFrame.GetProperty("streamId").GetString());
            Assert.NotEqual(firstFrameId, secondFrame.GetProperty("frameId").GetString());
            Assert.Equal(2, secondFrame.GetProperty("frameRevision").GetInt64());

            var html = await client.GetStringAsync($"http://localhost:{inspectorPort}/");
            var meta = System.Text.RegularExpressions.Regex.Match(
                html,
                "<meta name=\"devflow-frame\" content=\"([^\"]+)\">");
            Assert.True(meta.Success);
            using var initialFrame = JsonDocument.Parse(
                WebUtility.HtmlDecode(meta.Groups[1].Value));
            Assert.Equal(firstStreamId, initialFrame.RootElement.GetProperty("streamId").GetString());
            Assert.True(initialFrame.RootElement.GetProperty("frameRevision").GetInt64() >= 2);
            var initialFrameId = initialFrame.RootElement.GetProperty("frameId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(initialFrameId));
            Assert.Contains($"screenshot.png?frame={initialFrameId}", html);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Theory]
    [InlineData("appLogical", 5, 6, 15, 26)]
    [InlineData("screenshotPixel", 0.5, 0.25, 60, 45)]
    public async Task CorrelatedHitTest_ConvertsCoordinatesUsingReferencedFrame(
        string coordinateSpace,
        double x,
        double y,
        double expectedX,
        double expectedY)
    {
        await using var agent = new MockAgentServer
        {
            VisualTreeResponse = OffsetVisualTree
        };
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
            using var state = JsonDocument.Parse(
                await stateResponse.Content.ReadAsStringAsync());
            var frame = state.RootElement.GetProperty("frame");
            agent.ClearRecordedRequests();

            var requestJson = JsonSerializer.Serialize(new
            {
                x,
                y,
                correlation = new
                {
                    actionId = "action-1",
                    source = "inspector",
                    streamId = frame.GetProperty("streamId").GetString(),
                    frameId = frame.GetProperty("frameId").GetString(),
                    frameRevision = frame.GetProperty("frameRevision").GetInt64(),
                    coordinateSpace
                }
            });
            using var request = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/hitTest",
                request);
            response.EnsureSuccessStatusCode();

            var hitTest = Assert.Single(
                agent.RecordedRequests,
                recorded => recorded.Path == "/api/v1/ui/hit-test");
            Assert.Contains(
                $"x={expectedX.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                hitTest.QueryString);
            Assert.Contains(
                $"y={expectedY.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                hitTest.QueryString);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task CorrelatedHitTest_WithExpiredFrame_IsRejectedWithoutRetargeting()
    {
        await using var agent = new MockAgentServer();
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
            using var state = JsonDocument.Parse(
                await stateResponse.Content.ReadAsStringAsync());
            var frame = state.RootElement.GetProperty("frame");
            agent.ClearRecordedRequests();

            var requestJson = JsonSerializer.Serialize(new
            {
                x = 5,
                y = 6,
                correlation = new
                {
                    actionId = "action-expired",
                    source = "inspector",
                    streamId = frame.GetProperty("streamId").GetString(),
                    frameId = "00000000000000000000000000000000",
                    frameRevision = frame.GetProperty("frameRevision").GetInt64(),
                    coordinateSpace = "appLogical"
                }
            });
            using var request = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/hitTest",
                request);

            Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("frame-expired", error.RootElement.GetProperty("error").GetString());
            Assert.DoesNotContain(
                agent.RecordedRequests,
                recorded => recorded.Path == "/api/v1/ui/hit-test");
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task CorrelatedHitTest_NewestFrameRemainsValidPastAgePruningWindow()
    {
        await using var agent = new MockAgentServer
        {
            VisualTreeResponse = OffsetVisualTree
        };
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
            using var state = JsonDocument.Parse(
                await stateResponse.Content.ReadAsStringAsync());
            var frame = state.RootElement.GetProperty("frame");
            agent.ClearRecordedRequests();

            await Task.Delay(TimeSpan.FromMilliseconds(10_250));

            var requestJson = JsonSerializer.Serialize(new
            {
                x = 5,
                y = 6,
                correlation = new
                {
                    actionId = "action-after-idle",
                    source = "inspector",
                    streamId = frame.GetProperty("streamId").GetString(),
                    frameId = frame.GetProperty("frameId").GetString(),
                    frameRevision = frame.GetProperty("frameRevision").GetInt64(),
                    coordinateSpace = "appLogical"
                }
            });
            using var request = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/hitTest",
                request);

            response.EnsureSuccessStatusCode();
            Assert.Contains(
                agent.RecordedRequests,
                recorded => recorded.Path == "/api/v1/ui/hit-test");
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task CorrelatedHitTest_EquivalentNewerFrame_DoesNotInvalidateDisplayedCoordinates()
    {
        await using var agent = new MockAgentServer
        {
            VisualTreeResponse = OffsetVisualTree
        };
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var firstResponse = await client.GetAsync(
                $"http://localhost:{inspectorPort}/api/state");
            firstResponse.EnsureSuccessStatusCode();
            using var firstState = JsonDocument.Parse(
                await firstResponse.Content.ReadAsStringAsync());
            var firstFrame = firstState.RootElement.GetProperty("frame");

            using var backResponse = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/back",
                content: null);
            backResponse.EnsureSuccessStatusCode();
            using var secondResponse = await client.GetAsync(
                $"http://localhost:{inspectorPort}/api/state");
            secondResponse.EnsureSuccessStatusCode();
            agent.ClearRecordedRequests();

            var requestJson = JsonSerializer.Serialize(new
            {
                x = 5,
                y = 6,
                correlation = new
                {
                    actionId = "action-superseded",
                    source = "inspector",
                    streamId = firstFrame.GetProperty("streamId").GetString(),
                    frameId = firstFrame.GetProperty("frameId").GetString(),
                    frameRevision = firstFrame.GetProperty("frameRevision").GetInt64(),
                    coordinateSpace = "appLogical"
                }
            });
            using var request = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/hitTest",
                request);

            response.EnsureSuccessStatusCode();
            Assert.Contains(
                agent.RecordedRequests,
                recorded => recorded.Path == "/api/v1/ui/hit-test");
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task CorrelatedHitTest_ChangedGeometryFrame_IsRejectedWithoutRetargeting()
    {
        await using var agent = new MockAgentServer
        {
            VisualTreeResponse = OffsetVisualTree
        };
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var firstResponse = await client.GetAsync(
                $"http://localhost:{inspectorPort}/api/state");
            firstResponse.EnsureSuccessStatusCode();
            using var firstState = JsonDocument.Parse(
                await firstResponse.Content.ReadAsStringAsync());
            var firstFrame = firstState.RootElement.GetProperty("frame");

            agent.VisualTreeResponse = OffsetVisualTree.Replace(
                "\"x\": 10, \"y\": 20",
                "\"x\": 30, \"y\": 20",
                StringComparison.Ordinal);
            using var backResponse = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/back",
                content: null);
            backResponse.EnsureSuccessStatusCode();
            using var secondResponse = await client.GetAsync(
                $"http://localhost:{inspectorPort}/api/state");
            secondResponse.EnsureSuccessStatusCode();
            agent.ClearRecordedRequests();

            var requestJson = JsonSerializer.Serialize(new
            {
                x = 5,
                y = 6,
                correlation = new
                {
                    actionId = "action-superseded",
                    source = "inspector",
                    streamId = firstFrame.GetProperty("streamId").GetString(),
                    frameId = firstFrame.GetProperty("frameId").GetString(),
                    frameRevision = firstFrame.GetProperty("frameRevision").GetInt64(),
                    coordinateSpace = "appLogical"
                }
            });
            using var request = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/hitTest",
                request);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("frame-superseded", error.RootElement.GetProperty("error").GetString());
            Assert.DoesNotContain(
                agent.RecordedRequests,
                recorded => recorded.Path == "/api/v1/ui/hit-test");
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task CorrelatedCoordinateTap_RehitTestsWithFreshCaptureForParentFallback()
    {
        await using var agent = new MockAgentServer(
            failFirstHitTestCandidate: true,
            changeHitTestCandidates: true)
        {
            VisualTreeResponse = OffsetVisualTree
        };
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
            using var state = JsonDocument.Parse(
                await stateResponse.Content.ReadAsStringAsync());
            var frame = state.RootElement.GetProperty("frame");
            agent.ClearRecordedRequests();

            var requestJson = JsonSerializer.Serialize(new
            {
                x = 5,
                y = 6,
                correlation = new
                {
                    actionId = "action-parent-fallback",
                    source = "inspector",
                    streamId = frame.GetProperty("streamId").GetString(),
                    frameId = frame.GetProperty("frameId").GetString(),
                    frameRevision = frame.GetProperty("frameRevision").GetInt64(),
                    coordinateSpace = "appLogical"
                }
            });
            using var request = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/tap",
                request);

            response.EnsureSuccessStatusCode();
            var taps = agent.RecordedRequests
                .Where(recorded => recorded.Path == "/api/v1/ui/actions/tap")
                .ToArray();
            Assert.Equal(2, taps.Length);
            using var firstTap = JsonDocument.Parse(taps[0].Body!);
            using var secondTap = JsonDocument.Parse(taps[1].Body!);
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
    public async Task CorrelatedHitTest_RevisionlessResolutionOnlyFrameReplacement_RemainsValid()
    {
        await using var agent = new MockAgentServer
        {
            VisualTreeResponse = RevisionlessOffsetVisualTree
        };
        await agent.StartAsync();

        var inspectorPort = GetFreePort();
        using var inspector = new InspectorServer(inspectorPort, "localhost", agent.Port);
        inspector.Start();

        try
        {
            using var client = new HttpClient();
            using var firstResponse = await client.GetAsync(
                $"http://localhost:{inspectorPort}/api/state");
            firstResponse.EnsureSuccessStatusCode();
            using var firstState = JsonDocument.Parse(
                await firstResponse.Content.ReadAsStringAsync());
            var firstFrame = firstState.RootElement.GetProperty("frame");

            using var secondResponse = await client.GetAsync(
                $"http://localhost:{inspectorPort}/api/state?targetScale=2");
            secondResponse.EnsureSuccessStatusCode();
            using var secondState = JsonDocument.Parse(
                await secondResponse.Content.ReadAsStringAsync());
            var secondFrame = secondState.RootElement.GetProperty("frame");
            Assert.NotEqual(
                firstFrame.GetProperty("frameId").GetString(),
                secondFrame.GetProperty("frameId").GetString());
            Assert.Equal(1, firstFrame.GetProperty("requestedCaptureScale").GetDouble());
            Assert.Equal(2, secondFrame.GetProperty("requestedCaptureScale").GetDouble());
            agent.ClearRecordedRequests();

            var requestJson = JsonSerializer.Serialize(new
            {
                x = 5,
                y = 6,
                correlation = new
                {
                    actionId = "action-resolution-only",
                    source = "inspector",
                    streamId = firstFrame.GetProperty("streamId").GetString(),
                    frameId = firstFrame.GetProperty("frameId").GetString(),
                    frameRevision = firstFrame.GetProperty("frameRevision").GetInt64(),
                    coordinateSpace = "appLogical"
                }
            });
            using var request = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/hitTest",
                request);

            response.EnsureSuccessStatusCode();
            Assert.Contains(
                agent.RecordedRequests,
                recorded => recorded.Path == "/api/v1/ui/hit-test");
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public async Task CorrelatedFill_WithExpiredFrame_IsRejectedBeforeMutation()
    {
        await using var agent = new MockAgentServer();
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
            using var state = JsonDocument.Parse(
                await stateResponse.Content.ReadAsStringAsync());
            var frame = state.RootElement.GetProperty("frame");
            agent.ClearRecordedRequests();

            var requestJson = JsonSerializer.Serialize(new
            {
                elementId = "element-42",
                text = "updated",
                captureEpoch = 42,
                registryGeneration = 7,
                correlation = new
                {
                    actionId = "fill-expired",
                    source = "inspector",
                    streamId = frame.GetProperty("streamId").GetString(),
                    frameId = "00000000000000000000000000000000",
                    frameRevision = frame.GetProperty("frameRevision").GetInt64(),
                    coordinateSpace = "appLogical"
                }
            });
            using var request = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/fill",
                request);

            Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
            Assert.DoesNotContain(
                agent.RecordedRequests,
                recorded => recorded.Path == "/api/v1/ui/actions/fill");
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
            using var initialStateResponse = await client.GetAsync(
                $"http://localhost:{inspectorPort}/api/state");
            initialStateResponse.EnsureSuccessStatusCode();
            using var initialState = JsonDocument.Parse(
                await initialStateResponse.Content.ReadAsStringAsync());
            var frame = initialState.RootElement.GetProperty("frame");

            using var backResponse = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/back",
                content: null);
            backResponse.EnsureSuccessStatusCode();

            using var failedState = await client.GetAsync(
                $"http://localhost:{inspectorPort}/api/state");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failedState.StatusCode);

            agent.ClearRecordedRequests();
            var requestJson = JsonSerializer.Serialize(new
            {
                x = 5,
                y = 6,
                correlation = new
                {
                    actionId = "action-after-failed-capture",
                    source = "inspector",
                    streamId = frame.GetProperty("streamId").GetString(),
                    frameId = frame.GetProperty("frameId").GetString(),
                    frameRevision = frame.GetProperty("frameRevision").GetInt64(),
                    coordinateSpace = "appLogical"
                }
            });
            using var request = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                $"http://localhost:{inspectorPort}/api/hitTest",
                request);

            response.EnsureSuccessStatusCode();
            Assert.Contains(
                agent.RecordedRequests,
                recorded => recorded.Path == "/api/v1/ui/hit-test");
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Theory]
    // "/" intentionally still serves the inspector shell when the agent has no tree — devflow.js
    // renders the disconnected overlay and keeps polling /api/state, which is the gated endpoint.
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
