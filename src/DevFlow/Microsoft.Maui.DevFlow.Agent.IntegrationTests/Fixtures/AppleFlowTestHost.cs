using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Thin Apple lifecycle adapter for the one public MauiFlowRunner. XCTest transport is supplied as
/// an IMauiFlowDriver by the macOS host; this type never parses or reimplements flow semantics.
/// </summary>
internal sealed class AppleFlowTestHost : IAsyncDisposable
{
    readonly IPlatformFlowTestLifecycle _lifecycle;
    readonly IMauiFlowDriver _driver;
    readonly string _platform;
    readonly Func<MauiFlow, MauiTestPlan, PlatformFlowLifecycleException?>? _staticValidator;
    readonly Func<AgentStatus, MauiTestPlan, PlatformFlowLifecycleException?>? _capabilityValidator;
    bool _disposed;

    internal AppleFlowTestHost(
        string platform,
        IPlatformFlowTestLifecycle lifecycle,
        IMauiFlowDriver driver,
        Func<MauiFlow, MauiTestPlan, PlatformFlowLifecycleException?>? staticValidator = null,
        Func<AgentStatus, MauiTestPlan, PlatformFlowLifecycleException?>? capabilityValidator = null)
    {
        if (platform is not "ios" and not "maccatalyst" and not "macos")
            throw new ArgumentOutOfRangeException(nameof(platform));
        _platform = platform;
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _staticValidator = staticValidator;
        _capabilityValidator = capabilityValidator;
    }

    public async Task<AppleFlowRunResult> RunAsync(
        MauiFlow flow,
        MauiTestPlan plan,
        AppleFlowRunRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(plan);
        request ??= new AppleFlowRunRequest();
        var runId = request.RunId ?? $"{_platform}-{Guid.NewGuid():N}";
        var artifactRoot = request.ArtifactRoot ?? Path.Combine(
            AppFixtureBase.FindRepoRoot(),
            "artifacts",
            "TestResults",
            "devflow-flow",
            _platform);
        MauiFlowResetResult? reset = null;
        PlatformCheckpointVerification? checkpoint = null;
        var artifacts = new List<MauiFlowArtifactReference>();

        try
        {
            if (_staticValidator?.Invoke(flow, plan) is { } staticFailure)
                throw staticFailure;
            await _lifecycle.BuildAsync(cancellationToken).ConfigureAwait(false);
            await _lifecycle.InstallAsync(cancellationToken).ConfigureAwait(false);
            reset = await _lifecycle.HardResetAsync(request.Reset, cancellationToken).ConfigureAwait(false);
            if (string.Equals(reset.Strategy, "uninstall-reinstall", StringComparison.Ordinal))
                await _lifecycle.InstallAsync(cancellationToken).ConfigureAwait(false);

            var launch = await _lifecycle.LaunchAsync(cancellationToken).ConfigureAwait(false);
            var ready = await _lifecycle.WaitForExpectedAgentAsync(new PlatformAgentExpectation
            {
                PackageId = launch.PackageId,
                ExpectedProcessId = launch.ProcessId,
                PreviousAgent = launch.PreviousAgent,
                Timeout = request.AgentReadyTimeout,
            }, cancellationToken).ConfigureAwait(false);
            if (_capabilityValidator?.Invoke(ready.Status, plan) is { } capabilityFailure)
                throw capabilityFailure;
            var seed = await _lifecycle.SeedAsync(request.Seed, cancellationToken).ConfigureAwait(false);
            checkpoint = await _lifecycle.VerifyCheckpointAsync(new PlatformCheckpointRequest
            {
                Expected = request.ExpectedCheckpoint ?? new MauiFlowCheckpoint { Route = "//native" },
                Reset = reset,
                Seed = seed,
            }, cancellationToken).ConfigureAwait(false);

            var report = await new MauiFlowRunner(_driver, new MauiFlowRunnerOptions
            {
                RunId = runId,
                Target = checkpoint.Target,
                Reset = reset,
                Plan = plan,
                RunContext = checkpoint.RunContext,
                ArtifactRoot = artifactRoot,
            }, request.FailureEvidenceCapture).RunAsync(flow, cancellationToken).ConfigureAwait(false);
            var diagnostics = await CaptureDiagnosticsAsync(artifactRoot, runId, "flow-complete", cancellationToken).ConfigureAwait(false);
            artifacts.AddRange(diagnostics);
            AppendArtifacts(report, diagnostics);
            Persist(report, artifactRoot);
            return new AppleFlowRunResult(report, report.Artifacts.ToArray());
        }
        catch (PlatformFlowLifecycleException exception)
        {
            var diagnostics = await CaptureDiagnosticsAsync(artifactRoot, runId, exception.Message, CancellationToken.None).ConfigureAwait(false);
            artifacts.AddRange(diagnostics);
            var report = CreateLifecycleFailure(_platform, flow, plan, runId, reset, checkpoint, exception, artifacts);
            Persist(report, artifactRoot);
            return new AppleFlowRunResult(report, report.Artifacts.ToArray());
        }
        catch (Exception exception)
        {
            var lifecycleException = PlatformFlowLifecycleException.Infrastructure(
                $"{_platform} lifecycle failed unexpectedly: {exception.Message}",
                exception);
            var diagnostics = await CaptureDiagnosticsAsync(artifactRoot, runId, lifecycleException.Message, CancellationToken.None).ConfigureAwait(false);
            artifacts.AddRange(diagnostics);
            var report = CreateLifecycleFailure(_platform, flow, plan, runId, reset, checkpoint, lifecycleException, artifacts);
            Persist(report, artifactRoot);
            return new AppleFlowRunResult(report, report.Artifacts.ToArray());
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { await _lifecycle.StopAsync().ConfigureAwait(false); }
        finally { await _lifecycle.DisposeAsync().ConfigureAwait(false); }
    }

    async Task<IReadOnlyList<MauiFlowArtifactReference>> CaptureDiagnosticsAsync(
        string artifactRoot,
        string runId,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            var diagnostics = await _lifecycle.CaptureHostDiagnosticsAsync(new PlatformDiagnosticsRequest
            {
                ArtifactRoot = artifactRoot,
                RunId = runId,
                Reason = reason,
            }, cancellationToken).ConfigureAwait(false);
            return diagnostics.Artifacts;
        }
        catch
        {
            return [];
        }
    }

