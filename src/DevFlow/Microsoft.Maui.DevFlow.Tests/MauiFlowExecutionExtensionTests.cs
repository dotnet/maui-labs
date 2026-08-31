using System.Text.Json;
using Microsoft.Maui.DevFlow.Devices;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;
using DeviceFlowExecutionExtension = Microsoft.Maui.Cli.DevFlow.Flows.DeviceFlowExecutionExtension;
using DeviceFlowReview = Microsoft.Maui.Cli.DevFlow.Flows.DeviceFlowReview;
using InspectorServer = Microsoft.Maui.Cli.DevFlow.Inspector.InspectorServer;

namespace Microsoft.Maui.DevFlow.Tests;

public class MauiFlowExecutionExtensionTests
{
    private sealed class ObservationCapture : IFlowRunEvidenceCapture
    {
        public int BeginCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public int ExplainCalls { get; private set; }
        public int CaptureCalls { get; private set; }
        public MauiFlowArtifactReference? CapturedArtifact { get; private set; }
        public IReadOnlyCollection<string> CapturedEvidenceKinds =>
            CapturedArtifact is null ? [] : [MauiFlowEvidenceKinds.DeviceRecording];

        public Task<IAsyncDisposable?> BeginRunAsync(MauiFlow flow, CancellationToken cancellationToken)
        {
            BeginCalls++;
            return Task.FromResult<IAsyncDisposable?>(new Handle(this));
        }

        public Task<string?> ExplainFailureAsync(
            MauiFlow flow,
            FlowStep failedStep,
            FlowStepResult result,
            CancellationToken cancellationToken)
        {
            ExplainCalls++;
            return Task.FromResult<string?>("A native dialog covered the app.");
        }

        public Task CaptureOnFailureAsync(
            MauiFlow flow,
            FlowStep failedStep,
            FlowStepResult result,
            CancellationToken cancellationToken)
        {
            CaptureCalls++;
            return Task.CompletedTask;
        }

        public Task CaptureOnRunFailureAsync(
            MauiFlowRunEvidenceContext context,
            CancellationToken cancellationToken)
        {
            CaptureCalls++;
            return Task.CompletedTask;
        }

