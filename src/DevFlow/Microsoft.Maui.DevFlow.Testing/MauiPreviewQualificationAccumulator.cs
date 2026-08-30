using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>Contract version for accumulated qualification evidence.</summary>
public static class MauiQualificationAccumulationContract
{
    public const string Kind = "maui-preview-qualification-accumulation";
    public const string ContractVersion = "preview-qualification-accumulation-v1";
}

/// <summary>One qualification run considered for accumulation.</summary>
public sealed class MauiQualificationAccumulatedRun
{
    [JsonPropertyName("runFingerprint")] public string RunFingerprint { get; set; } = string.Empty;
    [JsonPropertyName("generatedAt")] public DateTimeOffset GeneratedAt { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = MauiPreviewQualificationStates.NotQualified;
    [JsonPropertyName("accepted")] public bool Accepted { get; set; }
    [JsonPropertyName("reasonCodes")] public List<string> ReasonCodes { get; set; } = [];
}

/// <summary>Merged qualification evidence across independent runs.</summary>
public sealed class MauiQualificationAccumulation
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("kind")] public string Kind { get; set; } = MauiQualificationAccumulationContract.Kind;
    [JsonPropertyName("contractVersion")] public string ContractVersion { get; set; } = MauiQualificationAccumulationContract.ContractVersion;
    [JsonPropertyName("generatedAt")] public DateTimeOffset GeneratedAt { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = MauiPreviewQualificationStates.NotQualified;
    [JsonPropertyName("platform")] public string? Platform { get; set; }
    [JsonPropertyName("policyVersion")] public string? PolicyVersion { get; set; }
    [JsonPropertyName("corpusFingerprint")] public string? CorpusFingerprint { get; set; }
    [JsonPropertyName("consideredRuns")] public int ConsideredRuns { get; set; }
    [JsonPropertyName("acceptedRuns")] public int AcceptedRuns { get; set; }
    [JsonPropertyName("rejectedRuns")] public int RejectedRuns { get; set; }
    [JsonPropertyName("distinctEvidenceRuns")] public int DistinctEvidenceRuns { get; set; }

    /// <summary>
    /// Product identity facts (repository commit, package and tool fingerprints) that at least one
    /// merged run did not assert. Merging cannot contradict a fact nobody stated, so these runs
    /// were pooled on trust rather than on a verified match. An empty list means every accepted
    /// run named the same build; a non-empty list names exactly what was taken on faith.
    /// </summary>
    [JsonPropertyName("unverifiedProductIdentity")] public List<string> UnverifiedProductIdentity { get; set; } = [];
    [JsonPropertyName("runs")] public List<MauiQualificationAccumulatedRun> Runs { get; set; } = [];
    [JsonPropertyName("metrics")] public Dictionary<string, MauiQualificationRateMetric> Metrics { get; set; } = [];

    /// <summary>
    /// Clean first attempts per Tier-1 flow, summed across accepted runs. This is the evidence the
    /// 20-attempt <c>--repeat</c> cap makes unreachable in a single run.
    /// </summary>
    [JsonPropertyName("firstAttemptFlows")] public List<MauiQualificationFlowAttemptSummary> FirstAttemptFlows { get; set; } = [];
    [JsonPropertyName("thresholds")] public MauiQualificationGateThresholds Thresholds { get; set; } = new();
    [JsonPropertyName("gates")] public List<MauiQualificationGateResult> Gates { get; set; } = [];
}

/// <summary>
/// Merges <c>metrics.*.numerator</c> and <c>metrics.*.denominator</c> across independent
/// qualification runs so a gate that needs many trials can be satisfied by many separate jobs,
/// machines, and days rather than by one long in-process loop.
/// </summary>
/// <remarks>
/// This is deliberately fail-closed. A run is rejected unless it agrees with the accumulation on
/// contract version, platform, policy version, corpus fingerprint, and every threshold that a
/// merged gate is judged against. Runs are also deduplicated by an evidence fingerprint that
/// excludes wall-clock time, so re-running the identical static corpus can never manufacture
/// additional independent trials.
/// </remarks>
public static class MauiPreviewQualificationAccumulator
{
    /// <summary>Rate metrics that merge additively across runs.</summary>
    public static readonly string[] MergedMetricNames =
    [
        "recordingValidity",
        "selectorStability",
        "repairPrecision",
        "repairRecall",
        "falseHeals",
        "abstention",
        "classificationAccuracy",
    ];

