using System.Globalization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>Versioned stable identities for a test, a recurring incident, and one occurrence.</summary>
public sealed class MauiFlowFingerprintSet
{
    public string Version { get; init; } = MauiFlowIncidentFingerprint.RuleVersion;
    public string? TestIdentityFingerprint { get; init; }
    public string? IncidentFingerprint { get; init; }
    public string? OccurrenceFingerprint { get; init; }
}

/// <summary>
/// Computes one-way, provider-neutral identities. Incident identity deliberately excludes run,
/// timestamp, source revision, report digest, and app-build occurrence facts.
/// </summary>
public static class MauiFlowIncidentFingerprint
{
    public const string RuleVersion = "maui-flow-fingerprints-v1";

    public static MauiFlowFingerprintSet Create(
        MauiFlow? flow,
        MauiFlowRunReport? report,
        MauiTestExecutionManifest? manifest,
        MauiFlowFailureClassification? classification = null)
    {
        var test = MauiTestingContractRedactor.NormalizeFingerprint(
                manifest?.TestIdentityFingerprint) ??
            CreateTestIdentityFingerprint(flow, report);
        var incident = CreateIncidentFingerprint(flow, report, manifest, classification, test);
        return new MauiFlowFingerprintSet
        {
            TestIdentityFingerprint = test,
            IncidentFingerprint = incident,
            OccurrenceFingerprint = CreateOccurrenceFingerprint(report, manifest, incident),
        };
    }

    public static string? CreateTestIdentityFingerprint(MauiFlow? flow, MauiFlowRunReport? report)
    {
        var material = new List<string> { RuleVersion, "test" };
        if (flow is not null)
        {
            material.Add("schema:" + flow.Schema.ToString(CultureInfo.InvariantCulture));
            foreach (var step in (flow.Steps ?? [])
                         .Select(static step => (Identity: MauiFlowStepIdentity.Get(step), Step: step))
                         .OrderBy(static item => item.Identity, StringComparer.Ordinal))
            {
                material.Add(StepMaterial(
                    step.Identity,
                    step.Step.Action,
                    step.Step.Asserts,
                    step.Step.AcceptanceCriterionIds));
            }
        }
        else if (report is not null)
        {
            foreach (var step in (report.Steps ?? [])
                         .Where(static step => !string.IsNullOrWhiteSpace(step.StepId) || step.Sequence is not null)
                         .OrderBy(static step => StepAttemptIdentity(step), StringComparer.Ordinal))
            {
                material.Add(string.Join(
                    "\u001f",
                    StepAttemptIdentity(step),
                    Normalize(step.Action)));
            }
            if (material.Count == 2 &&
                MauiTestingContractRedactor.Fingerprint(report.FlowDigest, "flow") is { } flowDigest)
            {
                material.Add("flow:" + flowDigest);
            }
        }

        return material.Count > 2
            ? MauiTestingContractRedactor.Hash(string.Join("\u001e", material))
            : null;
    }

    public static string? CreateIncidentFingerprint(
        MauiFlow? flow,
        MauiFlowRunReport? report,
        MauiTestExecutionManifest? manifest,
        MauiFlowFailureClassification? classification = null,
        string? testIdentityFingerprint = null)
    {
        var test = MauiTestingContractRedactor.NormalizeFingerprint(testIdentityFingerprint) ??
            MauiTestingContractRedactor.NormalizeFingerprint(manifest?.TestIdentityFingerprint) ??
            CreateTestIdentityFingerprint(flow, report);
        var failureClass = classification?.FailureClass ??
            report?.Failure?.Class ??
            report?.Steps.FirstOrDefault(static step => !string.IsNullOrWhiteSpace(step.FailureClass))?.FailureClass ??
            classification?.Code ??
            report?.Failure?.Code;
        var detailCode = report?.Failure?.Code ??
            classification?.Code ??
            failureClass;
        if (string.IsNullOrWhiteSpace(test) ||
            string.IsNullOrWhiteSpace(failureClass) ||
            string.Equals(failureClass, "passed", StringComparison.Ordinal))
            return null;

        var stepId = ResolveFailureStepIdentity(report);
        var platform = MauiTestingContractRedactor.SafeCode(
            manifest?.Device?.Platform ?? report?.Target?.Platform);
        var runtimeKind = MauiTestingContractRedactor.SafeCode(manifest?.Device?.RuntimeKind);
        var deviceProfile = MauiTestingContractRedactor.SafeDeviceProfile(
            manifest?.Device?.Profile ?? report?.Target?.DeviceProfile);
        var material = string.Join(
            "\u001e",
            RuleVersion,
            "incident",
            test,
            Normalize(failureClass),
            Normalize(detailCode),
            Normalize(classification?.Category ?? report?.Failure?.Category),
            Normalize(classification?.Phase ?? report?.Failure?.Phase),
            Normalize(stepId),
            Normalize(platform),
            Normalize(runtimeKind),
            Normalize(deviceProfile));
        return MauiTestingContractRedactor.Hash(material);
    }

