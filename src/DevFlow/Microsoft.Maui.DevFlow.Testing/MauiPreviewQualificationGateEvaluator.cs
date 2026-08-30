using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// Deterministically aggregates redacted static and device evidence into preview gate results.
/// It does not contact a device, read a workspace, replay a flow, or apply a proposal.
/// </summary>
public static class MauiPreviewQualificationGateEvaluator
{
    private const string Android = "android";
    private const string TierOne = "tier-1";

    /// <summary>Evaluates all Android engineering-preview gates from explicitly supplied evidence.</summary>
    public static MauiPreviewQualificationReport Evaluate(
        MauiPreviewQualificationInput? input,
        DateTimeOffset? generatedAt = null)
    {
        input ??= new MauiPreviewQualificationInput();
        var validation = MauiPreviewQualificationInputValidator.Validate(input);
        var thresholds = NormalizeThresholds(input.Thresholds ?? new MauiQualificationGateThresholds());
        var samples = (input.Samples ?? [])
            .Where(static sample => sample is not null)
            .ToList();
        var platform = NormalizePlatform(input.Platform);

        var report = new MauiPreviewQualificationReport
        {
            GeneratedAt = generatedAt ?? DateTimeOffset.UtcNow,
            Platform = platform,
            Fingerprints = Sanitize(input.Fingerprints),
            Profiles = (input.Profiles ?? []).Select(Sanitize).ToList(),
            AppleQa = SanitizeAppleQa(input.AppleQa),
            FeatureFlags = Sanitize(input.FeatureFlags),
            Review = Sanitize(input.Review),
            Corpus = Sanitize(input.Corpus),
            Thresholds = thresholds,
            ArtifactRefs = (input.ArtifactRefs ?? []).Select(Sanitize).ToList(),
            Exclusions = BuildExclusions(input),
        };

        foreach (var error in validation.Errors)
            AddReason(report, error.Code, "error", error.Message);

        report.Metrics = BuildMetrics(input, samples, platform, thresholds);
        AddCorpusGate(report);
        AddFeatureFlagGate(report);
        AddReviewGate(report, thresholds);
        AddRequiredEvidenceGate(report, input, samples);
        AddRepairPrecisionGate(report, thresholds);
        AddClassificationAccuracyGate(report, thresholds);
        AddFalseHealGate(report, thresholds);
        AddProductAnalyzerCoverageGate(report);
        AddSelectorStabilityGate(report, thresholds);
        AddCalibrationGate(report, thresholds);
        AddPrivacySecurityGate(report);
        AddHostPerformanceGate(report, thresholds);
        AddDeviceOverheadGate(report);
        AddAndroidFirstAttemptGate(report, platform, thresholds);

        if (validation.Errors.Count > 0)
        {
            report.Gates.Add(new MauiQualificationGateResult
            {
                GateId = "input-contract",
                Status = MauiPreviewQualificationStates.Fail,
                Message = "Qualification input violates the versioned contract.",
                ReasonCodes = validation.Errors.Select(static error => error.Code).Distinct(StringComparer.Ordinal).ToList(),
            });
        }

        report.Status = AggregateStatus(report.Gates);
        foreach (var gate in report.Gates.Where(static gate => gate.Status != MauiPreviewQualificationStates.Pass))
        {
            AddReason(
                report,
                $"gate-{gate.GateId}-{gate.Status}",
                gate.Status == MauiPreviewQualificationStates.Fail ? "error" : "warning",
                gate.Message);
        }
        return report;
    }

    private static MauiQualificationMetrics BuildMetrics(
        MauiPreviewQualificationInput input,
        IReadOnlyList<MauiQualificationExecutionSample> samples,
        string platform,
        MauiQualificationGateThresholds thresholds)
    {
        var included = samples.Where(static sample => sample.Excluded != true).ToList();
        var repair = included
            .Where(static sample => sample.RepairProposed == true && sample.RepairCorrect.HasValue)
            .ToList();
        var repairExpected = included
            .Where(static sample => sample.RepairExpected == true)
            .ToList();
        var noRepair = included
            .Where(static sample => sample.NoRepairExpected == true)
            .ToList();
        var abstention = noRepair
            .Where(static sample => sample.Abstained.HasValue)
            .ToList();
        var deviceSelector = included
            .Where(sample => IsInPlatformScope(sample, platform) && IsRealDeviceSample(sample) && sample.SelectorStable.HasValue)
            .ToList();
        var recording = included.Where(static sample => sample.RecordingValid.HasValue).ToList();
        var calibration = MauiQualificationStatistics.CalculateCalibration(
            included
                .Where(static sample => sample.ProbabilityLikeConfidence.HasValue && sample.ExpectedOutcome.HasValue)
                .Select(static sample => (sample.ProbabilityLikeConfidence!.Value, sample.ExpectedOutcome!.Value)),
            bucketCount: 10);
        var diagnosis = BuildDurationMetric(
            "time-to-diagnosis",
            included.Where(static sample => sample.TimeToDiagnosisMs is >= 0).Select(static sample => sample.TimeToDiagnosisMs!.Value));
        var trace = BuildTraceReportMetric(included);
        var overhead = SanitizeRuntimeOverhead(input.RuntimeOverhead);
        if (overhead.DeviceOverhead is null)
            overhead.DeviceOverhead = new MauiQualificationDurationMetric();
        if (overhead.DeviceOverhead.State == "missing" && string.IsNullOrWhiteSpace(overhead.DeviceOverhead.MissingReason))
        {
            overhead.DeviceOverhead.MissingReason =
                "No Android pilot artifact supplied device-overhead evidence.";
        }

        var privacy = BuildPrivacySecurityMetric(input, included);
        var firstAttempts = BuildFirstAttemptMetric(input, samples, platform, thresholds);
        var classification = included
            .Where(static sample =>
                MauiFlowFailureClassifier.IsKnownFailureClass(sample.ExpectedFailureClass) &&
                sample.ObservedFailureClass is not null)
            .ToList();

        // Deliberately a compile-time literal, not a value read from the supplied corpus summary.
        // MauiPreviewQualificationInput.Corpus is caller-supplied, so deriving this from
        // corpus.exercisesShippedAnalyzer would let a hand-written input assert that harness rules
        // are the shipped analyzer — the strongest claim the report can make, on no evidence.
        // Keeping the disclosure unforgeable is worth more than making it move automatically when
        // the runner is rewired. Flipping this literal alone is caught by
        // Corpus_DeclaresThatItScoresRepairWithHarnessRulesRatherThanTheShippedAnalyzer and by the
        // committed baseline diff; the tripwire watches the runner's own declaration instead.
        const string corpusKind = MauiQualificationMetricProvenanceKinds.HarnessLocalRules;
        const string corpusNote = HarnessRuleNote;

        return new MauiQualificationMetrics
        {
            RecordingValidity = BuildRate(
                recording,
                static sample => sample.RecordingValid == true,
                independentDeviceRuns: recording.Count > 0 && recording.All(IsRealDeviceSample),
                thresholds.ConfidenceLevel),
            RepairPrecision = BuildRate(
                repair,
                static sample => sample.RepairCorrect == true,
                independentDeviceRuns: repair.Count > 0 && repair.All(IsRealDeviceSample),
                thresholds.ConfidenceLevel,
                staticComponent: "MauiPreviewQualificationCorpusRunner.EvaluateRepairFixture",
                staticKind: corpusKind,
                staticNote: corpusNote),
            RepairRecall = BuildRate(
                repairExpected,
                static sample => sample.RepairProposed == true && sample.RepairCorrect == true,
                independentDeviceRuns: repairExpected.Count > 0 && repairExpected.All(IsRealDeviceSample),
                thresholds.ConfidenceLevel,
                staticComponent: "MauiPreviewQualificationCorpusRunner.EvaluateRepairFixture",
                staticKind: corpusKind,
                staticNote: corpusNote),
            FalseHeals = BuildRate(
                noRepair,
                static sample => sample.FalseHeal == true,
                independentDeviceRuns: noRepair.Count > 0 && noRepair.All(IsRealDeviceSample),
                thresholds.ConfidenceLevel,
                staticComponent: "MauiPreviewQualificationCorpusRunner.EvaluateFixture",
                staticKind: corpusKind,
                staticNote: corpusNote,
                // AddFalseHealGate compares Numerator, not IndependentNumerator, so this is the one
                // metric whose pooled share is compared against a threshold rather than only being
                // diffed — subject to that gate's count check short-circuiting ahead of it.
                pooledNumeratorIsGated: true),
            Abstention = BuildRate(
                abstention,
                static sample => sample.Abstained == true,
                independentDeviceRuns: noRepair.Count > 0 && noRepair.All(IsRealDeviceSample),
                thresholds.ConfidenceLevel,
                staticComponent: "MauiPreviewQualificationCorpusRunner.EvaluateFixture",
                staticKind: corpusKind,
                staticNote: corpusNote),
            SelectorStability = BuildRate(
                deviceSelector,
                static sample => sample.SelectorStable == true,
                independentDeviceRuns: deviceSelector.Count > 0,
                thresholds.ConfidenceLevel),
            ClassificationAccuracy = BuildRate(
                classification,
                static sample => string.Equals(
                    NormalizeFailureClass(sample.ExpectedFailureClass),
                    NormalizeFailureClass(sample.ObservedFailureClass),
                    StringComparison.Ordinal),
                independentDeviceRuns: classification.Count > 0 && classification.All(IsRealDeviceSample),
                thresholds.ConfidenceLevel,
                // A case whose evidence already stamped its failure class is answered by copying,
                // not by classifying. Those cases are reported but never count toward the gate's
                // minimum, or the accuracy headline would measure the corpus, not the classifier.
                independent: static sample =>
                    MauiQualificationSampleSources.IsIndependent(NormalizeSource(sample.Source)) &&
                    sample.FailureClassInferred == true,
                staticComponent: "MauiFlowFailureClassifier.Classify",
                staticKind: MauiQualificationMetricProvenanceKinds.ShippedAnalyzer,
                staticNote: "The same classifier entry point MauiFlowRunner and WorkflowRunCoordinator "
                    + "call at runtime. The expected label is corpus ground truth; the observed label "
                    + "is the product's answer to replay facts the harness derived, so the harness "
                    + "still chooses the classifier's input. Most static cases reach the classifier "
                    + "through a legacy-kind constant table, which exercises far less of it than "
                    + "cases that require precedence resolution.",
                // The source name cannot establish that the shipped classifier produced a label.
                // Only the component that called it can say so, so an unstamped sample downgrades
                // this metric to unknown instead of borrowing the classifier's name.
                staticKindAttested: static sample => string.Equals(
                    sample.ObservedFailureClassProducer,
                    MauiPreviewQualificationCorpusRunner.ClassifierEntryPoint,
                    StringComparison.Ordinal)),
            ClassificationMatrix = BuildClassificationMatrix(classification),
            Calibration = calibration,
            TimeToDiagnosis = diagnosis,
            TraceReportSize = trace,
            RuntimeOverhead = overhead,
            FlakeFirstAttemptStability = firstAttempts,
            HumanDecisionOutcomes = BuildHumanDecisionOutcomes(included),
            PrivacySecurityEscapes = privacy,
        };
    }

