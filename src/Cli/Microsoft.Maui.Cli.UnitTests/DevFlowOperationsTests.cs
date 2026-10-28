using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class NativeUiOperationsTests
{
    [Fact]
    public async Task Layout_ConstructsRequestWithScopeStabilityRulesAndPolicy()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var policy = new LayoutDiagnosticsPolicy
        {
            Suppressions = [new LayoutSuppression { RuleId = "text-clipping", AutomationId = "known-label" }]
        };

        var result = await new NativeUiOperations(client).AnalyzeLayoutAsync(
            "ci", "el-root", "text-clipping, overlap", "serious", true, false, 1234, true, 2,
            policy, CancellationToken.None);

        Assert.Equal(1, result!.Summary.Incomplete);
        var request = Assert.Single(server.RecordedRequests, r => r.Path.EndsWith("/diagnostics/layout"));
        using var document = JsonDocument.Parse(request.Body!);
        var body = document.RootElement;
        Assert.Equal("ci", body.GetProperty("profile").GetString());
        Assert.Equal("el-root", body.GetProperty("scope").GetProperty("rootElementId").GetString());
        Assert.Equal(2, body.GetProperty("scope").GetProperty("window").GetInt32());
        Assert.Equal("immediate", body.GetProperty("stability").GetProperty("mode").GetString());
        Assert.Equal(1234, body.GetProperty("stability").GetProperty("timeoutMs").GetInt32());
        Assert.Equal(["text-clipping", "overlap"], body.GetProperty("rules").EnumerateArray().Select(rule => rule.GetString()));
        Assert.Equal("known-label", body.GetProperty("suppressions")[0].GetProperty("automationId").GetString());
    }

    [Fact]
    public async Task Layout_UnsupportedWatch_ReportsNullOnceWithoutRetrying()
    {
        await using var server = new MockAgentServer { LayoutStatusCode = 404 };
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var results = new List<LayoutInspectionResult?>();

        await new NativeUiOperations(client).InspectLayoutAsync(
            "ci", null, null, "info", true, false, 1000, false, null,
            new LayoutDiagnosticsPolicy(), true, results.Add, CancellationToken.None);

        Assert.Null(Assert.Single(results));
        Assert.Single(server.RecordedRequests, r => r.Path.EndsWith("/diagnostics/layout"));
    }

    [Fact]
    public async Task Layout_WatchCancellation_PropagatesToCommandBoundary()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new NativeUiOperations(client).InspectLayoutAsync(
                "ci", null, null, "info", true, false, 1000, false, null,
                new LayoutDiagnosticsPolicy(), true, _ => cts.Cancel(), cts.Token));
    }

    [Theory]
    [InlineData("tap")]
    [InlineData("fill")]
    [InlineData("clear")]
    [InlineData("focus")]
    public async Task Actions_PreserveResolvedCaptureMetadataAndBorrowClient(string action)
    {
        await using var server = new MockAgentServer(supportMutationLease: true);
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port) { MutationLeaseId = "operation-test" };
        var operations = new NativeUiOperations(client);
        var element = new ElementInfo { Id = "el-target", CaptureEpoch = 42, RegistryGeneration = 7 };

        var success = action switch
        {
            "tap" => await operations.TapAsync(element),
            "fill" => await operations.FillAsync(element, "quoted ' & text"),
            "clear" => await operations.ClearAsync(element),
            _ => await operations.FocusAsync(element)
        };

        Assert.True(success);
        var request = Assert.Single(server.RecordedRequests, r => r.Path == $"/api/v1/ui/actions/{action}");
        using var document = JsonDocument.Parse(request.Body!);
        Assert.Equal("el-target", document.RootElement.GetProperty("elementId").GetString());
        Assert.Equal(42, document.RootElement.GetProperty("captureEpoch").GetInt64());
        Assert.Equal(7, document.RootElement.GetProperty("registryGeneration").GetInt64());
        Assert.NotNull(await client.GetStatusAsync());
    }

    [Fact]
    public async Task Drag_OntoAnotherElement_SendsBothFromOneTreeCapture()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        // Resolved by a query that never saw the drop target: its capture cannot validate both.
        var source = new ElementInfo { Id = "el-root", CaptureEpoch = 42, RegistryGeneration = 7 };

        var result = await new NativeUiOperations(client).DragAsync(source, 10, 10, null, null, "el-1", null, null, null);

        Assert.True(result.Success, result.Error);
        Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/tree");
        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/actions/gesture");
        using var document = JsonDocument.Parse(request.Body!);
        Assert.Equal("el-root", document.RootElement.GetProperty("elementId").GetString());
        Assert.Equal("el-1", document.RootElement.GetProperty("toElementId").GetString());
        // The tree's capture replaces the query's, so the stale epoch is not forwarded.
        Assert.False(document.RootElement.TryGetProperty("captureEpoch", out _));
    }

    [Fact]
    public async Task Drag_OntoAnElementNotInTheTree_FailsWithoutTouching()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var source = new ElementInfo { Id = "el-root", CaptureEpoch = 42, RegistryGeneration = 7 };

        var result = await new NativeUiOperations(client).DragAsync(source, 10, 10, null, null, "el-gone", null, null, null);

        Assert.False(result.Success);
        Assert.Contains("el-gone", result.Error);
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path == "/api/v1/ui/actions/gesture");
    }

    [Fact]
    public async Task Query_WaitGoneWithMatches_TimesOutWithoutChangingSelection()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var (elements, timedOut) = await new NativeUiOperations(client)
            .QueryAsync(null, "CounterBtn", null, waitUntil: "gone", timeout: 0);

        Assert.True(timedOut);
        Assert.NotEmpty(elements);
        Assert.Single(server.RecordedRequests, request => request.Path == "/api/v1/ui/elements");
    }

    [Fact]
    public async Task ResolveTarget_OutOfRange_ReturnsCountForCommandError()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var (element, count) = await new NativeUiOperations(client)
            .ResolveTargetAsync(null, "Button", null, int.MaxValue, ElementActionKind.Tap, true);

        Assert.Null(element);
        Assert.True(count > 0);
    }
}

