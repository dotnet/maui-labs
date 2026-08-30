using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Host-only lifecycle operations required before the canonical flow runner is allowed to drive
/// an application. This deliberately lives in integration tooling rather than the public Testing
/// package.
/// </summary>
internal interface IPlatformFlowTestLifecycle : IAsyncDisposable
{
    Task<PlatformBuildResult> BuildAsync(CancellationToken cancellationToken = default);
    Task<PlatformInstallResult> InstallAsync(CancellationToken cancellationToken = default);
    Task<MauiFlowResetResult> HardResetAsync(PlatformFlowResetRequest request, CancellationToken cancellationToken = default);
    Task<PlatformSeedResult> SeedAsync(PlatformFlowSeedRequest request, CancellationToken cancellationToken = default);
    Task<PlatformLaunchResult> LaunchAsync(CancellationToken cancellationToken = default);
    Task<PlatformAgentReadyResult> WaitForExpectedAgentAsync(
        PlatformAgentExpectation expectation,
        CancellationToken cancellationToken = default);
    Task<PlatformCheckpointVerification> VerifyCheckpointAsync(
        PlatformCheckpointRequest request,
        CancellationToken cancellationToken = default);
    Task<PlatformHostDiagnostics> CaptureHostDiagnosticsAsync(
        PlatformDiagnosticsRequest request,
        CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

internal interface IFlowLifecycleFixture
{
    IPlatformFlowTestLifecycle? FlowLifecycle { get; }
}

internal enum PlatformFlowLifecycleFailureKind
{
    Infrastructure,
    Precondition,
    Capability,
}

/// <summary>
/// Bounded, command-safe context retained with a lifecycle failure for host diagnostics.
/// It intentionally contains no command line, device identifier, environment, or raw output.
/// </summary>
internal sealed class PlatformFlowLifecycleFailureDetails
{
    public string? LifecyclePhase { get; init; }
    public string? ActionName { get; init; }
    public string? AdbCommandCategory { get; init; }
    public int? ExitCode { get; init; }
    public int? TimeoutSeconds { get; init; }
    public bool? TimedOut { get; init; }
    public bool? CancellationRequested { get; init; }
    public string? SafeErrorText { get; init; }
}

internal sealed class PlatformFlowLifecycleException : InvalidOperationException
{
    public PlatformFlowLifecycleException(
        PlatformFlowLifecycleFailureKind kind,
        string message,
        Exception? innerException = null,
        PlatformFlowLifecycleFailureDetails? details = null)
        : base(message, innerException)
    {
        Kind = kind;
        Details = details;
    }

    public PlatformFlowLifecycleFailureKind Kind { get; }
    public PlatformFlowLifecycleFailureDetails? Details { get; }

    public static PlatformFlowLifecycleException Infrastructure(
        string message,
        Exception? innerException = null,
        PlatformFlowLifecycleFailureDetails? details = null)
        => new(PlatformFlowLifecycleFailureKind.Infrastructure, message, innerException, details);

    public static PlatformFlowLifecycleException Precondition(
        string message,
        Exception? innerException = null,
        PlatformFlowLifecycleFailureDetails? details = null)
        => new(PlatformFlowLifecycleFailureKind.Precondition, message, innerException, details);

    public static PlatformFlowLifecycleException Capability(
        string message,
        Exception? innerException = null,
        PlatformFlowLifecycleFailureDetails? details = null)
        => new(PlatformFlowLifecycleFailureKind.Capability, message, innerException, details);
}

internal sealed class PlatformBuildResult
{
    public required string ArtifactPath { get; init; }
    public required string AppBuildFingerprint { get; init; }
    public required string PackageId { get; init; }
}

internal sealed class PlatformInstallResult
{
    public required string PackageId { get; init; }
    public required string ArtifactPath { get; init; }
    public bool Reinstalled { get; init; }
}

internal sealed class PlatformFlowResetRequest
{
    public MauiTestResetRequirement? Requirement { get; init; }
    public string? Strategy { get; init; }
    public string? ArtifactRoot { get; init; }
}

internal sealed class PlatformFlowSeedRequest
{
    public string SeedId { get; init; } = "devflow-sample-v1";
    public string? ExpectedSeedFingerprint { get; init; }
    public string? ExpectedBackendStateFingerprint { get; init; }
}

internal sealed class PlatformSeedResult
{
    public required string SeedId { get; init; }
    public required string SeedFingerprint { get; init; }
    public string? BackendStateFingerprint { get; init; }
    public string? StateFingerprint { get; init; }
    public string? ProcessInstanceId { get; init; }
    public MauiFlowAppStateSeedFingerprint AppStateSeed { get; init; } = new();
    public MauiFlowBackendTestDataSeedFingerprint? BackendTestDataSeed { get; init; }
    public MauiIndependentBusinessOracleResult? StateOracle { get; init; }
}

internal sealed class PlatformLaunchResult
{
    public required string PackageId { get; init; }
    public int? ProcessId { get; init; }
    public PlatformAgentIdentity? PreviousAgent { get; init; }
}

internal sealed record PlatformAgentIdentity(string? InstanceId, int ProcessId)
{
    public string StableId => !string.IsNullOrWhiteSpace(InstanceId)
        ? InstanceId
        : $"process-{ProcessId}";

    public static PlatformAgentIdentity? FromStatus(AgentStatus? status)
    {
        var processId = status?.App?.ProcessId;
        return processId is > 0
            ? new PlatformAgentIdentity(status!.Agent?.InstanceId, processId.Value)
            : null;
    }
}

internal sealed class PlatformAgentExpectation
{
    public string? PackageId { get; init; }
    public int? ExpectedProcessId { get; init; }
    public PlatformAgentIdentity? PreviousAgent { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);
}

internal sealed class PlatformAgentReadyResult
{
    public required AgentStatus Status { get; init; }
    public required PlatformAgentIdentity Agent { get; init; }
}

internal sealed class PlatformCheckpointRequest
{
    public required MauiFlowCheckpoint Expected { get; init; }
    public required MauiFlowResetResult Reset { get; init; }
    public required PlatformSeedResult Seed { get; init; }
}

internal sealed class PlatformCheckpointVerification
{
    public required MauiFlowCheckpoint Expected { get; init; }
    public required MauiFlowCheckpoint Observed { get; init; }
    public required MauiFlowRunTarget Target { get; init; }
    public required MauiFlowRunContext RunContext { get; init; }
}

internal sealed class PlatformDiagnosticsRequest
{
    public required string ArtifactRoot { get; init; }
    public required string RunId { get; init; }
    public string? Reason { get; init; }
}

internal sealed class PlatformHostDiagnostics
{
    public List<MauiFlowArtifactReference> Artifacts { get; } = [];
}

internal static class PlatformAgentReadiness
{
    internal static async Task<PlatformAgentReadyResult> WaitForExpectedAsync(
        Func<CancellationToken, Task<AgentStatus?>> getStatus,
        PlatformAgentExpectation expectation,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(getStatus);
        ArgumentNullException.ThrowIfNull(expectation);

        var deadline = DateTimeOffset.UtcNow + expectation.Timeout;
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(250);
        string? lastReason = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var status = await getStatus(cancellationToken).ConfigureAwait(false);
                var identity = PlatformAgentIdentity.FromStatus(status);
                if (status is null || identity is null)
                {
                    lastReason = "The agent status did not include a process-scoped identity.";
                }
                else if (!string.IsNullOrWhiteSpace(expectation.PackageId) &&
                         !string.Equals(status.App?.PackageId, expectation.PackageId, StringComparison.Ordinal))
                {
                    lastReason = $"Expected package '{expectation.PackageId}', observed '{status.App?.PackageId ?? "<none>"}'.";
                }
                else if (expectation.ExpectedProcessId is { } expectedProcessId &&
                         identity.ProcessId != expectedProcessId)
                {
                    lastReason = $"Expected process {expectedProcessId}, observed {identity.ProcessId}.";
                }
                else if (expectation.PreviousAgent is { } previous &&
                         string.Equals(identity.StableId, previous.StableId, StringComparison.Ordinal))
                {
                    lastReason = $"Rejected stale agent identity '{identity.StableId}'.";
                }
                else
                {
                    return new PlatformAgentReadyResult { Status = status, Agent = identity };
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                lastReason = $"{ex.GetType().Name}: {ex.Message}";
            }

            if (interval > TimeSpan.Zero)
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }

        throw PlatformFlowLifecycleException.Infrastructure(
            $"A new process-scoped DevFlow agent did not become ready within {expectation.Timeout.TotalSeconds:0}s. " +
            $"Last observation: {lastReason ?? "none"}");
    }
}
