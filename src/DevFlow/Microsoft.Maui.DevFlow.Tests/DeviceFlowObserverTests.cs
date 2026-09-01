using Microsoft.Maui.Cli.DevFlow.Flows;
using Microsoft.Maui.DevFlow.Devices;
using Microsoft.Maui.DevFlow.Testing;
// Cli.DevFlow.Flows declares its own flow type family (FlowCompatibility.cs), so the Testing types
// the replay seam is defined against must be named explicitly.
using MauiFlow = Microsoft.Maui.DevFlow.Testing.MauiFlow;
using FlowStep = Microsoft.Maui.DevFlow.Testing.FlowStep;
using FlowStepResult = Microsoft.Maui.DevFlow.Testing.FlowStepResult;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The device layer's contribution to a flow replay: a recording of the run, and a cause for a
/// step that could not be actioned.
/// <para>
/// The behaviour worth defending is restraint. An explanation is only offered for failures a
/// foreign window could actually account for, because sending a reader to investigate a healthy
/// environment while the real cause — usually a selector — goes unexamined is worse than saying
/// nothing.
/// </para>
/// </summary>
public class DeviceFlowObserverTests
{
    private sealed class StubSurface : IDeviceSurface, IDeviceRecordingPathAuthority
    {
        public bool RecordingSupported { get; set; } = true;
        public string? Foreground { get; set; }
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int? RecordingTimeoutSeconds { get; private set; }
        public string? StoppedPath { get; set; } = "/tmp/run.mp4";

        /// <summary>
        /// The directory this surface would have told a host to write into. Owning a root is what
        /// entitles a surface to say a reported path is one of its recordings, so the stub owns one
        /// too rather than borrowing another implementation's.
        /// </summary>
        public string RecordingRoot { get; } = Path.Combine(
            Path.GetTempPath(),
            "devflow-observer-tests",
            Guid.NewGuid().ToString("N"));

        public string? ResolveContainedRecordingPath(string? reported) =>
            DeviceRecordingPathGuard.Resolve(reported, RecordingRoot);

        public Task<DeviceHostHealth> GetHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceHostHealth { Availability = DeviceHostAvailability.Available });
        public Task<IReadOnlyList<DeviceTarget>?> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceTarget>?>([]);
        public Task<DeviceTarget?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<DeviceTarget?>(null);
        public Task<DeviceOperationResult> BootAsync(string id, CancellationToken ct = default) => Task.FromResult(DeviceOperationResult.Ok());
        public Task<DeviceOperationResult> ShutdownAsync(string id, CancellationToken ct = default) => Task.FromResult(DeviceOperationResult.Ok());
        public Task<DeviceOperationResult> TapAsync(string id, DevicePoint p, CancellationToken ct = default) => Task.FromResult(DeviceOperationResult.Ok());
        public Task<byte[]?> ScreenshotAsync(string id, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);

        public Task<DeviceOperationResult> StartRecordingAsync(string id, int timeoutSeconds = 180, CancellationToken ct = default)
        {
            StartCalls++;
            RecordingTimeoutSeconds = timeoutSeconds;
            return Task.FromResult(RecordingSupported
                ? DeviceOperationResult.Ok()
                : DeviceOperationResult.Unsupported("screen recording"));
        }

        public Task<DeviceRecordingResult> StopRecordingAsync(string id, CancellationToken ct = default)
        {
            StopCalls++;
            return Task.FromResult(new DeviceRecordingResult(StoppedPath is not null, StoppedPath));
        }