    private static MauiQualificationRateMetric BuildRate(
        IReadOnlyList<MauiQualificationExecutionSample> samples,
        Func<MauiQualificationExecutionSample, bool> success,
        bool independentDeviceRuns,
        double confidenceLevel,
        Func<MauiQualificationExecutionSample, bool>? independent = null,
        string? staticComponent = null,
        string? staticKind = null,
        string? staticNote = null,
        Func<MauiQualificationExecutionSample, bool>? staticKindAttested = null,
        bool pooledNumeratorIsGated = false)
    {
        var denominator = samples.Count;
        var numerator = samples.Count(success);
        independent ??= static sample => MauiQualificationSampleSources.IsIndependent(NormalizeSource(sample.Source));
        var independentSamples = samples.Where(independent).ToList();
        var independentDenominator = independentSamples.Count;
        var independentNumerator = independentSamples.Count(success);
        return new MauiQualificationRateMetric
        {
            State = denominator == 0 ? "missing" : "measured",
            Numerator = numerator,
            Denominator = denominator,
            Value = denominator == 0 ? null : (double)numerator / denominator,
            ConfidenceInterval = denominator == 0
                ? null
                : MauiQualificationStatistics.WilsonInterval(numerator, denominator, confidenceLevel),
            SampleSources = samples
                .Select(static sample => NormalizeSource(sample.Source))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static source => source, StringComparer.Ordinal)
                .ToList(),
            // Per-source counts keep a pooled denominator honest: 0/316 is reported as the
            // curated and generated shares it is actually made of.
            SourceCounts = samples
                .GroupBy(static sample => NormalizeSource(sample.Source), StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(group => new MauiQualificationRateSourceCount
                {
                    Source = group.Key,
                    Numerator = group.Count(success),
                    Denominator = group.Count(),
                    IndependentEvaluations = group.Count(independent),
                })
                .ToList(),
            IndependentEvaluations = independentDenominator,
            IndependentNumerator = independentNumerator,
            // Restating one observation 30 times narrows a pooled Wilson interval toward certainty
            // without adding a single new fact. Gates read this interval; the pooled one above is
            // published for disclosure only.
            IndependentConfidenceInterval = independentDenominator == 0
                ? null
                : MauiQualificationStatistics.WilsonInterval(independentNumerator, independentDenominator, confidenceLevel),
            IndependentDeviceRuns = denominator == 0 ? null : independentDeviceRuns,
            // Judged over the independent subset when there is one, because that is the share the
            // gates read. A metric whose gate-carrying evidence is device-backed is not made
            // harness-scored by generated mutants sitting in its pooled denominator, and a metric
            // whose gate-carrying evidence is the static corpus is not rescued by them either.
            Exercises = staticComponent is null || staticKind is null
                ? null
                : Exercised(
                    independentSamples.Count > 0 ? independentSamples : samples,
                    samples,
                    staticComponent,
                    staticKind,
                    staticNote ?? string.Empty,
                    staticKindAttested,
                    pooledNumeratorIsGated),
        };
    }

    /// <summary>
    /// Describes what actually decided a metric's observations, from the samples it counted.
    /// Static corpus samples are scored by <see cref="MauiPreviewQualificationCorpusRunner"/>;
    /// device-backed samples were observed by the run that submitted them. A judged subset holding
    /// both never publishes more than the weaker of the two claims, because pooling must not
    /// upgrade what a number measures. When the static kind is the stronger one — as it is for
    /// <c>classificationAccuracy</c>, whose samples name the shipped classifier — the mix drops all
    /// the way to <c>unknown</c>, since no single component produced the subset and no weaker named
    /// kind describes it either.
    /// </summary>
    /// <param name="judged">
    /// The subset the gates read, which is what the kind describes.
    /// </param>
    /// <param name="all">
    /// Every sample in the denominator, so the note can disclose statically scored samples that the
    /// judged subset excluded rather than implying the report scored nothing.
    /// </param>
    /// <param name="staticKindAttested">
    /// Must hold for every judged sample — statically scored and device-backed alike — before
    /// <paramref name="staticKind"/> is claimed. A device is no better placed than a fixture to say
    /// which code produced a label, so exempting device rows would let one stamped fixture speak for
    /// any number of unstamped ones. It exists because a kind like <c>shipped-analyzer</c> asserts
    /// which code produced an observation, and the source name cannot establish that — without a
    /// per-sample stamp from whatever actually called the product, a hand-written input would borrow
    /// the shipped classifier's name simply by not being device-backed. An unattested sample yields
    /// <c>unknown</c>, which the coverage gate treats as a failure rather than as evidence.
    /// </param>
    /// <param name="pooledNumeratorIsGated">
    /// True when a gate compares this metric's <em>pooled</em> numerator against a threshold rather
    /// than reading only its independent subset. Only <c>falseHeals</c> does — <c>AddFalseHealGate</c>
    /// compares <c>Numerator</c> — so only its note may say a gate reads the pooled share, and only
    /// with the count short-circuit that precedes it stated alongside.
    /// </param>
    private static MauiQualificationMetricProvenance? Exercised(
        IReadOnlyList<MauiQualificationExecutionSample> judged,
        IReadOnlyList<MauiQualificationExecutionSample> all,
        string staticComponent,
        string staticKind,
        string staticNote,
        Func<MauiQualificationExecutionSample, bool>? staticKindAttested = null,
        bool pooledNumeratorIsGated = false)
    {
        if (judged.Count == 0)
            return null;
        // The question here is "which code decided this observation", not "is this attested
        // real-device evidence". Only device-backed evidence arrived with the run; everything
        // else — including a source this harness does not model — was either read from corpus
        // files or cannot be credited to a submitting run at all. Using the stricter real-device
        // test would attribute an under-attested sample to a corpus evaluator that never saw it,
        // and would put this label out of step with the source counts published beside it.
        static bool ScoredHere(MauiQualificationExecutionSample sample) =>
            !string.Equals(
                NormalizeSource(sample.Source),
                MauiQualificationSampleSources.DeviceBacked,
                StringComparison.Ordinal);
        // AddFalseHealGate compares Numerator, not IndependentNumerator, so falseHeals is the only
        // metric whose pooled share is compared against a threshold rather than only diffed. The
        // clause is additive so the base sentence stays true for all five rates; saying it of the
        // other four would overstate which numbers are enforced. It also has to state the gate's
        // short-circuit: that gate checks IndependentEvaluations first and returns not-qualified
        // without ever reading Numerator, which is exactly the state the committed baseline is in.
        // Claiming unconditional enforcement there would attach a gate to the 0/316 headline that
        // is not currently guarding it — the precise overstatement this disclosure exists to stop.
        var pooledGateClause = pooledNumeratorIsGated
            ? " The zero-false-heals gate (accumulated-zero-false-heals in a merged report) compares"
                + " this metric's whole pooled numerator, judged and pooled alike, but only once"
                + " independentEvaluations reaches the no-repair minimum; below it the gate is"
                + " not-qualified on the count alone and the pooled numerator is enforced only by"
                + " the baseline diff."
            : string.Empty;
        var deviceOnly = !judged.Any(ScoredHere);
        if (deviceOnly)
        {
            // Symmetry with the static branch below: a metric whose gate-carrying subset came from
            // the run can still have statically scored samples pooled into its published
            // denominator, and "this report did not score it" would misdescribe those. Naming
            // which gates see them matters: the count and interval gates read the independent
            // subset, and only the false-heal gate reads a pooled numerator.
            var scoredHere = all.Count(ScoredHere);
            var note = "Observed by the run that submitted the sample. This report did not score it.";
            if (scoredHere > 0)
            {
                note += $" Pooled with {scoredHere} sample(s) scored by {staticComponent}: excluded from the "
                    + "independent subset the count and lower-bound gates read, but included in the pooled "
                    + "numerator the baseline diff compares." + pooledGateClause;
            }
            return new MauiQualificationMetricProvenance
            {
                Component = "submitting-run",
                Kind = MauiQualificationMetricProvenanceKinds.SampleSupplied,
                Note = note,
            };
        }
        var mixed = judged.Any(static sample => !ScoredHere(sample));
        if (staticKindAttested is not null)
        {
            // Every judged sample, not only the statically scored ones. A device-backed sample is
            // no better placed to say which code produced its failure class than a curated one, so
            // exempting it would let a single stamped fixture speak for ninety-nine unstamped
            // device rows — exactly the shape this branch is here to refuse.
            var unattested = judged.Count(sample => !staticKindAttested(sample));
            if (unattested > 0)
            {
                // Refusing to name a component is the point: the label would otherwise assert which
                // code ran, on nothing but the sample not being device-backed.
                return new MauiQualificationMetricProvenance
                {
                    Component = "unattested",
                    Kind = MauiQualificationMetricProvenanceKinds.Unknown,
                    Note = $"{unattested} of {judged.Count} judged sample(s) carry no producer stamp, so this "
                        + $"report cannot say {staticComponent} produced them. Treated as unknown rather than "
                        + "credited to the product.",
                };
            }
        }
        // The label describes the judged subset the gates read, but it is published beside the
        // pooled counts, so say how much of the denominator it does not speak for.
        var pooledElsewhere = all.Count - judged.Count;
        var pooledNote = pooledElsewhere > 0
            ? $" Describes the {judged.Count} judged sample(s); {pooledElsewhere} further sample(s) sit in the "
                + "pooled denominator the baseline diff compares." + pooledGateClause
            : string.Empty;
        // Mixed subsets take the weaker of the two claims, the same rule the accumulator applies
        // when pooling runs. "Any statically scored sample takes the static kind" is only
        // conservative while the static kind is the weak one; for classificationAccuracy it is the
        // strongest kind in the model, and taking it would let one corpus fixture upgrade a subset
        // of run-supplied rows.
        //
        // The answer is 'unknown', not 'sample-supplied'. It is ranked lower, so it is still a
        // downgrade; it is the same answer the attestation branch above gives when the judged
        // subset cannot speak with one voice; and it is the only one the accumulator will accept.
        // ProvenanceMatchesSources refuses a declared 'sample-supplied' whose judged sources are
        // not all device-backed, and it refuses the whole report, so publishing it here would make
        // an honest run's own understatement delete its stability and device evidence too.
        if (mixed && MauiQualificationMetricProvenanceKinds.Strength(MauiQualificationMetricProvenanceKinds.SampleSupplied)
            < MauiQualificationMetricProvenanceKinds.Strength(staticKind))
        {
            return new MauiQualificationMetricProvenance
            {
                Component = $"{staticComponent} + submitting-run",
                Kind = MauiQualificationMetricProvenanceKinds.Unknown,
                Note = $"{staticNote} Downgraded from '{staticKind}': the judged subset mixes samples "
                    + "scored here with run-supplied samples, so no single component can be named, and "
                    + "pooling must not raise what a number measures." + pooledNote,
            };
        }
        return new MauiQualificationMetricProvenance
        {
            Component = staticComponent,
            Kind = staticKind,
            Note = (mixed ? $"{staticNote} Pooled with run-supplied samples." : staticNote) + pooledNote,
        };
    }

    /// <summary>
    /// The corpus scores its diagnostic and repair decisions with rules re-implemented inside
    /// <see cref="MauiPreviewQualificationCorpusRunner"/>, not with
    /// <c>MauiSelectorHealthAnalyzer.Analyze</c>. Metrics built from those decisions carry this so
    /// the report states what they measure.
    /// </summary>
    private const string HarnessRuleNote =
        "Harness rules scored against expectations authored beside them. Not evidence about "
        + "MauiSelectorHealthAnalyzer, which the corpus never calls. The component names the code "
        + "that scores corpus samples; a sample supplied from a results file under a static source "
        + "is described the same way, because nothing here can tell them apart.";