    static MauiFlowRunReport CreateLifecycleFailure(
        string platform,
        MauiFlow flow,
        MauiTestPlan plan,
        string runId,
        MauiFlowResetResult? reset,
        PlatformCheckpointVerification? checkpoint,
        PlatformFlowLifecycleException exception,
        IReadOnlyList<MauiFlowArtifactReference> artifacts)
    {
        var context = checkpoint?.RunContext ?? new MauiFlowRunContext
        {
            Intent = MauiFlowReplayIntents.OrdinaryReplay,
            Reset = reset,
        };
        var infrastructure = exception.Kind == PlatformFlowLifecycleFailureKind.Infrastructure;
        var capability = exception.Kind == PlatformFlowLifecycleFailureKind.Capability;
        var code = infrastructure
            ? MauiFlowFailureClasses.Infrastructure
            : capability
                ? MauiFlowFailureClasses.CapabilityMissing
                : MauiFlowFailureClasses.PreconditionUnsatisfied;
        var now = DateTimeOffset.UtcNow;
        return new MauiFlowRunReport
        {
            RunId = runId,
            FlowId = $"sha256:{MauiFlowRunReportSerializer.ComputeFlowDigest(flow)}",
            FlowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow),
            LegacyFlowIdentity = flow.Name,
            Target = checkpoint?.Target,
            Reset = reset,
            Preconditions = context.Preconditions,
            SideEffectPolicy = plan.SideEffectPolicy,
            ReplayEligibility = MauiFlowReplaySafetyEvaluator.Evaluate(new MauiFlowRunRequest
            {
                Plan = plan,
                Context = context,
            }),
            StartedAt = now,
            EndedAt = now,
            Outcome = new MauiFlowRunOutcome
            {
                Status = infrastructure ? MauiFlowRunOutcomes.InfrastructureError : MauiFlowRunOutcomes.Failed,
                Summary = exception.Message,
                Terminal = true,
                Verified = false,
                VerificationReason = $"Replay did not start because {platform} lifecycle admission failed.",
            },
            Verification = new MauiFlowRunVerification
            {
                Verified = false,
                Reason = $"Replay did not start because {platform} lifecycle admission failed.",
                CheckedAt = now,
            },
            Failure = new MauiFlowFailure
            {
                FailureId = $"failure-{runId}",
                Class = code,
                Code = code,
                Category = infrastructure ? "infrastructure" : capability ? "capability" : "precondition",
                Phase = $"{platform}-lifecycle",
                Retryable = false,
                RepairEligible = false,
                Message = exception.Message,
                At = now,
                Artifacts = artifacts.ToList(),
            },
            Events =
            [
                new MauiFlowRunEvent
                {
                    Sequence = 1,
                    At = now,
                    Kind = "lifecycle-failed",
                    Message = exception.Message,
                },
            ],
            Artifacts = artifacts.ToList(),
        };
    }

    static void AppendArtifacts(MauiFlowRunReport report, IReadOnlyList<MauiFlowArtifactReference> artifacts)
    {
        foreach (var artifact in artifacts)
        {
            if (!report.Artifacts.Any(existing => string.Equals(existing.ArtifactId, artifact.ArtifactId, StringComparison.Ordinal)))
                report.Artifacts.Add(artifact);
        }
    }

    static void Persist(MauiFlowRunReport report, string artifactRoot)
    {
        var write = MauiFlowRunReportSerializer.WriteAtomic(report, artifactRoot);
        if (write.Ok)
        {
            report.ReportPath = write.Path;
            report.ReportDigest = write.Digest;
        }
        else
        {
            report.Truncated = true;
            report.Omissions.Add(new MauiFlowReportOmission
            {
                Kind = "report-artifact",
                Reason = write.Error ?? "The Apple host could not persist the run report.",
            });
        }
    }
}

internal sealed class AppleFlowRunRequest
{
    public string? RunId { get; init; }
    public string? ArtifactRoot { get; init; }
    public IFlowReplayEvidenceCapture? FailureEvidenceCapture { get; init; }
    public PlatformFlowResetRequest Reset { get; init; } = new();
    public PlatformFlowSeedRequest Seed { get; init; } = new();
    public MauiFlowCheckpoint? ExpectedCheckpoint { get; init; }
    public TimeSpan AgentReadyTimeout { get; init; } = TimeSpan.FromSeconds(120);
}

internal sealed class AppleFlowRunResult(MauiFlowRunReport report, IReadOnlyList<MauiFlowArtifactReference> artifacts)
{
    public MauiFlowRunReport Report { get; } = report;
    public IReadOnlyList<MauiFlowArtifactReference> Artifacts { get; } = artifacts;
}
