using System.Text.Json;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The two-axis outcome contract: a run's own verdict and the host's owned cleanup are recorded
/// separately, bounded identically, and never allowed to rewrite one another.
/// </summary>
public sealed class SecondaryFailureContractTests
{
    [Fact]
    public void Serialization_BoundsSecondaryFailuresAndKeepsThemIdentifierOnly()
    {
        var report = PassedReport();
        report.SecondaryFailures =
        [
            new MauiFlowSecondaryFailure
            {
                Phase = MauiFlowSecondaryFailurePhases.Cleanup,
                Code = "cleanup-failed",
                Class = MauiFlowFailureClasses.Infrastructure,
                Retryable = true,
            },
            // Duplicate of the entry above: two observations of one phase are one fact.
            new MauiFlowSecondaryFailure
            {
                Phase = MauiFlowSecondaryFailurePhases.Cleanup,
                Code = "cleanup-failed",
                Class = MauiFlowFailureClasses.Infrastructure,
            },
            // A hostile artifact that names one phase many times must not be able to evict a
            // different phase's real failure through the cap.
            new MauiFlowSecondaryFailure { Phase = "cleanup", Code = "a" },
            new MauiFlowSecondaryFailure { Phase = "cleanup", Code = "b" },
            new MauiFlowSecondaryFailure { Phase = "cleanup", Code = "c" },
            new MauiFlowSecondaryFailure { Phase = "cleanup", Code = "d" },
            new MauiFlowSecondaryFailure
            {
                Phase = MauiFlowSecondaryFailurePhases.ArtifactCleanup,
                Code = "artifact-cleanup-failed",
                Class = MauiFlowFailureClasses.Infrastructure,
            },
            // Neither of these can be retained: one has no phase, the other no code.
            new MauiFlowSecondaryFailure { Code = "phaseless" },
            new MauiFlowSecondaryFailure { Phase = MauiFlowSecondaryFailurePhases.ArtifactCleanup },
        ];

        MauiFlowRunReportSerializer.ApplyLimits(report, new MauiFlowRunReportLimits());

        Assert.Equal(
            [MauiFlowSecondaryFailurePhases.ArtifactCleanup, MauiFlowSecondaryFailurePhases.Cleanup],
            report.SecondaryFailures.Select(static failure => failure.Phase!).ToArray());
        Assert.Equal(
            ["artifact-cleanup-failed", "cleanup-failed"],
            report.SecondaryFailures.Select(static failure => failure.Code!).ToArray());
        Assert.All(
            report.SecondaryFailures,
            failure => Assert.Equal(MauiFlowFailureClasses.Infrastructure, failure.Class));
    }

    [Fact]
    public void Serialization_RestatesAnImportedSecondaryFailureClassAsInfrastructure()
    {
        // An imported artifact must not be able to claim a cleanup failure is evidence about the
        // app: `class` is a property of the phase, not something a producer gets to assert.
        var normalized = MauiFlowRunReportSerializer.NormalizeSecondaryFailures(
        [
            new MauiFlowSecondaryFailure
            {
                Phase = MauiFlowSecondaryFailurePhases.Cleanup,
                Code = "cleanup-failed",
                Class = MauiFlowFailureClasses.AssertionFailed,
            },
        ]);

        Assert.Equal(
            MauiFlowFailureClasses.Infrastructure,
            Assert.Single(normalized).Class);
    }

