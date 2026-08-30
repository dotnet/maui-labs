using System.Diagnostics;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>Deterministic host-only micro-measurement helpers for qualification reports.</summary>
public static class MauiPreviewQualificationPerformance
{
    /// <summary>Measures a bounded operation and returns p50/p95/max wall-clock milliseconds.</summary>
    public static MauiQualificationDurationMetric Measure(
        string operation,
        Action action,
        int iterations = 20)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(action);
        iterations = Math.Clamp(iterations, 1, 200);

        // Warm-up is intentionally outside the reported distribution.
        action();
        var values = new double[iterations];
        for (var index = 0; index < iterations; index++)
        {
            var timer = Stopwatch.StartNew();
            action();
            timer.Stop();
            values[index] = timer.Elapsed.TotalMilliseconds;
        }
        Array.Sort(values);
        return new MauiQualificationDurationMetric
        {
            State = "measured",
            Operation = operation,
            SampleCount = values.Length,
            P50Ms = MauiQualificationStatistics.Percentile(values, 0.50),
            P95Ms = MauiQualificationStatistics.Percentile(values, 0.95),
            MaxMs = values[^1],
        };
    }

    /// <summary>
    /// Measures parse, validation, report/redaction, fingerprint, candidate/ranking, and gate
    /// operations in-process. It never connects to an app; device overhead remains explicitly missing.
    /// </summary>
    public static MauiQualificationRuntimeOverheadMetric MeasureDeterministicHostOperations(int iterations = 20)
    {
        var flow = CreateFlow();
        var markdown = FlowMarkdown.Serialize(flow);
        var report = CreateReport();
        var observation = CreateObservation();
        var fingerprint = MauiElementFingerprintBuilder.Build(observation);
        var target = observation.Target!;
        var candidates = MauiSelectorCandidateGenerator.Generate(observation);
        var input = new MauiPreviewQualificationInput
        {
            Platform = "android",
            Corpus = new MauiQualificationCorpusSummary
            {
                ManifestValid = true,
                CaseSchemaValid = true,
                SecurityCorpus = new MauiQualificationSecurityCorpusSummary
                {
                    Valid = true,
                    CaseCount = 1,
                    PassedCount = 1,
                },
            },
            PrivacySecurity = new MauiQualificationPrivacySecurityMetric
            {
                State = "measured",
                TestCount = 1,
                CanaryScanPassed = true,
            },
        };

        return new MauiQualificationRuntimeOverheadMetric
        {
            HostOperations =
            [
                Measure("flow-markdown-parse", () => _ = FlowMarkdown.Parse(markdown), iterations),
                Measure("flow-validate", () => _ = FlowValidator.Validate(flow), iterations),
                Measure("report-serialize-redaction", () => _ = MauiFlowRunReportSerializer.SerializeToUtf8Bytes(CloneReport(report)), iterations),
                Measure("fingerprint", () => _ = MauiElementFingerprintBuilder.Build(observation), iterations),
                Measure("candidate-generation", () => _ = MauiSelectorCandidateGenerator.Generate(observation), iterations),
                Measure("candidate-ranking", () => _ = MauiSelectorCandidateGenerator.Generate(fingerprint, target, observation.Elements), iterations),
                Measure("qualification-gate", () => _ = MauiPreviewQualificationGateEvaluator.Evaluate(input), iterations),
            ],
            DeviceOverhead = new MauiQualificationDurationMetric
            {
                State = "missing",
                Operation = "android-device-overhead",
                MissingReason = "No Android pilot artifact supplied device-overhead evidence.",
            },
        };
    }

    private static MauiFlow CreateFlow() => new()
    {
        Name = "qualification-host-measurement",
        App = "com.example.preview",
        Platform = "android",
        Steps =
        [
            new FlowStep
            {
                Seq = 1,
                Action = FlowActions.Tap,
                Target = new FlowSelector { AutomationId = "save" },
                Asserts =
                [
                    new FlowAssert
                    {
                        Kind = "exists",
                        Selector = new FlowSelector { AutomationId = "save" },
                        Verify = true,
                    },
                ],
            },
        ],
    };

    private static MauiFlowRunReport CreateReport() => new()
    {
        RunId = "qualification-performance",
        FlowDigest = "performance-flow",
        StartedAt = DateTimeOffset.UnixEpoch,
        EndedAt = DateTimeOffset.UnixEpoch.AddSeconds(1),
        Outcome = new MauiFlowRunOutcome
        {
            Status = MauiFlowRunOutcomes.Passed,
            Terminal = true,
            Verified = false,
        },
        Steps =
        [
            new MauiFlowStepAttempt
            {
                StepId = "1",
                Intent = "measure",
                SelectorCandidates =
                [
                    new MauiSelectorCandidate
                    {
                        CandidateId = "candidate",
                        SelectorDescriptor = new MauiSelectorCandidateSelector
                        {
                            Kind = "automation-id",
                            AutomationId = "save",
                        },
                    },
                ],
            },
        ],
    };

    private static MauiFlowRunReport CloneReport(MauiFlowRunReport report) => new()
    {
        RunId = report.RunId,
        FlowDigest = report.FlowDigest,
        StartedAt = report.StartedAt,
        EndedAt = report.EndedAt,
        Outcome = new MauiFlowRunOutcome
        {
            Status = report.Outcome?.Status,
            Terminal = report.Outcome?.Terminal,
            Verified = report.Outcome?.Verified,
        },
        Steps = report.Steps.Select(static step => new MauiFlowStepAttempt
        {
            StepId = step.StepId,
            Intent = step.Intent,
            SelectorCandidates = step.SelectorCandidates.Select(static candidate => new MauiSelectorCandidate
            {
                CandidateId = candidate.CandidateId,
                SelectorDescriptor = new MauiSelectorCandidateSelector
                {
                    Kind = candidate.SelectorDescriptor.Kind,
                    AutomationId = candidate.SelectorDescriptor.AutomationId,
                },
            }).ToList(),
        }).ToList(),
    };

    private static MauiSelectorObservation CreateObservation()
    {
        var target = new MauiSelectorObservationElement
        {
            Id = "save",
            Type = "Button",
            FullType = "Microsoft.Maui.Controls.Button",
            Role = "button",
            Traits = ["interactive"],
            AutomationId = "save",
            IsVisible = true,
            IsEnabled = true,
        };
        return new MauiSelectorObservation
        {
            Target = target,
            Elements = [target],
            Context = new MauiSelectorObservationContext
            {
                AppId = "com.example.preview",
                AppBuild = "build",
                Platform = "android",
                Route = "/",
                Window = "main",
                Locale = "en-US",
                Theme = "light",
                Orientation = "portrait",
                DisplayProfile = "320x640",
            },
        };
    }
}