        private sealed class Handle(ObservationCapture owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.DisposeCalls++;
                owner.CapturedArtifact = new MauiFlowArtifactReference
                {
                    ArtifactId = "device-recording-test",
                    Kind = MauiFlowEvidenceKinds.DeviceRecording,
                };
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class MissingElementDriver : IMauiFlowDriver
    {
        public WorkflowCommandReceipt? LastWorkflowCommandReceipt => null;
        public Task<List<ElementInfo>> QueryAsync(string? type = null, string? automationId = null, string? text = null)
            => Task.FromResult(new List<ElementInfo>());
        public Task<ElementInfo?> GetElementAsync(string id) => Task.FromResult<ElementInfo?>(null);
        public Task<bool> TapAsync(string elementId) => Task.FromResult(false);
        public Task<bool> FillAsync(string elementId, string text) => Task.FromResult(false);
        public Task<bool> SetPropertyAsync(string elementId, string propertyName, string value) => Task.FromResult(false);
        public Task<bool> ScrollAsync(string? elementId = null, double deltaX = 0, double deltaY = 0, bool animated = true, int? itemIndex = null, string? scrollToPosition = null) => Task.FromResult(false);
        public Task<bool> NavigateAsync(string route) => Task.FromResult(false);
        public Task<bool> BackAsync() => Task.FromResult(false);
        public Task<ThemeResult> SetThemeAsync(DevFlowTheme theme) => Task.FromResult(new ThemeResult { Success = false });
        public Task<string?> GetPropertyAsync(string elementId, string propertyName) => Task.FromResult<string?>(null);
        public Task<AgentStatus?> GetStatusAsync() => Task.FromResult<AgentStatus?>(new AgentStatus());
    }

    private sealed class RecordingSurface : IDeviceSurface
    {
        public List<string> Calls { get; } = [];
        public DeviceOperationResult UiResult { get; set; } = DeviceOperationResult.Ok();

        public Task<DeviceHostHealth> GetHealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new DeviceHostHealth { Availability = DeviceHostAvailability.Available });
        public Task<IReadOnlyList<DeviceTarget>?> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DeviceTarget>?>([]);
        public Task<DeviceTarget?> GetAsync(string deviceId, CancellationToken cancellationToken = default)
            => Task.FromResult<DeviceTarget?>(null);
        public Task<DeviceOperationResult> BootAsync(string deviceId, CancellationToken cancellationToken = default)
            => Task.FromResult(DeviceOperationResult.Ok());
        public Task<DeviceOperationResult> ShutdownAsync(string deviceId, CancellationToken cancellationToken = default)
            => Task.FromResult(DeviceOperationResult.Ok());
        public Task<DeviceOperationResult> TapAsync(string deviceId, DevicePoint point, CancellationToken cancellationToken = default)
        {
            Calls.Add($"tap:{point.X},{point.Y}");
            return Task.FromResult(DeviceOperationResult.Ok());
        }
        public Task<byte[]?> ScreenshotAsync(string deviceId, CancellationToken cancellationToken = default)
            => Task.FromResult<byte[]?>(null);
        public Task<DeviceOperationResult> SetPermissionAsync(
            string deviceId,
            string appPackageId,
            string permission,
            string state,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"permission:{appPackageId}:{permission}:{state}");
            return Task.FromResult(DeviceOperationResult.Ok());
        }
        public Task<DeviceOperationResult> SetBatteryAsync(string deviceId, int percentage, CancellationToken cancellationToken = default)
        {
            Calls.Add($"battery:{percentage}");
            return Task.FromResult(DeviceOperationResult.Ok());
        }
        public Task<DeviceOperationResult> TapUiAsync(
            string deviceId,
            string? nativeId,
            string? text,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"ui:{nativeId ?? text}");
            return Task.FromResult(UiResult);
        }
    }

    [Fact]
    public async Task CanonicalRunner_ObservesExplainsCapturesAndDisposes()
    {
        var capture = new ObservationCapture();
        var flow = TapFlow();

        var result = await new MauiFlowRunner(
            new MissingElementDriver(),
            new MauiFlowRunnerOptions { PollTries = 1, PollGapMs = 0 },
            capture).RunWithLegacyAsync(flow);

        Assert.False(result.LegacyReport.Ok);
        Assert.Equal("A native dialog covered the app.", Assert.Single(result.LegacyReport.Results).FailureCause);
        Assert.Equal(1, capture.BeginCalls);
        Assert.Equal(1, capture.ExplainCalls);
        Assert.Equal(1, capture.CaptureCalls);
        Assert.Equal(1, capture.DisposeCalls);
        Assert.Contains(
            result.Report.Artifacts,
            artifact => artifact.Kind == MauiFlowEvidenceKinds.DeviceRecording);
    }

    [Fact]
    public async Task CanonicalRunner_RejectsDeclaredDeviceWorkWithoutAHostExtension()
    {
        var flow = TapFlow();
        flow.ExtensionData = new Dictionary<string, JsonElement>
        {
            [DeviceStep.ExtensionKey] = JsonDocument.Parse("[]").RootElement.Clone(),
        };

        var result = await new MauiFlowRunner(
            new MissingElementDriver(),
            new MauiFlowRunnerOptions { PollTries = 1, PollGapMs = 0 }).RunWithLegacyAsync(flow);

        Assert.False(result.LegacyReport.Ok);
        Assert.Contains("paired device", Assert.Single(result.LegacyReport.Results).Error);
    }

    [Fact]
    public async Task DeviceExtension_AppliesPreconditionsAndSemanticStepsInOrder()
    {
        var surface = new RecordingSurface();
        var flow = TapFlow();
        flow.ExtensionData = new Dictionary<string, JsonElement>
        {
            [DevicePreconditions.ExtensionKey] = JsonDocument.Parse(
                """{"permissions":{"camera":"denied"},"battery":5}""").RootElement.Clone(),
            [DeviceStep.ExtensionKey] = JsonDocument.Parse(
                """
                [{"afterStep":0,"action":"tap","x":10,"y":20,"nativeId":"allow"},
                 {"afterStep":1,"action":"tap","x":30,"y":40,"nativeText":"Continue"}]
                """).RootElement.Clone(),
        };
        var extension = new DeviceFlowExecutionExtension(
            surface,
            new DeviceTarget
            {
                Id = "android:emulator:Pixel_8",
                Platform = DevicePlatforms.Android,
                Name = "Pixel 8",
                State = DeviceStates.Booted,
            },
            "com.example.app");

        var prepared = await extension.PrepareAsync(flow, CancellationToken.None);
        var after = await extension.AfterStepAsync(
            flow,
            flow.Steps[0],
            new FlowStepResult { Ok = true },
            CancellationToken.None);

        Assert.True(prepared.Success, prepared.Error);
        Assert.True(after.Success, after.Error);
        Assert.Equal(
            ["permission:com.example.app:camera:denied", "battery:5", "ui:allow", "ui:Continue"],
            surface.Calls);
    }

    [Fact]
    public async Task DeviceExtension_RejectsMalformedDeclaredSteps()
    {
        var flow = TapFlow();
        flow.ExtensionData = new Dictionary<string, JsonElement>
        {
            [DeviceStep.ExtensionKey] = JsonDocument.Parse("{}").RootElement.Clone(),
        };
        var extension = new DeviceFlowExecutionExtension(new RecordingSurface(), device: null, appPackageId: null);

        var result = await extension.PrepareAsync(flow, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(FlowFailureKinds.Validation, result.FailureKind);
    }

    [Fact]
    public async Task DeviceExtension_RejectsNullDeclaredPreconditions()
    {
        var flow = TapFlow();
        flow.ExtensionData = new Dictionary<string, JsonElement>
        {
            [DevicePreconditions.ExtensionKey] = JsonDocument.Parse("null").RootElement.Clone(),
        };
        var extension = new DeviceFlowExecutionExtension(new RecordingSurface(), device: null, appPackageId: null);

        var result = await extension.PrepareAsync(flow, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(FlowFailureKinds.Validation, result.FailureKind);
        Assert.Contains("non-null object", result.Error);
    }

    [Fact]
    public async Task DeviceExtension_DoesNotCoordinateFallbackFromAnAmbiguousNativeSelector()
    {
        var surface = new RecordingSurface
        {
            UiResult = DeviceOperationResult.Ambiguous("The native accessibility selector is ambiguous."),
        };
        var flow = TapFlow();
        flow.ExtensionData = new Dictionary<string, JsonElement>
        {
            [DeviceStep.ExtensionKey] = JsonDocument.Parse(
                """[{"afterStep":0,"action":"tap","x":10,"y":20,"nativeId":"duplicate"}]""").RootElement.Clone(),
        };
        var extension = new DeviceFlowExecutionExtension(
            surface,
            new DeviceTarget
            {
                Id = "android:emulator:Pixel_8",
                Platform = DevicePlatforms.Android,
                Name = "Pixel 8",
                State = DeviceStates.Booted,
            },
            "com.example.app");

        var result = await extension.PrepareAsync(flow, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("ambiguous", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(surface.Calls, call => call.StartsWith("tap:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeviceExtension_DoesNotCoordinateFallbackAfterUnknownNativeCompletion()
    {
        var surface = new RecordingSurface
        {
            UiResult = DeviceOperationResult.UnknownCompletion(
                "The device host stopped responding before completion could be confirmed."),
        };
        var flow = TapFlow();
        flow.ExtensionData = new Dictionary<string, JsonElement>
        {
            [DeviceStep.ExtensionKey] = JsonDocument.Parse(
                """[{"afterStep":0,"action":"tap","x":10,"y":20,"nativeId":"allow"}]""").RootElement.Clone(),
        };
        var extension = new DeviceFlowExecutionExtension(
            surface,
            new DeviceTarget
            {
                Id = "android:emulator:Pixel_8",
                Platform = DevicePlatforms.Android,
                Name = "Pixel 8",
                State = DeviceStates.Booted,
            },
            "com.example.app");

        var result = await extension.PrepareAsync(flow, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("could not safely fall back", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(surface.Calls, call => call.StartsWith("tap:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeviceExtension_CoordinateFallbackRequiresConfirmedNativeNotFound()
    {
        var surface = new RecordingSurface
        {
            UiResult = DeviceOperationResult.NotFound(
                "The native accessibility selector did not match an element."),
        };
        var flow = TapFlow();
        flow.ExtensionData = new Dictionary<string, JsonElement>
        {
            [DeviceStep.ExtensionKey] = JsonDocument.Parse(
                """[{"afterStep":0,"action":"tap","x":10,"y":20,"nativeId":"allow"}]""").RootElement.Clone(),
        };
        var extension = new DeviceFlowExecutionExtension(
            surface,
            new DeviceTarget
            {
                Id = "android:emulator:Pixel_8",
                Platform = DevicePlatforms.Android,
                Name = "Pixel 8",
                State = DeviceStates.Booted,
            },
            "com.example.app");

        var result = await extension.PrepareAsync(flow, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["ui:allow", "tap:10,20"], surface.Calls);
    }

    [Fact]
    public void InspectorReviewDigest_BindsTheExactDeviceMutations()
    {
        var flow = TapFlow();
        flow.ExtensionData = new Dictionary<string, JsonElement>
        {
            [DeviceStep.ExtensionKey] = JsonDocument.Parse(
                """[{"afterStep":1,"action":"tap","x":10,"y":20,"nativeId":"allow"}]""").RootElement.Clone(),
        };
        var review = DeviceFlowReview.Describe(flow);

        Assert.True(review.Valid, review.Error);
        Assert.Single(review.Effects);
        Assert.NotNull(InspectorServer.ValidateReviewedDeviceEffects(flow, reviewedDigest: null));
        Assert.Null(InspectorServer.ValidateReviewedDeviceEffects(flow, review.Digest));

        flow.ExtensionData[DeviceStep.ExtensionKey] = JsonDocument.Parse(
            """[{"afterStep":1,"action":"tap","x":30,"y":40,"nativeId":"allow"}]""").RootElement.Clone();
        Assert.NotNull(InspectorServer.ValidateReviewedDeviceEffects(flow, review.Digest));
    }

    [Fact]
    public void InspectorReviewDigest_BindsDeviceRecordingConsent()
    {
        var flow = TapFlow();
        var withoutRecording = DeviceFlowReview.Describe(flow, includeDeviceRecording: false);
        var withRecording = DeviceFlowReview.Describe(flow, includeDeviceRecording: true);

        Assert.Empty(withoutRecording.Effects);
        Assert.False(withoutRecording.RecordDeviceRun);
        Assert.Single(withRecording.Effects);
        Assert.True(withRecording.RecordDeviceRun);
        Assert.NotEqual(withoutRecording.Digest, withRecording.Digest);
        Assert.NotNull(InspectorServer.ValidateReviewedDeviceEffects(
            flow,
            withoutRecording.Digest,
            includeDeviceRecording: true));
        Assert.Null(InspectorServer.ValidateReviewedDeviceEffects(
            flow,
            withRecording.Digest,
            includeDeviceRecording: true));
    }

    private static MauiFlow TapFlow() => new()
    {
        Name = "tap",
        Steps =
        [
            new FlowStep
            {
                Seq = 1,
                Action = FlowActions.Tap,
                Args = new FlowStepArgs
                {
                    Selector = new FlowSelector { AutomationId = "submit" },
                },
            },
        ],
    };
}
