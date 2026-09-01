using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// End-to-end replay tests: the real <see cref="AgentClient"/> against a stateful loopback fake
/// agent. Exercises selector resolution, drive, and hard-assertion verification.
/// </summary>
public class FlowReplayTests
{
    private static MauiFlow LoginFlow() => new()
    {
        Name = "login",
        Steps =
        {
            new FlowStep
            {
                Seq = 1, Action = "tap",
                Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "submit" } },
                Asserts = new() { new FlowAssert { Kind = "exists", Selector = new FlowSelector { AutomationId = "submit" }, Verify = true } },
            },
            new FlowStep
            {
                Seq = 2, Action = "fill", Value = "hello",
                Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "name" }, Text = "hello" },
                Asserts = new() { new FlowAssert { Kind = "propEquals", Selector = new FlowSelector { AutomationId = "name" }, Name = "Text", Expected = "hello", Verify = true } },
            },
        },
    };

    [Fact]
    public async Task Replay_EmptyFlow_ReturnsValidationFailureWithoutConnecting()
    {
        using var client = new AgentClient("127.0.0.1", 1);

        var report = await new FlowReplayer(client).ReplayAsync(new MauiFlow { Name = "empty" });

        Assert.False(report.Ok);
        Assert.Equal(0, report.Total);
        Assert.Equal(1, report.Failed);
        var failure = Assert.Single(report.Results);
        Assert.Equal(FlowFailureKinds.Validation, failure.FailureKind);
        Assert.Contains("at least one step", failure.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Replay_PassingFlow_ReportsAllPassed()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var report = await new FlowReplayer(client, pollTries: 2, pollGapMs: 10).ReplayAsync(LoginFlow());

        Assert.True(report.Ok, System.Text.Json.JsonSerializer.Serialize(report));
        Assert.Equal(2, report.Passed);
        Assert.Equal(0, report.Failed);
        Assert.Equal(new[] { "btn" }, agentSrv.Taps.ToArray());     // #submit resolved to id "btn"
        Assert.Equal("hello", agentSrv.TextOf("entry"));            // fill actually applied
        Assert.True(report.Results[0].Asserts[0].Ok);              // exists passed
        Assert.True(report.Results[1].Asserts[0].Ok);              // propEquals passed
    }

    [Fact]
    public async Task Replay_UnresolvableTarget_FailsStepAndSkipsAsserts()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Name = "bad",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1, Action = "tap",
                    Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "nope" } },
                    Asserts = new() { new FlowAssert { Kind = "exists", Selector = new FlowSelector { AutomationId = "nope" }, Verify = true } },
                },
            },
        };

        var report = await new FlowReplayer(client, pollTries: 1, pollGapMs: 0).ReplayAsync(flow);

        Assert.False(report.Ok);
        Assert.Equal(1, report.Failed);
        Assert.Contains("could not be resolved", report.Results[0].Error);
        Assert.True(report.Results[0].Asserts[0].Skipped);        // asserts not run after a failed drive
    }

    [Fact]
    public async Task Replay_PropEqualsMismatch_FailsStep()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Name = "mismatch",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1, Action = "fill",
                    Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "name" }, Text = "hello" },
                    Asserts = new() { new FlowAssert { Kind = "propEquals", Selector = new FlowSelector { AutomationId = "name" }, Name = "Text", Expected = "WRONG", Verify = true } },
                },
            },
        };

        var report = await new FlowReplayer(client, pollTries: 2, pollGapMs: 10).ReplayAsync(flow);

        Assert.False(report.Ok);
        Assert.False(report.Results[0].Asserts[0].Ok);
        Assert.Equal("hello", report.Results[0].Asserts[0].Actual);   // observed actual is reported
    }

    [Fact]
    public async Task Replay_AssertOnlyStep_RunsAssertionWithoutDriving()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Name = "assert-initial",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1, Action = "assert",
                    Asserts = new()
                    {
                        new FlowAssert { Kind = "exists", Selector = new FlowSelector { AutomationId = "submit" }, Verify = true },
                        new FlowAssert { Kind = "propEquals", Selector = new FlowSelector { AutomationId = "submit" }, Name = "Text", Expected = "Go", Verify = true },
                    },
                },
            },
        };

        var report = await new FlowReplayer(client, pollTries: 2, pollGapMs: 10).ReplayAsync(flow);

        Assert.True(report.Ok, JsonSerializer.Serialize(report));
        Assert.Empty(agentSrv.Taps);                       // an assert step drives nothing
        Assert.True(report.Results[0].Asserts[0].Ok);      // exists passed
        Assert.True(report.Results[0].Asserts[1].Ok);      // propEquals Text == "Go" passed
    }

    [Fact]
    public async Task Replay_UnevaluatableVerifiedAssertKind_FailsClosedAndNeverReportsGreen()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Name = "unevaluatable-hard-assert",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1, Action = "assert",
                    Asserts = new() { new FlowAssert { Kind = "contains", Selector = new FlowSelector { AutomationId = "submit" }, Expected = "Go", Verify = true } },
                },
            },
        };

        var report = await new FlowReplayer(client, pollTries: 2, pollGapMs: 10).ReplayAsync(flow);

        Assert.False(report.Ok, JsonSerializer.Serialize(report));
        Assert.Equal(0, report.Passed);
        Assert.Equal(FlowFailureKinds.Validation, report.Results[0].FailureKind);
        Assert.Empty(agentSrv.Taps);                       // fails closed before driving anything
    }

    [Fact]
    public async Task Replay_UnevaluatableReportOnlyAssertKind_IsSkippedAndStepPasses()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Name = "unevaluatable-soft-assert",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1, Action = "tap",
                    Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "submit" } },
                    Asserts = new() { new FlowAssert { Kind = "contains", Note = "observation only", Verify = false } },
                },
            },
        };

        var report = await new FlowReplayer(client, pollTries: 2, pollGapMs: 10).ReplayAsync(flow);

        Assert.True(report.Ok, JsonSerializer.Serialize(report));
        Assert.True(report.Results[0].Asserts[0].Skipped);
        Assert.Null(report.Results[0].Asserts[0].Ok);      // report-only kinds stay non-blocking
    }

    [Fact]
    public async Task Replay_AssertOnlyStep_FailsWhenExpectationWrong()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Name = "assert-wrong",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1, Action = "assert",
                    Asserts = new() { new FlowAssert { Kind = "propEquals", Selector = new FlowSelector { AutomationId = "submit" }, Name = "Text", Expected = "Nope", Verify = true } },
                },
            },
        };

        var report = await new FlowReplayer(client, pollTries: 2, pollGapMs: 10).ReplayAsync(flow);

        Assert.False(report.Ok);
        Assert.False(report.Results[0].Asserts[0].Ok);
    }

    [Fact]
    public async Task Replay_NotExists_PassesWhenTargetIsAbsent()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Name = "assert-absent",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = "assert",
                    Asserts = new()
                    {
                        new FlowAssert
                        {
                            Kind = "notExists",
                            Selector = new FlowSelector { AutomationId = "missing" },
                            Verify = true,
                        },
                    },
                },
            },
        };

        var report = await new FlowReplayer(client, pollTries: 1, pollGapMs: 0).ReplayAsync(flow);

        Assert.True(report.Ok, JsonSerializer.Serialize(report));
        Assert.True(report.Results[0].Asserts[0].Ok);
        Assert.Equal("0", report.Results[0].Asserts[0].Actual);
    }

    [Fact]
    public async Task Replay_TargetAppearingAfterPreviousAction_IsRetried()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Name = "async-target",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = "tap",
                    Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "submit" } },
                },
                new FlowStep
                {
                    Seq = 2,
                    Action = "tap",
                    Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "late" } },
                },
            },
        };

        var report = await new FlowReplayer(client, pollTries: 6, pollGapMs: 75).ReplayAsync(flow);

        Assert.True(report.Ok, JsonSerializer.Serialize(report));
        Assert.Equal(new[] { "btn", "late-btn" }, agentSrv.Taps);
    }

    [Fact]
    public async Task Replay_ScrollWithUnresolvableSelector_FailsInsteadOfScrollingRoot()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Name = "missing-scroll-target",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = "scroll",
                    Args = new FlowStepArgs
                    {
                        Selector = new FlowSelector { AutomationId = "missing" },
                        Dy = 100,
                    },
                },
            },
        };

        var report = await new FlowReplayer(client, pollTries: 2, pollGapMs: 10).ReplayAsync(flow);

        Assert.False(report.Ok);
        Assert.Contains("scroll target could not be resolved", report.Results[0].Error);
    }

    [Fact]
    public async Task Replay_AutomationIdMatchesMultipleElements_FailsAmbiguouslyWithoutDriving()
    {
        await using var agentSrv = new RoutingAgent();
        agentSrv.AddElement("duplicate", "submit", "Button", "Other");
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep { Seq = 1, Action = FlowActions.Tap, Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "submit" } } }
            }
        };

        var report = await new FlowReplayer(client, pollTries: 1, pollGapMs: 0).ReplayAsync(flow);

        Assert.False(report.Ok);
        Assert.Equal(FlowFailureKinds.Ambiguous, report.Results[0].FailureKind);
        Assert.Empty(agentSrv.Taps);
    }

    [Fact]
    public async Task Replay_TextSelector_RequiresExactUniqueText()
    {
        await using var agentSrv = new RoutingAgent();
        agentSrv.AddElement("partial", "other", "Button", "Go now");
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var exact = new MauiFlow
        {
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Tap,
                    Args = new FlowStepArgs { Selector = new FlowSelector { Text = "Go" } }
                }
            }
        };

        var report = await new FlowReplayer(client, pollTries: 1, pollGapMs: 0).ReplayAsync(exact);

        Assert.True(report.Ok, JsonSerializer.Serialize(report));
        Assert.Equal(["btn"], agentSrv.Taps);
    }

    [Fact]
    public async Task Replay_FailureStopsAtDivergence_AndInvokesEvidenceCallbackOnce()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var evidence = new CountingEvidenceCapture();
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep { Seq = 1, Action = FlowActions.Tap, Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "missing" } } },
                new FlowStep { Seq = 2, Action = FlowActions.Tap, Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "submit" } } }
            }
        };

        var report = await new FlowReplayer(client, pollTries: 1, pollGapMs: 0, evidenceCapture: evidence).ReplayAsync(flow);

        Assert.True(report.StoppedEarly);
        Assert.Equal(1, report.DivergencePoint);
        Assert.Single(report.Results);
        Assert.Equal(1, evidence.Count);
    }

    [Fact]
    public async Task Replay_CancellationFromFailureEvidenceIsNotSwallowed()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Tap,
                    Target = new FlowSelector { AutomationId = "missing" }
                }
            }
        };
        using var cts = new CancellationTokenSource();
        var evidence = new CancellingEvidenceCapture(cts);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FlowReplayer(
                client,
                pollTries: 1,
                pollGapMs: 0,
                evidenceCapture: evidence).ReplayAsync(flow, ct: cts.Token));
    }

    [Fact]
    public async Task Replay_RouteIsAssertion_UsesAgentStatusRoute()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Assert,
                    Asserts = new() { new FlowAssert { Kind = "routeIs", Expected = "/home", Verify = true } }
                }
            }
        };

        var report = await new FlowReplayer(client, pollTries: 1, pollGapMs: 0).ReplayAsync(flow);

        Assert.True(report.Ok);
        Assert.True(report.Results[0].Asserts[0].Ok);
    }

    [Fact]
    public async Task Replay_SecretBackedFill_ResolvesAtExecutionWithoutPersistingTheValue()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        const string variable = "MAUI_DEVFLOW_SECRET_PASSWORD_STEP_1";
        const string secret = "runtime-only-password";
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Fill,
                    Target = new FlowSelector { AutomationId = "name" },
                    Args = new FlowStepArgs { SecretEnvironmentVariable = variable }
                }
            }
        };

        var report = await new FlowReplayer(
            client,
            pollTries: 1,
            pollGapMs: 0,
            secretResolver: name => name == variable ? secret : null).ReplayAsync(flow);

        Assert.True(report.Ok, JsonSerializer.Serialize(report));
        Assert.Equal(secret, agentSrv.TextOf("entry"));
        Assert.DoesNotContain(secret, FlowMarkdown.Serialize(flow), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replay_MissingSecret_FailsBeforeDriving()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Fill,
                    Target = new FlowSelector { AutomationId = "name" },
                    Args = new FlowStepArgs
                    {
                        SecretEnvironmentVariable = "MAUI_DEVFLOW_SECRET_PASSWORD_STEP_1"
                    }
                }
            }
        };

        var report = await new FlowReplayer(
            client,
            pollTries: 1,
            pollGapMs: 0,
            secretResolver: _ => null).ReplayAsync(flow);

        Assert.False(report.Ok);
        Assert.Equal(FlowFailureKinds.SecretRequired, report.Results[0].FailureKind);
        Assert.Equal("", agentSrv.TextOf("entry"));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Replay_SetProperty_DoesNotRequireInteractionActionability(bool visible, bool enabled)
    {
        await using var agentSrv = new RoutingAgent();
        agentSrv.SetState("entry", visible, enabled);
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.SetProperty,
                    Target = new FlowSelector { AutomationId = "name" },
                    Value = "updated",
                    Args = new FlowStepArgs { Name = "Text", Value = "updated" }
                }
            }
        };

        var report = await new FlowReplayer(
            client,
            pollTries: 1,
            pollGapMs: 0).ReplayAsync(flow);

        Assert.True(report.Ok, JsonSerializer.Serialize(report));
        Assert.Equal("updated", agentSrv.TextOf("entry"));
    }

    [Fact]
    public async Task Replay_SafeTriggerValueSource_IsNotRejected()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.SetProperty,
                    Target = new FlowSelector { AutomationId = "name" },
                    Args = new FlowStepArgs
                    {
                        Name = "Text",
                        Value = "trigger-safe",
                        ValueSource = "trigger"
                    }
                }
            }
        };

        var report = await new FlowReplayer(
            client,
            pollTries: 1,
            pollGapMs: 0).ReplayAsync(flow);

        Assert.True(report.Ok, JsonSerializer.Serialize(report));
        Assert.Equal("trigger-safe", agentSrv.TextOf("entry"));
    }

    /// <summary>
    /// The cheap deterministic version of the whole product loop: record → replay → selector
    /// drift → classified failure → triage → repair proposal that is never applied. It runs
    /// against the loopback agent so it is fast and hermetic; the real-stack equivalent lives in
    /// <c>BrokerRepairValidationIntegrationTests</c>.
    /// </summary>
    [Fact]
    public async Task FullLoop_SelectorDrift_ClassifiesTriagesAndProposesRepairWithoutApplyingIt()
    {
        await using var agentSrv = new RoutingAgent();
        using var client = new AgentClient("127.0.0.1", agentSrv.Port);
        var capture = new CountingEvidenceCapture();
        var flow = new MauiFlow
        {
            Name = "checkout",
            App = "example-app",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    StepId = "submit-order",
                    Action = FlowActions.Tap,
                    Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "submit" } },
                    Asserts = new()
                    {
                        new FlowAssert
                        {
                            Kind = "exists",
                            Selector = new FlowSelector { AutomationId = "submit" },
                            Verify = true,
                        },
                    },
                },
            },
        };

        // 1. Record → replay. The recorded selector resolves and drives before any drift.
        var recorded = await new FlowReplayer(client, pollTries: 1, pollGapMs: 0, evidenceCapture: capture)
            .ReplayAsync(flow);
        Assert.True(recorded.Ok, JsonSerializer.Serialize(recorded));
        Assert.Equal(new[] { "btn" }, agentSrv.Taps.ToArray());
        Assert.Equal(0, capture.Count);

        // 2. Drift. The same control ships again under a renamed AutomationId.
        Assert.True(agentSrv.RemoveElement("btn"));
        agentSrv.AddElement("btn-v2", "submit-primary", "Button", "Go");

        // 3. Replay after drift: one classified locator failure and exactly one evidence capture.
        var drifted = await new FlowReplayer(client, pollTries: 1, pollGapMs: 0, evidenceCapture: capture)
            .ReplayAsync(flow);
        Assert.False(drifted.Ok);
        Assert.Equal(FlowFailureKinds.NotFound, drifted.Results[0].FailureKind);
        Assert.True(Assert.Single(drifted.Results[0].Asserts).Skipped);
        Assert.Equal(1, capture.Count);

        var run = drifted.StructuredReport;
        Assert.NotNull(run);
        Assert.Equal(MauiFlowFailureClasses.LocatorNotFound, run!.Failure?.Class);
        Assert.Equal("submit-order", run.Failure?.StepId);

        // The loopback agent has no checkpoint or fingerprint provider, so the two facts a real
        // host records are supplied here; everything else is what the replayer actually produced.
        var checkpoint = LoopCheckpoint();
        var fingerprint = LoopFingerprint();
        var failedAttempt = Assert.Single(run.Steps, step => step.StepId == "submit-order");
        failedAttempt.ExpectedCheckpoint = checkpoint;
        failedAttempt.ObservedCheckpoint = checkpoint;
        failedAttempt.Fingerprint = fingerprint;

        // 4. Triage the report the replayer actually produced. A plan-less legacy replay is
        // deliberately not repair-eligible on its own, so the decision a plan-carrying host
        // records is supplied alongside it.
        Assert.NotNull(run.ReplayEligibility);
        Assert.False(run.ReplayEligibility.RepairValidationAllowed);
        var replayEligibility = new MauiFlowReplayEligibilityDecision
        {
            SideEffectPolicy = MauiFlowSideEffectPolicies.None,
            OrdinaryReplayAllowed = true,
            DiagnosticReplayAllowed = true,
            RepairValidationAllowed = true,
            RepairEligibility = true,
            RunVerificationAllowed = true,
            DownstreamContinuationAllowed = true,
        };
        var triageInput = LoopTriageInput(flow, run, drifted.ReportDigest, checkpoint, fingerprint);
        triageInput.ReplayEligibility = replayEligibility;
        var triage = MauiFlowTriageAnalyzer.Analyze(triageInput);
        Assert.Equal(
            MauiFlowTriageEvidenceStates.Sufficient,
            triage.Evidence.State);
        Assert.Equal(MauiFlowFailureClasses.LocatorNotFound, triage.Classification.FailureClass);
        Assert.True(triage.RepairEligible, string.Join("; ", triage.RepairEligibilityCodes));
        Assert.False(triage.Retryable);
        Assert.Contains(MauiFlowTriageNextActions.ReviewSelectorRepair, triage.AllowedNextActions);

        // 5. Repair proposal, built from the live tree the agent exposes after the rename.
        var eligibility = MauiFlowRepairEligibilityEvaluator.Evaluate(new MauiFlowRepairEligibilityInput
        {
            Run = run,
            Plan = triageInput.Plan,
            ReplayEligibility = replayEligibility,
            ExpectedCheckpoint = checkpoint,
            CurrentCheckpoint = checkpoint,
            BeforeDispatch = true,
            IsCurrentLocalRun = true,
            PriorActiveSelectorResolution = triageInput.PriorActiveSelectorResolution,
            TargetFingerprint = fingerprint,
        });
        Assert.True(eligibility.Eligible, string.Join("; ", eligibility.Reasons.Select(r => r.Code)));

        var tree = await client.GetTreeAsync();
        var renamed = Assert.Single(tree, element =>
            element.Type == "Button" &&
            element.Text == "Go" &&
            element.AutomationId != "submit");
        var live = await client.QueryAsync(automationId: renamed.AutomationId);
        var candidate = LoopCandidate(renamed.AutomationId!, fingerprint);

        var generation = MauiFlowRepairProposalGenerator.Generate(new MauiFlowRepairProposalGenerationInput
        {
            Eligibility = eligibility,
            Plan = triageInput.Plan,
            Flow = flow,
            BaseFlow = new MauiFlowReference { Path = "checkout.md", Digest = run.FlowDigest },
            SourceRunId = run.RunId,
            SourceStepId = "submit-order",
            SourceFailureId = run.Failure?.FailureId,
            SourceFailureCode = MauiFlowFailureClasses.LocatorNotFound,
            PriorFingerprint = fingerprint,
            PriorActiveSelectorResolution = triageInput.PriorActiveSelectorResolution,
            SelectorHealthCandidates = [candidate],
            CurrentResolutions =
            [
                new MauiRepairCandidateResolution
                {
                    CandidateId = candidate.CandidateId,
                    MatchCount = live.Count,
                    SemanticFingerprintMatches = true,
                    CurrentFingerprint = fingerprint,
                    EvidenceRefs = ["live:tree"],
                },
            ],
            Trust = "current-local-run",
        });

        var proposal = Assert.Single(generation.Proposals);
        Assert.Equal("submit", proposal.OldSelector?.AutomationId);
        Assert.Equal("submit-primary", proposal.ProposedSelector?.AutomationId);
        Assert.Equal("submit-order", proposal.SourceStepId);
        Assert.True(proposal.Patch?.SelectorOnly);
        Assert.True(proposal.UnchangedAssertionsProof?.Unchanged);

        // 6. Nothing was applied: not to the flow, not to the app, and no extra evidence taken.
        Assert.Equal("submit", flow.Steps[0].Args?.Selector?.AutomationId);
        Assert.Equal("submit", flow.Steps[0].Asserts![0].Selector?.AutomationId);
        Assert.Equal(new[] { "btn" }, agentSrv.Taps.ToArray());

        // Replaying the untouched flow fails the same way, and evidence capture is per run: this
        // second capture proves the first one was not a one-shot latch, and that proposing a
        // repair did not quietly rewrite the flow the replayer reads.
        var stillBroken = await new FlowReplayer(client, pollTries: 1, pollGapMs: 0, evidenceCapture: capture)
            .ReplayAsync(flow);
        Assert.False(stillBroken.Ok);
        Assert.Equal(FlowFailureKinds.NotFound, stillBroken.Results[0].FailureKind);
        Assert.Equal(2, capture.Count);
    }

    private static MauiFlowTriageInput LoopTriageInput(
        MauiFlow flow,
        MauiFlowRunReport run,
        string? reportDigest,
        MauiFlowCheckpoint checkpoint,
        MauiElementFingerprint fingerprint)
    {
        var input = new MauiFlowTriageInput
        {
            Flow = flow,
            Report = run,
            Manifest = new MauiTestExecutionManifest
            {
                ManifestId = "manifest-" + run.RunId,
                RunId = run.RunId,
                FlowId = run.FlowId,
                FlowRevision = run.FlowRevision,
                FlowDigest = run.FlowDigest,
                Host = new MauiTestExecutionHostFacts
                {
                    Os = "loopback",
                    Architecture = "x64",
                    Runtime = "net10.0",
                    RunnerKind = "local",
                },
                Lifecycle = new MauiTestExecutionLifecycleFacts
                {
                    StartedAt = run.StartedAt,
                    EndedAt = run.EndedAt,
                    CleanupCompleted = true,
                },
                Outcome = new MauiTestExecutionOutcomeFacts
                {
                    Status = run.Outcome?.Status,
                    Terminal = run.Outcome?.Terminal,
                },
                Artifacts =
                [
                    new MauiTestExecutionArtifact
                    {
                        ArtifactId = "flow-run",
                        Kind = "flow-run-report",
                        Digest = reportDigest ?? run.ReportDigest,
                        RelativePath = "flow-run.json",
                        Redacted = true,
                    },
                ],
            },
            Plan = new MauiTestPlan
            {
                SideEffectPolicy = MauiFlowSideEffectPolicies.None,
                IndependentBusinessOracles =
                [
                    new MauiIndependentBusinessOracleDeclaration
                    {
                        OracleId = "order-submitted",
                        Required = true,
                        Independent = true,
                    },
                ],
            },
            ExpectedCheckpoint = checkpoint,
            CurrentCheckpoint = checkpoint,
            BeforeDispatch = true,
            IsCurrentLocalRun = true,
            PriorActiveSelectorResolution = new MauiRepairPriorSelectorResolution
            {
                RunId = run.RunId,
                TrustedRun = true,
                WasUniquelyResolved = true,
                ActiveSelector = new FlowSelector { AutomationId = "submit" },
                Fingerprint = fingerprint,
            },
            TargetFingerprint = fingerprint,
        };
        var fingerprints = MauiFlowIncidentFingerprint.Create(input.Flow, input.Report, input.Manifest);
        input.Manifest.TestIdentityFingerprint = fingerprints.TestIdentityFingerprint;
        input.Manifest.IncidentFingerprint = fingerprints.IncidentFingerprint;
        input.Manifest.OccurrenceFingerprint = fingerprints.OccurrenceFingerprint;
        return input;
    }

    private static MauiSelectorCandidate LoopCandidate(string automationId, MauiElementFingerprint fingerprint) => new()
    {
        CandidateId = "candidate-" + automationId,
        Rank = 1,
        Priority = 1,
        Selector = new FlowSelector { AutomationId = automationId },
        SelectorDescriptor = new MauiSelectorCandidateSelector
        {
            Kind = "automation-id",
            AutomationId = automationId,
        },
        Score = 0.92,
        Scores = new MauiSelectorCandidateScores { DeterministicRankScore = 0.92 },
        Unique = true,
        Validation = new MauiSelectorCandidateValidation
        {
            Unique = true,
            MatchCount = 1,
            Accepted = true,
            PlatformState = "validated",
        },
        CalibrationStatus = MauiSelectorHealthRules.Uncalibrated,
        Calibration = new MauiSelectorCandidateCalibration(),
        Fingerprint = fingerprint,
    };

    private static MauiFlowCheckpoint LoopCheckpoint() => new()
    {
        AppBuildFingerprint = "loopback-build",
        AgentInstanceId = "loopback-agent",
        SeedFingerprint = "loopback-seed",
        BackendStateFingerprint = "loopback-backend",
        Route = "/home",
        Window = "main",
        Modal = "none",
        Locale = "en-US",
        Theme = "light",
        Orientation = "portrait",
        DisplayProfile = "320x640@2",
        CollectionItemKey = "order-1",
    };

    private static MauiElementFingerprint LoopFingerprint() => new()
    {
        FingerprintId = "fp-loopback",
        Context = new MauiElementFingerprintContext
        {
            AppId = "example-app",
            AppBuild = "loopback-build",
            Platform = "loopback",
            Route = "/home",
            Window = "main",
            Modal = "none",
            Locale = "en-US",
            Theme = "light",
            Orientation = "portrait",
            DisplayProfile = "320x640@2",
        },
        Managed = new MauiManagedElementIdentity
        {
            Type = "Button",
            FullType = "Microsoft.Maui.Controls.Button",
            Role = "button",
            Traits = ["interactive"],
        },
        Topology = new MauiTopologySignature
        {
            AncestorHash = "ancestor",
            SiblingHash = "sibling",
        },
    };

    private sealed class CountingEvidenceCapture : IFlowReplayEvidenceCapture
    {
        public int Count { get; private set; }

        public Task CaptureOnFailureAsync(MauiFlow flow, FlowStep failedStep, FlowStepResult result, CancellationToken cancellationToken)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class CancellingEvidenceCapture(CancellationTokenSource cts) : IFlowReplayEvidenceCapture
    {
        public Task CaptureOnFailureAsync(
            MauiFlow flow,
            FlowStep failedStep,
            FlowStepResult result,
            CancellationToken cancellationToken)
        {
            cts.Cancel();
            throw new OperationCanceledException(cancellationToken);
        }
    }

    // ── Stateful fake agent ──────────────────────────────────────────────────────
    private sealed class Element
    {
        public required string Id;
        public required string AutomationId;
        public required string Type;
        public string Text = "";
        public bool Visible = true;
        public bool Enabled = true;
        public DateTimeOffset? VisibleAfter;
    }

    private sealed class RoutingAgent : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private readonly List<Element> _els = new()
        {
            new Element { Id = "btn", AutomationId = "submit", Type = "Button", Text = "Go" },
            new Element { Id = "entry", AutomationId = "name", Type = "Entry", Text = "" },
            new Element { Id = "late-btn", AutomationId = "late", Type = "Button", Text = "Later", VisibleAfter = DateTimeOffset.MaxValue },
        };

        public List<string> Taps { get; } = new();
        public string? TextOf(string id) => _els.FirstOrDefault(e => e.Id == id)?.Text;
        public void SetState(string id, bool visible, bool enabled)
        {
            var element = _els.Single(element => element.Id == id);
            element.Visible = visible;
            element.Enabled = enabled;
        }
        public void AddElement(string id, string automationId, string type, string text)
            => _els.Add(new Element { Id = id, AutomationId = automationId, Type = type, Text = text });
        public bool RemoveElement(string id) => _els.RemoveAll(element => element.Id == id) > 0;

        public RoutingAgent()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _loop = AcceptLoop(_cts.Token);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { break; }
                _ = Handle(client, ct);
            }
        }

        private async Task Handle(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var (method, path, body) = await ReadRequest(stream, ct);
                    var response = Route(method, path, body);
                    var payload = Encoding.UTF8.GetBytes(response ?? "{\"error\":\"not found\"}");
                    var status = response is null ? "404 Not Found" : "200 OK";
                    var header = $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
                    await stream.WriteAsync(payload, ct);
                    await stream.FlushAsync(ct);
                }
                catch { /* connection torn down — irrelevant */ }
            }
        }

        private static async Task<(string Method, string Path, string Body)> ReadRequest(NetworkStream stream, CancellationToken ct)
        {
            var buf = new byte[8192];
            var sb = new StringBuilder();
            int headerEnd;
            while ((headerEnd = sb.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal)) < 0)
            {
                var n = await stream.ReadAsync(buf, ct);
                if (n <= 0) break;
                sb.Append(Encoding.UTF8.GetString(buf, 0, n));
            }
            var text = sb.ToString();
            var firstLine = text.Split("\r\n", 2)[0].Split(' ');
            var method = firstLine.Length > 0 ? firstLine[0] : "";
            var path = firstLine.Length > 1 ? firstLine[1] : "";

            var contentLength = 0;
            foreach (var line in text.Split("\r\n"))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
            }
            var body = headerEnd >= 0 ? text[(headerEnd + 4)..] : "";
            while (Encoding.UTF8.GetByteCount(body) < contentLength)
            {
                var n = await stream.ReadAsync(buf, ct);
                if (n <= 0) break;
                body += Encoding.UTF8.GetString(buf, 0, n);
            }
            return (method, path, body);
        }

        private string? Route(string method, string rawPath, string body)
        {
            var qIdx = rawPath.IndexOf('?');
            var path = qIdx >= 0 ? rawPath[..qIdx] : rawPath;
            var query = ParseQuery(qIdx >= 0 ? rawPath[(qIdx + 1)..] : "");

            if (method == "GET" && path == "/api/v1/agent/status")
                return "{\"running\":true,\"route\":\"/home\"}";

            if (method == "GET" && path == "/api/v1/ui/elements")
            {
                IEnumerable<Element> matches = _els.Where(e => e.VisibleAfter is null || e.VisibleAfter <= DateTimeOffset.UtcNow);
                if (query.TryGetValue("automationId", out var aid)) matches = matches.Where(e => e.AutomationId == aid);
                else if (query.TryGetValue("text", out var txt)) matches = matches.Where(e => e.Text == txt);
                else if (query.TryGetValue("type", out var ty)) matches = matches.Where(e => e.Type == ty);
                return JsonSerializer.Serialize(matches.Select(ToJson));
            }

            if (method == "GET" && path == "/api/v1/ui/tree")
            {
                // A flat list, not the nested Children shape the real agent returns. That is
                // enough for selector resolution by AutomationId, type and text, which is all
                // this loopback agent models; a test that needs scoped or collectionScope
                // resolution must not use this fake, because it would pass against a tree shape
                // the product never emits.
                var visible = _els.Where(e => e.VisibleAfter is null || e.VisibleAfter <= DateTimeOffset.UtcNow);
                return JsonSerializer.Serialize(visible.Select(ToJson));
            }

            if (method == "GET" && path.StartsWith("/api/v1/ui/elements/") && path.Contains("/properties/"))
            {
                var (id, name) = PropPath(path);
                var el = _els.FirstOrDefault(e => e.Id == id);
                var value = el is null ? "" : name == "Text" ? el.Text : "";
                return JsonSerializer.Serialize(new { value });
            }

            if (method == "PUT" && path.Contains("/properties/"))
            {
                var (id, name) = PropPath(path);
                var el = _els.FirstOrDefault(e => e.Id == id);
                if (el is not null && name == "Text") el.Text = Field(body, "value") ?? "";
                return "{\"success\":true}";
            }

            if (method == "GET" && path.StartsWith("/api/v1/ui/elements/"))
            {
                var id = Uri.UnescapeDataString(path["/api/v1/ui/elements/".Length..]);
                var el = _els.FirstOrDefault(e => e.Id == id);
                return el is null ? "null" : JsonSerializer.Serialize(ToJson(el));
            }

            if (method == "POST" && path == "/api/v1/ui/actions/tap")
            {
                var id = Field(body, "elementId") ?? "";
                Taps.Add(id);
                if (id == "btn")
                    _els.First(e => e.Id == "late-btn").VisibleAfter = DateTimeOffset.UtcNow.AddMilliseconds(250);
                return "{\"success\":true}";
            }
            if (method == "POST" && path == "/api/v1/ui/actions/fill")
            {
                var id = Field(body, "elementId");
                var el = _els.FirstOrDefault(e => e.Id == id);
                if (el is not null) el.Text = Field(body, "text") ?? "";
                return "{\"success\":true}";
            }
            if (method == "POST" && path.StartsWith("/api/v1/ui/actions/"))
                return "{\"success\":true}";

            if (method == "PUT" && path == "/api/v1/device/app/theme")
                return "{\"theme\":\"dark\",\"requestedTheme\":\"dark\",\"effectiveTheme\":\"dark\",\"success\":true}";

            return null;
        }

        private static object ToJson(Element e) => new
        {
            id = e.Id,
            type = e.Type,
            fullType = e.Type,
            automationId = e.AutomationId,
            text = e.Text,
            isVisible = e.Visible,
            isEnabled = e.Enabled,
            bounds = new { x = 0, y = 0, width = 100, height = 40 }
        };

        private static (string Id, string Name) PropPath(string path)
        {
            // /api/v1/ui/elements/{id}/properties/{name}
            var parts = path.Split('/');
            var id = parts.Length > 5 ? Uri.UnescapeDataString(parts[5]) : "";
            var name = parts.Length > 7 ? Uri.UnescapeDataString(parts[7]) : "";
            return (id, name);
        }

        private static Dictionary<string, string> ParseQuery(string q)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var i = pair.IndexOf('=');
                if (i > 0) d[Uri.UnescapeDataString(pair[..i])] = Uri.UnescapeDataString(pair[(i + 1)..]);
            }
            return d;
        }

        private static string? Field(string body, string key)
        {
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                return doc.RootElement.TryGetProperty(key, out var v) ? v.GetString() : null;
            }
            catch { return null; }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            try { await _loop; } catch { }
            _cts.Dispose();
        }
    }
}