    public static string? CreateOccurrenceFingerprint(
        MauiFlowRunReport? report,
        MauiTestExecutionManifest? manifest,
        string? incidentFingerprint)
    {
        if (string.IsNullOrWhiteSpace(incidentFingerprint))
            return null;

        var facts = new List<string>();
        Add(facts, "manifest", manifest?.ManifestId);
        Add(facts, "manifest-run", manifest?.RunId);
        Add(facts, "report-run", report?.RunId);
        Add(facts, "manifest-start", Format(manifest?.Lifecycle?.StartedAt));
        Add(facts, "manifest-end", Format(manifest?.Lifecycle?.EndedAt));
        Add(facts, "report-start", Format(report?.StartedAt));
        Add(facts, "report-end", Format(report?.EndedAt));
        Add(facts, "source-revision", manifest?.Build?.SourceRevision);
        Add(facts, "report-digest", report?.ReportDigest);
        var artifactDigests = (manifest?.Artifacts ?? [])
            .Where(static artifact => artifact is not null)
            .Select(static artifact => artifact.Digest)
            .Where(static digest => !string.IsNullOrWhiteSpace(digest))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static digest => digest, StringComparer.Ordinal)
            ?? Enumerable.Empty<string?>();
        foreach (var digest in artifactDigests)
        {
            Add(facts, "artifact", digest);
        }

        if (facts.Count == 0)
            return null;
        return MauiTestingContractRedactor.Hash(string.Join(
            "\u001e",
            new[] { RuleVersion, "occurrence", incidentFingerprint }.Concat(facts)));
    }

    private static string StepMaterial(
        string identity,
        string? action,
        IEnumerable<FlowAssert>? assertions,
        IEnumerable<string>? acceptanceCriterionIds)
        => string.Join(
            "\u001f",
            identity,
            Normalize(action),
            string.Join(
                ",",
                (assertions ?? [])
                    .Select(static assertion => $"{Normalize(assertion.Kind)}:{assertion.Verify}")
                    .OrderBy(static value => value, StringComparer.Ordinal)),
            string.Join(
                ",",
                (acceptanceCriterionIds ?? [])
                    .Select(Normalize)
                    .OrderBy(static value => value, StringComparer.Ordinal)));

    private static string? ResolveFailureStepIdentity(MauiFlowRunReport? report)
    {
        var reported = report?.Failure?.StepId ?? report?.DivergenceStepId;
        var reportStep = report?.Steps?.FirstOrDefault(step =>
            string.Equals(step.StepId, reported, StringComparison.Ordinal));
        return reportStep is null ? reported : StepAttemptIdentity(reportStep);
    }

    private static string StepAttemptIdentity(MauiFlowStepAttempt step)
        => !string.IsNullOrWhiteSpace(step.StepId)
            ? step.StepId.Trim()
            : (step.Sequence ?? 0).ToString(CultureInfo.InvariantCulture);

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();

    private static string? Format(DateTimeOffset? value)
        => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static void Add(List<string> facts, string name, string? value)
    {
        var safe = name switch
        {
            "source-revision" => MauiTestingContractRedactor.SafeIdentifierOrFingerprint(
                value,
                "source-revision"),
            "report-digest" or "artifact" => MauiTestingContractRedactor.Fingerprint(value, name),
            _ => MauiTestingContractRedactor.SafeIdentifierOrFingerprint(value, name),
        };
        if (safe is not null)
            facts.Add(name + ":" + safe);
    }
}
