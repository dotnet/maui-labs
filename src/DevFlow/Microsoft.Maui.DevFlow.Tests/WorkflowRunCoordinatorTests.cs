using System.Collections.Concurrent;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public class WorkflowRunCoordinatorTests
{
    [Fact]
    public async Task Start_PassingAndFailingRuns_PreservesTerminalReportsAndFirstDivergence()
    {
        var leases = new RecordingLeaseRegistry();
        var coordinator = TestCoordinator(
            leases,
            (execution, _) => Task.FromResult(
                execution.Flow.Name == "pass"
                    ? PassingReport(execution.Flow)
                    : FailingReport(execution.Flow)));

        var passed = coordinator.Start(Request("pass", "pass-key"), Target(), static () => true);
        var passedSnapshot = await WaitForTerminalAsync(coordinator, passed);
        var failed = coordinator.Start(Request("fail", "fail-key"), Target(), static () => true);
        var failedSnapshot = await WaitForTerminalAsync(coordinator, failed);

        Assert.True(passed.Ok);
        Assert.Equal("passed", passedSnapshot.State);
        Assert.True(passedSnapshot.Terminal);
        Assert.NotNull(passedSnapshot.Report);
        Assert.Equal("passed", passedSnapshot.Report!.Outcome!.Status);

        Assert.True(failed.Ok);
        Assert.Equal("failed", failedSnapshot.State);
        Assert.Equal(1, failedSnapshot.FirstDivergence);
        Assert.NotNull(failedSnapshot.Report);
        Assert.Equal("1", failedSnapshot.Report!.DivergenceStepId);
        Assert.Equal(MauiFlowFailureClasses.AssertionFailed, failedSnapshot.Report.Failure!.Class);
    }

    [Fact]
    public void Start_InvalidFlow_RejectsBeforeAcquiringLease()
    {
        var leases = new RecordingLeaseRegistry();
        var coordinator = TestCoordinator(
            leases,
            static (_, _) => Task.FromResult(PassingReport()));
        var invalid = new MauiFlow
        {
            Name = "invalid",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.SetProperty,
                    Args = new FlowStepArgs
                    {
                        Selector = new FlowSelector { AutomationId = "label" },
                        Value = "hello"
                    }
                }
            }
        };

        var result = coordinator.Start(
            new WorkflowRunStartRequest
            {
                AgentId = "agent",
                AgentInstanceId = "instance",
                IdempotencyKey = "invalid-key",
                Flow = invalid
            },
            Target(),
            static () => true);

        Assert.False(result.Ok);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains(result.Errors!, error => error.Contains("setProperty requires a property name", StringComparison.Ordinal));
        Assert.Empty(leases.Actions);
    }

    [Fact]
    public async Task Start_WithInspectorHandoff_TransfersTransactionBeforeExecutionIsScheduled()
    {
        var leases = new RecordingLeaseRegistry();
        var releaseExecution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = TestCoordinator(
            leases,
            async (_, cancellationToken) =>
            {
                await releaseExecution.Task.WaitAsync(cancellationToken);
                return PassingReport();
            });

        var started = coordinator.Start(
            Request("handoff", "handoff-key"),
            Target(),
            static () => true,
            leaseHandoff: new WorkflowRunLeaseHandoff("inspector-lease", "web", "Browser Inspector"));

        Assert.True(started.Ok);
        Assert.Equal("transfer", Assert.Single(leases.Actions));

        releaseExecution.SetResult();
        await WaitForTerminalAsync(coordinator, started);
        Assert.DoesNotContain("claim", leases.Actions);
        Assert.DoesNotContain("begin", leases.Actions);
    }

    [Fact]
    public void Preflight_ValidatesAdmissionAndPlanBindingWithoutAcquiringLeaseOrStartingRun()
    {
        var leases = new RecordingLeaseRegistry();
        var executed = false;
        var coordinator = TestCoordinator(
            leases,
            (_, _) =>
            {
                executed = true;
                return Task.FromResult(PassingReport());
            });

        var admitted = coordinator.Preflight(Request("preflight", "preflight-key"), Target(), static () => true);

        Assert.True(admitted.Ok);
        Assert.NotNull(admitted.Admission);
        Assert.Empty(leases.Actions);
        Assert.False(executed);

        var stale = Request("stale-plan", "stale-plan-key");
        stale.Plan = ValidPlan(stale, flowDigest: new string('0', 64));
        stale.Context = MatchingContext();
        var rejected = coordinator.Preflight(stale, Target(), static () => true);

        Assert.False(rejected.Ok);
        Assert.Equal(409, rejected.StatusCode);
        Assert.Contains("stale flow digest", rejected.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(leases.Actions);
        Assert.False(executed);
    }

    [Fact]
    public void Preflight_PlanRequirements_RejectsMismatchedPlatformMissingCapabilityAndUnsupportedSemantic()
    {
        var leases = new RecordingLeaseRegistry();
        var executed = false;
        var coordinator = TestCoordinator(
            leases,
            (_, _) =>
            {
                executed = true;
                return Task.FromResult(PassingReport());
            });
        var request = Request("requirements", "requirements-key");
        request.Plan = ValidPlan(
            request,
            requiredPlatforms: ["windows"],
            requirements: new MauiFlowRequirements
            {
                RequiredCapabilities =
                [
                    new MauiCapabilityRequirement { Name = "logs", Required = true },
                ],
                RequiredSemantics =
                [
                    new MauiRequiredSemantic { Name = "future.checkpoint.v9", Required = true },
                ],
            });
        request.Context = MatchingContext();
        request.AvailableCapabilities = new MauiFlowCapabilitySet
        {
            Capabilities =
            [
                new MauiFlowCapability { Name = "ui", Version = 1 },
            ],
        };

        var rejected = coordinator.Preflight(
            request,
            new WorkflowRunTarget("agent", "instance", 12345, "android", "Test app"),
            static () => true);

        Assert.False(rejected.Ok);
        Assert.Equal(409, rejected.StatusCode);
        Assert.Contains(rejected.Errors!, error => error.Contains("requiredPlatforms", StringComparison.Ordinal));
        Assert.Contains(rejected.Errors!, error => error.Contains("[capability-missing]", StringComparison.Ordinal));
        Assert.Contains(rejected.Errors!, error => error.Contains("[required-semantics-unsupported]", StringComparison.Ordinal));
        Assert.Empty(leases.Actions);
        Assert.False(executed);
    }

    [Fact]
    public void Preflight_PlanRequirements_AllowsMatchingPlatformCapabilityAndSemantic()
    {
        var leases = new RecordingLeaseRegistry();
        var executed = false;
        var coordinator = TestCoordinator(
            leases,
            (_, _) =>
            {
                executed = true;
                return Task.FromResult(PassingReport());
            });
        var request = Request("requirements-ok", "requirements-ok-key");
        request.Plan = ValidPlan(
            request,
            requiredPlatforms: ["windows"],
            requirements: new MauiFlowRequirements
            {
                RequiredCapabilities =
                [
                    new MauiCapabilityRequirement { Name = "logs", Required = true },
                ],
                RequiredSemantics =
                [
                    new MauiRequiredSemantic { Name = "canonical-run", Required = true },
                ],
            });
        request.Context = MatchingContext();
        request.AvailableCapabilities = new MauiFlowCapabilitySet
        {
            Capabilities =
            [
                new MauiFlowCapability { Name = "logs", Version = 1 },
            ],
            Semantics =
            [
                new MauiSupportedSemantic { Name = "canonical-run", Version = 1 },
            ],
        };

        var admitted = coordinator.Preflight(
            request,
            new WorkflowRunTarget("agent", "instance", 12345, "windows", "Test app"),
            static () => true);

        Assert.True(admitted.Ok, admitted.Error);
        Assert.Empty(admitted.Errors ?? Array.Empty<string>());
        Assert.Empty(leases.Actions);
        Assert.False(executed);
    }

    [Fact]
    public async Task Run_ProgressCallback_UpdatesCurrentStepAndBoundedCountsBeforeTerminalReport()
    {
        var progressRaised = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = TestCoordinator(
            new RecordingLeaseRegistry(),
            async (execution, cancellationToken) =>
            {
                execution.Options.Progress?.Invoke(new MauiFlowRunProgress
                {
                    RunId = execution.RunId,
                    StepId = "1",
                    Sequence = 1,
                    CompletedSteps = 0,
                    TotalSteps = execution.Flow.Steps.Count,
                    Phase = "step-started",
                });
                progressRaised.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                execution.Options.Progress?.Invoke(new MauiFlowRunProgress
                {
                    RunId = execution.RunId,
                    StepId = "1",
                    Sequence = 1,
                    CompletedSteps = 1,
                    TotalSteps = execution.Flow.Steps.Count,
                    Phase = "step-completed",
                });
                return PassingReport(execution.Flow);
            });

        var started = coordinator.Start(Request("progress", "progress-key"), Target(), static () => true);
        await progressRaised.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var active = coordinator.GetStatus(started.Run!.RunId, started.CapabilityToken).Run;

        Assert.NotNull(active);
        Assert.Equal(1, active!.TotalSteps);
        Assert.Equal(0, active.CompletedSteps);
        Assert.Equal("1", active.CurrentStepId);
        Assert.Contains(active.Events, item => item.Kind == "step-started");

        release.TrySetResult();
        var terminal = await WaitForTerminalAsync(coordinator, started);
        Assert.Equal(1, terminal.CompletedSteps);
        Assert.Equal("1", terminal.CurrentStepId);
    }

    [Fact]
    public void Start_SideEffectAdmissionDeniedBeforeLeaseOrRunnerInvocation()
    {
        var leases = new RecordingLeaseRegistry();
        var executed = false;
        var coordinator = TestCoordinator(
            leases,
            (_, _) =>
            {
                executed = true;
                return Task.FromResult(PassingReport());
            });
        var request = Request("unsafe", "unsafe-key");
        request.Plan = ValidPlan(
            request,
            sideEffectPolicy: MauiFlowSideEffectPolicies.TestTenantResettable,
            checkpoint: new MauiFlowCheckpointRequirements
            {
                AppBuildFingerprint = "build-1",
                Route = "/home",
            });
        request.Context = new MauiFlowRunContext
        {
            Preconditions = new MauiFlowReplayPreconditions
            {
                Expected = new MauiFlowCheckpoint
                {
                    AppBuildFingerprint = "build-1",
                    Route = "/home",
                },
                Observed = new MauiFlowCheckpoint
                {
                    AppBuildFingerprint = "build-1",
                    Route = "/home",
                },
            },
        };

        var result = coordinator.Start(request, Target(), static () => true);

        Assert.False(result.Ok);
        Assert.Equal(409, result.StatusCode);
        Assert.NotNull(result.Admission);
        Assert.False(result.Admission!.OrdinaryReplayAllowed);
        Assert.Contains(result.Admission.Reasons, reason => reason.Code == "reset-proof-required");
        Assert.False(executed);
        Assert.Empty(leases.Actions);
    }

    [Fact]
    public async Task Start_ActiveRunForTarget_RejectsSecondMutatingRun()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leases = new RecordingLeaseRegistry();
        var coordinator = TestCoordinator(
            leases,
            async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return PassingReport();
            });

        var first = coordinator.Start(Request("first", "first-key"), Target(), static () => true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = coordinator.Start(Request("second", "second-key"), Target(), static () => true);

        Assert.True(first.Ok);
        Assert.False(second.Ok);
        Assert.Equal(409, second.StatusCode);

        var cancelled = coordinator.Cancel(first.Run!.RunId, first.CapabilityToken);
        Assert.True(cancelled.Ok);
        Assert.Equal("cancelled", (await WaitForTerminalAsync(coordinator, first)).State);
    }

    [Fact]
    public async Task Start_SameIdempotencyKeyAndDigest_ReturnsOriginalRun_AndDifferentDigestConflicts()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = TestCoordinator(
            new RecordingLeaseRegistry(),
            async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return PassingReport();
            });
        var request = Request("first", "shared-key");

        var original = coordinator.Start(request, Target(), static () => true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = coordinator.Start(Request("first", "shared-key"), Target(), static () => true);
        var conflict = coordinator.Start(Request("different", "shared-key"), Target(), static () => true);

        Assert.True(original.Ok);
        Assert.True(duplicate.Ok);
        Assert.True(duplicate.Existing);
        Assert.Equal(original.Run!.RunId, duplicate.Run!.RunId);
        Assert.Equal(original.CapabilityToken, duplicate.CapabilityToken);
        Assert.False(conflict.Ok);
        Assert.Equal(409, conflict.StatusCode);

        coordinator.Cancel(original.Run.RunId, original.CapabilityToken);
        await WaitForTerminalAsync(coordinator, original);
    }

    [Fact]
    public async Task Cancel_RunningRun_TerminatesAsCancelled()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = TestCoordinator(
            new RecordingLeaseRegistry(),
            async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return PassingReport();
            });

        var start = coordinator.Start(Request("cancel", "cancel-key"), Target(), static () => true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cancel = coordinator.Cancel(start.Run!.RunId, start.CapabilityToken);
        var terminal = await WaitForTerminalAsync(coordinator, start);

        Assert.True(cancel.Ok);
        Assert.True(cancel.Accepted);
        Assert.Equal("cancelled", terminal.State);
        Assert.Equal(MauiFlowFailureClasses.Cancelled, terminal.Report!.Failure!.Class);
    }

    [Fact]
    public async Task Start_TimedOutRun_TerminatesAsTimedOut()
    {
        var coordinator = TestCoordinator(
            new RecordingLeaseRegistry(),
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return PassingReport();
            },
            new WorkflowRunCoordinatorOptions
            {
                DefaultTimeout = TimeSpan.FromMilliseconds(25),
                MaximumTimeout = TimeSpan.FromSeconds(1),
                HeartbeatInterval = TimeSpan.FromMilliseconds(5)
            });

        var start = coordinator.Start(Request("timeout", "timeout-key", timeoutMs: 25), Target(), static () => true);
        var terminal = await WaitForTerminalAsync(coordinator, start);

        Assert.Equal("timed-out", terminal.State);
        Assert.Equal(MauiFlowFailureClasses.Timeout, terminal.Report!.Failure!.Class);
    }

    [Fact]
    public async Task Start_TinyDeadlineCapsALongerRequestedTimeout()
    {
        var coordinator = TestCoordinator(
            new RecordingLeaseRegistry(),
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return PassingReport();
            },
            new WorkflowRunCoordinatorOptions
            {
                DefaultTimeout = TimeSpan.FromMilliseconds(10),
                MaximumTimeout = TimeSpan.FromSeconds(1),
                HeartbeatInterval = TimeSpan.FromMilliseconds(5)
            });

        var start = coordinator.Start(
            Request("deadline", "deadline-key", timeoutMs: 500, deadlineMs: 25),
            Target(),
            static () => true);
        var terminal = await WaitForTerminalAsync(coordinator, start);

        Assert.Equal("timed-out", terminal.State);
        Assert.Equal(MauiFlowFailureClasses.Timeout, terminal.Report!.Failure!.Class);
    }

    [Fact]
    public async Task Start_DeviceRecordingTimeoutCoversTheAdmittedRun()
    {
        var observedTimeout = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = TestCoordinator(
            new RecordingLeaseRegistry(),
            (execution, _) =>
            {
                observedTimeout.TrySetResult(execution.Options.DeviceRecordingTimeoutSeconds);
                return Task.FromResult(PassingReport());
            });

        var start = coordinator.Start(
            Request("recording-timeout", "recording-timeout-key", timeoutMs: 240_000),
            Target(),
            static () => true,
            new WorkflowRunExecutionOptions { RecordDeviceRun = true });
        await WaitForTerminalAsync(coordinator, start);

        Assert.Equal(270, await observedTimeout.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Run_DeviceRecordingIsRetainedBesideTheBrokerOwnedReport()
    {
        var root = Path.Combine(Path.GetTempPath(), $"devflow-run-recording-{Guid.NewGuid():N}");
        var source = Path.Combine(Path.GetTempPath(), $"devflow-source-recording-{Guid.NewGuid():N}.mp4");
        var bytes = new byte[] { 0, 0, 0, 24, 102, 116, 121, 112, 109, 112, 52, 50 };
        await File.WriteAllBytesAsync(source, bytes);
        string? callbackPath = null;
        try
        {
            var coordinator = TestCoordinator(
                new RecordingLeaseRegistry(),
                (execution, _) =>
                {
                    execution.Options.DeviceRecordingCaptured?.Invoke(execution.RunId, source);
                    return Task.FromResult(PassingReport(execution.Flow));
                },
                new WorkflowRunCoordinatorOptions { ArtifactRoot = root });

            var start = coordinator.Start(
                Request("recording-artifact", "recording-artifact-key"),
                Target(),
                static () => true,
                new WorkflowRunExecutionOptions
                {
                    RecordDeviceRun = true,
                    DeviceRecordingCaptured = (_, path) => callbackPath = path,
                });
            var terminal = await WaitForTerminalAsync(coordinator, start);

            var retained = Path.Combine(
                root,
                terminal.RunId,
                "artifacts",
                "device-recording.mp4");
            Assert.Equal(retained, callbackPath);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(retained));
        }
        finally
        {
            File.Delete(source);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Retention_PrunesBrokerOwnedRunArtifactDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"devflow-run-retention-{Guid.NewGuid():N}");
        try
        {
            var coordinator = TestCoordinator(
                new RecordingLeaseRegistry(),
                static (execution, _) => Task.FromResult(PassingReport(execution.Flow)),
                new WorkflowRunCoordinatorOptions
                {
                    ArtifactRoot = root,
                    MaxRetainedTerminalRuns = 1,
                });

            var first = coordinator.Start(Request("first-artifact", "first-artifact-key"), Target(), static () => true);
            var firstTerminal = await WaitForTerminalAsync(coordinator, first);
            var second = coordinator.Start(Request("second-artifact", "second-artifact-key"), Target(), static () => true);
            var secondTerminal = await WaitForTerminalAsync(coordinator, second);

            Assert.False(Directory.Exists(Path.Combine(root, firstTerminal.RunId)));
            Assert.True(Directory.Exists(Path.Combine(root, secondTerminal.RunId)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Run_LeaseTransactionHeartbeatsAndAlwaysCleansUpAfterRunnerException()
    {
        var passingLeases = new RecordingLeaseRegistry();
        var passingCoordinator = TestCoordinator(
            passingLeases,
            async (_, cancellationToken) =>
            {
                await Task.Delay(35, cancellationToken);
                return PassingReport();
            },
            new WorkflowRunCoordinatorOptions
            {
                HeartbeatInterval = TimeSpan.FromMilliseconds(5)
            });

        var passing = passingCoordinator.Start(Request("lease", "lease-key"), Target(), static () => true);
        Assert.Equal("passed", (await WaitForTerminalAsync(passingCoordinator, passing)).State);
        Assert.Contains("claim", passingLeases.Actions);
        Assert.Contains("begin", passingLeases.Actions);
        Assert.Contains("heartbeat", passingLeases.Actions);
        Assert.Contains("end", passingLeases.Actions);
        Assert.Contains("release", passingLeases.Actions);

        var failingLeases = new RecordingLeaseRegistry();
        var failingCoordinator = TestCoordinator(
            failingLeases,
            static (_, _) => throw new InvalidOperationException("runner exploded"));

        var failed = failingCoordinator.Start(Request("exception", "exception-key"), Target(), static () => true);
        var failedTerminal = await WaitForTerminalAsync(failingCoordinator, failed);

        Assert.Equal("infrastructure-error", failedTerminal.State);
        Assert.Contains("end", failingLeases.Actions);
        Assert.Contains("release", failingLeases.Actions);
    }

    [Fact]
    public async Task Start_StaleInstanceIsRejected_AndReconnectOrphansActiveRun()
    {
        var staleCoordinator = TestCoordinator(
            new RecordingLeaseRegistry(),
            static (_, _) => Task.FromResult(PassingReport()));

        var stale = staleCoordinator.Start(Request("stale", "stale-key"), Target(), static () => false);
        Assert.False(stale.Ok);
        Assert.Equal(409, stale.StatusCode);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = TestCoordinator(
            new RecordingLeaseRegistry(),
            async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return PassingReport();
            });
        var start = coordinator.Start(Request("reconnect", "reconnect-key"), Target(), static () => true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        coordinator.MarkAgentInstanceUnavailable("agent", "instance", "Agent reconnected.");
        var terminal = await WaitForTerminalAsync(coordinator, start);

        Assert.Equal("orphaned", terminal.State);
        Assert.Equal(MauiFlowFailureClasses.AgentDisconnected, terminal.Report!.Failure!.Class);
    }

    [Fact]
    public async Task Run_AgentLedgerBeginsEndsAndAbandonsOnRunnerException()
    {
        var controls = new RecordingLedgerController();
        var coordinator = TestCoordinator(
            new RecordingLeaseRegistry(),
            static (_, _) => Task.FromResult(PassingReport()),
            controlLedger: controls.ControlAsync);

        var passed = coordinator.Start(Request("ledger-pass", "ledger-pass-key"), Target(), static () => true);
        Assert.Equal("passed", (await WaitForTerminalAsync(coordinator, passed)).State);
        Assert.Equal(new[] { "begin", "end" }, controls.Actions.ToArray());

        var failingControls = new RecordingLedgerController();
        var failingCoordinator = TestCoordinator(
            new RecordingLeaseRegistry(),
            static (_, _) => throw new InvalidOperationException("runner exploded"),
            controlLedger: failingControls.ControlAsync);
        var failed = failingCoordinator.Start(Request("ledger-fail", "ledger-fail-key"), Target(), static () => true);

        Assert.Equal("infrastructure-error", (await WaitForTerminalAsync(failingCoordinator, failed)).State);
        Assert.Equal(new[] { "begin", "abandon" }, failingControls.Actions.ToArray());

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellingControls = new RecordingLedgerController();
        var cancellingCoordinator = TestCoordinator(
            new RecordingLeaseRegistry(),
            async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return PassingReport();
            },
            controlLedger: cancellingControls.ControlAsync);
        var cancelled = cancellingCoordinator.Start(Request("ledger-cancel", "ledger-cancel-key"), Target(), static () => true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellingCoordinator.Cancel(cancelled.Run!.RunId, cancelled.CapabilityToken);

        Assert.Equal("cancelled", (await WaitForTerminalAsync(cancellingCoordinator, cancelled)).State);
        Assert.Equal(new[] { "begin", "abandon" }, cancellingControls.Actions.ToArray());
    }

    [Fact]
    public async Task Run_UnknownLedgerCompletion_MapsToExplicitTerminalFailure()
    {
        var controls = new RecordingLedgerController
        {
            ResultFactory = control => control.Action == "end"
                ? WorkflowRunLedgerControlResult.Success()
                : WorkflowRunLedgerControlResult.Success()
        };
        var coordinator = TestCoordinator(
            new RecordingLeaseRegistry(),
            static (execution, _) => Task.FromResult(new FlowReplayReport
            {
                Ok = false,
                Name = execution.Flow.Name,
                Total = 1,
                Failed = 1,
                DivergencePoint = 1,
                Results =
                {
                    new FlowStepResult
                    {
                        Seq = 1,
                        Action = FlowActions.Tap,
                        Label = "Tap",
                        Ok = false,
                        FailureKind = FlowFailureKinds.UnknownCompletion,
                        Error = "The command receipt was not returned."
                    }
                }
            }),
            controlLedger: controls.ControlAsync);

        var start = coordinator.Start(Request("unknown", "unknown-key"), Target(), static () => true);
        var terminal = await WaitForTerminalAsync(coordinator, start);

        Assert.Equal("unknown-completion", terminal.State);
        Assert.Equal(MauiFlowFailureClasses.UnknownCompletion, terminal.Report!.Failure!.Class);
        Assert.Equal(new[] { "begin", "abandon" }, controls.Actions.ToArray());
    }

    [Fact]
    public async Task Run_LedgerControlConflict_MapsToExplicitFailure()
    {
        var controls = new RecordingLedgerController
        {
            ResultFactory = control => control.Action == "begin"
                ? WorkflowRunLedgerControlResult.Failure("workflow-command-conflict", "Command sequence conflict.")
                : WorkflowRunLedgerControlResult.Success()
        };
        var coordinator = TestCoordinator(
            new RecordingLeaseRegistry(),
            static (_, _) => Task.FromResult(PassingReport()),
            controlLedger: controls.ControlAsync);

        var start = coordinator.Start(Request("conflict", "conflict-key"), Target(), static () => true);
        var terminal = await WaitForTerminalAsync(coordinator, start);

        Assert.Equal("failed", terminal.State);
        Assert.Equal(MauiFlowFailureClasses.WorkflowCommandConflict, terminal.Report!.Failure!.Class);
        Assert.Equal(new[] { "begin", "abandon" }, controls.Actions.ToArray());
    }

    [Fact]
    public async Task Retention_EvictsOldTerminalRunsDeterministicallyAndNeverActiveRuns()
    {
        var coordinator = TestCoordinator(
            new RecordingLeaseRegistry(),
            static (execution, _) => Task.FromResult(PassingReport(execution.Flow)),
            new WorkflowRunCoordinatorOptions
            {
                MaxRetainedTerminalRuns = 2
            });

        var first = coordinator.Start(Request("one", "one-key"), Target(), static () => true);
        await WaitForTerminalAsync(coordinator, first);
        var second = coordinator.Start(Request("two", "two-key"), Target(), static () => true);
        await WaitForTerminalAsync(coordinator, second);
        var third = coordinator.Start(Request("three", "three-key"), Target(), static () => true);
        await WaitForTerminalAsync(coordinator, third);

        Assert.Equal(404, coordinator.GetStatus(first.Run!.RunId, first.CapabilityToken).StatusCode);
        Assert.Equal(200, coordinator.GetStatus(second.Run!.RunId, second.CapabilityToken).StatusCode);
        Assert.Equal(200, coordinator.GetStatus(third.Run!.RunId, third.CapabilityToken).StatusCode);

        var activeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeCoordinator = TestCoordinator(
            new RecordingLeaseRegistry(),
            async (execution, cancellationToken) =>
            {
                if (execution.Flow.Name == "active")
                {
                    activeEntered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                return PassingReport(execution.Flow);
            },
            new WorkflowRunCoordinatorOptions
            {
                MaxRetainedTerminalRuns = 1
            });
        var active = activeCoordinator.Start(Request("active", "active-key"), Target(), static () => true);
        await activeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var completed = activeCoordinator.Start(
            Request("completed", "completed-key", agentId: "other-agent", instanceId: "other-instance"),
            Target("other-agent", "other-instance"),
            static () => true);
        await WaitForTerminalAsync(activeCoordinator, completed);

        Assert.Equal(200, activeCoordinator.GetStatus(active.Run!.RunId, active.CapabilityToken).StatusCode);
        Assert.Equal(200, activeCoordinator.GetStatus(completed.Run!.RunId, completed.CapabilityToken).StatusCode);

        activeCoordinator.Cancel(active.Run.RunId, active.CapabilityToken);
        await WaitForTerminalAsync(activeCoordinator, active);
        Assert.Equal(404, activeCoordinator.GetStatus(completed.Run!.RunId, completed.CapabilityToken).StatusCode);
    }

    [Fact]
    public async Task TerminalRun_WritesAtomicStructuredReportWhenArtifactRootConfigured()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory,
            "workflow-run-artifacts",
            Guid.NewGuid().ToString("N"));
        try
        {
            var coordinator = TestCoordinator(
                new RecordingLeaseRegistry(),
                static (execution, _) => Task.FromResult(FailingReport(execution.Flow)),
                new WorkflowRunCoordinatorOptions { ArtifactRoot = root });

            var start = coordinator.Start(Request("artifact", "artifact-key"), Target(), static () => true);
            var terminal = await WaitForTerminalAsync(coordinator, start);

            Assert.NotNull(terminal.ReportPath);
            Assert.NotNull(terminal.ReportDigest);
            Assert.True(File.Exists(terminal.ReportPath));
            Assert.Equal(terminal.ReportPath, terminal.Report!.ReportPath);
            Assert.Equal(terminal.ReportDigest, terminal.Report.ReportDigest);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(terminal.ReportPath!)!, "*.tmp"));
            var json = await File.ReadAllTextAsync(terminal.ReportPath!);
            Assert.Contains("\"runId\"", json, StringComparison.Ordinal);
            Assert.Contains("\"failure\"", json, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task LocalReproductionFacts_DoNotBackfillUnobservedCallerExpectations()
    {
        var request = Request("reproduction", "reproduction-key");
        var flowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(request.Flow!);
        request.ReproductionExpectation = new MauiLocalReproductionExpectation
        {
            FlowDigest = flowDigest,
            AppBuildFingerprint = "build-current",
            AppSourceFingerprint = "source-current",
            PackageDigest = "package-current",
            Platform = "test",
            DeviceProfile = "test-device",
        };
        var coordinator = TestCoordinator(
            new RecordingLeaseRegistry(),
            static (execution, _) => Task.FromResult(new FlowReplayReport
            {
                Ok = false,
                Name = execution.Flow.Name,
                Total = 1,
                Failed = 1,
                DivergencePoint = 1,
                StructuredReport = new MauiFlowRunReport
                {
                    FlowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(execution.Flow),
                    Target = new MauiFlowRunTarget
                    {
                        AppBuildFingerprint = "build-current",
                    },
                    Failure = new MauiFlowFailure
                    {
                        Code = MauiFlowFailureClasses.LocatorNotFound,
                        Class = MauiFlowFailureClasses.LocatorNotFound,
                        StepId = "1",
                    },
                    Steps =
                    [
                        new MauiFlowStepAttempt
                        {
                            StepId = "1",
                            ExpectedCheckpoint = new MauiFlowCheckpoint
                            {
                                AppBuildFingerprint = "build-current",
                                Route = "/todos",
                            },
                            ObservedCheckpoint = new MauiFlowCheckpoint
                            {
                                AppBuildFingerprint = "build-current",
                                Route = "/todos",
                            },
                        },
                    ],
                },
            }));

        var start = coordinator.Start(
            request,
            Target(),
            static () => true,
            new WorkflowRunExecutionOptions
            {
                ReproductionExpectation = request.ReproductionExpectation,
            });
        await WaitForTerminalAsync(coordinator, start);

        var local = coordinator.GetLocalReproductionFacts(start.Run!.RunId);

        Assert.True(local.Ok, local.Error);
        Assert.Equal(start.Run.RunId, local.Facts!.LocalRunId);
        Assert.True(local.Facts.IsNewLocalRun);
        Assert.Equal(flowDigest, local.Facts.FlowDigest);
        Assert.Null(local.Facts.AppSourceFingerprint);
        Assert.Null(local.Facts.PackageDigest);
        Assert.Null(local.Facts.DeviceProfile);
        Assert.Equal(MauiFlowFailureClasses.LocatorNotFound, local.Facts.Failure!.Code);
        Assert.Equal("/todos", local.Facts.Failure.ObservedCheckpoint!.Route);
    }

    /// <summary>
    /// Builds a coordinator for tests that exercise run mechanics rather than the dispatch
    /// authorization boundary. The authorizer is stated explicitly here so that a coordinator
    /// created without one keeps its production behaviour of refusing every start.
    /// </summary>
    [Fact]
    public async Task Start_LeaseBusyThenReleased_WaitsAndRunsInsteadOfFailing()
    {
        // The human approves in the Inspector, which holds a renewing writer lease, then closes it.
        var leases = new BusyThenFreeLeaseRegistry(refusals: 3);
        var coordinator = TestCoordinator(
            leases,
            static (execution, _) => Task.FromResult(PassingReport(execution.Flow)),
            new WorkflowRunCoordinatorOptions
            {
                LeaseAcquisitionTimeout = TimeSpan.FromSeconds(20),
                LeaseAcquisitionPollInterval = TimeSpan.FromMilliseconds(1),
            });

        var started = coordinator.Start(Request("pass", "busy-then-free"), Target(), static () => true);
        var snapshot = await WaitForTerminalAsync(coordinator, started);

        Assert.True(started.Ok);
        Assert.Equal("passed", snapshot.State);
        Assert.True(leases.ClaimAttempts > 1, "the coordinator should have retried the busy lease");
    }

    [Fact]
    public async Task Start_LeaseHeldThroughout_FailsClosedAndNamesTheHolder()
    {
        // A human genuinely driving the app keeps it: the wait must never force a takeover.
        var leases = new BusyThenFreeLeaseRegistry(refusals: int.MaxValue);
        var coordinator = TestCoordinator(
            leases,
            static (execution, _) => Task.FromResult(PassingReport(execution.Flow)),
            new WorkflowRunCoordinatorOptions
            {
                LeaseAcquisitionTimeout = TimeSpan.FromMilliseconds(20),
                LeaseAcquisitionPollInterval = TimeSpan.FromMilliseconds(1),
            });

        var started = coordinator.Start(Request("pass", "always-busy"), Target(), static () => true);
        var snapshot = await WaitForTerminalAsync(coordinator, started);

        Assert.Equal("failed", snapshot.State);
        Assert.Equal(MauiFlowFailureClasses.LeaseConflict, snapshot.Report!.Failure!.Class);
        Assert.Contains("VS Code Inspector", snapshot.Message, StringComparison.Ordinal);
        Assert.Contains("maui devflow approve", snapshot.Message, StringComparison.Ordinal);
    }
    private static WorkflowRunCoordinator TestCoordinator(
        IWorkflowMutationLeaseRegistry leases,
        Func<WorkflowRunExecution, CancellationToken, Task<FlowReplayReport>> execute,
        WorkflowRunCoordinatorOptions? options = null,
        TimeProvider? clock = null,
        Func<WorkflowRunLedgerControl, CancellationToken, Task<WorkflowRunLedgerControlResult>>? controlLedger = null,
        WorkflowRunDispatchAuthorizer? authorizeDispatch = null)
        => new(
            leases,
            execute,
            options,
            clock,
            controlLedger,
            authorizeDispatch ?? (static _ => WorkflowRunDispatchDecision.Allow("test-allow-all")));

    private static WorkflowRunStartRequest Request(
        string name,
        string idempotencyKey,
        int? timeoutMs = null,
        int? deadlineMs = null,
        string agentId = "agent",
        string instanceId = "instance") => new()
    {
        AgentId = agentId,
        AgentInstanceId = instanceId,
        IdempotencyKey = idempotencyKey,
        TimeoutMs = timeoutMs,
        DeadlineMs = deadlineMs,
        Flow = new MauiFlow
        {
            Name = name,
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Assert,
                    Asserts = new()
                    {
                        new FlowAssert
                        {
                            Kind = "exists",
                            Verify = false,
                            Selector = new FlowSelector { AutomationId = "label" }
                        }
                    }
                }
            }
        }
    };

    private static WorkflowRunTarget Target(string agentId = "agent", string instanceId = "instance")
        => new(agentId, instanceId, 12345, "test", "Test app");

    private static MauiTestPlan ValidPlan(
        WorkflowRunStartRequest request,
        string sideEffectPolicy = MauiFlowSideEffectPolicies.None,
        string? flowDigest = null,
        List<string>? requiredPlatforms = null,
        MauiFlowRequirements? requirements = null,
        MauiFlowCheckpointRequirements? checkpoint = null)
        => new()
        {
            PlanId = "plan-" + request.IdempotencyKey,
            Revision = 1,
            Flow = new MauiFlowReference
            {
                Path = (request.Flow?.Name ?? "flow") + ".md",
                Revision = 1,
                Digest = flowDigest ?? MauiFlowRunReportSerializer.ComputeFlowDigest(request.Flow!),
            },
            Title = "Workflow coordinator test",
            Goal = "Validate broker workflow coordination",
            Reset = new MauiTestResetRequirement { Required = false, Strategy = "host-owned" },
            Provenance = new MauiActorProvenance
            {
                ActorKind = "human",
                ActorId = "test",
                Channel = "unit-test",
                Provider = "xunit",
            },
            SideEffectPolicy = sideEffectPolicy,
            RequiredPlatforms = requiredPlatforms ?? [],
            Requirements = requirements,
            Checkpoint = checkpoint,
        };

    private static MauiFlowRunContext MatchingContext() => new()
    {
        Preconditions = new MauiFlowReplayPreconditions
        {
            Expected = new MauiFlowCheckpoint { Route = "/test" },
            Observed = new MauiFlowCheckpoint { Route = "/test" },
        },
    };

    private static async Task<WorkflowRunSnapshot> WaitForTerminalAsync(
        WorkflowRunCoordinator coordinator,
        WorkflowRunStartResult start)
    {
        Assert.True(start.Ok);
        Assert.NotNull(start.Run);
        Assert.NotNull(start.CapabilityToken);
        return await coordinator.WaitForTerminalAsync(
            start.Run!.RunId,
            start.CapabilityToken!,
            new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
    }

    private static FlowReplayReport PassingReport(MauiFlow? flow = null) => new()
    {
        Ok = true,
        Name = flow?.Name ?? "pass",
        Total = flow?.Steps.Count ?? 1,
        Passed = flow?.Steps.Count ?? 1
    };

    private static FlowReplayReport FailingReport(MauiFlow flow) => new()
    {
        Ok = false,
        Name = flow.Name,
        Total = flow.Steps.Count,
        Failed = 1,
        DivergencePoint = 1,
        StoppedEarly = true,
        Results =
        {
            new FlowStepResult
            {
                Seq = 1,
                Action = FlowActions.Assert,
                Label = "Assert",
                Ok = false,
                Error = "Expected element was absent.",
                FailureKind = FlowFailureKinds.Assertion
            }
        }
    };

    /// <summary>
    /// Refuses the claim a fixed number of times, then allows it. Models the Inspector holding a
    /// renewing writer lease that lapses shortly after the human finishes approving.
    /// </summary>
    private sealed class BusyThenFreeLeaseRegistry : IWorkflowMutationLeaseRegistry
    {
        private int _refusalsRemaining;

        public BusyThenFreeLeaseRegistry(int refusals) => _refusalsRemaining = refusals;

        public int ClaimAttempts { get; private set; }

        public MutationLeaseSnapshot Control(
            string agentId,
            string action,
            string? leaseId,
            string? holderKind,
            string? label,
            bool force,
            string? transactionId)
        {
            if (action != "claim")
            {
                return new MutationLeaseSnapshot(true, true, false, leaseId, transactionId, holderKind, label, 10_000)
                {
                    AuthorityEpoch = 1
                };
            }

            ClaimAttempts++;
            if (_refusalsRemaining-- > 0)
            {
                // Never forced: the holder keeps the lease until it lapses on its own.
                return new MutationLeaseSnapshot(
                    Allowed: false,
                    YouHold: false,
                    HeldByOther: true,
                    LeaseId: null,
                    TransactionId: null,
                    HolderKind: "vscode",
                    Label: "VS Code Inspector",
                    ExpiresInMs: 5_000);
            }

            return new MutationLeaseSnapshot(true, true, false, leaseId, transactionId, holderKind, label, 10_000)
            {
                AuthorityEpoch = 1
            };
        }


        public MutationLeaseSnapshot TryAdoptIdleLease(
            string agentId,
            string targetLeaseId,
            string transactionId,
            string? holderKind,
            string? label,
            IReadOnlyCollection<string> adoptableHolderKinds)
            => new(false, false, true, null, null, "vscode", "VS Code Inspector", 5_000);

        public MutationLeaseSnapshot TransferAndBegin(
            string agentId,
            string sourceLeaseId,
            string targetLeaseId,
            string transactionId,
            string? holderKind,
            string? label)
            => new(true, true, false, targetLeaseId, transactionId, holderKind, label, 10_000) { AuthorityEpoch = 1 };
    }
    private sealed class RecordingLeaseRegistry : IWorkflowMutationLeaseRegistry
    {
        private readonly ConcurrentQueue<string> _actions = new();

        public IReadOnlyCollection<string> Actions => _actions.ToArray();

        public MutationLeaseSnapshot Control(
            string agentId,
            string action,
            string? leaseId,
            string? holderKind,
            string? label,
            bool force,
            string? transactionId)
        {
            _actions.Enqueue(action);
            return new MutationLeaseSnapshot(
                Allowed: true,
                YouHold: true,
                HeldByOther: false,
                LeaseId: leaseId,
                TransactionId: transactionId,
                HolderKind: holderKind,
                Label: label,
                ExpiresInMs: 10_000)
            {
                AuthorityEpoch = 1
            };
        }


        public MutationLeaseSnapshot TryAdoptIdleLease(
            string agentId,
            string targetLeaseId,
            string transactionId,
            string? holderKind,
            string? label,
            IReadOnlyCollection<string> adoptableHolderKinds)
            => new(false, false, true, null, null, "vscode", "VS Code Inspector", 5_000);

        public MutationLeaseSnapshot TransferAndBegin(
            string agentId,
            string sourceLeaseId,
            string targetLeaseId,
            string transactionId,
            string? holderKind,
            string? label)
        {
            _actions.Enqueue("transfer");
            return new MutationLeaseSnapshot(
                Allowed: true,
                YouHold: true,
                HeldByOther: false,
                LeaseId: targetLeaseId,
                TransactionId: transactionId,
                HolderKind: holderKind,
                Label: label,
                ExpiresInMs: 10_000)
            {
                AuthorityEpoch = 2
            };
        }
    }

    private sealed class RecordingLedgerController
    {
        private readonly ConcurrentQueue<string> _actions = new();

        public IReadOnlyCollection<string> Actions => _actions.ToArray();
        public Func<WorkflowRunLedgerControl, WorkflowRunLedgerControlResult>? ResultFactory { get; init; }

        public Task<WorkflowRunLedgerControlResult> ControlAsync(
            WorkflowRunLedgerControl control,
            CancellationToken cancellationToken)
        {
            _actions.Enqueue(control.Action);
            return Task.FromResult(ResultFactory?.Invoke(control) ?? WorkflowRunLedgerControlResult.Success());
        }
    }
}
