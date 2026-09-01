using System.Diagnostics;
using System.Globalization;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>Options for the single canonical flow execution implementation.</summary>
public sealed class MauiFlowRunnerOptions
{
    public int PollTries { get; set; } = 10;
    public int PollGapMs { get; set; } = 250;
    /// <summary>
    /// Optional bounded pause after a completed non-final step so an interactive host can capture
    /// the resulting UI state before the next mutation. Production/CI callers leave this at zero.
    /// </summary>
    public int StepObservationDelayMs { get; set; }
    public bool ContinueOnFailure { get; set; }
    /// <summary>Preserves the legacy replayer cancellation contract when true.</summary>
    public bool ThrowOnCancellation { get; set; }
    public string? RunId { get; set; }
    public string? FlowId { get; set; }
    public int? FlowRevision { get; set; }
    public string? FlowDigest { get; set; }
    public string? LegacyFlowIdentity { get; set; }
    public MauiFlowRunTarget? Target { get; set; }
    public MauiFlowResetResult? Reset { get; set; }
    /// <summary>Optional plan whose side-effect policy is evaluated before the first mutation.</summary>
    public MauiTestPlan? Plan { get; set; }
    /// <summary>Host-observed reset, checkpoint, compensation, and oracle facts for admission.</summary>
    public MauiFlowRunContext? RunContext { get; set; }
    public MauiFlowCheckpoint? ExpectedCheckpoint { get; set; }
    public MauiFlowRunReportLimits ReportLimits { get; set; } = new();
    /// <summary>
    /// Value-free deterministic candidate capture settings. Candidates are recorded for diagnosis
    /// only and never change the active selector or replay fallback behavior.
    /// </summary>
    public MauiSelectorCandidateGenerationOptions SelectorCandidateOptions { get; set; } = new();
    /// <summary>Optional root for atomic <c>&lt;runId&gt;/flow-run.json</c> output.</summary>
    public string? ArtifactRoot { get; set; }
    public TimeProvider Clock { get; set; } = TimeProvider.System;
    /// <summary>
    /// Optional bounded progress observer. It receives step identity and counts only; it never
    /// receives typed values, driver payloads, or evidence content.
    /// </summary>
    public Action<MauiFlowRunProgress>? Progress { get; set; }
    /// <summary>
    /// Optional host-owned execution extension for work outside the app process. The Testing
    /// package defines only this generic fail-closed seam and does not reference a device backend.
    /// </summary>
    public IMauiFlowExecutionExtension? ExecutionExtension { get; set; }
}

/// <summary>Value-free progress emitted around one canonical flow step.</summary>
public sealed class MauiFlowRunProgress
{
    public string? RunId { get; init; }
    public string? StepId { get; init; }
    public int? Sequence { get; init; }
    public int CompletedSteps { get; init; }
    public int TotalSteps { get; init; }
    /// <summary><c>step-started</c> or <c>step-completed</c>.</summary>
    public string Phase { get; init; } = "";
}

/// <summary>Result from host-owned work performed around canonical app steps.</summary>
public sealed record MauiFlowExecutionExtensionResult
{
    public bool Success { get; init; }
    public string? FailureKind { get; init; }
    public string? Error { get; init; }

    public static readonly MauiFlowExecutionExtensionResult Ok = new() { Success = true };

    public static MauiFlowExecutionExtensionResult Failed(string error, string? failureKind = null) =>
        new()
        {
            Success = false,
            Error = error,
            FailureKind = failureKind ?? FlowFailureKinds.Drive,
        };
}

/// <summary>
/// Host-owned execution around the canonical app driver, such as device preconditions and native
/// interactions. Implementations must return a failure rather than silently skipping declared work.
/// </summary>
public interface IMauiFlowExecutionExtension
{
    Task<MauiFlowExecutionExtensionResult> PrepareAsync(
        MauiFlow flow,
        CancellationToken cancellationToken);

    Task<MauiFlowExecutionExtensionResult> AfterStepAsync(
        MauiFlow flow,
        FlowStep step,
        FlowStepResult result,
        CancellationToken cancellationToken);
}

/// <summary>The canonical structured result plus the compatibility report used by existing hosts.</summary>
public sealed class MauiFlowRunExecutionResult
{
    public MauiFlowRunReport Report { get; init; } = new();
    public FlowReplayReport LegacyReport { get; init; } = new();
    public string? ReportDigest { get; init; }
    public string? ReportPath { get; init; }
}

/// <summary>Context passed to evidence implementations after the terminal report is available.</summary>
public sealed class MauiFlowRunEvidenceContext
{
    public MauiFlowRunReport Report { get; init; } = new();
    public string? ReportDigest { get; init; }
    public string? ReportPath { get; init; }
    public MauiFlow Flow { get; init; } = new();
    public FlowStep FailedStep { get; init; } = new();
    public FlowStepResult LegacyStepResult { get; init; } = new();
}

/// <summary>
/// Optional additive evidence callback with final report identity. Implementations still support
/// <see cref="IFlowReplayEvidenceCapture"/> for legacy hosts.
/// </summary>
public interface IFlowRunEvidenceCapture : IFlowReplayEvidenceCapture
{
    /// <summary>Reference to the evidence captured for the most recent failed run, if any.</summary>
    MauiFlowArtifactReference? CapturedArtifact { get; }

    /// <summary>
    /// The <see cref="MauiFlowEvidenceKinds"/> this capture actually wrote for the most recent
    /// run. Hosts that do not report their contents return an empty set, which is read as "nothing
    /// was collected" rather than as "everything was collected".
    /// </summary>
    IReadOnlyCollection<string> CapturedEvidenceKinds => [];

    Task CaptureOnRunFailureAsync(MauiFlowRunEvidenceContext context, CancellationToken cancellationToken);
}

/// <summary>
/// The sole semantic flow runner. It records validation, selector resolution, actionability,
/// mutation receipts, assertions, and deterministic failure facts while the app is driven.
/// </summary>
public sealed class MauiFlowRunner
{
    internal const string InfrastructureErrorMessage = "The flow runner encountered an infrastructure error.";

    private readonly IMauiFlowDriver _driver;
    private readonly MauiFlowRunnerOptions _options;
    private readonly IFlowReplayEvidenceCapture? _evidenceCapture;
    private readonly Func<string, string?> _secretResolver;

