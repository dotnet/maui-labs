using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>Limits applied before a report is serialized or persisted.</summary>
public sealed class MauiFlowRunReportLimits
{
    public int MaxEvents { get; set; } = 128;
    public int MaxSteps { get; set; } = 2_000;
    public int MaxActionabilityAttemptsPerStep { get; set; } = 64;
    public int MaxAssertionsPerStep { get; set; } = 64;
    public int MaxSelectorCandidatesPerStep { get; set; } = 8;
    public int MaxSelectorCandidateOmissionsPerStep { get; set; } = 16;
    public int MaxArtifacts { get; set; } = 64;
    public int MaxTextLength { get; set; } = 512;
    public int MaxJsonBytes { get; set; } = 1_048_576;
}

/// <summary>The outcome of an atomic report write.</summary>
public sealed class MauiFlowRunReportWriteResult
{
    public bool Ok { get; init; }
    public string? Path { get; init; }
    public string? Digest { get; init; }
    public long Bytes { get; init; }
    public string? Error { get; init; }
}

/// <summary>A small schema-shaped validation result for reports produced without a JSON-schema runtime.</summary>
public sealed class MauiFlowRunReportValidation
{
    public List<string> Errors { get; } = [];
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Source-generated serialization, deterministic digests, bounds, and atomic persistence for
/// <see cref="MauiFlowRunReport"/>. The persisted document follows flow-run-report-v1.
/// </summary>
public static class MauiFlowRunReportSerializer
{
    public const string FileName = "flow-run.json";

    /// <summary>Lines of device crash output the report will carry, after redaction.</summary>
    private const int MaxCrashExcerptLines = 12;

    /// <summary>Declared-evidence checks the report will carry.</summary>
    private const int MaxExpectedEvidenceChecks = 64;

    public static byte[] SerializeToUtf8Bytes(MauiFlowRunReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ApplyLimits(report, new MauiFlowRunReportLimits());
        return JsonSerializer.SerializeToUtf8Bytes(report, MauiTestingJsonContext.Default.MauiFlowRunReport);
    }

    public static string ComputeDigest(MauiFlowRunReport report)
        => Convert.ToHexString(SHA256.HashData(SerializeToUtf8Bytes(report))).ToLowerInvariant();

    public static string ComputeFlowDigest(MauiFlow flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        var element = JsonSerializer.SerializeToElement(flow, MauiFlowJsonContext.Default.MauiFlow);
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        WriteCanonicalJson(writer, element);
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    /// <summary>
    /// Projects secondary failures to identifier-only values in a deterministic order and caps the
    /// retained count.
    /// </summary>
    /// <remarks>
    /// Every producer and consumer of the two-axis outcome goes through this one function, so the
    /// flow-run report, the execution manifest, and the runner's own exit-category composition can
    /// never disagree about what survived redaction. One entry is retained per phase: an artifact
    /// that claims many codes for one phase must not be able to evict a different phase's real
    /// failure through the cap. Which code survives for a repeated phase is the first one listed:
    /// the host appends its own observations in the order it made them, and for an artifact that
    /// repeats a phase no listed code is more truthful than another. The two phases this producer
    /// can actually observe are ordered ahead of everything else for a related reason: phases are
    /// deliberately open on the read path, so an artifact that invents four alphabetically earlier
    /// ones would otherwise push a real <c>cleanup</c> or <c>artifact-cleanup</c> fact out through
    /// the cap and publish a host that never tore down as clean. <c>class</c> is restated rather
    /// than copied because a cleanup failure is observed after the run reached its own terminal
    /// state and is therefore infrastructure by construction, whatever the artifact says.
    /// </remarks>
    public static List<MauiFlowSecondaryFailure> NormalizeSecondaryFailures(
        IEnumerable<MauiFlowSecondaryFailure>? failures)
        => (failures ?? [])
            .Where(static failure => failure is not null)
            .Select(static failure => new MauiFlowSecondaryFailure
            {
                Phase = MauiFlowReportRedactor.SafeIdentifier(failure.Phase),
                Code = MauiFlowReportRedactor.SafeIdentifier(failure.Code),
                Class = MauiFlowFailureClasses.Infrastructure,
                Retryable = failure.Retryable,
            })
            .Where(static failure =>
                !string.IsNullOrWhiteSpace(failure.Phase) &&
                !string.IsNullOrWhiteSpace(failure.Code))
            .DistinctBy(static failure => failure.Phase, StringComparer.Ordinal)
            .OrderBy(static failure => KnownSecondaryPhaseRank(failure.Phase))
            .ThenBy(static failure => failure.Phase, StringComparer.Ordinal)
            .Take(MauiFlowSecondaryFailurePhases.MaxRetained)
            .ToList();

    /// <summary>
    /// Sorts the owned cleanup phases this producer can observe ahead of any phase an imported
    /// artifact invented, so the cap can only ever drop the invented ones.
    /// </summary>
    private static int KnownSecondaryPhaseRank(string? phase)
        => phase is MauiFlowSecondaryFailurePhases.Cleanup or
                    MauiFlowSecondaryFailurePhases.ArtifactCleanup
            ? 0
            : 1;

    /// <summary>Writes <c>&lt;artifactRoot&gt;/&lt;runId&gt;/flow-run.json</c> atomically.</summary>
    public static MauiFlowRunReportWriteResult WriteAtomic(
        MauiFlowRunReport report,
        string artifactRoot,
        MauiFlowRunReportLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (string.IsNullOrWhiteSpace(artifactRoot))
            throw new ArgumentException("An artifact root is required.", nameof(artifactRoot));

        var runId = MauiFlowReportRedactor.SafeFileSegment(report.RunId) ?? "run";
        string root;
        try
        {
            root = Path.GetFullPath(artifactRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new MauiFlowRunReportWriteResult { Error = "The artifact root is not a valid path." };
        }

        var target = Path.Combine(root, runId, FileName);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            ApplyLimits(report, limits ?? new MauiFlowRunReportLimits());
            report.ReportPath = target;
            var identityDigest = ComputeDigest(report);
            // This is a stable report identity computed before its self-reference is added. A
            // literal SHA-256 of a JSON document cannot contain its own final SHA-256 value.
            report.ReportDigest = identityDigest;
            if (!report.Artifacts.Any(static artifact =>
                    string.Equals(artifact.Kind, "flow-run-report", StringComparison.Ordinal)))
            {
                report.Artifacts.Add(new MauiFlowArtifactReference
                {
                    ArtifactId = $"flow-run-{runId}",
                    Kind = "flow-run-report",
                    Path = target,
                    Digest = identityDigest,
                    MediaType = "application/json",
                    Redacted = true,
                    CreatedAt = report.EndedAt ?? DateTimeOffset.UtcNow,
                });
            }

            ApplyLimits(report, limits ?? new MauiFlowRunReportLimits());
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                report,
                MauiTestingJsonContext.Default.MauiFlowRunReport);
            if (bytes.Length > (limits ?? new MauiFlowRunReportLimits()).MaxJsonBytes)
                return new MauiFlowRunReportWriteResult { Error = "The bounded report still exceeds the JSON size limit." };

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, target, overwrite: true);
            report.ReportPath = target;

            return new MauiFlowRunReportWriteResult
            {
                Ok = true,
                Path = target,
                Digest = identityDigest,
                Bytes = bytes.LongLength,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new MauiFlowRunReportWriteResult
            {
                Error = MauiFlowReportRedactor.SafeMessage(ex.Message) ?? "The report could not be written.",
            };
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }

    /// <summary>Validates the required v1 shape before a host publishes a report.</summary>
    public static MauiFlowRunReportValidation Validate(MauiFlowRunReport? report)
    {
        var result = new MauiFlowRunReportValidation();
        if (report is null)
        {
            result.Errors.Add("Report is required.");
            return result;
        }
        if (report.Schema != 1)
            result.Errors.Add("schema must be 1.");
        if (string.IsNullOrWhiteSpace(report.RunId))
            result.Errors.Add("runId is required.");
        if (RequiresFlowDigest(report) && string.IsNullOrWhiteSpace(report.FlowDigest))
            result.Errors.Add("flowDigest is required.");
        if (report.FlowRevision is < 1)
            result.Errors.Add("flowRevision must be greater than or equal to 1.");
        if (report.StartedAt is null)
            result.Errors.Add("startedAt is required.");
        if (report.EndedAt is null)
            result.Errors.Add("endedAt is required.");
        if (report.StartedAt is { } startedAt &&
            report.EndedAt is { } endedAt &&
            endedAt < startedAt)
        {
            result.Errors.Add("endedAt cannot precede startedAt.");
        }
        if (report.Outcome is null)
            result.Errors.Add("outcome is required.");
        if (report.Outcome?.Terminal == true && report.EndedAt is null)
            result.Errors.Add("A terminal report requires endedAt.");
        if (report.Outcome?.Terminal == true && string.IsNullOrWhiteSpace(report.Outcome.Status))
            result.Errors.Add("A terminal report requires an outcome status.");
        if ((report.Events?.Count ?? 0) > new MauiFlowRunReportLimits().MaxEvents)
            result.Errors.Add("events exceeds the v1 bound.");
        if ((report.Steps?.Count ?? 0) > new MauiFlowRunReportLimits().MaxSteps)
            result.Errors.Add("steps exceeds the v1 bound.");
        if ((report.Outcome?.Verified == true || report.Verification?.Verified == true) &&
            report.ReplayEligibility?.RunVerificationAllowed != true)
        {
            result.Errors.Add("A verified run requires successful scenario, acceptance-criterion, and independent-oracle eligibility.");
        }
        if (report.Verification?.Verified == true &&
            !string.Equals(report.Outcome?.Status, MauiFlowRunOutcomes.Passed, StringComparison.Ordinal))
        {
            result.Errors.Add("Independent verification requires a passed execution outcome.");
        }
        if (report.Outcome?.Verified is { } outcomeVerified &&
            report.Verification?.Verified is { } verificationVerified &&
            outcomeVerified != verificationVerified)
        {
            result.Errors.Add("outcome.verified must match verification.verified.");
        }
        if (report.Failure?.RepairEligible == true && report.ReplayEligibility?.RepairEligibility != true)
            result.Errors.Add("A repair-eligible failure requires replay repair eligibility.");
        var passed = string.Equals(
            report.Outcome?.Status,
            MauiFlowRunOutcomes.Passed,
            StringComparison.Ordinal);
        if (passed && report.Failure is not null)
            result.Errors.Add("A passed outcome cannot contain a failure.");
        if (!passed && report.Outcome?.Terminal == true && report.Failure is null)
            result.Errors.Add("A terminal non-passed outcome requires a failure.");
        if (!passed &&
            (report.Outcome?.Verified == true || report.Verification?.Verified == true))
        {
            result.Errors.Add("A non-passed outcome cannot be verified.");
        }
        if (report.Failure?.Retryable is { } reportedRetryable &&
            reportedRetryable != MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
            {
                FailureClass = report.Failure.Class,
                LegacyFailureKind = report.Failure.LegacyKind,
                TerminalOutcome = report.Outcome?.Status,
            }).Retryable)
        {
            result.Errors.Add("failure.retryable must match the canonical failure classification.");
        }
        if (!HasPositiveUniqueSequence((report.Events ?? []).Where(static item => item is not null).Select(static item => item.Sequence)))
            result.Errors.Add("events must use positive unique sequence values when sequence is present.");
        if (!HasPositiveUniqueSequence((report.Steps ?? []).Where(static item => item is not null).Select(static item => item.Sequence)))
            result.Errors.Add("steps must use positive unique sequence values when sequence is present.");
        if (HasContradictoryOracleResults(report.BusinessOracles ?? []))
            result.Errors.Add("businessOracles cannot contain contradictory outcomes for one oracleId.");
        if (report.ReplayEligibility is not null &&
            !string.Equals(report.SideEffectPolicy, report.ReplayEligibility.SideEffectPolicy, StringComparison.Ordinal))
        {
            result.Errors.Add("sideEffectPolicy must match replayEligibility.sideEffectPolicy.");
        }
        return result;
    }