    /// <summary>
    /// Builds a bounded expected-versus-observed failure-class confusion matrix. Labels are
    /// normalized to the closed failure-class set so free text can never reach the report.
    /// </summary>
    private static MauiQualificationClassificationMatrix BuildClassificationMatrix(
        IReadOnlyList<MauiQualificationExecutionSample> samples)
    {
        if (samples.Count == 0)
        {
            return new MauiQualificationClassificationMatrix
            {
                State = "missing",
                MissingReason = "No sample carried both a ground-truth and an observed failure class.",
            };
        }

        var pairs = samples
            .Select(static sample => (
                Expected: NormalizeFailureClass(sample.ExpectedFailureClass),
                Observed: NormalizeFailureClass(sample.ObservedFailureClass)))
            .ToList();
        var cells = pairs
            .GroupBy(static pair => pair, EqualityComparer<(string Expected, string Observed)>.Default)
            .Select(static group => new MauiQualificationClassificationCell
            {
                Expected = group.Key.Expected,
                Observed = group.Key.Observed,
                Count = group.Count(),
            })
            .OrderBy(static cell => cell.Expected, StringComparer.Ordinal)
            .ThenBy(static cell => cell.Observed, StringComparer.Ordinal)
            .ToList();
        var labels = pairs
            .SelectMany(static pair => new[] { pair.Expected, pair.Observed })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static label => label, StringComparer.Ordinal)
            .ToList();
        var perClass = labels
            .Select(label =>
            {
                var support = pairs.Count(pair => pair.Expected == label);
                var predicted = pairs.Count(pair => pair.Observed == label);
                var correct = pairs.Count(pair => pair.Expected == label && pair.Observed == label);
                return new MauiQualificationClassificationClassResult
                {
                    FailureClass = label,
                    Support = support,
                    Predicted = predicted,
                    Correct = correct,
                    Precision = predicted == 0 ? null : (double)correct / predicted,
                    Recall = support == 0 ? null : (double)correct / support,
                };
            })
            .ToList();

