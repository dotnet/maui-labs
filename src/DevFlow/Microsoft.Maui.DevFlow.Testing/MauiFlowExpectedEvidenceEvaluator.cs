namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// Checks whether a run produced the evidence its flow declared it would produce.
/// <para>
/// This is a pure, idempotent projection over the flow and the finished report, so the runner, the
/// CLI, and any later re-analysis of a persisted report all reach the same answer. It deliberately
/// verifies <em>collection only</em>: that an artifact category exists for this run. It never
/// compares an artifact against a stored baseline and never diffs pixels. Most declarations are
/// observational; <c>device-recording</c> is the explicit opt-in exception and is admitted only
/// after the host surfaces its device effect for human review.
/// </para>
/// </summary>
public static class MauiFlowExpectedEvidenceEvaluator
{
    /// <summary>Artifact kinds that satisfy a <c>failure-evidence</c> expectation.</summary>
    private static readonly HashSet<string> FailureEvidenceArtifactKinds = new(StringComparer.Ordinal)
    {
        "mauitrace",
        MauiFlowEvidenceKinds.FailureEvidence,
    };

    /// <summary>
    /// Evidence that DevFlow collects only when a run fails. Declaring one of these on a flow says
    /// "if this test fails I expect to be able to see it", so a passing run records
    /// <see cref="MauiFlowEvidenceExpectationStates.NotApplicable"/> rather than a false miss.
    /// </summary>
    private static readonly HashSet<string> FailureScopedKinds = new(StringComparer.Ordinal)
    {
        MauiFlowEvidenceKinds.Screenshot,
        MauiFlowEvidenceKinds.VisualTree,
        MauiFlowEvidenceKinds.Logs,
        MauiFlowEvidenceKinds.FailureEvidence,
    };

    /// <summary>
    /// Evaluates every declaration on the flow against the finished report. Returns
    /// <see langword="null"/> when the flow declared nothing, so a flow that predates this feature
    /// produces byte-identical reports.
    /// </summary>
    /// <param name="producedEvidenceKinds">
    /// The evidence kinds the host's capture actually wrote for this run. An empty or null set
    /// means the host collected nothing, not that it collected everything.
    /// </param>
    public static MauiFlowExpectedEvidenceReport? Evaluate(
        MauiFlow? flow,
        MauiFlowRunReport? report,
        IReadOnlyCollection<string>? producedEvidenceKinds = null)
    {
        var declarations = Collect(flow);
        if (declarations.Count == 0)
            return null;

        var produced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kind in producedEvidenceKinds ?? [])
        {
            if (!string.IsNullOrWhiteSpace(kind))
                produced.Add(kind.Trim().ToLowerInvariant());
        }

        var passed = string.Equals(report?.Outcome?.Status, MauiFlowRunOutcomes.Passed, StringComparison.Ordinal);
        var checks = declarations
            .Select(declaration => Check(declaration, report, produced, passed))
            .ToList();

