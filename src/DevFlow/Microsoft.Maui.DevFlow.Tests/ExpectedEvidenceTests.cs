using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public class ExpectedEvidenceTests
{
    [Fact]
    public void Evaluate_FlowDeclaringNothing_ProducesNoBlockAtAll()
    {
        var flow = new MauiFlow { Name = "no declarations", Steps = [Step(1)] };

        Assert.Null(MauiFlowExpectedEvidenceEvaluator.Evaluate(flow, FailedReport()));
    }

    [Fact]
    public void Evaluate_RunReportExpectation_IsAlwaysSatisfied()
    {
        var flow = new MauiFlow
        {
            Name = "report",
            Steps = [Step(1)],
            ExpectedEvidence = [new FlowExpectedEvidence { Kind = MauiFlowEvidenceKinds.RunReport }],
        };

        var evaluated = MauiFlowExpectedEvidenceEvaluator.Evaluate(flow, FailedReport());

        var check = Assert.Single(evaluated!.Checks);
        Assert.Equal(MauiFlowEvidenceExpectationStates.Satisfied, check.State);
        Assert.Equal(MauiFlowEvidenceExpectationScopes.Flow, check.Scope);
        Assert.True(evaluated.AllSatisfied);
    }

    [Fact]
    public void Evaluate_FailureScopedExpectationOnAPassingRun_IsNotApplicableNotAMiss()
    {
        var flow = new MauiFlow
        {
            Name = "screenshot",
            Steps = [Step(1)],
            ExpectedEvidence = [new FlowExpectedEvidence { Kind = MauiFlowEvidenceKinds.Screenshot }],
        };

        var evaluated = MauiFlowExpectedEvidenceEvaluator.Evaluate(flow, PassedReport());

        var check = Assert.Single(evaluated!.Checks);
        Assert.Equal(MauiFlowEvidenceExpectationStates.NotApplicable, check.State);
        Assert.Equal(1, evaluated.NotApplicable);
        Assert.Equal(0, evaluated.Unsatisfied);
        Assert.True(evaluated.AllSatisfied);
    }

    [Fact]
    public void Evaluate_DeclaredEvidenceTheRunNeverCollected_IsUnsatisfied()
    {
        var flow = new MauiFlow
        {
            Name = "tree",
            Steps = [Step(1)],
            ExpectedEvidence = [new FlowExpectedEvidence { Kind = MauiFlowEvidenceKinds.VisualTree }],
        };

        var evaluated = MauiFlowExpectedEvidenceEvaluator.Evaluate(flow, FailedReport(), []);

        var check = Assert.Single(evaluated!.Checks);
        Assert.Equal(MauiFlowEvidenceExpectationStates.Unsatisfied, check.State);
        Assert.False(evaluated.AllSatisfied);
    }

    [Fact]
    public void Evaluate_DeclaredEvidenceTheRunCollected_IsSatisfied()
    {
        var flow = new MauiFlow
        {
            Name = "tree",
            Steps = [Step(1)],
            ExpectedEvidence = [new FlowExpectedEvidence { Kind = MauiFlowEvidenceKinds.VisualTree }],
        };

        var evaluated = MauiFlowExpectedEvidenceEvaluator.Evaluate(
            flow,
            FailedReport(),
            [MauiFlowEvidenceKinds.VisualTree]);

        Assert.Equal(MauiFlowEvidenceExpectationStates.Satisfied, Assert.Single(evaluated!.Checks).State);
    }

    [Fact]
    public void Evaluate_DeviceRecordingIsRunScopedAndMustBeProducedEvenWhenTheRunPasses()
    {
        var flow = new MauiFlow
        {
            Name = "device recording",
            Steps = [Step(1)],
            ExpectedEvidence = [new FlowExpectedEvidence { Kind = MauiFlowEvidenceKinds.DeviceRecording }],
        };

        var missing = MauiFlowExpectedEvidenceEvaluator.Evaluate(flow, PassedReport(), []);
        var collected = MauiFlowExpectedEvidenceEvaluator.Evaluate(
            flow,
            PassedReport(),
            [MauiFlowEvidenceKinds.DeviceRecording]);

        Assert.Equal(MauiFlowEvidenceExpectationStates.Unsatisfied, Assert.Single(missing!.Checks).State);
        Assert.Equal(MauiFlowEvidenceExpectationStates.Satisfied, Assert.Single(collected!.Checks).State);
    }

    [Fact]
    public void Evaluate_BusinessOracleExpectation_MatchesOnTheOracleId()
    {
        var flow = new MauiFlow
        {
            Name = "oracle",
            Steps = [Step(1)],
            ExpectedEvidence =
            [
                new FlowExpectedEvidence
                {
                    Kind = MauiFlowEvidenceKinds.BusinessOracle,
                    Reference = "todo-persisted",
                },
            ],
        };
        var report = PassedReport();
        report.BusinessOracles =
        [
            new MauiIndependentBusinessOracleResult { OracleId = "todo-persisted" },
        ];

        var satisfied = MauiFlowExpectedEvidenceEvaluator.Evaluate(flow, report);
        Assert.Equal(MauiFlowEvidenceExpectationStates.Satisfied, Assert.Single(satisfied!.Checks).State);

        report.BusinessOracles = [];
        var missing = MauiFlowExpectedEvidenceEvaluator.Evaluate(flow, report);
        Assert.Equal(MauiFlowEvidenceExpectationStates.Unsatisfied, Assert.Single(missing!.Checks).State);
    }

    [Fact]
    public void Evaluate_LegacyStepScreenshotField_IsReadAsAScreenshotExpectation()
    {
        var step = Step(1);
        step.StepId = "step-1";
        step.Screenshot = "after-add.png";
        var flow = new MauiFlow { Name = "legacy", Steps = [step] };

        var evaluated = MauiFlowExpectedEvidenceEvaluator.Evaluate(flow, FailedReport(), []);

        var check = Assert.Single(evaluated!.Checks);
        Assert.Equal(MauiFlowEvidenceKinds.Screenshot, check.Kind);
        Assert.Equal(MauiFlowEvidenceExpectationScopes.Step, check.Scope);
        Assert.Equal("step-1", check.StepId);
        Assert.Equal(MauiFlowEvidenceExpectationStates.Unsatisfied, check.State);
    }

    [Fact]
    public void Evaluate_LegacyScreenshotBesideAnExplicitDeclaration_IsNotCountedTwice()
    {
        var step = Step(1);
        step.StepId = "step-1";
        step.Screenshot = "after-add.png";
        step.ExpectedEvidence = [new FlowExpectedEvidence { Kind = MauiFlowEvidenceKinds.Screenshot }];
        var flow = new MauiFlow { Name = "legacy", Steps = [step] };

        var evaluated = MauiFlowExpectedEvidenceEvaluator.Evaluate(flow, FailedReport(), []);

        Assert.Equal(1, evaluated!.Declared);
    }

    [Fact]
    public void Evaluate_FailureEvidenceExpectation_IsSatisfiedByARedactedBundle()
    {
        var flow = new MauiFlow
        {
            Name = "bundle",
            Steps = [Step(1)],
            ExpectedEvidence = [new FlowExpectedEvidence { Kind = MauiFlowEvidenceKinds.FailureEvidence }],
        };
        var report = FailedReport();
        report.Artifacts.Add(new MauiFlowArtifactReference
        {
            ArtifactId = "evidence-1",
            Kind = "mauitrace",
            Path = "evidence.mauitrace",
        });

        var evaluated = MauiFlowExpectedEvidenceEvaluator.Evaluate(flow, report, []);

        Assert.Equal(MauiFlowEvidenceExpectationStates.Satisfied, Assert.Single(evaluated!.Checks).State);
    }

    [Fact]
    public void Evaluate_IsIdempotent()
    {
        var flow = new MauiFlow
        {
            Name = "twice",
            Steps = [Step(1)],
            ExpectedEvidence = [new FlowExpectedEvidence { Kind = MauiFlowEvidenceKinds.RunReport }],
        };
        var report = FailedReport();

        var first = MauiFlowExpectedEvidenceEvaluator.Evaluate(flow, report);
        var second = MauiFlowExpectedEvidenceEvaluator.Evaluate(flow, report);

        Assert.Equal(first!.Declared, second!.Declared);
        Assert.Equal(first.Satisfied, second.Satisfied);
    }

    [Fact]
    public void Validator_RejectsAnUnknownEvidenceKind()
    {
        var flow = new MauiFlow
        {
            Name = "unknown kind",
            Steps = [Step(1)],
            ExpectedEvidence = [new FlowExpectedEvidence { Kind = "pixel-perfect" }],
        };

        var result = FlowValidator.Validate(flow);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("expectedEvidence", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsABusinessOracleExpectationWithoutAReference()
    {
        var flow = new MauiFlow
        {
            Name = "oracle without reference",
            Steps = [Step(1)],
            ExpectedEvidence = [new FlowExpectedEvidence { Kind = MauiFlowEvidenceKinds.BusinessOracle }],
        };

        var result = FlowValidator.Validate(flow);

        Assert.False(result.Ok);
    }

    [Fact]
    public void Validator_RejectsDuplicateExpectationIds()
    {
        var flow = new MauiFlow
        {
            Name = "duplicates",
            Steps = [Step(1)],
            ExpectedEvidence =
            [
                new FlowExpectedEvidence { Id = "shot", Kind = MauiFlowEvidenceKinds.Screenshot },
                new FlowExpectedEvidence { Id = "shot", Kind = MauiFlowEvidenceKinds.VisualTree },
            ],
        };

        var result = FlowValidator.Validate(flow);

        Assert.False(result.Ok);
    }

    [Fact]
    public void Validator_AcceptsAFlowThatDeclaresNothing()
    {
        var flow = new MauiFlow { Name = "plain", Steps = [Step(1)] };

        Assert.True(FlowValidator.Validate(flow).Ok);
    }

    [Fact]
    public void Clone_CopiesDeclarationsWithoutSharingInstances()
    {
        var step = Step(1);
        step.ExpectedEvidence = [new FlowExpectedEvidence { Kind = MauiFlowEvidenceKinds.Logs }];
        var flow = new MauiFlow
        {
            Name = "clone",
            Steps = [step],
            ExpectedEvidence = [new FlowExpectedEvidence { Kind = MauiFlowEvidenceKinds.RunReport }],
        };

        var clone = MauiFlowClone.Clone(flow);

        Assert.NotNull(clone.ExpectedEvidence);
        Assert.Equal(MauiFlowEvidenceKinds.RunReport, clone.ExpectedEvidence![0].Kind);
        Assert.NotSame(flow.ExpectedEvidence![0], clone.ExpectedEvidence[0]);
        Assert.Equal(MauiFlowEvidenceKinds.Logs, clone.Steps[0].ExpectedEvidence![0].Kind);
        Assert.NotSame(flow.Steps[0].ExpectedEvidence![0], clone.Steps[0].ExpectedEvidence![0]);
    }

    private static FlowStep Step(int seq) => new()
    {
        Seq = seq,
        Action = FlowActions.Tap,
        Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "AddButton" } },
    };

    private static MauiFlowRunReport PassedReport() => new()
    {
        RunId = "run-pass",
        Outcome = new MauiFlowRunOutcome { Status = MauiFlowRunOutcomes.Passed, Terminal = true },
    };

    private static MauiFlowRunReport FailedReport() => new()
    {
        RunId = "run-fail",
        Outcome = new MauiFlowRunOutcome { Status = MauiFlowRunOutcomes.Failed, Terminal = true },
    };

    /// <summary>
    /// Trimming an over-long check list must never let the report claim more coverage than it
    /// carries. Dropping the unsatisfied tail and then reporting <c>allSatisfied</c> would turn a
    /// size limit into a false pass.
    /// </summary>
    [Fact]
    public void ApplyLimits_MoreChecksThanTheReportCanCarry_NeverClaimsEverythingWasSatisfied()
    {
        var report = FailedReport();
        var checks = new List<MauiFlowExpectedEvidenceCheck>();
        for (var index = 0; index < 70; index++)
        {
            checks.Add(new MauiFlowExpectedEvidenceCheck
            {
                ExpectationId = "expectation-" + index,
                Kind = MauiFlowEvidenceKinds.RunReport,
                Scope = MauiFlowEvidenceExpectationScopes.Flow,
                State = index < 64
                    ? MauiFlowEvidenceExpectationStates.Satisfied
                    : MauiFlowEvidenceExpectationStates.Unsatisfied,
            });
        }
        report.ExpectedEvidence = new MauiFlowExpectedEvidenceReport
        {
            Declared = checks.Count,
            Satisfied = 64,
            Unsatisfied = 6,
            AllSatisfied = false,
            Checks = checks,
        };

        MauiFlowRunReportSerializer.ApplyLimits(report, new MauiFlowRunReportLimits());

        var evidence = report.ExpectedEvidence!;
        Assert.False(evidence.AllSatisfied);
        Assert.Equal(70, evidence.Declared);
        Assert.True(evidence.Checks.Count < evidence.Declared);
        Assert.Contains(report.Omissions, item => item.Kind == "expected-evidence-checks");
    }

    /// <summary>
    /// A business-oracle declaration names the oracle it expects. One that names nothing cannot be
    /// satisfied by an oracle result that also happens to be unnamed.
    /// </summary>
    [Fact]
    public void Evaluate_BusinessOracleWithNoReference_IsNeverSatisfiedByAnUnnamedOracle()
    {
        var flow = new MauiFlow
        {
            Name = "flow",
            ExpectedEvidence = [new FlowExpectedEvidence { Kind = MauiFlowEvidenceKinds.BusinessOracle }],
        };
        var report = PassedReport();
        report.BusinessOracles = [new MauiIndependentBusinessOracleResult { Succeeded = true }];

        var evaluated = MauiFlowExpectedEvidenceEvaluator.Evaluate(flow, report, null);

        var check = Assert.Single(evaluated!.Checks);
        Assert.Equal(MauiFlowEvidenceExpectationStates.Unsatisfied, check.State);
    }

    /// <summary>
    /// Expectation ids and oracle references are author-chosen prose. Redaction may rewrite them to
    /// a stable digest, but it must not drop them: a check nobody can correlate back to the
    /// declaration that produced it is not reviewable evidence.
    /// </summary>
    [Fact]
    public void ApplyLimits_ExpectationIdContainingSpaces_StaysCorrelatable()
    {
        var report = FailedReport();
        report.ExpectedEvidence = new MauiFlowExpectedEvidenceReport
        {
            Declared = 1,
            Checks =
            [
                new MauiFlowExpectedEvidenceCheck
                {
                    ExpectationId = "ledger record written",
                    Kind = MauiFlowEvidenceKinds.BusinessOracle,
                    Scope = MauiFlowEvidenceExpectationScopes.Flow,
                    Reference = "todo ledger record",
                    State = MauiFlowEvidenceExpectationStates.Satisfied,
                },
            ],
        };

        MauiFlowRunReportSerializer.ApplyLimits(report, new MauiFlowRunReportLimits());

        var check = Assert.Single(report.ExpectedEvidence!.Checks);
        Assert.NotNull(check.ExpectationId);
        Assert.NotNull(check.Reference);
        Assert.NotEqual(check.ExpectationId, check.Reference);
    }
}