[Collection("CLI")]
public sealed class StorageOperationsTests
{
    [Fact]
    public async Task Preferences_EncodeQueryAndPreserveRequestBody()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var http = new HttpClient();
        var operations = new StorageOperations(http, $"http://localhost:{server.Port}");

        Assert.True((await operations.GetPreferenceAsync("quoted & key", "string", "shared & name")).Success);
        Assert.True((await operations.SetPreferenceAsync("quoted & key", "'value' & text", "string", "shared & name")).Success);

        var get = Assert.Single(server.RecordedRequests, r => r.Method == "GET");
        Assert.Equal("?type=string&sharedName=shared%20%26%20name", get.QueryString);
        var put = Assert.Single(server.RecordedRequests, r => r.Method == "PUT");
        using var body = JsonDocument.Parse(put.Body!);
        Assert.Equal("'value' & text", body.RootElement.GetProperty("value").GetString());
        Assert.Equal("shared & name", body.RootElement.GetProperty("sharedName").GetString());
    }

    [Theory]
    [InlineData("{}", "Download response did not include contentBase64.")]
    [InlineData("""{"contentBase64":"invalid%base64"}""", "Download response contained invalid base64 content:")]
    public async Task Download_InvalidResponse_DoesNotCreateDestination(string body, string expectedError)
    {
        await using var server = new MockAgentServer { FileDownloadResponse = body };
        await server.StartAsync();
        using var http = new HttpClient();
        var path = Path.GetFullPath($".devflow-download-test-{Guid.NewGuid():N}");

        var result = await new StorageOperations(http, $"http://localhost:{server.Port}")
            .DownloadToFileAsync("file.txt", "cache", path);

        Assert.StartsWith(expectedError, result.Error);
        Assert.False(File.Exists(path));
        Assert.Equal("?root=cache", Assert.Single(server.RecordedRequests).QueryString);
    }

    [Fact]
    public async Task Download_WritesLocalContentAndReturnsMetadataWithoutBase64()
    {
        await using var server = new MockAgentServer
        {
            FileDownloadResponse = """{"contentBase64":"aGVsbG8=","size":5}"""
        };
        await server.StartAsync();
        using var http = new HttpClient();
        var path = Path.GetFullPath($".devflow-download-test-{Guid.NewGuid():N}");

        try
        {
            var result = await new StorageOperations(http, $"http://localhost:{server.Port}")
                .DownloadToFileAsync("file.txt", null, path);

            Assert.Null(result.Error);
            Assert.Equal("hello", await File.ReadAllTextAsync(path));
            Assert.False(result.Metadata!.ContainsKey("contentBase64"));
            Assert.Equal(path, result.Metadata["localPath"]!.GetValue<string>());
            Assert.Equal("aGVsbG8=", await StorageOperations.ReadUploadFileAsync(path));
            Assert.NotNull(await http.GetAsync($"http://localhost:{server.Port}/api/v1/storage/roots"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MissingSecureValue_PreservesRawHttpFailure()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var http = new HttpClient();

        var response = await new StorageOperations(http, $"http://localhost:{server.Port}")
            .GetDeviceAsync("not-a-resource");

        Assert.False(response.Success);
        Assert.True(response.IsRead);
    }
}

[Collection("CLI")]
public sealed class ExtensionOperationsTests
{
    [Theory]
    [InlineData("missing", "echo", "Extension 'missing' was not found.")]
    [InlineData("com.example.diagnostics", "missing", "Tool 'missing' was not found in extension 'com.example.diagnostics'.")]
    public async Task Call_MissingDescriptor_DoesNotInvoke(string ns, string tool, string error)
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var result = await new ExtensionOperations(client).CallAsync(ns, tool, null);

        Assert.Null(result.Result);
        Assert.Equal(error, result.Error);
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path.StartsWith("/api/v1/ext/"));
    }

    [Fact]
    public async Task Call_UsesAdvertisedRouteAndParameters()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var result = await new ExtensionOperations(client)
            .CallAsync("com.example.diagnostics", "echo", """{"value":"'quoted' & value"}""");

        Assert.Null(result.Error);
        var request = Assert.Single(server.RecordedRequests, r => r.Path.EndsWith("/echo"));
        Assert.Equal("POST", request.Method);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("'quoted' & value", body.RootElement.GetProperty("value").GetString());
        Assert.NotNull(await client.GetStatusAsync());
    }
}