        return new MauiQualificationClassificationMatrix
        {
            State = "measured",
            SampleCount = pairs.Count,
            Correct = pairs.Count(static pair => pair.Expected == pair.Observed),
            LabelCount = labels.Count,
            // Splits the headline: cases whose evidence already named the failure class are
            // answered by copying it, so pooling them with genuine inference overstates accuracy.
            InferredSampleCount = samples.Count(static sample => sample.FailureClassInferred == true),
            InferredCorrect = samples.Count(static sample =>
                sample.FailureClassInferred == true &&
                NormalizeFailureClass(sample.ExpectedFailureClass) == NormalizeFailureClass(sample.ObservedFailureClass)),
            StampHonouredSampleCount = samples.Count(static sample => sample.FailureClassInferred != true),
            StampHonouredCorrect = samples.Count(static sample =>
                sample.FailureClassInferred != true &&
                NormalizeFailureClass(sample.ExpectedFailureClass) == NormalizeFailureClass(sample.ObservedFailureClass)),
            Cells = cells,
            PerClass = perClass,
        };
    }

    private static MauiQualificationFirstAttemptMetric BuildFirstAttemptMetric(
        MauiPreviewQualificationInput input,
        IReadOnlyList<MauiQualificationExecutionSample> samples,
        string platform,
        MauiQualificationGateThresholds thresholds)
    {
        var result = new MauiQualificationFirstAttemptMetric();
        var ignoredDiagnostics = samples.Count(static sample => sample.DiagnosticRerun == true);
        var firstAttempts = new List<MauiQualificationExecutionSample>();

        foreach (var sample in samples)
        {
            if (sample.DiagnosticRerun == true)
                continue;
            if (sample.Excluded == true)
                continue;
            if (!IsInPlatformScope(sample, platform) || !IsRealDeviceSample(sample) ||
                sample.CleanState != true || sample.FirstAttempt != true)
            {
                continue;
            }

            if (IsInfrastructureOutcome(sample.Outcome) &&
                !string.IsNullOrWhiteSpace(sample.InfrastructureExclusionReason))
            {
                result.InfrastructureExclusions.Add(new MauiQualificationExclusion
                {
                    Kind = "infrastructure-first-attempt",
                    Count = 1,
                    Reason = "recorded-deterministic-infrastructure-reason",
                });
                continue;
            }

            firstAttempts.Add(sample);
        }

        var declaredFlows = (input.Tier1Flows ?? [])
            .Where(static flow => !string.IsNullOrWhiteSpace(flow))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static flow => flow, StringComparer.Ordinal)
            .ToList();
        foreach (var flow in declaredFlows)
        {
            var flowSamples = firstAttempts
                .Where(sample => string.Equals(sample.FlowId, flow, StringComparison.Ordinal))
                .ToList();
            var passed = flowSamples.Count(static sample =>
                string.Equals(sample.Outcome, MauiFlowRunOutcomes.Passed, StringComparison.Ordinal));
            result.Flows.Add(new MauiQualificationFlowAttemptSummary
            {
                FlowId = MauiQualificationSanitizer.Fingerprint(flow),
                CleanFirstAttempts = flowSamples.Count,
                PassedFirstAttempts = passed,
                Stability = flowSamples.Count == 0 ? null : (double)passed / flowSamples.Count,
                RealDeviceEvidence = flowSamples.Count > 0,
            });
        }

        result.DiagnosticRerunsIgnored = ignoredDiagnostics;
        result.Stability = BuildRate(
            firstAttempts,
            static sample => string.Equals(sample.Outcome, MauiFlowRunOutcomes.Passed, StringComparison.Ordinal),
            independentDeviceRuns: firstAttempts.Count > 0,
            thresholds.ConfidenceLevel);
        result.State = firstAttempts.Count == 0 ? "missing" : "measured";
        return result;
    }

    private static MauiQualificationHumanDecisionOutcomes BuildHumanDecisionOutcomes(
        IEnumerable<MauiQualificationExecutionSample> samples)
    {
        var result = new MauiQualificationHumanDecisionOutcomes();
        foreach (var decision in samples.Select(static sample => sample.HumanDecision))
        {
            switch (decision?.Trim().ToLowerInvariant())
            {
                case "approved":
                    result.Approved++;
                    break;
                case "rejected":
                    result.Rejected++;
                    break;
                case "expired":
                    result.Expired++;
                    break;
                case "abstained":
                    result.Abstained++;
                    break;
                case null:
                case "":
                    break;
                default:
                    result.Unresolved++;
                    break;
            }
        }
        return result;
    }

    private static MauiQualificationDurationMetric BuildDurationMetric(string operation, IEnumerable<double> values)
    {
        var sorted = values.Where(static value => !double.IsNaN(value) && !double.IsInfinity(value)).OrderBy(static value => value).ToArray();
        if (sorted.Length == 0)
        {
            return new MauiQualificationDurationMetric
            {
                Operation = operation,
                MissingReason = "No recorded samples were supplied.",
            };
        }

        return new MauiQualificationDurationMetric
        {
            State = "measured",
            Operation = operation,
            SampleCount = sorted.Length,
            P50Ms = MauiQualificationStatistics.Percentile(sorted, 0.50),
            P95Ms = MauiQualificationStatistics.Percentile(sorted, 0.95),
            MaxMs = sorted[^1],
        };
    }

    private static MauiQualificationTraceReportMetric BuildTraceReportMetric(
        IReadOnlyList<MauiQualificationExecutionSample> samples)
    {
        var reports = samples.Where(static sample => sample.ReportPresent == true).ToList();
        var expected = samples.Count(static sample =>
            NormalizeSource(sample.Source) == MauiQualificationSampleSources.DeviceBacked &&
            sample.DiagnosticRerun != true);
        var reportSizes = reports.Where(static sample => sample.ReportBytes is >= 0).Select(static sample => (double)sample.ReportBytes!.Value).OrderBy(static value => value).ToArray();
        var traceSizes = samples.Where(static sample => sample.TraceBytes is >= 0).Select(static sample => (double)sample.TraceBytes!.Value).OrderBy(static value => value).ToArray();
        return new MauiQualificationTraceReportMetric
        {
            State = reports.Count == 0 ? "missing" : "measured",
            ExpectedReportCount = expected,
            ReportPresent = reports.Count,
            ReportSchemaValid = reports.Count(static sample => sample.ReportSchemaValid == true),
            ReportComplete = reports.Count(static sample => sample.ReportComplete == true),
            ReportCompleteness = expected == 0 ? null : reports.Count(static sample => sample.ReportComplete == true) / (double)expected,
            TraceSampleCount = traceSizes.Length,
            ReportP50Bytes = reportSizes.Length == 0 ? null : MauiQualificationStatistics.Percentile(reportSizes, 0.50),
            ReportP95Bytes = reportSizes.Length == 0 ? null : MauiQualificationStatistics.Percentile(reportSizes, 0.95),
            TraceP50Bytes = traceSizes.Length == 0 ? null : MauiQualificationStatistics.Percentile(traceSizes, 0.50),
            TraceP95Bytes = traceSizes.Length == 0 ? null : MauiQualificationStatistics.Percentile(traceSizes, 0.95),
            MissingReason = reports.Count == 0 ? "No flow report size or completeness evidence was supplied." : null,
        };
    }

    private static MauiQualificationPrivacySecurityMetric BuildPrivacySecurityMetric(
        MauiPreviewQualificationInput input,
        IReadOnlyList<MauiQualificationExecutionSample> samples)
    {
        var supplied = input.PrivacySecurity;
        var corpus = input.Corpus?.SecurityCorpus;
        var escapes = samples.Count(static sample => sample.PrivacySecurityEscape == true) + (supplied?.EscapeCount ?? 0);
        var count = Math.Max(
            samples.Count(static sample => sample.PrivacySecurityEscape.HasValue),
            Math.Max(supplied?.TestCount ?? 0, corpus?.CaseCount ?? 0));
        var validCorpus = corpus?.Valid;
        return new MauiQualificationPrivacySecurityMetric
        {
            State = count == 0 || validCorpus == false ? "missing" : "measured",
            TestCount = count,
            EscapeCount = escapes,
            CanaryScanPassed = escapes == 0 && (supplied?.CanaryScanPassed ?? validCorpus ?? false),
            CaseIds = (supplied?.CaseIds ?? corpus?.CaseIds ?? [])
                .Select(MauiQualificationSanitizer.Fingerprint)
                .Where(static value => value is not null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            MissingReason = count == 0
                ? "No security/privacy adversarial corpus evidence was supplied."
                : validCorpus == false
                    ? "The security/privacy corpus was invalid."
                    : null,
        };
    }

    private static void AddCorpusGate(MauiPreviewQualificationReport report)
    {
        var valid = report.Corpus.ManifestValid == true && report.Corpus.CaseSchemaValid == true;
        var status = valid
            ? MauiPreviewQualificationStates.Pass
            : report.Corpus.ManifestValid == false || report.Corpus.CaseSchemaValid == false
                ? MauiPreviewQualificationStates.Fail
                : MauiPreviewQualificationStates.NotQualified;
        report.Gates.Add(new MauiQualificationGateResult
        {
            GateId = "corpus-contract",
            Status = status,
            Message = status == MauiPreviewQualificationStates.Pass
                ? "The deterministic corpus manifest and case schemas are valid."
                : "The deterministic corpus manifest and case-schema evidence is incomplete or invalid.",
            ReasonCodes = status == MauiPreviewQualificationStates.Pass
                ? []
                : ["corpus-manifest-or-schema-missing"],
        });
    }

    private static void AddFeatureFlagGate(MauiPreviewQualificationReport report)
    {
        var flags = report.FeatureFlags;
        var unsafeFlags = new List<string>();
        if (flags.AutoApplyRepair) unsafeFlags.Add("auto-apply-repair-enabled");
        if (flags.AutoApplySource) unsafeFlags.Add("auto-apply-source-enabled");
        if (flags.ModelProviderEnabled) unsafeFlags.Add("model-provider-enabled");
        if (flags.TelemetryEgressEnabled) unsafeFlags.Add("telemetry-egress-enabled");
        if (flags.RequiredPullRequestGate) unsafeFlags.Add("required-pr-gate-enabled");

        report.Gates.Add(new MauiQualificationGateResult
        {
            GateId = "preview-safety-flags",
            Status = unsafeFlags.Count == 0 ? MauiPreviewQualificationStates.Pass : MauiPreviewQualificationStates.Fail,
            Message = unsafeFlags.Count == 0
                ? "Preview flags retain proposal-only, no-provider, no-egress, advisory behavior."
                : "Unsafe preview flags contradict the engineering-preview contract.",
            ReasonCodes = unsafeFlags,
        });
    }

    private static void AddReviewGate(MauiPreviewQualificationReport report, MauiQualificationGateThresholds thresholds)
    {
        if (!thresholds.RequireRecordedReviews)
        {
            report.Gates.Add(new MauiQualificationGateResult
            {
                GateId = "independent-review",
                Status = MauiPreviewQualificationStates.Pass,
                Message = "Recorded review is disabled by the explicitly versioned policy.",
            });
            return;
        }

        var review = report.Review;
        var missing = new List<string>();
        if (!IsApproved(review.PlanReviewStatus)) missing.Add("plan-review-status-missing-or-not-approved");
        if (!IsApproved(review.RubberDuckReviewStatus)) missing.Add("rubber-duck-review-status-missing-or-not-approved");
        if (!IsApproved(review.IndependentReviewStatus)) missing.Add("independent-review-status-missing-or-not-approved");

        report.Gates.Add(new MauiQualificationGateResult
        {
            GateId = "independent-review",
            Status = missing.Count == 0 ? MauiPreviewQualificationStates.Pass : MauiPreviewQualificationStates.NotQualified,
            Message = missing.Count == 0
                ? "Plan, rubber-duck, and independent review statuses are recorded as approved."
                : "Recorded approved plan, rubber-duck, and independent review evidence is required.",
            ReasonCodes = missing,
        });
    }

    private static void AddRequiredEvidenceGate(
        MauiPreviewQualificationReport report,
        MauiPreviewQualificationInput input,
        IReadOnlyList<MauiQualificationExecutionSample> samples)
    {
        var supplied = input.Evidence;
        var inferredReportSchema = samples.Where(static sample => sample.ReportPresent == true).ToList();
        var facts = new (string Code, bool? Value)[]
        {
            ("corpus-manifest-evidence-missing", supplied?.CorpusManifestValid ?? report.Corpus.ManifestValid),
            ("case-schema-evidence-missing", supplied?.CaseSchemaValid ?? report.Corpus.CaseSchemaValid),
            ("report-schema-evidence-missing", supplied?.ReportSchemaValid ??
                (inferredReportSchema.Count == 0 ? null : inferredReportSchema.All(static sample => sample.ReportSchemaValid == true))),
            ("recording-validity-evidence-missing", supplied?.RecordingValid ??
                (samples.Any(static sample => sample.RecordingValid.HasValue)
                    ? samples.Where(static sample => sample.RecordingValid.HasValue).All(static sample => sample.RecordingValid == true)
                    : null)),
            ("first-attempt-evidence-missing", supplied?.FirstAttemptEvidencePresent ??
                (samples.Any(static sample => sample.FirstAttempt == true) ? true : null)),
            ("artifact-manifest-evidence-missing", supplied?.ArtifactManifestValid),
            ("artifact-reference-evidence-missing", supplied?.ArtifactReferencesComplete),
        };
        var failed = facts.Where(static fact => fact.Value == false).Select(static fact => fact.Code).ToList();
        var missing = facts.Where(static fact => !fact.Value.HasValue).Select(static fact => fact.Code).ToList();
        var status = failed.Count > 0
            ? MauiPreviewQualificationStates.Fail
            : missing.Count > 0
                ? MauiPreviewQualificationStates.NotQualified
                : MauiPreviewQualificationStates.Pass;
        report.Gates.Add(new MauiQualificationGateResult
        {
            GateId = "required-evidence",
            Status = status,
            Message = status == MauiPreviewQualificationStates.Pass
                ? "Required corpus, report, recording, first-attempt, and artifact evidence is present."
                : "Required report/schema/first-attempt/artifact evidence is missing or invalid.",
            ReasonCodes = [.. failed, .. missing],
        });
    }

    private static void AddRepairPrecisionGate(MauiPreviewQualificationReport report, MauiQualificationGateThresholds thresholds)
    {
        var metric = report.Metrics.RepairPrecision;
        var lower = metric.IndependentConfidenceInterval?.Lower;
        var status = metric.IndependentEvaluations < thresholds.MinimumRepairEvaluations
            ? MauiPreviewQualificationStates.NotQualified
            : lower >= thresholds.MinimumRepairPrecision
                ? MauiPreviewQualificationStates.Pass
                : MauiPreviewQualificationStates.Fail;
        report.Gates.Add(new MauiQualificationGateResult
        {
            GateId = "repair-precision",
            Status = status,
            Message = status == MauiPreviewQualificationStates.Pass
                ? "Repair precision meets the conservative Wilson lower-bound threshold."
                : "Repair precision lacks enough evaluations or misses the conservative lower-bound threshold.",
            ReasonCodes = status == MauiPreviewQualificationStates.Pass
                ? []
                : metric.IndependentEvaluations < thresholds.MinimumRepairEvaluations
                    ? ["repair-evaluation-count-insufficient"]
                    : ["repair-precision-lower-bound-below-threshold"],
        });
    }

    private static void AddFalseHealGate(MauiPreviewQualificationReport report, MauiQualificationGateThresholds thresholds)
    {
        var metric = report.Metrics.FalseHeals;
        var status = metric.IndependentEvaluations < thresholds.MinimumNoRepairEvaluations
            ? MauiPreviewQualificationStates.NotQualified
            : metric.Numerator <= thresholds.MaximumFalseHeals
                ? MauiPreviewQualificationStates.Pass
                : MauiPreviewQualificationStates.Fail;
        report.Gates.Add(new MauiQualificationGateResult
        {
            GateId = "zero-false-heals",
            Status = status,
            Message = status == MauiPreviewQualificationStates.Pass
                ? "No false heal was observed across the required no-repair denominator."
                : "No-repair evidence is insufficient or includes a false heal.",
            ReasonCodes = status == MauiPreviewQualificationStates.Pass
                ? []
                : metric.IndependentEvaluations < thresholds.MinimumNoRepairEvaluations
                    ? ["no-repair-evaluation-count-insufficient"]
                    : ["false-heal-observed"],
        });
    }

    private static void AddClassificationAccuracyGate(
        MauiPreviewQualificationReport report,
        MauiQualificationGateThresholds thresholds)
    {
        var metric = report.Metrics.ClassificationAccuracy;
        var lower = metric.IndependentConfidenceInterval?.Lower;
        var status = metric.IndependentEvaluations < thresholds.MinimumClassificationEvaluations
            ? MauiPreviewQualificationStates.NotQualified
            : lower >= thresholds.MinimumClassificationAccuracy
                ? MauiPreviewQualificationStates.Pass
                : MauiPreviewQualificationStates.Fail;
        report.Gates.Add(new MauiQualificationGateResult
        {
            GateId = "classification-accuracy",
            Status = status,
            Message = status == MauiPreviewQualificationStates.Pass
                ? "Failure-class accuracy meets the conservative Wilson lower-bound threshold."
                : "Failure-class accuracy lacks enough labeled evaluations or misses the conservative lower-bound threshold.",
            ReasonCodes = status == MauiPreviewQualificationStates.Pass
                ? []
                : metric.IndependentEvaluations < thresholds.MinimumClassificationEvaluations
                    ? ["classification-evaluation-count-insufficient"]
                    : ["classification-accuracy-lower-bound-below-threshold"],
        });
    }

    /// <summary>
    /// Reports whether the metrics that decide repair behaviour came from the shipped analyzer.
    /// The corpus runner re-implements the selector-health and repair-eligibility rules, so those
    /// numbers are a self-consistency check between the harness and the expectations written beside
    /// it. This gate exists so that fact is carried in the report and the committed baseline
    /// permanently, rather than living only in a comment: a large clean denominator produced by
    /// harness rules must never be mistaken for evidence that the product behaves correctly.
    /// <para>
    /// Nothing here can observe which code produced a sample. Every kind other than
    /// <c>harness-local-rules</c> — which is a compile-time literal in this file — ultimately rests
    /// on a label in the submitted data, so a passing result always carries
    /// <c>provenance-self-reported</c> and says whose word it is taking. There is no unqualified
    /// pass, deliberately: an empty reason list would read as verification.
    /// </para>
    /// </summary>
    private static void AddProductAnalyzerCoverageGate(MauiPreviewQualificationReport report)
    {
        var metrics = new (string Name, MauiQualificationRateMetric Metric)[]
        {
            ("repairPrecision", report.Metrics.RepairPrecision),
            ("repairRecall", report.Metrics.RepairRecall),
            ("falseHeals", report.Metrics.FalseHeals),
            ("abstention", report.Metrics.Abstention),
            ("classificationAccuracy", report.Metrics.ClassificationAccuracy),
        };
        report.Gates.Add(BuildProductAnalyzerCoverageGate(
            metrics.Select(static entry => (entry.Name, entry.Metric.Denominator, entry.Metric.Exercises))));
    }

    /// <summary>
    /// The metrics this gate exists to disclose. A verdict computed while these are absent says
    /// nothing about the gap, so their absence is reported rather than passed over — otherwise an
    /// input carrying only <c>classificationAccuracy</c> would clear the gate that exists because
    /// repair scoring does not reach the product.
    /// </summary>
    private static readonly string[] RepairScoringMetrics =
        ["repairPrecision", "repairRecall", "falseHeals", "abstention"];

    /// <summary>
    /// Shared by the per-run report and the accumulated verdict so a merged result cannot report
    /// coverage the runs it merged never had.
    /// </summary>
    internal static MauiQualificationGateResult BuildProductAnalyzerCoverageGate(
        IEnumerable<(string Name, int Denominator, MauiQualificationMetricProvenance? Exercises)> metrics)
    {
        var scored = metrics.Where(static entry => entry.Denominator > 0).ToList();
        if (scored.Count == 0)
        {
            // Absent evidence is not coverage. Every other gate in this file answers
            // "nothing was measured" with not-qualified, and so does this one.
            return new MauiQualificationGateResult
            {
                GateId = "product-analyzer-coverage",
                Status = MauiPreviewQualificationStates.NotQualified,
                Message = "No gated metric carried any evidence, so nothing is known about what produced it.",
                ReasonCodes = ["product-analyzer-coverage-evidence-missing"],
            };
        }

        static string Describe(IEnumerable<string> names) => string.Join(", ", names.OrderBy(static name => name, StringComparer.Ordinal));
        static bool Is(MauiQualificationMetricProvenance? exercises, string kind) =>
            string.Equals(exercises?.Kind, kind, StringComparison.Ordinal);
        var scoredNames = scored.Select(static entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        var missingScope = RepairScoringMetrics.Where(name => !scoredNames.Contains(name)).ToList();
        var harnessScored = scored
            .Where(static entry => Is(entry.Exercises, MauiQualificationMetricProvenanceKinds.HarnessLocalRules))
            .Select(static entry => entry.Name)
            .ToList();
        // Distinguished from "unknown": one is a metric that said nothing, the other is a merged
        // metric that said it could not tell. Collapsing them would lose the reason.
        var undeclared = scored
            .Where(static entry => entry.Exercises is null || string.IsNullOrWhiteSpace(entry.Exercises.Kind))
            .Select(static entry => entry.Name)
            .ToList();
        var undeclaredComponent = scored
            .Where(static entry => entry.Exercises is not null &&
                !string.IsNullOrWhiteSpace(entry.Exercises.Kind) &&
                (string.IsNullOrWhiteSpace(entry.Exercises.Component) ||
                    // The contract's default. A provenance that never set a component reads as
                    // "unknown", which is an absence, not a declaration.
                    string.Equals(
                        entry.Exercises.Component,
                        MauiQualificationMetricProvenance.UndeclaredComponent,
                        StringComparison.Ordinal)))
            .Select(static entry => entry.Name)
            .ToList();
        var unknown = scored
            .Where(static entry => entry.Exercises is not null &&
                !string.IsNullOrWhiteSpace(entry.Exercises.Kind) &&
                !Is(entry.Exercises, MauiQualificationMetricProvenanceKinds.HarnessLocalRules) &&
                !Is(entry.Exercises, MauiQualificationMetricProvenanceKinds.ShippedAnalyzer) &&
                !Is(entry.Exercises, MauiQualificationMetricProvenanceKinds.SampleSupplied))
            .Select(static entry => entry.Name)
            .ToList();
        // shipped-analyzer and sample-supplied differ in what they claim, not in how well it is
        // established: both are labels this gate read rather than facts it observed. They are
        // reported separately so the claim is legible, and both carry provenance-self-reported.
        var claimsAnalyzer = scored
            .Where(static entry => Is(entry.Exercises, MauiQualificationMetricProvenanceKinds.ShippedAnalyzer))
            .Select(static entry => entry.Name)
            .ToList();
        var selfReported = scored
            .Where(static entry => Is(entry.Exercises, MauiQualificationMetricProvenanceKinds.SampleSupplied))
            .Select(static entry => entry.Name)
            .ToList();

        if (harnessScored.Count > 0 ||
            undeclared.Count > 0 ||
            undeclaredComponent.Count > 0 ||
            unknown.Count > 0 ||
            missingScope.Count > 0)
        {
            var reasons = new List<string>();
            var parts = new List<string>();
            if (harnessScored.Count > 0)
            {
                reasons.Add("corpus-does-not-exercise-shipped-analyzer");
                // Leads with "Not produced by" so a log or baseline grep for "shipped analyzer"
                // cannot mistake this message for the passing one.
                parts.Add($"Not produced by the shipped analyzer — scored by harness-local rules: {Describe(harnessScored)}. "
                    + "These measure agreement between the harness and its own expectations, not product behaviour.");
            }
            if (undeclared.Count > 0)
            {
                reasons.Add("provenance-undeclared");
                parts.Add($"Declared nothing about what produced them: {Describe(undeclared)}. "
                    + "An undeclared metric is not coverage.");
            }
            if (undeclaredComponent.Count > 0)
            {
                reasons.Add("provenance-component-missing");
                parts.Add($"Named a kind but no component: {Describe(undeclaredComponent)}.");
            }
            if (unknown.Count > 0)
            {
                reasons.Add("provenance-unknown");
                parts.Add($"Could not be credited to any one component: {Describe(unknown)}. A judged "
                    + "subset that mixes sources, one holding samples with no producer stamp, a kind "
                    + "this gate does not recognise, or contributors that disagreed all land here.");
            }
            if (missingScope.Count > 0)
            {
                reasons.Add("product-analyzer-coverage-scope-missing");
                parts.Add($"Carried no evidence for the metrics this gate exists to disclose: "
                    + $"{Describe(missingScope)}. Their absence is not coverage of them.");
            }
            return new MauiQualificationGateResult
            {
                GateId = "product-analyzer-coverage",
                Status = MauiPreviewQualificationStates.NotQualified,
                Message = string.Join(" ", parts),
                ReasonCodes = reasons,
            };
        }

        // Nothing was harness-scored — but this gate cannot watch code run, so the best it can say
        // is whose word it is taking. The reason code is unconditional on purpose: an empty reason
        // list on a pass would be read as verification, and nothing here verified anything.
        var claimed = new List<string>();
        if (claimsAnalyzer.Count > 0)
            claimed.Add($"claiming the shipped analyzer: {Describe(claimsAnalyzer)}");
        if (selfReported.Count > 0)
            claimed.Add($"claiming the submitting run observed them: {Describe(selfReported)}");
        return new MauiQualificationGateResult
        {
            GateId = "product-analyzer-coverage",
            Status = MauiPreviewQualificationStates.Pass,
            Message = "No gated metric was scored by harness-local rules. This rests on labels in the "
                + "submitted data, which this gate read rather than observed — "
                + string.Join("; ", claimed) + ".",
            ReasonCodes = ["provenance-self-reported"],
        };
    }

    private static void AddSelectorStabilityGate(MauiPreviewQualificationReport report, MauiQualificationGateThresholds thresholds)
    {
        var metric = report.Metrics.SelectorStability;
        var status = metric.IndependentEvaluations < thresholds.MinimumSelectorObservations
            ? MauiPreviewQualificationStates.NotQualified
            : metric.Value >= thresholds.MinimumSelectorStability
                ? MauiPreviewQualificationStates.Pass
                : MauiPreviewQualificationStates.Fail;
        report.Gates.Add(new MauiQualificationGateResult
        {
            GateId = "selector-stability",
            Status = status,
            Message = status == MauiPreviewQualificationStates.Pass
                ? "Selector stability meets the declared platform-scope threshold."
                : "Declared-platform real-device selector-stability evidence is insufficient or below threshold.",
            ReasonCodes = status == MauiPreviewQualificationStates.Pass
                ? []
                : metric.IndependentEvaluations < thresholds.MinimumSelectorObservations
                    ? ["selector-stability-device-evidence-insufficient"]
                    : ["selector-stability-below-threshold"],
        });
    }

    private static void AddCalibrationGate(MauiPreviewQualificationReport report, MauiQualificationGateThresholds thresholds)
    {
        var metric = report.Metrics.Calibration;
        var status = !metric.ProbabilityLikeConfidenceDisplayed
            ? MauiPreviewQualificationStates.Pass
            : metric.Ece is null
                ? MauiPreviewQualificationStates.NotQualified
                : metric.Ece <= thresholds.MaximumCalibrationEce
                    ? MauiPreviewQualificationStates.Pass
                    : MauiPreviewQualificationStates.Fail;
        report.Gates.Add(new MauiQualificationGateResult
        {
            GateId = "confidence-calibration",
            Status = status,
            Message = status == MauiPreviewQualificationStates.Pass
                ? metric.ProbabilityLikeConfidenceDisplayed
                    ? "Probability-like confidence meets the ECE threshold."
                    : "No probability-like confidence is displayed; calibration is not applicable."
                : "Probability-like confidence lacks calibration evidence or exceeds the ECE threshold.",
            ReasonCodes = status == MauiPreviewQualificationStates.Pass
                ? []
                : metric.Ece is null
                    ? ["calibration-evidence-missing"]
                    : ["calibration-ece-above-threshold"],
        });
    }

    private static void AddPrivacySecurityGate(MauiPreviewQualificationReport report)
    {
        var metric = report.Metrics.PrivacySecurityEscapes;
        var status = metric.EscapeCount > 0 || metric.CanaryScanPassed == false
            ? MauiPreviewQualificationStates.Fail
            : metric.TestCount == 0 || metric.CanaryScanPassed is null
                ? MauiPreviewQualificationStates.NotQualified
                : MauiPreviewQualificationStates.Pass;
        report.Gates.Add(new MauiQualificationGateResult
        {
            GateId = "privacy-security-escapes",
            Status = status,
            Message = status == MauiPreviewQualificationStates.Pass
                ? "No privacy or security escape was observed by the adversarial corpus."
                : "Privacy/security evidence is missing or an escape was observed.",
            ReasonCodes = status == MauiPreviewQualificationStates.Pass
                ? []
                : status == MauiPreviewQualificationStates.Fail
                    ? ["privacy-or-security-escape-observed"]
                    : ["privacy-security-corpus-evidence-missing"],
        });
    }

    private static void AddHostPerformanceGate(MauiPreviewQualificationReport report, MauiQualificationGateThresholds thresholds)
    {
        var operations = report.Metrics.RuntimeOverhead.HostOperations ?? [];
        var measured = operations.Where(static operation => operation.State == "measured" && operation.P95Ms.HasValue).ToList();
        var regression = measured.Where(operation => operation.P95Ms > thresholds.HostOperationP95BudgetMs).ToList();
        var status = measured.Count == 0
            ? MauiPreviewQualificationStates.NotQualified
            : regression.Count > 0
                ? MauiPreviewQualificationStates.Fail
                : MauiPreviewQualificationStates.Pass;
        report.Gates.Add(new MauiQualificationGateResult
        {
            GateId = "deterministic-host-performance",
            Status = status,
            Message = status == MauiPreviewQualificationStates.Pass
                ? "Measured deterministic host operations remain within the p95 budget."
                : "Deterministic host performance evidence is missing or exceeds its p95 budget.",
            ReasonCodes = status == MauiPreviewQualificationStates.Pass
                ? []
                : regression.Count > 0
                    ? ["host-operation-p95-budget-exceeded"]
                    : ["host-performance-evidence-missing"],
        });
    }

    private static void AddDeviceOverheadGate(MauiPreviewQualificationReport report)
    {
        var metric = report.Metrics.RuntimeOverhead.DeviceOverhead;
        var status = metric?.State == "measured"
            ? MauiPreviewQualificationStates.Pass
            : MauiPreviewQualificationStates.NotQualified;
        report.Gates.Add(new MauiQualificationGateResult
        {
            GateId = "android-device-overhead",
            Status = status,
            Message = status == MauiPreviewQualificationStates.Pass
                ? "Android device-overhead evidence is present."
                : "Android device-overhead evidence remains missing until real pilot artifacts are supplied.",
            ReasonCodes = status == MauiPreviewQualificationStates.Pass ? [] : ["android-device-overhead-evidence-missing"],
        });
    }

    private static void AddAndroidFirstAttemptGate(
        MauiPreviewQualificationReport report,
        string platform,
        MauiQualificationGateThresholds thresholds)
    {
        if (!string.Equals(platform, Android, StringComparison.Ordinal))
        {
            report.Gates.Add(new MauiQualificationGateResult
            {
                GateId = "android-tier1-first-attempts",
                Status = MauiPreviewQualificationStates.NotQualified,
                Message = "This engineering-preview policy requires an explicit Android platform scope.",
                ReasonCodes = ["android-platform-scope-missing"],
            });
            return;
        }

        var metric = report.Metrics.FlakeFirstAttemptStability;
        var reasons = new List<string>();
        if (metric.Flows.Count == 0)
            reasons.Add("tier1-flow-declaration-missing");
        // Each flow is judged on its own evidence and the worst verdict wins. Pooling the reasons
        // would let one flow that has not run yet downgrade another flow's measured stability
        // failure to not-qualified, which reports exit 0 on a real regression.
        var status = metric.Flows.Count == 0
            ? MauiPreviewQualificationStates.NotQualified
            : MauiPreviewQualificationStates.Pass;
        foreach (var flow in metric.Flows)
        {
            var flowStatus = MauiPreviewQualificationStates.Pass;
            if (!flow.RealDeviceEvidence)
                reasons.Add("android-real-device-evidence-missing");
            if (flow.CleanFirstAttempts < thresholds.MinimumCleanFirstAttemptsPerTier1Flow)
            {
                reasons.Add("android-clean-first-attempt-count-insufficient");
                flowStatus = MauiPreviewQualificationStates.NotQualified;
            }
            else
            {
                if (!flow.RealDeviceEvidence)
                    flowStatus = MauiPreviewQualificationStates.Fail;
                if (flow.Stability < thresholds.MinimumFirstAttemptStability)
                {
                    reasons.Add("android-first-attempt-stability-below-threshold");
                    flowStatus = MauiPreviewQualificationStates.Fail;
                }
            }

            status = WorseGateStatus(status, flowStatus);
        }

        report.Gates.Add(new MauiQualificationGateResult
        {
            GateId = "android-tier1-first-attempts",
            Status = status,
            Message = status == MauiPreviewQualificationStates.Pass
                ? "Every declared Tier-1 flow has enough clean real-device first attempts at the stability threshold."
                : "Android qualification requires at least 100 clean real-device first attempts per declared Tier-1 flow.",
            ReasonCodes = reasons.Distinct(StringComparer.Ordinal).ToList(),
        });
    }

    /// <summary>Fail is worse than not-qualified, which is worse than pass.</summary>
    private static string WorseGateStatus(string left, string right)
    {
        static int Rank(string status) => status switch
        {
            MauiPreviewQualificationStates.Fail => 2,
            MauiPreviewQualificationStates.NotQualified => 1,
            _ => 0,
        };
        return Rank(right) > Rank(left) ? right : left;
    }

    private static MauiQualificationGateThresholds NormalizeThresholds(MauiQualificationGateThresholds source)    {
        return new MauiQualificationGateThresholds
        {
            PolicyVersion = MauiQualificationSanitizer.FingerprintOrUnknown(source.PolicyVersion),
            ConfidenceLevel = source.ConfidenceLevel is <= 0 or >= 1 ? 0.95 : source.ConfidenceLevel,
            MinimumRepairPrecision = Math.Clamp(source.MinimumRepairPrecision, 0, 1),
            MinimumRepairEvaluations = Math.Max(1, source.MinimumRepairEvaluations),
            MinimumNoRepairEvaluations = Math.Max(1, source.MinimumNoRepairEvaluations),
            MaximumFalseHeals = Math.Max(0, source.MaximumFalseHeals),
            MinimumSelectorStability = Math.Clamp(source.MinimumSelectorStability, 0, 1),
            MinimumSelectorObservations = Math.Max(1, source.MinimumSelectorObservations),
            MinimumClassificationAccuracy = Math.Clamp(source.MinimumClassificationAccuracy, 0, 1),
            MinimumClassificationEvaluations = Math.Max(1, source.MinimumClassificationEvaluations),
            MaximumCalibrationEce = Math.Clamp(source.MaximumCalibrationEce, 0, 1),
            MinimumCleanFirstAttemptsPerTier1Flow = Math.Max(1, source.MinimumCleanFirstAttemptsPerTier1Flow),
            MinimumFirstAttemptStability = Math.Clamp(source.MinimumFirstAttemptStability, 0, 1),
            HostOperationP95BudgetMs = Math.Max(1, source.HostOperationP95BudgetMs),
            RequireRealAndroidDeviceEvidence = source.RequireRealAndroidDeviceEvidence,
            RequireRecordedReviews = source.RequireRecordedReviews,
        };
    }

    private static bool IsRealDeviceSample(MauiQualificationExecutionSample sample) =>
        string.Equals(NormalizeSource(sample.Source), MauiQualificationSampleSources.DeviceBacked, StringComparison.Ordinal) &&
        sample.RealDevice == true &&
        sample.DeviceEvidenceKind is "physical-device" or "real-device";

    private static bool IsInPlatformScope(MauiQualificationExecutionSample sample, string platform) =>
        string.Equals(NormalizePlatform(sample.Platform), platform, StringComparison.Ordinal);

    private static bool IsInfrastructureOutcome(string? outcome) =>
        string.Equals(outcome, MauiFlowRunOutcomes.InfrastructureError, StringComparison.Ordinal);

    private static bool IsApproved(string? status) =>
        status is not null &&
        (string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(status, "passed", StringComparison.OrdinalIgnoreCase));

    private static string NormalizePlatform(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "android" => "android",
            "ios" => "ios",
            "maccatalyst" => "maccatalyst",
            "macos" => "macos",
            "windows" => "windows",
            "wpf" => "wpf",
            "gtk" => "gtk",
            _ => "unknown",
        };

    private static string NormalizeSource(string? value) =>
        MauiQualificationSampleSources.IsKnown(value) ? value! : "unknown";

    private static string NormalizeFailureClass(string? value) =>
        MauiFlowFailureClassifier.IsKnownFailureClass(value) ? value! : "unknown";

    private static string AggregateStatus(IEnumerable<MauiQualificationGateResult> gates)
    {
        if (gates.Any(static gate => gate.Status == MauiPreviewQualificationStates.Fail))
            return MauiPreviewQualificationStates.Fail;
        if (gates.Any(static gate => gate.Status != MauiPreviewQualificationStates.Pass))
            return MauiPreviewQualificationStates.NotQualified;
        return MauiPreviewQualificationStates.Pass;
    }

    private static MauiQualificationFingerprints Sanitize(MauiQualificationFingerprints? source) => new()
    {
        CorpusVersion = MauiQualificationSanitizer.FingerprintOrUnknown(source?.CorpusVersion),
        CorpusFingerprint = MauiQualificationSanitizer.FingerprintOrUnknown(source?.CorpusFingerprint),
        RepositoryCommit = MauiQualificationSanitizer.FingerprintOrUnknown(source?.RepositoryCommit),
        TestingPackageVersion = MauiQualificationSanitizer.FingerprintOrUnknown(source?.TestingPackageVersion),
        PackageId = MauiQualificationSanitizer.FingerprintOrUnknown(source?.PackageId),
        PackageFingerprint = MauiQualificationSanitizer.FingerprintOrUnknown(source?.PackageFingerprint),
        ToolVersion = MauiQualificationSanitizer.FingerprintOrUnknown(source?.ToolVersion),
        ToolFingerprint = MauiQualificationSanitizer.FingerprintOrUnknown(source?.ToolFingerprint),
        PolicyVersion = MauiQualificationSanitizer.FingerprintOrUnknown(source?.PolicyVersion),
        PolicyFingerprint = MauiQualificationSanitizer.FingerprintOrUnknown(source?.PolicyFingerprint),
    };

    private static MauiQualificationPlatformProfile Sanitize(MauiQualificationPlatformProfile source) => new()
    {
        Platform = NormalizePlatform(source.Platform),
        Scope = MauiQualificationSanitizer.FingerprintOrUnknown(source.Scope),
        DeviceEvidenceKind = source.DeviceEvidenceKind is
            "physical-device" or "real-device" or "emulator" or "simulator" or "desktop-host" or "none"
                ? source.DeviceEvidenceKind
                : "unknown",
        RealDevice = source.RealDevice,
        DeviceFingerprint = MauiQualificationSanitizer.FingerprintOrUnknown(source.DeviceFingerprint),
        RuntimeFingerprint = MauiQualificationSanitizer.FingerprintOrUnknown(source.RuntimeFingerprint),
        BuildFingerprint = MauiQualificationSanitizer.FingerprintOrUnknown(source.BuildFingerprint),
        PackageFingerprint = MauiQualificationSanitizer.FingerprintOrUnknown(source.PackageFingerprint),
        SeedFingerprint = MauiQualificationSanitizer.FingerprintOrUnknown(source.SeedFingerprint),
        BackendStateFingerprint = MauiQualificationSanitizer.FingerprintOrUnknown(source.BackendStateFingerprint),
        FirstAttemptMode = MauiQualificationSanitizer.FingerprintOrUnknown(source.FirstAttemptMode),
    };

    private static MauiQualificationAppleQaEvidence? SanitizeAppleQa(MauiQualificationAppleQaEvidence? source)
    {
        if (source is null)
            return null;

        return new MauiQualificationAppleQaEvidence
        {
            ContractVersion = string.Equals(
                source.ContractVersion,
                MauiAppleFlowQaManifestReader.AdapterContractVersion,
                StringComparison.Ordinal)
                    ? MauiAppleFlowQaManifestReader.AdapterContractVersion
                    : "unknown",
            Platform = NormalizePlatform(source.Platform),
            Experimental = source.Experimental,
            Backend = source.Backend is "appkit" ? "appkit" : source.Backend is null ? null : "unknown",
            OfficialCoverage = source.OfficialCoverage,
            MacCatalystEquivalent = source.MacCatalystEquivalent,
            SpikeStatus = source.SpikeStatus is
                "proved" or "not-proved" or "pending-spike" or "proof-incomplete" ? source.SpikeStatus : "unknown",
            ForegroundProof = source.ForegroundProof,
            AuthenticatedTransport = source.AuthenticatedTransport,
            Receipt = source.Receipt,
            Cancellation = source.Cancellation,
            Parity = source.Parity,
            AppProject = MauiQualificationSanitizer.FingerprintOrUnknown(source.AppProject),
            AppSourceDigest = MauiQualificationSanitizer.FingerprintOrUnknown(source.AppSourceDigest),
            PackageDigest = MauiQualificationSanitizer.FingerprintOrUnknown(source.PackageDigest),
            FlowDigests = (source.FlowDigests ?? [])
                .Take(128)
                .Select(MauiQualificationSanitizer.Fingerprint)
                .Where(static value => value is not null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            FirstAttemptCount = Math.Clamp(source.FirstAttemptCount, 0, 4_096),
            CleanAttemptCount = Math.Clamp(source.CleanAttemptCount, 0, 4_096),
            ArtifactCount = Math.Clamp(source.ArtifactCount, 0, 4_096),
            OmissionCount = Math.Clamp(source.OmissionCount, 0, 4_096),
            XcodeVersion = MauiQualificationSanitizer.FingerprintOrUnknown(source.XcodeVersion),
            SimulatorRuntime = MauiQualificationSanitizer.FingerprintOrUnknown(source.SimulatorRuntime),
            DeviceIdFingerprint = MauiQualificationSanitizer.FingerprintOrUnknown(source.DeviceIdFingerprint),
            DeviceProfile = MauiQualificationSanitizer.FingerprintOrUnknown(source.DeviceProfile),
            ResetFingerprint = MauiQualificationSanitizer.FingerprintOrUnknown(source.ResetFingerprint),
            SeedFingerprint = MauiQualificationSanitizer.FingerprintOrUnknown(source.SeedFingerprint),
            BackendStateFingerprint = MauiQualificationSanitizer.FingerprintOrUnknown(source.BackendStateFingerprint),
        };
    }

    private static MauiQualificationReviewEvidence Sanitize(MauiQualificationReviewEvidence? source) => new()
    {
        PlanId = MauiQualificationSanitizer.FingerprintOrUnknown(source?.PlanId),
        PlanRevision = source?.PlanRevision,
        PlanReviewStatus = NormalizeReviewStatus(source?.PlanReviewStatus) ?? "missing",
        RubberDuckReviewStatus = NormalizeReviewStatus(source?.RubberDuckReviewStatus) ?? "missing",
        IndependentReviewStatus = NormalizeReviewStatus(source?.IndependentReviewStatus) ?? "missing",
        ReviewedAt = source?.ReviewedAt,
        ReviewerFingerprints = (source?.ReviewerFingerprints ?? [])
            .Select(MauiQualificationSanitizer.Fingerprint)
            .Where(static value => value is not null)
            .Cast<string>()
            .ToList(),
        ArtifactRefs = (source?.ArtifactRefs ?? [])
            .Select(MauiQualificationSanitizer.Fingerprint)
            .Where(static value => value is not null)
            .Cast<string>()
            .ToList(),
    };

    private static MauiPreviewFeatureFlags Sanitize(MauiPreviewFeatureFlags? source) => new()
    {
        Schema = 1,
        PolicyVersion = "preview-flags-v1",
        WorkbenchEnabled = source?.WorkbenchEnabled == true,
        AgentAuthoringEnabled = source?.AgentAuthoringEnabled == true,
        RepairProposalsEnabled = source?.RepairProposalsEnabled == true,
        SourceProposalsEnabled = source?.SourceProposalsEnabled == true,
        TraceImportExportEnabled = source?.TraceImportExportEnabled == true,
        AutoApplyRepair = source?.AutoApplyRepair == true,
        AutoApplySource = source?.AutoApplySource == true,
        ModelProviderEnabled = source?.ModelProviderEnabled == true,
        TelemetryEgressEnabled = source?.TelemetryEgressEnabled == true,
        RequiredPullRequestGate = source?.RequiredPullRequestGate == true,
        KillSwitches = (source?.KillSwitches ?? [])
            .Where(static value => value is "workbench" or "agent-authoring" or "repair-proposals" or "source-proposals" or "trace-import-export")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToList(),
    };

    private static MauiQualificationCorpusSummary Sanitize(MauiQualificationCorpusSummary? source) => new()
    {
        Version = MauiQualificationSanitizer.FingerprintOrUnknown(source?.Version),
        ManifestFingerprint = MauiQualificationSanitizer.FingerprintOrUnknown(source?.ManifestFingerprint),
        StaticOnly = source?.StaticOnly,
        ManifestValid = source?.ManifestValid,
        CaseSchemaValid = source?.CaseSchemaValid,
        CuratedCases = Math.Max(0, source?.CuratedCases ?? 0),
        GeneratedCases = Math.Max(0, source?.GeneratedCases ?? 0),
        DeviceBackedCases = Math.Max(0, source?.DeviceBackedCases ?? 0),
        CuratedRepairPositiveCases = Math.Max(0, source?.CuratedRepairPositiveCases ?? 0),
        CuratedDerivedCases = Math.Max(0, source?.CuratedDerivedCases ?? 0),
        UndeclaredProjectionCollisions = Math.Max(0, source?.UndeclaredProjectionCollisions ?? 0),
        UndeclaredShapeCollisions = Math.Max(0, source?.UndeclaredShapeCollisions ?? 0),
        CuratedNoRepairCases = Math.Max(0, source?.CuratedNoRepairCases ?? 0),
        GeneratedNoRepairCases = Math.Max(0, source?.GeneratedNoRepairCases ?? 0),
        CuratedClassificationLabeledCases = Math.Max(0, source?.CuratedClassificationLabeledCases ?? 0),
        ProvenanceComplete = source?.ProvenanceComplete,
        ProvenanceSourceCounts = (source?.ProvenanceSourceCounts ?? [])
            .Where(static item => item is not null)
            .GroupBy(
                static item => MauiQualificationCorpusProvenanceSourceKinds.Normalize(item.SourceKind),
                StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => new MauiQualificationCorpusProvenanceCount
            {
                SourceKind = group.Key,
                Count = group.Sum(static item => Math.Max(0, item.Count)),
            })
            .ToList(),
        MutationSeed = source?.MutationSeed,
        GeneratorVersion = MauiQualificationSanitizer.FingerprintOrUnknown(source?.GeneratorVersion),
        GeneratedBaseFixtures = source?.GeneratedBaseFixtures,
        GeneratedSeedCount = source?.GeneratedSeedCount,
        ExercisesShippedAnalyzer = source?.ExercisesShippedAnalyzer,
        Errors = (source?.Errors ?? []).Select(static _ => "corpus-validation-error").Distinct(StringComparer.Ordinal).ToList(),
        SecurityCorpus = source?.SecurityCorpus is null ? null : new MauiQualificationSecurityCorpusSummary
        {
            Version = MauiQualificationSanitizer.FingerprintOrUnknown(source.SecurityCorpus.Version),
            ManifestFingerprint = MauiQualificationSanitizer.FingerprintOrUnknown(source.SecurityCorpus.ManifestFingerprint),
            Valid = source.SecurityCorpus.Valid,
            CaseCount = Math.Max(0, source.SecurityCorpus.CaseCount),
            PassedCount = Math.Max(0, source.SecurityCorpus.PassedCount),
            CaseIds = source.SecurityCorpus.CaseIds.Select(MauiQualificationSanitizer.Fingerprint).Where(static value => value is not null).Cast<string>().ToList(),
            Errors = source.SecurityCorpus.Errors.Select(static _ => "security-corpus-validation-error").Distinct(StringComparer.Ordinal).ToList(),
        },
    };

    private static MauiQualificationRuntimeOverheadMetric SanitizeRuntimeOverhead(
        MauiQualificationRuntimeOverheadMetric? source)
    {
        var host = (source?.HostOperations ?? [])
            .Take(32)
            .Select(SanitizeDuration)
            .ToList();
        return new MauiQualificationRuntimeOverheadMetric
        {
            HostOperations = host,
            DeviceOverhead = SanitizeDuration(source?.DeviceOverhead),
        };
    }

    private static MauiQualificationDurationMetric SanitizeDuration(MauiQualificationDurationMetric? source)
    {
        var state = source?.State == "measured" ? "measured" : "missing";
        return new MauiQualificationDurationMetric
        {
            State = state,
            Operation = MauiQualificationSanitizer.SafeOperation(source?.Operation),
            SampleCount = Math.Clamp(source?.SampleCount ?? 0, 0, 1_000_000),
            P50Ms = SafeNonNegative(source?.P50Ms),
            P95Ms = SafeNonNegative(source?.P95Ms),
            MaxMs = SafeNonNegative(source?.MaxMs),
            MissingReason = state == "missing" ? "measurement-evidence-missing" : null,
        };
    }

    private static double? SafeNonNegative(double? value) =>
        value.HasValue && double.IsFinite(value.Value) && value.Value >= 0 ? value : null;

    private static MauiQualificationArtifactReference Sanitize(MauiQualificationArtifactReference source) => new()
    {
        Kind = MauiQualificationSanitizer.SafeKind(source.Kind),
        Digest = MauiQualificationSanitizer.FingerprintOrUnknown(source.Digest),
        Reference = MauiQualificationSanitizer.FingerprintOrUnknown(source.Reference),
        Redacted = source.Redacted ?? true,
    };

    private static List<MauiQualificationExclusion> BuildExclusions(MauiPreviewQualificationInput input)
    {
        var result = BuildDeclaredExclusions(input.Exclusions);
        var excludedSamples = input.Samples?.Count(static sample => sample?.Excluded == true) ?? 0;
        if (excludedSamples > 0)
        {
            result.Add(new MauiQualificationExclusion
            {
                Kind = "excluded-sample",
                Count = excludedSamples,
                Reason = "explicit-sample-exclusion",
            });
        }
        return result;
    }

    private static List<MauiQualificationExclusion> BuildDeclaredExclusions(
        IEnumerable<MauiQualificationExclusion>? exclusions) =>
        (exclusions ?? [])
            .Where(static exclusion => exclusion is not null)
            .Select(static exclusion => new MauiQualificationExclusion
            {
                Kind = MauiQualificationSanitizer.SafeKind(exclusion.Kind),
                Count = Math.Max(0, exclusion.Count),
                Reason = "declared-exclusion",
            })
            .ToList();

    private static string? NormalizeReviewStatus(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "approved" => "approved",
            "passed" => "passed",
            "rejected" => "rejected",
            "pending" => "pending",
            _ => null,
        };

    private static void AddReason(MauiPreviewQualificationReport report, string code, string severity, string message)
    {
        if (report.Reasons.Any(reason => string.Equals(reason.Code, code, StringComparison.Ordinal)))
            return;
        report.Reasons.Add(new MauiQualificationReason { Code = code, Severity = severity, Message = message });
    }
}

/// <summary>Statistical helpers with fixed, documented calculations for release evidence.</summary>
public static class MauiQualificationStatistics
{
    private const double Z95 = 1.959963984540054;

    /// <summary>
    /// Returns a two-sided Wilson score interval. The qualification gate compares its lower bound
    /// with precision thresholds; callers must not substitute a point estimate.
    /// </summary>
    public static MauiQualificationConfidenceInterval WilsonInterval(int successes, int trials, double confidenceLevel = 0.95)
    {
        if (trials <= 0)
            throw new ArgumentOutOfRangeException(nameof(trials));
        if (successes < 0 || successes > trials)
            throw new ArgumentOutOfRangeException(nameof(successes));

        // The release contract intentionally fixes 95% Wilson intervals. Retaining the requested
        // value in output makes a malformed caller visible while avoiding an unreviewed inverse-CDF implementation.
        var z = Z95;
        var p = (double)successes / trials;
        var z2 = z * z;
        var denominator = 1 + z2 / trials;
        var center = (p + z2 / (2 * trials)) / denominator;
        var margin = z * Math.Sqrt((p * (1 - p) / trials) + (z2 / (4d * trials * trials))) / denominator;
        return new MauiQualificationConfidenceInterval
        {
            ConfidenceLevel = confidenceLevel,
            Lower = Math.Clamp(center - margin, 0, 1),
            Upper = Math.Clamp(center + margin, 0, 1),
        };
    }

    /// <summary>Calculates deterministic linear-interpolated percentiles over an already bounded sample set.</summary>
    public static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        ArgumentNullException.ThrowIfNull(sortedValues);
        if (sortedValues.Count == 0)
            throw new ArgumentException("At least one value is required.", nameof(sortedValues));
        var bounded = Math.Clamp(percentile, 0, 1);
        var index = (sortedValues.Count - 1) * bounded;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper)
            return sortedValues[lower];
        return sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * (index - lower));
    }

    /// <summary>Calculates ECE and Brier score over ten equal-width buckets by default.</summary>
    public static MauiQualificationCalibrationMetric CalculateCalibration(
        IEnumerable<(double Confidence, bool Outcome)> samples,
        int bucketCount = 10)
    {
        ArgumentNullException.ThrowIfNull(samples);
        bucketCount = Math.Clamp(bucketCount, 1, 100);
        var values = samples
            .Where(static sample => !double.IsNaN(sample.Confidence) && !double.IsInfinity(sample.Confidence))
            .Select(static sample => (Confidence: Math.Clamp(sample.Confidence, 0, 1), sample.Outcome))
            .ToArray();
        if (values.Length == 0)
            return new MauiQualificationCalibrationMetric();

        var buckets = Enumerable.Range(0, bucketCount)
            .Select(index => new List<(double Confidence, bool Outcome)>())
            .ToArray();
        foreach (var sample in values)
        {
            var index = Math.Min(bucketCount - 1, (int)(sample.Confidence * bucketCount));
            buckets[index].Add(sample);
        }

        var result = new MauiQualificationCalibrationMetric
        {
            State = "measured",
            ProbabilityLikeConfidenceDisplayed = true,
            SampleCount = values.Length,
            Brier = values.Average(static sample =>
            {
                var outcome = sample.Outcome ? 1d : 0d;
                return Math.Pow(sample.Confidence - outcome, 2);
            }),
        };
        var ece = 0d;
        for (var index = 0; index < bucketCount; index++)
        {
            var bucket = buckets[index];
            var lower = (double)index / bucketCount;
            var upper = (double)(index + 1) / bucketCount;
            var meanConfidence = bucket.Count == 0 ? (double?)null : bucket.Average(static sample => sample.Confidence);
            var empiricalRate = bucket.Count == 0 ? (double?)null : bucket.Count(static sample => sample.Outcome) / (double)bucket.Count;
            if (meanConfidence.HasValue && empiricalRate.HasValue)
                ece += bucket.Count / (double)values.Length * Math.Abs(meanConfidence.Value - empiricalRate.Value);
            result.Buckets.Add(new MauiQualificationCalibrationBucket
            {
                LowerInclusive = lower,
                UpperInclusive = upper,
                SampleCount = bucket.Count,
                MeanConfidence = meanConfidence,
                EmpiricalRate = empiricalRate,
            });
        }
        result.Ece = ece;
        return result;
    }
}

