using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class MauiFlowReplaySafetyEvaluatorTests
{
    [Fact]
    public void Evaluate_ObservationDeferredUntilLaunch_AdmitsReplayButClosesEveryTrustClaim()
    {
        var context = Context(includeReset: false, includeOracle: true);
        context.Preconditions!.Observed = null;
        context.Preconditions.ObservationDeferredUntilLaunch = true;

        var decision = MauiFlowReplaySafetyEvaluator.Evaluate(Request(
            Plan(MauiFlowSideEffectPolicies.None),
            context));

        Assert.True(decision.OrdinaryReplayAllowed);
        Assert.False(decision.RepairValidationAllowed);
        Assert.False(decision.RepairEligibility);
        Assert.False(decision.RunVerificationAllowed);
        Assert.False(decision.DownstreamContinuationAllowed);
        var reason = Assert.Single(
            decision.Reasons,
            candidate => candidate.Code == "preconditions-observation-deferred");
        Assert.False(reason.Blocking);
    }

    [Fact]
    public void Evaluate_DeferralDoesNotExcuseADeclarationConflict()
    {
        var context = Context(includeReset: false, includeOracle: true);
        context.Preconditions!.Observed = null;
        context.Preconditions.ObservationDeferredUntilLaunch = true;
        // The plan declares "/home"; the host supplied a different expectation. That conflict is
        // decided before anything is launched, so the deferral must not admit the replay.
        context.Preconditions.Expected!.Route = "/settings";

        var decision = MauiFlowReplaySafetyEvaluator.Evaluate(Request(
            Plan(MauiFlowSideEffectPolicies.None),
            context));

        Assert.False(decision.OrdinaryReplayAllowed);
        Assert.Contains(decision.Reasons, reason => reason.Code == "precondition-declaration-route-mismatch");
        Assert.Contains(decision.Reasons, reason => reason.Code == "preconditions-observation-deferred");
    }

    [Fact]
    public void Evaluate_ObservationMissingWithoutDeferral_StillRefuses()
    {
        var context = Context(includeReset: false, includeOracle: true);
        context.Preconditions!.Observed = null;

        var decision = MauiFlowReplaySafetyEvaluator.Evaluate(Request(
            Plan(MauiFlowSideEffectPolicies.None),
            context));

        Assert.False(decision.OrdinaryReplayAllowed);
        Assert.Contains(decision.Reasons, reason => reason.Code == "preconditions-observation-missing");
        Assert.DoesNotContain(decision.Reasons, reason => reason.Code == "preconditions-observation-deferred");
    }

    [Fact]
    public void Evaluate_DeferralFlagWithSuppliedObservation_StillComparesCheckpointFields()
    {
        var context = Context(includeReset: false, includeOracle: true);
        context.Preconditions!.ObservationDeferredUntilLaunch = true;
        context.Preconditions.Observed!.Route = "/settings";

        var decision = MauiFlowReplaySafetyEvaluator.Evaluate(Request(
            Plan(MauiFlowSideEffectPolicies.None),
            context));

        Assert.False(decision.OrdinaryReplayAllowed);
        Assert.Contains(decision.Reasons, reason => reason.Code == "precondition-route-mismatch");
        Assert.DoesNotContain(decision.Reasons, reason => reason.Code == "preconditions-observation-deferred");
    }

    [Fact]
    public void Evaluate_DeferralFlagDoesNotSuppressAnUnsatisfiableDeclaration()
    {
        var context = Context(includeReset: false, includeOracle: true);
        context.Preconditions!.ObservationDeferredUntilLaunch = true;
        context.Preconditions.Observed!.Route = null;

        var decision = MauiFlowReplaySafetyEvaluator.Evaluate(Request(
            Plan(MauiFlowSideEffectPolicies.None),
            context));

        Assert.False(decision.OrdinaryReplayAllowed);
        Assert.Contains(decision.Reasons, reason => reason.Code == "precondition-route-missing");
    }

    [Fact]
    public void PublicApi_UsesDistinctFlowAwareNameAndRetainsCompatibilityOverload()
    {
        var type = typeof(MauiFlowReplaySafetyEvaluator);

        Assert.NotNull(type.GetMethod(
            nameof(MauiFlowReplaySafetyEvaluator.EvaluateWithFlow),
            [typeof(MauiFlowRunRequest), typeof(MauiFlow)]));
        var compatibility = type.GetMethod(
            nameof(MauiFlowReplaySafetyEvaluator.Evaluate),
            [typeof(MauiFlowRunRequest), typeof(MauiFlow)]);
        Assert.NotNull(compatibility);
        Assert.NotNull(compatibility!.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false).Single());
    }

    [Fact]
    public void Evaluate_NonePolicy_WithMatchingPreconditions_AllowsReplayAndVerifiedRepairEligibility()
    {
        var decision = MauiFlowReplaySafetyEvaluator.Evaluate(Request(
            Plan(MauiFlowSideEffectPolicies.None),
            Context(includeReset: false, includeOracle: true)));

        Assert.True(decision.OrdinaryReplayAllowed);
        Assert.True(decision.DiagnosticReplayAllowed);
        Assert.True(decision.DownstreamContinuationAllowed);
        Assert.True(decision.RepairValidationAllowed);
        Assert.True(decision.RepairEligibility);
        Assert.True(decision.RunVerificationAllowed);
        Assert.Equal(MauiFlowSideEffectPolicies.None, decision.SideEffectPolicy);
    }

    [Fact]
    public void Evaluate_TestTenantResettable_WithoutBackendResetProof_DeniesBeforeReplay()
    {
        var context = Context(includeReset: true, includeOracle: true);
        context.Reset!.BackendTestDataSucceeded = false;
        context.Reset.Outcome!.BackendTestDataSucceeded = false;

        var decision = MauiFlowReplaySafetyEvaluator.Evaluate(Request(
            Plan(MauiFlowSideEffectPolicies.TestTenantResettable),
            context));

        Assert.False(decision.OrdinaryReplayAllowed);
        Assert.False(decision.DiagnosticReplayAllowed);
        Assert.False(decision.RepairValidationAllowed);
        Assert.Contains(decision.Reasons, reason => reason.Code == "backend-test-data-reset-not-proven");
    }

    /// <summary>A plan that declares an app-state seed and deliberately declares no backend seed.</summary>
    private static MauiTestPlan AppStateOnlyPlan(bool declareAppSeed = true)
    {
        // Round-trips the shared helper so the plan keeps every field deterministic validation
        // requires, then removes exactly the seed declarations under test.
        var json = JsonSerializer.SerializeToNode(
            Plan(MauiFlowSideEffectPolicies.AppStateResettable),
            MauiTestingJsonContext.Default.MauiTestPlan)!.AsObject();

        foreach (var section in new[] { "checkpoint", "reset" })
        {
            if (json[section] is not JsonObject node)
                continue;
            node.Remove("backendStateFingerprint");
            node.Remove("backendTestDataSeed");
            if (!declareAppSeed)
            {
                node.Remove("seedFingerprint");
                node.Remove("appStateSeed");
            }
        }

        return json.Deserialize(MauiTestingJsonContext.Default.MauiTestPlan)!;
    }

    [Fact]
    public void Evaluate_AppStateResettable_WithAppOnlyResetProof_AllowsReplay()
    {
        // An in-app reset owner restores app state and seeds no backend. Demanding backend proof
        // here would make the claim unprovable rather than safer, so the policy asks only for what
        // an app-state reset can actually establish.
        var context = Context(includeReset: true, includeOracle: true);
        context.Reset!.BackendTestDataSucceeded = false;
        context.Reset.Outcome!.BackendTestDataSucceeded = false;
        context.Reset.BackendStateFingerprint = null;

        var decision = MauiFlowReplaySafetyEvaluator.Evaluate(Request(AppStateOnlyPlan(), context));

        Assert.True(decision.OrdinaryReplayAllowed);
        Assert.True(decision.RepairValidationAllowed);
        Assert.DoesNotContain(decision.Reasons, reason => reason.Code == "backend-test-data-reset-not-proven");
        Assert.DoesNotContain(decision.Reasons, reason => reason.Code == "backend-test-data-seed-not-declared");
    }

    [Fact]
    public void Evaluate_AppStateResettable_WithoutAppStateResetProof_DeniesBeforeReplay()
    {
        // Dropping backend proof must not weaken the proof the policy does claim.
        var context = Context(includeReset: true, includeOracle: true);
        context.Reset!.AppStateSucceeded = false;
        context.Reset.Outcome!.AppStateSucceeded = false;

        var decision = MauiFlowReplaySafetyEvaluator.Evaluate(Request(AppStateOnlyPlan(), context));

        Assert.False(decision.OrdinaryReplayAllowed);
        Assert.False(decision.RepairValidationAllowed);
        Assert.Contains(decision.Reasons, reason => reason.Code == "app-state-reset-not-proven");
    }

    [Fact]
    public void Evaluate_AppStateResettable_WithoutDeclaredAppSeed_DeniesBeforeReplay()
    {
        var context = Context(includeReset: true, includeOracle: true);

        var decision = MauiFlowReplaySafetyEvaluator.Evaluate(
            Request(AppStateOnlyPlan(declareAppSeed: false), context));

        Assert.False(decision.OrdinaryReplayAllowed);
        Assert.Contains(decision.Reasons, reason => reason.Code == "app-state-seed-not-declared");
    }

    [Fact]
    public void Evaluate_Compensated_WithDeclaredSuccessfulCompensator_AllowsReplayWithoutReset()
    {
        var plan = Plan(MauiFlowSideEffectPolicies.Compensated);
        plan.Compensator = new MauiFlowCompensatorReference
        {
            CompensatorId = "delete-test-order",
            Scope = "test-tenant",
        };
        var context = Context(includeReset: false, includeOracle: true);
        context.Compensator = new MauiFlowCompensatorOutcome
        {
            Compensator = new MauiFlowCompensatorReference { CompensatorId = "delete-test-order" },
            Succeeded = true,
            EvidenceReference = "compensation-evidence",
        };

        var decision = MauiFlowReplaySafetyEvaluator.Evaluate(Request(plan, context));

        Assert.True(decision.OrdinaryReplayAllowed);
        Assert.True(decision.DiagnosticReplayAllowed);
        Assert.True(decision.RepairEligibility);
    }

    [Fact]
    public void Evaluate_NonReplayable_RequiresDistinctManualAuthorizationAndNeverAllowsRepair()
    {
        var denied = MauiFlowReplaySafetyEvaluator.Evaluate(Request(
            Plan(MauiFlowSideEffectPolicies.NonReplayable),
            Context(includeReset: false, includeOracle: true)));
        Assert.False(denied.OrdinaryReplayAllowed);
        Assert.Contains(denied.Reasons, reason => reason.Code == "manual-one-shot-authorization-required");

        var authorizedContext = Context(includeReset: false, includeOracle: true);
        authorizedContext.ManualOneShotAuthorization = true;
        var authorized = MauiFlowReplaySafetyEvaluator.Evaluate(Request(
            Plan(MauiFlowSideEffectPolicies.NonReplayable),
            authorizedContext));

        Assert.True(authorized.OrdinaryReplayAllowed);
        Assert.False(authorized.DiagnosticReplayAllowed);
        Assert.False(authorized.DownstreamContinuationAllowed);
        Assert.False(authorized.RepairValidationAllowed);
        Assert.False(authorized.RepairEligibility);
        Assert.True(authorized.RunVerificationAllowed);
    }

    [Fact]
    public void Evaluate_MissingOrFailedIndependentOracle_PreventsVerification()
    {
        var missing = MauiFlowReplaySafetyEvaluator.Evaluate(Request(
            Plan(MauiFlowSideEffectPolicies.None),
            Context(includeReset: false, includeOracle: false)));

        Assert.True(missing.OrdinaryReplayAllowed);
        Assert.False(missing.RunVerificationAllowed);
        Assert.False(missing.RepairEligibility);
        Assert.Contains(missing.Reasons, reason => reason.Code == "independent-oracle-missing");

        var failedContext = Context(includeReset: false, includeOracle: true);
        failedContext.BusinessOracles[0].Succeeded = false;
        var failed = MauiFlowReplaySafetyEvaluator.Evaluate(Request(
            Plan(MauiFlowSideEffectPolicies.None),
            failedContext));

        Assert.False(failed.RunVerificationAllowed);
        Assert.False(failed.RepairValidationAllowed);
        Assert.False(failed.RepairEligibility);
        Assert.Contains(failed.Reasons, reason => reason.Code == "independent-oracle-failed");
    }

    [Fact]
    public void Evaluate_InvalidPlanAndNonIndependentOracleEvidence_FailClosed()
    {
        var invalidPlan = Plan(MauiFlowSideEffectPolicies.None);
        invalidPlan.IndependentBusinessOracles[0].Independent = false;

        var invalid = MauiFlowReplaySafetyEvaluator.Evaluate(Request(
            invalidPlan,
            Context(includeReset: false, includeOracle: true)));

        Assert.False(invalid.OrdinaryReplayAllowed);
        Assert.Contains(invalid.Reasons, reason => reason.Code == "test-plan-invalid");

        var context = Context(includeReset: false, includeOracle: true);
        context.BusinessOracles[0].Independent = null;
        var nonIndependent = MauiFlowReplaySafetyEvaluator.Evaluate(Request(
            Plan(MauiFlowSideEffectPolicies.None),
            context));

        Assert.True(nonIndependent.OrdinaryReplayAllowed);
        Assert.False(nonIndependent.RunVerificationAllowed);
        Assert.Contains(
            nonIndependent.Reasons,
            reason => reason.Code == "independent-oracle-missing");
    }

    [Fact]
    public void Evaluate_ContradictoryOracleOutcomes_PreventVerification()
    {
        var context = Context(includeReset: false, includeOracle: true);
        context.BusinessOracles.Add(new MauiIndependentBusinessOracleResult
        {
            OracleId = "order-recorded",
            Succeeded = false,
            Independent = true,
        });

        var decision = MauiFlowReplaySafetyEvaluator.Evaluate(Request(
            Plan(MauiFlowSideEffectPolicies.None),
            context));

        Assert.False(decision.RunVerificationAllowed);
        Assert.False(decision.RepairEligibility);
        Assert.Contains(
            decision.Reasons,
            reason => reason.Code == "independent-oracle-outcome-conflict");
    }

    [Fact]
    public void Evaluate_RequiredScenarioCriterionAndOracleCoverage_AllowsVerification()
    {
        var plan = CoveragePlan();
        var decision = MauiFlowReplaySafetyEvaluator.EvaluateWithFlow(
            Request(plan, Context(includeReset: false, includeOracle: true)),
            CoveredFlow());

        Assert.True(decision.RunVerificationAllowed);
        Assert.True(decision.RepairEligibility);
        Assert.DoesNotContain(
            decision.Reasons,
            reason => reason.Code is
                "required-scenario-uncovered" or
                "required-acceptance-criterion-uncovered" or
                "acceptance-criterion-oracle-undeclared");
    }

    [Fact]
    public void Evaluate_UncoveredScenarioAndCriterion_PreventsVerificationWithoutBlockingReplay()
    {
        var plan = CoveragePlan();
        var flow = CoveredFlow();
        flow.Steps[0].AcceptanceCriterionIds = null;

        var decision = MauiFlowReplaySafetyEvaluator.EvaluateWithFlow(
            Request(plan, Context(includeReset: false, includeOracle: true)),
            flow);

        Assert.True(decision.OrdinaryReplayAllowed);
        Assert.False(decision.RunVerificationAllowed);
        Assert.False(decision.RepairEligibility);
        Assert.Contains(decision.Reasons, reason => reason.Code == "required-scenario-uncovered");
        Assert.Contains(decision.Reasons, reason => reason.Code == "required-acceptance-criterion-uncovered");
    }

    [Fact]
    public void Evaluate_RequiredCriterionOracleMustBeDeclaredRequiredAndIndependent()
    {
        var plan = CoveragePlan();
        plan.AcceptanceCriteria[0] = new MauiAcceptanceCriterion
        {
            CriterionId = "order-visible",
            Required = true,
            BusinessOracleId = "inventory-updated",
        };

        var decision = MauiFlowReplaySafetyEvaluator.EvaluateWithFlow(
            Request(plan, Context(includeReset: false, includeOracle: true)),
            CoveredFlow());

        Assert.False(decision.OrdinaryReplayAllowed);
        Assert.False(decision.RunVerificationAllowed);
        Assert.Contains(
            decision.Reasons,
            reason => reason.Code == "test-plan-invalid");
    }

    [Fact]
    public void Evaluate_RequiredCoverageWithoutFlow_PreventsVerification()
    {
        var decision = MauiFlowReplaySafetyEvaluator.Evaluate(
            Request(CoveragePlan(), Context(includeReset: false, includeOracle: true)));

        Assert.True(decision.OrdinaryReplayAllowed);
        Assert.False(decision.RunVerificationAllowed);
        Assert.Contains(decision.Reasons, reason => reason.Code == "verification-flow-missing");
    }

    [Fact]
    public void Evaluate_DeclaredCheckpointMismatchForEverySafetyField_DeniesAdmission()
    {
        var fieldMutations = new (string Code, Action<MauiFlowCheckpoint> Mutate)[]
        {
            ("app-build", checkpoint => checkpoint.AppBuildFingerprint = "other-build"),
            ("app-state-seed", checkpoint => checkpoint.SeedFingerprint = "other-app-seed"),
            ("backend-test-data-seed", checkpoint => checkpoint.BackendStateFingerprint = "other-backend-seed"),
            ("route", checkpoint => checkpoint.Route = "/other"),
            ("window", checkpoint => checkpoint.Window = "other-window"),
            ("modal", checkpoint => checkpoint.Modal = "other-modal"),
            ("locale", checkpoint => checkpoint.Locale = "fr-FR"),
            ("theme", checkpoint => checkpoint.Theme = "dark"),
            ("orientation", checkpoint => checkpoint.Orientation = "landscape"),
            ("display", checkpoint => checkpoint.DisplayProfile = "tablet"),
            ("collection-item-key", checkpoint => checkpoint.CollectionItemKey = "other-item"),
        };

        foreach (var (code, mutate) in fieldMutations)
        {
            var context = Context(includeReset: false, includeOracle: true);
            mutate(context.Preconditions!.Observed!);
            var decision = MauiFlowReplaySafetyEvaluator.Evaluate(Request(
                Plan(MauiFlowSideEffectPolicies.None),
                context));

            Assert.False(decision.OrdinaryReplayAllowed);
            Assert.Contains(decision.Reasons, reason => reason.Code == $"precondition-{code}-mismatch");
        }
    }

    [Fact]
    public void Evaluate_LegacyManualRequest_RemainsReplayableButNeverRepairEligible()
    {
        var decision = MauiFlowReplaySafetyEvaluator.Evaluate(new MauiFlowRunRequest());

        Assert.True(decision.OrdinaryReplayAllowed);
        Assert.True(decision.DiagnosticReplayAllowed);
        Assert.True(decision.DownstreamContinuationAllowed);
        Assert.False(decision.RepairValidationAllowed);
        Assert.False(decision.RepairEligibility);
        Assert.False(decision.RunVerificationAllowed);
        Assert.Equal(MauiFlowSideEffectPolicies.Unspecified, decision.SideEffectPolicy);
        Assert.Contains(decision.Reasons, reason => reason.Code == "side-effect-policy-unspecified");
    }

    [Fact]
    public async Task Runner_DeniedSafetyAdmission_DoesNotInvokeMutationDriver()
    {
        var driver = new NoMutationDriver();
        var context = Context(includeReset: false, includeOracle: true);
        var result = await new MauiFlowRunner(
            driver,
            new MauiFlowRunnerOptions
            {
                Plan = Plan(MauiFlowSideEffectPolicies.TestTenantResettable),
                RunContext = context,
            }).RunWithLegacyAsync(new MauiFlow
            {
                Name = "unsafe",
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
            });

        Assert.False(result.LegacyReport.Ok);
        Assert.Equal(MauiFlowFailureClasses.PreconditionUnsatisfied, result.Report.Failure!.Code);
        Assert.Equal(0, driver.MutationCalls);
        Assert.False(result.Report.ReplayEligibility!.OrdinaryReplayAllowed);
    }

    [Fact]
    public async Task Runner_PassedExecutionWithUncoveredPlan_RemainsUnverified()
    {
        var result = await new MauiFlowRunner(
            new NoMutationDriver(),
            new MauiFlowRunnerOptions
            {
                Plan = CoveragePlan(),
                RunContext = Context(includeReset: false, includeOracle: true),
            }).RunWithLegacyAsync(new MauiFlow
            {
                Name = "coverage-gap",
                Steps =
                [
                    new FlowStep
                    {
                        Seq = 1,
                        StepId = "run-without-proof",
                        Action = FlowActions.Assert,
                    },
                ],
            });

        Assert.Equal(MauiFlowRunOutcomes.Passed, result.Report.Outcome!.Status);
        Assert.False(result.Report.Outcome.Verified);
        Assert.False(result.Report.Verification!.Verified);
        Assert.Contains(
            "required-acceptance-criterion-uncovered",
            result.Report.Verification.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RunReport_RoundTripsSafetyEvidenceAndRedactsSensitiveNestedMessages()
    {
        const string secret = "CorrectHorseBatteryStaple";
        var decision = MauiFlowReplaySafetyEvaluator.Evaluate(Request(
            Plan(MauiFlowSideEffectPolicies.None),
            Context(includeReset: false, includeOracle: true)));
        decision.Reasons.Add(new MauiFlowReplayEligibilityReason
        {
            Code = "x",
            Message = secret,
            Blocking = false,
        });
        var report = new MauiFlowRunReport
        {
            RunId = "run-safety",
            FlowDigest = new string('a', 64),
            SideEffectPolicy = decision.SideEffectPolicy,
            Preconditions = new MauiFlowReplayPreconditions
            {
                Expected = Checkpoint(),
                Observed = Checkpoint(),
                EvidenceReference = $"oracle={secret}",
            },
            Reset = new MauiFlowResetResult
            {
                Reference = new MauiFlowResetReference { EvidenceReference = $"token={secret}" },
                Outcome = new MauiFlowResetOutcome { Message = $"secret={secret}" },
            },
            Compensator = new MauiFlowCompensatorOutcome
            {
                Message = $"password={secret}",
            },
            BusinessOracles =
            [
                new MauiIndependentBusinessOracleResult
                {
                    OracleId = "order-recorded",
                    Succeeded = true,
                    Independent = true,
                    Message = $"token={secret}",
                },
            ],
            ReplayEligibility = decision,
            StartedAt = DateTimeOffset.UtcNow,
            EndedAt = DateTimeOffset.UtcNow,
            Outcome = new MauiFlowRunOutcome { Status = MauiFlowRunOutcomes.Passed, Terminal = true },
        };

        var json = Encoding.UTF8.GetString(MauiFlowRunReportSerializer.SerializeToUtf8Bytes(report));
        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        var roundTrip = JsonSerializer.Deserialize(json, MauiTestingJsonContext.Default.MauiFlowRunReport);

        Assert.NotNull(roundTrip);
        Assert.Equal(MauiFlowSideEffectPolicies.None, roundTrip!.SideEffectPolicy);
        Assert.NotNull(roundTrip.Preconditions);
        Assert.NotNull(roundTrip.Reset?.Reference);
        Assert.NotNull(roundTrip.ReplayEligibility);
        Assert.Single(roundTrip.BusinessOracles);
    }

    private static MauiFlowRunRequest Request(MauiTestPlan plan, MauiFlowRunContext context) => new()
    {
        Plan = plan,
        Context = context,
    };

    private static MauiTestPlan Plan(string policy) => new()
    {
        PlanId = "plan",
        Revision = 1,
        Flow = new MauiFlowReference
        {
            Path = "flow.md",
            Digest = new string('a', 64),
        },
        Goal = "Verify the declared behavior.",
        SideEffectPolicy = policy,
        Checkpoint = new MauiFlowCheckpointRequirements
        {
            AppBuildFingerprint = "build-1",
            SeedFingerprint = "app-seed-1",
            BackendStateFingerprint = "backend-seed-1",
            Route = "/home",
            Window = "main",
            Modal = "none",
            Locale = "en-US",
            Theme = "light",
            Orientation = "portrait",
            DisplayProfile = "phone",
            CollectionItemKey = "item-1",
        },
        Reset = new MauiTestResetRequirement
        {
            SeedFingerprint = "app-seed-1",
            BackendStateFingerprint = "backend-seed-1",
        },
        IndependentBusinessOracles =
        [
            new MauiIndependentBusinessOracleDeclaration
            {
                OracleId = "order-recorded",
                Required = true,
                Independent = true,
            },
        ],
        Provenance = new MauiActorProvenance
        {
            ActorKind = "human",
            Channel = "unit-test",
        },
    };

    private static MauiTestPlan CoveragePlan()
    {
        var plan = Plan(MauiFlowSideEffectPolicies.None);
        plan.Scenarios.Add(new MauiTestScenario
        {
            ScenarioId = "checkout",
            AcceptanceCriterionIds = ["order-visible"],
        });
        plan.AcceptanceCriteria.Add(new MauiAcceptanceCriterion
        {
            CriterionId = "order-visible",
            Required = true,
            BusinessOracleId = "order-recorded",
        });
        return plan;
    }

    private static MauiFlow CoveredFlow() => new()
    {
        Name = "covered",
        Steps =
        [
            new FlowStep
            {
                Seq = 1,
                StepId = "verify-order",
                Action = FlowActions.Assert,
                AcceptanceCriterionIds = ["order-visible"],
                Asserts =
                [
                    new FlowAssert
                    {
                        Kind = "exists",
                        Verify = true,
                        Selector = new FlowSelector { AutomationId = "order-row" },
                    },
                ],
            },
        ],
    };

    private static MauiFlowRunContext Context(bool includeReset, bool includeOracle)
    {
        var context = new MauiFlowRunContext
        {
            Preconditions = new MauiFlowReplayPreconditions
            {
                Expected = Checkpoint(),
                Observed = Checkpoint(),
            },
        };
        if (includeReset)
        {
            context.Reset = new MauiFlowResetResult
            {
                Requested = true,
                Succeeded = true,
                AppStateSucceeded = true,
                BackendTestDataSucceeded = true,
                SeedFingerprint = "app-seed-1",
                BackendStateFingerprint = "backend-seed-1",
                Outcome = new MauiFlowResetOutcome
                {
                    Succeeded = true,
                    AppStateSucceeded = true,
                    BackendTestDataSucceeded = true,
                },
            };
        }
        if (includeOracle)
        {
            context.BusinessOracles.Add(new MauiIndependentBusinessOracleResult
            {
                OracleId = "order-recorded",
                Succeeded = true,
                Independent = true,
            });
        }
        return context;
    }

    private static MauiFlowCheckpoint Checkpoint() => new()
    {
        AppBuildFingerprint = "build-1",
        SeedFingerprint = "app-seed-1",
        BackendStateFingerprint = "backend-seed-1",
        Route = "/home",
        Window = "main",
        Modal = "none",
        Locale = "en-US",
        Theme = "light",
        Orientation = "portrait",
        DisplayProfile = "phone",
        CollectionItemKey = "item-1",
    };

    private sealed class NoMutationDriver : IMauiFlowDriver
    {
        public int MutationCalls { get; private set; }
        public WorkflowCommandReceipt? LastWorkflowCommandReceipt => null;

        public Task<List<ElementInfo>> QueryAsync(string? type = null, string? automationId = null, string? text = null)
            => Task.FromResult(new List<ElementInfo>());

        public Task<ElementInfo?> GetElementAsync(string id) => Task.FromResult<ElementInfo?>(null);
        public Task<bool> TapAsync(string elementId)
        {
            MutationCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> FillAsync(string elementId, string text)
        {
            MutationCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> SetPropertyAsync(string elementId, string propertyName, string value)
        {
            MutationCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> ScrollAsync(
            string? elementId = null,
            double deltaX = 0,
            double deltaY = 0,
            bool animated = true,
            int? itemIndex = null,
            string? scrollToPosition = null)
        {
            MutationCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> NavigateAsync(string route)
        {
            MutationCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> BackAsync()
        {
            MutationCalls++;
            return Task.FromResult(true);
        }

        public Task<ThemeResult> SetThemeAsync(DevFlowTheme theme)
        {
            MutationCalls++;
            return Task.FromResult(new ThemeResult { Success = true });
        }

        public Task<string?> GetPropertyAsync(string elementId, string propertyName) => Task.FromResult<string?>(null);
        public Task<AgentStatus?> GetStatusAsync() => Task.FromResult<AgentStatus?>(null);
    }
}