    public static void ApplyLimits(MauiFlowRunReport report, MauiFlowRunReportLimits limits)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(limits);

        limits.MaxEvents = Math.Max(1, limits.MaxEvents);
        limits.MaxSteps = Math.Max(1, limits.MaxSteps);
        limits.MaxActionabilityAttemptsPerStep = Math.Max(1, limits.MaxActionabilityAttemptsPerStep);
        limits.MaxAssertionsPerStep = Math.Max(1, limits.MaxAssertionsPerStep);
        limits.MaxSelectorCandidatesPerStep = Math.Clamp(limits.MaxSelectorCandidatesPerStep, 1, 32);
        limits.MaxSelectorCandidateOmissionsPerStep = Math.Clamp(limits.MaxSelectorCandidateOmissionsPerStep, 1, 64);
        limits.MaxArtifacts = Math.Max(1, limits.MaxArtifacts);
        limits.MaxTextLength = Math.Max(32, limits.MaxTextLength);
        limits.MaxJsonBytes = Math.Max(4_096, limits.MaxJsonBytes);

        SanitizeReport(report, limits.MaxTextLength);
        ClearExtensionData(report);
        Trim(report.Events, limits.MaxEvents, report, "events", "The event limit was reached.");
        Trim(report.Steps, limits.MaxSteps, report, "steps", "The step-attempt limit was reached.");
        Trim(report.Artifacts, limits.MaxArtifacts, report, "artifacts", "The artifact-reference limit was reached.");

        foreach (var step in report.Steps)
        {
            Trim(step.Actionability, limits.MaxActionabilityAttemptsPerStep, report, "actionability",
                "An actionability-attempt limit was reached.");
            Trim(step.Assertions, limits.MaxAssertionsPerStep, report, "assertions",
                "An assertion-result limit was reached.");
            Trim(step.SelectorCandidates, limits.MaxSelectorCandidatesPerStep, report, "selector-candidates",
                "The selector-candidate limit was reached.");
            Trim(step.SelectorCandidateOmissions, limits.MaxSelectorCandidateOmissionsPerStep, report, "selector-candidate-omissions",
                "The selector-candidate-omission limit was reached.");
            Trim(step.Artifacts, limits.MaxArtifacts, report, "step-artifacts",
                "A step artifact-reference limit was reached.");
            BoundStepText(step, limits.MaxTextLength);
        }
        if (report.SelectorHealth is not null)
        {
            report.SelectorHealth.CapturedSteps = report.Steps.Count(step => step.Fingerprint is not null);
            report.SelectorHealth.CandidateCount = report.Steps.Sum(step => step.SelectorCandidates.Count);
            report.SelectorHealth.OmissionCount = report.Steps.Sum(step => step.SelectorCandidateOmissions.Count);
        }

        foreach (var item in report.Events)
            item.Message = MauiFlowReportRedactor.SafeMessage(item.Message, limits.MaxTextLength);
        report.Outcome?.Let(outcome => outcome.Summary = MauiFlowReportRedactor.SafeMessage(outcome.Summary, limits.MaxTextLength));
        report.Outcome?.Let(outcome => outcome.VerificationReason = MauiFlowReportRedactor.SafeMessage(outcome.VerificationReason, limits.MaxTextLength));
        if (report.Failure is not null)
            report.Failure.Message = MauiFlowReportRedactor.SafeMessage(report.Failure.Message, limits.MaxTextLength);
        report.Verification?.Let(value => value.Reason = MauiFlowReportRedactor.SafeMessage(value.Reason, limits.MaxTextLength));
        report.TruncationReason = MauiFlowReportRedactor.SafeMessage(report.TruncationReason, limits.MaxTextLength);

        // The normal cardinality and text caps leave ample headroom. If a caller supplied
        // hostile extension data or unusually large identifiers, degrade to a concise report
        // rather than writing more than the v1 one-megabyte budget.
        if (JsonSerializer.SerializeToUtf8Bytes(report, MauiTestingJsonContext.Default.MauiFlowRunReport).Length >
            limits.MaxJsonBytes)
        {
            report.Events = report.Events.Take(1).ToList();
            foreach (var step in report.Steps)
            {
                step.Actionability = step.Actionability.Take(1).ToList();
                step.Assertions = step.Assertions.Take(1).ToList();
                step.Artifacts = step.Artifacts.Take(1).ToList();
                step.ExtensionData = null;
            }
            ClearExtensionData(report);
            report.Truncated = true;
            AddOmission(report, "report-size", "The report was reduced to satisfy the JSON size limit.", null);
        }

        if (JsonSerializer.SerializeToUtf8Bytes(report, MauiTestingJsonContext.Default.MauiFlowRunReport).Length >
            limits.MaxJsonBytes)
        {
            var retainedStep = report.Steps.FirstOrDefault(step =>
                    string.Equals(step.StepId, report.DivergenceStepId, StringComparison.Ordinal))
                ?? report.Steps.LastOrDefault();
            report.Events.Clear();
            report.Artifacts.Clear();
            report.BusinessOracles.Clear();
            report.Steps = retainedStep is null ? [] : [retainedStep];
            if (retainedStep is not null)
            {
                retainedStep.Intent = null;
                retainedStep.Selector = null;
                retainedStep.SelectorRequest = null;
                retainedStep.CandidateSummary = null;
                retainedStep.TargetResolution = null;
                retainedStep.Actionability.Clear();
                retainedStep.Dispatch = null;
                retainedStep.Assertions.Clear();
                retainedStep.Fingerprint = null;
                retainedStep.SelectorCandidates.Clear();
                retainedStep.SelectorCandidateOmissions.Clear();
                retainedStep.Artifacts.Clear();
                retainedStep.ExtensionData = null;
            }
            report.Reset = null;
            report.Preconditions = null;
            report.Compensator = null;
            // Dropping the replay decision outright orphaned every terminal claim that depends on
            // it: the contract requires a verified run to carry runVerificationAllowed, so a
            // truncated passing run emitted a report its own validator rejects. Retaining the two
            // scalars the remaining claims are checked against costs a handful of bytes and keeps
            // the terminal summary true, while the discarded per-oracle detail correctly leaves
            // repair validation unproven.
            report.ReplayEligibility = report.ReplayEligibility is null
                ? null
                : new MauiFlowReplayEligibilityDecision
                {
                    SideEffectPolicy = report.ReplayEligibility.SideEffectPolicy,
                    RunVerificationAllowed = report.ReplayEligibility.RunVerificationAllowed,
                };
            report.SelectorHealth = null;
            report.Omissions = report.Omissions.Take(8).ToList();
            report.Truncated = true;
            report.TruncationReason = "The report was reduced to its terminal summary to satisfy the JSON size limit.";
            AddOmission(report, "report-size-terminal-summary", "Only the terminal summary was retained.", retainedStep?.Sequence);
            // Repair eligibility is the conjunction of the classifier's verdict and this report's
            // replay eligibility, and the per-oracle repair facts were just discarded. Leaving the
            // stale conjunction in place emits a report that claims a repair-eligible failure with
            // no replay repair eligibility to justify it -- a state the contract validator rejects
            // and a reader would reasonably act on. Re-gating narrows it to false and preserves the
            // classifier's own verdict alongside it.
            MauiFlowFailureClassifier.ApplyRepairEligibilityGate(report);
        }
    }