/// <summary>Validates untrusted qualification evidence without retaining its raw contents.</summary>
public static class MauiPreviewQualificationInputValidator
{
    /// <summary>Validates bounds and known sample origins before evaluation.</summary>
    public static MauiPreviewQualificationInputValidation Validate(MauiPreviewQualificationInput? input)
    {
        var result = new MauiPreviewQualificationInputValidation();
        if (input is null)
        {
            result.Errors.Add(new("input-missing", "Qualification input is required."));
            return result;
        }
        if (input.Schema != 1)
            result.Errors.Add(new("input-schema-invalid", "Qualification input schema must be 1."));
        foreach (var sample in input.Samples ?? [])
        {
            if (sample is null)
            {
                result.Errors.Add(new("sample-null", "Qualification samples cannot contain null entries."));
                continue;
            }
            if (!MauiQualificationSampleSources.IsKnown(sample.Source))
                result.Errors.Add(new("sample-source-invalid", "Qualification samples require a known source."));
            if (sample.ProbabilityLikeConfidence is < 0 or > 1)
                result.Errors.Add(new("sample-confidence-invalid", "Probability-like confidence must be between zero and one."));
            if (sample.TimeToDiagnosisMs is < 0 || sample.TraceBytes is < 0 || sample.ReportBytes is < 0 || sample.RuntimeOverheadMs is < 0)
                result.Errors.Add(new("sample-measurement-invalid", "Qualification measurements cannot be negative."));
        }
        return result;
    }

