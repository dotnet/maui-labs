namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// Explicit compatibility adapter between the legacy replay response and flow-run-report-v1.
/// It preserves legacy <see cref="FlowFailureKinds"/> strings while the structured report carries
/// the typed failure code.
/// </summary>
public static class FlowReplayReportAdapter
{
    public static FlowReplayReport ToLegacy(MauiFlowRunReport report, string? name = null, string? file = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var result = new FlowReplayReport
        {
            Ok = string.Equals(report.Outcome?.Status, MauiFlowRunOutcomes.Passed, StringComparison.Ordinal),
            Name = name ?? report.LegacyFlowIdentity ?? report.FlowId ?? "scenario",
            File = file,
            Total = report.Steps.Count,
            DivergencePoint = FindStepSequence(report, report.DivergenceStepId),
            StoppedEarly = !string.Equals(report.Outcome?.Status, MauiFlowRunOutcomes.Passed, StringComparison.Ordinal),
            StructuredReport = report,
            ReportDigest = report.ReportDigest,
            ReportPath = report.ReportPath,
        };

        foreach (var step in report.Steps)
        {
            var passed = string.IsNullOrEmpty(step.FailureClass);
            var legacyStep = new FlowStepResult
            {
                Seq = step.Sequence ?? 0,
                Action = step.Action ?? string.Empty,
                Label = step.Intent ?? step.Action ?? string.Empty,
                Ok = passed,
                FailureKind = ToLegacyFailureKind(step.FailureClass ?? report.Failure?.Class),
                Error = step.TargetResolution?.Message ?? report.Failure?.Message,
                MatchCount = step.CandidateCount,
                CommandId = step.CommandId ?? step.Dispatch?.CommandId,
                ActionDigest = step.ActionDigest ?? step.Dispatch?.ActionDigest,
                AuthorityEpoch = step.AuthorityEpoch ?? step.Dispatch?.AuthorityEpoch,
                AcknowledgementState = step.AcknowledgementState ?? step.Dispatch?.AcknowledgementState,
            };
            legacyStep.Asserts.AddRange(step.Assertions.Select(assertion => new FlowAssertResult
            {
                Kind = assertion.Kind ?? string.Empty,
                Ok = assertion.Passed,
                Skipped = assertion.Skipped ?? false,
                Expected = assertion.Expected,
                Actual = assertion.Actual,
                TargetStatus = assertion.TargetResolution?.Status,
                TargetMatchCount = assertion.TargetResolution?.MatchCount,
            }));
            result.Results.Add(legacyStep);
            if (passed)
                result.Passed++;
            else
                result.Failed++;
        }

        if (!result.Ok && result.Results.Count == 0)
        {
            result.Results.Add(new FlowStepResult
            {
                Seq = TryParseStepSequence(report.DivergenceStepId) ?? 0,
                Action = "run",
                Label = "Prepare run",
                Ok = false,
                FailureKind = ToLegacyFailureKind(report.Failure?.Class ?? report.Failure?.Code),
                Error = report.Failure?.Message ??
                    report.Outcome?.Summary ??
                    "The flow run failed before the first step.",
            });
            result.Total = Math.Max(result.Total, 1);
            result.Failed = 1;
        }
        return result;
    }

    public static string? ToLegacyFailureKind(string? failureClass) => failureClass switch
    {
        MauiFlowFailureClasses.FlowInvalid => FlowFailureKinds.Validation,
        MauiFlowFailureClasses.AssertionFailed => FlowFailureKinds.Assertion,
        MauiFlowFailureClasses.LocatorAmbiguous => FlowFailureKinds.Ambiguous,
        MauiFlowFailureClasses.LocatorNotFound => FlowFailureKinds.NotFound,
        MauiFlowFailureClasses.NotVisible => FlowFailureKinds.NotVisible,
        MauiFlowFailureClasses.Disabled => FlowFailureKinds.Disabled,
        MauiFlowFailureClasses.UnstableBounds => FlowFailureKinds.Unstable,
        MauiFlowFailureClasses.SecretUnavailable => FlowFailureKinds.SecretRequired,
        MauiFlowFailureClasses.UnsafeValue => FlowFailureKinds.UnsafeValue,
        MauiFlowFailureClasses.UnknownCompletion => FlowFailureKinds.UnknownCompletion,
        MauiFlowFailureClasses.WorkflowCommandConflict => FlowFailureKinds.WorkflowCommandConflict,
        MauiFlowFailureClasses.DriveFailed or
        MauiFlowFailureClasses.ActionRejected or
        MauiFlowFailureClasses.Transport or
        MauiFlowFailureClasses.AgentDisconnected or
        MauiFlowFailureClasses.Infrastructure => FlowFailureKinds.Drive,
        null or "" => null,
        _ => FlowFailureKinds.Drive,
    };

    private static int? TryParseStepSequence(string? stepId)
    {
        if (string.IsNullOrWhiteSpace(stepId))
            return null;
        var value = stepId.StartsWith("step-", StringComparison.Ordinal)
            ? stepId[5..]
            : stepId;
        return int.TryParse(value, out var sequence) ? sequence : null;
    }

    private static int? FindStepSequence(MauiFlowRunReport report, string? stepId)
        => report.Steps.FirstOrDefault(step =>
                string.Equals(step.StepId, stepId, StringComparison.Ordinal))?.Sequence
            ?? TryParseStepSequence(stepId);
}