    public MauiFlowRunner(
        IMauiFlowDriver driver,
        MauiFlowRunnerOptions? options = null,
        IFlowReplayEvidenceCapture? evidenceCapture = null,
        Func<string, string?>? secretResolver = null)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _options = options ?? new MauiFlowRunnerOptions();
        _evidenceCapture = evidenceCapture;
        _secretResolver = secretResolver ?? Environment.GetEnvironmentVariable;
    }

    public MauiFlowRunner(
        AgentClient agent,
        MauiFlowRunnerOptions? options = null,
        IFlowReplayEvidenceCapture? evidenceCapture = null,
        Func<string, string?>? secretResolver = null)
        : this(new AgentClientMauiFlowDriver(agent), options, evidenceCapture, secretResolver)
    {
    }

    /// <summary>Runs a flow and returns its bounded, structured report.</summary>
    public async Task<MauiFlowRunReport> RunAsync(MauiFlow flow, CancellationToken cancellationToken = default)
        => (await RunWithLegacyAsync(flow, file: null, cancellationToken).ConfigureAwait(false)).Report;

    /// <summary>
    /// Runs the canonical engine and additionally exposes the legacy response shape. Existing
    /// CLI, MCP, and Inspector callers use this while they migrate to <see cref="RunAsync"/>.
    /// </summary>
    public async Task<MauiFlowRunExecutionResult> RunWithLegacyAsync(
        MauiFlow flow,
        string? file = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flow);
        var startedAt = _options.Clock.GetUtcNow();
        var runId = string.IsNullOrWhiteSpace(_options.RunId)
            ? CreateRunId()
            : MauiFlowReportRedactor.SafeIdentifier(_options.RunId) ?? CreateRunId();
        var flowDigest = _options.FlowDigest ?? MauiFlowRunReportSerializer.ComputeFlowDigest(flow);
        var safetyRequest = new MauiFlowRunRequest
        {
            Plan = _options.Plan,
            Context = _options.RunContext,
        };
        var replayEligibility = MauiFlowReplaySafetyEvaluator.EvaluateWithFlow(safetyRequest, flow);
        var expectedCheckpoint = _options.ExpectedCheckpoint ?? _options.RunContext?.Preconditions?.Expected;
        var report = new MauiFlowRunReport
        {
            RunId = runId,
            FlowId = _options.FlowId ?? $"sha256:{flowDigest}",
            FlowRevision = _options.FlowRevision,
            FlowDigest = flowDigest,
            LegacyFlowIdentity = _options.LegacyFlowIdentity ??
                MauiFlowReportRedactor.SafeIdentifier(
                    string.IsNullOrWhiteSpace(file) ? flow.Name : Path.GetFileName(file)),
            Target = CreateUnenrichedTarget(flow),
            Reset = _options.RunContext?.Reset ?? _options.Reset,
            Preconditions = _options.RunContext?.Preconditions,
            SideEffectPolicy = replayEligibility.SideEffectPolicy,
            Compensator = _options.RunContext?.Compensator,
            BusinessOracles = _options.RunContext?.BusinessOracles.ToList() ?? [],
            ReplayEligibility = replayEligibility,
            StartedAt = startedAt,
            SelectorHealth = new MauiFlowSelectorHealthSummary(),
        };
        var legacy = new FlowReplayReport
        {
            Name = flow.Name,
            File = file,
            Total = flow.Steps?.Count ?? 0,
        };

        AddEvent(report, "started", "Canonical flow run started.");
        var requestedIntent = _options.RunContext?.Intent ?? MauiFlowReplayIntents.OrdinaryReplay;
        if (!replayEligibility.IsAllowedForIntent(requestedIntent))
        {
            const string admissionMessage = "Flow replay was rejected before mutation because its side-effect admission requirements were not met.";
            var legacyFailure = new FlowStepResult
            {
                Seq = 0,
                Action = "admission",
                Label = "Evaluate replay safety",
                Ok = false,
                FailureKind = FlowFailureKinds.Drive,
                Error = admissionMessage,
            };
            legacy.Results.Add(legacyFailure);
            legacy.Failed = 1;
            legacy.StoppedEarly = true;
            report.Failure = new MauiFlowFailure
            {
                FailureId = $"failure-{runId}",
                Class = MauiFlowFailureClasses.PreconditionUnsatisfied,
                Code = MauiFlowFailureClasses.PreconditionUnsatisfied,
                Category = "precondition",
                Phase = "admission",
                Retryable = false,
                RepairEligible = false,
                LegacyKind = FlowFailureKinds.Drive,
                Message = admissionMessage,
                StepId = "admission",
                At = _options.Clock.GetUtcNow(),
            };
            AddEvent(report, "admission-rejected", admissionMessage, "admission");
            return await FinalizeAsync(
                report,
                legacy,
                flow,
                failedStep: null,
                failedLegacyStep: legacyFailure,
                outcome: MauiFlowRunOutcomes.Failed,
                summary: admissionMessage,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        FlowStep? failedStep = null;
        FlowStepResult? failedLegacyStep = null;
        FlowStep? activeStep = null;
        FlowStepResult? activeLegacyStep = null;
        List<MauiFlowActionabilityAttempt>? activeActionability = null;
        DateTimeOffset? activeStepStartedAt = null;
        Stopwatch? activeStopwatch = null;
        MauiFlowCheckpoint? activeObservedCheckpoint = null;
        IAsyncDisposable? runObservation = null;

        async Task StopRunObservationAsync()
        {
            var current = runObservation;
            runObservation = null;
            if (current is null)
                return;
            try { await current.DisposeAsync().ConfigureAwait(false); }
            catch { /* finishing optional observation must not change the run outcome */ }
        }

        try
        {
            var validation = FlowValidator.Validate(flow);
            if (!validation.Ok)
            {
                var message = string.Join("; ", validation.Errors);
                var legacyFailure = new FlowStepResult
                {
                    Seq = 0,
                    Action = "validate",
                    Label = "Validate flow",
                    Ok = false,
                    FailureKind = FlowFailureKinds.Validation,
                    Error = message,
                };
                legacy.Results.Add(legacyFailure);
                legacy.Failed = 1;
                legacy.StoppedEarly = true;
                failedLegacyStep = legacyFailure;
                report.Steps.Add(new MauiFlowStepAttempt
                {
                    StepId = "validation",
                    Sequence = 0,
                    Action = "validate",
                    Intent = "Validate flow",
                    StartedAt = startedAt,
                    EndedAt = _options.Clock.GetUtcNow(),
                    DurationMs = 0,
                    FailureClass = MauiFlowFailureClasses.FlowInvalid,
                });
                report.Failure = MauiFlowFailureClassifier.ToFailure(
                    MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts { FlowInvalid = true }),
                    $"failure-{runId}",
                    FlowFailureKinds.Validation,
                    "validation",
                    _options.Clock.GetUtcNow(),
                    message);
                AddEvent(report, "validation-failed", "Flow validation failed.");
                return await FinalizeAsync(
                    report,
                    legacy,
                    flow,
                    failedStep,
                    failedLegacyStep,
                    MauiFlowRunOutcomes.Failed,
                    "Flow validation failed.",
                    cancellationToken).ConfigureAwait(false);
            }

            if (_options.ExecutionExtension is null && RequiresHostExecution(flow))
            {
                const string message =
                    "This flow declares devicePreconditions or deviceSteps and must run through a broker host with a paired device.";
                var legacyFailure = new FlowStepResult
                {
                    Seq = 0,
                    Action = "prepare-host",
                    Label = "Prepare host environment",
                    Ok = false,
                    FailureKind = FlowFailureKinds.Drive,
                    Error = message,
                };
                legacy.Results.Add(legacyFailure);
                legacy.Failed = 1;
                legacy.StoppedEarly = true;
                failedLegacyStep = legacyFailure;
                report.Failure = MauiFlowFailureClassifier.ToFailure(
                    MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
                    {
                        LegacyFailureKind = legacyFailure.FailureKind,
                    }),
                    $"failure-{runId}",
                    legacyFailure.FailureKind,
                    "host-preparation",
                    _options.Clock.GetUtcNow(),
                    message);
                AddEvent(report, "host-preparation-missing", message, "host-preparation");
                return await FinalizeAsync(
                    report,
                    legacy,
                    flow,
                    failedStep: null,
                    failedLegacyStep: legacyFailure,
                    outcome: MauiFlowRunOutcomes.Failed,
                    summary: "Host execution support is required.",
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            if (_evidenceCapture is not null)
            {
                try
                {
                    runObservation = await _evidenceCapture
                        .BeginRunAsync(flow, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Observing a run is optional and must never prevent replay.
                }
            }

            if (_options.ExecutionExtension is not null)
            {
                var preparation = await _options.ExecutionExtension
                    .PrepareAsync(flow, cancellationToken)
                    .ConfigureAwait(false);
                if (!preparation.Success)
                {
                    var message = preparation.Error ?? "The host execution preconditions were not satisfied.";
                    var legacyFailure = new FlowStepResult
                    {
                        Seq = 0,
                        Action = "prepare-host",
                        Label = "Prepare host environment",
                        Ok = false,
                        FailureKind = preparation.FailureKind ?? FlowFailureKinds.Drive,
                        Error = message,
                    };
                    legacy.Results.Add(legacyFailure);
                    legacy.Failed = 1;
                    legacy.StoppedEarly = true;
                    failedLegacyStep = legacyFailure;
                    report.Failure = MauiFlowFailureClassifier.ToFailure(
                        MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
                        {
                            LegacyFailureKind = legacyFailure.FailureKind,
                        }),
                        $"failure-{runId}",
                        legacyFailure.FailureKind,
                        "host-preparation",
                        _options.Clock.GetUtcNow(),
                        message);
                    AddEvent(report, "host-preparation-failed", message, "host-preparation");
                    await StopRunObservationAsync().ConfigureAwait(false);
                    return await FinalizeAsync(
                        report,
                        legacy,
                        flow,
                        failedStep: null,
                        failedLegacyStep: legacyFailure,
                        outcome: MauiFlowRunOutcomes.Failed,
                        summary: "Host environment preparation failed.",
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }

            report.Target = await CreateTargetAsync(flow, cancellationToken).ConfigureAwait(false);

            foreach (var step in flow.Steps ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                var replayStep = step ?? throw new InvalidOperationException("A validated flow cannot contain a null step.");
                var stepStartedAt = _options.Clock.GetUtcNow();
                var stopwatch = Stopwatch.StartNew();
                var actionability = new List<MauiFlowActionabilityAttempt>();
                activeStep = replayStep;
                activeStepStartedAt = stepStartedAt;
                activeStopwatch = stopwatch;
                activeActionability = actionability;
                var actionabilitySequence = 0;
                ReportProgress(
                    runId,
                    replayStep,
                    report.Steps.Count,
                    flow.Steps?.Count ?? 0,
                    "step-started");
                var engine = new FlowActionabilityEngine(
                    _driver,
                    _options.PollTries,
                    _options.PollGapMs,
                    observation => actionabilitySequence = AddActionabilityAttempt(
                        actionability,
                        actionabilitySequence,
                        observation,
                        report));
                var observedCheckpoint = await TryGetCheckpointAsync(cancellationToken).ConfigureAwait(false);
                activeObservedCheckpoint = observedCheckpoint;
                AddEvent(report, "step-started", $"Started {MauiFlowReportRedactor.SafeIdentifier(replayStep.Action) ?? "step"}.", StepId(replayStep));

                var legacyStep = new FlowStepResult
                {
                    Seq = replayStep.Seq,
                    Action = replayStep.Action,
                    Label = FlowMarkdown.Label(replayStep),
                };
                var priorCommandSequence = _driver.LastWorkflowCommandReceipt?.Sequence;
                var drive = await DriveAsync(replayStep, engine, cancellationToken).ConfigureAwait(false);
                var observedReceipt = drive.Receipt ?? _driver.LastWorkflowCommandReceipt;
                var receipt = drive.Receipt ??
                    (observedReceipt is not null && observedReceipt.Sequence != priorCommandSequence
                        ? observedReceipt
                        : null);
                if (receipt is not null)
                {
                    legacyStep.CommandId = receipt.CommandId;
                    legacyStep.ActionDigest = receipt.ActionDigest;
                    legacyStep.AuthorityEpoch = receipt.AuthorityEpoch;
                    legacyStep.AcknowledgementState = receipt.AcknowledgementState;
                }

                legacyStep.MatchCount = drive.Target?.MatchCount;
                legacyStep.SelectorQuality = drive.Target?.Quality;
                string? assertionTargetResolution = null;
                if (!drive.Ok)
                {
                    legacyStep.Ok = false;
                    legacyStep.Error = drive.Error;
                    legacyStep.FailureKind = drive.Kind;
                    foreach (var assertion in replayStep.Asserts ?? Enumerable.Empty<FlowAssert>())
                    {
                        legacyStep.Asserts.Add(new FlowAssertResult
                        {
                            Kind = assertion.Kind,
                            Skipped = true,
                            Name = assertion.Name,
                            Expected = assertion.Expected,
                        });
                    }
                }
                else
                {
                    legacyStep.Ok = true;
                    foreach (var assertion in replayStep.Asserts ?? Enumerable.Empty<FlowAssert>())
                    {
                        if (!assertion.Verify)
                        {
                            legacyStep.Asserts.Add(new FlowAssertResult
                            {
                                Kind = assertion.Kind,
                                Skipped = true,
                                Name = assertion.Name,
                                Expected = assertion.Expected,
                            });
                            continue;
                        }

                        var assertionResult = await VerifyAsync(assertion, cancellationToken).ConfigureAwait(false);
                        legacyStep.Asserts.Add(assertionResult);
                        if (assertionResult.Ok == false)
                        {
                            legacyStep.Ok = false;
                            if (FlowValidator.VerifiableAssertKinds.Contains(assertion.Kind))
                            {
                                // The step is attributed to the first assertion that failed, so
                                // the resolution outcome is taken from that same assertion.
                                if (legacyStep.FailureKind is null)
                                    assertionTargetResolution = assertionResult.TargetStatus;
                                legacyStep.FailureKind ??= FlowFailureKinds.Assertion;
                                legacyStep.Error ??= $"{assertion.Kind} assertion failed.";
                            }
                            else
                            {
                                // An unevaluatable kind is an authoring defect, not an app regression.
                                legacyStep.FailureKind ??= FlowFailureKinds.Validation;
                                legacyStep.Error ??= $"assert kind '{assertion.Kind}' is marked verify:true but cannot be evaluated on replay.";
                            }
                        }
                    }
                }

                if (legacyStep.Ok && _options.ExecutionExtension is not null)
                {
                    var hostStep = await _options.ExecutionExtension
                        .AfterStepAsync(flow, replayStep, legacyStep, cancellationToken)
                        .ConfigureAwait(false);
                    if (!hostStep.Success)
                    {
                        legacyStep.Ok = false;
                        legacyStep.FailureKind = hostStep.FailureKind ?? FlowFailureKinds.Drive;
                        legacyStep.Error = hostStep.Error ?? "Host execution failed after the app step.";
                    }
                }

                var completedAt = _options.Clock.GetUtcNow();
                var classification = legacyStep.Ok
                    ? null
                    : MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
                    {
                        LegacyFailureKind = legacyStep.FailureKind,
                        AssertionTargetResolution = assertionTargetResolution,
                        BeforeDispatch = receipt is null,
                        CompletionCertain = drive.Kind == FlowFailureKinds.UnknownCompletion ? false : null,
                        CheckpointVerified = observedCheckpoint is not null && expectedCheckpoint is not null,
                        CheckpointMatches = CheckpointsMatch(expectedCheckpoint, observedCheckpoint),
                        RouteMatches = RoutesMatch(expectedCheckpoint, observedCheckpoint),
                    });
                var structuredStep = CreateStructuredStep(
                    replayStep,
                    stepStartedAt,
                    completedAt,
                    stopwatch.ElapsedMilliseconds,
                    drive,
                    receipt,
                    actionability,
                    legacyStep,
                    expectedCheckpoint,
                    observedCheckpoint,
                    classification);
                await AttachSelectorEvidenceAsync(
                    structuredStep,
                    drive.Target,
                    drive.SelectorObservation,
                    report.Target,
                    observedCheckpoint,
                    report,
                    cancellationToken).ConfigureAwait(false);
                report.Steps.Add(structuredStep);
                legacy.Results.Add(legacyStep);
                activeStep = null;
                activeLegacyStep = null;
                activeActionability = null;
                activeStepStartedAt = null;
                activeStopwatch = null;
                activeObservedCheckpoint = null;
                if (legacyStep.Ok)
                    legacy.Passed++;
                else
                    legacy.Failed++;

                AddEvent(
                    report,
                    legacyStep.Ok ? "step-passed" : "step-failed",
                    legacyStep.Ok ? "Step completed." : "Step diverged.",
                    StepId(replayStep));
                ReportProgress(
                    runId,
                    replayStep,
                    report.Steps.Count,
                    flow.Steps?.Count ?? 0,
                    "step-completed");
                if (_options.StepObservationDelayMs > 0 &&
                    report.Steps.Count < (flow.Steps?.Count ?? 0))
                {
                    await Task.Delay(
                        Math.Clamp(_options.StepObservationDelayMs, 0, 5_000),
                        cancellationToken).ConfigureAwait(false);
                }

                if (!legacyStep.Ok)
                {
                    legacy.DivergencePoint ??= replayStep.Seq;
                    report.DivergenceStepId ??= StepId(replayStep);
                    if (report.Failure is null && classification is not null)
                    {
                        report.Failure = MauiFlowFailureClassifier.ToFailure(
                            classification,
                            $"failure-{runId}",
                            legacyStep.FailureKind,
                            StepId(replayStep),
                            completedAt,
                            legacyStep.Error);
                    }

                    failedStep ??= replayStep;
                    failedLegacyStep ??= legacyStep;
                    if (!_options.ContinueOnFailure)
                    {
                        legacy.StoppedEarly = true;
                        break;
                    }
                }

            }

            legacy.Ok = legacy.Failed == 0;
            await StopRunObservationAsync().ConfigureAwait(false);
            return await FinalizeAsync(
                report,
                legacy,
                flow,
                failedStep,
                failedLegacyStep,
                legacy.Ok ? MauiFlowRunOutcomes.Passed : MauiFlowRunOutcomes.Failed,
                legacy.Ok ? "Flow replay passed." : "Flow replay reported a divergence.",
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_options.ThrowOnCancellation)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            if (activeStep is not null && !report.Steps.Any(item =>
                    string.Equals(item.StepId, StepId(activeStep), StringComparison.Ordinal)))
            {
                activeLegacyStep = new FlowStepResult
                {
                    Seq = activeStep.Seq,
                    Action = activeStep.Action,
                    Label = FlowMarkdown.Label(activeStep),
                    Ok = false,
                    FailureKind = FlowFailureKinds.Drive,
                    Error = "Flow run cancelled.",
                };
                var interruptedAt = _options.Clock.GetUtcNow();
                var classification = MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
                {
                    TerminalOutcome = MauiFlowRunOutcomes.Cancelled,
                    LegacyFailureKind = activeLegacyStep.FailureKind,
                });
                report.Steps.Add(CreateStructuredStep(
                    activeStep,
                    activeStepStartedAt ?? report.StartedAt ?? interruptedAt,
                    interruptedAt,
                    activeStopwatch?.ElapsedMilliseconds ?? 0,
                    DriveResult.Failure(FlowFailureKinds.Drive, "Flow run cancelled."),
                    receipt: null,
                    activeActionability ?? [],
                    activeLegacyStep,
                    expectedCheckpoint,
                    activeObservedCheckpoint,
                    classification));
                report.DivergenceStepId ??= StepId(activeStep);
                legacy.DivergencePoint ??= activeStep.Seq;
                failedStep ??= activeStep;
                failedLegacyStep ??= activeLegacyStep;
                legacy.Results.Add(activeLegacyStep);
            }
            legacy.Ok = false;
            legacy.StoppedEarly = true;
            legacy.Failed = Math.Max(legacy.Failed, 1);
            await StopRunObservationAsync().ConfigureAwait(false);
            return await FinalizeAsync(
                report,
                legacy,
                flow,
                failedStep,
                failedLegacyStep,
                MauiFlowRunOutcomes.Cancelled,
                "Flow run cancelled.",
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            legacy.Ok = false;
            legacy.StoppedEarly = true;
            legacy.Failed = Math.Max(legacy.Failed, 1);
            report.Failure ??= MauiFlowFailureClassifier.ToFailure(
                MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts()),
                $"failure-{runId}",
                null,
                report.DivergenceStepId,
                _options.Clock.GetUtcNow(),
                InfrastructureErrorMessage);
            await StopRunObservationAsync().ConfigureAwait(false);
            return await FinalizeAsync(
                report,
                legacy,
                flow,
                failedStep,
                failedLegacyStep,
                MauiFlowRunOutcomes.InfrastructureError,
                "Flow runner infrastructure error.",
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await StopRunObservationAsync().ConfigureAwait(false);
        }
    }

    private static bool RequiresHostExecution(MauiFlow flow) =>
        flow.ExtensionData is { } extensions &&
        (extensions.ContainsKey("devicePreconditions") || extensions.ContainsKey("deviceSteps"));

    private void ReportProgress(
        string runId,
        FlowStep step,
        int completedSteps,
        int totalSteps,
        string phase)
    {
        try
        {
            _options.Progress?.Invoke(new MauiFlowRunProgress
            {
                RunId = runId,
                StepId = StepId(step),
                Sequence = step.Seq,
                CompletedSteps = Math.Max(0, completedSteps),
                TotalSteps = Math.Max(0, totalSteps),
                Phase = phase,
            });
        }
        catch
        {
            // A UI/status observer must never affect canonical replay semantics.
        }
    }

    /// <summary>Finds the recorded attempt for a step id, so run-level facts agree with the step.</summary>
    private static MauiFlowStepAttempt? FindStepAttempt(MauiFlowRunReport report, string? stepId)
        => string.IsNullOrWhiteSpace(stepId)
            ? null
            : (report.Steps ?? []).FirstOrDefault(step =>
                string.Equals(step?.StepId, stepId, StringComparison.Ordinal));

    private async Task<MauiFlowRunExecutionResult> FinalizeAsync(
        MauiFlowRunReport report,
        FlowReplayReport legacy,
        MauiFlow flow,
        FlowStep? failedStep,
        FlowStepResult? failedLegacyStep,
        string outcome,
        string summary,
        CancellationToken cancellationToken)
    {
        var endedAt = _options.Clock.GetUtcNow();
        report.EndedAt = endedAt;
        report.Outcome = new MauiFlowRunOutcome
        {
            Status = outcome,
            Summary = summary,
            Terminal = true,
        };
        if (!legacy.Ok && report.Failure is null)
        {
            // Belt and braces: the step loop already stamps the corrected class into report.Failure,
            // so this branch is reached only when no step recorded one (cancellation or an
            // infrastructure error). Passing the fact here keeps the two paths from disagreeing.
            var classification = MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
            {
                TerminalOutcome = outcome,
                LegacyFailureKind = failedLegacyStep?.FailureKind,
                AssertionTargetResolution = MauiFlowFailureClassifier.AssertionTargetResolutionOf(
                    FindStepAttempt(report, report.DivergenceStepId)),
            });
            report.Failure = MauiFlowFailureClassifier.ToFailure(
                classification,
                $"failure-{report.RunId}",
                failedLegacyStep?.FailureKind,
                report.DivergenceStepId,
                endedAt,
                summary);
        }
        else if (!legacy.Ok && report.Failure is not null &&
            outcome != MauiFlowRunOutcomes.Failed)
        {
            var classification = MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
            {
                TerminalOutcome = outcome,
                LegacyFailureKind = report.Failure.LegacyKind,
                AssertionTargetResolution = MauiFlowFailureClassifier.AssertionTargetResolutionOf(
                    FindStepAttempt(report, report.Failure.StepId)),
            });
            report.Failure = MauiFlowFailureClassifier.ToFailure(
                classification,
                report.Failure.FailureId,
                report.Failure.LegacyKind,
                report.Failure.StepId,
                endedAt,
                summary);
        }
        // The decision available here is provisional: this run's independent business oracles have
        // not been evaluated yet, so a flow that requires one is necessarily not yet repair-eligible.
        // Hosts that evaluate those oracles re-apply the gate once the real decision exists.
        MauiFlowFailureClassifier.ApplyRepairEligibilityGate(report);
        var verified = outcome == MauiFlowRunOutcomes.Passed &&
            report.ReplayEligibility?.RunVerificationAllowed == true;
        var verificationReason = VerificationReason(outcome, report.ReplayEligibility, verified);
        report.Outcome.Verified = verified;
        report.Outcome.VerificationReason = verificationReason;
        report.Verification = new MauiFlowRunVerification
        {
            Verified = verified,
            Reason = verificationReason,
            CheckedAt = endedAt,
        };
        AddEvent(report, "terminal", summary, report.DivergenceStepId);

        if (_evidenceCapture is IFlowRunEvidenceCapture completedCapture &&
            completedCapture.CapturedArtifact is { } completedArtifact)
        {
            report.Artifacts.Add(completedArtifact);
        }

        if (!legacy.Ok && failedStep is not null && failedLegacyStep is not null && _evidenceCapture is not null)
        {
            try
            {
                failedLegacyStep.FailureCause = await _evidenceCapture
                    .ExplainFailureAsync(flow, failedStep, failedLegacyStep, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_options.ThrowOnCancellation)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // An optional explanation must never mask the original replay failure.
            }
        }

        report.ExpectedEvidence = MauiFlowExpectedEvidenceEvaluator.Evaluate(
            flow,
            report,
            (_evidenceCapture as IFlowRunEvidenceCapture)?.CapturedEvidenceKinds);

        MauiFlowRunReportSerializer.ApplyLimits(report, _options.ReportLimits);
        var reportDigest = MauiFlowRunReportSerializer.ComputeDigest(report);
        report.ReportDigest = reportDigest;
        string? reportPath = null;
        if (!string.IsNullOrWhiteSpace(_options.ArtifactRoot))
        {
            var write = MauiFlowRunReportSerializer.WriteAtomic(report, _options.ArtifactRoot!, _options.ReportLimits);
            if (write.Ok)
            {
                reportPath = write.Path;
                reportDigest = write.Digest ?? reportDigest;
                report.ReportPath = reportPath;
                report.ReportDigest = reportDigest;
            }
            else
            {
                report.Truncated = true;
                report.Omissions.Add(new MauiFlowReportOmission
                {
                    Kind = "report-artifact",
                    Reason = write.Error ?? "The report artifact could not be written.",
                });
            }
        }

        legacy.StructuredReport = report;
        legacy.ReportDigest = reportDigest;
        legacy.ReportPath = reportPath;
        if (!legacy.Ok && failedStep is not null && failedLegacyStep is not null && _evidenceCapture is not null)
        {
            try
            {
                if (_evidenceCapture is IFlowRunEvidenceCapture detailed)
                {
                    await detailed.CaptureOnRunFailureAsync(new MauiFlowRunEvidenceContext
                    {
                        Report = report,
                        ReportDigest = reportDigest,
                        ReportPath = reportPath,
                        Flow = flow,
                        FailedStep = failedStep,
                        LegacyStepResult = failedLegacyStep,
                    }, cancellationToken).ConfigureAwait(false);
                    if (detailed.CapturedArtifact is not null &&
                        !report.Artifacts.Any(artifact =>
                            string.Equals(
                                artifact.ArtifactId,
                                detailed.CapturedArtifact.ArtifactId,
                                StringComparison.Ordinal)))
                    {
                        report.Artifacts.Add(detailed.CapturedArtifact);
                        // The capture is the only thing that can satisfy a collection expectation,
                        // so the declared-evidence verdict is recomputed once its results exist.
                        report.ExpectedEvidence = MauiFlowExpectedEvidenceEvaluator.Evaluate(
                            flow,
                            report,
                            detailed.CapturedEvidenceKinds);
                        MauiFlowRunReportSerializer.ApplyLimits(report, _options.ReportLimits);
                        reportDigest = MauiFlowRunReportSerializer.ComputeDigest(report);
                        report.ReportDigest = reportDigest;
                        if (!string.IsNullOrWhiteSpace(_options.ArtifactRoot))
                        {
                            var rewritten = MauiFlowRunReportSerializer.WriteAtomic(
                                report,
                                _options.ArtifactRoot!,
                                _options.ReportLimits);
                            if (rewritten.Ok)
                            {
                                reportPath = rewritten.Path;
                                reportDigest = rewritten.Digest ?? reportDigest;
                                report.ReportPath = reportPath;
                                report.ReportDigest = reportDigest;
                            }
                        }
                    }
                }
                else
                {
                    await _evidenceCapture.CaptureOnFailureAsync(
                        flow,
                        failedStep,
                        failedLegacyStep,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_options.ThrowOnCancellation)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                report.Truncated = true;
                report.Omissions.Add(new MauiFlowReportOmission
                {
                    Kind = "failure-evidence",
                    Reason = "Failure evidence could not be captured.",
                });
            }
        }

        legacy.StructuredReport = report;
        legacy.ReportDigest = reportDigest;
        legacy.ReportPath = reportPath;
        return new MauiFlowRunExecutionResult
        {
            Report = report,
            LegacyReport = legacy,
            ReportDigest = reportDigest,
            ReportPath = reportPath,
        };
    }

    private static string VerificationReason(
        string outcome,
        MauiFlowReplayEligibilityDecision? decision,
        bool verified)
    {
        if (verified)
        {
            return "Required scenario, acceptance-criterion, and independent business-oracle coverage verified the run.";
        }
        if (!string.Equals(outcome, MauiFlowRunOutcomes.Passed, StringComparison.Ordinal))
            return "The execution did not pass, so it was not independently verified.";

        var reasonCodes = (decision?.Reasons ?? [])
            .Where(static reason => string.Equals(reason.Scope, "verification", StringComparison.Ordinal))
            .Select(static reason => MauiFlowReportRedactor.SafeIdentifier(reason.Code))
            .Where(static code => code is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static code => code, StringComparer.Ordinal)
            .ToList();
        return reasonCodes.Count == 0
            ? "The execution passed, but independent verification requirements were not satisfied."
            : "The execution passed, but independent verification requirements were not satisfied: " +
              string.Join(", ", reasonCodes) + ".";
    }

    private async Task<DriveResult> DriveAsync(
        FlowStep step,
        FlowActionabilityEngine actionability,
        CancellationToken cancellationToken)
    {
        try
        {
            var args = step.Args;
            switch (step.Action)
            {
                case FlowActions.Tap:
                {
                    var target = await actionability.WaitForActionableAsync(
                        FlowValidator.EffectiveSelector(step),
                        requireStableBounds: true,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (!target.Ok)
                        return DriveResult.FromTarget("tap", target);
                    var observation = await CaptureSelectorObservationAtResolutionAsync(target, cancellationToken).ConfigureAwait(false);
                    return await _driver.TapAsync(target.Element!.Id).ConfigureAwait(false)
                        ? DriveResult.Success(target, observation)
                        : DriveResult.Failure(FlowFailureKinds.Drive, "tap reported failure", target, observation: observation);
                }
                case FlowActions.Fill:
                {
                    var target = await actionability.WaitForActionableAsync(
                        FlowValidator.EffectiveSelector(step),
                        requireStableBounds: false,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (!target.Ok)
                        return DriveResult.FromTarget("fill", target);
                    var observation = await CaptureSelectorObservationAtResolutionAsync(target, cancellationToken).ConfigureAwait(false);
                    var value = ResolveStepValue(step, args?.Text ?? step.Value, out var secretError);
                    if (secretError is not null)
                        return DriveResult.Failure(FlowFailureKinds.SecretRequired, secretError, target, observation: observation);
                    return await _driver.FillAsync(target.Element!.Id, value ?? string.Empty).ConfigureAwait(false)
                        ? DriveResult.Success(target, observation)
                        : DriveResult.Failure(FlowFailureKinds.Drive, "fill reported failure", target, observation: observation);
                }
                case FlowActions.SetProperty:
                {
                    var target = await actionability.WaitForResolvedAsync(
                        FlowValidator.EffectiveSelector(step),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (!target.Ok)
                        return DriveResult.FromTarget("setProperty", target);
                    var observation = await CaptureSelectorObservationAtResolutionAsync(target, cancellationToken).ConfigureAwait(false);
                    if (IsUnsafeValueSource(args?.ValueSource))
                        return DriveResult.Failure(
                            FlowFailureKinds.UnsafeValue,
                            "setProperty value came from an unsafe source and cannot be replayed.",
                            target,
                            observation: observation);
                    var name = string.IsNullOrEmpty(args?.Name) ? "Text" : args!.Name!;
                    var value = ResolveStepValue(step, args?.Value ?? step.Value, out var secretError);
                    if (secretError is not null)
                        return DriveResult.Failure(FlowFailureKinds.SecretRequired, secretError, target, observation: observation);
                    return await _driver.SetPropertyAsync(target.Element!.Id, name, value ?? string.Empty).ConfigureAwait(false)
                        ? DriveResult.Success(target, observation)
                        : DriveResult.Failure(FlowFailureKinds.Drive, "setProperty reported failure", target, observation: observation);
                }
                case FlowActions.Scroll:
                {
                    string? id = null;
                    FlowTargetResolution? target = null;
                    MauiSelectorObservation? observation = null;
                    var selector = FlowValidator.EffectiveSelector(step);
                    if (selector is not null && !selector.IsEmpty)
                    {
                        target = await actionability.WaitForActionableAsync(
                            selector,
                            requireStableBounds: false,
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                        if (!target.Ok)
                            return DriveResult.FromTarget("scroll", target);
                        id = target.Element!.Id;
                        observation = await CaptureSelectorObservationAtResolutionAsync(target, cancellationToken).ConfigureAwait(false);
                    }
                    var ok = await _driver.ScrollAsync(
                        id,
                        args?.Dx ?? 0,
                        args?.Dy ?? 0,
                        args?.Animated ?? false,
                        args?.ItemIndex,
                        args?.Position).ConfigureAwait(false);
                    return ok
                        ? DriveResult.Success(target, observation)
                        : DriveResult.Failure(FlowFailureKinds.Drive, "scroll reported failure", target, observation: observation);
                }
                case FlowActions.Navigate:
                {
                    var route = args?.Route ?? step.Value;
                    if (string.IsNullOrEmpty(route))
                        return DriveResult.Failure(FlowFailureKinds.Drive, "navigate requires a route");
                    return await _driver.NavigateAsync(route).ConfigureAwait(false)
                        ? DriveResult.Success()
                        : DriveResult.Failure(FlowFailureKinds.Drive, "navigate reported failure");
                }
                case FlowActions.Back:
                    return await _driver.BackAsync().ConfigureAwait(false)
                        ? DriveResult.Success()
                        : DriveResult.Failure(FlowFailureKinds.Drive, "back reported failure");
                case FlowActions.Assert:
                    return DriveResult.Success();
                case FlowActions.SetTheme:
                {
                    if (!TryParseTheme(args?.Theme ?? step.Value, out var theme))
                        return DriveResult.Failure(FlowFailureKinds.Drive, "setTheme requires light, dark, or system.");
                    var result = await _driver.SetThemeAsync(theme).ConfigureAwait(false);
                    return result.Success
                        ? DriveResult.Success()
                        : DriveResult.Failure(FlowFailureKinds.Drive, "setTheme reported failure");
                }
                default:
                    return DriveResult.Failure(FlowFailureKinds.Drive, "The flow action is not supported.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WorkflowCommandException ex)
        {
            return DriveResult.Failure(
                ex.IsUnknownCompletion
                    ? FlowFailureKinds.UnknownCompletion
                    : FlowFailureKinds.WorkflowCommandConflict,
                ex.IsUnknownCompletion
                    ? "Workflow command completion is unknown."
                    : "The workflow command conflicted with another command.",
                receipt: ex.Receipt);
        }
        catch (Exception)
        {
            return DriveResult.Failure(FlowFailureKinds.Drive, "The driver operation failed.");
        }
    }

    private async Task<MauiSelectorObservation?> CaptureSelectorObservationAtResolutionAsync(
        FlowTargetResolution resolution,
        CancellationToken cancellationToken)
    {
        if (resolution.Element is null)
            return null;
        try
        {
            var tree = await _driver.GetTreeAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return MauiSelectorObservationFactory.Create(
                resolution.Element,
                tree,
                context: null,
                truncated: tree.Count == 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Selector evidence is optional and must not alter canonical action dispatch.
            return null;
        }
    }

    private async Task<FlowAssertResult> VerifyAsync(FlowAssert assertion, CancellationToken cancellationToken)
    {
        var result = new FlowAssertResult
        {
            Kind = assertion.Kind,
            Name = assertion.Name,
            Expected = assertion.Expected,
            TargetHint = FlowAssertTargetHint.Describe(assertion.Selector),
        };
        for (var attempt = 0; attempt < Math.Max(1, _options.PollTries); attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (assertion.Kind == "propEquals")
                {
                    var resolution = await ResolveAssertionTargetAsync(result, assertion.Selector, cancellationToken).ConfigureAwait(false);
                    if (resolution.Ok)
                    {
                        var actual = await _driver.GetPropertyAsync(
                            resolution.Element!.Id,
                            string.IsNullOrEmpty(assertion.Name) ? "Text" : assertion.Name!).ConfigureAwait(false);
                        result.Actual = actual;
                        if (FlowReplayer.PropertyValuesEqual(actual, assertion.Expected))
                        {
                            result.Ok = true;
                            return result;
                        }
                    }
                }
                else if (assertion.Kind == "exists")
                {
                    var resolution = await ResolveAssertionTargetAsync(result, assertion.Selector, cancellationToken).ConfigureAwait(false);
                    if (resolution.Ok)
                    {
                        result.Ok = true;
                        return result;
                    }
                }
                else if (assertion.Kind == "notExists")
                {
                    var resolution = await new FlowActionabilityEngine(_driver, 1, 0)
                        .ResolveAsync(assertion.Selector, cancellationToken)
                        .ConfigureAwait(false);
                    result.Actual = resolution.MatchCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (!resolution.Ok &&
                        string.Equals(resolution.Kind, FlowFailureKinds.NotFound, StringComparison.Ordinal))
                    {
                        result.Ok = true;
                        return result;
                    }
                }
                else if (assertion.Kind == "routeIs")
                {
                    var route = (await _driver.GetStatusAsync().ConfigureAwait(false))?.Route;
                    result.Actual = route;
                    if (string.Equals(route, assertion.Expected, StringComparison.Ordinal))
                    {
                        result.Ok = true;
                        return result;
                    }
                }
                else
                {
                    // A verify:true assertion we cannot evaluate fails closed. Skipping it would
                    // report a green step that checked nothing.
                    result.Ok = false;
                    return result;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // The observed assertion state is retried; raw exception text is not persisted.
            }

            if (attempt < Math.Max(1, _options.PollTries) - 1)
                await Task.Delay(Math.Max(0, _options.PollGapMs), cancellationToken).ConfigureAwait(false);
        }
        result.Ok = false;
        return result;
    }

    /// <summary>
    /// Resolves an assertion's own selector and records the outcome on the result. A failed
    /// assertion whose selector never resolved read no value from the app, so recording the
    /// resolution is what lets triage separate a drifted assertion selector from a real
    /// behavioural change.
    /// </summary>
    private async Task<FlowTargetResolution> ResolveAssertionTargetAsync(
        FlowAssertResult result,
        FlowSelector? selector,
        CancellationToken cancellationToken)
    {
        var actionability = new FlowActionabilityEngine(_driver, _options.PollTries, _options.PollGapMs);
        var resolution = await actionability.ResolveAsync(selector, cancellationToken).ConfigureAwait(false);
        result.TargetStatus = resolution.Ok ? FlowAssertTargetStatuses.Resolved : resolution.Kind;
        result.TargetMatchCount = resolution.MatchCount;
        return resolution;
    }

    private MauiFlowStepAttempt CreateStructuredStep(
        FlowStep step,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        long durationMs,
        DriveResult drive,
        WorkflowCommandReceipt? receipt,
        List<MauiFlowActionabilityAttempt> actionability,
        FlowStepResult legacy,
        MauiFlowCheckpoint? expectedCheckpoint,
        MauiFlowCheckpoint? observedCheckpoint,
        MauiFlowFailureClassification? classification)
    {
        var target = drive.Target;
        var candidates = target?.Candidates ?? [];
        var stepAttempt = new MauiFlowStepAttempt
        {
            StepId = StepId(step),
            Sequence = step.Seq,
            Action = MauiFlowReportRedactor.SafeIdentifier(step.Action),
            Intent = DescribeIntent(step),
            StartedAt = startedAt,
            EndedAt = endedAt,
            DurationMs = Math.Max(0, durationMs),
            Selector = MauiFlowReportRedactor.SanitizeSelector(FlowValidator.EffectiveSelector(step)),
            SelectorRequest = MauiFlowReportRedactor.DescribeSelector(FlowValidator.EffectiveSelector(step)),
            CandidateCount = target?.MatchCount,
            CandidateSummary = target is null ? null : new MauiFlowCandidateSummary
            {
                Count = target.MatchCount,
                Types = candidates.Take(8)
                    .Select(static candidate => MauiFlowReportRedactor.SafeIdentifier(candidate.Type) ?? "element")
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                Final = target.Element is null
                    ? null
                    : MauiFlowReportRedactor.DescribeElement(
                        target.Element.Type,
                        target.Element.AutomationId,
                        target.Element.Id),
            },
            TargetResolution = target is null ? null : new MauiFlowTargetResolution
            {
                Status = target.Ok ? "resolved" : MauiFlowReportRedactor.SafeIdentifier(target.Kind),
                MatchCount = target.MatchCount,
                ElementId = target.Element?.Id is null
                    ? null
                    : "sha256:" + ShortDigest(target.Element.Id),
                Message = MauiFlowReportRedactor.SafeMessage(target.Error),
                FinalResolution = target.Element is null
                    ? null
                    : MauiFlowReportRedactor.DescribeElement(
                        target.Element.Type,
                        target.Element.AutomationId,
                        target.Element.Id),
            },
            Actionability = actionability,
            Dispatch = receipt is null ? null : new MauiFlowDispatchReceipt
            {
                CommandId = MauiFlowReportRedactor.SafeIdentifier(receipt.CommandId),
                Sequence = receipt.Sequence,
                ActionDigest = MauiFlowReportRedactor.SafeIdentifier(receipt.ActionDigest),
                AuthorityEpoch = receipt.AuthorityEpoch,
                AcknowledgementState = MauiFlowReportRedactor.SafeIdentifier(receipt.AcknowledgementState),
                CompletionCertainty = legacy.FailureKind == FlowFailureKinds.UnknownCompletion ||
                    string.Equals(receipt.AcknowledgementState, "unknown-completion", StringComparison.Ordinal)
                    ? "unknown"
                    : "completed",
                ReceivedAt = endedAt,
            },
            ExpectedCheckpoint = expectedCheckpoint,
            ObservedCheckpoint = observedCheckpoint,
            CommandId = receipt is null ? null : MauiFlowReportRedactor.SafeIdentifier(receipt.CommandId),
            CommandSequence = receipt?.Sequence,
            ActionDigest = receipt is null ? null : MauiFlowReportRedactor.SafeIdentifier(receipt.ActionDigest),
            AuthorityEpoch = receipt?.AuthorityEpoch,
            AcknowledgementState = receipt is null ? null : MauiFlowReportRedactor.SafeIdentifier(receipt.AcknowledgementState),
            CompletionCertainty = receipt is null
                ? (legacy.FailureKind == FlowFailureKinds.UnknownCompletion ? "unknown" : null)
                : legacy.FailureKind == FlowFailureKinds.UnknownCompletion ? "unknown" : "completed",
            FailureClass = classification?.FailureClass,
            Assertions = legacy.Asserts.Select(CreateStructuredAssertion).ToList(),
        };
        return stepAttempt;
    }

    private async Task AttachSelectorEvidenceAsync(
        MauiFlowStepAttempt step,
        FlowTargetResolution? resolution,
        MauiSelectorObservation? capturedObservation,
        MauiFlowRunTarget? target,
        MauiFlowCheckpoint? checkpoint,
        MauiFlowRunReport report,
        CancellationToken cancellationToken)
    {
        if (resolution?.Element is null)
        {
            if (resolution is not null && resolution.MatchCount > 1)
            {
                step.SelectorCandidateOmissions.Add(new MauiSelectorEvidenceOmission
                {
                    Kind = "selector-candidates",
                    Reason = "No candidate fingerprint was captured because normal replay rejected an ambiguous selector.",
                    Count = resolution.MatchCount,
                });
                IncrementSelectorHealthSummary(report, step);
            }
            return;
        }

        try
        {
            var context = new MauiSelectorObservationContext
            {
                AppId = target?.AppId,
                AppBuild = target?.AppBuildFingerprint,
                Platform = target?.Platform,
                Route = checkpoint?.Route,
                Window = checkpoint?.Window,
                Modal = checkpoint?.Modal,
                Locale = target?.Locale ?? checkpoint?.Locale,
                Theme = target?.Theme ?? checkpoint?.Theme,
                Orientation = target?.Orientation ?? checkpoint?.Orientation,
                DisplayProfile = target?.DisplayProfile ?? checkpoint?.DisplayProfile,
                CapabilityVersion = "flow-run-selector-evidence-v1",
                ObservedAt = _options.Clock.GetUtcNow(),
            };
            var observation = capturedObservation;
            if (observation is null)
            {
                var tree = await _driver.GetTreeAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                observation = MauiSelectorObservationFactory.Create(
                    resolution.Element,
                    tree,
                    context,
                    truncated: tree.Count == 0);
            }
            else
            {
                observation.Context ??= context;
                observation.Context.AppId ??= context.AppId;
                observation.Context.AppBuild ??= context.AppBuild;
                observation.Context.Platform ??= context.Platform;
                observation.Context.Route ??= context.Route;
                observation.Context.Window ??= context.Window;
                observation.Context.Modal ??= context.Modal;
                observation.Context.Locale ??= context.Locale;
                observation.Context.Theme ??= context.Theme;
                observation.Context.Orientation ??= context.Orientation;
                observation.Context.DisplayProfile ??= context.DisplayProfile;
                observation.Context.CapabilityVersion ??= context.CapabilityVersion;
                observation.Context.ObservedAt ??= context.ObservedAt;
            }
            if (observation.Truncated == true)
            {
                step.Fingerprint = MauiElementFingerprintBuilder.Build(observation);
                step.SelectorCandidateOmissions.Add(new MauiSelectorEvidenceOmission
                {
                    Kind = "live-tree",
                    Reason = "The driver did not provide a tree, so selector uniqueness was not assumed and no candidate was generated.",
                });
            }
            else
            {
                var generated = MauiSelectorCandidateGenerator.Generate(
                    observation,
                    _options.SelectorCandidateOptions);
                step.Fingerprint = generated.Fingerprint;
                step.SelectorCandidates = generated.Candidates;
                step.SelectorCandidateOmissions = generated.Omissions;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            step.SelectorCandidateOmissions.Add(new MauiSelectorEvidenceOmission
            {
                Kind = "selector-candidates",
                Reason = "Selector evidence could not be captured; normal replay behavior was unchanged.",
            });
        }
        finally
        {
            IncrementSelectorHealthSummary(report, step);
        }
    }

    private static void IncrementSelectorHealthSummary(MauiFlowRunReport report, MauiFlowStepAttempt step)
    {
        var summary = report.SelectorHealth ??= new MauiFlowSelectorHealthSummary();
        if (step.Fingerprint is not null)
            summary.CapturedSteps++;
        summary.CandidateCount += step.SelectorCandidates.Count;
        summary.OmissionCount += step.SelectorCandidateOmissions.Count;
    }

    private static MauiFlowAssertionResult CreateStructuredAssertion(FlowAssertResult result)
    {
        // A failed assertion is the only case where the observed value carries the diagnosis. The
        // author already committed the expectation to the flow file, so disclosing the counterpart
        // withholds nothing from anyone who can read the test — and withholding it leaves a length
        // and a digest, which cannot be acted on. Passing and skipped assertions keep the strict
        // scalar-only rule, and every disclosure still has to clear the safe-text screen.
        var failed = result.Ok == false && !result.Skipped;
        var sensitive = FlowSecretReference.LooksSensitive(result.Name) ||
            FlowSecretReference.LooksSensitive(result.TargetHint);
        var expected = MauiFlowReportRedactor.DescribeValue(result.Expected, allowPlain: !sensitive, allowSafeText: failed && !sensitive);
        var actual = MauiFlowReportRedactor.DescribeValue(result.Actual, allowPlain: !sensitive, allowSafeText: failed && !sensitive);
        return new MauiFlowAssertionResult
        {
            Kind = MauiFlowReportRedactor.SafeIdentifier(result.Kind),
            Passed = result.Ok,
            Skipped = result.Skipped,
            Expected = expected.Value,
            Actual = actual.Value,
            ExpectedDisclosure = expected,
            ActualDisclosure = actual,
            // Without this a failed assertion is indistinguishable from an app regression, even
            // when the assertion's selector is what stopped matching and no value was ever read.
            TargetResolution = result.TargetStatus is null ? null : new MauiFlowTargetResolution
            {
                Status = MauiFlowReportRedactor.SafeIdentifier(result.TargetStatus),
                MatchCount = result.TargetMatchCount,
            },
            Message = result.Ok == false ? "Assertion did not match." : null,
        };
    }

    private async Task<MauiFlowRunTarget> CreateTargetAsync(MauiFlow flow, CancellationToken cancellationToken)
    {
        var target = CloneTarget(_options.Target) ?? new MauiFlowRunTarget
        {
            Platform = MauiFlowReportRedactor.SafeIdentifier(flow.Platform),
            AppId = MauiFlowReportRedactor.SafeIdentifier(flow.App),
        };
        try
        {
            var status = await _driver.GetStatusAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (status is null)
                return target;

            target.Platform ??= MauiFlowReportRedactor.SafeIdentifier(status.Device?.Platform);
            target.DeviceProfile ??= BuildDisplayProfile(status);
            target.AppId ??= MauiFlowReportRedactor.SafeIdentifier(status.App?.PackageId ?? status.App?.Name);
            target.AppBuildFingerprint ??= BuildFingerprint(status);
            target.AgentInstanceId ??= MauiFlowReportRedactor.SafeIdentifier(status.Agent?.InstanceId);
            target.Locale ??= MauiFlowReportRedactor.SafeIdentifier(status.Locale);
            target.Theme ??= MauiFlowReportRedactor.SafeIdentifier(status.Theme);
            target.Orientation ??= MauiFlowReportRedactor.SafeIdentifier(status.Orientation);
            target.DisplayProfile ??= MauiFlowReportRedactor.SafeIdentifier(status.DisplayProfile);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Target enrichment is best effort and must not turn a usable flow into a failure.
        }
        return target;
    }

    private async Task<MauiFlowCheckpoint?> TryGetCheckpointAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await _driver.GetStatusAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return status is null ? null : CreateCheckpoint(status);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Projects a live agent status into the checkpoint facts a run records and compares. Callers
    /// that observe a clean-state checkpoint outside the runner use this so both observations are
    /// produced by exactly the same projection.
    /// </summary>
    public static MauiFlowCheckpoint CreateCheckpoint(AgentStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new MauiFlowCheckpoint
        {
            AppBuildFingerprint = BuildFingerprint(status),
            AgentInstanceId = MauiFlowReportRedactor.SafeIdentifier(status.Agent?.InstanceId),
            Route = MauiFlowReportRedactor.SafeRoute(status.Route),
            Window = MauiFlowReportRedactor.SafeIdentifier(status.Window),
            Modal = MauiFlowReportRedactor.SafeIdentifier(status.Modal),
            Locale = MauiFlowReportRedactor.SafeIdentifier(status.Locale),
            Theme = MauiFlowReportRedactor.SafeIdentifier(status.Theme),
            Orientation = MauiFlowReportRedactor.SafeIdentifier(status.Orientation),
            DisplayProfile = MauiFlowReportRedactor.SafeDisplayProfile(status.DisplayProfile),
        };
    }

    private static int AddActionabilityAttempt(
        List<MauiFlowActionabilityAttempt> attempts,
        int sequence,
        FlowActionabilityObservation observation,
        MauiFlowRunReport report)
    {
        if (attempts.Count >= 64)
        {
            MarkOmission(report, "actionability", "The actionability-attempt limit was reached.");
            return sequence;
        }
        attempts.Add(new MauiFlowActionabilityAttempt
        {
            Sequence = ++sequence,
            Attempt = observation.Attempt,
            At = observation.At,
            Kind = MauiFlowReportRedactor.SafeIdentifier(observation.Kind),
            Passed = observation.Outcome is "resolved" or "stable",
            Resolved = observation.Resolved,
            Visible = observation.Visible,
            Enabled = observation.Enabled,
            HasBounds = observation.HasBounds,
            BoundsStable = observation.BoundsStable,
            WaitDurationMs = observation.WaitDurationMs,
            Outcome = MauiFlowReportRedactor.SafeIdentifier(observation.Outcome),
            Message = MauiFlowReportRedactor.SafeMessage(observation.Message),
        });
        return sequence;
    }

    private void AddEvent(MauiFlowRunReport report, string kind, string message, string? stepId = null)
    {
        if (report.Events.Count >= Math.Max(1, _options.ReportLimits.MaxEvents))
        {
            MarkOmission(report, "events", "The run-event limit was reached.");
            return;
        }
        report.Events.Add(new MauiFlowRunEvent
        {
            Sequence = report.Events.Count + 1,
            At = _options.Clock.GetUtcNow(),
            Kind = MauiFlowReportRedactor.SafeIdentifier(kind),
            Message = MauiFlowReportRedactor.SafeMessage(message, _options.ReportLimits.MaxTextLength),
            StepId = MauiFlowReportRedactor.SafeIdentifier(stepId),
        });
    }

    private static void MarkOmission(MauiFlowRunReport report, string kind, string reason)
    {
        report.Truncated = true;
        report.TruncationReason ??= reason;
        if (report.Omissions.Any(item => string.Equals(item.Kind, kind, StringComparison.Ordinal)))
            return;
        report.Omissions.Add(new MauiFlowReportOmission { Kind = kind, Reason = reason });
    }

    private static string DescribeIntent(FlowStep step) => step.Action switch
    {
        FlowActions.Tap => "Tap target.",
        FlowActions.Fill => "Fill target with a value descriptor.",
        FlowActions.SetProperty => "Set target property with a value descriptor.",
        FlowActions.Scroll => "Scroll target or current view.",
        FlowActions.Navigate => "Navigate to a route.",
        FlowActions.Back => "Navigate back.",
        FlowActions.SetTheme => "Set application theme.",
        FlowActions.Assert => "Run assertions.",
        _ => "Run flow action.",
    };

    private static string StepId(FlowStep step)
        => MauiFlowStepIdentity.Get(step);

    private string? ResolveStepValue(FlowStep step, string? literal, out string? error)
    {
        error = null;
        var variable = step.Args?.SecretEnvironmentVariable;
        if (variable is null)
            return literal;
        if (!FlowSecretReference.IsValidEnvironmentVariable(variable))
        {
            error = $"Step {step.Seq} has an invalid sensitive-input reference.";
            return null;
        }
        var value = _secretResolver(variable);
        if (value is null)
        {
            error = $"Step {step.Seq} requires sensitive input from an unavailable environment reference.";
            return null;
        }
        return value;
    }

    private static bool IsUnsafeValueSource(string? source)
        => source is not null &&
           (source.Contains("binding", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("resource", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("unsafe", StringComparison.OrdinalIgnoreCase));

    private static bool TryParseTheme(string? text, out DevFlowTheme theme)
    {
        theme = DevFlowTheme.System;
        switch (text?.Trim().ToLowerInvariant())
        {
            case "light": theme = DevFlowTheme.Light; return true;
            case "dark": theme = DevFlowTheme.Dark; return true;
            case "system": theme = DevFlowTheme.System; return true;
            default: return false;
        }
    }

    private static bool? CheckpointsMatch(MauiFlowCheckpoint? expected, MauiFlowCheckpoint? observed)
    {
        if (expected is null || observed is null)
            return null;
        // Seed, backend-state, and collection-item facts are supplied by host-owned providers and
        // remain on the recorded expected checkpoint for repair review. AgentStatus cannot observe
        // them per step, so comparing them here would turn every provider-backed locator failure
        // into precondition drift even after preflight verified those facts.
        return Matches(expected.AppBuildFingerprint, observed.AppBuildFingerprint) &&
            Matches(expected.AgentInstanceId, observed.AgentInstanceId) &&
            Matches(expected.Locale, observed.Locale) &&
            Matches(expected.Theme, observed.Theme) &&
            Matches(expected.Orientation, observed.Orientation) &&
            Matches(expected.DisplayProfile, observed.DisplayProfile);
    }

    private static bool? RoutesMatch(MauiFlowCheckpoint? expected, MauiFlowCheckpoint? observed)
    {
        if (expected?.Route is null || observed?.Route is null)
            return null;
        return string.Equals(expected.Route, observed.Route, StringComparison.Ordinal);
    }

    private static bool Matches(string? expected, string? observed)
        => expected is null || string.Equals(expected, observed, StringComparison.Ordinal);

    private static MauiFlowRunTarget? CloneTarget(MauiFlowRunTarget? target) => target is null
        ? null
        : new MauiFlowRunTarget
        {
            TargetId = target.TargetId,
            Platform = target.Platform,
            DeviceId = target.DeviceId,
            DeviceProfile = target.DeviceProfile,
            AppId = target.AppId,
            AppBuildFingerprint = target.AppBuildFingerprint,
            AppSourceFingerprint = target.AppSourceFingerprint,
            PackageDigest = target.PackageDigest,
            NormalizedPayloadDigest = target.NormalizedPayloadDigest,
            AgentId = target.AgentId,
            AgentInstanceId = target.AgentInstanceId,
            Locale = target.Locale,
            Theme = target.Theme,
            Orientation = target.Orientation,
            DisplayProfile = target.DisplayProfile,
        };

    private MauiFlowRunTarget CreateUnenrichedTarget(MauiFlow flow)
        => CloneTarget(_options.Target) ?? new MauiFlowRunTarget
        {
            Platform = MauiFlowReportRedactor.SafeIdentifier(flow.Platform),
            AppId = MauiFlowReportRedactor.SafeIdentifier(flow.App),
        };

    internal static string? BuildFingerprint(AgentStatus status)
    {
        var version = NormalizeFingerprintPart(MauiFlowReportRedactor.SafeIdentifier(status.App?.Version));
        var build = NormalizeFingerprintPart(MauiFlowReportRedactor.SafeIdentifier(status.App?.Build));
        return version is null && build is null ? null : $"{version ?? "unknown"}:{build ?? "unknown"}";
    }

    private static string? NormalizeFingerprintPart(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;

    private static string? BuildDisplayProfile(AgentStatus status)
    {
        var width = status.Device?.WindowWidth;
        var height = status.Device?.WindowHeight;
        return width is null || height is null
            ? null
            : $"{width.Value.ToString(CultureInfo.InvariantCulture)}x{height.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string ShortDigest(string value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];

    private static string CreateRunId()
        => "run_" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private sealed record DriveResult(
        bool Ok,
        string? Kind = null,
        string? Error = null,
        FlowTargetResolution? Target = null,
        WorkflowCommandReceipt? Receipt = null,
        MauiSelectorObservation? SelectorObservation = null)
    {
        public static DriveResult Success(
            FlowTargetResolution? target = null,
            MauiSelectorObservation? observation = null)
            => new(true, Target: target, SelectorObservation: observation);

        public static DriveResult Failure(
            string kind,
            string error,
            FlowTargetResolution? target = null,
            WorkflowCommandReceipt? receipt = null,
            MauiSelectorObservation? observation = null)
            => new(false, kind, error, target, receipt, observation);

        public static DriveResult FromTarget(string action, FlowTargetResolution target)
            => Failure(
                target.Kind,
                $"{action} target could not be resolved: {target.Error}",
                target);
    }
}