    public static MauiQualificationAccumulation Accumulate(
        IEnumerable<MauiPreviewQualificationReport> reports,
        DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(reports);
        var ordered = reports
            .Where(static report => report is not null)
            .OrderBy(static report => report.GeneratedAt)
            .ToList();
        var accumulation = new MauiQualificationAccumulation
        {
            GeneratedAt = generatedAt,
            ConsideredRuns = ordered.Count,
        };
        if (ordered.Count == 0)
        {
            accumulation.Thresholds = new MauiQualificationGateThresholds();
            AddGates(accumulation);
            accumulation.Status = MauiPreviewQualificationStates.NotQualified;
            return accumulation;
        }

        // The reference run defines what "the same evidence" means, so choosing the wrong one loses
        // evidence: with the oldest run as reference, one leftover file from a superseded corpus
        // rejected every current run as a fingerprint mismatch and the merge reported almost
        // nothing — a silent undercount dressed up as a clean result. Losing a run is not
        // harmless, because a run discarded here never reaches the gates, so a flow that actually
        // measured a stability failure disappears instead of failing.
        //
        // Elect the run that admits the most others, under the *real* acceptance predicate rather
        // than a proxy for it. Grouping on a subset of the compared fields is not good enough:
        // three ordinary runs differing only in --generated-no-repair share contract, platform,
        // policy and corpus fingerprint yet reject each other on static evidence, so a group of
        // them can win the vote and then admit only one of its own members. The cohort is one file
        // per CI shard, so the quadratic scan costs nothing.
        //
        // A run that rejects *itself* — relaxed thresholds, unmodelled evidence, counts that do
        // not add up — can never be accepted, so it must never be the run everything else is
        // measured against. Fall back to the whole set only when nothing is self-valid, so that
        // the rejection reasons still get reported rather than the merge silently emptying.
        //
        // This is a majority rule, not a proof: enough forged runs would out-vote the genuine
        // ones. Forgery is already outside what a self-reported file can be checked for; a stale
        // file is an accident that happens, and that is the failure this addresses.
        var eligible = ordered
            .Where(static report => Incompatibilities(report, report).Count == 0)
            .ToList();
        var candidates = eligible.Count > 0 ? eligible : ordered;
        var reference = candidates
            .OrderByDescending(candidate => candidates.Count(other => Incompatibilities(candidate, other).Count == 0))
            .ThenByDescending(static candidate => candidate.GeneratedAt)
            .ThenBy(static candidate => ComputeEvidenceFingerprint(candidate), StringComparer.Ordinal)
            .First();
        accumulation.Platform = reference.Platform;
        // Thresholds are anchored to the compiled policy defaults, never adopted from a run file.
        // A hand-edited run could otherwise set minimumRepairEvaluations to 1 and the accumulated
        // gates would obligingly agree.
        accumulation.Thresholds = new MauiQualificationGateThresholds();

        var accepted = new List<MauiPreviewQualificationReport>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var report in ordered)
        {
            var fingerprint = ComputeEvidenceFingerprint(report);
            var reasons = Incompatibilities(reference, report);
            if (!seen.Add(fingerprint))
                reasons.Add("accumulate-duplicate-run");
            var entry = new MauiQualificationAccumulatedRun
            {
                RunFingerprint = fingerprint,
                GeneratedAt = report.GeneratedAt,
                Status = report.Status,
                Accepted = reasons.Count == 0,
                ReasonCodes = reasons,
            };
            accumulation.Runs.Add(entry);
            if (entry.Accepted)
                accepted.Add(report);
        }

        accumulation.AcceptedRuns = accepted.Count;
        accumulation.RejectedRuns = accumulation.ConsideredRuns - accepted.Count;
        // Provenance names evidence that actually contributed. Taking it from ordered[0] would let
        // the published policy version and corpus fingerprint come from a run that was rejected.
        accumulation.PolicyVersion = accepted.Count > 0 ? accepted[0].Thresholds.PolicyVersion : null;
        accumulation.CorpusFingerprint = accepted.Count > 0 ? accepted[0].Fingerprints.CorpusFingerprint : null;
        accumulation.DistinctEvidenceRuns = accumulation.Runs
            .Where(static run => run.Accepted)
            .Select(static run => run.RunFingerprint)
            .Distinct(StringComparer.Ordinal)
            .Count();
        accumulation.UnverifiedProductIdentity = accepted
            .SelectMany(report => UnverifiedIdentityFields(reference.Fingerprints, report.Fingerprints))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToList();

        foreach (var name in MergedMetricNames)
            accumulation.Metrics[name] = MergeRate(accepted.Select(report => Select(report.Metrics, name)));
        accumulation.FirstAttemptFlows = MergeFirstAttemptFlows(accepted);

