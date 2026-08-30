using Microsoft.Maui.Cli.DevFlow.Execution;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture-owned Android flow path. It prepares host/device state, then invokes the one public
/// <see cref="MauiFlowRunner"/> implementation without reimplementing flow semantics.
/// </summary>
internal sealed class AndroidFlowTestHost : IAsyncDisposable
{
    /// <summary>Route the harness verifies when a plan declares no checkpoint route.</summary>
    internal const string DefaultCheckpointRoute = "//native";

    readonly IPlatformFlowTestLifecycle _lifecycle;
    readonly IMauiFlowDriver _driver;
    readonly AgentClient? _agentClient;
    bool _disposed;

    public AndroidFlowTestHost(AndroidEmulatorFixture fixture)
        : this(
            fixture ?? throw new ArgumentNullException(nameof(fixture)),
            new AgentClientMauiFlowDriver(fixture.Client),
            fixture.Client)
    {
    }

    internal AndroidFlowTestHost(
        IPlatformFlowTestLifecycle lifecycle,
        IMauiFlowDriver driver,
        AgentClient? agentClient = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _agentClient = agentClient;
    }

    public async Task<AndroidFlowRunResult> RunAsync(
        MauiFlow flow,
        MauiTestPlan plan,
        AndroidFlowRunRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(plan);

        request ??= new AndroidFlowRunRequest();
        var runId = string.IsNullOrWhiteSpace(request.RunId)
            ? $"android-{Guid.NewGuid():N}"
            : request.RunId;
        var artifactRoot = request.ArtifactRoot ?? Path.Combine(
            AppFixtureBase.FindRepoRoot(),
            "artifacts",
            "TestResults",
            "devflow-flow",
            "android");
        var artifacts = new List<MauiFlowArtifactReference>();
        PlatformCheckpointVerification? verification = null;
        MauiFlowResetResult? reset = null;

        try
        {
            await _lifecycle.BuildAsync(cancellationToken).ConfigureAwait(false);
            await _lifecycle.InstallAsync(cancellationToken).ConfigureAwait(false);
            reset = await _lifecycle.HardResetAsync(request.Reset, cancellationToken).ConfigureAwait(false);
            if (string.Equals(reset.Strategy, "uninstall-reinstall", StringComparison.Ordinal))
                await _lifecycle.InstallAsync(cancellationToken).ConfigureAwait(false);

            var launch = await _lifecycle.LaunchAsync(cancellationToken).ConfigureAwait(false);
            await _lifecycle.WaitForExpectedAgentAsync(new PlatformAgentExpectation
            {
                PackageId = launch.PackageId,
                ExpectedProcessId = launch.ProcessId,
                PreviousAgent = launch.PreviousAgent,
                Timeout = request.AgentReadyTimeout,
            }, cancellationToken).ConfigureAwait(false);
            var seed = await _lifecycle.SeedAsync(request.Seed, cancellationToken).ConfigureAwait(false);
            verification = await _lifecycle.VerifyCheckpointAsync(new PlatformCheckpointRequest
            {
                Expected = request.ExpectedCheckpoint ?? new MauiFlowCheckpoint { Route = DefaultCheckpointRoute },
                Reset = reset,
                Seed = seed,
            }, cancellationToken).ConfigureAwait(false);

            using var retryScope = new NoMutatingRetryScope(_agentClient);
            var runner = new MauiFlowRunner(_driver, new MauiFlowRunnerOptions
            {
                RunId = runId,
                Target = verification.Target,
                Reset = reset,
                Plan = plan,
                RunContext = verification.RunContext,
                ArtifactRoot = artifactRoot,
            }, request.FailureEvidenceCapture);
            var report = await runner.RunAsync(flow, cancellationToken).ConfigureAwait(false);
            await EvaluatePostRunOraclesAsync(report, flow, plan, verification, cancellationToken)
                .ConfigureAwait(false);
            var diagnostics = await CaptureDiagnosticsAsync(artifactRoot, runId, "flow-complete", cancellationToken).ConfigureAwait(false);
            artifacts.AddRange(diagnostics);
            AppendArtifactsAndPersist(report, artifactRoot, diagnostics);
            artifacts.AddRange(report.Artifacts);
            return new AndroidFlowRunResult(report, artifacts);
        }
        catch (PlatformFlowLifecycleException ex)
        {
            var diagnostics = await CaptureDiagnosticsAsync(
                artifactRoot,
                runId,
                AndroidLifecycleDiagnosticRedactor.Sanitize(
                    ex.Message,
                    AndroidFixtureInitializationDiagnostics.MaxSafeErrorTextCharacters),
                CancellationToken.None).ConfigureAwait(false);
            artifacts.AddRange(diagnostics);
            var report = CreateLifecycleFailureReport(flow, plan, runId, reset, verification, ex, artifacts);
            Persist(report, artifactRoot);
            artifacts.AddRange(report.Artifacts);
            return new AndroidFlowRunResult(report, artifacts);
        }
        catch (Exception ex)
        {
            var safeError = AndroidLifecycleDiagnosticRedactor.Sanitize(
                ex.Message,
                AndroidFixtureInitializationDiagnostics.MaxSafeErrorTextCharacters);
            var diagnostics = await CaptureDiagnosticsAsync(
                artifactRoot,
                runId,
                safeError,
                CancellationToken.None).ConfigureAwait(false);
            artifacts.AddRange(diagnostics);
            var failure = PlatformFlowLifecycleException.Infrastructure(
                $"Android lifecycle failed unexpectedly: {safeError}",
                ex);
            var report = CreateLifecycleFailureReport(flow, plan, runId, reset, verification, failure, artifacts);
            Persist(report, artifactRoot);
            artifacts.AddRange(report.Artifacts);
            return new AndroidFlowRunResult(report, artifacts);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            await _lifecycle.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await _lifecycle.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Evaluates the plan's declared independent business oracles after the run, then restates the
    /// run's verification from what they reported.
    /// </summary>
    /// <remarks>
    /// A post-run oracle asks whether the app durably committed the record the flow claims to have
    /// created, so it cannot be answered from the pre-run checkpoint the harness already gathers.
    /// Without this the harness supplied only its own seed oracle, the oracle each plan actually
    /// requires never reported, and every oracle-backed flow stayed unverified no matter how well
    /// it ran. This deliberately reuses the same provider and the same verification decision the
    /// <c>flow run</c> path uses rather than restating either, so the two cannot drift apart.
    /// </remarks>
    static async Task EvaluatePostRunOraclesAsync(
        MauiFlowRunReport report,
        MauiFlow flow,
        MauiTestPlan plan,
        PlatformCheckpointVerification verification,
        CancellationToken cancellationToken)
    {
        var target = verification.Target;
        var serial = target?.DeviceId;
        var packageId = target?.AppId;
        if (string.IsNullOrWhiteSpace(serial) || string.IsNullOrWhiteSpace(packageId))
            return;

        var artifact = new ResolvedAppArtifact
        {
            Path = packageId,
            ProjectPath = packageId,
            AgentSessionId = target!.AgentInstanceId ?? packageId,
            TargetFramework = "net10.0-android",
            TargetPlatformIdentifier = "android",
            Configuration = "Debug",
            ArtifactType = "apk",
            ApplicationId = packageId,
            PackageDigest = target!.AppBuildFingerprint ?? "not-observed",
        };

        var provider = new AndroidAppStorageEvidenceProvider(
            new AndroidProvider(new JdkManager()),
            new ExecutionProcessRunner());
        if (!provider.Supports(new FlowStateEvidenceRequest { Plan = plan, Flow = flow, Artifact = artifact }))
            return;

        var buildFingerprint = target.AppBuildFingerprint ?? "not-observed";
        var evidence = await provider.EvaluatePostRunAsync(
            new FlowPostRunOracleEvaluationRequest
            {
                Plan = plan,
                Flow = flow,
                Artifact = artifact,
                RunId = report.RunId ?? "run",
                FlowDigest = report.FlowDigest ?? MauiFlowRunReportSerializer.ComputeFlowDigest(flow),
                DeviceIdentityFingerprint = AndroidLifecycleDiagnosticRedactor.Fingerprint(serial!),
                AppBuildFingerprint = buildFingerprint,
                // The harness verifies the installed APK against this same value, so it identifies
                // the package as precisely as a separate digest would.
                PackageDigest = buildFingerprint,
                Platform = "android",
                DeviceSerial = serial!,
                PackageId = packageId!,
                StartedAt = report.StartedAt ?? DateTimeOffset.UtcNow,
                EndedAt = report.EndedAt ?? DateTimeOffset.UtcNow,
                EvaluationDeadline = DateTimeOffset.UtcNow.AddMinutes(1),
                Report = report,
            },
            cancellationToken).ConfigureAwait(false);

        var oracles = verification.RunContext.BusinessOracles.ToList();
        foreach (var evaluated in evidence.BusinessOracles)
        {
            oracles.RemoveAll(existing =>
                string.Equals(existing.OracleId, evaluated.OracleId, StringComparison.Ordinal));
            oracles.Add(evaluated);
        }

        verification.RunContext.BusinessOracles = oracles;
        FlowExecutionCoordinator.ApplyPostRunVerification(
            report,
            plan,
            flow,
            verification.RunContext,
            DateTimeOffset.UtcNow);
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
            // Diagnostics must not hide the primary lifecycle failure.
            return [];
        }
    }

    static MauiFlowRunReport CreateLifecycleFailureReport(
        MauiFlow flow,
        MauiTestPlan plan,
        string runId,
        MauiFlowResetResult? reset,
        PlatformCheckpointVerification? verification,
        PlatformFlowLifecycleException exception,
        IReadOnlyList<MauiFlowArtifactReference> artifacts)
    {
        var context = verification?.RunContext ?? new MauiFlowRunContext
        {
            Intent = MauiFlowReplayIntents.OrdinaryReplay,
            Reset = reset,
        };
        var eligibility = MauiFlowReplaySafetyEvaluator.Evaluate(new MauiFlowRunRequest
        {
            Plan = plan,
            Context = context,
        });
        var isInfrastructure = exception.Kind == PlatformFlowLifecycleFailureKind.Infrastructure;
        var now = DateTimeOffset.UtcNow;
        var safeMessage = AndroidLifecycleDiagnosticRedactor.Sanitize(
            exception.Message,
            AndroidFixtureInitializationDiagnostics.MaxSafeErrorTextCharacters);
        return new MauiFlowRunReport
        {
            RunId = runId,
            FlowId = $"sha256:{MauiFlowRunReportSerializer.ComputeFlowDigest(flow)}",
            FlowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow),
            LegacyFlowIdentity = flow.Name,
            Target = verification?.Target,
            Reset = reset,
            Preconditions = context.Preconditions,
            SideEffectPolicy = eligibility.SideEffectPolicy,
            ReplayEligibility = eligibility,
            StartedAt = now,
            EndedAt = now,
            Outcome = new MauiFlowRunOutcome
            {
                Status = isInfrastructure ? MauiFlowRunOutcomes.InfrastructureError : MauiFlowRunOutcomes.Failed,
                Summary = safeMessage,
                Terminal = true,
                Verified = false,
                VerificationReason = "Replay did not start because Android lifecycle preconditions failed.",
            },
            Verification = new MauiFlowRunVerification
            {
                Verified = false,
                Reason = "Replay did not start because Android lifecycle preconditions failed.",
                CheckedAt = now,
            },
            Failure = new MauiFlowFailure
            {
                FailureId = $"failure-{runId}",
                Class = isInfrastructure ? MauiFlowFailureClasses.Infrastructure : MauiFlowFailureClasses.PreconditionUnsatisfied,
                Code = isInfrastructure ? MauiFlowFailureClasses.Infrastructure : MauiFlowFailureClasses.PreconditionUnsatisfied,
                Category = isInfrastructure ? "infrastructure" : "precondition",
                Phase = "android-lifecycle",
                Retryable = false,
                RepairEligible = false,
                Message = safeMessage,
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
                    Message = safeMessage,
                },
            ],
            Artifacts = artifacts.ToList(),
        };
    }

    static void AppendArtifactsAndPersist(
        MauiFlowRunReport report,
        string artifactRoot,
        IReadOnlyList<MauiFlowArtifactReference> diagnostics)
    {
        foreach (var artifact in diagnostics)
        {
            if (!report.Artifacts.Any(existing =>
                    string.Equals(existing.ArtifactId, artifact.ArtifactId, StringComparison.Ordinal)))
            {
                report.Artifacts.Add(artifact);
            }
        }

        Persist(report, artifactRoot);
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
                Reason = write.Error ?? "The Android host could not persist the run report.",
            });
        }
    }

    sealed class NoMutatingRetryScope : IDisposable
    {
        readonly AgentClient? _client;
        readonly bool _previous;

        public NoMutatingRetryScope(AgentClient? client)
        {
            _client = client;
            _previous = client?.RetryMutatingRequests ?? false;
            if (_client is not null)
                _client.RetryMutatingRequests = false;
        }

        public void Dispose()
        {
            if (_client is not null)
                _client.RetryMutatingRequests = _previous;
        }
    }
}

internal sealed class AndroidFlowRunRequest
{
    public string? RunId { get; init; }
    public string? ArtifactRoot { get; init; }
    /// <summary>Optional redacted, failure-only evidence capture supplied by an integration host.</summary>
    public IFlowReplayEvidenceCapture? FailureEvidenceCapture { get; init; }
    public PlatformFlowResetRequest Reset { get; init; } = new();
    public PlatformFlowSeedRequest Seed { get; init; } = new();
    public MauiFlowCheckpoint? ExpectedCheckpoint { get; init; }
    public TimeSpan AgentReadyTimeout { get; init; } = TimeSpan.FromSeconds(120);
}

internal sealed class AndroidFlowRunResult
{
    public AndroidFlowRunResult(MauiFlowRunReport report, IReadOnlyList<MauiFlowArtifactReference> artifacts)
    {
        Report = report;
        Artifacts = artifacts;
    }

    public MauiFlowRunReport Report { get; }
    public IReadOnlyList<MauiFlowArtifactReference> Artifacts { get; }
}