        return new MauiFlowExpectedEvidenceReport
        {
            Declared = checks.Count,
            Satisfied = checks.Count(check =>
                check.State == MauiFlowEvidenceExpectationStates.Satisfied),
            Unsatisfied = checks.Count(check =>
                check.State == MauiFlowEvidenceExpectationStates.Unsatisfied),
            NotApplicable = checks.Count(check =>
                check.State == MauiFlowEvidenceExpectationStates.NotApplicable),
            AllSatisfied = !checks.Any(check =>
                check.State == MauiFlowEvidenceExpectationStates.Unsatisfied),
            Checks = checks,
        };
    }

    private readonly record struct Declaration(
        string Kind,
        string Scope,
        string? StepId,
        string? Reference,
        string? Id);

    private static List<Declaration> Collect(MauiFlow? flow)
    {
        var declarations = new List<Declaration>();
        if (flow is null)
            return declarations;

        foreach (var declared in flow.ExpectedEvidence ?? [])
        {
            if (Normalize(declared) is { } kind)
                declarations.Add(new Declaration(kind, MauiFlowEvidenceExpectationScopes.Flow, null, declared.Reference, declared.Id));
        }

        var ordinal = 0;
        foreach (var step in flow.Steps ?? [])
        {
            ordinal++;
            if (step is null)
                continue;
            var stepId = StepIdentity(step, ordinal);
            foreach (var declared in step.ExpectedEvidence ?? [])
            {
                if (Normalize(declared) is { } kind)
                    declarations.Add(new Declaration(kind, MauiFlowEvidenceExpectationScopes.Step, stepId, declared.Reference, declared.Id));
            }

            // Legacy shorthand: a step that names a screenshot is declaring that the run should
            // produce one. Reading it here is what stops the field from being inert.
            if (!string.IsNullOrWhiteSpace(step.Screenshot) &&
                !declarations.Any(existing =>
                    existing.Kind == MauiFlowEvidenceKinds.Screenshot &&
                    existing.Scope == MauiFlowEvidenceExpectationScopes.Step &&
                    string.Equals(existing.StepId, stepId, StringComparison.Ordinal)))
            {
                declarations.Add(new Declaration(
                    MauiFlowEvidenceKinds.Screenshot,
                    MauiFlowEvidenceExpectationScopes.Step,
                    stepId,
                    Reference: null,
                    Id: null));
            }
        }
        return declarations;
    }

    private static string? Normalize(FlowExpectedEvidence? declared)
    {
        if (declared is null || !MauiFlowEvidenceKinds.IsKnown(declared.Kind))
            return null;
        return declared.Kind.Trim().ToLowerInvariant();
    }

    private static string StepIdentity(FlowStep step, int ordinal)
        => string.IsNullOrWhiteSpace(step.StepId)
            ? (step.Seq > 0 ? step.Seq : ordinal).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : step.StepId!;

    private static MauiFlowExpectedEvidenceCheck Check(
        Declaration declaration,
        MauiFlowRunReport? report,
        HashSet<string> produced,
        bool passed)
    {
        var check = new MauiFlowExpectedEvidenceCheck
        {
            ExpectationId = declaration.Id,
            Kind = declaration.Kind,
            Scope = declaration.Scope,
            StepId = declaration.StepId,
            Reference = declaration.Reference,
        };

        if (declaration.Kind == MauiFlowEvidenceKinds.RunReport)
        {
            check.State = MauiFlowEvidenceExpectationStates.Satisfied;
            check.Reason = "The run report was produced.";
            return check;
        }

        if (declaration.Kind == MauiFlowEvidenceKinds.BusinessOracle)
        {
            // A declaration with no reference names nothing, so nothing can satisfy it. Without
            // this guard it would match an oracle whose own id was also absent.
            var matched = !string.IsNullOrWhiteSpace(declaration.Reference) &&
                (report?.BusinessOracles ?? []).Any(result =>
                    string.Equals(result?.OracleId, declaration.Reference, StringComparison.Ordinal));
            check.State = matched
                ? MauiFlowEvidenceExpectationStates.Satisfied
                : MauiFlowEvidenceExpectationStates.Unsatisfied;
            check.Reason = matched
                ? "An independent business-oracle result was recorded for this reference."
                : "No independent business-oracle result was recorded for this reference.";
            return check;
        }

        if (passed && FailureScopedKinds.Contains(declaration.Kind))
        {
            check.State = MauiFlowEvidenceExpectationStates.NotApplicable;
            check.Reason = "The run passed, so failure evidence was not collected.";
            return check;
        }

        if (declaration.Kind == MauiFlowEvidenceKinds.FailureEvidence)
        {
            var bundled = (report?.Artifacts ?? []).Any(artifact =>
                artifact?.Kind is { } kind && FailureEvidenceArtifactKinds.Contains(kind));
            check.State = bundled
                ? MauiFlowEvidenceExpectationStates.Satisfied
                : MauiFlowEvidenceExpectationStates.Unsatisfied;
            check.Reason = bundled
                ? "A redacted failure-evidence bundle was recorded for this run."
                : "The run did not pass and no failure-evidence bundle was recorded.";
            return check;
        }

        var collected = produced.Contains(declaration.Kind);
        check.State = collected
            ? MauiFlowEvidenceExpectationStates.Satisfied
            : MauiFlowEvidenceExpectationStates.Unsatisfied;
        check.Reason = collected
            ? "The run collected this evidence."
            : declaration.Kind == MauiFlowEvidenceKinds.DeviceRecording
                ? "The run did not produce the explicitly approved device recording."
                : "The run did not collect this evidence; check how the host was configured to capture failure evidence.";
        return check;
    }
}
