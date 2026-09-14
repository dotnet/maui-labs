using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Platforms.MacOS.Platform;
using DriverElementInfo = Microsoft.Maui.DevFlow.Driver.ElementInfo;

namespace Microsoft.Maui.DevFlow.Tests;

[Collection(NativeElementDiagnosticsCollection.Name)]
public class DevFlowAgentServiceLifecycleTests
{
    private static readonly byte[] NativeScreenshotPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x00
    ];

    [Fact]
    public async Task StartServerOnly_AllowsLateAppBinding()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);

        service.StartServerOnly(new ImmediateDispatcher());

        var beforeBind = await WaitForStatusAsync(client);
        Assert.NotNull(beforeBind);
        Assert.False(beforeBind!.Running);
        Assert.Equal("unknown", beforeBind.AppName);

        var app = new Application();
        service.BindApp(app);

        var afterBind = await WaitForStatusAsync(client);
        Assert.NotNull(afterBind);
        Assert.True(afterBind!.Running);
        Assert.Equal(app.GetType().Assembly.GetName().Name, afterBind.AppName);
    }

    [Fact]
    public async Task RecordingStatus_IsReadableByNonOwner_ButStopIsLeaseProtected()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var owner = new AgentClient("localhost", port) { MutationLeaseId = "owner" };
        using var observer = new AgentClient("localhost", port) { MutationLeaseId = "observer" };

        service.StartServerOnly(new ImmediateDispatcher());
        await WaitForStatusAsync(owner);
        var claim = await owner.ControlMutationLeaseAsync("claim");
        Assert.True(claim.YouHold);

        var status = await observer.ControlMutationRecordingAsync("status");
        Assert.False(status.Ok);
        Assert.Contains("broker", status.Error, StringComparison.OrdinalIgnoreCase);

        var stopError = await Assert.ThrowsAsync<MutationLeaseException>(() =>
            observer.ControlMutationRecordingAsync("stop", null, null, null, null, "recording"));
        Assert.Contains("driving", stopError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BaseAgent_ReportsJobsUnsupportedConsistently()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);

        service.StartServerOnly(new ImmediateDispatcher());

        var status = await WaitForStatusAsync(client);
        Assert.NotNull(status);
        Assert.NotNull(status!.Capabilities);
        Assert.False(status.Capabilities!.Jobs);
        Assert.Equal(Environment.ProcessId, status.App?.ProcessId);
        Assert.NotNull(status.Extensions);
        Assert.Equal(0, status.Extensions!.Count);
        Assert.Matches("^[a-f0-9]{64}$", status.Extensions.Hash);

        var capabilities = await client.GetCapabilitiesAsync();
        var capabilityMap = capabilities.GetProperty("capabilities");
        Assert.Contains("property-descriptors", capabilityMap.GetProperty("ui.actions").GetProperty("features").EnumerateArray().Select(feature => feature.GetString()));
        Assert.Contains("subscribe", capabilityMap.GetProperty("ui.events").GetProperty("features").EnumerateArray().Select(feature => feature.GetString()));
        var jobsCapabilities = capabilityMap.GetProperty("device.jobs");
        Assert.False(jobsCapabilities.GetProperty("supported").GetBoolean());
        Assert.Empty(jobsCapabilities.GetProperty("features").EnumerateArray());

        var jobs = await client.GetJobsAsync();
        Assert.False(jobs.GetProperty("supported").GetBoolean());
        Assert.Empty(jobs.GetProperty("jobs").EnumerateArray());

        var error = await Assert.ThrowsAsync<NotSupportedByAgentException>(
            () => client.RunJobAsync("missing-job"));
        Assert.Equal("device.jobs", error.Capability);
        Assert.Contains("not supported", error.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LayoutDiagnostics_AreAdvertisedAndCallableBeforeWindowCreation()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions
        {
            Port = port,
            EnableLayoutDiagnostics = true
        });
        using var client = new AgentClient("localhost", port);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(new Application());
        _ = await WaitForStatusAsync(client);

        var capabilities = await client.GetCapabilitiesAsync();
        var layoutCapabilities = capabilities.GetProperty("capabilities").GetProperty("ui.layoutDiagnostics");
        Assert.Equal("1.0", layoutCapabilities.GetProperty("schemaVersion").GetString());
        Assert.True(layoutCapabilities.GetProperty("watch").GetProperty("supported").GetBoolean());
        Assert.Equal(
            "polling",
            layoutCapabilities.GetProperty("watch").GetProperty("transport").GetString());
        Assert.False(layoutCapabilities.GetProperty("blazor").GetProperty("supported").GetBoolean());

        var result = await client.AnalyzeLayoutAsync(new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest
        {
            Stability = new Microsoft.Maui.DevFlow.Driver.LayoutStabilityOptions { Mode = "immediate" }
        });

        Assert.NotNull(result);
        Assert.Equal("1.0", result!.SchemaVersion);
        Assert.Equal(0, result.Snapshot.NodeCount);
        Assert.True(result.Summary.Incomplete >= 1);
        Assert.Contains(result.Coverage.Limitations, limitation =>
            limitation.Contains("visual tree is empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LayoutDiagnostics_DefaultOptions_DoNotAdvertiseExperimentalCapability()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);

        service.StartServerOnly(new ImmediateDispatcher());
        _ = await WaitForStatusAsync(client);

        var capabilities = await client.GetCapabilitiesAsync();
        Assert.False(capabilities.GetProperty("capabilities").TryGetProperty(
            "ui.layoutDiagnostics",
            out _));
        Assert.Null(await client.AnalyzeLayoutAsync(
            new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest()));
    }

    [Fact]
    public async Task LayoutDiagnostics_UnboundAgent_ReturnsRetryableNotReadyError()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions
        {
            Port = port,
            EnableLayoutDiagnostics = true
        });
        using var client = new AgentClient("localhost", port);

        service.StartServerOnly(new ImmediateDispatcher());
        _ = await WaitForStatusAsync(client);

        var exception = await Assert.ThrowsAsync<LayoutDiagnosticsException>(
            () => client.AnalyzeLayoutAsync(
                new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest()));
        Assert.Equal(503, exception.StatusCode);
        Assert.Equal("layout-diagnostics-not-ready", exception.ErrorType);
        Assert.True(exception.Retryable);
    }

    [Fact]
    public async Task LayoutDiagnostics_InvalidContractValues_ReturnBadRequest()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions
        {
            Port = port,
            EnableLayoutDiagnostics = true
        });
        using var client = new AgentClient("localhost", port);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(new Application());
        _ = await WaitForStatusAsync(client);

        Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest[] invalidRequests =
        [
            new()
            {
                MinimumSeverity = "seriuos"
            },
            new()
            {
                Occlusion = new Microsoft.Maui.DevFlow.Driver.LayoutOcclusionOptions
                {
                    Mode = "sometimes"
                }
            },
            new()
            {
                Privacy = new Microsoft.Maui.DevFlow.Driver.LayoutPrivacyOptions
                {
                    Text = "hash"
                }
            },
            new()
            {
                Rules = ["layout.element-cliped"]
            }
        ];

        foreach (var request in invalidRequests)
        {
            var exception = await Assert.ThrowsAsync<LayoutDiagnosticsException>(
                () => client.AnalyzeLayoutAsync(request));
            Assert.Equal(400, exception.StatusCode);
            Assert.Equal("layout-diagnostics-validation", exception.ErrorType);
            Assert.False(exception.Retryable);
        }
    }

    [Fact]
    public async Task Capabilities_WhenJobsRunUnsupported_DoesNotAdvertiseRunFeature()
    {
        var port = GetFreePort();
        using var service = new ListOnlyJobsAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);

        service.StartServerOnly(new ImmediateDispatcher());

        var status = await WaitForStatusAsync(client);
        Assert.NotNull(status);
        Assert.NotNull(status!.Capabilities);
        Assert.True(status.Capabilities!.Jobs);

        var capabilities = await client.GetCapabilitiesAsync();
        var jobsCapabilities = capabilities.GetProperty("capabilities").GetProperty("device.jobs");
        Assert.True(jobsCapabilities.GetProperty("supported").GetBoolean());

        var features = jobsCapabilities.GetProperty("features").EnumerateArray().Select(feature => feature.GetString()).ToArray();
        Assert.Equal(new[] { "list" }, features);
    }

    [Fact]
    public async Task DispatchAsync_WhenDispatcherDoesNotRequireDispatchButMainThreadDoes_UsesMainThreadFallback()
    {
        using var service = new DispatchProbeAgentService(new ImmediateDispatcher(), mainThreadDispatchRequired: true);

        var result = await service.RunDispatchAsync(() => service.IsInsideMainThreadFallback ? "main-thread" : "direct");

        Assert.Equal("main-thread", result);
        Assert.Equal(1, service.MainThreadFallbackCallCount);
    }

    [Fact]
    public async Task DispatchAsync_AsyncFunc_WhenDispatcherDoesNotRequireDispatchButMainThreadDoes_UsesMainThreadFallback()
    {
        using var service = new DispatchProbeAgentService(new ImmediateDispatcher(), mainThreadDispatchRequired: true);

        var result = await service.RunDispatchAsync(async () =>
        {
            await Task.Yield();
            return service.IsInsideMainThreadFallback ? "main-thread" : "direct";
        });

        Assert.Equal("main-thread", result);
        Assert.Equal(1, service.MainThreadFallbackCallCount);
    }

    [Fact]
    public async Task Extensions_AreDiscoverableAndCallable()
    {
        var port = GetFreePort();
        var options = new AgentOptions { Port = port };
        var extension = options.RegisterExtension(
            "com.example.diagnostics",
            "Diagnostics extension",
            "1.2.3",
            new[] { "build_info" });
        extension.MapTool(
            "build_info",
            "Returns build information.",
            "GET",
            "build-info",
            _ => Task.FromResult(HttpResponse.Json(new { version = "1.0.0" })),
            returns: JsonDocument.Parse("""{"type":"object","properties":{"version":{"type":"string"}}}""").RootElement.Clone(),
            annotations: new ExtensionToolAnnotations
            {
                ReadOnly = true,
                Idempotent = true,
                Category = "diagnostics"
            });

        using var service = new MauiDevFlowAgentService(options);
        using var client = new AgentClient("localhost", port);

        service.StartServerOnly(new ImmediateDispatcher());

        var status = await WaitForStatusAsync(client);
        Assert.NotNull(status?.Extensions);
        Assert.Equal(1, status!.Extensions!.Count);
        Assert.Matches("^[a-f0-9]{64}$", status.Extensions.Hash);

        var extensions = await client.GetExtensionsAsync();
        var descriptor = Assert.Single(extensions);
        Assert.Equal("com.example.diagnostics", descriptor.Key);
        Assert.Equal("1.2.3", descriptor.Value.Version);
        var tool = Assert.Single(descriptor.Value.Tools);
        Assert.Equal("build_info", tool.Name);
        Assert.Equal("/api/v1/ext/com.example.diagnostics/build-info", tool.Path);
        Assert.True(tool.Annotations!.ReadOnly);

        var result = await client.CallExtensionToolAsync(tool.Method, tool.Path);
        using var resultJson = JsonDocument.Parse(result);
        Assert.Equal("1.0.0", resultJson.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public void RegisterExtension_RejectsInvalidNamespace()
    {
        var options = new AgentOptions();

        Assert.Throws<ArgumentException>(() => options.RegisterExtension("diagnostics", "Invalid namespace"));
    }

    [Fact]
    public void RegisterExtension_RejectsInvalidVersion()
    {
        var options = new AgentOptions();

        Assert.Throws<ArgumentException>(() => options.RegisterExtension("com.example.diagnostics", "Diagnostics", "beta"));
    }

    [Fact]
    public async Task Extensions_WithSamePathAndDifferentMethods_GenerateUniqueToolNames()
    {
        var port = GetFreePort();
        var options = new AgentOptions { Port = port };
        var extension = options.RegisterExtension("com.example.diagnostics", "Diagnostics");
        extension.MapGet("echo", _ => Task.FromResult(HttpResponse.Json(new { method = "GET" })));
        extension.MapPost("echo", _ => Task.FromResult(HttpResponse.Json(new { method = "POST" })));

        using var service = new MauiDevFlowAgentService(options);
        using var client = new AgentClient("localhost", port);

        service.StartServerOnly(new ImmediateDispatcher());

        var extensions = await client.GetExtensionsAsync();
        var descriptor = Assert.Single(extensions);
        var toolNames = descriptor.Value.Tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "get_echo", "post_echo" }, toolNames);
    }

    [Fact]
    public async Task CaptureEpoch_RemainsValidAcrossPollingAndExpiresAfterMutation()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var invoked = false;
        var button = new Button
        {
            AutomationId = "CaptureEpochButton",
            Text = "Invoke"
        };
        button.Clicked += (_, _) => invoked = true;
        var app = new Application();
        var window = new Window(new ContentPage { Content = button });
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var firstTree = await client.GetTreeAsync();
        var firstButton = Assert.Single(
            Flatten(firstTree),
            element => element.AutomationId == "CaptureEpochButton");
        var secondTree = await client.GetTreeAsync();
        var secondButton = Assert.Single(
            Flatten(secondTree),
            element => element.AutomationId == "CaptureEpochButton");

        Assert.True(firstButton.CaptureEpoch > 0);
        Assert.True(secondButton.CaptureEpoch > firstButton.CaptureEpoch);
        Assert.True(await client.TapAsync(
            firstButton.Id,
            firstButton.CaptureEpoch,
            firstButton.RegistryGeneration));
        Assert.True(invoked);

        invoked = false;
        var thirdTree = await client.GetTreeAsync();
        var thirdButton = Assert.Single(
            Flatten(thirdTree),
            element => element.AutomationId == "CaptureEpochButton");
        Assert.True(await client.TapAsync(
            thirdButton.Id,
            thirdButton.CaptureEpoch,
            thirdButton.RegistryGeneration));
        Assert.True(invoked);

        invoked = false;
        Assert.False(await client.TapAsync(
            thirdButton.Id,
            thirdButton.CaptureEpoch,
            thirdButton.RegistryGeneration));
        Assert.False(invoked);
    }

    [Fact]
    public async Task CaptureEpoch_RoundTrip_StaleEpochReturns409ThenFreshEpochSucceeds()
    {
        // Exercises the actual /api/v1/ui/actions/tap route (ExecuteUiMutationAsync -> HandleTap
        // -> PrepareUiMutationAsync -> ValidateUiCapture/BuildStaleCaptureResponse), not just the
        // private capture-epoch helper methods directly, using AgentClient.TapResultAsync so the
        // HTTP status code and "reason" the wire actually returns are asserted rather than only
        // the collapsed bool TapAsync gives back.
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var invocationCount = 0;
        var button = new Button
        {
            AutomationId = "RoundTripCaptureEpochButton",
            Text = "Invoke"
        };
        button.Clicked += (_, _) => invocationCount++;
        var app = new Application();
        var window = new Window(new ContentPage { Content = button });
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        // Obtain a valid capture epoch and use it for a mutation: this must succeed over the real
        // route and invalidate that epoch as a side effect (mutations bump the mutation generation).
        var firstCapture = Assert.Single(
            Flatten(await client.GetTreeAsync()),
            element => element.AutomationId == "RoundTripCaptureEpochButton");
        var validResult = await client.TapResultAsync(
            firstCapture.Id,
            firstCapture.CaptureEpoch,
            firstCapture.RegistryGeneration);
        Assert.True(validResult.Success, $"Tap failed: {validResult.Error}; reason={validResult.Reason}");
        Assert.Equal(1, invocationCount);

        // Replay the now-stale epoch: the actual HTTP handler must answer 409 with
        // reason "stale-capture-epoch", not merely "failed".
        var staleResult = await client.TapResultAsync(
            firstCapture.Id,
            firstCapture.CaptureEpoch,
            firstCapture.RegistryGeneration);
        Assert.False(staleResult.Success);
        Assert.Equal(409, staleResult.StatusCode);
        Assert.Equal("stale-capture-epoch", staleResult.Reason);
        Assert.True(staleResult.Retryable);
        Assert.Equal(1, invocationCount);

        // A fresh capture epoch for the same element must succeed again over the same route.
        var freshCapture = Assert.Single(
            Flatten(await client.GetTreeAsync()),
            element => element.AutomationId == "RoundTripCaptureEpochButton");
        Assert.True(freshCapture.CaptureEpoch > firstCapture.CaptureEpoch);
        var freshResult = await client.TapResultAsync(
            freshCapture.Id,
            freshCapture.CaptureEpoch,
            freshCapture.RegistryGeneration);
        Assert.True(freshResult.Success, $"Tap failed: {freshResult.Error}; reason={freshResult.Reason}");
        Assert.Equal(2, invocationCount);
    }

    [Fact]
    public async Task CaptureEpoch_ExpiresAfterExternalElementPropertyChange()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var invoked = false;
        var button = new Button
        {
            AutomationId = "ExternallyChangedButton",
            Text = "Before"
        };
        button.Clicked += (_, _) => invoked = true;
        var app = new Application();
        var window = new Window(new ContentPage { Content = button });
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var tree = await client.GetTreeAsync();
        var buttonInfo = Assert.Single(
            Flatten(tree),
            element => element.AutomationId == "ExternallyChangedButton");

        button.Text = "After";

        Assert.False(await client.TapAsync(
            buttonInfo.Id,
            buttonInfo.CaptureEpoch,
            buttonInfo.RegistryGeneration));
        Assert.False(invoked);
    }

    [Fact]
    public async Task CaptureEpoch_ExpiresAfterSuccessfulPhysicalUiOperation()
    {
        var port = GetFreePort();
        using var service = new UiOperationProbeAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var invoked = false;
        var button = new Button
        {
            AutomationId = "PhysicalOperationButton",
            Text = "Invoke"
        };
        button.Clicked += (_, _) => invoked = true;
        var app = new Application();
        var window = new Window(new ContentPage { Content = button });
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var tree = await client.GetTreeAsync();
        var buttonInfo = Assert.Single(
            Flatten(tree),
            element => element.AutomationId == "PhysicalOperationButton");

        service.ReportSuccessfulUiOperation();

        Assert.False(await client.TapAsync(
            buttonInfo.Id,
            buttonInfo.CaptureEpoch,
            buttonInfo.RegistryGeneration));
        Assert.False(invoked);
    }

    [Fact]
    public async Task CaptureEpoch_ExpiresWhenDuplicateIdIsInsertedBeforeCapturedElement()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var originalInvocations = 0;
        var insertedInvocations = 0;
        var original = new Button
        {
            AutomationId = "DuplicateButton",
            Text = "Original"
        };
        original.Clicked += (_, _) => originalInvocations++;
        var layout = new VerticalStackLayout { Children = { original } };
        var app = new Application();
        var window = new Window(new ContentPage { Content = layout });
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var tree = await client.GetTreeAsync();
        var originalInfo = Assert.Single(
            Flatten(tree),
            element => element.AutomationId == "DuplicateButton");
        var inserted = new Button
        {
            AutomationId = "DuplicateButton",
            Text = "Inserted"
        };
        inserted.Clicked += (_, _) => insertedInvocations++;
        layout.Children.Insert(0, inserted);

        Assert.False(await client.TapAsync(
            originalInfo.Id,
            originalInfo.CaptureEpoch,
            originalInfo.RegistryGeneration));
        Assert.Equal(0, originalInvocations);
        Assert.Equal(0, insertedInvocations);
    }

    [Fact]
    public async Task CaptureEpoch_RejectsElementIdNotEmittedByThatCapture()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var invoked = false;
        var first = new Button { AutomationId = "FirstCaptureButton", Text = "First" };
        var second = new Button { AutomationId = "SecondCaptureButton", Text = "Second" };
        second.Clicked += (_, _) => invoked = true;
        var app = new Application();
        var window = new Window(new ContentPage
        {
            Content = new VerticalStackLayout { Children = { first, second } }
        });
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var tree = await client.GetTreeAsync();
        var secondInfo = Assert.Single(
            Flatten(tree),
            element => element.AutomationId == "SecondCaptureButton");
        var firstQuery = await client.QueryAsync(automationId: "FirstCaptureButton");
        var firstInfo = Assert.Single(firstQuery);

        Assert.False(await client.TapAsync(
            secondInfo.Id,
            firstInfo.CaptureEpoch,
            firstInfo.RegistryGeneration));
        Assert.False(invoked);
    }

    [Fact]
    public async Task Screenshot_RejectsElementIdNotEmittedByCapture()
    {
        var port = GetFreePort();
        using var service = new NativeScreenshotAgentService(
            new AgentOptions { Port = port },
            new RegisteredNativeElementRegistry());
        using var client = new AgentClient("localhost", port);
        var first = new Button { AutomationId = "FirstScreenshotButton", Text = "First" };
        var second = new Button { AutomationId = "SecondScreenshotButton", Text = "Second" };
        var app = new Application();
        var window = new Window(new ContentPage
        {
            Content = new VerticalStackLayout { Children = { first, second } }
        });
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var tree = await client.GetTreeAsync();
        var secondInfo = Assert.Single(
            Flatten(tree),
            element => element.AutomationId == "SecondScreenshotButton");
        var firstInfo = Assert.Single(await client.QueryAsync(automationId: "FirstScreenshotButton"));

        var result = await client.ScreenshotResultAsync(
            window: null,
            elementId: secondInfo.Id,
            selector: null,
            maxWidth: null,
            scale: null,
            captureEpoch: firstInfo.CaptureEpoch,
            registryGeneration: firstInfo.RegistryGeneration);

        Assert.False(result.Success);
        Assert.Equal("stale-capture-epoch", result.Reason);
    }

    [Fact]
    public async Task TreeAndQuery_DefaultToAllWindowsAndPreserveWindowIds()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var app = new Application();
        var firstWindow = new Window(new ContentPage
        {
            Content = new Label { AutomationId = "FirstWindowLabel", Text = "First" }
        });
        var secondWindow = new Window(new ContentPage
        {
            Content = new Label { AutomationId = "SecondWindowLabel", Text = "Second" }
        });
        var addWindow = typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        addWindow.Invoke(app, [firstWindow]);
        addWindow.Invoke(app, [secondWindow]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var allWindows = await client.GetTreeAsync();
        Assert.Equal(2, allWindows.Count);
        Assert.Equal(0, allWindows[0].WindowId);
        Assert.Equal(1, allWindows[1].WindowId);
        Assert.Equal(
            0,
            Assert.Single(
                Flatten(allWindows),
                element => element.AutomationId == "FirstWindowLabel").WindowId);
        Assert.Equal(
            1,
            Assert.Single(
                Flatten(allWindows),
                element => element.AutomationId == "SecondWindowLabel").WindowId);

        var secondWindowOnly = await client.GetTreeAsync(window: 1);
        var secondRoot = Assert.Single(secondWindowOnly);
        Assert.Equal(1, secondRoot.WindowId);
        Assert.DoesNotContain(
            Flatten(secondWindowOnly),
            element => element.AutomationId == "FirstWindowLabel");
        Assert.Contains(
            Flatten(secondWindowOnly),
            element => element.AutomationId == "SecondWindowLabel");

        var labels = await client.QueryAsync(type: "Label");
        Assert.Equal(
            0,
            Assert.Single(
                labels,
                element => element.AutomationId == "FirstWindowLabel").WindowId);
        Assert.Equal(
            1,
            Assert.Single(
                labels,
                element => element.AutomationId == "SecondWindowLabel").WindowId);
    }

    [Fact]
    public async Task WindowScopedTree_DuplicateAutomationId_PreservesGlobalIdAndTargetsCorrectWindow()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var firstInvocations = 0;
        var secondInvocations = 0;
        var thirdInvocations = 0;
        var firstButton = new Button { AutomationId = "SharedWindowButton", Text = "First" };
        var secondButton = new Button { AutomationId = "SharedWindowButton", Text = "Second" };
        var thirdButton = new Button { AutomationId = "SharedWindowButton", Text = "Third" };
        firstButton.Clicked += (_, _) => firstInvocations++;
        secondButton.Clicked += (_, _) => secondInvocations++;
        thirdButton.Clicked += (_, _) => thirdInvocations++;
        var app = new Application();
        var addWindow = typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        addWindow.Invoke(app, [new Window(new ContentPage { Content = firstButton })]);
        addWindow.Invoke(app, [new Window(new ContentPage
        {
            Content = new VerticalStackLayout { Children = { secondButton, thirdButton } }
        })]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var allWindows = await client.GetTreeAsync();
        var globalSecond = Assert.Single(
            Flatten(allWindows),
            element => element.Text == "Second");
        var globalThird = Assert.Single(
            Flatten(allWindows),
            element => element.Text == "Third");
        var secondWindow = await client.GetTreeAsync(window: 1);
        var scopedSecond = Assert.Single(
            Flatten(secondWindow),
            element => element.Text == "Second");
        var scopedThird = Assert.Single(
            Flatten(secondWindow),
            element => element.Text == "Third");

        Assert.Equal(globalSecond.Id, scopedSecond.Id);
        Assert.Equal(globalThird.Id, scopedThird.Id);
        Assert.NotEqual(scopedSecond.Id, scopedThird.Id);
        Assert.Equal(1, scopedSecond.WindowId);

        var detail = await client.GetElementAsync(scopedSecond.Id);
        Assert.NotNull(detail);
        Assert.Equal(1, detail!.WindowId);
        Assert.Same(
            secondButton,
            new VisualTreeWalker().GetElementById(scopedSecond.Id, app));

        Assert.True(await client.TapAsync(
            scopedSecond.Id,
            scopedSecond.CaptureEpoch,
            scopedSecond.RegistryGeneration));
        Assert.Equal(0, firstInvocations);
        Assert.Equal(1, secondInvocations);
        Assert.Equal(0, thirdInvocations);
    }

    [Fact]
    public async Task WindowScopedCapture_IsNotInvalidatedByAnotherWindowChange()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var firstLabel = new Label { AutomationId = "FirstWindowStatus", Text = "Before" };
        var secondInvocations = 0;
        var secondButton = new Button
        {
            AutomationId = "SecondWindowAction",
            Text = "Invoke"
        };
        secondButton.Clicked += (_, _) => secondInvocations++;
        var app = new Application();
        var addWindow = typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        addWindow.Invoke(app, [new Window(new ContentPage { Content = firstLabel })]);
        addWindow.Invoke(app, [new Window(new ContentPage { Content = secondButton })]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        await client.GetTreeAsync();
        var secondCapture = Assert.Single(
            Flatten(await client.GetTreeAsync(window: 1)),
            element => element.AutomationId == "SecondWindowAction");

        firstLabel.Text = "After";

        Assert.True(await client.TapAsync(
            secondCapture.Id,
            secondCapture.CaptureEpoch,
            secondCapture.RegistryGeneration));
        Assert.Equal(1, secondInvocations);
    }

    [Fact]
    public async Task HitTest_WithoutWindow_DefaultsToWindowZero()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var app = new Application();
        var addWindow = typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        addWindow.Invoke(app, [new Window(new ContentPage())]);
        addWindow.Invoke(app, [new Window(new ContentPage())]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var response = await client.HitTestAsync(0, 0);
        using var document = JsonDocument.Parse(response);

        Assert.Equal(0, document.RootElement.GetProperty("window").GetInt32());
        Assert.True(document.RootElement.GetProperty("captureEpoch").GetInt64() > 0);
    }

    [Fact]
    public void HitTestCoordinates_UseInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.True(MauiDevFlowAgentService.TryParseCoordinate("95.5", out var coordinate));
            Assert.Equal(95.5, coordinate);
            Assert.False(MauiDevFlowAgentService.TryParseCoordinate("95,5", out _));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task CaptureEpoch_RetainsConcurrentClientLeaseWindow()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var invoked = false;
        var button = new Button
        {
            AutomationId = "LeaseWindowButton",
            Text = "Invoke"
        };
        button.Clicked += (_, _) => invoked = true;
        var app = new Application();
        var window = new Window(new ContentPage { Content = button });
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var firstTree = await client.GetTreeAsync();
        var firstButton = Assert.Single(
            Flatten(firstTree),
            element => element.AutomationId == "LeaseWindowButton");
        for (var i = 0; i < 64; i++)
            await client.GetTreeAsync();

        Assert.True(await client.TapAsync(
            firstButton.Id,
            firstButton.CaptureEpoch,
            firstButton.RegistryGeneration));
        Assert.True(invoked);
    }

    [Fact]
    public async Task StaleMutationRequest_DoesNotInvalidateAnotherClientCapture()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var invoked = false;
        var button = new Button
        {
            AutomationId = "LeaseIsolationButton",
            Text = "Invoke"
        };
        button.Clicked += (_, _) => invoked = true;
        var app = new Application();
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [new Window(new ContentPage { Content = button })]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var validCapture = Assert.Single(
            Flatten(await client.GetTreeAsync()),
            element => element.AutomationId == "LeaseIsolationButton");

        Assert.False(await client.TapAsync(
            validCapture.Id,
            validCapture.CaptureEpoch + 10_000,
            validCapture.RegistryGeneration));
        Assert.True(await client.TapAsync(
            validCapture.Id,
            validCapture.CaptureEpoch,
            validCapture.RegistryGeneration));
        Assert.True(invoked);
    }

    [Fact]
    public async Task NativeElement_GenericPropertyEndpointsAreRejected()
    {
        var port = GetFreePort();
        var registry = new RegisteredNativeElementRegistry();
        var nativeElement = new NativePropertyTarget { IsEnabled = true };
        var elementId = registry.Register(
            new ToolbarItem { Text = "Native" },
            nativeElement,
            "ToolbarItem");
        using var service = new MauiDevFlowAgentService(
            new AgentOptions { Port = port },
            registry,
            nativeElementSubscription: null);
        using var client = new AgentClient("localhost", port);
        var app = new Application();

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetPropertyAsync(elementId, nameof(NativePropertyTarget.IsEnabled)));
        Assert.Contains("native-property-not-supported", ex.Message);
        Assert.Contains("Generic property reflection is not supported for native elements", ex.Message);
        Assert.False(await client.SetPropertyAsync(
            elementId,
            nameof(NativePropertyTarget.IsEnabled),
            "false"));
        Assert.True(nativeElement.IsEnabled);
    }

    [Fact]
    public async Task DiagnosticListenerRegistration_BecomesVisibleAsNativeRegisteredElement_AndResolvesById()
    {
        // Fires through the real production publisher (NativeElementDiagnosticsBridge, linked in
        // from platforms/MacOS — see NativeElementDiagnosticContractTests) rather than calling
        // RegisteredNativeElementRegistry.Register directly, so this covers the whole seam: a
        // DiagnosticListener event -> MauiNativeElementDiagnosticSubscriber -> registry ->
        // VisualTreeWalker -> the tree/query HTTP service path -> resolution by id.
        var port = GetFreePort();
        var registry = new RegisteredNativeElementRegistry();
        using var subscriber = new MauiNativeElementDiagnosticSubscriber(registry);
        using var service = new RegistryBackedAgentService(
            new AgentOptions { Port = port },
            registry,
            nativeElementSubscription: subscriber);
        using var client = new AgentClient("localhost", port);

        var nativeElement = new object();
        var page = new ContentPage();
        var app = new Application();
        var window = new Window(page);
        var owner = window;
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);
        Assert.Equal(0, VisualTreeWalker.GetWindowIdForElement(owner, app));

        var dispatcher = new AsyncDispatchRequiredDispatcher();
        service.StartServerOnly(dispatcher);
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        NativeElementDiagnosticsBridge.Register(owner, nativeElement, "Dialog", "RealizedView");
        var registration = Assert.Single(registry.GetSnapshot());

        var resolved = await client.GetElementAsync(registration.Id);
        Assert.NotNull(resolved);
        Assert.Equal(registration.Id, resolved!.Id);
        Assert.Equal("native", resolved.Framework);
        Assert.Equal(0, resolved.WindowId);
        Assert.True(service.WindowResolutionWasDispatched);

        var tree = await client.GetTreeAsync();
        var registered = Assert.Single(
            Flatten(tree),
            element => element.Id.StartsWith("native:registered:", StringComparison.Ordinal));
        Assert.Equal("native", registered.Framework);
        Assert.NotNull(registered.OwnerId);

        NativeElementDiagnosticsBridge.Unregister(nativeElement);
    }

    [Fact]
    public async Task CaptureEpoch_AllowsOnlyOneConcurrentMutation()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var invocationCount = 0;
        var button = new Button
        {
            AutomationId = "ConcurrentMutationButton",
            Text = "Invoke"
        };
        button.Clicked += (_, _) => Interlocked.Increment(ref invocationCount);
        var app = new Application();
        var window = new Window(new ContentPage { Content = button });
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var tree = await client.GetTreeAsync();
        var buttonInfo = Assert.Single(
            Flatten(tree),
            element => element.AutomationId == "ConcurrentMutationButton");
        var results = await Task.WhenAll(
            client.TapAsync(
                buttonInfo.Id,
                buttonInfo.CaptureEpoch,
                buttonInfo.RegistryGeneration),
            client.TapAsync(
                buttonInfo.Id,
                buttonInfo.CaptureEpoch,
                buttonInfo.RegistryGeneration));

        Assert.Equal(1, results.Count(result => result));
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public async Task Batch_WithCaptureLease_ExecutesAllActionsAgainstCapturedTargets()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var invoked = false;
        var entry = new Entry { AutomationId = "BatchEntry" };
        var button = new Button { AutomationId = "BatchButton", Text = "Submit" };
        button.Clicked += (_, _) => invoked = true;
        var page = new ContentPage
        {
            Content = new VerticalStackLayout
            {
                Children = { entry, button }
            }
        };
        var app = new Application();
        var window = new Window(page);
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var tree = await client.GetTreeAsync();
        var entryInfo = Assert.Single(Flatten(tree), element => element.AutomationId == "BatchEntry");
        var buttonInfo = Assert.Single(Flatten(tree), element => element.AutomationId == "BatchButton");
        var result = await client.BatchAsync(
            [
                new JsonObject
                {
                    ["action"] = "fill",
                    ["elementId"] = entryInfo.Id,
                    ["text"] = "hello"
                },
                new JsonObject
                {
                    ["action"] = "tap",
                    ["elementId"] = buttonInfo.Id
                }
            ],
            continueOnError: false,
            captureEpoch: entryInfo.CaptureEpoch,
            registryGeneration: entryInfo.RegistryGeneration);

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal("hello", entry.Text);
        Assert.True(invoked);
    }

    [Fact]
    public async Task Batch_WithCaptureLease_StopsWhenEarlierActionRemovesLaterTarget()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var removedTargetInvoked = false;
        var layout = new VerticalStackLayout();
        var removeButton = new Button { AutomationId = "RemoveTarget", Text = "Remove" };
        var removedTarget = new Button { AutomationId = "RemovedTarget", Text = "Target" };
        removeButton.Clicked += (_, _) => layout.Children.Remove(removedTarget);
        removedTarget.Clicked += (_, _) => removedTargetInvoked = true;
        layout.Children.Add(removeButton);
        layout.Children.Add(removedTarget);
        var app = new Application();
        var window = new Window(new ContentPage { Content = layout });
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var tree = await client.GetTreeAsync();
        var removeInfo = Assert.Single(Flatten(tree), element => element.AutomationId == "RemoveTarget");
        var removedInfo = Assert.Single(Flatten(tree), element => element.AutomationId == "RemovedTarget");
        var result = await client.BatchAsync(
            [
                new JsonObject
                {
                    ["action"] = "tap",
                    ["elementId"] = removeInfo.Id
                },
                new JsonObject
                {
                    ["action"] = "tap",
                    ["elementId"] = removedInfo.Id
                }
            ],
            continueOnError: false,
            captureEpoch: removeInfo.CaptureEpoch,
            registryGeneration: removeInfo.RegistryGeneration);

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.False(removedTargetInvoked);
        var results = result.GetProperty("results");
        Assert.Equal(2, results.GetArrayLength());
        Assert.True(results[0].GetProperty("success").GetBoolean());
        Assert.Equal(409, results[1].GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task Batch_WithCaptureLease_ContinueOnError_ExecutesLaterActions()
    {
        var port = GetFreePort();
        using var service = new MauiDevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var invoked = false;
        var button = new Button { AutomationId = "ContinueBatchButton", Text = "Continue" };
        button.Clicked += (_, _) => invoked = true;
        var app = new Application();
        var window = new Window(new ContentPage { Content = button });
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var buttonInfo = Assert.Single(
            Flatten(await client.GetTreeAsync()),
            element => element.AutomationId == "ContinueBatchButton");
        var result = await client.BatchAsync(
            [
                new JsonObject { ["action"] = "unsupported" },
                new JsonObject
                {
                    ["action"] = "tap",
                    ["elementId"] = buttonInfo.Id
                }
            ],
            continueOnError: true,
            captureEpoch: buttonInfo.CaptureEpoch,
            registryGeneration: buttonInfo.RegistryGeneration);

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.True(invoked);
        var results = result.GetProperty("results");
        Assert.Equal(2, results.GetArrayLength());
        Assert.False(results[0].GetProperty("success").GetBoolean());
        Assert.True(results[1].GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Screenshot_RegisteredNativeElement_UsesNativeCaptureHook()
    {
        var port = GetFreePort();
        var registry = new RegisteredNativeElementRegistry();
        var owner = new ToolbarItem { Text = "Native" };
        var nativeElement = new object();
        var nativeId = registry.Register(owner, nativeElement, "ToolbarItem");
        using var service = new NativeScreenshotAgentService(
            new AgentOptions { Port = port },
            registry);
        using var client = new AgentClient("localhost", port);
        var page = new ContentPage();
        page.ToolbarItems.Add(owner);
        var app = new Application();
        var window = new Window(page);
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var result = await client.ScreenshotResultAsync(elementId: nativeId);

        Assert.True(result.Success);
        Assert.Equal(NativeScreenshotPng, result.Data);
        Assert.Same(nativeElement, service.CapturedNativeElement);
    }

    [Fact]
    public async Task Screenshot_DetachedNativeElement_DoesNotUseUiDispatcher()
    {
        var port = GetFreePort();
        var dispatcher = new RejectingDispatcher();
        using var service = new DetachedNativeScreenshotAgentService(
            new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var app = new Application();
        var window = new Window(new ContentPage());
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        service.StartServerOnly(dispatcher);
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));
        dispatcher.RejectDispatch = true;

        var result = await client.ScreenshotResultAsync(
            elementId: DetachedNativeTreeWalker.ElementId);

        Assert.True(
            result.Success,
            $"Screenshot failed: {result.Error}; reason={result.Reason}");
        Assert.Equal(NativeScreenshotPng, result.Data);
        Assert.Same(service.Walker.NativeElement, service.CapturedNativeElement);
        Assert.Equal(0, dispatcher.RejectedDispatchCount);
    }

    [Fact]
    public async Task Screenshot_FreshElementCapture_ReturnedEpochCanCaptureSameElementAgain()
    {
        var port = GetFreePort();
        var registry = new RegisteredNativeElementRegistry();
        var owner = new ToolbarItem { Text = "Reusable native screenshot" };
        var nativeElement = new object();
        var nativeId = registry.Register(owner, nativeElement, "ToolbarItem");
        using var service = new NativeScreenshotAgentService(
            new AgentOptions { Port = port },
            registry);
        using var client = new AgentClient("localhost", port);
        var page = new ContentPage();
        page.ToolbarItems.Add(owner);
        var app = new Application();
        var window = new Window(page);
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        var first = await client.ScreenshotResultAsync(elementId: nativeId);
        Assert.True(
            first.Success,
            $"Screenshot failed: {first.Error}; reason={first.Reason}");
        Assert.True(first.CaptureEpoch > 0);

        var second = await client.ScreenshotResultAsync(
            window: null,
            elementId: nativeId,
            selector: null,
            maxWidth: null,
            scale: null,
            captureEpoch: first.CaptureEpoch,
            registryGeneration: first.RegistryGeneration);

        Assert.True(
            second.Success,
            $"Screenshot failed: {second.Error}; reason={second.Reason}");
        Assert.Same(nativeElement, service.CapturedNativeElement);
    }

    [Fact]
    public async Task NativeProbe_WhenPreviousProbeTimedOut_DoesNotStartAnotherWorker()
    {
        var port = GetFreePort();
        using var service = new BlockingNativeProbeAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);
        var app = new Application();
        var window = new Window(new ContentPage
        {
            Content = new Label { Text = "Managed tree remains available" }
        });
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);
        Assert.NotNull(await WaitForStatusAsync(client));

        try
        {
            Assert.NotEmpty(await client.GetTreeAsync());
            await service.Walker.ProbeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotEmpty(await client.GetTreeAsync());

            Assert.Equal(1, service.Walker.InvocationCount);
            Assert.Equal(1, service.Walker.MaxConcurrentInvocations);
        }
        finally
        {
            service.Walker.ReleaseProbe.Set();
        }
    }

    [Fact]
    public void StartServerOnly_RejectsDuplicateExtensionNamespace()
    {
        var options = new AgentOptions { Port = GetFreePort() };
        options.RegisterExtension("com.example.diagnostics", "First");
        options.RegisterExtension("com.example.diagnostics", "Second");

        Assert.Throws<InvalidOperationException>(() => new MauiDevFlowAgentService(options));
    }

    private static async Task<AgentStatus?> WaitForStatusAsync(AgentClient client)
    {
        for (int i = 0; i < 10; i++)
        {
            var status = await client.GetStatusAsync();
            if (status != null)
                return status;

            await Task.Delay(100);
        }

        return null;
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static IEnumerable<DriverElementInfo> Flatten(IEnumerable<DriverElementInfo> elements)
    {
        foreach (var element in elements)
        {
            yield return element;
            if (element.Children is null)
                continue;

            foreach (var child in Flatten(element.Children))
                yield return child;
        }
    }

    private sealed class ListOnlyJobsAgentService(AgentOptions options) : MauiDevFlowAgentService(options)
    {
        protected override bool IsJobsSupported => true;

        protected override bool IsJobRunSupported => false;
    }

    private sealed class UiOperationProbeAgentService(AgentOptions options) : MauiDevFlowAgentService(options)
    {
        public void ReportSuccessfulUiOperation()
            => PublishUiOperationSpan(
                "ui.input.physical",
                DateTime.UtcNow,
                success: true);
    }

    private sealed class NativeScreenshotAgentService : MauiDevFlowAgentService
    {
        public NativeScreenshotAgentService(
            AgentOptions options,
            RegisteredNativeElementRegistry registry)
            : base(options, registry, nativeElementSubscription: null)
        {
        }

        public object? CapturedNativeElement { get; private set; }

        protected override VisualTreeWalker CreateTreeWalker()
            => new(NativeElementRegistry!);

        protected override Task<byte[]?> CaptureNativeElementScreenshotAsync(
            object nativeElement,
            Microsoft.Maui.DevFlow.Agent.Core.ElementInfo? elementInfo)
        {
            CapturedNativeElement = nativeElement;
            return Task.FromResult<byte[]?>(NativeScreenshotPng);
        }
    }

    /// <summary>
    /// A registry-backed agent whose tree walker actually consumes that registry.
    /// </summary>
    /// <remarks>
    /// The base <c>MauiDevFlowAgentService.CreateTreeWalker()</c> deliberately returns a bare
    /// <c>new VisualTreeWalker()</c> — wiring a registry into the walker is a platform backend's
    /// job (see <c>Agent</c>/<c>Agent.Gtk</c>/<c>Agent.WPF</c>'s own <c>AgentServiceExtensions</c>).
    /// A registry passed to <see cref="MauiDevFlowAgentService"/>'s internal constructor without a
    /// matching <c>CreateTreeWalker()</c> override lets <c>NativeElementRegistry</c>/property
    /// endpoints resolve entries directly, but <c>ui.tree</c>/<c>ui.elements</c> never surface them,
    /// because <see cref="VisualTreeWalker.SupportsNativeElements"/>'s registered-element merge
    /// requires the walker instance itself to hold the registry. This stands in for that wiring.
    /// </remarks>
    private sealed class RegistryBackedAgentService : MauiDevFlowAgentService
    {
        private AffinityTrackingVisualTreeWalker? _walker;

        public RegistryBackedAgentService(
            AgentOptions options,
            RegisteredNativeElementRegistry registry,
            IDisposable? nativeElementSubscription)
            : base(options, registry, nativeElementSubscription)
        {
        }

        public bool WindowResolutionWasDispatched
            => _walker?.WindowResolutionWasDispatched == true;

        protected override VisualTreeWalker CreateTreeWalker()
            => _walker ??= new AffinityTrackingVisualTreeWalker(NativeElementRegistry!);
    }

    private sealed class AffinityTrackingVisualTreeWalker(
        RegisteredNativeElementRegistry registry) : VisualTreeWalker(registry)
    {
        public bool WindowResolutionWasDispatched { get; private set; }

        public override int? GetRegisteredNativeWindowId(string id, Application app)
        {
            WindowResolutionWasDispatched = AsyncDispatchRequiredDispatcher.IsExecutingDispatch;
            return base.GetRegisteredNativeWindowId(id, app);
        }
    }

    private sealed class DetachedNativeScreenshotAgentService : MauiDevFlowAgentService
    {
        private DetachedNativeTreeWalker? _walker;

        public DetachedNativeScreenshotAgentService(AgentOptions options)
            : base(options)
        {
        }

        public DetachedNativeTreeWalker Walker => _walker!;

        public object? CapturedNativeElement { get; private set; }

        protected override VisualTreeWalker CreateTreeWalker()
            => _walker ??= new DetachedNativeTreeWalker();

        protected override bool IsMainThreadDispatchRequired() => false;

        protected override Task<byte[]?> CaptureNativeElementScreenshotAsync(
            object nativeElement,
            Microsoft.Maui.DevFlow.Agent.Core.ElementInfo? elementInfo)
        {
            CapturedNativeElement = nativeElement;
            return Task.FromResult<byte[]?>(NativeScreenshotPng);
        }
    }

    private sealed class DetachedNativeTreeWalker : VisualTreeWalker
    {
        public const string ElementId = "native:detached:test";

        public object NativeElement { get; } = new();

        public override bool SupportsNativeElements => true;

        public override object? GetNativeElementById(string id)
            => id == ElementId ? NativeElement : null;

        public override Microsoft.Maui.DevFlow.Agent.Core.ElementInfo? GetNativeElementInfoById(
            string id)
            => id == ElementId
                ? new Microsoft.Maui.DevFlow.Agent.Core.ElementInfo
                {
                    Id = ElementId,
                    Type = "Button",
                    Framework = "test-native",
                    NativeProperties = new Dictionary<string, string?>
                    {
                        ["displayDensity"] = "1"
                    }
                }
                : null;
    }

    private sealed class BlockingNativeProbeAgentService : MauiDevFlowAgentService
    {
        private BlockingNativeTreeWalker? _walker;

        public BlockingNativeProbeAgentService(AgentOptions options)
            : base(options)
        {
        }

        public BlockingNativeTreeWalker Walker => _walker!;

        protected override VisualTreeWalker CreateTreeWalker()
            => _walker ??= new BlockingNativeTreeWalker();
    }

    private sealed class BlockingNativeTreeWalker : VisualTreeWalker
    {
        private int _concurrentInvocations;
        private int _invocationCount;
        private int _maxConcurrentInvocations;

        public override bool SupportsNativeElements => true;
        public TaskCompletionSource ProbeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ReleaseProbe { get; } = new(initialState: false);
        public int InvocationCount => Volatile.Read(ref _invocationCount);
        public int MaxConcurrentInvocations => Volatile.Read(ref _maxConcurrentInvocations);

        public override List<Microsoft.Maui.DevFlow.Agent.Core.ElementInfo> WalkNativeTree(
            IReadOnlyList<IntPtr> knownWindowHandles,
            int maxDepth = 0)
        {
            Interlocked.Increment(ref _invocationCount);
            var concurrent = Interlocked.Increment(ref _concurrentInvocations);
            UpdateMaximum(ref _maxConcurrentInvocations, concurrent);
            ProbeStarted.TrySetResult();
            try
            {
                ReleaseProbe.Wait(TimeSpan.FromSeconds(10));
                return [];
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentInvocations);
            }
        }

        private static void UpdateMaximum(ref int location, int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref location);
                if (current >= value
                    || Interlocked.CompareExchange(ref location, value, current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class DispatchProbeAgentService : MauiDevFlowAgentService
    {
        private readonly bool _mainThreadDispatchRequired;

        public DispatchProbeAgentService(IDispatcher dispatcher, bool mainThreadDispatchRequired)
        {
            _dispatcher = ToAgentDispatcher(dispatcher);
            _mainThreadDispatchRequired = mainThreadDispatchRequired;
        }

        public int MainThreadFallbackCallCount { get; private set; }

        public bool IsInsideMainThreadFallback { get; private set; }

        public Task<string> RunDispatchAsync(Func<string> func) => DispatchAsync(func);

        public Task<string?> RunDispatchAsync(Func<Task<string?>> func) => DispatchAsync(func);

        protected override bool IsMainThreadDispatchRequired() => _mainThreadDispatchRequired;

        protected override Task<T> DispatchViaMainThreadAsync<T>(Func<T> func)
        {
            MainThreadFallbackCallCount++;
            return RunInsideMainThreadFallbackAsync(func);
        }

        protected override Task<T?> DispatchViaMainThreadAsync<T>(Func<Task<T?>> func) where T : class
        {
            MainThreadFallbackCallCount++;
            return RunInsideMainThreadFallbackAsync(func);
        }

        private Task<T> RunInsideMainThreadFallbackAsync<T>(Func<T> func)
        {
            var wasInsideMainThreadFallback = IsInsideMainThreadFallback;
            IsInsideMainThreadFallback = true;
            try
            {
                return Task.FromResult(func());
            }
            finally
            {
                IsInsideMainThreadFallback = wasInsideMainThreadFallback;
            }
        }

        private async Task<T?> RunInsideMainThreadFallbackAsync<T>(Func<Task<T?>> func) where T : class
        {
            var wasInsideMainThreadFallback = IsInsideMainThreadFallback;
            IsInsideMainThreadFallback = true;
            try
            {
                return await func();
            }
            finally
            {
                IsInsideMainThreadFallback = wasInsideMainThreadFallback;
            }
        }
    }

    private sealed class ImmediateDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;

        public bool Dispatch(Action action)
        {
            action();
            return true;
        }

        public bool DispatchDelayed(TimeSpan delay, Action action)
        {
            action();
            return true;
        }

        public IDispatcherTimer CreateTimer() => new ImmediateDispatcherTimer();
    }

    private sealed class AsyncDispatchRequiredDispatcher : IDispatcher
    {
        private static readonly AsyncLocal<bool> InsideDispatch = new();

        public static bool IsExecutingDispatch => InsideDispatch.Value;

        public bool IsDispatchRequired => true;

        public bool Dispatch(Action action)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                InsideDispatch.Value = true;
                try
                {
                    action();
                }
                finally
                {
                    InsideDispatch.Value = false;
                }
            });
            return true;
        }

        public bool DispatchDelayed(TimeSpan delay, Action action) => Dispatch(action);

        public IDispatcherTimer CreateTimer() => new ImmediateDispatcherTimer();
    }

    private sealed class RejectingDispatcher : IDispatcher
    {
        public bool RejectDispatch { get; set; }

        public int RejectedDispatchCount { get; private set; }

        public bool IsDispatchRequired => RejectDispatch;

        public bool Dispatch(Action action)
        {
            if (RejectDispatch)
            {
                RejectedDispatchCount++;
                throw new InvalidOperationException("UI dispatcher is blocked.");
            }

            action();
            return true;
        }

        public bool DispatchDelayed(TimeSpan delay, Action action)
            => Dispatch(action);

        public IDispatcherTimer CreateTimer() => new ImmediateDispatcherTimer();
    }

    private sealed class NativePropertyTarget
    {
        public bool IsEnabled { get; set; }
    }

    private sealed class ImmediateDispatcherTimer : IDispatcherTimer
    {
        public bool IsRepeating { get; set; }
        public TimeSpan Interval { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick
        {
            add { }
            remove { }
        }

        public void Start()
        {
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
        }
    }
}
