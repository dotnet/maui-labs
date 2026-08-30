using System.Security.Cryptography;
using Microsoft.Maui.DevFlow.Devices;
using Microsoft.Maui.DevFlow.Testing;
// This namespace declares its own MauiFlow (FlowCompatibility.cs), which would otherwise shadow
// the Testing one the replay seam is defined against.
using TestingFlow = Microsoft.Maui.DevFlow.Testing.MauiFlow;
using TestingStep = Microsoft.Maui.DevFlow.Testing.FlowStep;
using TestingStepResult = Microsoft.Maui.DevFlow.Testing.FlowStepResult;

namespace Microsoft.Maui.Cli.DevFlow.Flows;

/// <summary>
/// Lets the device layer contribute to a flow replay: a recording of the run, and a cause for a
/// step that could not be actioned.
/// <para>
/// This is a decorator rather than a replacement. The existing evidence capture keeps doing
/// exactly what it did; this adds the two things the replay engine structurally cannot obtain,
/// because both require seeing <em>outside</em> the app process.
/// </para>
/// <para>
/// The dependency points inwards on purpose. <c>Microsoft.Maui.DevFlow.Testing</c> is a shipped
/// package and does not reference the device layer; it declares a seam, and the CLI — which owns
/// both — implements it. Adding a project reference the other way would have forced a
/// non-shipping assembly into a public package.
/// </para>
/// <para>
/// Whether a reported recording path is genuinely a recording is asked of the surface that
/// produced it, through <see cref="IDeviceRecordingPathAuthority"/>, rather than checked against
/// any particular implementation's directory. Only the surface that told the host where to write
/// knows what its root is, and a surface that cannot answer publishes nothing at all: no digest,
/// and no path handed onwards. Everything downstream — retention, serving — receives a
/// <see cref="DeviceRecordingCapture"/> carrying that surface's own authority, so no later hop has
/// to guess whose root to re-validate against.
/// </para>
/// </summary>
public sealed class DeviceFlowObserver : IFlowRunEvidenceCapture
{
    private readonly IFlowReplayEvidenceCapture? _inner;
    private readonly IDeviceSurface _devices;
    /// <summary>
    /// The surface's own answer to "is this a recording you own", when it can give one. A surface
    /// that cannot publishes no recording artifact: an unverifiable path must never become a digest
    /// in a flow's evidence.
    /// </summary>
    private readonly IDeviceRecordingPathAuthority? _recordingPaths;
    private readonly string _deviceId;
    private readonly string? _appPackageId;
    private readonly Action<DeviceRecordingCapture>? _onRecording;
    private readonly bool _recordRun;
    private readonly int _recordingTimeoutSeconds;
    private MauiFlowArtifactReference? _deviceRecordingArtifact;

    public DeviceFlowObserver(
        IDeviceSurface devices,
        string deviceId,
        IFlowReplayEvidenceCapture? inner = null,
        string? appPackageId = null,
        Action<DeviceRecordingCapture>? onRecording = null,
        bool recordRun = true,
        int recordingTimeoutSeconds = 180)
    {
        _devices = devices ?? throw new ArgumentNullException(nameof(devices));
        _recordingPaths = devices as IDeviceRecordingPathAuthority;
        _deviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
        _inner = inner;
        _appPackageId = appPackageId;
        _onRecording = onRecording;
        _recordRun = recordRun;
        _recordingTimeoutSeconds = Math.Clamp(recordingTimeoutSeconds, 1, 3600);
    }

    public Task CaptureOnFailureAsync(
        TestingFlow flow, TestingStep failedStep, TestingStepResult result, CancellationToken cancellationToken)
        => _inner?.CaptureOnFailureAsync(flow, failedStep, result, cancellationToken) ?? Task.CompletedTask;

    public MauiFlowArtifactReference? CapturedArtifact =>
        (_inner as IFlowRunEvidenceCapture)?.CapturedArtifact ?? _deviceRecordingArtifact;

    /// <summary>
    /// Only claims <c>deviceRecording</c> when the recording is the artifact this run actually
    /// publishes. When the inner capture already produced one, that artifact wins on
    /// <see cref="CapturedArtifact"/>, and advertising the kind anyway would let a flow declaring
    /// <c>expectedEvidence: deviceRecording</c> be satisfied by a screenshot's digest — evidence
    /// this layer would have fabricated.
    /// </summary>
    public IReadOnlyCollection<string> CapturedEvidenceKinds
    {
        get
        {
            var kinds = new HashSet<string>(
                (_inner as IFlowRunEvidenceCapture)?.CapturedEvidenceKinds ?? [],
                StringComparer.Ordinal);
            if (_deviceRecordingArtifact is not null &&
                ReferenceEquals(CapturedArtifact, _deviceRecordingArtifact))
            {
                kinds.Add(MauiFlowEvidenceKinds.DeviceRecording);
            }
            else
            {
                kinds.Remove(MauiFlowEvidenceKinds.DeviceRecording);
            }
            return kinds;
        }
    }

    public Task CaptureOnRunFailureAsync(
        MauiFlowRunEvidenceContext context,
        CancellationToken cancellationToken) =>
        _inner is IFlowRunEvidenceCapture detailed
            ? detailed.CaptureOnRunFailureAsync(context, cancellationToken)
            : _inner?.CaptureOnFailureAsync(
                context.Flow,
                context.FailedStep,
                context.LegacyStepResult,
                cancellationToken) ?? Task.CompletedTask;