    /// <summary>Parses a bounded input JSON document while returning safe fixed diagnostics.</summary>
    public static MauiPreviewQualificationInputParseResult ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > 1_048_576)
            return new MauiPreviewQualificationInputParseResult { ErrorCode = "qualification-input-missing-or-too-large" };
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return new MauiPreviewQualificationInputParseResult { ErrorCode = "qualification-input-not-object" };
            var input = JsonSerializer.Deserialize(json, MauiTestingJsonContext.Default.MauiPreviewQualificationInput);
            var validation = Validate(input);
            return new MauiPreviewQualificationInputParseResult
            {
                Input = input,
                ErrorCode = validation.Errors.Count == 0 ? null : validation.Errors[0].Code,
            };
        }
        catch (JsonException)
        {
            return new MauiPreviewQualificationInputParseResult { ErrorCode = "qualification-input-invalid-json" };
        }
    }
}

/// <summary>Small schema-shaped validation result for qualification reports without a schema runtime dependency.</summary>
public sealed class MauiPreviewQualificationReportValidation
{
    public List<string> Errors { get; } = [];
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Validates required v1 report fields before a host publishes a qualification artifact.</summary>
public static class MauiPreviewQualificationReportValidator
{
    /// <summary>Checks the stable report envelope and safety invariants.</summary>
    public static MauiPreviewQualificationReportValidation Validate(MauiPreviewQualificationReport? report)
    {
        var result = new MauiPreviewQualificationReportValidation();
        if (report is null)
        {
            result.Errors.Add("report-required");
            return result;
        }
        if (report.Schema != 1) result.Errors.Add("schema-must-be-1");
        if (!string.Equals(report.Kind, "maui-preview-qualification", StringComparison.Ordinal))
            result.Errors.Add("kind-invalid");
        if (!string.Equals(report.ContractVersion, "preview-qualification-v1", StringComparison.Ordinal))
            result.Errors.Add("contract-version-invalid");
        if (report.GeneratedAt == default) result.Errors.Add("generated-at-required");
        if (report.Status is not MauiPreviewQualificationStates.Pass and not MauiPreviewQualificationStates.Fail and not MauiPreviewQualificationStates.NotQualified)
            result.Errors.Add("status-invalid");
        if (string.IsNullOrWhiteSpace(report.Platform)) result.Errors.Add("platform-required");
        if (report.Fingerprints is null) result.Errors.Add("fingerprints-required");
        if (report.Profiles is null) result.Errors.Add("profiles-required");
        if (report.FeatureFlags is null) result.Errors.Add("feature-flags-required");
        if (report.Review is null) result.Errors.Add("review-required");
        if (report.Corpus is null) result.Errors.Add("corpus-required");
        if (report.Metrics is null) result.Errors.Add("metrics-required");
        if (report.Thresholds is null) result.Errors.Add("thresholds-required");
        if (report.Gates is null) result.Errors.Add("gates-required");
        if (report.Reasons is null) result.Errors.Add("reasons-required");
        if (report.ArtifactRefs is null) result.Errors.Add("artifact-refs-required");
        if (report.Exclusions is null) result.Errors.Add("exclusions-required");
        if (report.FeatureFlags is { } flags &&
            (flags.AutoApplyRepair || flags.AutoApplySource ||
             flags.ModelProviderEnabled || flags.TelemetryEgressEnabled))
        {
            result.Errors.Add("preview-safety-flags-invalid");
        }
        return result;
    }
}

/// <summary>Bounded qualification-input validation findings.</summary>
public sealed class MauiPreviewQualificationInputValidation
{
    public List<MauiPreviewQualificationInputValidationError> Errors { get; } = [];
}

/// <summary>A safe fixed-text qualification-input validation finding.</summary>
public sealed record MauiPreviewQualificationInputValidationError(string Code, string Message);

/// <summary>Parse result for a qualification evidence input document.</summary>
public sealed class MauiPreviewQualificationInputParseResult
{
    public MauiPreviewQualificationInput? Input { get; init; }
    public string? ErrorCode { get; init; }
    public bool Ok => Input is not null && ErrorCode is null;
}

/// <summary>Shared redaction helpers for qualification reports and artifact ingestion.</summary>
public static class MauiQualificationSanitizer
{
    /// <summary>Returns a SHA-256 fingerprint rather than copying caller-provided text into a report.</summary>
    public static string? Fingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
            value.Length == 71 &&
            value.AsSpan(7).ToString().All(Uri.IsHexDigit))
        {
            return "sha256:" + value[7..].ToLowerInvariant();
        }
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    /// <summary>Returns a non-secret placeholder when a required fingerprint fact was not supplied.</summary>
    public static string FingerprintOrUnknown(string? value) => Fingerprint(value) ?? "unknown";