    private static bool RequiresFlowDigest(MauiFlowRunReport report)
    {
        if ((report.Steps?.Count ?? 0) > 0)
            return true;
        if (report.Target is { } target &&
            new[]
            {
                target.Platform,
                target.AppId,
                target.AppBuildFingerprint,
                target.PackageDigest,
            }.Any(static value => !string.IsNullOrWhiteSpace(value)))
        {
            return true;
        }
        return report.Failure?.Class is not
            MauiFlowFailureClasses.FlowInvalid and not
            MauiFlowFailureClasses.SchemaUnsupported;
    }

    private static bool HasPositiveUniqueSequence(IEnumerable<int?> sequences)
    {
        var seen = new HashSet<int>();
        foreach (var sequence in sequences)
        {
            if (sequence is null)
                continue;
            if (sequence < 1 || !seen.Add(sequence.Value))
                return false;
        }
        return true;
    }

    private static bool HasContradictoryOracleResults(
        IEnumerable<MauiIndependentBusinessOracleResult> results)
        => results
            .Where(static result =>
                result is not null &&
                !string.IsNullOrWhiteSpace(result.OracleId) &&
                result.Succeeded is not null)
            .GroupBy(static result => result.OracleId!, StringComparer.Ordinal)
            .Any(static group =>
                group.Select(static result => result.Succeeded).Distinct().Count() > 1);

    private static void BoundStepText(MauiFlowStepAttempt step, int maxTextLength)
    {
        step.Intent = MauiFlowReportRedactor.SafeMessage(step.Intent, maxTextLength);
        step.TargetResolution?.Let(value =>
        {
            value.Message = MauiFlowReportRedactor.SafeMessage(value.Message, maxTextLength);
            value.FinalResolution = MauiFlowReportRedactor.SafeMessage(value.FinalResolution, maxTextLength);
        });
        foreach (var actionability in step.Actionability)
            actionability.Message = MauiFlowReportRedactor.SafeMessage(actionability.Message, maxTextLength);
        foreach (var assertion in step.Assertions)
        {
            assertion.Message = MauiFlowReportRedactor.SafeMessage(assertion.Message, maxTextLength);
            assertion.TargetResolution?.Let(value =>
            {
                value.Message = MauiFlowReportRedactor.SafeMessage(value.Message, maxTextLength);
                value.FinalResolution = MauiFlowReportRedactor.SafeMessage(value.FinalResolution, maxTextLength);
            });
        }
    }

    /// <summary>
    /// Projects secondary failures to identifier-only values in a deterministic order and caps the
    /// retained count, so a report round-trips to the same bytes regardless of the order the host
    /// observed the owned cleanup phases in.
    /// </summary>
    private static List<MauiFlowSecondaryFailure> SanitizeSecondaryFailures(
        IEnumerable<MauiFlowSecondaryFailure>? source)
        => NormalizeSecondaryFailures(source);

    private static void SanitizeReport(MauiFlowRunReport report, int maxTextLength)
    {
        report.Events = (report.Events ?? [])
            .Where(static item => item is not null)
            .ToList();
        report.Steps = (report.Steps ?? [])
            .Where(static item => item is not null)
            .ToList();
        report.Artifacts = (report.Artifacts ?? [])
            .Where(static item => item is not null)
            .ToList();
        report.BusinessOracles = (report.BusinessOracles ?? [])
            .Where(static item => item is not null)
            .ToList();
        report.Omissions = (report.Omissions ?? [])
            .Where(static item => item is not null)
            .ToList();
        report.SecondaryFailures = SanitizeSecondaryFailures(report.SecondaryFailures);
        report.RunId = MauiFlowReportRedactor.SafeIdentifier(report.RunId);
        report.FlowId = MauiFlowReportRedactor.SafeIdentifier(report.FlowId);
        report.FlowDigest = MauiFlowReportRedactor.SafeIdentifier(report.FlowDigest);
        report.LegacyFlowIdentity = MauiFlowReportRedactor.SafeIdentifier(report.LegacyFlowIdentity);
        report.ReportDigest = MauiFlowReportRedactor.SafeIdentifier(report.ReportDigest);
        report.ReportPath = MauiFlowReportRedactor.SafeRelativePath(report.ReportPath);
        report.SideEffectPolicy = MauiFlowReportRedactor.SafeIdentifier(report.SideEffectPolicy);
        SanitizeTarget(report.Target);
        SanitizeCheckpoint(report.Reset);
        SanitizePreconditions(report.Preconditions);
        SanitizeCompensator(report.Compensator);
        SanitizeAppProcess(report.AppProcess, maxTextLength);
        SanitizeExpectedEvidence(report, report.ExpectedEvidence, maxTextLength);
        SanitizeOracles(report.BusinessOracles, maxTextLength);
        SanitizeEligibility(report.ReplayEligibility, maxTextLength);

        foreach (var eventItem in report.Events)
        {
            eventItem.Kind = MauiFlowReportRedactor.SafeIdentifier(eventItem.Kind);
            eventItem.StepId = MauiFlowReportRedactor.SafeIdentifier(eventItem.StepId);
            eventItem.Data = null;
        }

        foreach (var step in report.Steps)
        {
            step.Actionability = (step.Actionability ?? [])
                .Where(static item => item is not null)
                .ToList();
            step.Assertions = (step.Assertions ?? [])
                .Where(static item => item is not null)
                .ToList();
            step.SelectorCandidates = (step.SelectorCandidates ?? [])
                .Where(static item => item is not null)
                .ToList();
            step.SelectorCandidateOmissions = (step.SelectorCandidateOmissions ?? [])
                .Where(static item => item is not null)
                .ToList();
            step.Artifacts = (step.Artifacts ?? [])
                .Where(static item => item is not null)
                .ToList();
            step.StepId = MauiFlowReportRedactor.SafeIdentifier(step.StepId);
            step.Action = MauiFlowReportRedactor.SafeIdentifier(step.Action);
            step.Selector = MauiFlowReportRedactor.SanitizeSelector(step.Selector);
            SanitizeSelectorRequest(step.SelectorRequest);
            step.CommandId = MauiFlowReportRedactor.SafeIdentifier(step.CommandId);
            step.ActionDigest = MauiFlowReportRedactor.SafeIdentifier(step.ActionDigest);
            step.AcknowledgementState = MauiFlowReportRedactor.SafeIdentifier(step.AcknowledgementState);
            step.CompletionCertainty = MauiFlowReportRedactor.SafeIdentifier(step.CompletionCertainty);
            step.FailureClass = MauiFlowReportRedactor.SafeIdentifier(step.FailureClass);
            SanitizeSelectorEvidence(step);
            step.TargetResolution?.Let(value =>
            {
                value.Status = MauiFlowReportRedactor.SafeIdentifier(value.Status);
                value.ElementId = MauiFlowReportRedactor.SafeIdentifier(value.ElementId);
            });
            step.CandidateSummary?.Let(value =>
            {
                value.Types = value.Types
                    .Take(8)
                    .Select(static type => MauiFlowReportRedactor.SafeIdentifier(type) ?? "element")
                    .ToList();
                value.Final = MauiFlowReportRedactor.SafeMessage(value.Final, maxTextLength);
            });
            SanitizeCheckpoint(step.ExpectedCheckpoint);
            SanitizeCheckpoint(step.ObservedCheckpoint);
            step.Dispatch?.Let(value =>
            {
                value.CommandId = MauiFlowReportRedactor.SafeIdentifier(value.CommandId);
                value.ActionDigest = MauiFlowReportRedactor.SafeIdentifier(value.ActionDigest);
                value.AcknowledgementState = MauiFlowReportRedactor.SafeIdentifier(value.AcknowledgementState);
                value.CompletionCertainty = MauiFlowReportRedactor.SafeIdentifier(value.CompletionCertainty);
            });
            foreach (var assertion in step.Assertions)
            {
                assertion.Kind = MauiFlowReportRedactor.SafeIdentifier(assertion.Kind);
                assertion.TargetResolution?.Let(value =>
                {
                    value.Status = MauiFlowReportRedactor.SafeIdentifier(value.Status);
                    value.ElementId = MauiFlowReportRedactor.SafeIdentifier(value.ElementId);
                });
                // A failed assertion is the one place where the observed value is the whole point
                // of the report. Withholding it leaves the author a length and a digest, which is
                // unusable. Disclosure still has to clear IsSafeText.
                var failed = assertion.Passed == false && assertion.Skipped != true;
                assertion.ExpectedDisclosure = NormalizeDisclosure(assertion.ExpectedDisclosure, assertion.Expected, failed);
                assertion.ActualDisclosure = NormalizeDisclosure(assertion.ActualDisclosure, assertion.Actual, failed);
                assertion.Expected = assertion.ExpectedDisclosure.Value;
                assertion.Actual = assertion.ActualDisclosure.Value;
            }
            foreach (var actionability in step.Actionability)
            {
                actionability.Kind = MauiFlowReportRedactor.SafeIdentifier(actionability.Kind);
                actionability.Outcome = MauiFlowReportRedactor.SafeIdentifier(actionability.Outcome);
            }
            SanitizeArtifacts(step.Artifacts, maxTextLength);
        }

        if (report.Failure is not null)
        {
            report.Failure.Artifacts = (report.Failure.Artifacts ?? [])
                .Where(static item => item is not null)
                .ToList();
            report.Failure.FailureId = MauiFlowReportRedactor.SafeIdentifier(report.Failure.FailureId);
            report.Failure.Class = MauiFlowReportRedactor.SafeIdentifier(report.Failure.Class);
            report.Failure.Code = MauiFlowReportRedactor.SafeIdentifier(report.Failure.Code);
            report.Failure.Category = MauiFlowReportRedactor.SafeIdentifier(report.Failure.Category);
            report.Failure.Phase = MauiFlowReportRedactor.SafeIdentifier(report.Failure.Phase);
            report.Failure.LegacyKind = MauiFlowReportRedactor.SafeIdentifier(report.Failure.LegacyKind);
            report.Failure.StepId = MauiFlowReportRedactor.SafeIdentifier(report.Failure.StepId);
            report.Failure.Retryable = MauiFlowFailureClassifier.Classify(
                new MauiFlowFailureFacts
                {
                    TerminalOutcome = report.Outcome?.Status,
                    FailureClass = report.Failure.Class,
                    LegacyFailureKind = report.Failure.LegacyKind,
                }).Retryable;
            SanitizeArtifacts(report.Failure.Artifacts, maxTextLength);
        }
        SanitizeArtifacts(report.Artifacts, maxTextLength);
    }