        AddGates(accumulation);
        accumulation.Status = accumulation.Gates.Any(static gate => gate.Status == MauiPreviewQualificationStates.Fail)
            ? MauiPreviewQualificationStates.Fail
            : accumulation.Gates.All(static gate => gate.Status == MauiPreviewQualificationStates.Pass)
                ? MauiPreviewQualificationStates.Pass
                : MauiPreviewQualificationStates.NotQualified;
        return accumulation;
    }

    /// <summary>
    /// Upper bound on run files considered in one merge. The reference election compares every
    /// candidate against every other, so an unbounded directory turns into a quadratic scan: 2,000
    /// files measured at ~29s. A CI matrix contributes one file per shard, so this is far above any
    /// real cohort, and exceeding it is reported rather than silently truncated.
    /// </summary>
    public const int MaximumRunFiles = 512;

    /// <summary>Reads every qualification report in a directory, newest last.</summary>
    public static List<MauiPreviewQualificationReport> ReadDirectory(string directory, out List<string> errors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        errors = [];
        var reports = new List<MauiPreviewQualificationReport>();
        if (!Directory.Exists(directory))
            return reports;
        var files = Directory.GetFiles(directory, "run-*.json").OrderBy(static path => path, StringComparer.Ordinal).ToList();
        if (files.Count > MaximumRunFiles)
        {
            errors.Add("accumulate-directory-too-large");
            files = files.Take(MaximumRunFiles).ToList();
        }
        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Length > 4_194_304)
                {
                    errors.Add("accumulate-run-file-too-large");
                    continue;
                }
                var parsed = JsonSerializer.Deserialize(
                    File.ReadAllText(file),
                    MauiTestingJsonContext.Default.MauiPreviewQualificationReport);
                if (parsed is null)
                {
                    errors.Add("accumulate-run-file-unreadable");
                    continue;
                }
                // Explicit JSON nulls deserialize to null non-nullable members and then throw far
                // from here. Reject the file instead of crashing mid-merge.
                if (parsed.Metrics is null || parsed.Corpus is null || parsed.Fingerprints is null ||
                    parsed.Thresholds is null || parsed.Profiles is null || parsed.Gates is null ||
                    parsed.Metrics.FlakeFirstAttemptStability is null)
                {
                    errors.Add("accumulate-run-file-incomplete");
                    continue;
                }
                reports.Add(parsed);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
            {
                errors.Add("accumulate-run-file-unreadable");
            }
        }
        return reports;
    }

    /// <summary>
    /// Fingerprints the trial-bearing evidence of a run. Wall-clock time, host timing
    /// measurements, and size distributions are excluded on purpose: two runs that observed the
    /// same trials are one observation, and timing jitter must not be able to disguise a repeat
    /// as a fresh independent run.
    /// </summary>
    public static string ComputeEvidenceFingerprint(MauiPreviewQualificationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var payload = new StringBuilder()
            .Append(report.ContractVersion).Append('\n')
            .Append(report.Platform).Append('\n')
            .Append(report.Status).Append('\n')
            .Append(JsonSerializer.Serialize(report.Fingerprints, MauiTestingJsonContext.Default.MauiQualificationFingerprints)).Append('\n')
            .Append(JsonSerializer.Serialize(report.Corpus, MauiTestingJsonContext.Default.MauiQualificationCorpusSummary)).Append('\n');
        // Profiles are hashed in a stable order so that reordering the same platform evidence
        // cannot be presented as a second, independent run.
        foreach (var profile in report.Profiles
            .Select(static profile => JsonSerializer.Serialize(profile, MauiTestingJsonContext.Default.MauiQualificationPlatformProfile))
            .OrderBy(static text => text, StringComparer.Ordinal))
        {
            payload.Append(profile).Append('\n');
        }
        foreach (var name in MergedMetricNames)
            AppendRate(payload, name, Select(report.Metrics, name));
        foreach (var flow in report.Metrics.FlakeFirstAttemptStability.Flows.OrderBy(static flow => flow.FlowId, StringComparer.Ordinal))
        {
            payload
                .Append("flow|").Append(flow.FlowId).Append('|')
                .Append(flow.CleanFirstAttempts).Append('|')
                .Append(flow.PassedFirstAttempts).Append('|')
                .Append(flow.Stability).Append('\n');
        }
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())));
    }

    private static void AppendRate(StringBuilder builder, string name, MauiQualificationRateMetric metric)
    {
        builder
            .Append(name).Append('|')
            .Append(metric.Numerator).Append('/').Append(metric.Denominator).Append('|')
            .Append(metric.IndependentEvaluations).Append('|')
            .Append(metric.IndependentDeviceRuns);
        foreach (var count in (metric.SourceCounts ?? []).OrderBy(static count => count.Source, StringComparer.Ordinal))
            builder.Append('|').Append(count.Source).Append(':').Append(count.Numerator).Append('/').Append(count.Denominator).Append('/').Append(count.IndependentEvaluations);
        builder.Append('\n');
    }

    private static List<string> Incompatibilities(
        MauiPreviewQualificationReport reference,
        MauiPreviewQualificationReport candidate)
    {
        var policy = new MauiQualificationGateThresholds();
        var reasons = new List<string>();
        if (!string.Equals(reference.ContractVersion, candidate.ContractVersion, StringComparison.Ordinal))
            reasons.Add("accumulate-contract-version-mismatch");
        if (!string.Equals(reference.Platform, candidate.Platform, StringComparison.Ordinal))
            reasons.Add("accumulate-platform-mismatch");
        if (!string.Equals(reference.Thresholds.PolicyVersion, candidate.Thresholds.PolicyVersion, StringComparison.Ordinal))
            reasons.Add("accumulate-policy-version-mismatch");
        if (!string.Equals(reference.Fingerprints.CorpusFingerprint, candidate.Fingerprints.CorpusFingerprint, StringComparison.Ordinal))
            reasons.Add("accumulate-corpus-fingerprint-mismatch");
        // Evidence about one build is not evidence about another. Without this, the *only*
        // documented way to distinguish two shards was `deviceFingerprint` — and a run file could
        // just as easily mint independence by varying the commit it claims to have tested, which
        // reads as ordinary metadata rather than as the lever it is. Pooling across builds is also
        // wrong on its own terms: a stability number that spans a fix and its regression describes
        // neither build.
        //
        // Only a *contradiction* rejects. Every one of these fields is written by
        // FingerprintOrUnknown, which turns "the harness was never told" into the literal string
        // "unknown", so comparing raw strings read a static run (commit "unknown") and a
        // device-evidence run built from an artifact manifest (commit sha256:...) as two different
        // builds — and the majority vote then threw away whichever side was outnumbered, which for
        // one device shard among static runs is the device evidence. That is the same harm as
        // electing the wrong reference, arrived at from the other direction. An unasserted field
        // cannot contradict anything, so it cannot reject; what it also cannot do is confirm that
        // the runs describe one build, so UnverifiedProductIdentity publishes which fields were
        // taken on trust rather than pretending the check covered them.
        if (IdentityConflicts(reference.Fingerprints, candidate.Fingerprints).Count > 0)
            reasons.Add("accumulate-product-identity-mismatch");
        if (!ThresholdsMatch(reference.Thresholds, candidate.Thresholds))
            reasons.Add("accumulate-threshold-mismatch");
        // A run whose own thresholds were relaxed relative to policy cannot contribute evidence to
        // a gate decision, no matter how many other runs agree with it.
        if (!ThresholdsMatch(policy, candidate.Thresholds))
            reasons.Add("accumulate-threshold-not-policy-default");
        // Unknown JSON properties survive a round-trip through [JsonExtensionData] but are not
        // hashed, so two reports differing only there share a fingerprint and dedupe would treat
        // one as a repeat of the other. Refuse to reason about evidence we did not model.
        if (candidate.Fingerprints.ExtensionData?.Count > 0 ||
            candidate.Corpus.ExtensionData?.Count > 0 ||
            candidate.Profiles.Any(static profile => profile.ExtensionData?.Count > 0))
        {
            reasons.Add("accumulate-unmodelled-evidence");
        }
        foreach (var name in MergedMetricNames)
        {
            var metric = Select(candidate.Metrics, name);
            // Totals are trusted verbatim by the merge, so a hand-edited run file could otherwise
            // declare any independence it liked. Require the parts to add up to the whole.
            if (!IsCoherent(metric))
            {
                reasons.Add("accumulate-incoherent-metric");
                break;
            }
            // A declared provenance is checkable against the sources the metric counted, so it is
            // checked rather than trusted. Otherwise a hand-edited run file could keep an honest
            // curated denominator and simply relabel what produced it.
            if (!ProvenanceMatchesSources(metric))
            {
                reasons.Add("accumulate-provenance-mismatch");
                break;
            }
        }
        // Static evidence is a property of the corpus, and the corpus fingerprint already had to
        // match. A run whose static counts nevertheless differ is describing a different corpus
        // than it claims to, so it cannot be merged with the reference.
        if (!StaticEvidenceMatches(reference, candidate))
            reasons.Add("accumulate-static-evidence-mismatch");
        // MergeRate sums device-backed counts and takes the minimum of everything else. A source
        // name it does not model would be silently classified as "not device-backed" and quietly
        // de-duplicated, or — before this check — silently summed. The single-run input validator
        // already rejects unknown sources; the merge path must agree with it.
        if (!SampleSourcesAreKnown(candidate))
            reasons.Add("accumulate-unknown-sample-source");
        // Clean first attempts are the only evidence the merge sums across runs, so a run's flow
        // list is trusted arithmetic. Repeating one flow entry five times inside a single file
        // would otherwise multiply one device run by five.
        if (!FlowEvidenceIsCoherent(candidate))
            reasons.Add("accumulate-incoherent-flow-evidence");
        // A flow may only claim real-device evidence if the run says which device produced it.
        // Unattributed device claims are not refused outright — they are recorded — but they must
        // not be indistinguishable from attributed ones.
        if (!DeviceEvidenceIsAttributed(candidate))
            reasons.Add("accumulate-unattributed-device-evidence");
        return reasons;
    }

    /// <summary>The identity facts that must not contradict each other for two runs to be merged.</summary>
    private static readonly (string Name, Func<MauiQualificationFingerprints, string?> Read)[] ProductIdentityFields =
    [
        ("repositoryCommit", static f => f.RepositoryCommit),
        ("testingPackageVersion", static f => f.TestingPackageVersion),
        ("packageId", static f => f.PackageId),
        ("packageFingerprint", static f => f.PackageFingerprint),
        ("toolVersion", static f => f.ToolVersion),
        ("toolFingerprint", static f => f.ToolFingerprint),
    ];

    /// <summary>A fingerprint field is asserted when it is present and is not the "unknown" placeholder.</summary>
    private static bool IsAsserted(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !string.Equals(value, "unknown", StringComparison.Ordinal);

    /// <summary>Product identity fields that both runs assert and disagree about.</summary>
    private static List<string> IdentityConflicts(MauiQualificationFingerprints reference, MauiQualificationFingerprints candidate) =>
        ProductIdentityFields
            .Where(field => IsAsserted(field.Read(reference)) && IsAsserted(field.Read(candidate)) &&
                            !string.Equals(field.Read(reference), field.Read(candidate), StringComparison.Ordinal))
            .Select(static field => field.Name)
            .ToList();

    /// <summary>Product identity fields at least one of the two runs left unasserted.</summary>
    private static List<string> UnverifiedIdentityFields(MauiQualificationFingerprints reference, MauiQualificationFingerprints candidate) =>
        ProductIdentityFields
            .Where(field => !IsAsserted(field.Read(reference)) || !IsAsserted(field.Read(candidate)))
            .Select(static field => field.Name)
            .ToList();

    private static bool FlowEvidenceIsCoherent(MauiPreviewQualificationReport report)
    {        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var flow in report.Metrics.FlakeFirstAttemptStability.Flows)
        {
            if (string.IsNullOrWhiteSpace(flow.FlowId))
                return false;
            if (!seen.Add(flow.FlowId))
                return false;
            if (flow.CleanFirstAttempts < 0 || flow.PassedFirstAttempts < 0)
                return false;
            if (flow.PassedFirstAttempts > flow.CleanFirstAttempts)
                return false;
        }

        return true;
    }

    private static bool DeviceEvidenceIsAttributed(MauiPreviewQualificationReport report) =>        !report.Metrics.FlakeFirstAttemptStability.Flows.Any(static flow => flow.RealDeviceEvidence) ||
        DeclaredDevices(report).Count > 0;

    private static HashSet<string> DeclaredDevices(MauiPreviewQualificationReport report) =>        report.Profiles
            .Where(static profile => profile.RealDevice == true && !string.IsNullOrWhiteSpace(profile.DeviceFingerprint))
            .Select(static profile => profile.DeviceFingerprint!)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Fail is worse than not-qualified, which is worse than pass.</summary>
    private static string WorseOf(string left, string right)
    {
        static int Rank(string status) => status switch
        {
            MauiPreviewQualificationStates.Fail => 2,
            MauiPreviewQualificationStates.NotQualified => 1,
            _ => 0,
        };
        return Rank(right) > Rank(left) ? right : left;
    }

    private static bool SampleSourcesAreKnown(MauiPreviewQualificationReport report) =>
        MergedMetricNames.All(name => (Select(report.Metrics, name).SourceCounts ?? [])
            .All(static count => MauiQualificationSampleSources.IsKnown(count.Source)));

    private static bool IsCoherent(MauiQualificationRateMetric metric)
    {
        if (metric.Numerator < 0 || metric.Denominator < 0 || metric.IndependentEvaluations < 0)
            return false;
        if (metric.Numerator > metric.Denominator || metric.IndependentEvaluations > metric.Denominator)
            return false;
        var counts = metric.SourceCounts ?? [];
        if (counts.Count == 0)
            return metric.Denominator == 0;
        if (counts.Any(static count => count.Numerator < 0 || count.Denominator < 0 ||
            count.IndependentEvaluations < 0 || count.Numerator > count.Denominator ||
            count.IndependentEvaluations > count.Denominator))
        {
            return false;
        }
        return counts.Sum(static count => (long)count.Denominator) == metric.Denominator &&
            counts.Sum(static count => (long)count.Numerator) == metric.Numerator &&
            counts.Sum(static count => (long)count.IndependentEvaluations) == metric.IndependentEvaluations;
    }

    private static bool StaticEvidenceMatches(
        MauiPreviewQualificationReport reference,
        MauiPreviewQualificationReport candidate)
    {
        foreach (var name in MergedMetricNames)
        {
            if (!StaticCounts(Select(reference.Metrics, name)).SequenceEqual(StaticCounts(Select(candidate.Metrics, name)), StringComparer.Ordinal))
                return false;
        }
        return true;
    }

    private static IEnumerable<string> StaticCounts(MauiQualificationRateMetric metric) =>
        (metric.SourceCounts ?? [])
            .Where(static count => MauiQualificationSampleSources.IsStatic(count.Source))
            .OrderBy(static count => count.Source, StringComparer.Ordinal)
            .Select(static count => $"{count.Source}:{count.Numerator}/{count.Denominator}/{count.IndependentEvaluations}");

    /// <summary>
    /// A metric's declared provenance is partly derivable from the sources it counted, so that part
    /// is verified rather than trusted. The rule mirrors how the per-run evaluator assigns it: the
    /// gates read the independent subset when there is one, and only device-backed evidence arrived
    /// with the run.
    /// <para>
    /// Deliberately one-directional. It refuses a claim the sources cannot support — "the run
    /// observed this" over evidence this harness scored — and accepts an understatement, because
    /// rejecting an understatement would discard honest evidence written by an earlier version of
    /// this tool, and a discarded run never reaches the gates at all. The harness-versus-shipped
    /// axis is not checkable from sources at all; the coverage gate handles it by reporting every
    /// pass as <c>provenance-self-reported</c> and naming whose word it is taking.
    /// </para>
    /// </summary>
    private static bool ProvenanceMatchesSources(MauiQualificationRateMetric metric)
    {
        var declared = metric.Exercises?.Kind;
        if (metric.Denominator <= 0 || string.IsNullOrWhiteSpace(declared))
            return true;
        if (!string.Equals(declared, MauiQualificationMetricProvenanceKinds.SampleSupplied, StringComparison.Ordinal))
            return true;
        var counts = (metric.SourceCounts ?? []).Where(static count => count.Denominator > 0).ToList();
        if (counts.Count == 0)
            return true;
        var judged = counts.Where(static count => count.IndependentEvaluations > 0).ToList();
        if (judged.Count == 0)
            judged = counts;
        // Only device-backed evidence can be credited to the submitting run. A source this harness
        // does not model is not device-backed, so a "the run observed it" claim over it is refused.
        return judged.All(static count => string.Equals(
            count.Source,
            MauiQualificationSampleSources.DeviceBacked,
            StringComparison.Ordinal));
    }

    /// <summary>
    /// Compares every threshold the contract carries, not the subset the gates happen to read
    /// today. A run shipping <c>minimumCleanFirstAttemptsPerTier1Flow: 1</c> is describing a
    /// different policy than the one being enforced, and merging it would mean publishing its
    /// evidence under a claim it was never held to — even though the merge re-evaluates against
    /// the compiled defaults and so would not adopt the relaxed number itself.
    /// </summary>
    private static bool ThresholdsMatch(MauiQualificationGateThresholds left, MauiQualificationGateThresholds right) =>
        left.ConfidenceLevel.Equals(right.ConfidenceLevel) &&
        left.MinimumRepairPrecision.Equals(right.MinimumRepairPrecision) &&
        left.MinimumRepairEvaluations == right.MinimumRepairEvaluations &&
        left.MinimumNoRepairEvaluations == right.MinimumNoRepairEvaluations &&
        left.MaximumFalseHeals == right.MaximumFalseHeals &&
        left.MinimumSelectorStability.Equals(right.MinimumSelectorStability) &&
        left.MinimumSelectorObservations == right.MinimumSelectorObservations &&
        left.MinimumClassificationAccuracy.Equals(right.MinimumClassificationAccuracy) &&
        left.MinimumClassificationEvaluations == right.MinimumClassificationEvaluations &&
        left.MaximumCalibrationEce.Equals(right.MaximumCalibrationEce) &&
        left.MinimumCleanFirstAttemptsPerTier1Flow == right.MinimumCleanFirstAttemptsPerTier1Flow &&
        left.MinimumFirstAttemptStability.Equals(right.MinimumFirstAttemptStability) &&
        left.HostOperationP95BudgetMs.Equals(right.HostOperationP95BudgetMs) &&
        left.RequireRealAndroidDeviceEvidence == right.RequireRealAndroidDeviceEvidence &&
        left.RequireRecordedReviews == right.RequireRecordedReviews;

    private static MauiQualificationRateMetric Select(MauiQualificationMetrics metrics, string name) => name switch
    {
        "recordingValidity" => metrics.RecordingValidity,
        "selectorStability" => metrics.SelectorStability,
        "repairPrecision" => metrics.RepairPrecision,
        "repairRecall" => metrics.RepairRecall,
        "falseHeals" => metrics.FalseHeals,
        "abstention" => metrics.Abstention,
        "classificationAccuracy" => metrics.ClassificationAccuracy,
        _ => new MauiQualificationRateMetric(),
    };

    /// <summary>
    /// Merges one rate metric across runs. Every accepted run shares a corpus fingerprint, so its
    /// static (curated / curated-derived / generated) evidence is byte-identical to every other
    /// run's: it is counted exactly once. Only device-backed evidence is genuinely per-run and is
    /// therefore the only thing summed. Without this, running the same static corpus 100 times
    /// would report 100 independent evaluations of one corpus and walk every count gate to pass.
    /// </summary>
    private static MauiQualificationRateMetric MergeRate(IEnumerable<MauiQualificationRateMetric> metrics)
    {
        var list = metrics.Where(static metric => metric is not null).ToList();
        var contributing = list.Where(static metric => metric.Denominator > 0).ToList();
        var merged = new List<MauiQualificationRateSourceCount>();
        foreach (var group in contributing
            .SelectMany(static metric => metric.SourceCounts ?? [])
            .GroupBy(static count => count.Source, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            if (!MauiQualificationSampleSources.DeviceBacked.Equals(group.Key, StringComparison.Ordinal))
            {
                // Only device-backed evidence is a fresh observation. Everything else is a re-read
                // of the same corpus, and an unrecognised source name is refused outright by
                // Incompatibilities before it reaches here — taking the minimum keeps this
                // fail-closed if that ever stops holding.
                merged.Add(new MauiQualificationRateSourceCount
                {
                    Source = group.Key,
                    Numerator = group.Min(static count => Math.Max(0, count.Numerator)),
                    Denominator = group.Min(static count => Math.Max(0, count.Denominator)),
                    IndependentEvaluations = group.Min(static count => Math.Max(0, count.IndependentEvaluations)),
                });
                continue;
            }

            // Summed in long and clamped: a checked int overflow here throws out of the
            // accumulator's catch filter and takes the whole command down instead of failing the
            // merge closed.
            merged.Add(new MauiQualificationRateSourceCount
            {
                Source = group.Key,
                Numerator = (int)Math.Clamp(group.Sum(static count => (long)Math.Max(0, count.Numerator)), 0, int.MaxValue),
                Denominator = (int)Math.Clamp(group.Sum(static count => (long)Math.Max(0, count.Denominator)), 0, int.MaxValue),
                IndependentEvaluations = (int)Math.Clamp(group.Sum(static count => (long)Math.Max(0, count.IndependentEvaluations)), 0, int.MaxValue),
            });
        }

        var numerator = (int)Math.Clamp(merged.Sum(static count => (long)count.Numerator), 0, int.MaxValue);
        var denominator = (int)Math.Clamp(merged.Sum(static count => (long)count.Denominator), 0, int.MaxValue);
        var independent = (int)Math.Clamp(merged.Sum(static count => (long)count.IndependentEvaluations), 0, int.MaxValue);
        // A numerator larger than its denominator is incoherent evidence, not a rate above 1.
        numerator = Math.Min(numerator, denominator);
        independent = Math.Min(independent, denominator);
        var independentNumerator = Math.Min(
            (int)Math.Clamp(
                merged.Where(static count => MauiQualificationSampleSources.IsIndependent(count.Source))
                    .Sum(static count => (long)Math.Min(count.Numerator, count.IndependentEvaluations)),
                0,
                int.MaxValue),
            independent);
        var confidence = list
            .Select(static metric => metric.ConfidenceInterval?.ConfidenceLevel)
            .FirstOrDefault(static level => level is > 0 and < 1) ?? 0.95;
        return new MauiQualificationRateMetric
        {
            State = denominator == 0 ? "missing" : "measured",
            Numerator = numerator,
            Denominator = denominator,
            IndependentEvaluations = independent,
            IndependentNumerator = independentNumerator,
            Value = denominator == 0 ? null : (double)numerator / denominator,
            ConfidenceInterval = denominator == 0
                ? null
                : MauiQualificationStatistics.WilsonInterval(numerator, denominator, confidence),
            IndependentConfidenceInterval = independent == 0
                ? null
                : MauiQualificationStatistics.WilsonInterval(independentNumerator, independent, confidence),
            SampleSources = list
                .SelectMany(static metric => metric.SampleSources ?? [])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static source => source, StringComparer.Ordinal)
                .ToList(),
            SourceCounts = merged,
            // Independence is conjunctive: one pooled or non-device contributor makes the
            // merged total non-independent, exactly as it does within a single run.
            IndependentDeviceRuns = denominator == 0
                ? null
                : contributing.Count > 0 && contributing.All(static metric => metric.IndependentDeviceRuns == true),
            Exercises = MergeExercises(contributing),
        };
    }

    /// <summary>
    /// Merges what the contributing runs actually exercised. Conjunctive, like independence: the
    /// merged kind is the weakest any contributor declared, so pooling can never launder a
    /// harness-scored number into product evidence, and it cannot be decided by the order the runs
    /// happened to be sorted in. A contributor that carried evidence but declared nothing is treated
    /// as <c>unknown</c> for the same reason — silence is not a claim of product provenance.
    /// Disagreement about the component itself is reported rather than resolved.
    /// </summary>
    private static MauiQualificationMetricProvenance? MergeExercises(
        IReadOnlyList<MauiQualificationRateMetric> contributing)
    {
        var declared = contributing
            .Select(static metric => metric.Exercises)
            .Where(static exercises => exercises is not null)
            .Select(static exercises => exercises!)
            .ToList();
        if (declared.Count == 0)
            return null;

        // Every contributing metric here already has a non-zero denominator, so silence is a
        // genuine omission rather than "nothing to declare".
        var undeclared = contributing.Count(static metric => metric.Exercises is null);
        var components = declared
            .Select(static exercises => exercises.Component ?? MauiQualificationMetricProvenance.UndeclaredComponent)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static component => component, StringComparer.Ordinal)
            .ToList();
        // Ranked rather than first-match: with every contributor claiming product evidence, a
        // first-match rule would let run order decide between shipped-analyzer and sample-supplied,
        // and merge order here is wall-clock sort order.
        var weakest = declared
            .OrderBy(static exercises => MauiQualificationMetricProvenanceKinds.Strength(exercises.Kind))
            .ThenBy(static exercises => exercises.Kind, StringComparer.Ordinal)
            .First();
        if (undeclared > 0)
        {
            // The merged kind drops to unknown, but the specific disclosure the declared
            // contributors made is kept in the note: "we cannot tell" is less actionable than
            // "one contributor was harness-scored and another said nothing", and the gate reports
            // both facts rather than collapsing them.
            return new MauiQualificationMetricProvenance
            {
                Component = string.Join(" + ", components.Append("undeclared")),
                Kind = MauiQualificationMetricProvenanceKinds.Unknown,
                Note = $"{undeclared} contributing run(s) counted samples without declaring what produced them, "
                    + $"so the merged total cannot claim any provenance. The weakest kind the runs that did "
                    + $"declare reported was '{weakest.Kind}'.",
            };
        }

        return new MauiQualificationMetricProvenance
        {
            Component = components.Count == 1 ? components[0] : string.Join(" + ", components),
            Kind = weakest.Kind,
            Note = weakest.Note,
        };
    }

    /// <summary>
    /// Sums clean first attempts per Tier-1 flow across runs. This is the whole reason
    /// <c>--accumulate</c> exists rather than a higher <c>--repeat</c> cap: the stability gate wants
    /// 100 clean first attempts per flow and the harness caps a single run at 20, so the count has
    /// to come from independent jobs. Unlike the static corpus share, these are genuinely fresh
    /// device observations and summing them is correct — duplicate runs are already refused by the
    /// evidence fingerprint, which hashes the per-flow attempt counts.
    ///
    /// Independence itself is self-reported and cannot be verified from a JSON file. What the
    /// merge can do is publish the shape of the claim: how many runs and how many distinct
    /// declared devices a flow's total came from, so a reviewer can see 100 attempts arriving as
    /// 5 runs on 5 devices rather than as 5 restatements of one.
    /// </summary>
    private static List<MauiQualificationFlowAttemptSummary> MergeFirstAttemptFlows(
        IReadOnlyList<MauiPreviewQualificationReport> accepted)
    {
        var merged = new List<MauiQualificationFlowAttemptSummary>();
        foreach (var group in accepted
            .SelectMany(static report => report.Metrics.FlakeFirstAttemptStability.Flows
                .Where(static flow => !string.IsNullOrWhiteSpace(flow.FlowId))
                .Select(flow => (Flow: flow, Devices: DeclaredDevices(report))))
            .GroupBy(static entry => entry.Flow.FlowId!, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var clean = (int)Math.Clamp(group.Sum(static entry => (long)Math.Max(0, entry.Flow.CleanFirstAttempts)), 0, int.MaxValue);
            var passed = (int)Math.Clamp(group.Sum(static entry => (long)Math.Max(0, entry.Flow.PassedFirstAttempts)), 0, int.MaxValue);
            merged.Add(new MauiQualificationFlowAttemptSummary
            {
                FlowId = group.Key,
                CleanFirstAttempts = clean,
                PassedFirstAttempts = Math.Min(passed, clean),
                Stability = clean == 0 ? null : (double)Math.Min(passed, clean) / clean,
                // One run without real-device evidence taints the merged flow. A flow is only
                // device-backed if every contribution to it was.
                RealDeviceEvidence = group.All(static entry => entry.Flow.RealDeviceEvidence),
                ContributingRuns = group.Count(),
                ContributingDevices = group
                    .SelectMany(static entry => entry.Devices)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
            });
        }
        return merged;
    }

    private static void AddGates(MauiQualificationAccumulation accumulation)
    {
        var thresholds = accumulation.Thresholds;
        AddRateGate(
            accumulation,
            "accumulated-repair-precision",
            "repairPrecision",
            thresholds.MinimumRepairEvaluations,
            thresholds.MinimumRepairPrecision,
            "repair-evaluation-count-insufficient",
            "repair-precision-lower-bound-below-threshold");
        AddRateGate(
            accumulation,
            "accumulated-selector-stability",
            "selectorStability",
            thresholds.MinimumSelectorObservations,
            thresholds.MinimumSelectorStability,
            "selector-observation-count-insufficient",
            "selector-stability-below-threshold",
            // Mirrors AddSelectorStabilityGate, which judges the point estimate. Using a different
            // rule here would make an accumulated result disagree with its own per-run gate.
            useLowerBound: false);
        AddRateGate(
            accumulation,
            "accumulated-classification-accuracy",
            "classificationAccuracy",
            thresholds.MinimumClassificationEvaluations,
            thresholds.MinimumClassificationAccuracy,
            "classification-evaluation-count-insufficient",
            "classification-accuracy-lower-bound-below-threshold");

        var falseHeals = accumulation.Metrics.GetValueOrDefault("falseHeals") ?? new MauiQualificationRateMetric();
        var falseHealStatus = falseHeals.IndependentEvaluations < thresholds.MinimumNoRepairEvaluations
            ? MauiPreviewQualificationStates.NotQualified
            : falseHeals.Numerator <= thresholds.MaximumFalseHeals
                ? MauiPreviewQualificationStates.Pass
                : MauiPreviewQualificationStates.Fail;
        accumulation.Gates.Add(new MauiQualificationGateResult
        {
            GateId = "accumulated-zero-false-heals",
            Status = falseHealStatus,
            Message = falseHealStatus == MauiPreviewQualificationStates.Pass
                ? "No false heal was observed across the accumulated independent no-repair denominator."
                : "Accumulated independent no-repair evidence is insufficient or includes a false heal.",
            ReasonCodes = falseHealStatus == MauiPreviewQualificationStates.Pass
                ? []
                : falseHeals.IndependentEvaluations < thresholds.MinimumNoRepairEvaluations
                    ? ["no-repair-evaluation-count-insufficient"]
                    : ["false-heal-observed"],
        });

        // The same disclosure the per-run report carries. Without it, --accumulate --fail-on-non-pass
        // — which is what CI reads — would return pass on a merged total whose every observation was
        // scored by the harness against its own expectations.
        accumulation.Gates.Add(MauiPreviewQualificationGateEvaluator.BuildProductAnalyzerCoverageGate(
            new[] { "repairPrecision", "repairRecall", "falseHeals", "abstention", "classificationAccuracy" }
                .Select(name =>
                {
                    var metric = accumulation.Metrics.GetValueOrDefault(name);
                    return (Name: name, Denominator: metric?.Denominator ?? 0, Exercises: metric?.Exercises);
                })));

        var flows = accumulation.FirstAttemptFlows;
        var firstAttemptReasons = new List<string>();
        var androidScope = string.Equals(accumulation.Platform, "android", StringComparison.Ordinal);
        if (!androidScope)
            firstAttemptReasons.Add("android-platform-scope-missing");
        if (flows.Count == 0)
            firstAttemptReasons.Add("tier1-flow-declaration-missing");
        // Each flow is judged on its own evidence and the worst verdict wins. Pooling the reasons
        // would let one unexercised flow downgrade another flow's measured failure to
        // not-qualified, and accumulation is exactly where partial flow coverage is normal.
        var firstAttemptStatus = androidScope && flows.Count > 0
            ? MauiPreviewQualificationStates.Pass
            : MauiPreviewQualificationStates.NotQualified;
        foreach (var flow in flows)
        {
            var flowStatus = MauiPreviewQualificationStates.Pass;
            if (!flow.RealDeviceEvidence)
                firstAttemptReasons.Add("android-real-device-evidence-missing");
            if (flow.CleanFirstAttempts < thresholds.MinimumCleanFirstAttemptsPerTier1Flow)
            {
                firstAttemptReasons.Add("android-clean-first-attempt-count-insufficient");
                flowStatus = MauiPreviewQualificationStates.NotQualified;
            }
            else
            {
                if (!flow.RealDeviceEvidence)
                    flowStatus = MauiPreviewQualificationStates.Fail;
                if (flow.Stability < thresholds.MinimumFirstAttemptStability)
                {
                    firstAttemptReasons.Add("android-first-attempt-stability-below-threshold");
                    flowStatus = MauiPreviewQualificationStates.Fail;
                }
            }

            firstAttemptStatus = WorseOf(firstAttemptStatus, flowStatus);
        }
        accumulation.Gates.Add(new MauiQualificationGateResult
        {
            GateId = "accumulated-tier1-first-attempts",
            Status = firstAttemptStatus,
            Message = firstAttemptStatus == MauiPreviewQualificationStates.Pass
                ? "Every declared Tier-1 flow reached the clean real-device first-attempt count across accumulated runs."
                : "Accumulated Tier-1 first-attempt evidence is insufficient or below the stability threshold.",
            ReasonCodes = firstAttemptReasons.Distinct(StringComparer.Ordinal).OrderBy(static code => code, StringComparer.Ordinal).ToList(),
        });

        if (accumulation.RejectedRuns > 0)
        {
            accumulation.Gates.Add(new MauiQualificationGateResult
            {
                GateId = "accumulated-run-compatibility",
                Status = MauiPreviewQualificationStates.NotQualified,
                Message = "At least one run was excluded from accumulation because it did not agree with the reference run.",
                ReasonCodes = accumulation.Runs
                    .Where(static run => !run.Accepted)
                    .SelectMany(static run => run.ReasonCodes)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static code => code, StringComparer.Ordinal)
                    .ToList(),
            });
        }
    }

    private static void AddRateGate(
        MauiQualificationAccumulation accumulation,
        string gateId,
        string metricName,
        int minimumDenominator,
        double minimumLowerBound,
        string insufficientCode,
        string belowThresholdCode,
        bool useLowerBound = true)
    {
        var metric = accumulation.Metrics.GetValueOrDefault(metricName) ?? new MauiQualificationRateMetric();
        // Reads the interval over the independent subset. The pooled interval narrows every time a
        // derived clone or a generated mutant is added, which is not new information.
        var observed = useLowerBound ? metric.IndependentConfidenceInterval?.Lower : metric.Value;
        // Counts independent evaluations, not raw denominator: restating one seed a hundred times
        // is one trial, and the per-run gates hold the same line.
        var status = metric.IndependentEvaluations < minimumDenominator
            ? MauiPreviewQualificationStates.NotQualified
            : observed >= minimumLowerBound
                ? MauiPreviewQualificationStates.Pass
                : MauiPreviewQualificationStates.Fail;
        var criterion = useLowerBound ? "conservative Wilson lower-bound" : "point-estimate";
        accumulation.Gates.Add(new MauiQualificationGateResult
        {
            GateId = gateId,
            Status = status,
            Message = status == MauiPreviewQualificationStates.Pass
                ? $"Accumulated {metricName} meets the {criterion} threshold."
                : $"Accumulated {metricName} lacks enough independent evaluations or misses the {criterion} threshold.",
            ReasonCodes = status == MauiPreviewQualificationStates.Pass
                ? []
                : metric.IndependentEvaluations < minimumDenominator
                    ? [insufficientCode]
                    : [belowThresholdCode],
        });
    }
}