    /// <summary>Allows only a bounded kind token to appear in a report.</summary>
    public static string SafeKind(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "flow-pilot-manifest" => "flow-pilot-manifest",
            "apple-flow-qa-manifest" => "apple-flow-qa-manifest",
            "flow-digest" => "flow-digest",
            "flow-run-report" => "flow-run-report",
            "mauitrace" => "mauitrace",
            "test-results" => "test-results",
            "host-diagnostic" => "host-diagnostic",
            "json" => "json",
            "package-digest" => "package-digest",
            "app-digest" => "app-digest",
            "failure-evidence" => "failure-evidence",
            "diagnostic-rerun" => "diagnostic-rerun",
            "prerequisite" => "prerequisite",
            "host-platform" => "host-platform",
            "android-host-diagnostics" => "android-host-diagnostics",
            "fixture-initialization-diagnostic" => "fixture-initialization-diagnostic",
            "android-fixture-initialization" => "android-fixture-initialization",
            "qualification-report" => "qualification-report",
            "infrastructure-first-attempt" => "infrastructure-first-attempt",
            "excluded-sample" => "excluded-sample",
            "report" => "report",
            "evidence" => "evidence",
            "audit" => "audit",
            "model-projection" => "model-projection",
            "artifact" => "artifact",
            _ => "unknown",
        };
    }

    /// <summary>Allows only fixed operation labels in a report; external labels become <c>unknown</c>.</summary>
    public static string SafeOperation(string? value) => value switch
    {
        "flow-markdown-parse" => "flow-markdown-parse",
        "flow-validate" => "flow-validate",
        "report-serialize-redaction" => "report-serialize-redaction",
        "fingerprint" => "fingerprint",
        "candidate-generation" => "candidate-generation",
        "candidate-ranking" => "candidate-ranking",
        "qualification-gate" => "qualification-gate",
        "time-to-diagnosis" => "time-to-diagnosis",
        "android-device-overhead" => "android-device-overhead",
        "parse" => "parse",
        _ => "unknown",
    };
}

/// <summary>Transition policy for proposal/grant fuzzing. It never auto-applies a proposal.</summary>
public static class MauiQualificationProposalTransitionPolicy
{
    /// <summary>Returns a fail-closed transition result without performing an apply operation.</summary>
    public static MauiQualificationProposalTransitionResult Evaluate(MauiQualificationProposalTransition? transition)
    {
        if (transition?.ApplyRequested == true)
        {
            return new MauiQualificationProposalTransitionResult
            {
                Allowed = false,
                AutomaticApplyAllowed = false,
                ReasonCode = "automatic-apply-prohibited",
            };
        }
        if (transition?.HumanApprovalRecorded != true || transition.GrantValid != true)
        {
            return new MauiQualificationProposalTransitionResult
            {
                Allowed = false,
                AutomaticApplyAllowed = false,
                ReasonCode = "human-approval-or-grant-missing",
            };
        }
        return new MauiQualificationProposalTransitionResult
        {
            Allowed = true,
            AutomaticApplyAllowed = false,
            ReasonCode = "host-mediated-review-only",
        };
    }
}