    /// <summary>
    /// A managed type name is structural identity, not free text. The generic identifier rules
    /// classify any long mixed-case token as an opaque secret, which silently drops real type
    /// names such as <c>Microsoft.Maui.Controls.Button</c>; a dropped name then makes every later
    /// fingerprint comparison read as "both sides missing", which is not a match. A value that is
    /// recognisably a namespace-qualified type name is therefore kept as a stable digest, in the
    /// same shape <see cref="MauiFlowReportRedactor.SafeReference"/> uses.
    /// </summary>
    /// <remarks>
    /// The shape test narrows the digest branch, it does not make it exact. A dotted, identifier-
    /// shaped, short-segmented value that is not a type name would still be digested, and a digest
    /// is a commitment to the value: it confirms a guess and links reports. That residual is
    /// accepted only because the alternative — publishing the raw value or dropping type identity
    /// entirely — is worse, and because every value reaching here is already a type-name field.
    /// Values with a sensitive-looking name are dropped outright rather than digested.
    /// </remarks>
    private static string? SafeManagedTypeName(string? value)
    {
        var safe = MauiFlowReportRedactor.SafeIdentifier(value);
        if (safe is not null || string.IsNullOrWhiteSpace(value))
            return safe;

        var trimmed = value.Trim();
        if (FlowSecretReference.LooksSensitive(trimmed) || !LooksLikeManagedTypeName(trimmed))
            return null;

        return "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes("managed-type|" + trimmed))).ToLowerInvariant();
    }