    [Fact]
    public void Serialization_RedactsHostileSecondaryFailureValues()
    {
        var report = PassedReport();
        report.SecondaryFailures =
        [
            new MauiFlowSecondaryFailure
            {
                Phase = MauiFlowSecondaryFailurePhases.Cleanup,
                Code = "cleanup-failed",
                Class = @"C:\Users\someone\secrets\class.txt",
            },
            new MauiFlowSecondaryFailure
            {
                Phase = @"C:\Users\someone\phase",
                Code = "would-leak-a-path",
            },
        ];

        MauiFlowRunReportSerializer.ApplyLimits(report, new MauiFlowRunReportLimits());
        var json = System.Text.Encoding.UTF8.GetString(
            MauiFlowRunReportSerializer.SerializeToUtf8Bytes(report));

        Assert.DoesNotContain("secrets", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("someone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Users", json, StringComparison.Ordinal);
        var retained = Assert.Single(report.SecondaryFailures);
        Assert.Equal(MauiFlowSecondaryFailurePhases.Cleanup, retained.Phase);
        Assert.Equal("cleanup-failed", retained.Code);
        Assert.Equal(MauiFlowFailureClasses.Infrastructure, retained.Class);
    }

    [Fact]
    public void Serialization_CapsSecondaryFailuresFromAForeignArtifact()
    {
        // Phases are not a closed set on the read path, so the cap is the only thing bounding an
        // artifact that invents them.
        var normalized = MauiFlowRunReportSerializer.NormalizeSecondaryFailures(
            Enumerable.Range(0, 12).Select(index => new MauiFlowSecondaryFailure
            {
                Phase = "phase-" + index,
                Code = "code-" + index,
            }));

        Assert.Equal(MauiFlowSecondaryFailurePhases.MaxRetained, normalized.Count);
        Assert.Equal(
            normalized.Select(static failure => failure.Phase).OrderBy(static value => value, StringComparer.Ordinal),
            normalized.Select(static failure => failure.Phase));
    }

    /// <summary>
    /// The cap bounds a hostile artifact; it must not be usable as a way to delete one. Phases that
    /// sort ahead of both real ones alphabetically would, under a plain ordinal sort, push the
    /// run's actual cleanup facts out through the cap and publish a host that never tore itself
    /// down as clean.
    /// </summary>
    [Fact]
    public void Serialization_InventedPhasesCannotEvictARealCleanupFailureThroughTheCap()
    {
        MauiFlowSecondaryFailure[] hostile =
        [
            new() { Phase = "aaa-first", Code = "invented" },
            new() { Phase = "aab-second", Code = "invented" },
            new() { Phase = "aac-third", Code = "invented" },
            new() { Phase = "aad-fourth", Code = "invented" },
            new() { Phase = "aae-fifth", Code = "invented" },
            new()
            {
                Phase = MauiFlowSecondaryFailurePhases.Cleanup,
                Code = "cleanup-failed",
            },
            new()
            {
                Phase = MauiFlowSecondaryFailurePhases.ArtifactCleanup,
                Code = "artifact-cleanup-failed",
            },
        ];

        var normalized = MauiFlowRunReportSerializer.NormalizeSecondaryFailures(hostile);

        Assert.Equal(MauiFlowSecondaryFailurePhases.MaxRetained, normalized.Count);
        Assert.Equal(
            [MauiFlowSecondaryFailurePhases.ArtifactCleanup, MauiFlowSecondaryFailurePhases.Cleanup],
            normalized.Take(2).Select(static failure => failure.Phase!).ToArray());
        Assert.Equal(
            ["artifact-cleanup-failed", "cleanup-failed"],
            normalized.Take(2).Select(static failure => failure.Code!).ToArray());
        // The invented phases are still bounded, and the ones that survive are deterministic.
        Assert.Equal(
            ["aaa-first", "aab-second"],
            normalized.Skip(2).Select(static failure => failure.Phase!).ToArray());
    }

    /// <summary>
    /// The projection has to be a function of the set, not of the order a host or an artifact
    /// happened to list it in, or two artifacts describing the same run disagree about what was
    /// retained.
    /// </summary>
    [Fact]
    public void Serialization_SecondaryFailureOrderIsIndependentOfTheObservedOrder()
    {
        MauiFlowSecondaryFailure[] observed =
        [
            new() { Phase = "zzz-invented", Code = "invented" },
            new() { Phase = MauiFlowSecondaryFailurePhases.Cleanup, Code = "cleanup-failed" },
            new() { Phase = "aaa-invented", Code = "invented" },
            new()
            {
                Phase = MauiFlowSecondaryFailurePhases.ArtifactCleanup,
                Code = "artifact-cleanup-failed",
            },
        ];

        var forward = MauiFlowRunReportSerializer.NormalizeSecondaryFailures(observed);
        var reversed = MauiFlowRunReportSerializer.NormalizeSecondaryFailures(observed.Reverse());

        Assert.Equal(
            forward.Select(static failure => failure.Phase),
            reversed.Select(static failure => failure.Phase));
        Assert.Equal(
            [
                MauiFlowSecondaryFailurePhases.ArtifactCleanup,
                MauiFlowSecondaryFailurePhases.Cleanup,
                "aaa-invented",
                "zzz-invented",
            ],
            forward.Select(static failure => failure.Phase!).ToArray());
    }

    /// <summary>
    /// The manifest mirrors the report through the same projection, so a hostile artifact cannot
    /// make the two disagree about which cleanup facts survived.
    /// </summary>
    [Fact]
    public void ManifestSerialization_KeepsTheKnownCleanupPhasesAgainstInventedOnes()
    {
        var manifest = new MauiTestExecutionManifest
        {
            RunId = "run-1",
            Outcome = new MauiTestExecutionOutcomeFacts
            {
                Status = MauiFlowRunOutcomes.Passed,
                ExitCategory = "infrastructure-failure",
                Terminal = true,
                SecondaryFailures =
                [
                    new() { Phase = "aaa-first", Code = "invented" },
                    new() { Phase = "aab-second", Code = "invented" },
                    new() { Phase = "aac-third", Code = "invented" },
                    new() { Phase = "aad-fourth", Code = "invented" },
                    new()
                    {
                        Phase = MauiFlowSecondaryFailurePhases.Cleanup,
                        Code = "cleanup-failed",
                    },
                ],
            },
        };

        var projection = MauiTestExecutionManifestSerializer.CreateSafeProjection(manifest);

        Assert.Equal(
            MauiFlowSecondaryFailurePhases.Cleanup,
            projection.Outcome!.SecondaryFailures[0].Phase);
        Assert.Equal(
            MauiFlowSecondaryFailurePhases.MaxRetained,
            projection.Outcome.SecondaryFailures.Count);
    }

    [Fact]
    public void Serialization_RoundTripsSecondaryFailuresWithoutTouchingThePrimaryVerdict()
    {
        var report = PassedReport();
        report.SecondaryFailures =
        [
            new MauiFlowSecondaryFailure
            {
                Phase = MauiFlowSecondaryFailurePhases.Cleanup,
                Code = "cleanup-exception",
                Class = MauiFlowFailureClasses.Infrastructure,
                Retryable = true,
            },
        ];

        MauiFlowRunReportSerializer.ApplyLimits(report, new MauiFlowRunReportLimits());
        var restored = JsonSerializer.Deserialize(
            MauiFlowRunReportSerializer.SerializeToUtf8Bytes(report),
            MauiTestingJsonContext.Default.MauiFlowRunReport)!;

        Assert.Equal(MauiFlowRunOutcomes.Passed, restored.Outcome?.Status);
        Assert.Null(restored.Failure);
        var secondary = Assert.Single(restored.SecondaryFailures);
        Assert.Equal(MauiFlowSecondaryFailurePhases.Cleanup, secondary.Phase);
        Assert.Equal("cleanup-exception", secondary.Code);
        Assert.Equal(MauiFlowFailureClasses.Infrastructure, secondary.Class);
        Assert.True(secondary.Retryable);
        // The report stays valid: a pass with a cleanup problem is not a contradictory report.
        Assert.True(MauiFlowRunReportSerializer.Validate(restored).IsValid);
    }

    /// <summary>
    /// A restatement writes the displaced verdict here, and runs produced before the two-axis
    /// contract wrote their pre-cleanup verdict here too. Both have to survive the report limits.
    /// </summary>
    [Fact]
    public void Serialization_AllowlistsThePrimaryExecutionOutcome()
    {
        var report = PassedReport();
        report.Outcome!.Status = MauiFlowRunOutcomes.InfrastructureError;
        report.Failure = new MauiFlowFailure
        {
            Class = MauiFlowFailureClasses.Infrastructure,
            Code = "cleanup-failed",
            Phase = "cleanup",
        };
        report.ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["exitCategory"] = Element("""  "infrastructure-failure"  """),
            ["primaryExecutionOutcome"] = Element(
                """{ "exitCategory": "test-failure", "status": "failed", "verified": false, "leaked": "C:\\Users\\someone" }"""),
        };

        MauiFlowRunReportSerializer.ApplyLimits(report, new MauiFlowRunReportLimits());

        var primary = report.ExtensionData!["primaryExecutionOutcome"];
        Assert.Equal("test-failure", primary.GetProperty("exitCategory").GetString());
        Assert.Equal("failed", primary.GetProperty("status").GetString());
        Assert.False(primary.GetProperty("verified").GetBoolean());
        Assert.False(primary.TryGetProperty("leaked", out _));
    }

