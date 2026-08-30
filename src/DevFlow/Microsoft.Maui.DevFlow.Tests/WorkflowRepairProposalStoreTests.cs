using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class WorkflowRepairProposalStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        AppContext.BaseDirectory,
        "workflow-repair-store-tests",
        Guid.NewGuid().ToString("N"));

    public WorkflowRepairProposalStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Store_ValidationApprovalAndGrantReuse_FailsClosed()
    {
        var proposal = Proposal("repair.md", new string('a', 64));
        var store = new WorkflowRepairProposalStore();
        var proposed = store.Propose(proposal, trustedContext: TrustedContext(proposal));
        Assert.True(proposed.Ok);
        var previewed = store.Preview(proposal.ProposalId!);
        Assert.True(previewed.Ok);

        var binding = Binding(proposed.Proposal!);
        var validationGrant = store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Validation,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            Binding = binding,
        });
        Assert.True(validationGrant.Ok);
        var validation = store.RecordValidation(proposal.ProposalId, validationGrant.Grant, new WorkflowRepairValidationRecord
        {
            Passed = true,
            RunIds = ["validation-run"],
            EvidenceIds = ["evidence-1"],
            RecordedAt = DateTimeOffset.UtcNow,
        });
        Assert.True(validation.Ok);
        Assert.Equal(MauiFlowRepairOutcomeStates.Previewed, validation.Proposal!.State);

        var approval = store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Apply,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            Binding = binding,
        });
        Assert.True(approval.Ok);
        Assert.Equal(MauiFlowRepairOutcomeStates.Approved, approval.Proposal!.State);
        var applying = store.BeginApply(proposal.ProposalId, approval.Grant, binding);
        Assert.True(applying.Ok);
        Assert.Equal(MauiFlowRepairOutcomeStates.Applying, applying.Proposal!.State);

        var reused = store.BeginApply(proposal.ProposalId, approval.Grant, binding);
        Assert.False(reused.Ok);
        Assert.Equal("approval-required", reused.Code);
    }

    [Fact]
    public void ApplyAndRollback_UseNewRevisionsAndRedactedHashLinkedHistory()
    {
        var flow = Flow();
        var markdown = "Human review prose that must survive selector repair.\n\n" + FlowMarkdown.Serialize(flow);
        var workflowRoot = Path.Combine(_root, "maui-tests");
        Directory.CreateDirectory(workflowRoot);
        var flowPath = Path.Combine(workflowRoot, "repair.md");
        File.WriteAllText(flowPath, markdown);
        var digest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow);
        File.WriteAllText(
            Path.Combine(workflowRoot, "repair.maui-plan.json"),
            JsonSerializer.Serialize(
                new MauiTestPlan
                {
                    PlanId = "plan-repair",
                    Revision = 1,
                    Flow = new MauiFlowReference
                    {
                        Path = "repair.md",
                        FlowId = "flow-repair",
                        Revision = 1,
                        Digest = digest,
                    },
                    Title = "Repair",
                    Goal = "Verify repair lifecycle",
                    Reset = new MauiTestResetRequirement { Required = false, Strategy = "host-owned" },
                    SideEffectPolicy = MauiFlowSideEffectPolicies.None,
                    Provenance = new MauiActorProvenance
                    {
                        ActorKind = "human",
                        ActorId = "reviewer",
                        Channel = "test",
                        Provider = "unit-test",
                    },
                },
                MauiTestingJsonContext.Default.MauiTestPlan));
        var proposal = Proposal("repair.md", digest);
        proposal.Candidate!.RiskFlags.Add("CorrectHorseBatteryStaple");
        var store = new WorkflowPlanStore(_root);
        var baseline = store.Load("repair.md").Snapshot!;
        var proposedHistory = store.AppendRepairHistory(new WorkflowRepairHistoryAppendRequest
        {
            Proposal = proposal,
            State = MauiFlowRepairOutcomeStates.Proposed,
            Reviewer = "reviewer",
        });
        Assert.True(proposedHistory.Ok, proposedHistory.Error);
        var duplicateHistory = store.AppendRepairHistory(new WorkflowRepairHistoryAppendRequest
        {
            Proposal = proposal,
            State = MauiFlowRepairOutcomeStates.Proposed,
            Reviewer = "reviewer",
        });
        Assert.True(duplicateHistory.Ok, duplicateHistory.Error);
        Assert.Single(File.ReadAllLines(proposedHistory.HistoryPath!));

        var stalePlan = store.ApplySelectorRepair(new WorkflowRepairFlowApplyRequest
        {
            Proposal = proposal,
            ExpectedFlowDigest = digest,
            ExpectedFlowRevision = 1,
            ExpectedPlanDigest = "sha256:" + new string('f', 64),
            ExpectedPlanRevision = baseline.Plan!.Revision,
            ExpectedSafetyPolicy = baseline.Plan.SideEffectPolicy,
        });
        Assert.False(stalePlan.Ok);
        Assert.True(stalePlan.Stale);

        var applied = store.ApplySelectorRepair(new WorkflowRepairFlowApplyRequest
        {
            Proposal = proposal,
            ExpectedFlowDigest = digest,
            ExpectedFlowRevision = 1,
            ExpectedPlanDigest = baseline.PlanDigest,
            ExpectedPlanRevision = baseline.Plan!.Revision,
            ExpectedSafetyPolicy = baseline.Plan.SideEffectPolicy,
            Reviewer = "reviewer",
            GrantDigest = "sha256:grant",
            ValidationRunIds = ["validation-run"],
        });

        Assert.True(applied.Ok, applied.Error);
        Assert.Equal(2, applied.FlowRevision);
        var afterApply = FlowMarkdown.Parse(File.ReadAllText(flowPath));
        Assert.True(afterApply.Ok, afterApply.Error);
        Assert.Equal("new-save", afterApply.Flow!.Steps[0].Args!.Selector!.AutomationId);
        Assert.Contains("Human review prose that must survive selector repair.", File.ReadAllText(flowPath));
        Assert.True(afterApply.Flow.ExtensionData!.ContainsKey("customReviewerMetadata"));

        var reverted = store.RollbackSelectorRepair(new WorkflowRepairFlowRollbackRequest
        {
            Proposal = proposal,
            ExpectedAppliedFlowDigest = applied.FlowDigest,
            ExpectedAppliedFlowRevision = applied.FlowRevision,
            ExpectedPlanDigest = applied.PlanDigest,
            ExpectedPlanRevision = applied.PlanRevision,
            ExpectedSafetyPolicy = applied.SafetyPolicy,
            Reviewer = "reviewer",
            GrantDigest = "sha256:rollback",
            VerificationRunIds = ["verify-1", "verify-2", "verify-3"],
        });

        Assert.True(reverted.Ok, reverted.Error);
        Assert.Equal(3, reverted.FlowRevision);
        var afterRollback = FlowMarkdown.Parse(File.ReadAllText(flowPath));
        Assert.True(afterRollback.Ok, afterRollback.Error);
        var rolledBackStep = Assert.Single(afterRollback.Flow!.Steps);
        Assert.Equal("old-save", rolledBackStep.Args!.Selector!.AutomationId);
        Assert.Equal("stable-save-step", rolledBackStep.StepId);
        Assert.Equal(1, rolledBackStep.Seq);
        Assert.Equal(FlowActions.Tap, rolledBackStep.Action);
        Assert.Equal("CorrectHorseBatteryStaple", rolledBackStep.Value);
        Assert.Single(rolledBackStep.Asserts!);

        var historyPath = reverted.HistoryPath!;
        var lines = File.ReadAllLines(historyPath);
        Assert.Equal(3, lines.Length);
        Assert.DoesNotContain("old-save", string.Join('\n', lines), StringComparison.Ordinal);
        Assert.DoesNotContain("new-save", string.Join('\n', lines), StringComparison.Ordinal);
        Assert.DoesNotContain("CorrectHorseBatteryStaple", string.Join('\n', lines), StringComparison.Ordinal);
        using var initial = JsonDocument.Parse(lines[0]);
        using var first = JsonDocument.Parse(lines[1]);
        using var second = JsonDocument.Parse(lines[2]);
        Assert.Equal("proposed", initial.RootElement.GetProperty("state").GetString());
        Assert.Equal("applied", first.RootElement.GetProperty("state").GetString());
        Assert.Equal("reverted", second.RootElement.GetProperty("state").GetString());
        Assert.Equal(
            first.RootElement.GetProperty("hash").GetString(),
            second.RootElement.GetProperty("previousHash").GetString());
        Assert.Equal(
            initial.RootElement.GetProperty("hash").GetString(),
            first.RootElement.GetProperty("previousHash").GetString());
    }

    [Fact]
    public void RecordVerification_FailureTransitionsToRollbackRequired()
    {
        var proposal = Proposal("repair.md", new string('a', 64));
        var store = new WorkflowRepairProposalStore();
        Assert.True(store.Propose(proposal, trustedContext: TrustedContext(proposal)).Ok);
        Assert.True(store.Preview(proposal.ProposalId!).Ok);
        var binding = Binding(store.Get(proposal.ProposalId!).Proposal!);
        var validationGrant = store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Validation,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            Binding = binding,
        });
        Assert.True(store.RecordValidation(proposal.ProposalId, validationGrant.Grant, new WorkflowRepairValidationRecord { Passed = true }).Ok);
        var approval = store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Apply,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            Binding = binding,
        });
        Assert.True(store.BeginApply(proposal.ProposalId, approval.Grant, binding).Ok);
        Assert.True(store.CompleteApply(proposal.ProposalId, new WorkflowRepairApplyRecord
        {
            Applied = true,
            NewFlowRevision = 2,
            AppliedFlowDigest = new string('b', 64),
            AppliedPlanDigest = "sha256:" + new string('d', 64),
            AppliedPlanRevision = 2,
            AppliedSafetyPolicy = MauiFlowSideEffectPolicies.None,
        }).Ok);

        var result = store.RecordVerification(proposal.ProposalId,
        [
            VerifiedRun("verify-1"),
            VerifiedRun("verify-2"),
            VerifiedRun("verify-2"),
        ]);

        Assert.True(result.Ok);
        Assert.Equal(MauiFlowRepairOutcomeStates.RollbackRequired, result.Proposal!.State);
        Assert.Equal(MauiFlowRepairOutcomeStates.VerificationFailed, result.Proposal.LastRecoveryState);

        var rollbackBinding = new WorkflowRepairGrantBinding
        {
            FlowPath = result.Proposal.BaseFlow!.Path,
            FlowDigest = result.Proposal.AppliedFlowDigest,
            FlowRevision = result.Proposal.NewFlowRevision,
            PatchDigest = result.Proposal.PatchDigest,
            TargetId = "agent:instance",
            Policy = "repair-policy-v1",
            PlanDigest = result.Proposal.AppliedPlanDigest,
            PlanRevision = result.Proposal.AppliedPlanRevision,
            SafetyPolicy = result.Proposal.AppliedSafetyPolicy,
        };
        var rollbackGrant = store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Rollback,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            Binding = rollbackBinding,
        });
        Assert.True(rollbackGrant.Ok, rollbackGrant.Error);
        Assert.True(store.BeginRollback(proposal.ProposalId, rollbackGrant.Grant, rollbackBinding).Ok);
    }

    [Fact]
    public void History_PathSafetyAndCaps_FailClosed()
    {
        var workflowRoot = Path.Combine(_root, "maui-tests");
        Directory.CreateDirectory(workflowRoot);
        var flow = Flow();
        File.WriteAllText(Path.Combine(workflowRoot, "repair.md"), FlowMarkdown.Serialize(flow));
        var digest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow);
        var store = new WorkflowPlanStore(_root);

        var traversal = Proposal("../outside.md", digest);
        var rejected = store.AppendRepairHistory(new WorkflowRepairHistoryAppendRequest
        {
            Proposal = traversal,
            State = MauiFlowRepairOutcomeStates.Proposed,
        });
        Assert.False(rejected.Ok);

        var proposal = Proposal("repair.md", digest);
        for (var index = 0; index < WorkflowPlanStore.MaxRepairHistoryEntries; index++)
        {
            var appended = store.AppendRepairHistory(new WorkflowRepairHistoryAppendRequest
            {
                Proposal = proposal,
                State = MauiFlowRepairOutcomeStates.Proposed,
                ReasonCode = "case-" + index,
            });
            Assert.True(appended.Ok, appended.Error);
        }
        var capped = store.AppendRepairHistory(new WorkflowRepairHistoryAppendRequest
        {
            Proposal = proposal,
            State = MauiFlowRepairOutcomeStates.Proposed,
        });
        Assert.False(capped.Ok);
        Assert.Equal("repair-history-invalid", capped.Code);
    }

    [Fact]
    public void Approval_ExpiredOrStaleBinding_FailsClosed()
    {
        var clock = new AdjustableTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var proposal = Proposal("repair.md", new string('a', 64));
        var store = new WorkflowRepairProposalStore(
            new WorkflowRepairProposalStoreOptions
            {
                DefaultGrantLifetime = TimeSpan.FromMinutes(1),
                MaximumGrantLifetime = TimeSpan.FromMinutes(5),
            },
            clock);
        Assert.True(store.Propose(proposal, trustedContext: TrustedContext(proposal)).Ok);
        Assert.True(store.Preview(proposal.ProposalId!).Ok);
        var binding = Binding(store.Get(proposal.ProposalId!).Proposal!);
        var validation = store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Validation,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            Binding = binding,
        });
        Assert.True(store.RecordValidation(proposal.ProposalId, validation.Grant, new WorkflowRepairValidationRecord { Passed = true }).Ok);
        var approval = store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Apply,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            ExpiresAt = clock.GetUtcNow().AddSeconds(1),
            Binding = binding,
        });
        Assert.True(approval.Ok);
        clock.Advance(TimeSpan.FromSeconds(2));
        var expired = store.Get(proposal.ProposalId);
        Assert.True(expired.Ok);
        Assert.Equal(MauiFlowRepairOutcomeStates.ApprovalExpired, expired.Proposal!.State);
        Assert.False(store.BeginApply(proposal.ProposalId, approval.Grant, binding).Ok);
    }

    [Fact]
    public void LatestValidationAndPlanSafetyBinding_AreAuthoritative()
    {
        var proposal = Proposal("repair.md", new string('a', 64));
        var store = new WorkflowRepairProposalStore();
        Assert.True(store.Propose(proposal, trustedContext: TrustedContext(proposal)).Ok);
        Assert.True(store.Preview(proposal.ProposalId!).Ok);
        var snapshot = store.Get(proposal.ProposalId!).Proposal!;
        var binding = Binding(snapshot);

        WorkflowRepairGrantIssueResult ValidationGrant() => store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Validation,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            Binding = binding,
        });
        var passedGrant = ValidationGrant();
        Assert.True(store.RecordValidation(
            proposal.ProposalId,
            passedGrant.Grant,
            new WorkflowRepairValidationRecord { Passed = true, RunIds = ["pass"] }).Ok);
        var failedGrant = ValidationGrant();
        Assert.True(store.RecordValidation(
            proposal.ProposalId,
            failedGrant.Grant,
            new WorkflowRepairValidationRecord
            {
                Passed = false,
                RunIds = ["fail"],
                FailureCode = "checkpoint-drift",
            }).Ok);

        var applyAfterLatestFailure = store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Apply,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            Binding = binding,
        });
        Assert.False(applyAfterLatestFailure.Ok);
        Assert.Equal("grant-state-invalid", applyAfterLatestFailure.Code);

        var tamperedBinding = new WorkflowRepairGrantBinding
        {
            FlowPath = binding.FlowPath,
            FlowDigest = binding.FlowDigest,
            FlowRevision = binding.FlowRevision,
            PatchDigest = binding.PatchDigest,
            TargetId = binding.TargetId,
            Policy = binding.Policy,
            PlanDigest = "sha256:" + new string('f', 64),
            PlanRevision = binding.PlanRevision,
            SafetyPolicy = binding.SafetyPolicy,
        };
        var tampered = store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Validation,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            Binding = tamperedBinding,
        });
        Assert.False(tampered.Ok);
        Assert.Equal("grant-binding-mismatch", tampered.Code);
    }

    [Fact]
    public void Reject_AllowsOnlyProposedOrPreviewedStates()
    {
        var proposal = Proposal("repair.md", new string('a', 64));
        var store = new WorkflowRepairProposalStore();
        Assert.True(store.Propose(proposal, trustedContext: TrustedContext(proposal)).Ok);
        Assert.True(store.Preview(proposal.ProposalId!).Ok);
        var binding = Binding(store.Get(proposal.ProposalId!).Proposal!);
        var validationGrant = store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Validation,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            Binding = binding,
        });
        Assert.True(store.RecordValidation(
            proposal.ProposalId,
            validationGrant.Grant,
            new WorkflowRepairValidationRecord { Passed = true }).Ok);
        var applyGrant = store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Apply,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            Binding = binding,
        });
        Assert.True(applyGrant.Ok);

        var rejected = store.Reject(proposal.ProposalId, "reviewer", "too-late");

        Assert.False(rejected.Ok);
        Assert.Equal("proposal-not-rejectable", rejected.Code);
        Assert.Equal(
            MauiFlowRepairOutcomeStates.Approved,
            store.Get(proposal.ProposalId).Proposal!.State);
    }

    [Fact]
    public void HistoryFailure_DoesNotPublishOrAdvanceLifecycleState()
    {
        var proposal = Proposal("repair.md", new string('a', 64));
        var store = new WorkflowRepairProposalStore();
        WorkflowRepairHistoryAppendResult FailHistory(
            WorkflowRepairProposalSnapshot _,
            string __)
            => WorkflowRepairHistoryAppendResult.Failure("write-failed", "disk unavailable");

        var failedProposal = store.Propose(
            proposal,
            trustedContext: TrustedContext(proposal),
            historyWriter: FailHistory);
        Assert.False(failedProposal.Ok);
        Assert.Equal("history-persistence-failed", failedProposal.Code);
        Assert.False(store.Get(proposal.ProposalId).Ok);

        Assert.True(store.Propose(proposal, trustedContext: TrustedContext(proposal)).Ok);
        var failedPreview = store.Preview(proposal.ProposalId, FailHistory);
        Assert.False(failedPreview.Ok);
        Assert.Equal("history-persistence-failed", failedPreview.Code);
        Assert.Equal(
            MauiFlowRepairOutcomeStates.Proposed,
            store.Get(proposal.ProposalId).Proposal!.State);
    }

    [Fact]
    public void Propose_CraftedProposalWithoutCandidateProof_FailsClosed()
    {
        var cases = new (string Name, Action<JsonObject> RemoveProof)[]
        {
            ("candidate", node => node["candidate"] = null),
            ("candidate uniqueness", node => node["candidate"]!.AsObject()["unique"] = false),
            ("candidate fingerprint", node => node["candidate"]!.AsObject()["fingerprint"] = null),
            ("uniqueness proof", node => node["uniquenessProof"] = null),
            ("unique match count", node => node["uniquenessProof"]!.AsObject()["matchCount"] = 2),
        };

        foreach (var testCase in cases)
        {
            var node = JsonSerializer.SerializeToNode(
                Proposal("repair.md", new string('a', 64)),
                MauiTestingJsonContext.Default.MauiFlowRepairProposal)!.AsObject();
            testCase.RemoveProof(node);
            var proposal = node.Deserialize(MauiTestingJsonContext.Default.MauiFlowRepairProposal)!;

            var result = new WorkflowRepairProposalStore().Propose(
                proposal,
                trustedContext: TrustedContext(proposal));

            Assert.False(result.Ok, testCase.Name);
            Assert.Equal("proposal-invalid", result.Code);
        }
    }

    [Fact]
    public void ApplyGrant_AllowedOnlyAfterPassedValidation()
    {
        var proposal = Proposal("repair.md", new string('a', 64));
        var store = new WorkflowRepairProposalStore();
        Assert.True(store.Propose(proposal, trustedContext: TrustedContext(proposal)).Ok);
        var previewed = store.Preview(proposal.ProposalId!);
        Assert.True(previewed.Ok);
        var binding = Binding(previewed.Proposal!);

        var beforeValidation = store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Apply,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            Binding = binding,
        });
        Assert.False(beforeValidation.Ok);
        Assert.Contains(
            "bounded transient validation is required",
            beforeValidation.Error,
            StringComparison.Ordinal);

        var failingGrant = store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Validation,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            Binding = binding,
        });
        Assert.True(failingGrant.Ok);
        Assert.True(store.RecordValidation(proposal.ProposalId, failingGrant.Grant, new WorkflowRepairValidationRecord
        {
            Passed = false,
            FailureCode = "transient-replay-failed",
            FailureFacts = ["candidate-not-uniquely-resolved"],
            RecordedAt = DateTimeOffset.UtcNow,
        }).Ok);

        var afterFailedValidation = store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Apply,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            Binding = binding,
        });
        Assert.False(afterFailedValidation.Ok);

        var passingGrant = store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Validation,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            Binding = binding,
        });
        Assert.True(passingGrant.Ok);
        Assert.True(store.RecordValidation(proposal.ProposalId, passingGrant.Grant, new WorkflowRepairValidationRecord
        {
            Passed = true,
            RunIds = ["validation-run"],
            EvidenceIds = ["evidence-1"],
            RecordedAt = DateTimeOffset.UtcNow,
        }).Ok);

        var afterPassedValidation = store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Apply,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            Binding = binding,
        });
        Assert.True(afterPassedValidation.Ok);
        Assert.Equal(MauiFlowRepairOutcomeStates.Approved, afterPassedValidation.Proposal!.State);
    }

    [Fact]
    public void CanRedeemGrant_ChecksTheGrantWithoutConsumingOrMutatingIt()
    {
        var proposal = Proposal("repair.md", new string('a', 64));
        var store = new WorkflowRepairProposalStore();
        Assert.True(store.Propose(proposal, trustedContext: TrustedContext(proposal)).Ok);
        var previewed = store.Preview(proposal.ProposalId!);
        Assert.True(previewed.Ok);

        Assert.False(store.CanRedeemGrant(
            proposal.ProposalId,
            "not-a-grant",
            WorkflowRepairGrantKinds.Validation,
            out var unknownError));
        Assert.False(string.IsNullOrWhiteSpace(unknownError));

        var grant = store.IssueGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposal.ProposalId,
            Kind = WorkflowRepairGrantKinds.Validation,
            Reviewer = "reviewer",
            HumanConfirmed = true,
            Binding = Binding(previewed.Proposal!),
        });
        Assert.True(grant.Ok);

        // A pre-check must remain repeatable and must not spend the single-use grant.
        Assert.True(store.CanRedeemGrant(
            proposal.ProposalId,
            grant.Grant,
            WorkflowRepairGrantKinds.Validation,
            out _));
        Assert.True(store.CanRedeemGrant(
            proposal.ProposalId,
            grant.Grant,
            WorkflowRepairGrantKinds.Validation,
            out _));
        Assert.False(store.CanRedeemGrant(
            proposal.ProposalId,
            grant.Grant,
            WorkflowRepairGrantKinds.Apply,
            out _));
        Assert.True(store.RecordValidation(proposal.ProposalId, grant.Grant, new WorkflowRepairValidationRecord
        {
            Passed = true,
            RunIds = ["validation-run"],
            RecordedAt = DateTimeOffset.UtcNow,
        }).Ok);
        Assert.False(store.CanRedeemGrant(
            proposal.ProposalId,
            grant.Grant,
            WorkflowRepairGrantKinds.Validation,
            out _));
    }

    private static WorkflowRepairVerificationRun VerifiedRun(string id) => new()
    {
        RunId = id,
        BrokerRetained = true,
        StartedAt = DateTimeOffset.UtcNow.AddMinutes(1),
        CleanReset = true,
        CheckpointMatched = true,
        FingerprintMatched = true,
        UniqueResolution = true,
        HardAssertionsUnchanged = true,
        IndependentOracleSucceeded = true,
        Passed = true,
    };

    private static WorkflowRepairGrantBinding Binding(WorkflowRepairProposalSnapshot snapshot) => new()
    {
        FlowPath = snapshot.BaseFlow!.Path,
        FlowDigest = snapshot.BaseFlow.Digest,
        FlowRevision = snapshot.BaseFlow.Revision,
        PatchDigest = snapshot.PatchDigest,
        TargetId = "agent:instance",
        Policy = "repair-policy-v1",
        PlanDigest = snapshot.TrustedContext.PlanDigest,
        PlanRevision = snapshot.TrustedContext.PlanRevision,
        SafetyPolicy = snapshot.TrustedContext.SafetyPolicy,
    };

    private static WorkflowRepairTrustedContext TrustedContext(MauiFlowRepairProposal proposal)
    {
        var checkpoint = new MauiFlowCheckpoint
        {
            AppBuildFingerprint = "build-a",
            AgentInstanceId = "instance-a",
            SeedFingerprint = "seed-a",
            BackendStateFingerprint = "backend-a",
            Route = "//home",
            Window = "main",
            Modal = "none",
            Locale = "en-US",
            Theme = "light",
            Orientation = "portrait",
            DisplayProfile = "phone",
            CollectionItemKey = "none",
        };
        return new WorkflowRepairTrustedContext
        {
            Eligibility = new MauiFlowRepairEligibilityDecision
            {
                Eligible = true,
                SourceRunId = proposal.SourceRunId,
                SourceStepId = proposal.SourceStepId,
                CurrentCheckpoint = checkpoint,
            },
            ReplaySafety = new MauiFlowReplayEligibilityDecision
            {
                SideEffectPolicy = MauiFlowSideEffectPolicies.None,
                RepairValidationAllowed = true,
                RepairEligibility = true,
            },
            ClassifiedCheckpoint = checkpoint,
            PlanDigest = "sha256:" + new string('c', 64),
            PlanRevision = 1,
            SafetyPolicy = MauiFlowSideEffectPolicies.None,
        };
    }

    private static MauiFlowRepairProposal Proposal(string path, string digest)
    {
        var flow = Flow();
        var fingerprint = Fingerprint();
        var generated = MauiFlowRepairProposalGenerator.Generate(new MauiFlowRepairProposalGenerationInput
        {
            Eligibility = new MauiFlowRepairEligibilityDecision { Eligible = true, FailureCode = MauiFlowFailureClasses.LocatorNotFound },
            Flow = flow,
            BaseFlow = new MauiFlowReference
            {
                Path = path,
                FlowId = "flow-repair",
                Digest = digest,
                Revision = 1,
            },
            SourceRunId = "run-local",
            SourceStepId = "1",
            SourceFailureId = "failure-1",
            SourceFailureCode = MauiFlowFailureClasses.LocatorNotFound,
            PriorFingerprint = fingerprint,
            PriorActiveSelectorResolution = new MauiRepairPriorSelectorResolution
            {
                RunId = "prior-run",
                TrustedRun = true,
                WasUniquelyResolved = true,
                ActiveSelector = new FlowSelector { AutomationId = "old-save" },
                Fingerprint = fingerprint,
            },
            SelectorHealthCandidates =
            [
                new MauiSelectorCandidate
                {
                    CandidateId = "candidate-new-save",
                    Rank = 1,
                    Priority = 1,
                    Selector = new FlowSelector { AutomationId = "new-save" },
                    SelectorDescriptor = new MauiSelectorCandidateSelector { Kind = "automation-id", AutomationId = "new-save" },
                    Score = .9,
                    Scores = new MauiSelectorCandidateScores { DeterministicRankScore = .9 },
                    Unique = true,
                    Validation = new MauiSelectorCandidateValidation { Unique = true, MatchCount = 1, Accepted = true, PlatformState = "validated" },
                    Fingerprint = fingerprint,
                },
            ],
            CurrentResolutions =
            [
                new MauiRepairCandidateResolution
                {
                    CandidateId = "candidate-new-save",
                    MatchCount = 1,
                    SemanticFingerprintMatches = true,
                    CurrentFingerprint = fingerprint,
                },
            ],
            Trust = "current-local-run",
        });
        return Assert.Single(generated.Proposals);
    }

    private static MauiFlow Flow() => new()
    {
        Name = "repair",
        ExtensionData = new Dictionary<string, JsonElement>
        {
            ["customReviewerMetadata"] = JsonSerializer.SerializeToElement("preserve-me"),
            ["flowId"] = JsonSerializer.SerializeToElement("flow-repair"),
            ["revision"] = JsonSerializer.SerializeToElement(1),
        },
        Steps =
        [
            new FlowStep
            {
                Seq = 1,
                StepId = "stable-save-step",
                Action = FlowActions.Tap,
                Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "old-save" } },
                Value = "CorrectHorseBatteryStaple",
                Asserts =
                [
                    new FlowAssert
                    {
                        Kind = "exists",
                        Verify = true,
                        Selector = new FlowSelector { AutomationId = "old-save" },
                    },
                ],
            },
        ],
    };

    private static MauiElementFingerprint Fingerprint() => new()
    {
        FingerprintId = "fp",
        Context = new MauiElementFingerprintContext
        {
            AppId = "com.example.app",
            AppBuild = "build-1",
            Platform = "android",
            Route = "/checkout",
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
        },
        Topology = new MauiTopologySignature { AncestorHash = "ancestor", SiblingHash = "sibling" },
    };

    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public AdjustableTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan elapsed) => _now = _now.Add(elapsed);
    }
}