    /// <summary>
    /// Starts recording the run. A failed test that ships with video of its own failure is the
    /// most useful artifact a replay can produce, because the interesting moment has already
    /// passed by the time anyone reads the report.
    /// </summary>
    public async Task<IAsyncDisposable?> BeginRunAsync(TestingFlow flow, CancellationToken cancellationToken)
    {
        var innerHandle = _inner is null
            ? null
            : await _inner.BeginRunAsync(flow, cancellationToken).ConfigureAwait(false);
        if (!_recordRun)
            return innerHandle;

        DeviceOperationResult started;
        try
        {
            started = await _devices
                .StartRecordingAsync(
                    _deviceId,
                    _recordingTimeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // The inner observer already opened its own capture. Throwing past it would strand
            // that handle for the whole run, because the caller never receives anything to dispose.
            if (innerHandle is not null)
            {
                try { await innerHandle.DisposeAsync().ConfigureAwait(false); }
                catch { }
            }
            throw;
        }

        // A device that cannot record is not a reason to refuse the run.
        return started.Success ? new RecordingHandle(this, innerHandle) : innerHandle;
    }

    /// <summary>
    /// Turns "not visible" into a reason. The replay engine can say a step could not be actioned;
    /// only something outside the app can say a permission dialog was covering it.
    /// </summary>
    public async Task<string?> ExplainFailureAsync(
        TestingFlow flow, TestingStep failedStep, TestingStepResult result, CancellationToken cancellationToken)
    {
        // Only ask when the failure is the kind a foreign window would explain. A selector that
        // matched nothing is an authoring problem, and blaming the environment for it would send
        // the reader somewhere useless.
        if (!IsExplainableByForeground(result))
            return null;

        var foreground = await _devices
            .DescribeForegroundAsync(_deviceId, _appPackageId, cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(foreground)
            ? null
            : $"{foreground} was covering the app, so the step could not reach its target.";
    }

    /// <summary>
    /// Whether something on top of the app could plausibly account for this failure.
    /// <para>
    /// A wrong explanation is worse than none: it sends the reader to investigate an environment
    /// that was fine while the real cause — usually a selector — goes unexamined.
    /// </para>
    /// </summary>
    internal static bool IsExplainableByForeground(TestingStepResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Ok)
            return false;

        var kind = result.FailureKind;
        if (string.IsNullOrWhiteSpace(kind))
            return false;

        return kind.Contains("visible", StringComparison.OrdinalIgnoreCase)
            || kind.Contains("actionable", StringComparison.OrdinalIgnoreCase)
            || kind.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || kind.Contains("stale", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Stops the recording when the run ends, however it ends.</summary>
    private sealed class RecordingHandle(
        DeviceFlowObserver owner,
        IAsyncDisposable? inner) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                var stopped = await owner._devices.StopRecordingAsync(owner._deviceId).ConfigureAwait(false);
                if (stopped.Success && !string.IsNullOrWhiteSpace(stopped.Path))
                {
                    // The one place the surface's judgement is taken. Nothing downstream sees the
                    // path the host reported; it sees only what the surface vouched for, carried
                    // with the authority that vouched, so a surface without standing to answer
                    // produces no digest, no retained copy, and nothing served.
                    if (owner._recordingPaths is null)
                        return;

                    var capture = DeviceRecordingCapture.TryCreate(stopped.Path, owner._recordingPaths);
                    if (capture is null)
                        return;

                    owner._deviceRecordingArtifact =
                        await CreateArtifactAsync(capture).ConfigureAwait(false);
                    owner._onRecording?.Invoke(capture);
                }
            }
            catch
            {
                // Finishing a recording must never change the run's outcome.
            }
            finally
            {
                if (inner is not null)
                {
                    try { await inner.DisposeAsync().ConfigureAwait(false); }
                    catch { }
                }
            }
        }

        private static async Task<MauiFlowArtifactReference?> CreateArtifactAsync(
            DeviceRecordingCapture capture)
        {
            try
            {
                // Re-prove containment on the handle that is hashed, through the same authority
                // rather than a particular implementation's root: the digest travels into a flow's
                // evidence, so hashing a link's target instead of the recording would put a
                // truthful-looking digest of the wrong file into the record.
                var fullPath = capture.ResolveForRead();
                if (fullPath is null)
                    return null;
                var info = new FileInfo(fullPath);
                if (!info.Exists || info.Length <= 0 || info.Length > 2L * 1024 * 1024 * 1024)
                    return null;

                await using var stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var digest = Convert.ToHexString(
                    await SHA256.HashDataAsync(stream).ConfigureAwait(false))
                    .ToLowerInvariant();
                var artifactId = "device-recording-" + digest[..16];
                return new MauiFlowArtifactReference
                {
                    ArtifactId = artifactId,
                    Kind = MauiFlowEvidenceKinds.DeviceRecording,
                    Path = "artifacts/device-recording.mp4",
                    Digest = digest,
                    MediaType = "video/mp4",
                    Redacted = false,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }
        }
    }
}