    /// <summary>
    /// Recognises a namespace-qualified CLR type name: two or more dot- or plus-separated
    /// identifier segments with an optional generic arity suffix. Requiring a separator is what
    /// excludes single-token credentials, which are the common shape for API keys and access
    /// tokens; the segment cap excludes encoded blobs.
    /// </summary>
    private static bool LooksLikeManagedTypeName(string value)
    {
        if (value.Length is 0 or > 256)
            return false;

        var segments = value.Split('.', '+');
        if (segments.Length < 2)
            return false;

        foreach (var segment in segments)
        {
            var name = segment;
            var arity = name.IndexOf('`', StringComparison.Ordinal);
            if (arity >= 0)
            {
                var digits = name[(arity + 1)..];
                if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
                    return false;
                name = name[..arity];
            }

            if (name.Length is 0 or > 40 ||
                !(char.IsAsciiLetter(name[0]) || name[0] == '_') ||
                !name.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static void SanitizeSelectorEvidence(MauiFlowStepAttempt step)
    {
        if (step.Fingerprint is { } fingerprint)
        {
            fingerprint.ExtensionData = null;
            fingerprint.FingerprintId = MauiFlowReportRedactor.SafeIdentifier(fingerprint.FingerprintId);
            fingerprint.Context.AppId = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Context.AppId);
            fingerprint.Context.AppBuild = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Context.AppBuild);
            fingerprint.Context.Platform = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Context.Platform);
            fingerprint.Context.Route = MauiFlowReportRedactor.SafeRoute(fingerprint.Context.Route);
            fingerprint.Context.Window = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Context.Window);
            fingerprint.Context.Modal = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Context.Modal);
            fingerprint.Context.Locale = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Context.Locale);
            fingerprint.Context.Theme = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Context.Theme);
            fingerprint.Context.Orientation = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Context.Orientation);
            fingerprint.Context.DisplayProfile = MauiFlowReportRedactor.SafeDisplayProfile(fingerprint.Context.DisplayProfile);
            fingerprint.Managed.Type = SafeManagedTypeName(fingerprint.Managed.Type);
            fingerprint.Managed.FullType = SafeManagedTypeName(fingerprint.Managed.FullType);
            fingerprint.Managed.Framework = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Managed.Framework);
            fingerprint.Managed.Role = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Managed.Role);
            fingerprint.Managed.AutomationId = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Managed.AutomationId);
            fingerprint.Managed.Traits = fingerprint.Managed.Traits
                .Select(static trait => MauiFlowReportRedactor.SafeIdentifier(trait) ?? "trait")
                .Take(16)
                .ToList();
            if (fingerprint.Native is not null)
            {
                fingerprint.Native.Identity = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Native.Identity);
                fingerprint.Native.Kind = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Native.Kind);
            }
            if (fingerprint.Source is not null)
            {
                fingerprint.Source.File = MauiFlowReportRedactor.SafeRelativePath(fingerprint.Source.File);
                fingerprint.Source.BuildHash = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Source.BuildHash);
                fingerprint.Source.CurrentHash = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Source.CurrentHash);
                fingerprint.Source.State = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Source.State);
                fingerprint.Source.Confidence = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Source.Confidence);
            }
            fingerprint.Topology.AncestorHash = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Topology.AncestorHash);
            fingerprint.Topology.SiblingHash = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Topology.SiblingHash);
            fingerprint.Topology.ChildHash = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Topology.ChildHash);
            fingerprint.Topology.StableAncestorAutomationId = MauiFlowReportRedactor.SafeIdentifier(
                fingerprint.Topology.StableAncestorAutomationId);
            if (fingerprint.Collection is not null)
            {
                fingerprint.Collection.Scope = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Collection.Scope);
                fingerprint.Collection.ItemKey = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Collection.ItemKey);
                fingerprint.Collection.TemplateKind = MauiFlowReportRedactor.SafeIdentifier(fingerprint.Collection.TemplateKind);
            }
            fingerprint.NormalizedBounds?.Let(value => value.ExtensionData = null);
            fingerprint.EvidenceRefs = fingerprint.EvidenceRefs
                .Select(static value => MauiFlowReportRedactor.SafeIdentifier(value) ?? "evidence")
                .Take(32)
                .ToList();
        }

        foreach (var candidate in step.SelectorCandidates)
        {
            candidate.ExtensionData = null;
            candidate.CandidateId = MauiFlowReportRedactor.SafeIdentifier(candidate.CandidateId);
            candidate.Origin = MauiFlowReportRedactor.SafeIdentifier(candidate.Origin);
            candidate.Selector = MauiFlowReportRedactor.SanitizeSelector(candidate.Selector);
            candidate.SelectorDescriptor.Kind = MauiFlowReportRedactor.SafeIdentifier(candidate.SelectorDescriptor.Kind);
            candidate.SelectorDescriptor.AutomationId = MauiFlowReportRedactor.SafeIdentifier(candidate.SelectorDescriptor.AutomationId);
            candidate.SelectorDescriptor.StableItemKey = MauiFlowReportRedactor.SafeIdentifier(candidate.SelectorDescriptor.StableItemKey);
            candidate.SelectorDescriptor.NativeAutomationIdentity = MauiFlowReportRedactor.SafeIdentifier(candidate.SelectorDescriptor.NativeAutomationIdentity);
            candidate.SelectorDescriptor.Role = MauiFlowReportRedactor.SafeIdentifier(candidate.SelectorDescriptor.Role);
            candidate.SelectorDescriptor.Type = MauiFlowReportRedactor.SafeIdentifier(candidate.SelectorDescriptor.Type);
            candidate.SelectorDescriptor.AncestorAutomationId = MauiFlowReportRedactor.SafeIdentifier(candidate.SelectorDescriptor.AncestorAutomationId);
            candidate.SelectorDescriptor.SourceAnchor = MauiFlowReportRedactor.SafeMessage(candidate.SelectorDescriptor.SourceAnchor);
            // Run reports never retain raw exact text, even if an external caller populated it.
            candidate.SelectorDescriptor.ExactText = null;
            candidate.ScopeDescriptor.Route = MauiFlowReportRedactor.SafeRoute(candidate.ScopeDescriptor.Route);
            candidate.ScopeDescriptor.Window = MauiFlowReportRedactor.SafeIdentifier(candidate.ScopeDescriptor.Window);
            candidate.ScopeDescriptor.Modal = MauiFlowReportRedactor.SafeIdentifier(candidate.ScopeDescriptor.Modal);
            candidate.ScopeDescriptor.CollectionScope = MauiFlowReportRedactor.SafeIdentifier(candidate.ScopeDescriptor.CollectionScope);
            candidate.ScopeDescriptor.LocaleAssumption = MauiFlowReportRedactor.SafeIdentifier(candidate.ScopeDescriptor.LocaleAssumption);
            var sanitizedRationale = candidate.RationaleCodes
                .Select(static value => MauiFlowReportRedactor.SafeIdentifier(value) ?? "rationale")
                .Take(16)
                .ToList();
            candidate.RationaleCodes.Clear();
            candidate.RationaleCodes.AddRange(sanitizedRationale);
            var sanitizedRisks = candidate.RiskFlags
                .Select(static value => MauiFlowReportRedactor.SafeIdentifier(value) ?? "risk")
                .Take(16)
                .ToList();
            candidate.RiskFlags.Clear();
            candidate.RiskFlags.AddRange(sanitizedRisks);
            candidate.Validation.PlatformState = MauiFlowReportRedactor.SafeIdentifier(candidate.Validation.PlatformState);
            candidate.Validation.SourceState = MauiFlowReportRedactor.SafeIdentifier(candidate.Validation.SourceState);
            candidate.Validation.RejectionReason = MauiFlowReportRedactor.SafeMessage(candidate.Validation.RejectionReason);
            candidate.Calibration.State = MauiFlowReportRedactor.SafeIdentifier(candidate.Calibration.State) ??
                MauiSelectorHealthRules.Uncalibrated;
            candidate.Calibration.RuleVersion = MauiFlowReportRedactor.SafeIdentifier(candidate.Calibration.RuleVersion) ??
                MauiSelectorHealthRules.RankerRuleVersion;
            candidate.EvidenceRefs = candidate.EvidenceRefs
                .Select(static value => MauiFlowReportRedactor.SafeIdentifier(value) ?? "evidence")
                .Take(32)
                .ToList();
        }
        foreach (var omission in step.SelectorCandidateOmissions)
        {
            omission.Kind = MauiFlowReportRedactor.SafeIdentifier(omission.Kind);
            omission.Reason = MauiFlowReportRedactor.SafeMessage(omission.Reason);
        }
    }

    private static void SanitizeTarget(MauiFlowRunTarget? target)
    {
        if (target is null)
            return;
        target.TargetId = MauiFlowReportRedactor.SafeIdentifier(target.TargetId);
        target.Platform = MauiFlowReportRedactor.SafeIdentifier(target.Platform);
        target.DeviceId = MauiFlowReportRedactor.SafeIdentifier(target.DeviceId);
        target.DeviceProfile = MauiFlowReportRedactor.SafeIdentifier(target.DeviceProfile);
        target.AppId = MauiFlowReportRedactor.SafeIdentifier(target.AppId);
        target.AppBuildFingerprint = MauiFlowReportRedactor.SafeIdentifier(target.AppBuildFingerprint);
        target.AppSourceFingerprint = MauiFlowReportRedactor.SafeIdentifier(target.AppSourceFingerprint);
        target.PackageDigest = MauiFlowReportRedactor.SafeIdentifier(target.PackageDigest);
        target.NormalizedPayloadDigest = MauiFlowReportRedactor.SafeIdentifier(target.NormalizedPayloadDigest);
        target.AgentId = MauiFlowReportRedactor.SafeIdentifier(target.AgentId);
        target.AgentInstanceId = MauiFlowReportRedactor.SafeIdentifier(target.AgentInstanceId);
        target.Locale = MauiFlowReportRedactor.SafeIdentifier(target.Locale);
        target.Theme = MauiFlowReportRedactor.SafeIdentifier(target.Theme);
        target.Orientation = MauiFlowReportRedactor.SafeIdentifier(target.Orientation);
        target.DisplayProfile = MauiFlowReportRedactor.SafeDisplayProfile(target.DisplayProfile);
    }

    private static void SanitizeCheckpoint(MauiFlowResetResult? reset)
    {
        if (reset is null)
            return;
        reset.Strategy = MauiFlowReportRedactor.SafeIdentifier(reset.Strategy);
        reset.ResetIdentity = MauiFlowReportRedactor.SafeIdentifier(reset.ResetIdentity);
        reset.SeedFingerprint = MauiFlowReportRedactor.SafeIdentifier(reset.SeedFingerprint);
        reset.BackendStateFingerprint = MauiFlowReportRedactor.SafeIdentifier(reset.BackendStateFingerprint);
        reset.Message = MauiFlowReportRedactor.SafeMessage(reset.Message);
        if (reset.Reference is not null)
        {
            reset.Reference.Strategy = MauiFlowReportRedactor.SafeIdentifier(reset.Reference.Strategy);
            reset.Reference.ResetId = MauiFlowReportRedactor.SafeIdentifier(reset.Reference.ResetId);
            reset.Reference.Scope = MauiFlowReportRedactor.SafeIdentifier(reset.Reference.Scope);
            reset.Reference.Version = MauiFlowReportRedactor.SafeIdentifier(reset.Reference.Version);
            reset.Reference.EvidenceReference = MauiFlowReportRedactor.SafeReference(reset.Reference.EvidenceReference);
        }
        SanitizeAppStateSeed(reset.AppStateSeed);
        SanitizeBackendSeed(reset.BackendTestDataSeed);
        if (reset.Outcome is not null)
        {
            reset.Outcome.EvidenceReference = MauiFlowReportRedactor.SafeReference(reset.Outcome.EvidenceReference);
            reset.Outcome.Message = MauiFlowReportRedactor.SafeMessage(reset.Outcome.Message);
        }
    }

    private static void SanitizeCheckpoint(MauiFlowCheckpoint? checkpoint)
    {
        if (checkpoint is null)
            return;
        checkpoint.AppBuildFingerprint = MauiFlowReportRedactor.SafeIdentifier(checkpoint.AppBuildFingerprint);
        checkpoint.AgentInstanceId = MauiFlowReportRedactor.SafeIdentifier(checkpoint.AgentInstanceId);
        checkpoint.SeedFingerprint = MauiFlowReportRedactor.SafeIdentifier(checkpoint.SeedFingerprint);
        checkpoint.BackendStateFingerprint = MauiFlowReportRedactor.SafeIdentifier(checkpoint.BackendStateFingerprint);
        checkpoint.Route = MauiFlowReportRedactor.SafeRoute(checkpoint.Route);
        checkpoint.Window = MauiFlowReportRedactor.SafeIdentifier(checkpoint.Window);
        checkpoint.Modal = MauiFlowReportRedactor.SafeIdentifier(checkpoint.Modal);
        checkpoint.Locale = MauiFlowReportRedactor.SafeIdentifier(checkpoint.Locale);
        checkpoint.Theme = MauiFlowReportRedactor.SafeIdentifier(checkpoint.Theme);
        checkpoint.Orientation = MauiFlowReportRedactor.SafeIdentifier(checkpoint.Orientation);
        checkpoint.DisplayProfile = MauiFlowReportRedactor.SafeDisplayProfile(checkpoint.DisplayProfile);
        checkpoint.CollectionItemKey = MauiFlowReportRedactor.SafeIdentifier(checkpoint.CollectionItemKey);
    }

    private static void SanitizePreconditions(MauiFlowReplayPreconditions? preconditions)
    {
        if (preconditions is null)
            return;
        SanitizeCheckpoint(preconditions.Expected);
        SanitizeCheckpoint(preconditions.Observed);
        preconditions.EvidenceReference = MauiFlowReportRedactor.SafeReference(preconditions.EvidenceReference);
    }

    private static void SanitizeAppStateSeed(MauiFlowAppStateSeedFingerprint? seed)
    {
        if (seed is null)
            return;
        seed.SeedId = MauiFlowReportRedactor.SafeIdentifier(seed.SeedId);
        seed.Fingerprint = MauiFlowReportRedactor.SafeIdentifier(seed.Fingerprint);
        seed.Version = MauiFlowReportRedactor.SafeIdentifier(seed.Version);
        seed.Source = MauiFlowReportRedactor.SafeIdentifier(seed.Source);
    }

    private static void SanitizeBackendSeed(MauiFlowBackendTestDataSeedFingerprint? seed)
    {
        if (seed is null)
            return;
        seed.SeedId = MauiFlowReportRedactor.SafeIdentifier(seed.SeedId);
        seed.Fingerprint = MauiFlowReportRedactor.SafeIdentifier(seed.Fingerprint);
        seed.Dataset = MauiFlowReportRedactor.SafeIdentifier(seed.Dataset);
        seed.Version = MauiFlowReportRedactor.SafeIdentifier(seed.Version);
        seed.Source = MauiFlowReportRedactor.SafeIdentifier(seed.Source);
    }

    /// <summary>
    /// Bounds and redacts host-collected application-process evidence. The crash excerpt is the
    /// only place raw device output reaches the report, so it is line-capped and every line is put
    /// through the same message redaction as any other free text.
    /// </summary>
    private static void SanitizeAppProcess(MauiFlowAppProcessEvidence? evidence, int maxTextLength)
    {
        if (evidence is null)
            return;

        evidence.Source = MauiFlowReportRedactor.SafeIdentifier(evidence.Source);
        evidence.ExitReason = MauiFlowReportRedactor.SafeIdentifier(evidence.ExitReason);
        evidence.CrashSignature = MauiFlowReportRedactor.SafeMessage(evidence.CrashSignature, maxTextLength);
        evidence.ProbeError = MauiFlowReportRedactor.SafeMessage(evidence.ProbeError, maxTextLength);
        var excerpt = (evidence.CrashExcerpt ?? [])
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Take(MaxCrashExcerptLines)
            .Select(line => MauiFlowReportRedactor.SafeMessage(line, maxTextLength))
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => line!)
            .ToList();
        evidence.CrashExcerpt = excerpt.Count == 0 ? null : excerpt;
    }

    /// <summary>
    /// Bounds the declared-evidence block and recomputes its counters from the checks that
    /// survived, so a trimmed report never claims more coverage than it shows.
    /// </summary>
    private static void SanitizeExpectedEvidence(
        MauiFlowRunReport report,
        MauiFlowExpectedEvidenceReport? evidence,
        int maxTextLength)
    {
        if (evidence is null)
            return;

        var retained = (evidence.Checks ?? [])
            .Where(static item => item is not null)
            .ToList();
        var declared = retained.Count;
        var trimmed = declared > MaxExpectedEvidenceChecks;
        if (trimmed)
        {
            retained = retained.Take(MaxExpectedEvidenceChecks).ToList();
            AddOmission(
                report,
                "expected-evidence-checks",
                "Some expected-evidence checks were omitted to satisfy the report limit.",
                declared - retained.Count);
        }

        evidence.Checks = retained;
        foreach (var check in evidence.Checks)
        {
            check.ExpectationId = MauiFlowReportRedactor.SafeReference(check.ExpectationId);
            check.Kind = MauiFlowReportRedactor.SafeIdentifier(check.Kind);
            check.Scope = MauiFlowReportRedactor.SafeIdentifier(check.Scope);
            check.StepId = MauiFlowReportRedactor.SafeIdentifier(check.StepId);
            check.Reference = MauiFlowReportRedactor.SafeReference(check.Reference);
            check.State = MauiFlowReportRedactor.SafeIdentifier(check.State);
            check.Reason = MauiFlowReportRedactor.SafeMessage(check.Reason, maxTextLength);
        }

        // Declared keeps the pre-trim count so a reader can see coverage was reduced, and a trimmed
        // report can never claim every expectation held: the dropped checks are unknown, not passing.
        evidence.Declared = declared;
        evidence.Satisfied = CountState(evidence.Checks, MauiFlowEvidenceExpectationStates.Satisfied);
        evidence.Unsatisfied = CountState(evidence.Checks, MauiFlowEvidenceExpectationStates.Unsatisfied);
        evidence.NotApplicable = CountState(evidence.Checks, MauiFlowEvidenceExpectationStates.NotApplicable);
        evidence.AllSatisfied = evidence.Unsatisfied == 0 && !trimmed;
    }

    private static int CountState(List<MauiFlowExpectedEvidenceCheck> checks, string state)
        => checks.Count(check => string.Equals(check.State, state, StringComparison.Ordinal));

    private static void SanitizeCompensator(MauiFlowCompensatorOutcome? outcome)
    {
        if (outcome is null)
            return;
        if (outcome.Compensator is not null)
        {
            outcome.Compensator.CompensatorId = MauiFlowReportRedactor.SafeIdentifier(outcome.Compensator.CompensatorId);
            outcome.Compensator.Description = MauiFlowReportRedactor.SafeMessage(outcome.Compensator.Description);
            outcome.Compensator.Scope = MauiFlowReportRedactor.SafeIdentifier(outcome.Compensator.Scope);
            outcome.Compensator.EvidenceKind = MauiFlowReportRedactor.SafeIdentifier(outcome.Compensator.EvidenceKind);
            outcome.Compensator.Reference = MauiFlowReportRedactor.SafeReference(outcome.Compensator.Reference);
        }
        outcome.EvidenceReference = MauiFlowReportRedactor.SafeReference(outcome.EvidenceReference);
        outcome.Message = MauiFlowReportRedactor.SafeMessage(outcome.Message);
    }

    private static void SanitizeOracles(
        IEnumerable<MauiIndependentBusinessOracleResult> results,
        int maxTextLength)
    {
        foreach (var result in results)
        {
            result.OracleId = MauiFlowReportRedactor.SafeIdentifier(result.OracleId);
            result.EvidenceReference = MauiFlowReportRedactor.SafeReference(result.EvidenceReference);
            result.Message = MauiFlowReportRedactor.SafeMessage(result.Message, maxTextLength);
        }
    }

    private static void SanitizeEligibility(
        MauiFlowReplayEligibilityDecision? decision,
        int maxTextLength)
    {
        if (decision is null)
            return;
        decision.SideEffectPolicy = MauiFlowReportRedactor.SafeIdentifier(decision.SideEffectPolicy) ??
            MauiFlowSideEffectPolicies.Unspecified;
        decision.Reasons = (decision.Reasons ?? [])
            .Where(static reason => reason is not null)
            .ToList();
        foreach (var reason in decision.Reasons)
        {
            reason.Code = MauiFlowReportRedactor.SafeIdentifier(reason.Code);
            reason.Message = MauiFlowReportRedactor.SafeMessage(reason.Message, maxTextLength);
            reason.Scope = MauiFlowReportRedactor.SafeIdentifier(reason.Scope);
        }
    }

    private static void SanitizeSelectorRequest(MauiFlowSelectorRequest? request)
    {
        if (request is null)
            return;
        request.Kind = MauiFlowReportRedactor.SafeIdentifier(request.Kind);
        request.Scope = MauiFlowReportRedactor.SafeIdentifier(request.Scope);
        request.Value = NormalizeDisclosure(request.Value, request.Value?.Value);
    }

    private static MauiFlowValueDisclosure NormalizeDisclosure(
        MauiFlowValueDisclosure? disclosure,
        string? rawValue,
        bool allowSafeText = false)
    {
        if (disclosure is null)
            return MauiFlowReportRedactor.DescribeValue(rawValue, allowPlain: allowSafeText, allowSafeText: allowSafeText);

        // Import never widens what the producer decided. A producer that redacted or omitted a
        // value withheld it deliberately (it saw the assertion target, which the wire format does
        // not carry), so import has strictly less information than the producer did and must not
        // second-guess it. allowSafeText only widens what an already-disclosed value is allowed to
        // be; it never re-opens something the producer closed.
        var producerDisclosed = string.Equals(disclosure.State, "disclosed", StringComparison.Ordinal);
        var normalized = MauiFlowReportRedactor.DescribeValue(
            producerDisclosed ? disclosure.Value ?? rawValue : null,
            allowPlain: producerDisclosed,
            allowSafeText: allowSafeText && producerDisclosed);
        if (!producerDisclosed || (disclosure.Value is null && rawValue is null))
        {
            normalized.State = disclosure.State is "disclosed" or "redacted" or "omitted"
                ? disclosure.State
                : "omitted";
            normalized.Type = MauiFlowReportRedactor.SafeIdentifier(disclosure.Type);
            normalized.Length = disclosure.Length;
            normalized.Digest = MauiFlowReportRedactor.SafeIdentifier(disclosure.Digest);
        }
        return normalized;
    }

    private static void SanitizeArtifacts(List<MauiFlowArtifactReference> artifacts, int maxTextLength)
    {
        foreach (var artifact in artifacts)
        {
            artifact.ArtifactId = MauiFlowReportRedactor.SafeIdentifier(artifact.ArtifactId);
            artifact.Kind = MauiFlowReportRedactor.SafeIdentifier(artifact.Kind);
            artifact.Path = MauiFlowReportRedactor.SafeRelativePath(artifact.Path);
            artifact.Digest = MauiFlowReportRedactor.SafeIdentifier(artifact.Digest);
            artifact.MediaType = MauiFlowReportRedactor.SafeIdentifier(artifact.MediaType);
        }
    }

    private static void ClearExtensionData(MauiFlowRunReport report)
    {
        report.ExtensionData = ProjectKnownReportExtensions(report.ExtensionData);
        report.Target?.Let(value => value.ExtensionData = null);
        report.Reset?.Let(value =>
        {
            value.ExtensionData = null;
            value.Reference?.Let(reference => reference.ExtensionData = null);
            value.AppStateSeed?.Let(seed => seed.ExtensionData = null);
            value.BackendTestDataSeed?.Let(seed => seed.ExtensionData = null);
            value.Outcome?.Let(outcome => outcome.ExtensionData = null);
        });
        report.Preconditions?.Let(value =>
        {
            value.ExtensionData = null;
            value.Expected?.Let(checkpoint => checkpoint.ExtensionData = null);
            value.Observed?.Let(checkpoint => checkpoint.ExtensionData = null);
        });
        report.Compensator?.Let(value =>
        {
            value.ExtensionData = null;
            value.Compensator?.Let(reference => reference.ExtensionData = null);
        });
        report.ReplayEligibility?.Let(value =>
        {
            value.ExtensionData = null;
            foreach (var reason in value.Reasons)
                reason.ExtensionData = null;
        });
        report.Verification?.Let(value => value.ExtensionData = null);
        report.AppProcess?.Let(value => value.ExtensionData = null);
        report.ExpectedEvidence?.Let(value =>
        {
            value.ExtensionData = null;
            foreach (var check in value.Checks)
                check.ExtensionData = null;
        });
        foreach (var oracle in report.BusinessOracles)
            oracle.ExtensionData = null;
        report.Outcome?.Let(value => value.ExtensionData = null);
        report.Failure?.Let(value => value.ExtensionData = null);
        foreach (var eventItem in report.Events)
            eventItem.ExtensionData = null;
        foreach (var step in report.Steps)
        {
            step.ExtensionData = null;
            step.Selector?.Let(value => value.ExtensionData = null);
            step.SelectorRequest?.Let(value => value.ExtensionData = null);
            step.CandidateSummary?.Let(value => value.ExtensionData = null);
            step.TargetResolution?.Let(value => value.ExtensionData = null);
            step.Dispatch?.Let(value => value.ExtensionData = null);
            step.ExpectedCheckpoint?.Let(value => value.ExtensionData = null);
            step.ObservedCheckpoint?.Let(value => value.ExtensionData = null);
            foreach (var actionability in step.Actionability)
                actionability.ExtensionData = null;
            foreach (var assertion in step.Assertions)
            {
                assertion.ExtensionData = null;
                assertion.ExpectedDisclosure?.Let(value => value.ExtensionData = null);
                assertion.ActualDisclosure?.Let(value => value.ExtensionData = null);
                assertion.TargetResolution?.Let(value => value.ExtensionData = null);
            }
            foreach (var artifact in step.Artifacts)
                artifact.ExtensionData = null;
        }
        foreach (var artifact in report.Artifacts)
            artifact.ExtensionData = null;
        foreach (var omission in report.Omissions)
            omission.ExtensionData = null;
    }

    private static Dictionary<string, JsonElement>? ProjectKnownReportExtensions(
        Dictionary<string, JsonElement>? extensionData)
    {
        if (extensionData is null)
            return null;

        var projected = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (extensionData.TryGetValue("exitCategory", out var exitCategory) &&
            exitCategory.ValueKind == JsonValueKind.String &&
            MauiFlowReportRedactor.SafeIdentifier(exitCategory.GetString()) is { } safeExitCategory)
        {
            projected["exitCategory"] = CreateStringElement(safeExitCategory);
        }
        if (!extensionData.TryGetValue("primaryExecutionOutcome", out var primary) ||
            primary.ValueKind != JsonValueKind.Object)
        {
            return projected.Count == 0 ? null : projected;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            // `displacedBy` is a discriminator over a closed set, not free text. An artifact that
            // invents a third value would assert a meaning for the whole object that no reader has,
            // so anything but the known value is dropped and the object reads as the older,
            // pre-contract one it is indistinguishable from.
            if (primary.TryGetProperty("displacedBy", out var displacedBy) &&
                displacedBy.ValueKind == JsonValueKind.String &&
                string.Equals(
                    displacedBy.GetString(),
                    MauiFlowPrimaryOutcomeDisplacements.Restatement,
                    StringComparison.Ordinal))
            {
                writer.WriteString("displacedBy", MauiFlowPrimaryOutcomeDisplacements.Restatement);
            }
            foreach (var name in new[]
                     {
                         "exitCategory",
                         "status",
                         "failureClass",
                         "failureCode",
                         "failurePhase",
                     })
            {
                if (primary.TryGetProperty(name, out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    MauiFlowReportRedactor.SafeIdentifier(value.GetString()) is { } safe)
                {
                    writer.WriteString(name, safe);
                }
            }
            if (primary.TryGetProperty("verified", out var verified) &&
                verified.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                writer.WriteBoolean("verified", verified.GetBoolean());
            }
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        projected["primaryExecutionOutcome"] = document.RootElement.Clone();
        return projected;
    }

    private static JsonElement CreateStringElement(string value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            writer.WriteStringValue(value);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void Trim<T>(
        List<T> values,
        int maximum,
        MauiFlowRunReport report,
        string kind,
        string reason)
    {
        if (values.Count <= maximum)
            return;

        var omitted = values.Count - maximum;
        values.RemoveRange(maximum, omitted);
        report.Truncated = true;
        report.TruncationReason ??= reason;
        AddOmission(report, kind, reason, omitted);
    }

    private static void AddOmission(MauiFlowRunReport report, string kind, string reason, int? count)
    {
        if (report.Omissions.Any(item => string.Equals(item.Kind, kind, StringComparison.Ordinal)))
            return;
        report.Omissions.Add(new MauiFlowReportOmission
        {
            Kind = kind,
            Reason = reason,
            Count = count,
        });
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
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
}

/// <summary>Privacy projection used by run reports before a host persists or publishes them.</summary>
public static class MauiFlowReportRedactor
{
    private static readonly Regex SensitiveAssignment = new(
        @"(?ix)
        \b(password|passcode|secret|token|api[_-]?key|authorization|cookie|credential|private[_-]?key|
        access[_-]?key|client[_-]?secret|refresh[_-]?token|pin|otp|cvv|ssn)\b
        \s*[:=]\s*([^\s,;]+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BearerToken = new(
        @"(?i)\bbearer\s+[a-z0-9._~+/\-=]{8,}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex JsonWebToken = new(
        @"(?i)\beyj[a-z0-9_-]{8,}\.[a-z0-9_-]{8,}(?:\.[a-z0-9_-]{8,})?\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AbsolutePath = new(
        @"(?ix)
        (?<![a-z0-9])
        (?:
          [a-z]:[\\/][^\s""']+
          |
          (?:\\\\|//)[^\\/\s]+[\\/][^\s""']+
          |
          /(?:home|users?|var|etc|opt|private|mnt|workspace|workspaces)/[^\s""']+
          |
          /(?:data|sdcard|storage|system|vendor|product|apex)/[^\s""']+
        )",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EmailAddress = new(
        @"(?i)\b[a-z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-z0-9.-]+\.[a-z]{2,}\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LogicalDisplayProfile = new(
        @"\A(?<width>\d+(?:\.\d+)?)x(?<height>\d+(?:\.\d+)?)[@:](?<density>\d+(?:\.\d+)?)\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PhysicalDisplayProfile = new(
        @"\A(?:physical|override) size:\s*(?<width>\d+)x(?<height>\d+);(?:physical|override) density:\s*(?<density>\d+)\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static MauiFlowValueDisclosure DescribeValue(string? value, bool allowPlain = false)
        => DescribeValue(value, allowPlain, allowSafeText: false);

    /// <summary>
    /// Describes an assertion or step value for the report, disclosing it in the clear only when
    /// that is demonstrably safe.
    /// </summary>
    /// <param name="allowSafeText">
    /// When <see langword="true"/>, non-scalar text is disclosed provided it survives
    /// <see cref="IsSafeText"/>. Callers pass this only for a <em>failed</em> assertion, where the
    /// observed value is the single fact the author needs and where withholding it protects
    /// nobody: the author already committed the expectation to the flow file.
    /// </param>
    public static MauiFlowValueDisclosure DescribeValue(string? value, bool allowPlain, bool allowSafeText)
    {
        if (value is null)
            return new MauiFlowValueDisclosure { State = "omitted" };

        var normalized = RemoveControls(value);
        var scalar = IsSafeScalar(normalized);
        var disclose = allowPlain &&
            (scalar || (allowSafeText && IsSafeText(normalized))) &&
            !FlowSecretReference.LooksSensitive(normalized);
        return new MauiFlowValueDisclosure
        {
            State = disclose ? "disclosed" : "redacted",
            Type = scalar ? "scalar" : "string",
            Length = normalized.Length,
            Digest = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant(),
            Value = disclose ? normalized : null,
        };
    }

    public static string? SafeMessage(string? value, int maximumLength = 512)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var safe = RemoveControls(value);
        safe = SensitiveAssignment.Replace(safe, "$1=<redacted>");
        safe = BearerToken.Replace(safe, "Bearer <redacted>");
        safe = JsonWebToken.Replace(safe, "******");
        safe = AbsolutePath.Replace(safe, "<path>");
        safe = EmailAddress.Replace(safe, "<email>");
        if (FlowSecretReference.LooksSensitive(safe) || LooksOpaqueSecret(safe))
            safe = "<redacted>";
        if (safe.Length > maximumLength)
            safe = safe[..Math.Max(0, maximumLength - 12)] + "[truncated]";
        return safe;
    }

    public static string? SafeReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            FlowSecretReference.LooksSensitive(value))
            return null;
        var trimmed = value.Trim();
        if (LooksOpaqueSecret(trimmed))
        {
            return "sha256:" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(trimmed))).ToLowerInvariant();
        }
        var safe = SafeIdentifier(trimmed, 256);
        if (safe is not null &&
            string.Equals(safe, trimmed, StringComparison.Ordinal) &&
            !safe.Contains('/', StringComparison.Ordinal))
        {
            return safe;
        }
        return "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(trimmed))).ToLowerInvariant();
    }

    public static string? SafeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || FlowSecretReference.LooksSensitive(value))
            return null;
        var normalized = value.Trim().Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.StartsWith("//", StringComparison.Ordinal) ||
            (normalized.Length >= 2 &&
             char.IsAsciiLetter(normalized[0]) &&
             normalized[1] == ':') ||
            normalized.Contains("://", StringComparison.Ordinal))
        {
            return null;
        }
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(static segment => segment is "." or ".."))
            return null;
        var safeSegments = segments.Select(SafeFileSegment).ToArray();
        return safeSegments.Any(string.IsNullOrWhiteSpace)
            ? null
            : string.Join("/", safeSegments!);
    }

    public static string? SafeIdentifier(string? value, int maximumLength = 128)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            LooksOpaqueSecret(value))
            return null;

        var normalized = RemoveControls(value.Trim());
        if (normalized.Length > maximumLength ||
            normalized.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_' and not '.' and not ':' and not '/'))
        {
            return null;
        }
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.StartsWith("\\", StringComparison.Ordinal) ||
            normalized.StartsWith("//", StringComparison.Ordinal) ||
            normalized.StartsWith("\\\\", StringComparison.Ordinal) ||
            (normalized.Length >= 2 &&
             char.IsAsciiLetter(normalized[0]) &&
             normalized[1] == ':') ||
            normalized.Contains("://", StringComparison.Ordinal))
        {
            return null;
        }
        return normalized;
    }

    internal static string? SafeDisplayProfile(string? value)
    {
        var safe = SafeIdentifier(value);
        if (safe is not null || string.IsNullOrWhiteSpace(value))
            return safe;

        var normalized = RemoveControls(value.Trim());
        if (normalized.Length > 128 || LooksOpaqueSecret(normalized))
            return null;

        var match = LogicalDisplayProfile.Match(normalized);
        if (!match.Success)
            match = PhysicalDisplayProfile.Match(normalized);
        if (!match.Success)
            return null;

        return $"{match.Groups["width"].Value}x{match.Groups["height"].Value}:{match.Groups["density"].Value}";
    }

    public static string? SafeFileSegment(string? value)
    {
        var safe = SafeIdentifier(value, 96);
        return string.IsNullOrWhiteSpace(safe)
            ? null
            : safe.Replace('/', '_').Replace(':', '_').Trim('_', '.');
    }

    public static string? SafeRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return null;
        var index = route.IndexOfAny(['?', '#']);
        return SafeMessage(index >= 0 ? route[..index] : route, 256);
    }

    public static FlowSelector? SanitizeSelector(FlowSelector? selector)
    {
        if (selector is null)
            return null;
        return new FlowSelector
        {
            AutomationId = SafeIdentifier(selector.AutomationId),
            StableItemKey = SafeIdentifier(selector.StableItemKey),
            CollectionScope = SafeIdentifier(selector.CollectionScope),
            Id = selector.Id is null ? null : "sha256:" + ShortDigest(selector.Id),
            TypeIndex = selector.TypeIndex is null ? null : new FlowTypeIndex
            {
                Type = SafeIdentifier(selector.TypeIndex.Type),
                Index = selector.TypeIndex.Index,
            },
            Type = SafeIdentifier(selector.Type),
            Index = selector.Index,
            SelectorKind = SafeIdentifier(selector.SelectorKind),
            MatchCount = selector.MatchCount,
            Quality = SafeIdentifier(selector.Quality),
            FragilityReasons = selector.FragilityReasons?
                .Take(8)
                .Select(static reason => SafeIdentifier(reason) ?? "unknown")
                .ToList(),
        };
    }

    public static MauiFlowSelectorRequest? DescribeSelector(FlowSelector? selector)
    {
        if (selector is null || selector.IsEmpty)
            return null;

        if (!string.IsNullOrEmpty(selector.AutomationId))
            return new MauiFlowSelectorRequest
            {
                Kind = selector.HasScopedStableItem ? "scopedAutomationId" : "automationId",
                Value = DescribeValue(SafeIdentifier(selector.AutomationId), allowPlain: true),
                Scope = selector.HasScopedStableItem
                    ? $"{SafeIdentifier(selector.CollectionScope)}/{SafeIdentifier(selector.StableItemKey)}"
                    : null,
            };
        if (!string.IsNullOrEmpty(selector.Text))
            return new MauiFlowSelectorRequest
            {
                Kind = "text",
                Value = DescribeValue(selector.Text),
            };
        if (selector.TypeIndex is not null ||
            (string.Equals(selector.SelectorKind, "typeIndex", StringComparison.Ordinal) && selector.Index is not null))
            return new MauiFlowSelectorRequest
            {
                Kind = "typeIndex",
                Value = DescribeValue(
                    $"{selector.TypeIndex?.Type ?? selector.Type}:{selector.TypeIndex?.Index ?? selector.Index}",
                    allowPlain: true),
            };
        return new MauiFlowSelectorRequest
        {
            Kind = "id",
            Value = DescribeValue(selector.Id),
        };
    }

    public static string DescribeElement(string? type, string? automationId, string? id)
    {
        var safeType = SafeIdentifier(type) ?? "element";
        var safeAutomationId = SafeIdentifier(automationId);
        return safeAutomationId is not null
            ? $"{safeType}[{safeAutomationId}]"
            : $"{safeType}[{ShortDigest(id)}]";
    }

    private static bool IsSafeScalar(string value)
        => bool.TryParse(value, out _) ||
           decimal.TryParse(value, System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out _) ||
           (value.StartsWith('#') && value.Length is 7 or 9 && value[1..].All(Uri.IsHexDigit));

    /// <summary>Longest assertion text disclosed in the clear. UI labels are short; a long value is a payload.</summary>
    internal const int SafeTextDisclosureLimit = 256;

    /// <summary>
    /// Decides whether a piece of observed UI text may be written into the report in the clear.
    /// </summary>
    /// <remarks>
    /// The test is deliberately conservative and, crucially, <em>reuses the redaction pipeline as
    /// its own oracle</em>: a value qualifies only when <see cref="SafeMessage(string?, int)"/>
    /// returns it unchanged. Any string that contains an email address, an absolute Windows or
    /// POSIX path, a bearer token, a JWT, a <c>secret=</c>-style assignment, or that is long
    /// enough to be truncated, is rewritten by that pass and so is rejected here. On top of that
    /// the value must not look like an opaque credential and must be short. Because disclosure is
    /// byte-identical to the value that was hashed, the reported <c>length</c> and <c>digest</c>
    /// stay honest descriptions of the disclosed text.
    /// </remarks>
    private static bool IsSafeText(string value)
    {
        if (value.Length is 0 or > SafeTextDisclosureLimit)
            return false;
        if (LooksOpaqueSecret(value))
            return false;
        return string.Equals(SafeMessage(value, SafeTextDisclosureLimit), value, StringComparison.Ordinal);
    }

    private static bool LooksOpaqueSecret(string value)
    {
        var compact = new string(value.Where(char.IsAsciiLetterOrDigit).ToArray());
        return !value.Any(char.IsWhiteSpace) &&
            compact.Length >= 20 &&
            compact.Any(char.IsUpper) &&
            compact.Any(char.IsLower);
    }

    private static string ShortDigest(string? value)
        => value is null
            ? "none"
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];

    private static string RemoveControls(string value)
        => new(value.Where(static character =>
            !char.IsControl(character) &&
            char.GetUnicodeCategory(character) != UnicodeCategory.Format).ToArray());
}

internal static class MauiFlowReportExtensions
{
    public static void Let<T>(this T? value, Action<T> action)
        where T : class
    {
        if (value is not null)
            action(value);
    }
}