[Collection("CLI")]
public sealed class ThemeOperationsTests
{
    [Fact]
    public async Task AppScope_UsesAgentAndPreservesTheme()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var operations = new ThemeOperations(client);

        var result = await operations.SetAsync("maccatalyst", DevFlowTheme.Dark, ThemeSetScope.App, null, null);

        Assert.True(result.Success);
        Assert.Equal(DevFlowTheme.Dark, result.Theme);
        Assert.Contains(server.RecordedRequests, r => r.Method == "PUT" && r.Path == "/api/v1/device/app/theme");
        Assert.NotNull(await operations.GetAsync());
    }

    [Fact]
    public async Task UnsupportedSystemScope_DoesNotFallBackToAppMutation()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var result = await new ThemeOperations(client)
            .SetAsync("maccatalyst", DevFlowTheme.Dark, ThemeSetScope.System, null, null);

        Assert.False(result.Success);
        Assert.Contains("--scope app", result.Message);
        Assert.DoesNotContain(server.RecordedRequests, r => r.Method == "PUT");
    }
}

[Collection("CLI")]
public sealed class ScreenshotOperationsTests
{
    [Fact]
    public async Task ElementCapture_ForwardsScopingWithoutSimulatorProbe()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var result = await new ScreenshotOperations(client).CaptureAsync(2, "el-target", null, 300, "native");

        Assert.True(result.Success);
        Assert.Equal(MockAgentResponses.ScreenshotPng, result.Data);
        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/screenshot");
        Assert.Contains("elementId=el-target", request.QueryString);
        Assert.Contains("maxWidth=300", request.QueryString);
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path == "/api/v1/agent/status");
    }

    [Fact]
    public void Resize_InvalidImageAndNativeScale_PreserveFallback()
    {
        var invalid = Encoding.UTF8.GetBytes("not a png");

        Assert.Same(invalid, ScreenshotOperations.ResizeSimctlScreenshot(invalid, 100, null));
        Assert.Same(invalid, ScreenshotOperations.ResizeSimctlScreenshot(invalid, null, "native"));
    }
}
