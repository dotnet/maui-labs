using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.DevFlow.Driver;
using DeviceLayer = Microsoft.Maui.DevFlow.Devices;
using Testing = Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>Identifies the run whose independent business oracles are to be evaluated.</summary>
internal sealed record RunOracleSessionRequest(
    string RunId,
    Testing.MauiTestPlan Plan,
    WorkflowRunTarget Target);

/// <summary>
/// An open evaluation of one run's independent business oracles.
/// </summary>
/// <remarks>
/// The session is created before the flow runs so the evaluator can record what the declared
/// evidence already said. A run the broker merely attached to did not start from a freshly
/// installed app, so only the difference between the two observations can be attributed to it.
/// </remarks>
internal interface IWorkflowRunOracleSession
{
    Task<IReadOnlyList<Testing.MauiIndependentBusinessOracleResult>> EvaluateAsync(
        CancellationToken cancellationToken);
}

/// <summary>
/// Coordinates one bounded, mutating flow replay for a connected agent instance. It owns broker
/// lifecycle state, idempotency, the mutation-lease transaction, and retained reports; the public
/// Testing package remains the only replay implementation.
/// </summary>
internal sealed class WorkflowRunCoordinator : IDisposable
{
    private const int MaxLifecycleEvents = 128;

    private readonly object _gate = new();
    private readonly IWorkflowMutationLeaseRegistry _leases;
    private readonly Func<WorkflowRunExecution, CancellationToken, Task<Testing.FlowReplayReport>> _execute;
    private readonly Func<WorkflowRunLedgerControl, CancellationToken, Task<WorkflowRunLedgerControlResult>>? _controlLedger;
    private readonly WorkflowRunDispatchAuthorizer _authorizeDispatch;
    private readonly Func<RunOracleSessionRequest, CancellationToken, Task<IWorkflowRunOracleSession?>>? _beginOracleSession;
    private readonly WorkflowRunCoordinatorOptions _options;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, RunRecord> _runs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IdempotencyEntry> _idempotency = new(StringComparer.Ordinal);
    private readonly Dictionary<WorkflowRunTargetKey, string> _activeTargets = [];
    private long _terminalOrder;
    private bool _disposed;

    public WorkflowRunCoordinator(
        IWorkflowMutationLeaseRegistry leases,
        Func<WorkflowRunExecution, CancellationToken, Task<Testing.FlowReplayReport>> execute,
        WorkflowRunCoordinatorOptions? options = null,
        TimeProvider? clock = null,
        Func<WorkflowRunLedgerControl, CancellationToken, Task<WorkflowRunLedgerControlResult>>? controlLedger = null,
        WorkflowRunDispatchAuthorizer? authorizeDispatch = null,
        Func<RunOracleSessionRequest, CancellationToken, Task<IWorkflowRunOracleSession?>>? beginOracleSession = null)
    {
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _controlLedger = controlLedger;
        _beginOracleSession = beginOracleSession;
        // A coordinator without an authorizer refuses every start. Authorization is a precondition
        // of this type rather than of one HTTP route, so a host that forgets to wire it up fails
        // closed instead of silently dispatching device-mutating runs for nobody.
        _authorizeDispatch = authorizeDispatch ?? DenyUnconfiguredDispatch;
        _options = options ?? new WorkflowRunCoordinatorOptions();
        _clock = clock ?? TimeProvider.System;

        if (_options.MaxRetainedTerminalRuns < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "At least one terminal run must be retained.");
        if (_options.DefaultTimeout <= TimeSpan.Zero || _options.MaximumTimeout < _options.DefaultTimeout)
            throw new ArgumentOutOfRangeException(nameof(options), "Workflow run timeout bounds are invalid.");
        if (_options.HeartbeatInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Workflow run heartbeat interval must be positive.");
        PrunePersistedArtifacts();
    }

    /// <summary>
    /// Projects a dispatch's flow to the steps the broker's authorization check reasons about. A
    /// null flow projects to null rather than an empty list, so a caller that cannot see the flow
    /// is treated as unknown and refused rather than as "no steps".
    /// </summary>
    internal static IReadOnlyList<Testing.FlowStep>? DescribeDispatchSteps(Testing.MauiFlow? flow)
        => flow?.Steps;

    internal static bool HasDeviceExtensions(Testing.MauiFlow? flow) =>
        flow is not null &&
        ((flow.ExtensionData is { } extensions &&
          (extensions.ContainsKey("devicePreconditions") || extensions.ContainsKey("deviceSteps"))) ||
         (flow.ExpectedEvidence ?? []).Any(expected =>
             string.Equals(
                 expected?.Kind,
                 Testing.MauiFlowEvidenceKinds.DeviceRecording,
                 StringComparison.Ordinal)));

    internal static string? DescribeDispatchFlowDigest(Testing.MauiFlow? flow) =>
        flow is null ? null : Testing.MauiFlowRunReportSerializer.ComputeFlowDigest(flow);

    /// <summary>
    /// Starts one bounded, mutating replay. Every broker-hosted dispatch surface reaches the device
    /// through this method, so the broker's authorization decision is taken here rather than in the
    /// route that happens to be calling: a new entry point inherits the check instead of having to
    /// remember it.
    /// </summary>
    public WorkflowRunStartResult Start(
        WorkflowRunStartRequest request,
        WorkflowRunTarget target,
        Func<bool> isTargetCurrent,
        WorkflowRunExecutionOptions? executionOptions = null,
        WorkflowRunLeaseHandoff? leaseHandoff = null,
        WorkflowRunDispatchOrigin dispatchOrigin = WorkflowRunDispatchOrigin.TestAgentGrant,
        string? dispatchTicket = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(isTargetCurrent);

        // Authorize against the broker's canonical target rather than the client-supplied ids, and
        // before any validation, lease, token, or journal state exists for this request.
        var decision = _authorizeDispatch(new WorkflowRunDispatch(
            dispatchOrigin,
            target.AgentId,
            target.AgentInstanceId,
            request.AuthorizationId,
            dispatchTicket,
            leaseHandoff,
            DescribeDispatchSteps(request.Flow),
            HasDeviceExtensions(request.Flow),
            DescribeDispatchFlowDigest(request.Flow)));
        if (!decision.Allowed)
        {
            return WorkflowRunStartResult.Rejected(
                403,
                decision.Error ?? "The workflow run dispatch was not authorized.",
                null,
                null);
        }

        var prepared = Prepare(request, target);
        if (!prepared.Ok)
            return WorkflowRunStartResult.Rejected(
                prepared.StatusCode,
                prepared.Error!,
                prepared.ValidationErrors,
                prepared.ValidationWarnings,
                prepared.Admission);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_idempotency.TryGetValue(request.IdempotencyKey!, out var previous))
            {
                if (!CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(previous.RequestDigest),
                        Encoding.UTF8.GetBytes(prepared.RequestDigest!)))
                {
                    return WorkflowRunStartResult.Conflict(
                        "The idempotency key was already used with a different request digest.");
                }

                if (_runs.TryGetValue(previous.RunId, out var existing))
                    return WorkflowRunStartResult.FromExisting(CreateSnapshotLocked(existing), existing.CapabilityToken);

                _idempotency.Remove(request.IdempotencyKey!);
            }

            if (!isTargetCurrent())
            {
                return WorkflowRunStartResult.Conflict(
                    "The requested agent instance is no longer connected. Refresh agent discovery and retry.");
            }

            var targetKey = new WorkflowRunTargetKey(target.AgentId, target.AgentInstanceId);
            if (_activeTargets.TryGetValue(targetKey, out var activeRunId) &&
                _runs.TryGetValue(activeRunId, out var active) &&
                !WorkflowRunStates.IsTerminal(active.State))
            {
                return WorkflowRunStartResult.Conflict(
                    $"Agent instance '{target.AgentId}' already has mutating workflow run '{active.RunId}'.");
            }

            _activeTargets.Remove(targetKey);

            var now = _clock.GetUtcNow();
            var run = new RunRecord(
                CreateOpaqueId("run"),
                CreateCapabilityToken(),
                request.IdempotencyKey!,
                prepared.RequestDigest!,
                prepared.FlowDigest!,
                prepared.Flow!,
                prepared.SafetyRequest!,
                prepared.Admission!,
                target,
                prepared.Timeout,
                isTargetCurrent,
                executionOptions ?? new WorkflowRunExecutionOptions(),
                now);
            AddEventLocked(
                run,
                "dispatch-authorized",
                $"Broker authorized this dispatch as '{decision.AuditReason}'.");
            if (leaseHandoff is not null)
            {
                var transfer = _leases.TransferAndBegin(
                    target.LeaseKey,
                    leaseHandoff.LeaseId,
                    run.LeaseId,
                    run.TransactionId,
                    "workflow-run",
                    run.RunId);
                if (!transfer.Allowed ||
                    !string.Equals(transfer.TransactionId, run.TransactionId, StringComparison.Ordinal))
                {
                    return WorkflowRunStartResult.Conflict(
                        "The Inspector no longer owns the mutation lease required to start this workflow run.");
                }

                run.LeaseClaimed = true;
                run.TransactionBegun = true;
                run.AuthorityEpoch = transfer.AuthorityEpoch;
                AddEventLocked(run, "lease-transferred", "Inspector authority transferred atomically to the workflow run.");
            }
            else if (dispatchOrigin == WorkflowRunDispatchOrigin.TestAgentGrant)
            {
                // The human approved this exact run in the Inspector, and the Inspector holds the
                // app's single-writer lease for as long as it is open. Without this the approval
                // deadlocks behind the window that granted it. Adoption refuses while any
                // transaction is open, so a human mid-mutation keeps control.
                var adopted = _leases.TryAdoptIdleLease(
                    target.LeaseKey,
                    run.LeaseId,
                    run.TransactionId,
                    "workflow-run",
                    run.RunId,
                    WorkflowMutationLeasePolicy.AdoptableApprovalHostKinds);
                if (adopted.Allowed &&
                    adopted.YouHold &&
                    string.Equals(adopted.TransactionId, run.TransactionId, StringComparison.Ordinal))
                {
                    run.LeaseClaimed = true;
                    run.TransactionBegun = true;
                    run.AuthorityEpoch = adopted.AuthorityEpoch;
                    AddEventLocked(
                        run,
                        "lease-adopted",
                        "An idle trusted-host mutation lease was adopted for this human-approved run.");
                }
            }
            AddEventLocked(run, "queued", "Run accepted and queued.");
            AddEventLocked(
                run,
                "admission",
                $"Replay admission accepted with side-effect policy '{prepared.Admission!.SideEffectPolicy}'.");

            _runs.Add(run.RunId, run);
            _idempotency.Add(run.IdempotencyKey, new IdempotencyEntry(run.RequestDigest, run.RunId));
            _activeTargets[targetKey] = run.RunId;
            run.ExecutionTask = Task.Run(() => ExecuteAsync(run));