        public Task<string?> DescribeForegroundAsync(string id, string? appPackageId = null, CancellationToken ct = default) =>
            Task.FromResult(Foreground);
    }

    private static FlowStepResult Failed(string kind) =>
        new() { Ok = false, FailureKind = kind, Error = "step failed" };

    [Fact]
    public async Task RecordsTheRun_AndStopsWhenItEnds()
    {
        // A failed test that ships with video of its own failure is the most useful artifact a
        // replay can produce: the interesting moment has passed by the time anyone reads a report.
        // The file is placed inside the surface's own recording root, because the digest is only
        // produced for a path that surface will vouch for.
        var surface = new StubSurface();
        Directory.CreateDirectory(surface.RecordingRoot);
        var path = Path.Combine(
            surface.RecordingRoot,
            $"devflow-recording-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        surface.StoppedPath = path;
        DeviceRecordingCapture? recorded = null;
        var observer = new DeviceFlowObserver(surface, "ios:A1B2", onRecording: c => recorded = c);

        try
        {
            var handle = await observer.BeginRunAsync(new MauiFlow(), CancellationToken.None);
            Assert.NotNull(handle);
            await handle!.DisposeAsync();

            Assert.Equal(1, surface.StartCalls);
            Assert.Equal(1, surface.StopCalls);
            Assert.Equal(DeviceRecordingPathGuard.ResolveFinalPath(path), recorded?.Path);
            // The capture carries the surface that vouched, not a root the next hop has to guess.
            Assert.Same(surface, recorded?.Authority);
            Assert.Equal(MauiFlowEvidenceKinds.DeviceRecording, observer.CapturedArtifact?.Kind);
            Assert.Equal("artifacts/device-recording.mp4", observer.CapturedArtifact?.Path);
            Assert.False(Path.IsPathRooted(observer.CapturedArtifact?.Path));
            Assert.Contains(MauiFlowEvidenceKinds.DeviceRecording, observer.CapturedEvidenceKinds);
        }
        finally
        {
            Directory.Delete(surface.RecordingRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ADeviceThatCannotRecordDoesNotBlockTheRun()
    {
        var surface = new StubSurface { RecordingSupported = false };
        var observer = new DeviceFlowObserver(surface, "ios:A1B2");

        var handle = await observer.BeginRunAsync(new MauiFlow(), CancellationToken.None);

        Assert.Null(handle);
        Assert.Equal(0, surface.StopCalls);
    }

    [Fact]
    public async Task RecordingTimeout_CoversTheConfiguredWorkflowWindow()
    {
        var surface = new StubSurface { RecordingSupported = false };
        var observer = new DeviceFlowObserver(
            surface,
            "ios:A1B2",
            recordingTimeoutSeconds: 630);

        await observer.BeginRunAsync(new MauiFlow(), CancellationToken.None);

        Assert.Equal(630, surface.RecordingTimeoutSeconds);
    }

    [Fact]
    public async Task ExplainsAFailureAForeignWindowCouldCause()
    {
        var surface = new StubSurface { Foreground = "A system permission dialog" };
        var observer = new DeviceFlowObserver(surface, "ios:A1B2");

        var cause = await observer.ExplainFailureAsync(
            new MauiFlow(), new FlowStep(), Failed("not_visible"), CancellationToken.None);

        Assert.NotNull(cause);
        Assert.Contains("permission dialog", cause, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StaysSilentWhenTheAppIsFrontmost()
    {
        // Nothing was covering the app, so the failure is the app's own. Inventing environmental
        // context here would be a confident lie.
        var surface = new StubSurface { Foreground = null };
        var observer = new DeviceFlowObserver(surface, "ios:A1B2");

        var cause = await observer.ExplainFailureAsync(
            new MauiFlow(), new FlowStep(), Failed("not_visible"), CancellationToken.None);

        Assert.Null(cause);
    }

    [Theory]
    [InlineData("not_found")]
    [InlineData("ambiguous")]
    [InlineData("validation")]
    public async Task DoesNotBlameTheEnvironmentForAnAuthoringProblem(string kind)
    {
        // A selector that matched nothing, or matched too much, is not something a dialog caused.
        // Offering one would send the reader to investigate an environment that was fine.
        var surface = new StubSurface { Foreground = "A system permission dialog" };
        var observer = new DeviceFlowObserver(surface, "ios:A1B2");

        var cause = await observer.ExplainFailureAsync(
            new MauiFlow(), new FlowStep(), Failed(kind), CancellationToken.None);

        Assert.Null(cause);
    }

    [Fact]
    public async Task NeverExplainsASuccessfulStep()
    {
        var surface = new StubSurface { Foreground = "A share sheet" };
        var observer = new DeviceFlowObserver(surface, "ios:A1B2");

        var cause = await observer.ExplainFailureAsync(
            new MauiFlow(), new FlowStep(), new FlowStepResult { Ok = true }, CancellationToken.None);

        Assert.Null(cause);
    }

    [Theory]
    [InlineData("not_visible", true)]
    [InlineData("not_actionable", true)]
    [InlineData("timeout", true)]
    [InlineData("stale_element", true)]
    [InlineData("not_found", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ClassifiesWhichFailuresAForegroundCouldExplain(string? kind, bool explainable)
    {
        var result = new FlowStepResult { Ok = false, FailureKind = kind };

        Assert.Equal(explainable, DeviceFlowObserver.IsExplainableByForeground(result));
    }

    [Fact]
    public async Task DelegatesEvidenceCaptureToTheInnerObserver()
    {
        // A decorator, not a replacement: the existing evidence path keeps working untouched.
        var inner = new RecordingCapture();
        var observer = new DeviceFlowObserver(new StubSurface(), "ios:A1B2", inner);

        await observer.CaptureOnFailureAsync(
            new MauiFlow(), new FlowStep(), Failed("not_visible"), CancellationToken.None);

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task WorksWithNoInnerObserver()
    {
        var observer = new DeviceFlowObserver(new StubSurface(), "ios:A1B2");

        var exception = await Record.ExceptionAsync(() => observer.CaptureOnFailureAsync(
            new MauiFlow(), new FlowStep(), Failed("not_visible"), CancellationToken.None));

        Assert.Null(exception);
    }

    /// <summary>
    /// The inner observer has already opened its own capture by the time the device recording is
    /// attempted. If starting that recording throws, the caller never receives a handle, so the
    /// inner one has to be closed here or it stays open for the whole run.
    /// </summary>
    [Fact]
    public async Task DisposesTheInnerCaptureWhenTheDeviceRecordingFailsToStart()
    {
        var inner = new RecordingCapture { Handle = new TrackedHandle() };
        var surface = new ThrowingRecordingSurface();
        var observer = new DeviceFlowObserver(surface, "ios:A1B2", inner);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            observer.BeginRunAsync(new MauiFlow(), CancellationToken.None));

        Assert.True(inner.Handle.Disposed);
    }

    /// <summary>
    /// A refused start is not a failure — it just means this device cannot record — so the inner
    /// capture keeps running and is handed back to the caller intact.
    /// </summary>
    [Fact]
    public async Task KeepsTheInnerCaptureWhenTheDeviceMerelyCannotRecord()
    {
        var handle = new TrackedHandle();
        var inner = new RecordingCapture { Handle = handle };
        var observer = new DeviceFlowObserver(
            new StubSurface { RecordingSupported = false },
            "ios:A1B2",
            inner);

        var returned = await observer.BeginRunAsync(new MauiFlow(), CancellationToken.None);

        Assert.Same(handle, returned);
        Assert.False(handle.Disposed);
    }

    /// <summary>
    /// A stopped recording that the surface will not vouch for produces no artifact: a digest
    /// published as evidence has to be a digest of the recording.
    /// </summary>
    [Fact]
    public async Task PublishesNoArtifactForAnUncontainedRecordingPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"uncontained-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        var captures = new List<DeviceRecordingCapture>();
        var observer = new DeviceFlowObserver(
            new StubSurface { StoppedPath = path },
            "ios:A1B2",
            onRecording: captures.Add);

        try
        {
            var handle = await observer.BeginRunAsync(new MauiFlow(), CancellationToken.None);
            await handle!.DisposeAsync();

            Assert.Empty(captures);
            Assert.Null(observer.CapturedArtifact);
            Assert.DoesNotContain(MauiFlowEvidenceKinds.DeviceRecording, observer.CapturedEvidenceKinds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Containment is the recording surface's own judgement, not a fact about one implementation's
    /// directory. A surface that cannot make that judgement gets nothing at all — no digest, and no
    /// path passed onwards — because the alternative is validating its file against a root that
    /// nothing writes to, which is either a wrong refusal or, worse, an accidental acceptance.
    /// </summary>
    [Fact]
    public async Task PublishesNothingWhenTheSurfaceCannotVouchForItsOwnRecordings()
    {
        var root = Path.Combine(Path.GetTempPath(), "devflow-observer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "run.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        var surface = new UnvouchedRecordingSurface { StoppedPath = path };
        var captures = new List<DeviceRecordingCapture>();
        var observer = new DeviceFlowObserver(surface, "ios:A1B2", onRecording: captures.Add);

        try
        {
            var handle = await observer.BeginRunAsync(new MauiFlow(), CancellationToken.None);
            await handle!.DisposeAsync();

            // Nothing unverified reaches the run: no artifact to publish, and — the part that
            // matters downstream — no path for retention or the Inspector to copy and serve.
            Assert.Empty(captures);
            Assert.Null(observer.CapturedArtifact);
            Assert.DoesNotContain(MauiFlowEvidenceKinds.DeviceRecording, observer.CapturedEvidenceKinds);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The inversion, stated as a property rather than a story about one implementation: a file that
    /// is a genuine recording of surface A is not one of surface B's, so driving B and being handed
    /// A's path publishes nothing. Validating against "the" recording root — any single concrete
    /// root — would accept it, which is exactly the confusion two surfaces make possible.
    /// </summary>
    [Fact]
    public async Task OneSurfaceDoesNotVouchForAnotherSurfacesRecording()
    {
        var owning = new StubSurface();
        var driven = new StubSurface();
        Directory.CreateDirectory(owning.RecordingRoot);
        var path = Path.Combine(owning.RecordingRoot, "run.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        // The surface actually being driven reports a file that lives in the other one's root.
        driven.StoppedPath = path;
        var captures = new List<DeviceRecordingCapture>();
        var observer = new DeviceFlowObserver(driven, "ios:A1B2", onRecording: captures.Add);

        try
        {
            // The premise: this really is a valid recording — for the surface that owns it.
            Assert.NotNull(owning.ResolveContainedRecordingPath(path));

            var handle = await observer.BeginRunAsync(new MauiFlow(), CancellationToken.None);
            await handle!.DisposeAsync();

            Assert.Empty(captures);
            Assert.Null(observer.CapturedArtifact);
            Assert.DoesNotContain(MauiFlowEvidenceKinds.DeviceRecording, observer.CapturedEvidenceKinds);
        }
        finally
        {
            Directory.Delete(owning.RecordingRoot, recursive: true);
        }
    }

    /// <summary>
    /// And the same property the other way round: an alternate surface's own recording is published
    /// normally, carrying that surface as its authority. Nothing here is privileged to the one
    /// implementation the product ships.
    /// </summary>
    [Fact]
    public async Task AnAlternateSurfaceVouchesForItsOwnRecording()
    {
        var alternate = new StubSurface();
        Directory.CreateDirectory(alternate.RecordingRoot);
        var path = Path.Combine(alternate.RecordingRoot, "run.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        alternate.StoppedPath = path;
        var captures = new List<DeviceRecordingCapture>();
        var observer = new DeviceFlowObserver(alternate, "ios:A1B2", onRecording: captures.Add);

        try
        {
            var handle = await observer.BeginRunAsync(new MauiFlow(), CancellationToken.None);
            await handle!.DisposeAsync();

            var capture = Assert.Single(captures);
            Assert.Same(alternate, capture.Authority);
            Assert.Equal(DeviceRecordingPathGuard.ResolveFinalPath(path), capture.ResolveForRead());
            Assert.Equal(MauiFlowEvidenceKinds.DeviceRecording, observer.CapturedArtifact?.Kind);
        }
        finally
        {
            Directory.Delete(alternate.RecordingRoot, recursive: true);
        }
    }

    /// <summary>
    /// A capture is only as good as the moment it is opened. A directory swapped for a link out of
    /// the root between two hops — the TOCTOU shape the whole re-validation exists for — makes the
    /// authority stop vouching, and the consumer that re-checks before reading gets nothing.
    /// </summary>
    [Fact]
    public async Task ACaptureStopsResolvingOnceItsAuthorityNoLongerVouches()
    {
        var surface = new StubSurface();
        var nested = Path.Combine(surface.RecordingRoot, "nested");
        Directory.CreateDirectory(nested);
        var elsewhere = Path.Combine(
            Path.GetTempPath(),
            "devflow-observer-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(elsewhere);
        var path = Path.Combine(nested, "run.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(Path.Combine(elsewhere, "run.mp4"), [9, 9, 9, 9]);
        surface.StoppedPath = path;
        var captures = new List<DeviceRecordingCapture>();
        var observer = new DeviceFlowObserver(surface, "ios:A1B2", onRecording: captures.Add);

        try
        {
            var handle = await observer.BeginRunAsync(new MauiFlow(), CancellationToken.None);
            await handle!.DisposeAsync();
            var capture = Assert.Single(captures);
            Assert.NotNull(capture.ResolveForRead());

            // The directory the recording sat in becomes a link to a file of the same name outside
            // the root. Nothing about the captured path string changes.
            Directory.Delete(nested, recursive: true);
            FileSystemLinks.CreateDirectoryLink(nested, elsewhere);
            Assert.True(File.Exists(capture.Path));

            Assert.Null(capture.ResolveForRead());
        }
        finally
        {
            try { Directory.Delete(surface.RecordingRoot, recursive: true); } catch { }
            try { Directory.Delete(elsewhere, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Refusing to publish an unvouched recording must not strand the inner capture: the caller
    /// already handed us its handle, so bailing out early without closing it would leave a
    /// screenshot capture open for the rest of the run.
    /// </summary>
    [Fact]
    public async Task DisposesTheInnerCaptureWhenTheRecordingIsNotVouchedFor()
    {
        var handle = new TrackedHandle();
        var inner = new RecordingCapture { Handle = handle };
        var path = Path.Combine(Path.GetTempPath(), $"uncontained-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        var observer = new DeviceFlowObserver(
            new StubSurface { StoppedPath = path },
            "ios:A1B2",
            inner);

        try
        {
            var returned = await observer.BeginRunAsync(new MauiFlow(), CancellationToken.None);
            await returned!.DisposeAsync();

            Assert.Null(observer.CapturedArtifact);
            Assert.True(handle.Disposed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A recording surface that does not implement <see cref="IDeviceRecordingPathAuthority"/>.</summary>
    private sealed class UnvouchedRecordingSurface : IDeviceSurface
    {
        public string? StoppedPath { get; set; }

        public Task<DeviceHostHealth> GetHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceHostHealth { Availability = DeviceHostAvailability.Available });
        public Task<IReadOnlyList<DeviceTarget>?> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceTarget>?>([]);
        public Task<DeviceTarget?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<DeviceTarget?>(null);
        public Task<DeviceOperationResult> BootAsync(string id, CancellationToken ct = default) => Task.FromResult(DeviceOperationResult.Ok());
        public Task<DeviceOperationResult> ShutdownAsync(string id, CancellationToken ct = default) => Task.FromResult(DeviceOperationResult.Ok());
        public Task<DeviceOperationResult> TapAsync(string id, DevicePoint p, CancellationToken ct = default) => Task.FromResult(DeviceOperationResult.Ok());
        public Task<byte[]?> ScreenshotAsync(string id, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
        public Task<DeviceOperationResult> StartRecordingAsync(string id, int timeoutSeconds = 180, CancellationToken ct = default) =>
            Task.FromResult(DeviceOperationResult.Ok());
        public Task<DeviceRecordingResult> StopRecordingAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(new DeviceRecordingResult(StoppedPath is not null, StoppedPath));
    }

    private sealed class ThrowingRecordingSurface : IDeviceSurface
    {
        public Task<DeviceHostHealth> GetHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceHostHealth { Availability = DeviceHostAvailability.Available });
        public Task<IReadOnlyList<DeviceTarget>?> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceTarget>?>([]);
        public Task<DeviceTarget?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<DeviceTarget?>(null);
        public Task<DeviceOperationResult> BootAsync(string id, CancellationToken ct = default) => Task.FromResult(DeviceOperationResult.Ok());
        public Task<DeviceOperationResult> ShutdownAsync(string id, CancellationToken ct = default) => Task.FromResult(DeviceOperationResult.Ok());
        public Task<DeviceOperationResult> TapAsync(string id, DevicePoint p, CancellationToken ct = default) => Task.FromResult(DeviceOperationResult.Ok());
        public Task<byte[]?> ScreenshotAsync(string id, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
        public Task<DeviceOperationResult> StartRecordingAsync(string id, int timeoutSeconds = 180, CancellationToken ct = default) =>
            throw new InvalidOperationException("The device host connection dropped.");
    }

    private sealed class TrackedHandle : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingCapture : IFlowReplayEvidenceCapture
    {
        public int Calls { get; private set; }
        public TrackedHandle Handle { get; set; } = new();

        public Task CaptureOnFailureAsync(MauiFlow flow, FlowStep failedStep, FlowStepResult result, CancellationToken ct)
        {
            Calls++;
            return Task.CompletedTask;
        }

        public Task<IAsyncDisposable?> BeginRunAsync(MauiFlow flow, CancellationToken ct) =>
            Task.FromResult<IAsyncDisposable?>(Handle);
    }

    [Fact]
    public async Task TheSeamDefaultsToDoingNothing()
    {
        // An existing implementer that predates these members must keep compiling and keep
        // behaving identically, which is why they are defaulted rather than required.
        IFlowReplayEvidenceCapture legacy = new LegacyCapture();

        Assert.Null(await legacy.BeginRunAsync(new MauiFlow(), CancellationToken.None));
        Assert.Null(await legacy.ExplainFailureAsync(
            new MauiFlow(),
            new FlowStep(),
            Failed("not_visible"),
            CancellationToken.None));
    }

    private sealed class LegacyCapture : IFlowReplayEvidenceCapture
    {
        public Task CaptureOnFailureAsync(MauiFlow flow, FlowStep failedStep, FlowStepResult result, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