    /// <summary>
    /// The discriminator that separates a restatement from the object's older, cleanup meaning is
    /// only useful if it survives the projection every report goes through on the way to disk —
    /// and only if it stays a closed set, so an artifact cannot assert a third meaning for the
    /// object that no reader has.
    /// </summary>
    [Fact]
    public void Serialization_KeepsTheRestatementDiscriminatorAndDropsInventedMembers()
    {
        var report = PassedReport();
        report.ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["primaryExecutionOutcome"] = Element(
                """
                {
                  "displacedBy": "restatement",
                  "exitCategory": "pass",
                  "status": "passed",
                  "verified": true,
                  "displacedByHost": "C:\\Users\\someone"
                }
                """),
        };

        MauiFlowRunReportSerializer.ApplyLimits(report, new MauiFlowRunReportLimits());

        var primary = report.ExtensionData!["primaryExecutionOutcome"];
        Assert.Equal(
            MauiFlowPrimaryOutcomeDisplacements.Restatement,
            primary.GetProperty("displacedBy").GetString());
        Assert.True(primary.GetProperty("verified").GetBoolean());
        Assert.False(primary.TryGetProperty("displacedByHost", out _));
    }

    [Theory]
    [InlineData("\"cleanup\"")]
    [InlineData("\"Restatement\"")]
    [InlineData("\"\"")]
    [InlineData("true")]
    public void Serialization_DropsAnInventedDisplacementReason(string displacedBy)
    {
        var report = PassedReport();
        report.ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["primaryExecutionOutcome"] = Element(
                $$"""{ "displacedBy": {{displacedBy}}, "status": "passed", "verified": true }"""),
        };

        MauiFlowRunReportSerializer.ApplyLimits(report, new MauiFlowRunReportLimits());

        var primary = report.ExtensionData!["primaryExecutionOutcome"];
        // Dropping it makes the object read as the pre-contract one it is indistinguishable from,
        // which is the conservative reading. The rest of the object is still published.
        Assert.False(primary.TryGetProperty("displacedBy", out _));
        Assert.Equal(MauiFlowRunOutcomes.Passed, primary.GetProperty("status").GetString());
    }

    [Fact]
    public void ManifestSerialization_MirrorsAndBoundsSecondaryFailures()
    {
        var manifest = new MauiTestExecutionManifest
        {
            RunId = "run-1",
            Outcome = new MauiTestExecutionOutcomeFacts
            {
                Status = MauiFlowRunOutcomes.Passed,
                ExitCategory = "infrastructure-failure",
                Terminal = true,
                Verified = true,
                UnknownCompletion = false,
                SecondaryFailures =
                [
                    new MauiFlowSecondaryFailure
                    {
                        Phase = MauiFlowSecondaryFailurePhases.Cleanup,
                        Code = "cleanup-failed",
                        Class = MauiFlowFailureClasses.Infrastructure,
                        Retryable = true,
                    },
                    new MauiFlowSecondaryFailure
                    {
                        Phase = MauiFlowSecondaryFailurePhases.ArtifactCleanup,
                        Code = "artifact-cleanup-failed",
                        Class = MauiFlowFailureClasses.Infrastructure,
                        Retryable = true,
                    },
                    new MauiFlowSecondaryFailure { Phase = @"C:\Users\someone", Code = "leak" },
                ],
            },
        };

        var projection = MauiTestExecutionManifestSerializer.CreateSafeProjection(manifest);

        Assert.Equal(MauiFlowRunOutcomes.Passed, projection.Outcome?.Status);
        Assert.True(projection.Outcome?.Verified);
        Assert.Equal(
            [MauiFlowSecondaryFailurePhases.ArtifactCleanup, MauiFlowSecondaryFailurePhases.Cleanup],
            projection.Outcome!.SecondaryFailures.Select(static failure => failure.Phase!).ToArray());
    }

    [Fact]
    public void Triage_ExpectedExitCategory_PromotesOnlyPassAndUnverified()
    {
        // A cleanup failure may make the command fail; it may never relabel a real regression or a
        // fail-closed refusal as a retryable environment problem.
        Assert.Equal("infrastructure-failure", ExpectedWithCleanupFailure(PassedReport(verified: true)));
        Assert.Equal("infrastructure-failure", ExpectedWithCleanupFailure(PassedReport()));
        Assert.Equal(
            "test-failure",
            ExpectedWithCleanupFailure(FailedReport(MauiFlowRunOutcomes.Failed, MauiFlowFailureClasses.AssertionFailed)));
        Assert.Equal(
            "unknown-completion",
            ExpectedWithCleanupFailure(FailedReport(MauiFlowRunOutcomes.UnknownCompletion, MauiFlowFailureClasses.UnknownCompletion)));
        Assert.Equal(
            "invalid-configuration",
            ExpectedWithCleanupFailure(FailedReport(MauiFlowRunOutcomes.Failed, MauiFlowFailureClasses.FlowInvalid)));
        Assert.Equal(
            "infrastructure-failure",
            ExpectedWithCleanupFailure(FailedReport(MauiFlowRunOutcomes.InfrastructureError, MauiFlowFailureClasses.Infrastructure)));
    }

    /// <summary>
    /// The analyzer does not publish the category it expects, but it reports an
    /// <c>exit-category-match</c> shortfall for every manifest category except that one, so the
    /// value is recovered by probing the candidates.
    /// </summary>
    private static string? ExpectedWithCleanupFailure(MauiFlowRunReport report)
    {
        report.SecondaryFailures =
        [
            new MauiFlowSecondaryFailure
            {
                Phase = MauiFlowSecondaryFailurePhases.Cleanup,
                Code = "cleanup-failed",
                Class = MauiFlowFailureClasses.Infrastructure,
                Retryable = true,
            },
        ];
        var manifest = new MauiTestExecutionManifest
        {
            RunId = report.RunId,
            Outcome = new MauiTestExecutionOutcomeFacts
            {
                Status = report.Outcome?.Status,
                Terminal = true,
                Verified = report.Outcome?.Verified ?? false,
                SecondaryFailures = report.SecondaryFailures,
            },
        };
        string? accepted = null;
        foreach (var candidate in new[]
                 {
                     "pass",
                     "unverified",
                     "test-failure",
                     "unknown-completion",
                     "invalid-configuration",
                     "infrastructure-failure",
                 })
        {
            manifest.Outcome!.ExitCategory = candidate;
            var triage = MauiFlowTriageAnalyzer.Analyze(new MauiFlowTriageInput
            {
                Report = report,
                Manifest = manifest,
                IsCurrentLocalRun = true,
            });
            if (triage.Evidence!.MissingFacts.Contains("exit-category-match"))
                continue;
            Assert.Null(accepted);
            accepted = candidate;
        }
        return accepted;
    }

    [Fact]
    public void Triage_ManifestThatDropsTheCleanupFailure_IsInconsistentEvidence()
    {
        var report = PassedReport();
        report.SecondaryFailures =
        [
            new MauiFlowSecondaryFailure
            {
                Phase = MauiFlowSecondaryFailurePhases.Cleanup,
                Code = "cleanup-failed",
                Class = MauiFlowFailureClasses.Infrastructure,
                Retryable = true,
            },
        ];
        var manifest = new MauiTestExecutionManifest
        {
            RunId = report.RunId,
            Outcome = new MauiTestExecutionOutcomeFacts
            {
                Status = MauiFlowRunOutcomes.Passed,
                ExitCategory = "infrastructure-failure",
                Terminal = true,
                Verified = false,
            },
        };

        var triage = MauiFlowTriageAnalyzer.Analyze(new MauiFlowTriageInput
        {
            Report = report,
            Manifest = manifest,
            IsCurrentLocalRun = true,
        });

        Assert.Contains("secondary-failure-match", triage.Evidence!.MissingFacts);
    }

    /// <summary>
    /// A cleanup failure must not become a reason to repair. It is an explicit, separately named
    /// refusal so it cannot be mistaken for a classifier defect on an otherwise repairable run.
    /// </summary>
    [Fact]
    public void Repair_IsRefusedWhenOwnedCleanupDidNotComplete()
    {
        var report = new MauiFlowRunReport
        {
            RunId = "run-repair",
            Outcome = new MauiFlowRunOutcome
            {
                Status = MauiFlowRunOutcomes.Failed,
                Terminal = true,
                Verified = false,
            },
            Failure = new MauiFlowFailure
            {
                Class = MauiFlowFailureClasses.LocatorNotFound,
                Code = MauiFlowFailureClasses.LocatorNotFound,
                Phase = "resolution",
                StepId = "step-1",
            },
        };

        var clean = MauiFlowRepairEligibilityEvaluator.Evaluate(new MauiFlowRepairEligibilityInput
        {
            Run = report,
            IsCurrentLocalRun = true,
        });
        Assert.DoesNotContain(clean.Reasons, reason => reason.Code == "owned-cleanup-incomplete");

        report.SecondaryFailures =
        [
            new MauiFlowSecondaryFailure
            {
                Phase = MauiFlowSecondaryFailurePhases.Cleanup,
                Code = "cleanup-failed",
                Class = MauiFlowFailureClasses.Infrastructure,
                Retryable = true,
            },
        ];
        var blocked = MauiFlowRepairEligibilityEvaluator.Evaluate(new MauiFlowRepairEligibilityInput
        {
            Run = report,
            IsCurrentLocalRun = true,
        });

        Assert.False(blocked.Eligible);
        // The refusal is additive: the cleanup failure adds exactly one blocking reason and
        // changes nothing else, so it cannot be mistaken for a classifier defect.
        Assert.Equal(clean.Reasons.Count + 1, blocked.Reasons.Count);
        Assert.Equal(
            clean.Reasons.Select(static reason => reason.Code),
            blocked.Reasons.Where(static reason => reason.Code != "owned-cleanup-incomplete")
                .Select(static reason => reason.Code));
        Assert.True(
            Assert.Single(
                blocked.Reasons,
                candidate => candidate.Code == "owned-cleanup-incomplete").Blocking);
    }

    [Fact]
    public void Repair_NullSecondaryFailures_IsReadAsNoCleanupFailure()
    {
        // System.Text.Json assigns null to a List<T> property for an explicit JSON null, so an
        // imported artifact reaches the public evaluator with a null list.
        var report = JsonSerializer.Deserialize(
            """{ "schema": 1, "runId": "run-null", "secondaryFailures": null }""",
            MauiTestingJsonContext.Default.MauiFlowRunReport)!;

        var decision = MauiFlowRepairEligibilityEvaluator.Evaluate(new MauiFlowRepairEligibilityInput
        {
            Run = report,
            IsCurrentLocalRun = true,
        });

        Assert.DoesNotContain(decision.Reasons, reason => reason.Code == "owned-cleanup-incomplete");
    }

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static MauiFlowRunReport PassedReport(bool verified = false) => new()
    {
        RunId = "run-secondary",
        FlowDigest = "sha256:" + new string('a', 64),
        StartedAt = DateTimeOffset.UnixEpoch,
        EndedAt = DateTimeOffset.UnixEpoch.AddSeconds(1),
        Outcome = new MauiFlowRunOutcome
        {
            Status = MauiFlowRunOutcomes.Passed,
            Terminal = true,
            Verified = verified,
        },
        Verification = verified
            ? new MauiFlowRunVerification { Verified = true }
            : null,
        ReplayEligibility = verified
            ? new MauiFlowReplayEligibilityDecision { RunVerificationAllowed = true }
            : null,
    };

    private static MauiFlowRunReport FailedReport(string status, string failureClass) => new()
    {
        RunId = "run-secondary",
        StartedAt = DateTimeOffset.UnixEpoch,
        EndedAt = DateTimeOffset.UnixEpoch.AddSeconds(1),
        Outcome = new MauiFlowRunOutcome
        {
            Status = status,
            Terminal = true,
            Verified = false,
        },
        Failure = new MauiFlowFailure
        {
            Class = failureClass,
            Code = failureClass,
        },
    };
}