            return WorkflowRunStartResult.Started(CreateSnapshotLocked(run), run.CapabilityToken);
        }
    }

    public WorkflowRunCapabilitiesResponse GetCapabilities() => new()
    {
        MaxTimeoutMs = (long)_options.MaximumTimeout.TotalMilliseconds,
        MaxSteps = _options.MaximumSteps,
        WorkflowCommandLedger = _controlLedger is not null
    };

    /// <summary>
    /// Validates a prospective run without taking a lease, minting a token, or scheduling replay.
    /// Inspector hosts use this to render the broker's canonical admission decision before a human
    /// explicitly starts the run.
    /// </summary>
    public WorkflowRunPreflightResult Preflight(
        WorkflowRunStartRequest request,
        WorkflowRunTarget target,
        Func<bool> isTargetCurrent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(isTargetCurrent);

        if (!isTargetCurrent())
        {
            return WorkflowRunPreflightResult.Rejected(
                409,
                "The requested agent instance is no longer connected. Refresh agent discovery and retry.");
        }

        var prepared = Prepare(request, target);
        return prepared.Ok
            ? WorkflowRunPreflightResult.Accepted(
                prepared.FlowDigest!,
                (long)prepared.Timeout.TotalMilliseconds,
                prepared.ValidationWarnings,
                prepared.Admission!)
            : WorkflowRunPreflightResult.Rejected(
                prepared.StatusCode,
                prepared.Error!,
                prepared.ValidationErrors,
                prepared.ValidationWarnings,
                prepared.Admission);
    }

    public WorkflowRunAccessResult GetStatus(string runId, string? capabilityToken)
    {
        lock (_gate)
        {
            if (!_runs.TryGetValue(runId, out var run))
                return WorkflowRunAccessResult.NotFound();
            if (!HasCapability(run, capabilityToken))
                return WorkflowRunAccessResult.Unauthorized();
            return WorkflowRunAccessResult.Success(CreateSnapshotLocked(run));
        }
    }

    public WorkflowRunRepairContextResult GetRepairContext(string runId, string? capabilityToken)
    {
        lock (_gate)
        {
            if (!_runs.TryGetValue(runId, out var run))
                return WorkflowRunRepairContextResult.NotFound();
            if (!HasCapability(run, capabilityToken))
                return WorkflowRunRepairContextResult.Unauthorized();
            if (!WorkflowRunStates.IsTerminal(run.State) || run.StructuredReport is null)
            {
                return WorkflowRunRepairContextResult.Unavailable(
                    "The broker-owned run has not produced a terminal structured report.");
            }

            return WorkflowRunRepairContextResult.Success(new WorkflowRunRepairContext
            {
                RunId = run.RunId,
                FlowDigest = run.FlowDigest,
                Target = run.Target.ToSnapshot(),
                Flow = CloneFlow(run.Flow),
                Plan = ClonePlan(run.SafetyRequest.Plan),
                Report = CloneRunReport(run.StructuredReport),
                Admission = CloneReplayEligibility(run.Admission),
            });
        }
    }

    /// <summary>
    /// Returns facts observed by a broker-owned local run for artifact-trust matching. This is
    /// deliberately internal: an imported-artifact capability can request only a derived binding,
    /// never arbitrary local run reports.
    /// </summary>
    public WorkflowRunLocalReproductionResult GetLocalReproductionFacts(string runId)
    {
        lock (_gate)
        {
            if (!_runs.TryGetValue(runId, out var run))
                return WorkflowRunLocalReproductionResult.NotFound();
            if (!WorkflowRunStates.IsTerminal(run.State) || run.StructuredReport is null)
                return WorkflowRunLocalReproductionResult.Unavailable(
                    "The requested local run has not produced a terminal structured report.");

            var report = run.StructuredReport;
            var failure = report.Failure;
            var failedStep = failure?.StepId is { Length: > 0 } stepId
                ? report.Steps.FirstOrDefault(step => string.Equals(step.StepId, stepId, StringComparison.Ordinal))
                : null;
            return WorkflowRunLocalReproductionResult.Success(new Testing.MauiLocalReproductionFacts
            {
                LocalRunId = run.RunId,
                IsNewLocalRun = true,
                StartedAt = run.StartedAt,
                FlowDigest = report.FlowDigest,
                AppBuildFingerprint = report.Target?.AppBuildFingerprint,
                AppSourceFingerprint = report.Target?.AppSourceFingerprint,
                PackageDigest = report.Target?.PackageDigest,
                Platform = report.Target?.Platform,
                DeviceProfile = report.Target?.DeviceProfile,
                Failure = failure is null
                    ? null
                    : new Testing.MauiLocalFailureFacts
                    {
                        Code = failure.Code,
                        Class = failure.Class,
                        StepId = failure.StepId,
                        ExpectedCheckpoint = CloneCheckpoint(failedStep?.ExpectedCheckpoint),
                        ObservedCheckpoint = CloneCheckpoint(failedStep?.ObservedCheckpoint),
                    },
            });
        }
    }

    /// <summary>
    /// Finds broker-owned evidence that the active selector for a source step resolved uniquely in
    /// a prior successful local run. It returns value-free selector/fingerprint facts only.
    /// </summary>
    public WorkflowRunPriorSelectorResolutionResult GetPriorSelectorResolution(
        string? sourceRunId,
        string? sourceStepId)
    {
        if (string.IsNullOrWhiteSpace(sourceRunId) || string.IsNullOrWhiteSpace(sourceStepId))
            return WorkflowRunPriorSelectorResolutionResult.Unavailable(
                "A source run and step are required for prior selector-resolution lookup.");

        lock (_gate)
        {
            if (!_runs.TryGetValue(sourceRunId, out var source) || source.StructuredReport is null)
                return WorkflowRunPriorSelectorResolutionResult.Unavailable(
                    "The source run has no retained structured report.");

            var sourceReport = source.StructuredReport;
            var prior = _runs.Values
                .Where(run =>
                    !string.Equals(run.RunId, sourceRunId, StringComparison.Ordinal) &&
                    WorkflowRunStates.IsTerminal(run.State) &&
                    run.StructuredReport is not null &&
                    string.Equals(run.StructuredReport.FlowDigest, sourceReport.FlowDigest, StringComparison.Ordinal) &&
                    string.Equals(run.StructuredReport.Target?.AppBuildFingerprint, sourceReport.Target?.AppBuildFingerprint, StringComparison.Ordinal) &&
                    string.Equals(run.StructuredReport.Target?.Platform, sourceReport.Target?.Platform, StringComparison.Ordinal) &&
                    string.Equals(run.StructuredReport.Outcome?.Status, Testing.MauiFlowRunOutcomes.Passed, StringComparison.Ordinal))
                .OrderByDescending(run => run.EndedAt ?? run.CreatedAt)
                .Select(run => new
                {
                    Run = run,
                    Step = run.StructuredReport!.Steps.FirstOrDefault(step =>
                        string.Equals(step.StepId, sourceStepId, StringComparison.Ordinal)),
                })
                .FirstOrDefault(item =>
                    item.Step?.TargetResolution?.MatchCount == 1 &&
                    string.Equals(item.Step.TargetResolution.Status, "resolved", StringComparison.Ordinal) &&
                    item.Step.Fingerprint is not null &&
                    item.Step.Selector is { IsEmpty: false });
            if (prior is null)
            {
                return WorkflowRunPriorSelectorResolutionResult.Unavailable(
                    "No prior trusted successful run uniquely resolved this selector step.");
            }

            return WorkflowRunPriorSelectorResolutionResult.Success(
                new Testing.MauiRepairPriorSelectorResolution
                {
                    RunId = prior.Run.RunId,
                    WasUniquelyResolved = true,
                    TrustedRun = true,
                    Trust = "broker-local-run",
                    ActiveSelector = CloneSelector(prior.Step!.Selector!),
                    Fingerprint = CloneFingerprint(prior.Step.Fingerprint!),
                });
        }
    }

    public WorkflowRunCancelResult Cancel(string runId, string? capabilityToken)
    {
        RunRecord? cancellationTarget = null;
        WorkflowRunSnapshot? snapshot;
        var alreadyTerminal = false;

        lock (_gate)
        {
            if (!_runs.TryGetValue(runId, out var run))
                return WorkflowRunCancelResult.NotFound();
            if (!HasCapability(run, capabilityToken))
                return WorkflowRunCancelResult.Unauthorized();

            if (WorkflowRunStates.IsTerminal(run.State))
            {
                alreadyTerminal = true;
                snapshot = CreateSnapshotLocked(run);
            }
            else if (run.State == WorkflowRunState.Queued)
            {
                run.CancellationRequested = true;
                AddEventLocked(run, "cancellation-requested", "Cancellation requested before lease acquisition.");
                CompleteTerminalLocked(
                    run,
                    WorkflowRunState.Cancelled,
                    "Run cancelled before lease acquisition.",
                    compatibilityReport: null,
                    failureClass: Testing.MauiFlowFailureClasses.Cancelled);
                snapshot = CreateSnapshotLocked(run);
            }
            else
            {
                run.CancellationRequested = true;
                AddEventLocked(run, "cancellation-requested", "Cancellation requested; no future flow steps will be started.");
                cancellationTarget = run;
                snapshot = CreateSnapshotLocked(run);
            }
        }

        cancellationTarget?.Cancellation.Cancel();
        return WorkflowRunCancelResult.FromAccepted(snapshot!, alreadyTerminal);
    }

    public async Task<WorkflowRunSnapshot> WaitForTerminalAsync(
        string runId,
        string capabilityToken,
        CancellationToken cancellationToken = default)
    {
        RunRecord run;
        lock (_gate)
        {
            if (!_runs.TryGetValue(runId, out run!))
                throw new WorkflowRunNotFoundException(runId);
            if (!HasCapability(run, capabilityToken))
                throw new WorkflowRunCapabilityException();
        }

        return await run.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Called by the broker when a WebSocket connection is superseded or disappears. A previous
    /// process instance must never continue as the target of a workflow run.
    /// </summary>
    public void MarkAgentInstanceUnavailable(string agentId, string agentInstanceId, string message)
    {
        RunRecord? affected = null;
        lock (_gate)
        {
            var key = new WorkflowRunTargetKey(agentId, agentInstanceId);
            if (!_activeTargets.TryGetValue(key, out var runId) ||
                !_runs.TryGetValue(runId, out var run) ||
                WorkflowRunStates.IsTerminal(run.State))
            {
                return;
            }

            run.LeaseLostRequested = true;
            run.AgentInstanceUnavailable = true;
            run.OverrideMessage = message;
            AddEventLocked(run, "lease-lost", message);
            affected = run;
        }

        affected.Cancellation.Cancel();
    }

    public void Dispose()
    {
        List<RunRecord> active;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            active = _runs.Values.Where(run => !WorkflowRunStates.IsTerminal(run.State)).ToList();
            foreach (var run in active)
            {
                run.LeaseLostRequested = true;
                run.OverrideMessage = "Broker is shutting down.";
                AddEventLocked(run, "lease-lost", run.OverrideMessage);
            }
        }

        foreach (var run in active)
            run.Cancellation.Cancel();
    }

    private async Task ExecuteAsync(RunRecord run)
    {
        Testing.FlowReplayReport? compatibilityReport = null;
        var terminalState = WorkflowRunState.InfrastructureError;
        var message = "Workflow runner stopped unexpectedly.";
        var failureClass = Testing.MauiFlowFailureClasses.Infrastructure;
        IWorkflowRunOracleSession? oracleSession = null;

        try
        {
            lock (_gate)
            {
                if (WorkflowRunStates.IsTerminal(run.State))
                    return;
                run.StartedAt = _clock.GetUtcNow();
                TransitionLocked(run, WorkflowRunState.AcquiringLease, "Acquiring the broker mutation lease.");
            }

            if (!run.IsTargetCurrent())
            {
                terminalState = WorkflowRunState.LeaseLost;
                message = "The requested agent instance is no longer connected.";
                failureClass = Testing.MauiFlowFailureClasses.LeaseLost;
                return;
            }

            if (run.Cancellation.IsCancellationRequested)
            {
                terminalState = WorkflowRunState.Cancelled;
                message = "Run cancelled before lease acquisition.";
                failureClass = Testing.MauiFlowFailureClasses.Cancelled;
                return;
            }

            if (!run.LeaseClaimed)
            {
                var claim = await AcquireLeaseAsync(run).ConfigureAwait(false);
                if (!claim.Allowed)
                {
                    terminalState = WorkflowRunState.Failed;
                    message = DescribeLeaseConflict(claim);
                    failureClass = Testing.MauiFlowFailureClasses.LeaseConflict;
                    return;
                }
                run.LeaseClaimed = true;
            }

            if (run.Cancellation.IsCancellationRequested)
            {
                terminalState = WorkflowRunState.Cancelled;
                message = "Run cancelled while acquiring the mutation lease.";
                failureClass = Testing.MauiFlowFailureClasses.Cancelled;
                return;
            }

            if (!run.TransactionBegun)
            {
                var transaction = _leases.Control(
                    run.Target.LeaseKey,
                    "begin",
                    run.LeaseId,
                    "workflow-run",
                    run.RunId,
                    force: false,
                    transactionId: run.TransactionId);
                if (!transaction.Allowed)
                {
                    terminalState = WorkflowRunState.Failed;
                    message = "The broker could not begin the mutation-lease transaction.";
                    failureClass = Testing.MauiFlowFailureClasses.LeaseConflict;
                    return;
                }
                run.TransactionBegun = true;
                run.AuthorityEpoch = transaction.AuthorityEpoch;
            }

            if (_controlLedger is not null)
            {
                if (run.AuthorityEpoch <= 0)
                {
                    terminalState = WorkflowRunState.InfrastructureError;
                    message = "The broker did not provide a workflow authority epoch.";
                    failureClass = Testing.MauiFlowFailureClasses.Infrastructure;
                    return;
                }

                run.LedgerBeginAttempted = true;
                var began = await ControlLedgerAsync(run, "begin", reason: null).ConfigureAwait(false);
                if (!began.Ok)
                {
                    (terminalState, message, failureClass) = MapLedgerControlFailure(
                        began,
                        "The agent rejected the workflow ledger begin request.");
                    return;
                }

                run.LedgerBegun = true;
            }

            lock (_gate)
            {
                if (WorkflowRunStates.IsTerminal(run.State))
                    return;
                TransitionLocked(run, WorkflowRunState.Preparing, "Preparing canonical flow replay.");
            }

            run.HeartbeatTask = HeartbeatAsync(run);

            if (!run.IsTargetCurrent())
            {
                terminalState = WorkflowRunState.LeaseLost;
                message = "The requested agent instance reconnected before replay began.";
                failureClass = Testing.MauiFlowFailureClasses.LeaseLost;
                return;
            }

            if (run.Cancellation.IsCancellationRequested)
            {
                terminalState = WorkflowRunState.Cancelled;
                message = "Run cancelled before replay began.";
                failureClass = Testing.MauiFlowFailureClasses.Cancelled;
                return;
            }

            lock (_gate)
            {
                if (WorkflowRunStates.IsTerminal(run.State))
                    return;
                TransitionLocked(run, WorkflowRunState.Running, "Running canonical flow replay.");
            }

            run.TimeoutCancellation.CancelAfter(run.Timeout);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                run.Cancellation.Token,
                run.TimeoutCancellation.Token);
            oracleSession = await BeginOracleSessionAsync(run, linkedCancellation.Token).ConfigureAwait(false);
            var recordingCaptured = run.ExecutionOptions.DeviceRecordingCaptured;
            if (!string.IsNullOrWhiteSpace(_options.ArtifactRoot))
            {
                recordingCaptured = (runId, capture) =>
                {
                    // Retention re-homes the file into a root this coordinator owns, so the capture
                    // handed onwards carries that root's authority. Forwarding the surface's
                    // original capture after copying would point the next hop at a path that only
                    // the device surface's root would ever vouch for; forwarding a bare retained
                    // path would leave it with nothing to validate against at all.
                    var retained = RetainDeviceRecording(runId, capture);
                    run.ExecutionOptions.DeviceRecordingCaptured?.Invoke(
                        runId,
                        retained ?? capture);
                };
            }
            var executionOptions = new WorkflowRunExecutionOptions
            {
                EvidenceCaptureFactory = run.ExecutionOptions.EvidenceCaptureFactory,
                ReproductionExpectation = run.ExecutionOptions.ReproductionExpectation,
                RecordDeviceRun = run.ExecutionOptions.RecordDeviceRun,
                DeviceRecordingTimeoutSeconds = run.ExecutionOptions.DeviceRecordingTimeoutSeconds,
                DeviceRecordingCaptured = recordingCaptured,
                Progress = progress => ReportProgress(run, progress),
            };
            compatibilityReport = await _execute(
                new WorkflowRunExecution(
                    run.RunId,
                    run.Flow,
                    run.SafetyRequest,
                    run.Admission,
                    run.Target,
                    run.LeaseId,
                    run.TransactionId,
                    run.AuthorityEpoch,
                    executionOptions),
                linkedCancellation.Token).ConfigureAwait(false);

            terminalState = compatibilityReport.Ok ? WorkflowRunState.Passed : WorkflowRunState.Failed;
            message = compatibilityReport.Ok ? "Flow replay passed." : "Flow replay reported a divergence.";
            failureClass = compatibilityReport.Ok
                ? string.Empty
                : ClassifyFailure(compatibilityReport);
            if (string.Equals(failureClass, Testing.MauiFlowFailureClasses.UnknownCompletion, StringComparison.Ordinal))
            {
                terminalState = WorkflowRunState.UnknownCompletion;
                message = "Flow replay has a command with unknown completion.";
            }
        }
        catch (OperationCanceledException)
        {
            terminalState = WorkflowRunState.Cancelled;
            message = "Workflow run cancelled.";
            failureClass = Testing.MauiFlowFailureClasses.Cancelled;
        }
        catch (Exception ex)
        {
            terminalState = WorkflowRunState.InfrastructureError;
            message = $"Workflow runner infrastructure error: {ex.Message}";
            failureClass = Testing.MauiFlowFailureClasses.Infrastructure;
        }
        finally
        {
            if (run.HeartbeatTask is not null)
            {
                run.HeartbeatCancellation.Cancel();
                try
                {
                    await run.HeartbeatTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            var shouldComplete = false;
            lock (_gate)
            {
                if (WorkflowRunStates.IsTerminal(run.State))
                {
                    // A queued cancellation completed before this task reached lease acquisition.
                }
                else
                {
                    if (run.AgentInstanceUnavailable)
                    {
                        terminalState = WorkflowRunState.Orphaned;
                        message = run.OverrideMessage ?? "The target agent instance is no longer available.";
                        failureClass = Testing.MauiFlowFailureClasses.AgentDisconnected;
                    }
                    else if (run.LeaseLostRequested)
                    {
                        terminalState = WorkflowRunState.LeaseLost;
                        message = run.OverrideMessage ?? "The workflow mutation lease was lost.";
                        failureClass = Testing.MauiFlowFailureClasses.LeaseLost;
                    }
                    else if (run.TimeoutCancellation.IsCancellationRequested)
                    {
                        terminalState = WorkflowRunState.TimedOut;
                        message = "Workflow run timed out.";
                        failureClass = Testing.MauiFlowFailureClasses.Timeout;
                    }
                    else if (run.CancellationRequested || run.Cancellation.IsCancellationRequested)
                    {
                        terminalState = WorkflowRunState.Cancelled;
                        message = "Workflow run cancelled.";
                        failureClass = Testing.MauiFlowFailureClasses.Cancelled;
                    }
                    shouldComplete = true;
                }
            }

            if (shouldComplete && run.LedgerBeginAttempted)
            {
                var action = run.LedgerBegun &&
                    (terminalState is WorkflowRunState.Passed or WorkflowRunState.Failed)
                    ? "end"
                    : "abandon";
                var ledgerResult = await ControlLedgerAsync(
                    run,
                    action,
                    action == "abandon" ? WorkflowRunStates.ToWireValue(terminalState) : null)
                    .ConfigureAwait(false);
                if (!ledgerResult.Ok)
                {
                    (terminalState, message, failureClass) = MapLedgerControlFailure(
                        ledgerResult,
                        "The agent did not confirm workflow ledger cleanup.");
                }
            }

            EndLease(run);

            if (shouldComplete)
            {
                await AdoptPostRunOracleEvidenceAsync(run, oracleSession, terminalState).ConfigureAwait(false);
                lock (_gate)
                {
                    if (!WorkflowRunStates.IsTerminal(run.State))
                        CompleteTerminalLocked(run, terminalState, message, compatibilityReport, failureClass);
                }
            }
        }
    }

    /// <summary>
    /// Opens an independent business-oracle evaluation for this run, or returns null when nothing
    /// can evaluate the plan's declared oracles against this target.
    /// </summary>
    /// <remarks>
    /// A run that cannot be oracle-verified is not a failure: it simply stays unverified and
    /// therefore ineligible for repair, exactly as before. Only a session that opened successfully
    /// can later certify anything.
    /// </remarks>
    private async Task<IWorkflowRunOracleSession?> BeginOracleSessionAsync(
        RunRecord run,
        CancellationToken cancellationToken)
    {
        if (_beginOracleSession is null || run.SafetyRequest.Plan is not { } plan)
            return null;

        try
        {
            var session = await _beginOracleSession(
                new RunOracleSessionRequest(run.RunId, plan, run.Target),
                cancellationToken).ConfigureAwait(false);
            if (session is not null)
                AddEvent(run, "oracle-baseline-observed", "Recorded the declared business evidence before the run.");
            return session;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Evidence that cannot be observed must not stop a run the human already approved. The
            // run simply stays unverified, which is the outcome an absent baseline already implies.
            AddEvent(run, "oracle-baseline-unavailable", "The declared business evidence could not be read before the run.");
            return null;
        }
    }

    /// <summary>
    /// Attaches this run's independent business-oracle results and re-decides admission from them.
    /// </summary>
    /// <remarks>
    /// Admission is first decided before the run, when no oracle has produced anything, so a plan
    /// that requires one is necessarily unverified and repair-ineligible at that moment. That
    /// provisional decision was previously the only one ever recorded, which is why a broker-owned
    /// run could never become repair-eligible however well it went. The decision made here is the
    /// first one that can account for what the run actually established.
    /// </remarks>
    private async Task AdoptPostRunOracleEvidenceAsync(
        RunRecord run,
        IWorkflowRunOracleSession? session,
        WorkflowRunState terminalState)
    {
        if (session is null || run.SafetyRequest.Context is not { } context)
            return;

        // A run that never reached its steps, or whose outcome is unknown, cannot support a claim
        // about what the app committed, so its evidence is not collected at all.
        if (terminalState is not (WorkflowRunState.Passed or WorkflowRunState.Failed))
            return;

        IReadOnlyList<Testing.MauiIndependentBusinessOracleResult> results;
        try
        {
            using var timeout = new CancellationTokenSource(_options.OracleEvaluationTimeout);
            results = await session.EvaluateAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            AddEvent(run, "oracle-evaluation-failed", "The declared business evidence could not be read after the run.");
            return;
        }

        if (results.Count == 0)
            return;

        context.BusinessOracles = [.. results];
        var decision = Testing.MauiFlowReplaySafetyEvaluator.EvaluateWithFlow(run.SafetyRequest, run.Flow);
        run.AdoptPostRunAdmission(decision);
        AddEvent(
            run,
            "oracle-evidence-recorded",
            results.All(static result => result.Succeeded == true)
                ? "Independent business-oracle evidence verified this run."
                : "Independent business-oracle evidence did not verify this run.");
    }

    private void AddEvent(RunRecord run, string kind, string message)
    {
        lock (_gate)
        {
            if (!WorkflowRunStates.IsTerminal(run.State))
                AddEventLocked(run, kind, message);
        }
    }

    private async Task HeartbeatAsync(RunRecord run)
    {
        try
        {
            using var timer = new PeriodicTimer(_options.HeartbeatInterval);
            while (await timer.WaitForNextTickAsync(run.HeartbeatCancellation.Token).ConfigureAwait(false))
            {
                var heartbeat = _leases.Control(
                    run.Target.LeaseKey,
                    "heartbeat",
                    run.LeaseId,
                    "workflow-run",
                    run.RunId,
                    force: false,
                    transactionId: run.TransactionId);
                if (heartbeat.Allowed)
                    continue;

                lock (_gate)
                {
                    if (!WorkflowRunStates.IsTerminal(run.State))
                    {
                        run.LeaseLostRequested = true;
                        run.OverrideMessage = "The broker mutation lease could not be heartbeated.";
                        AddEventLocked(run, "lease-lost", run.OverrideMessage);
                    }
                }
                run.Cancellation.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) when (run.HeartbeatCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (!WorkflowRunStates.IsTerminal(run.State))
                {
                    run.LeaseLostRequested = true;
                    run.OverrideMessage = $"The broker mutation-lease heartbeat failed: {ex.Message}";
                    AddEventLocked(run, "lease-lost", run.OverrideMessage);
                }
            }
            run.Cancellation.Cancel();
        }
    }

    private async Task<WorkflowRunLedgerControlResult> ControlLedgerAsync(
        RunRecord run,
        string action,
        string? reason)
    {
        if (_controlLedger is null)
            return WorkflowRunLedgerControlResult.Success();

        try
        {
            return await _controlLedger(
                new WorkflowRunLedgerControl(
                    action,
                    run.RunId,
                    run.Target,
                    run.LeaseId,
                    run.AuthorityEpoch,
                    ApprovalDigest: null,
                    Reason: reason),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return WorkflowRunLedgerControlResult.Failure(
                "workflow-transport",
                $"The agent workflow ledger control failed: {ex.Message}");
        }
    }

    private static (WorkflowRunState State, string Message, string FailureClass) MapLedgerControlFailure(
        WorkflowRunLedgerControlResult result,
        string fallbackMessage)
    {
        var message = string.IsNullOrWhiteSpace(result.Error)
            ? fallbackMessage
            : $"{fallbackMessage} {result.Error}";
        return result.Reason switch
        {
            "workflow-unknown-completion" => (
                WorkflowRunState.UnknownCompletion,
                message,
                Testing.MauiFlowFailureClasses.UnknownCompletion),
            "workflow-agent-instance" or "workflow-broker-unavailable" or "workflow-transport" => (
                WorkflowRunState.Orphaned,
                message,
                Testing.MauiFlowFailureClasses.AgentDisconnected),
            _ => (
                WorkflowRunState.Failed,
                message,
                Testing.MauiFlowFailureClasses.WorkflowCommandConflict)
        };
    }

    private void EndLease(RunRecord run)
    {
        if (Interlocked.Exchange(ref run.LeaseEnded, 1) != 0)
            return;

        if (run.TransactionBegun)
        {
            _leases.Control(
                run.Target.LeaseKey,
                "end",
                run.LeaseId,
                "workflow-run",
                run.RunId,
                force: false,
                transactionId: run.TransactionId);
        }

        if (run.LeaseClaimed)
        {
            _leases.Control(
                run.Target.LeaseKey,
                "release",
                run.LeaseId,
                "workflow-run",
                run.RunId,
                force: false,
                transactionId: null);
        }
    }

    private void CompleteTerminalLocked(
        RunRecord run,
        WorkflowRunState terminalState,
        string message,
        Testing.FlowReplayReport? compatibilityReport,
        string failureClass)
    {
        if (!WorkflowRunStates.IsTerminal(terminalState))
            throw new ArgumentOutOfRangeException(nameof(terminalState), "A terminal state is required.");
        if (WorkflowRunStates.IsTerminal(run.State))
            return;

        run.CompatibilityReport = compatibilityReport;
        run.FirstDivergence = compatibilityReport?.DivergencePoint;
        run.Message = message;
        run.EndedAt = _clock.GetUtcNow();
        TransitionLocked(run, terminalState, message);
        run.StructuredReport = BuildStructuredReport(run, terminalState, message, compatibilityReport, failureClass);
        run.CompletedSteps = Math.Max(run.CompletedSteps, run.StructuredReport.Steps.Count);
        run.CurrentStepId ??= run.StructuredReport.Steps.LastOrDefault()?.StepId;
        PersistStructuredReportLocked(run);
        if (run.CompatibilityReport is not null)
        {
            run.CompatibilityReport.StructuredReport = run.StructuredReport;
            run.CompatibilityReport.ReportPath = run.StructuredReport.ReportPath;
            run.CompatibilityReport.ReportDigest = run.StructuredReport.ReportDigest;
        }

        _activeTargets.Remove(new WorkflowRunTargetKey(run.Target.AgentId, run.Target.AgentInstanceId));
        run.TerminalOrder = ++_terminalOrder;
        run.Completion.TrySetResult(CreateSnapshotLocked(run));
        EvictTerminalRunsLocked();
    }

    private Testing.MauiFlowRunReport BuildStructuredReport(
        RunRecord run,
        WorkflowRunState state,
        string message,
        Testing.FlowReplayReport? replay,
        string failureClass)
    {
        if (replay?.StructuredReport is { } canonical)
        {
            canonical.RunId = run.RunId;
            canonical.FlowId ??= $"sha256:{run.FlowDigest}";
            canonical.FlowDigest = run.FlowDigest;
            canonical.LegacyFlowIdentity ??= run.Flow.Name;
            canonical.Target = MergeTarget(canonical.Target, run.Target);
            canonical.StartedAt ??= run.StartedAt ?? run.CreatedAt;
            canonical.EndedAt = run.EndedAt;
            canonical.Outcome = new Testing.MauiFlowRunOutcome
            {
                Status = WorkflowRunStates.ToWireValue(state),
                Summary = message,
                Terminal = true,
            };
            canonical.DivergenceStepId ??= run.FirstDivergence?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            canonical.Events = MergeEvents(canonical.Events, run.Events);
            if (run.LifecycleEventsTruncated)
            {
                canonical.Truncated = true;
                canonical.TruncationReason ??= "The broker lifecycle-event limit was reached.";
                canonical.Omissions.Add(new Testing.MauiFlowReportOmission
                {
                    Kind = "broker-events",
                    Reason = "The broker lifecycle-event limit was reached.",
                });
            }
            if (state == WorkflowRunState.Passed)
            {
                canonical.Failure = null;
            }
            else if (state == WorkflowRunState.Failed && canonical.Failure is not null)
            {
                // The canonical runner already recorded the precise step failure. Keep its
                // redacted message and detailed classifier instead of replacing it with a
                // broker-level "divergence" summary.
            }
            else
            {
                var classification = Testing.MauiFlowFailureClassifier.Classify(new Testing.MauiFlowFailureFacts
                {
                    TerminalOutcome = WorkflowRunStates.ToWireValue(state),
                    FailureClass = failureClass,
                    LegacyFailureKind = replay.Results.FirstOrDefault(result => !result.Ok)?.FailureKind,
                });
                canonical.Failure = Testing.MauiFlowFailureClassifier.ToFailure(
                    classification,
                    canonical.Failure?.FailureId ?? $"failure-{run.RunId}",
                    canonical.Failure?.LegacyKind ?? replay.Results.FirstOrDefault(result => !result.Ok)?.FailureKind,
                    canonical.DivergenceStepId,
                    run.EndedAt ?? _clock.GetUtcNow(),
                    message);
            }
            ApplyAdmissionFacts(run, canonical, state);
            Testing.MauiFlowRunReportSerializer.ApplyLimits(canonical, _options.ReportLimits);
            return canonical;
        }

        var steps = replay?.Results.Select(result => new Testing.MauiFlowStepAttempt
        {
            StepId = result.Seq.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Sequence = result.Seq,
            Action = result.Action,
            Intent = result.Label,
            FailureClass = result.FailureKind is null ? null : MapFailureKind(result.FailureKind),
            CommandId = result.CommandId,
            CommandSequence = null,
            ActionDigest = result.ActionDigest,
            AuthorityEpoch = result.AuthorityEpoch ?? (run.AuthorityEpoch > 0 ? run.AuthorityEpoch : null),
            AcknowledgementState = result.AcknowledgementState,
            Dispatch = result.CommandId is null ? null : new Testing.MauiFlowDispatchReceipt
            {
                CommandId = result.CommandId,
                ActionDigest = result.ActionDigest,
                AuthorityEpoch = result.AuthorityEpoch,
                AcknowledgementState = result.AcknowledgementState,
                CompletionCertainty = result.AcknowledgementState == "unknown-completion"
                    ? "unknown"
                    : "completed"
            },
            TargetResolution = result.MatchCount is null ? null : new Testing.MauiFlowTargetResolution
            {
                MatchCount = result.MatchCount,
                Status = result.Ok ? "resolved" : "failed",
                Message = result.Error
            },
            Assertions = result.Asserts.Select(assertion => new Testing.MauiFlowAssertionResult
            {
                Kind = assertion.Kind,
                Passed = assertion.Ok,
                Skipped = assertion.Skipped,
                Expected = Testing.MauiFlowReportRedactor.DescribeValue(assertion.Expected).Value,
                Actual = Testing.MauiFlowReportRedactor.DescribeValue(assertion.Actual).Value,
                ExpectedDisclosure = Testing.MauiFlowReportRedactor.DescribeValue(assertion.Expected),
                ActualDisclosure = Testing.MauiFlowReportRedactor.DescribeValue(assertion.Actual)
            }).ToList()
        }).ToList() ?? [];

        var events = run.Events.Select((item, index) => new Testing.MauiFlowRunEvent
        {
            Sequence = index + 1,
            At = item.At,
            Kind = item.Kind,
            Message = item.Message
        }).ToList();

        var succeeded = state == WorkflowRunState.Passed;
        var fallback = new Testing.MauiFlowRunReport
        {
            RunId = run.RunId,
            FlowId = $"sha256:{run.FlowDigest}",
            FlowDigest = run.FlowDigest,
            LegacyFlowIdentity = run.Flow.Name,
            Target = new Testing.MauiFlowRunTarget
            {
                TargetId = run.Target.AgentId,
                AgentId = run.Target.AgentId,
                AgentInstanceId = run.Target.AgentInstanceId,
                Platform = run.Target.Platform,
                AppId = run.Target.AppName
            },
            StartedAt = run.StartedAt ?? run.CreatedAt,
            EndedAt = run.EndedAt,
            Outcome = new Testing.MauiFlowRunOutcome
            {
                Status = WorkflowRunStates.ToWireValue(state),
                Summary = message,
                Terminal = true
            },
            DivergenceStepId = run.FirstDivergence?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Events = events,
            Steps = steps,
            Failure = succeeded ? null : new Testing.MauiFlowFailure
            {
                FailureId = $"failure-{run.RunId}",
                Class = failureClass,
                Code = failureClass,
                Category = Testing.MauiFlowFailureClassifier.Classify(new Testing.MauiFlowFailureFacts
                {
                    FailureClass = failureClass
                }).Category,
                Phase = Testing.MauiFlowFailureClassifier.Classify(new Testing.MauiFlowFailureFacts
                {
                    FailureClass = failureClass
                }).Phase,
                Retryable = Testing.MauiFlowFailureClassifier.Classify(new Testing.MauiFlowFailureFacts
                {
                    FailureClass = failureClass
                }).Retryable,
                RepairEligible = false,
                LegacyKind = replay?.Results.FirstOrDefault(result => !result.Ok)?.FailureKind,
                Message = message,
                StepId = run.FirstDivergence?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                At = run.EndedAt
            }
        };
        if (run.LifecycleEventsTruncated)
        {
            fallback.Truncated = true;
            fallback.TruncationReason = "The broker lifecycle-event limit was reached.";
            fallback.Omissions.Add(new Testing.MauiFlowReportOmission
            {
                Kind = "broker-events",
                Reason = "The broker lifecycle-event limit was reached.",
            });
        }
        ApplyAdmissionFacts(run, fallback, state);
        Testing.MauiFlowRunReportSerializer.ApplyLimits(fallback, _options.ReportLimits);
        return fallback;
    }

    private static void ApplyAdmissionFacts(
        RunRecord run,
        Testing.MauiFlowRunReport report,
        WorkflowRunState state)
    {
        var context = run.SafetyRequest.Context;
        report.SideEffectPolicy = run.Admission.SideEffectPolicy;
        report.ReplayEligibility = run.Admission;
        report.Preconditions ??= context?.Preconditions;
        report.Reset ??= context?.Reset;
        report.Compensator ??= context?.Compensator;
        if (report.BusinessOracles.Count == 0 && context is not null)
            report.BusinessOracles = context.BusinessOracles.ToList();

        report.Outcome ??= new Testing.MauiFlowRunOutcome();
        var verified = state == WorkflowRunState.Passed && run.Admission.RunVerificationAllowed;
        report.Outcome.Verified = verified;
        report.Outcome.VerificationReason = verified
            ? "Required independent business oracles verified the run."
            : "The run is not verified because required independent business-oracle evidence is absent, failed, or the run did not pass.";
        report.Verification = new Testing.MauiFlowRunVerification
        {
            Verified = verified,
            Reason = report.Outcome.VerificationReason,
            CheckedAt = report.EndedAt,
        };
        // run.Admission is this report's replay eligibility, assigned above, so the shared gate is
        // the single rule deciding whether the classifier's verdict survives.
        Testing.MauiFlowFailureClassifier.ApplyRepairEligibilityGate(report);
    }

    private void PersistStructuredReportLocked(RunRecord run)
    {
        if (run.StructuredReport is null || string.IsNullOrWhiteSpace(_options.ArtifactRoot))
            return;

        var write = Testing.MauiFlowRunReportSerializer.WriteAtomic(
            run.StructuredReport,
            _options.ArtifactRoot!,
            _options.ReportLimits);
        if (write.Ok)
        {
            run.ReportPath = write.Path;
            run.ReportDigest = write.Digest;
            run.StructuredReport.ReportPath = write.Path;
            run.StructuredReport.ReportDigest = write.Digest;
            PrunePersistedArtifacts(run.RunId);
            return;
        }

        run.StructuredReport.Truncated = true;
        run.StructuredReport.Omissions.Add(new Testing.MauiFlowReportOmission
        {
            Kind = "report-artifact",
            Reason = write.Error ?? "The report artifact could not be written.",
        });
    }

    private void PrunePersistedArtifacts(string? retainedRunId = null)
    {
        if (string.IsNullOrWhiteSpace(_options.ArtifactRoot))
            return;

        try
        {
            var root = Path.GetFullPath(_options.ArtifactRoot);
            if (!Directory.Exists(root))
                return;

            var retainedSegment = Testing.MauiFlowReportRedactor.SafeFileSegment(retainedRunId);
            var expired = Directory.EnumerateDirectories(root)
                .Where(IsBrokerOwnedRunDirectory)
                .OrderByDescending(path => string.Equals(
                    Path.GetFileName(path),
                    retainedSegment,
                    StringComparison.Ordinal))
                .ThenByDescending(Directory.GetLastWriteTimeUtc)
                .ThenBy(static path => path, StringComparer.Ordinal)
                .Skip(_options.MaxRetainedTerminalRuns)
                .ToArray();
            foreach (var directory in expired)
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Persisted evidence is additive; inability to prune it must not change a run outcome.
        }
    }

    private static bool IsBrokerOwnedRunDirectory(string path)
    {
        var name = Path.GetFileName(path);
        return name.Length == 36 &&
            name.StartsWith("run_", StringComparison.Ordinal) &&
            name.Skip(4).All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f') &&
            File.Exists(Path.Combine(path, Testing.MauiFlowRunReportSerializer.FileName));
    }

    private static Testing.MauiFlowRunTarget MergeTarget(
        Testing.MauiFlowRunTarget? existing,
        WorkflowRunTarget target)
    {
        existing ??= new Testing.MauiFlowRunTarget();
        existing.TargetId ??= target.AgentId;
        existing.AgentId ??= target.AgentId;
        existing.AgentInstanceId ??= target.AgentInstanceId;
        existing.Platform ??= target.Platform;
        existing.AppId ??= target.AppName;
        return existing;
    }

    private static Testing.MauiFlowCheckpoint? CloneCheckpoint(Testing.MauiFlowCheckpoint? checkpoint)
        => checkpoint is null
            ? null
            : new Testing.MauiFlowCheckpoint
            {
                AppBuildFingerprint = checkpoint.AppBuildFingerprint,
                AgentInstanceId = checkpoint.AgentInstanceId,
                SeedFingerprint = checkpoint.SeedFingerprint,
                BackendStateFingerprint = checkpoint.BackendStateFingerprint,
                Route = checkpoint.Route,
                Window = checkpoint.Window,
                Modal = checkpoint.Modal,
                Locale = checkpoint.Locale,
                Theme = checkpoint.Theme,
                Orientation = checkpoint.Orientation,
                DisplayProfile = checkpoint.DisplayProfile,
                CollectionItemKey = checkpoint.CollectionItemKey,
            };

    private static Testing.FlowSelector CloneSelector(Testing.FlowSelector selector)
        => JsonSerializer.Deserialize(
            JsonSerializer.Serialize(selector, Testing.MauiFlowJsonContext.Default.FlowSelector),
            Testing.MauiFlowJsonContext.Default.FlowSelector)
            ?? throw new InvalidOperationException("Selector clone failed.");

    private static Testing.MauiElementFingerprint CloneFingerprint(Testing.MauiElementFingerprint fingerprint)
        => JsonSerializer.Deserialize(
            JsonSerializer.Serialize(fingerprint, Testing.MauiTestingJsonContext.Default.MauiElementFingerprint),
            Testing.MauiTestingJsonContext.Default.MauiElementFingerprint)
            ?? throw new InvalidOperationException("Fingerprint clone failed.");

    private static Testing.MauiFlow CloneFlow(Testing.MauiFlow flow)
        => JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(flow, Testing.MauiFlowJsonContext.Default.MauiFlow),
            Testing.MauiFlowJsonContext.Default.MauiFlow)
            ?? throw new InvalidOperationException("Flow clone failed.");

    private static Testing.MauiTestPlan? ClonePlan(Testing.MauiTestPlan? plan)
        => plan is null
            ? null
            : JsonSerializer.Deserialize(
                JsonSerializer.SerializeToUtf8Bytes(plan, Testing.MauiTestingJsonContext.Default.MauiTestPlan),
                Testing.MauiTestingJsonContext.Default.MauiTestPlan);

    private static Testing.MauiFlowRunReport CloneRunReport(Testing.MauiFlowRunReport report)
        => JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(report, Testing.MauiTestingJsonContext.Default.MauiFlowRunReport),
            Testing.MauiTestingJsonContext.Default.MauiFlowRunReport)
            ?? throw new InvalidOperationException("Run report clone failed.");

    private static Testing.MauiFlowReplayEligibilityDecision CloneReplayEligibility(
        Testing.MauiFlowReplayEligibilityDecision decision)
        => JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(
                decision,
                Testing.MauiTestingJsonContext.Default.MauiFlowReplayEligibilityDecision),
            Testing.MauiTestingJsonContext.Default.MauiFlowReplayEligibilityDecision)
            ?? throw new InvalidOperationException("Replay eligibility clone failed.");

    private static List<Testing.MauiFlowRunEvent> MergeEvents(
        IReadOnlyList<Testing.MauiFlowRunEvent> runnerEvents,
        IReadOnlyList<WorkflowRunLifecycleEvent> brokerEvents)
    {
        var merged = runnerEvents
            .Concat(brokerEvents.Select(item => new Testing.MauiFlowRunEvent
            {
                At = item.At,
                Kind = item.Kind,
                Message = item.Message,
            }))
            .OrderBy(item => item.At ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Sequence ?? int.MaxValue)
            .Take(128)
            .Select((item, index) => new Testing.MauiFlowRunEvent
            {
                Sequence = index + 1,
                At = item.At,
                Kind = item.Kind,
                Message = item.Message,
                StepId = item.StepId,
                Data = item.Data,
            })
            .ToList();
        return merged;
    }

    private void EvictTerminalRunsLocked()
    {
        while (_runs.Values.Count(run => WorkflowRunStates.IsTerminal(run.State)) >
            _options.MaxRetainedTerminalRuns)
        {
            var candidate = _runs.Values
                .Where(run => WorkflowRunStates.IsTerminal(run.State))
                .OrderBy(run => run.TerminalOrder)
                .ThenBy(run => run.RunId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (candidate is null)
                return;

            _runs.Remove(candidate.RunId);
            if (_idempotency.TryGetValue(candidate.IdempotencyKey, out var entry) &&
                string.Equals(entry.RunId, candidate.RunId, StringComparison.Ordinal))
            {
                _idempotency.Remove(candidate.IdempotencyKey);
            }

            // A queued cancellation can complete before its scheduled execution task observes the
            // terminal state. Let its cancellation sources be collected with the record instead of
            // disposing a token that that task may still read.
        }
    }

    /// <summary>
    /// Copies a vouched recording into this coordinator's own artifact root and returns a capture
    /// for the copy, so the next hop re-validates against the directory the bytes now live in.
    /// Returns <c>null</c> when nothing was retained, which leaves the caller forwarding the
    /// original capture rather than a path nobody can vouch for.
    /// </summary>
    internal DeviceLayer.DeviceRecordingCapture? RetainDeviceRecording(
        string runId,
        DeviceLayer.DeviceRecordingCapture capture)
    {
        var safeRunId = Testing.MauiFlowReportRedactor.SafeFileSegment(runId);
        if (string.IsNullOrWhiteSpace(_options.ArtifactRoot) || safeRunId is null)
            return null;

        // Re-prove containment through the capture's own authority at the moment the bytes are
        // opened: this is a read of a file an untrusted host named, and the check that happened one
        // hop ago says nothing about what the path points at now.
        var source = capture.ResolveForRead();
        if (source is null)
            return null;

        string? temporary = null;
        try
        {
            var info = new FileInfo(source);
            if (!info.Exists || info.Length <= 0 || info.Length > 2L * 1024 * 1024 * 1024)
                return null;

            var artifactRoot = Path.GetFullPath(_options.ArtifactRoot);
            var authority = new DeviceLayer.TrustedRootRecordingPathAuthority(artifactRoot);

            // The destination is vouched for *before* anything is created. The check that used to
            // happen after the copy could only ever report a mistake that had already been made: a
            // mis-derived artifact root — an empty configured directory collapsing to a drive
            // letter is the usual way — or a link planted at the run's own directory would have the
            // recording written outside the root first and refused second, leaving a copy of the
            // bytes somewhere nothing owns and nothing will ever sweep. Refusal here touches the
            // filesystem not at all: no directory, no temporary file, no orphan.
            var vouched = authority.ResolveContainedRecordingPath(Path.Combine(
                artifactRoot,
                safeRunId,
                "artifacts",
                "device-recording.mp4"));
            if (vouched is null)
                return null;

            Directory.CreateDirectory(Path.GetDirectoryName(vouched)!);
            temporary = vouched + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.Copy(source, temporary, overwrite: false);
            File.Move(temporary, vouched, overwrite: true);
            temporary = null;

            // Asked again on the file that now exists: between the check above and the move, the
            // path could have become a link out of the root. A copy that no longer sits where it
            // was vouched for is deleted rather than published, because leaving it is the same
            // orphan by a slower route.
            var retained = DeviceLayer.DeviceRecordingCapture.TryCreate(vouched, authority);
            if (retained is null)
            {
                try { File.Delete(vouched); } catch { }
                return null;
            }

            return retained;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); } catch { }
            }
            return null;
        }
    }

    private WorkflowRunSnapshot CreateSnapshotLocked(RunRecord run) => new()
    {
        Schema = 1,
        RunId = run.RunId,
        State = WorkflowRunStates.ToWireValue(run.State),
        Terminal = WorkflowRunStates.IsTerminal(run.State),
        FlowDigest = run.FlowDigest,
        AuthorityEpoch = run.AuthorityEpoch > 0 ? run.AuthorityEpoch : null,
        Target = run.Target.ToSnapshot(),
        CreatedAt = run.CreatedAt,
        StartedAt = run.StartedAt,
        EndedAt = run.EndedAt,
        TotalSteps = run.TotalSteps,
        CompletedSteps = run.CompletedSteps,
        CurrentStepId = run.CurrentStepId,
        FirstDivergence = run.FirstDivergence,
        CancellationRequested = run.CancellationRequested,
        Message = run.Message,
        Events = run.Events.ToList(),
        Report = run.StructuredReport,
        ReportPath = run.ReportPath ?? run.StructuredReport?.ReportPath,
        ReportDigest = run.ReportDigest ?? run.StructuredReport?.ReportDigest,
        Admission = run.Admission,
        CompatibilityReport = run.CompatibilityReport
    };

    private static bool HasCapability(RunRecord run, string? suppliedToken)
    {
        if (string.IsNullOrWhiteSpace(suppliedToken) || suppliedToken.Length > 128)
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(run.CapabilityToken),
            Encoding.UTF8.GetBytes(suppliedToken));
    }

    private void TransitionLocked(RunRecord run, WorkflowRunState next, string message)
    {
        if (run.State == next)
            return;
        if (!WorkflowRunStates.CanTransition(run.State, next))
            throw new InvalidOperationException(
                $"Invalid workflow run transition from '{WorkflowRunStates.ToWireValue(run.State)}' to '{WorkflowRunStates.ToWireValue(next)}'.");

        run.State = next;
        AddEventLocked(run, WorkflowRunStates.ToWireValue(next), message);
    }

    private static WorkflowRunDispatchDecision DenyUnconfiguredDispatch(WorkflowRunDispatch dispatch)
        => WorkflowRunDispatchDecision.Deny(
            "This broker was not configured to authorize workflow run dispatch, so no run can start.");

    /// <summary>
    /// Claims the mutation lease, waiting for a current holder to finish rather than failing on the
    /// first attempt. The Inspector holds a short renewing lease while it is open, and it is also the
    /// surface a human uses to approve this very run, so failing immediately made an approved run
    /// unrunnable for the most ordinary sequence there is: approve in the Inspector, then run. This
    /// never forces. It only proceeds once the holder releases or its lease lapses, so a human who is
    /// actively driving the app keeps control and the wait ends in an honest, named conflict.
    /// </summary>
    private async Task<MutationLeaseSnapshot> AcquireLeaseAsync(RunRecord run)
    {
        var deadline = _clock.GetUtcNow() + _options.LeaseAcquisitionTimeout;
        while (true)
        {
            var claim = _leases.Control(
                run.Target.LeaseKey,
                "claim",
                run.LeaseId,
                "workflow-run",
                run.RunId,
                force: false,
                transactionId: null);
            if (claim.Allowed ||
                run.Cancellation.IsCancellationRequested ||
                _clock.GetUtcNow() >= deadline)
            {
                return claim;
            }

            try
            {
                await Task.Delay(_options.LeaseAcquisitionPollInterval, _clock, run.Cancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return claim;
            }
        }
    }

    /// <summary>Names the blocking surface, because "held by another lease" is not actionable.</summary>
    private static string DescribeLeaseConflict(MutationLeaseSnapshot claim)
    {
        var holder = string.IsNullOrWhiteSpace(claim.Label) ? claim.HolderKind : claim.Label;
        return string.IsNullOrWhiteSpace(holder)
            ? "The target agent is already held by another mutation lease."
            : $"The target agent is still held by another mutation lease ('{holder}'). Close or " +
              "release that surface, then request a new run approval. The Inspector holds a renewing " +
              "lease while it is open, so approving there and leaving it open blocks the run it just " +
              "authorized; approving with 'maui devflow approve' avoids that.";
    }

    private void AddEventLocked(RunRecord run, string kind, string message)
    {
        if (run.Events.Count == MaxLifecycleEvents)
        {
            run.Events.RemoveAt(0);
            run.LifecycleEventsTruncated = true;
        }
        run.Events.Add(new WorkflowRunLifecycleEvent
        {
            At = _clock.GetUtcNow(),
            Kind = kind,
            Message = message
        });
    }

    private void ReportProgress(RunRecord run, Testing.MauiFlowRunProgress progress)
    {
        if (progress is null)
            return;

        lock (_gate)
        {
            if (WorkflowRunStates.IsTerminal(run.State))
                return;

            run.CurrentStepId = progress.StepId ?? run.CurrentStepId;
            run.CompletedSteps = Math.Clamp(progress.CompletedSteps, 0, run.TotalSteps);
            var kind = progress.Phase is "step-started" or "step-completed"
                ? progress.Phase
                : "step-progress";
            AddEventLocked(
                run,
                kind,
                kind == "step-started"
                    ? "A canonical flow step started."
                    : kind == "step-completed"
                        ? "A canonical flow step completed."
                        : "Canonical flow progress updated.");
        }
    }

    private PreparedStart Prepare(WorkflowRunStartRequest request, WorkflowRunTarget target)
    {
        if (string.IsNullOrWhiteSpace(request.AgentId) ||
            string.IsNullOrWhiteSpace(request.AgentInstanceId) ||
            !string.Equals(request.AgentId, target.AgentId, StringComparison.Ordinal) ||
            !string.Equals(request.AgentInstanceId, target.AgentInstanceId, StringComparison.Ordinal))
        {
            return PreparedStart.Invalid(409, "The request must target the exact connected agent ID and instance ID.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 256)
            return PreparedStart.Invalid(400, "idempotencyKey is required and must be 256 characters or fewer.");

        if (request.Markdown is not null && request.Flow is not null)
            return PreparedStart.Invalid(400, "Specify exactly one of markdown or flow.");

        Testing.MauiFlow? flow = request.Flow;
        if (request.Markdown is not null)
        {
            var parsed = Testing.FlowMarkdown.Parse(request.Markdown);
            if (!parsed.Ok || parsed.Flow is null)
                return PreparedStart.Invalid(400, parsed.Error ?? "Could not parse flow Markdown.");
            flow = parsed.Flow;
        }
        if (flow is null)
            return PreparedStart.Invalid(400, "Either markdown or flow is required.");
        if (flow.Steps is null)
            return PreparedStart.Invalid(400, "The flow must contain a steps array.");
        if (HasNullFlowMembers(flow))
            return PreparedStart.Invalid(400, "The flow contains an invalid null step or assertion.");

        var validation = Testing.FlowValidator.Validate(flow);
        if (!validation.Ok)
        {
            return PreparedStart.Invalid(
                400,
                "Flow failed validation.",
                validation.Errors,
                validation.Warnings);
        }

        if (flow.Steps.Count > _options.MaximumSteps)
            return PreparedStart.Invalid(400, $"Flow has too many steps (max {_options.MaximumSteps}).");

        TimeSpan timeout;
        if (request.TimeoutMs is null)
        {
            timeout = _options.DefaultTimeout;
        }
        else if (request.TimeoutMs <= 0 || request.TimeoutMs > _options.MaximumTimeout.TotalMilliseconds)
        {
            return PreparedStart.Invalid(
                400,
                $"timeoutMs must be greater than zero and no more than {(long)_options.MaximumTimeout.TotalMilliseconds}.");
        }
        else
        {
            timeout = TimeSpan.FromMilliseconds(request.TimeoutMs.Value);
        }
        if (request.DeadlineMs is { } deadlineMs)
        {
            if (deadlineMs <= 0)
                return PreparedStart.Invalid(400, "deadlineMs must be a positive duration.");

            timeout = TimeSpan.FromMilliseconds(
                Math.Min(timeout.TotalMilliseconds, deadlineMs));
        }

        var canonicalFlow = CanonicalizeFlow(flow);
        var clonedFlow = JsonSerializer.Deserialize(canonicalFlow, Testing.MauiFlowJsonContext.Default.MauiFlow);
        if (clonedFlow is null)
            return PreparedStart.Invalid(400, "The flow could not be normalized.");

        var safetyRequest = CloneSafetyRequest(request.Plan, request.Context);
        var admission = Testing.MauiFlowReplaySafetyEvaluator.Evaluate(safetyRequest);
        if (!admission.IsAllowedForIntent(safetyRequest.Context?.Intent))
        {
            return PreparedStart.Invalid(
                409,
                "Flow replay admission was denied before mutation.",
                admission.Reasons
                    .Where(static reason => reason.Blocking == true)
                    .Select(static reason => reason.Message ?? reason.Code ?? "Replay admission was denied.")
                    .ToArray(),
                admission.Reasons
                    .Where(static reason => reason.Blocking != true)
                    .Select(static reason => reason.Message ?? reason.Code ?? "Replay admission warning.")
                    .ToArray(),
                admission);
        }

        var flowDigest = Convert.ToHexString(SHA256.HashData(canonicalFlow)).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(request.Plan?.Flow?.Digest) &&
            !string.Equals(request.Plan.Flow.Digest, flowDigest, StringComparison.OrdinalIgnoreCase))
        {
            return PreparedStart.Invalid(
                409,
                "The plan references a stale flow digest. Refresh or commit the matching flow and plan before replay.");
        }

        var requirementErrors = new List<string>();
        var requirementWarnings = new List<string>();
        ValidatePlanExecutionRequirements(
            request.Plan,
            target,
            request.AvailableCapabilities,
            requirementErrors,
            requirementWarnings);
        if (requirementErrors.Count > 0)
        {
            return PreparedStart.Invalid(
                409,
                "The test plan requirements are not satisfied for the connected target.",
                requirementErrors,
                validation.Warnings
                    .Concat(requirementWarnings)
                    .Concat(admission.Reasons
                        .Where(static reason => reason.Blocking != true)
                        .Select(static reason => reason.Message ?? reason.Code ?? "Replay admission warning."))
                    .ToArray(),
                admission);
        }

        var reproductionError = ValidateReproductionExpectation(
            request.ReproductionExpectation,
            target,
            flowDigest);
        if (reproductionError is not null)
            return PreparedStart.Invalid(400, reproductionError);

        var requestDigest = ComputeRequestDigest(
            target,
            flowDigest,
            timeout,
            ComputeSafetyDigest(safetyRequest),
            ComputeReproductionDigest(request.ReproductionExpectation));
        var warnings = validation.Warnings
            .Concat(requirementWarnings)
            .Concat(admission.Reasons
                .Where(static reason => reason.Blocking != true)
                .Select(static reason => reason.Message ?? reason.Code ?? "Replay admission warning."))
            .ToArray();
        return PreparedStart.Success(clonedFlow, flowDigest, requestDigest, timeout, warnings, safetyRequest, admission);
    }

    private static Testing.MauiFlowRunRequest CloneSafetyRequest(
        Testing.MauiTestPlan? plan,
        Testing.MauiFlowRunContext? context)
    {
        var source = new Testing.MauiFlowRunRequest
        {
            Plan = plan,
            Context = context,
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(source, Testing.MauiTestingJsonContext.Default.MauiFlowRunRequest);
        return JsonSerializer.Deserialize(bytes, Testing.MauiTestingJsonContext.Default.MauiFlowRunRequest) ??
            new Testing.MauiFlowRunRequest();
    }

    private static string ComputeSafetyDigest(Testing.MauiFlowRunRequest request)
    {
        var element = JsonSerializer.SerializeToElement(request, Testing.MauiTestingJsonContext.Default.MauiFlowRunRequest);
        using var output = new MemoryStream();
        using var writer = new Utf8JsonWriter(output);
        WriteCanonicalJson(writer, element);
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(output.ToArray())).ToLowerInvariant();
    }

    private static string ComputeReproductionDigest(Testing.MauiLocalReproductionExpectation? expectation)
    {
        if (expectation is null)
            return string.Empty;

        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            expectation,
            Testing.MauiTestingJsonContext.Default.MauiLocalReproductionExpectation);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string? ValidateReproductionExpectation(
        Testing.MauiLocalReproductionExpectation? expectation,
        WorkflowRunTarget target,
        string flowDigest)
    {
        if (expectation is null)
            return null;

        if (string.IsNullOrWhiteSpace(expectation.FlowDigest) ||
            string.IsNullOrWhiteSpace(expectation.AppBuildFingerprint) ||
            string.IsNullOrWhiteSpace(expectation.AppSourceFingerprint) ||
            string.IsNullOrWhiteSpace(expectation.PackageDigest) ||
            string.IsNullOrWhiteSpace(expectation.Platform) ||
            string.IsNullOrWhiteSpace(expectation.DeviceProfile))
        {
            return "reproductionExpectation requires flow, app build/source, package, platform, and device profile fingerprints.";
        }

        if (!string.Equals(expectation.FlowDigest, flowDigest, StringComparison.Ordinal))
            return "reproductionExpectation.flowDigest must match the canonical flow digest.";

        if (!string.IsNullOrWhiteSpace(target.Platform) &&
            !string.Equals(expectation.Platform, target.Platform, StringComparison.OrdinalIgnoreCase))
        {
            return "reproductionExpectation.platform must match the connected agent platform.";
        }

        return null;
    }

    private static void ValidatePlanExecutionRequirements(
        Testing.MauiTestPlan? plan,
        WorkflowRunTarget target,
        Testing.MauiFlowCapabilitySet? available,
        List<string> errors,
        List<string> warnings)
    {
        if (plan is null)
            return;

        var actualPlatform = NormalizePlatformTag(target.Platform);
        var requiredPlatforms = plan.RequiredPlatforms
            .Where(static platform => !string.IsNullOrWhiteSpace(platform))
            .Select(NormalizePlatformTag)
            .Where(static platform => !string.IsNullOrWhiteSpace(platform))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requiredPlatforms.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(actualPlatform))
            {
                errors.Add("requiredPlatforms are declared, but the connected target did not report a platform.");
            }
            else if (!requiredPlatforms.Contains(actualPlatform, StringComparer.Ordinal))
            {
                errors.Add(
                    $"requiredPlatforms [{string.Join(", ", requiredPlatforms)}] do not include the connected platform '{target.Platform}'.");
            }
        }

        var requirementValidation = Testing.MauiFlowRequirementValidator.Validate(plan.Requirements, available);
        errors.AddRange(requirementValidation.Errors.Select(FormatRequirementViolation));
        warnings.AddRange(requirementValidation.Warnings.Select(FormatRequirementViolation));
    }

    private static string FormatRequirementViolation(Testing.MauiFlowRequirementViolation violation)
        => string.IsNullOrWhiteSpace(violation.Code)
            ? violation.Message
            : $"[{violation.Code}] {violation.Message}";

    internal static Testing.MauiFlowCapabilitySet BuildAvailableCapabilities(AgentStatus? status)
    {
        var capabilities = new Testing.MauiFlowCapabilitySet();
        AddCapability(capabilities, status?.Capabilities?.Ui == true, "ui", "agent.ui");
        AddCapability(capabilities, "screenshots", status?.Capabilities?.Screenshots == true);
        AddCapability(capabilities, status?.Capabilities?.WebView == true, "webview", "agent.webview");
        AddCapability(capabilities, "network", status?.Capabilities?.Network == true);
        AddCapability(capabilities, "logs", status?.Capabilities?.Logs == true);
        AddCapability(capabilities, "sensors", status?.Capabilities?.Sensors == true);
        AddCapability(capabilities, "storage", status?.Capabilities?.Storage == true);
        AddCapability(capabilities, "profiler", status?.Capabilities?.Profiler == true);
        AddCapability(capabilities, "jobs", status?.Capabilities?.Jobs == true);
        AddCapability(capabilities, status?.Capabilities?.Theme == true, "theme", "app.theme", "agent.theme");
        AddCapability(capabilities, status?.Capabilities?.Mutations == true, "mutations", "agent.mutations");
        AddCapability(
            capabilities,
            status?.Capabilities?.WorkflowCommandLedger == true,
            "workflowCommandLedger",
            "agent.workflowCommandLedger");
        return capabilities;
    }

    private static void AddCapability(
        Testing.MauiFlowCapabilitySet available,
        string name,
        bool present)
    {
        if (!present)
            return;

        available.Capabilities.Add(new Testing.MauiFlowCapability
        {
            Name = name,
            Version = 1,
        });
    }

    private static void AddCapability(
        Testing.MauiFlowCapabilitySet available,
        bool present,
        params string[] names)
    {
        if (!present)
            return;

        foreach (var name in names.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            if (available.Capabilities.Any(capability => string.Equals(capability.Name, name, StringComparison.Ordinal)))
                continue;

            AddCapability(available, name, present: true);
        }
    }

    private static string NormalizePlatformTag(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        normalized = normalized.Replace('_', '-').Replace(' ', '-');
        return normalized switch
        {
            "winui" or "windows" => "windows",
            "ios-simulator" => "ios",
            "mac-catalyst" => "maccatalyst",
            _ => normalized,
        };
    }

    private static bool HasNullFlowMembers(Testing.MauiFlow flow)
    {
        foreach (var step in flow.Steps)
        {
            if (step is null || step.Asserts?.Any(assertion => assertion is null) == true)
                return true;
        }
        return false;
    }

    private static byte[] CanonicalizeFlow(Testing.MauiFlow flow)
    {
        var element = JsonSerializer.SerializeToElement(flow, Testing.MauiFlowJsonContext.Default.MauiFlow);
        using var output = new MemoryStream();
        using var writer = new Utf8JsonWriter(output);
        WriteCanonicalJson(writer, element);
        writer.Flush();
        return output.ToArray();
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteCanonicalJson(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static string ComputeRequestDigest(
        WorkflowRunTarget target,
        string flowDigest,
        TimeSpan timeout,
        string safetyDigest,
        string reproductionDigest)
    {
        var material = string.Join(
            "\n",
            target.AgentId,
            target.AgentInstanceId,
            target.AgentPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            flowDigest,
            ((long)timeout.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
            safetyDigest,
            reproductionDigest);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    private static string ClassifyFailure(Testing.FlowReplayReport report)
        => Testing.MauiFlowFailureClassifier.Classify(new Testing.MauiFlowFailureFacts
        {
            LegacyFailureKind = report.Results.FirstOrDefault(result => !result.Ok)?.FailureKind,
        }).FailureClass;

    private static string MapFailureKind(string kind)
        => Testing.MauiFlowFailureClassifier.FromLegacyFailureKind(kind)
            ?? Testing.MauiFlowFailureClasses.Infrastructure;

    private static string CreateOpaqueId(string prefix)
        => $"{prefix}_{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}";

    private static string CreateCapabilityToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class RunRecord
    {
        public RunRecord(
            string runId,
            string capabilityToken,
            string idempotencyKey,
            string requestDigest,
            string flowDigest,
            Testing.MauiFlow flow,
            Testing.MauiFlowRunRequest safetyRequest,
            Testing.MauiFlowReplayEligibilityDecision admission,
            WorkflowRunTarget target,
            TimeSpan timeout,
            Func<bool> isTargetCurrent,
            WorkflowRunExecutionOptions executionOptions,
            DateTimeOffset createdAt)
        {
            RunId = runId;
            CapabilityToken = capabilityToken;
            IdempotencyKey = idempotencyKey;
            RequestDigest = requestDigest;
            FlowDigest = flowDigest;
            Flow = flow;
            SafetyRequest = safetyRequest;
            Admission = admission;
            Target = target;
            Timeout = timeout;
            IsTargetCurrent = isTargetCurrent;
            ExecutionOptions = new WorkflowRunExecutionOptions
            {
                EvidenceCaptureFactory = executionOptions.EvidenceCaptureFactory,
                ReproductionExpectation = CloneReproductionExpectation(executionOptions.ReproductionExpectation),
                RecordDeviceRun = executionOptions.RecordDeviceRun,
                DeviceRecordingTimeoutSeconds = executionOptions.DeviceRecordingTimeoutSeconds > 0
                    ? Math.Clamp(executionOptions.DeviceRecordingTimeoutSeconds, 1, 3600)
                    : Math.Clamp((int)Math.Ceiling(timeout.TotalSeconds) + 30, 1, 3600),
                DeviceRecordingCaptured = executionOptions.DeviceRecordingCaptured,
            };
            CreatedAt = createdAt;
            TotalSteps = flow.Steps.Count;
            LeaseId = CreateOpaqueId("lease");
            TransactionId = CreateOpaqueId("transaction");
        }

        public string RunId { get; }
        public string CapabilityToken { get; }
        public string IdempotencyKey { get; }
        public string RequestDigest { get; }
        public string FlowDigest { get; }
        public Testing.MauiFlow Flow { get; }
        public Testing.MauiFlowRunRequest SafetyRequest { get; }
        public Testing.MauiFlowReplayEligibilityDecision Admission { get; private set; }

        /// <summary>
        /// Replaces the admission decided before the run with the one decided from what the run
        /// established. Only the post-run decision can account for independent oracle evidence,
        /// which by definition does not exist yet when a run is admitted.
        /// </summary>
        public void AdoptPostRunAdmission(Testing.MauiFlowReplayEligibilityDecision decision)
            => Admission = decision ?? throw new ArgumentNullException(nameof(decision));

        public WorkflowRunTarget Target { get; }
        public TimeSpan Timeout { get; }
        public Func<bool> IsTargetCurrent { get; }
        public WorkflowRunExecutionOptions ExecutionOptions { get; }
        public string LeaseId { get; }
        public string TransactionId { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public WorkflowRunState State { get; set; } = WorkflowRunState.Queued;
        public List<WorkflowRunLifecycleEvent> Events { get; } = [];
        public CancellationTokenSource Cancellation { get; } = new();
        public CancellationTokenSource TimeoutCancellation { get; } = new();
        public CancellationTokenSource HeartbeatCancellation { get; } = new();
        public TaskCompletionSource<WorkflowRunSnapshot> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? ExecutionTask { get; set; }
        public Task? HeartbeatTask { get; set; }
        public bool CancellationRequested { get; set; }
        public bool LeaseLostRequested { get; set; }
        public bool AgentInstanceUnavailable { get; set; }
        public string? OverrideMessage { get; set; }
        public bool LeaseClaimed { get; set; }
        public bool TransactionBegun { get; set; }
        public bool LedgerBeginAttempted { get; set; }
        public bool LedgerBegun { get; set; }
        public long AuthorityEpoch { get; set; }
        public int LeaseEnded;
        public int? FirstDivergence { get; set; }
        public string? Message { get; set; }
        public Testing.FlowReplayReport? CompatibilityReport { get; set; }
        public Testing.MauiFlowRunReport? StructuredReport { get; set; }
        public string? ReportPath { get; set; }
        public string? ReportDigest { get; set; }
        public int TotalSteps { get; }
        public int CompletedSteps { get; set; }
        public string? CurrentStepId { get; set; }
        public bool LifecycleEventsTruncated { get; set; }
        public long TerminalOrder { get; set; }
    }

    private static Testing.MauiLocalReproductionExpectation? CloneReproductionExpectation(
        Testing.MauiLocalReproductionExpectation? expectation)
        => expectation is null
            ? null
            : JsonSerializer.Deserialize(
                JsonSerializer.SerializeToUtf8Bytes(
                    expectation,
                    Testing.MauiTestingJsonContext.Default.MauiLocalReproductionExpectation),
                Testing.MauiTestingJsonContext.Default.MauiLocalReproductionExpectation);

    private sealed record IdempotencyEntry(string RequestDigest, string RunId);

    private sealed class PreparedStart
    {
        public bool Ok { get; private init; }
        public int StatusCode { get; private init; }
        public string? Error { get; private init; }
        public Testing.MauiFlow? Flow { get; private init; }
        public Testing.MauiFlowRunRequest? SafetyRequest { get; private init; }
        public Testing.MauiFlowReplayEligibilityDecision? Admission { get; private init; }
        public string? FlowDigest { get; private init; }
        public string? RequestDigest { get; private init; }
        public TimeSpan Timeout { get; private init; }
        public IReadOnlyList<string> ValidationErrors { get; private init; } = [];
        public IReadOnlyList<string> ValidationWarnings { get; private init; } = [];

        public static PreparedStart Success(
            Testing.MauiFlow flow,
            string flowDigest,
            string requestDigest,
            TimeSpan timeout,
            IReadOnlyList<string> warnings,
            Testing.MauiFlowRunRequest safetyRequest,
            Testing.MauiFlowReplayEligibilityDecision admission)
            => new()
            {
                Ok = true,
                Flow = flow,
                FlowDigest = flowDigest,
                RequestDigest = requestDigest,
                Timeout = timeout,
                ValidationWarnings = warnings.ToArray(),
                SafetyRequest = safetyRequest,
                Admission = admission,
            };

        public static PreparedStart Invalid(
            int statusCode,
            string error,
            IReadOnlyList<string>? errors = null,
            IReadOnlyList<string>? warnings = null,
            Testing.MauiFlowReplayEligibilityDecision? admission = null)
            => new()
            {
                StatusCode = statusCode,
                Error = error,
                ValidationErrors = errors?.ToArray() ?? [],
                ValidationWarnings = warnings?.ToArray() ?? [],
                Admission = admission,
            };
    }
}

internal interface IWorkflowMutationLeaseRegistry
{
    MutationLeaseSnapshot Control(
        string agentId,
        string action,
        string? leaseId,
        string? holderKind,
        string? label,
        bool force,
        string? transactionId);

    MutationLeaseSnapshot TransferAndBegin(
        string agentId,
        string sourceLeaseId,
        string targetLeaseId,
        string transactionId,
        string? holderKind,
        string? label);

    /// <summary>
    /// Adopts an idle lease held by an allow-listed trusted host. Implementations must refuse while a
    /// transaction is open so an active human driver is never interrupted.
    /// </summary>
    MutationLeaseSnapshot TryAdoptIdleLease(
        string agentId,
        string targetLeaseId,
        string transactionId,
        string? holderKind,
        string? label,
        IReadOnlyCollection<string> adoptableHolderKinds);
}

internal sealed record WorkflowRunLeaseHandoff(
    string LeaseId,
    string HolderKind,
    string? Label);

/// <summary>
/// How one workflow-run dispatch claims to be authorized. The default is the strictest shape, so a
/// caller that forgets to declare an origin is held to the human-grant rule instead of sailing past.
/// </summary>
internal enum WorkflowRunDispatchOrigin
{
    /// <summary>An MCP test-agent dispatch backed by a live human-issued mutation grant.</summary>
    TestAgentGrant = 0,
    /// <summary>The broker-owned Inspector workbench acting for a human at the local Inspector UI.</summary>
    InspectorWorkbench,
    /// <summary>The broker's in-process Inspector replay bridge for one exact agent instance.</summary>
    InspectorReplayBridge,
    /// <summary>The broker's transient replay that validates a reviewer-approved repair proposal.</summary>
    RepairValidation,
}

/// <summary>One dispatch presented for authorization, described in the broker's own canonical terms.</summary>
internal sealed record WorkflowRunDispatch(
    WorkflowRunDispatchOrigin Origin,
    string AgentId,
    string AgentInstanceId,
    string? AuthorizationId,
    string? DispatchTicket,
    WorkflowRunLeaseHandoff? LeaseHandoff,
    IReadOnlyList<Testing.FlowStep>? Steps = null,
    bool HasDeviceExtensions = false,
    string? FlowDigest = null)
{
    /// <summary>
    /// Keeps the ticket out of the generated <c>ToString()</c>, so interpolating a dispatch into a
    /// log line or an exception message cannot publish a credential that stays valid for the life
    /// of the broker process. The flow's steps are summarized by count for the same reason: they
    /// carry recorded selectors and values.
    /// </summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"{nameof(Origin)} = {Origin}, ");
        builder.Append($"{nameof(AgentId)} = {AgentId}, ");
        builder.Append($"{nameof(AgentInstanceId)} = {AgentInstanceId}, ");
        builder.Append($"{nameof(HasDeviceExtensions)} = {HasDeviceExtensions}, ");
        builder.Append($"{nameof(FlowDigest)} = {(FlowDigest is null ? "null" : "present")}, ");
        builder.Append($"{nameof(AuthorizationId)} = {AuthorizationId}, ");
        builder.Append($"{nameof(DispatchTicket)} = {(DispatchTicket is null ? "null" : "[redacted]")}, ");
        builder.Append($"{nameof(Steps)} = {(Steps is null ? "null" : Steps.Count)}, ");
        builder.Append($"{nameof(LeaseHandoff)} = {LeaseHandoff}");
        return true;
    }
}

/// <summary>The broker's answer, with the reason recorded on the run's audit journal when allowed.</summary>
internal sealed record WorkflowRunDispatchDecision(bool Allowed, string? Error, string? AuditReason)
{
    public static WorkflowRunDispatchDecision Allow(string auditReason) => new(true, null, auditReason);

    public static WorkflowRunDispatchDecision Deny(string error) => new(false, error, null);
}

/// <summary>
/// Verifies, broker-side, that one workflow-run dispatch is permitted. The coordinator refuses to
/// start without an allowing decision, so authorization cannot be lost by adding a caller that
/// forgets to repeat the check the way a route-level guard can be.
/// </summary>
internal delegate WorkflowRunDispatchDecision WorkflowRunDispatchAuthorizer(WorkflowRunDispatch dispatch);

internal sealed record WorkflowRunLedgerControl(
    string Action,
    string RunId,
    WorkflowRunTarget Target,
    string LeaseId,
    long AuthorityEpoch,
    string? ApprovalDigest,
    string? Reason);

internal sealed class WorkflowRunLedgerControlResult
{
    public bool Ok { get; private init; }
    public string? Reason { get; private init; }
    public string? Error { get; private init; }

    public static WorkflowRunLedgerControlResult Success() => new() { Ok = true };

    public static WorkflowRunLedgerControlResult Failure(string? reason, string? error)
        => new()
        {
            Reason = reason,
            Error = error
        };
}

internal sealed class WorkflowRunCoordinatorOptions
{
    public int MaxRetainedTerminalRuns { get; init; } = 128;
    public int MaximumSteps { get; init; } = 2_000;
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan MaximumTimeout { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long an approved run waits for a busy mutation lease before giving up. The Inspector's
    /// lease renews only while it is open, so a few seconds covers the ordinary "approve, then close
    /// the panel" sequence without ever taking the app from a human still using it.
    /// </summary>
    public TimeSpan LeaseAcquisitionTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan LeaseAcquisitionPollInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How long the post-run independent business-oracle read may take. It runs after the mutation
    /// lease is released, so it delays only the run's own terminal transition.
    /// </summary>
    public TimeSpan OracleEvaluationTimeout { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>Optional trusted root for atomic &lt;runId&gt;/flow-run.json artifacts.</summary>
    public string? ArtifactRoot { get; init; }
    public Testing.MauiFlowRunReportLimits ReportLimits { get; init; } = new();
}

internal enum WorkflowRunState
{
    Queued,
    AcquiringLease,
    Preparing,
    Running,
    Passed,
    Failed,
    Cancelled,
    TimedOut,
    LeaseLost,
    InfrastructureError,
    UnknownCompletion,
    Orphaned
}

internal static class WorkflowRunStates
{
    public static bool IsTerminal(WorkflowRunState state) => state is
        WorkflowRunState.Passed or
        WorkflowRunState.Failed or
        WorkflowRunState.Cancelled or
        WorkflowRunState.TimedOut or
        WorkflowRunState.LeaseLost or
        WorkflowRunState.InfrastructureError or
        WorkflowRunState.UnknownCompletion or
        WorkflowRunState.Orphaned;

    public static bool CanTransition(WorkflowRunState current, WorkflowRunState next) => current switch
    {
        WorkflowRunState.Queued => next is WorkflowRunState.AcquiringLease or WorkflowRunState.Cancelled,
        WorkflowRunState.AcquiringLease => next is WorkflowRunState.Preparing or
            WorkflowRunState.Failed or WorkflowRunState.Cancelled or WorkflowRunState.LeaseLost or
            WorkflowRunState.TimedOut or WorkflowRunState.InfrastructureError or
            WorkflowRunState.UnknownCompletion or WorkflowRunState.Orphaned,
        WorkflowRunState.Preparing => next is WorkflowRunState.Running or WorkflowRunState.Failed or
            WorkflowRunState.Cancelled or WorkflowRunState.LeaseLost or WorkflowRunState.TimedOut or
            WorkflowRunState.InfrastructureError or WorkflowRunState.UnknownCompletion or
            WorkflowRunState.Orphaned,
        WorkflowRunState.Running => IsTerminal(next),
        _ => false
    };

    public static string ToWireValue(WorkflowRunState state) => state switch
    {
        WorkflowRunState.Queued => "queued",
        WorkflowRunState.AcquiringLease => "acquiring-lease",
        WorkflowRunState.Preparing => "preparing",
        WorkflowRunState.Running => "running",
        WorkflowRunState.Passed => "passed",
        WorkflowRunState.Failed => "failed",
        WorkflowRunState.Cancelled => "cancelled",
        WorkflowRunState.TimedOut => "timed-out",
        WorkflowRunState.LeaseLost => "lease-lost",
        WorkflowRunState.InfrastructureError => "infrastructure-error",
        WorkflowRunState.UnknownCompletion => "unknown-completion",
        WorkflowRunState.Orphaned => "orphaned",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}

internal sealed class WorkflowRunStartRequest
{
    [JsonPropertyName("agentId")] public string? AgentId { get; set; }
    [JsonPropertyName("agentInstanceId")] public string? AgentInstanceId { get; set; }
    /// <summary>
    /// Identifier of the human-approved mutation authorization that permits this dispatch. The
    /// coordinator verifies it in <see cref="WorkflowRunCoordinator.Start"/> before starting the
    /// run, so no dispatch surface can reach the device without it.
    /// </summary>
    [JsonPropertyName("authorizationId")] public string? AuthorizationId { get; set; }
    [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; set; }
    [JsonPropertyName("markdown")] public string? Markdown { get; set; }
    [JsonPropertyName("flow")] public Testing.MauiFlow? Flow { get; set; }
    [JsonPropertyName("timeoutMs")] public int? TimeoutMs { get; set; }
    [JsonPropertyName("deadlineMs")] public int? DeadlineMs { get; set; }
    [JsonPropertyName("plan")] public Testing.MauiTestPlan? Plan { get; set; }
    [JsonPropertyName("context")] public Testing.MauiFlowRunContext? Context { get; set; }
    [JsonPropertyName("availableCapabilities")] public Testing.MauiFlowCapabilitySet? AvailableCapabilities { get; set; }
    /// <summary>
    /// Optional host-supplied current-workspace fingerprints recorded with the newly executed
    /// local run. They are not imported evidence and do not grant proposal authority by themselves.
    /// </summary>
    [JsonPropertyName("reproductionExpectation")] public Testing.MauiLocalReproductionExpectation? ReproductionExpectation { get; set; }
}

internal sealed class WorkflowRunAccessRequest
{
    [JsonPropertyName("capabilityToken")] public string? CapabilityToken { get; set; }
}

internal sealed class WorkflowRunCapabilitiesResponse
{
    [JsonPropertyName("schema")] public int Schema { get; init; } = 1;
    [JsonPropertyName("supported")] public bool Supported { get; init; } = true;
    [JsonPropertyName("requiresExplicitAgentInstance")] public bool RequiresExplicitAgentInstance { get; init; } = true;
    [JsonPropertyName("requiresIdempotencyKey")] public bool RequiresIdempotencyKey { get; init; } = true;
    [JsonPropertyName("capabilityTokenRequired")] public bool CapabilityTokenRequired { get; init; } = true;
    [JsonPropertyName("states")] public string[] States { get; init; } =
        Enum.GetValues<WorkflowRunState>().Select(WorkflowRunStates.ToWireValue).ToArray();
    [JsonPropertyName("maxTimeoutMs")] public long MaxTimeoutMs { get; init; }
    [JsonPropertyName("maxSteps")] public int MaxSteps { get; init; }
    [JsonPropertyName("workflowCommandLedger")] public bool WorkflowCommandLedger { get; init; }
}

/// <summary>
/// A read-only result from canonical admission validation. Unlike a start result this never
/// reserves a target, mints a run token, or creates journal state.
/// </summary>
internal sealed class WorkflowRunPreflightResult
{
    [JsonPropertyName("ok")] public bool Ok { get; private init; }
    [JsonPropertyName("flowDigest")] public string? FlowDigest { get; private init; }
    [JsonPropertyName("timeoutMs")] public long? TimeoutMs { get; private init; }
    [JsonPropertyName("error")] public string? Error { get; private init; }
    [JsonPropertyName("errors")] public IReadOnlyList<string>? Errors { get; private init; }
    [JsonPropertyName("warnings")] public IReadOnlyList<string>? Warnings { get; private init; }
    [JsonPropertyName("admission")] public Testing.MauiFlowReplayEligibilityDecision? Admission { get; private init; }
    [JsonPropertyName("deviceEffects")] public IReadOnlyList<string>? DeviceEffects { get; private set; }
    [JsonPropertyName("deviceEffectsDigest")] public string? DeviceEffectsDigest { get; private set; }
    [JsonIgnore] public int StatusCode { get; private init; }

    public WorkflowRunPreflightResult WithDeviceReview(
        IReadOnlyList<string> effects,
        string digest)
    {
        DeviceEffects = effects.Count > 0 ? effects : null;
        DeviceEffectsDigest = effects.Count > 0 ? digest : null;
        return this;
    }

    public static WorkflowRunPreflightResult Accepted(
        string flowDigest,
        long timeoutMs,
        IReadOnlyList<string>? warnings,
        Testing.MauiFlowReplayEligibilityDecision admission) => new()
    {
        Ok = true,
        FlowDigest = flowDigest,
        TimeoutMs = timeoutMs,
        Warnings = warnings is { Count: > 0 } ? warnings : null,
        Admission = admission,
        StatusCode = 200,
    };

    public static WorkflowRunPreflightResult Rejected(
        int statusCode,
        string error,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<string>? warnings = null,
        Testing.MauiFlowReplayEligibilityDecision? admission = null) => new()
    {
        Error = error,
        Errors = errors is { Count: > 0 } ? errors : null,
        Warnings = warnings is { Count: > 0 } ? warnings : null,
        Admission = admission,
        StatusCode = statusCode,
    };
}

internal sealed class WorkflowRunStartResult
{
    [JsonPropertyName("ok")] public bool Ok { get; private init; }
    [JsonPropertyName("existing")] public bool Existing { get; private init; }
    [JsonPropertyName("run")] public WorkflowRunSnapshot? Run { get; private init; }
    [JsonPropertyName("capabilityToken")] public string? CapabilityToken { get; private init; }
    [JsonPropertyName("error")] public string? Error { get; private init; }
    [JsonPropertyName("errors")] public IReadOnlyList<string>? Errors { get; private init; }
    [JsonPropertyName("warnings")] public IReadOnlyList<string>? Warnings { get; private init; }
    [JsonPropertyName("admission")] public Testing.MauiFlowReplayEligibilityDecision? Admission { get; private init; }
    [JsonIgnore] public int StatusCode { get; private init; }

    public static WorkflowRunStartResult Started(WorkflowRunSnapshot run, string capabilityToken) => new()
    {
        Ok = true,
        Run = run,
        CapabilityToken = capabilityToken,
        StatusCode = 202
    };

    public static WorkflowRunStartResult FromExisting(WorkflowRunSnapshot run, string capabilityToken) => new()
    {
        Ok = true,
        Existing = true,
        Run = run,
        CapabilityToken = capabilityToken,
        StatusCode = 200
    };

    public static WorkflowRunStartResult Conflict(string error) => new()
    {
        Error = error,
        StatusCode = 409
    };

    public static WorkflowRunStartResult Rejected(
        int statusCode,
        string error,
        IReadOnlyList<string>? errors,
        IReadOnlyList<string>? warnings,
        Testing.MauiFlowReplayEligibilityDecision? admission = null) => new()
    {
        Error = error,
        Errors = errors is { Count: > 0 } ? errors : null,
        Warnings = warnings is { Count: > 0 } ? warnings : null,
        Admission = admission,
        StatusCode = statusCode
    };
}

internal sealed class WorkflowRunAccessResult
{
    public WorkflowRunSnapshot? Run { get; private init; }
    public int StatusCode { get; private init; }
    public string? Error { get; private init; }

    public static WorkflowRunAccessResult Success(WorkflowRunSnapshot run) => new() { Run = run, StatusCode = 200 };
    public static WorkflowRunAccessResult NotFound() => new() { StatusCode = 404, Error = "Workflow run was not found." };
    public static WorkflowRunAccessResult Unauthorized() => new() { StatusCode = 403, Error = "A valid workflow run capability token is required." };
}

internal sealed class WorkflowRunRepairContextResult
{
    public WorkflowRunRepairContext? Context { get; private init; }
    public int StatusCode { get; private init; }
    public string? Error { get; private init; }

    public static WorkflowRunRepairContextResult Success(WorkflowRunRepairContext context)
        => new() { Context = context, StatusCode = 200 };

    public static WorkflowRunRepairContextResult NotFound()
        => new() { StatusCode = 404, Error = "Workflow run was not found." };

    public static WorkflowRunRepairContextResult Unauthorized()
        => new() { StatusCode = 403, Error = "A valid workflow run capability token is required." };

    public static WorkflowRunRepairContextResult Unavailable(string error)
        => new() { StatusCode = 409, Error = error };
}

internal sealed class WorkflowRunRepairContext
{
    public string RunId { get; init; } = "";
    public string FlowDigest { get; init; } = "";
    public WorkflowRunTargetSnapshot Target { get; init; } = new();
    public Testing.MauiFlow Flow { get; init; } = new();
    public Testing.MauiTestPlan? Plan { get; init; }
    public Testing.MauiFlowRunReport Report { get; init; } = new();
    public Testing.MauiFlowReplayEligibilityDecision Admission { get; init; } = new();
}

internal sealed class WorkflowRunLocalReproductionResult
{
    public bool Ok { get; private init; }
    public string? Error { get; private init; }
    public Testing.MauiLocalReproductionFacts? Facts { get; private init; }

    public static WorkflowRunLocalReproductionResult Success(Testing.MauiLocalReproductionFacts facts)
        => new() { Ok = true, Facts = facts };

    public static WorkflowRunLocalReproductionResult NotFound()
        => new() { Error = "The local workflow run was not found." };

    public static WorkflowRunLocalReproductionResult Unavailable(string error)
        => new() { Error = error };
}

internal sealed class WorkflowRunPriorSelectorResolutionResult
{
    public bool Ok { get; private init; }
    public string? Error { get; private init; }
    public Testing.MauiRepairPriorSelectorResolution? Resolution { get; private init; }

    public static WorkflowRunPriorSelectorResolutionResult Success(
        Testing.MauiRepairPriorSelectorResolution resolution)
        => new() { Ok = true, Resolution = resolution };

    public static WorkflowRunPriorSelectorResolutionResult Unavailable(string error)
        => new() { Error = error };
}

internal sealed class WorkflowRunStatusResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("run")] public WorkflowRunSnapshot? Run { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }

    public static WorkflowRunStatusResponse Success(WorkflowRunSnapshot run) => new() { Ok = true, Run = run };
    public static WorkflowRunStatusResponse Failure(string error) => new() { Error = error };
}

internal sealed class WorkflowRunCancelResult
{
    [JsonPropertyName("ok")] public bool Ok { get; private init; }
    [JsonPropertyName("accepted")] public bool Accepted { get; private init; }
    [JsonPropertyName("alreadyTerminal")] public bool AlreadyTerminal { get; private init; }
    [JsonPropertyName("run")] public WorkflowRunSnapshot? Run { get; private init; }
    [JsonPropertyName("error")] public string? Error { get; private init; }
    [JsonIgnore] public int StatusCode { get; private init; }

    public static WorkflowRunCancelResult FromAccepted(WorkflowRunSnapshot run, bool alreadyTerminal) => new()
    {
        Ok = true,
        Accepted = !alreadyTerminal,
        AlreadyTerminal = alreadyTerminal,
        Run = run,
        StatusCode = 200
    };

    public static WorkflowRunCancelResult NotFound() => new()
    {
        StatusCode = 404,
        Error = "Workflow run was not found."
    };

    public static WorkflowRunCancelResult Unauthorized() => new()
    {
        StatusCode = 403,
        Error = "A valid workflow run capability token is required."
    };
}

internal sealed class WorkflowRunSnapshot
{
    [JsonPropertyName("schema")] public int Schema { get; init; }
    [JsonPropertyName("runId")] public string RunId { get; init; } = "";
    [JsonPropertyName("state")] public string State { get; init; } = "";
    [JsonPropertyName("terminal")] public bool Terminal { get; init; }
    [JsonPropertyName("flowDigest")] public string FlowDigest { get; init; } = "";
    [JsonPropertyName("authorityEpoch")] public long? AuthorityEpoch { get; init; }
    [JsonPropertyName("target")] public WorkflowRunTargetSnapshot Target { get; init; } = new();
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("startedAt")] public DateTimeOffset? StartedAt { get; init; }
    [JsonPropertyName("endedAt")] public DateTimeOffset? EndedAt { get; init; }
    [JsonPropertyName("totalSteps")] public int TotalSteps { get; init; }
    [JsonPropertyName("completedSteps")] public int CompletedSteps { get; init; }
    [JsonPropertyName("currentStepId")] public string? CurrentStepId { get; init; }
    [JsonPropertyName("firstDivergence")] public int? FirstDivergence { get; init; }
    [JsonPropertyName("cancellationRequested")] public bool CancellationRequested { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
    [JsonPropertyName("events")] public IReadOnlyList<WorkflowRunLifecycleEvent> Events { get; init; } = [];
    [JsonPropertyName("report")] public Testing.MauiFlowRunReport? Report { get; init; }
    [JsonPropertyName("reportPath")] public string? ReportPath { get; init; }
    [JsonPropertyName("reportDigest")] public string? ReportDigest { get; init; }
    [JsonPropertyName("admission")] public Testing.MauiFlowReplayEligibilityDecision? Admission { get; init; }
    [JsonIgnore] public Testing.FlowReplayReport? CompatibilityReport { get; init; }
}

internal sealed class WorkflowRunLifecycleEvent
{
    [JsonPropertyName("at")] public DateTimeOffset At { get; init; }
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";
    [JsonPropertyName("message")] public string Message { get; init; } = "";
}

internal sealed record WorkflowRunTarget(
    string AgentId,
    string AgentInstanceId,
    int AgentPort,
    string? Platform = null,
    string? AppName = null,
    string? DeviceLeaseKey = null)
{
    [JsonIgnore]
    public string LeaseKey => string.IsNullOrWhiteSpace(DeviceLeaseKey) ? AgentId : DeviceLeaseKey;

    public WorkflowRunTargetSnapshot ToSnapshot() => new()
    {
        AgentId = AgentId,
        AgentInstanceId = AgentInstanceId,
        Platform = Platform,
        AppName = AppName
    };
}

internal sealed class WorkflowRunTargetSnapshot
{
    [JsonPropertyName("agentId")] public string AgentId { get; init; } = "";
    [JsonPropertyName("agentInstanceId")] public string AgentInstanceId { get; init; } = "";
    [JsonPropertyName("platform")] public string? Platform { get; init; }
    [JsonPropertyName("appName")] public string? AppName { get; init; }
}

internal sealed class WorkflowRunExecutionOptions
{
    public Func<AgentClient, Testing.IFlowReplayEvidenceCapture?>? EvidenceCaptureFactory { get; init; }
    public Testing.MauiLocalReproductionExpectation? ReproductionExpectation { get; init; }
    public bool RecordDeviceRun { get; init; }
    public int DeviceRecordingTimeoutSeconds { get; init; }
    public Action<string, DeviceLayer.DeviceRecordingCapture>? DeviceRecordingCaptured { get; init; }
    public Action<Testing.MauiFlowRunProgress>? Progress { get; init; }
}

internal sealed record WorkflowRunExecution(
    string RunId,
    Testing.MauiFlow Flow,
    Testing.MauiFlowRunRequest SafetyRequest,
    Testing.MauiFlowReplayEligibilityDecision Admission,
    WorkflowRunTarget Target,
    string LeaseId,
    string TransactionId,
    long AuthorityEpoch,
    WorkflowRunExecutionOptions Options);

internal sealed class WorkflowRunNotFoundException : Exception
{
    public WorkflowRunNotFoundException(string runId)
        : base($"Workflow run '{runId}' was not found.")
    {
    }
}

internal sealed class WorkflowRunCapabilityException : Exception
{
    public WorkflowRunCapabilityException()
        : base("A valid workflow run capability token is required.")
    {
    }
}

internal sealed class WorkflowRunRejectedException : Exception
{
    public WorkflowRunRejectedException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

internal readonly record struct WorkflowRunTargetKey(string AgentId, string AgentInstanceId);
