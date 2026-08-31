using System.Text.Json;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public class AppCrashSignalTests
{
    [Fact]
    public void ProvesAppCrash_AgentDisconnectWithoutProcessEvidence_IsNotACrash()
    {
        var facts = new MauiFlowFailureFacts
        {
            FailureClass = MauiFlowFailureClasses.AgentDisconnected,
            AgentDisconnected = true,
        };

        Assert.False(MauiFlowFailureClassifier.ProvesAppCrash(facts));
        Assert.Equal(
            MauiFlowFailureClasses.AgentDisconnected,
            MauiFlowFailureClassifier.Classify(facts).FailureClass);
        Assert.Equal(
            MauiFlowTriageDispositions.Inconclusive,
            MauiFlowFailureClassifier.Project(MauiFlowFailureClasses.AgentDisconnected));
    }

    [Fact]
    public void ProvesAppCrash_ProcessGoneWithNoNamedReason_StaysUncertain()
    {
        var facts = new MauiFlowFailureFacts
        {
            FailureClass = MauiFlowFailureClasses.AgentDisconnected,
            AppProcessExited = true,
        };

        Assert.False(MauiFlowFailureClassifier.ProvesAppCrash(facts));
        Assert.Equal(
            MauiFlowFailureClasses.AgentDisconnected,
            MauiFlowFailureClassifier.Classify(facts).FailureClass);
    }

    [Fact]
    public void ProvesAppCrash_NonZeroExitCodeAlone_IsNotEnough()
    {
        var facts = new MauiFlowFailureFacts
        {
            FailureClass = MauiFlowFailureClasses.AgentDisconnected,
            AppProcessExited = true,
            AppExitCode = 1,
        };

        Assert.False(MauiFlowFailureClassifier.ProvesAppCrash(facts));
    }

    [Theory]
    [InlineData(MauiFlowAppExitReasons.UserRequested)]
    [InlineData(MauiFlowAppExitReasons.Signaled)]
    [InlineData(MauiFlowAppExitReasons.ExitSelf)]
    [InlineData(MauiFlowAppExitReasons.LowMemory)]
    [InlineData(MauiFlowAppExitReasons.Unknown)]
    public void ProvesAppCrash_DeliberateOrAmbiguousExit_IsNotACrash(string reason)
    {
        var facts = new MauiFlowFailureFacts
        {
            FailureClass = MauiFlowFailureClasses.AgentDisconnected,
            AppProcessExited = true,
            AppExitReason = reason,
        };

        Assert.False(MauiFlowFailureClassifier.ProvesAppCrash(facts));
        Assert.Equal(
            MauiFlowFailureClasses.AgentDisconnected,
            MauiFlowFailureClassifier.Classify(facts).FailureClass);
    }

    [Theory]
    [InlineData(MauiFlowAppExitReasons.Crash)]
    [InlineData(MauiFlowAppExitReasons.CrashNative)]
    [InlineData(MauiFlowAppExitReasons.Anr)]
    public void ProvesAppCrash_AbnormalNamedReason_IsACrash(string reason)
    {
        var facts = new MauiFlowFailureFacts
        {
            AppProcessExited = true,
            AppExitReason = reason,
        };

        Assert.True(MauiFlowFailureClassifier.ProvesAppCrash(facts));
    }

    [Fact]
    public void ProvesAppCrash_CrashRecordWithoutNamedReason_IsACrash()
    {
        var facts = new MauiFlowFailureFacts
        {
            AppProcessExited = true,
            CrashLogPresent = true,
        };

        Assert.True(MauiFlowFailureClassifier.ProvesAppCrash(facts));
    }

    [Fact]
    public void Classify_ProvenCrashDuringAgentLoss_OutranksTheDisconnectSymptom()
    {
        var classification = MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
        {
            TerminalOutcome = MauiFlowRunOutcomes.Orphaned,
            AgentDisconnected = true,
            AppProcessExited = true,
            AppExitReason = MauiFlowAppExitReasons.Crash,
            CrashLogPresent = true,
        });

        Assert.Equal(MauiFlowFailureClasses.AppCrash, classification.FailureClass);
        Assert.Equal(MauiFlowClassificationBases.Inferred, classification.Basis);
        Assert.Equal(
            MauiFlowTriageDispositions.AppRegression,
            MauiFlowFailureClassifier.Project(classification.FailureClass));
    }

    [Fact]
    public void Classify_ProvenCrashWithUnprovenCompletion_KeepsTheFailClosedAnswer()
    {
        var classification = MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
        {
            CompletionCertain = false,
            AppProcessExited = true,
            AppExitReason = MauiFlowAppExitReasons.Crash,
            CrashLogPresent = true,
        });

        Assert.Equal(MauiFlowFailureClasses.UnknownCompletion, classification.FailureClass);
        Assert.Equal(
            MauiFlowTriageDispositions.Inconclusive,
            MauiFlowFailureClassifier.Project(classification.FailureClass));
    }

    [Theory]
    [InlineData(MauiFlowFailureClasses.FlowInvalid)]
    [InlineData(MauiFlowFailureClasses.SchemaUnsupported)]
    [InlineData(MauiFlowFailureClasses.CapabilityMissing)]
    [InlineData(MauiFlowFailureClasses.ResetFailed)]
    [InlineData(MauiFlowFailureClasses.LeaseConflict)]
    [InlineData(MauiFlowFailureClasses.LeaseLost)]
    [InlineData(MauiFlowFailureClasses.Cancelled)]
    [InlineData(MauiFlowFailureClasses.Timeout)]
    [InlineData(MauiFlowFailureClasses.SecretUnavailable)]
    [InlineData(MauiFlowFailureClasses.UnsafeValue)]
    [InlineData(MauiFlowFailureClasses.PreconditionUnsatisfied)]
    public void Classify_ProvenCrash_NeverDisplacesARefusalToRun(string declared)
    {
        var classification = MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
        {
            FailureClass = declared,
            AppProcessExited = true,
            AppExitReason = MauiFlowAppExitReasons.Crash,
            CrashLogPresent = true,
        });

        Assert.Equal(declared, classification.FailureClass);
    }

    [Fact]
    public void Classify_ProvenCrash_ReplacesAnAssertionMiss()
    {
        var classification = MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
        {
            FailureClass = MauiFlowFailureClasses.AssertionFailed,
            AppProcessExited = true,
            AppExitReason = MauiFlowAppExitReasons.CrashNative,
        });

        Assert.Equal(MauiFlowFailureClasses.AppCrash, classification.FailureClass);
    }

    [Fact]
    public void AppCrash_ProjectsToAppRegressionAndIsNotRetryable()
    {
        var classification = MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
        {
            AppProcessExited = true,
            CrashLogPresent = true,
        });

        Assert.Equal(MauiFlowFailureClasses.AppCrash, classification.FailureClass);
        Assert.Equal("app", classification.Category);
        Assert.Equal("execution", classification.Phase);
        Assert.False(classification.Retryable);
        Assert.Equal(
            MauiFlowTriageDispositions.AppRegression,
            MauiFlowFailureClassifier.Project(MauiFlowFailureClasses.AppCrash));
    }

    [Fact]
    public void ProvesAbnormalExit_MirrorsTheClassifierRule()
    {
        Assert.False(new MauiFlowAppProcessEvidence { Probed = true }.ProvesAbnormalExit());
        Assert.False(new MauiFlowAppProcessEvidence
        {
            Probed = true,
            ProcessExited = true,
            ExitReason = MauiFlowAppExitReasons.UserRequested,
        }.ProvesAbnormalExit());
        Assert.True(new MauiFlowAppProcessEvidence
        {
            Probed = true,
            ProcessExited = true,
            ExitReason = MauiFlowAppExitReasons.Anr,
        }.ProvesAbnormalExit());
    }

    [Fact]
    public void Triage_ReadsAppProcessEvidenceFromTheReport()
    {
        var report = new MauiFlowRunReport
        {
            RunId = "run-1",
            Outcome = new MauiFlowRunOutcome { Status = MauiFlowRunOutcomes.Failed, Terminal = true },
            Failure = new MauiFlowFailure
            {
                FailureId = "failure-run-1",
                Class = MauiFlowFailureClasses.AgentDisconnected,
                Code = MauiFlowFailureClasses.AgentDisconnected,
            },
            AppProcess = new MauiFlowAppProcessEvidence
            {
                Probed = true,
                Source = "adb",
                ProcessExited = true,
                ExitReason = MauiFlowAppExitReasons.Crash,
                CrashLogPresent = true,
            },
        };

        var triage = MauiFlowTriageAnalyzer.Analyze(new MauiFlowTriageInput { Report = report });

        Assert.Equal(MauiFlowFailureClasses.AppCrash, triage.Classification.FailureClass);
        Assert.Equal(MauiFlowTriageDispositions.AppRegression, triage.Classification.Disposition);
    }

    [Fact]
    public void Triage_AgentLossWithoutCrashEvidence_StaysInconclusive()
    {
        var report = new MauiFlowRunReport
        {
            RunId = "run-2",
            Outcome = new MauiFlowRunOutcome { Status = MauiFlowRunOutcomes.Failed, Terminal = true },
            Failure = new MauiFlowFailure
            {
                FailureId = "failure-run-2",
                Class = MauiFlowFailureClasses.AgentDisconnected,
                Code = MauiFlowFailureClasses.AgentDisconnected,
            },
            AppProcess = new MauiFlowAppProcessEvidence
            {
                Probed = true,
                Source = "adb",
                ProbeError = "probe-failed",
            },
        };

        var triage = MauiFlowTriageAnalyzer.Analyze(new MauiFlowTriageInput { Report = report });

        Assert.Equal(MauiFlowFailureClasses.AgentDisconnected, triage.Classification.FailureClass);
        Assert.Equal(MauiFlowTriageDispositions.Inconclusive, triage.Classification.Disposition);
    }

    [Fact]
    public void Serialization_RedactsCrashEvidenceAndBoundsTheExcerpt()
    {
        var report = new MauiFlowRunReport
        {
            RunId = "run-3",
            Outcome = new MauiFlowRunOutcome { Status = MauiFlowRunOutcomes.Failed, Terminal = true },
            AppProcess = new MauiFlowAppProcessEvidence
            {
                Probed = true,
                Source = "adb",
                ProcessExited = true,
                ExitReason = MauiFlowAppExitReasons.Crash,
                CrashLogPresent = true,
                CrashSignature = "java.lang.Exception at C:\\Users\\someone\\app\\Main.cs:12",
                CrashExcerpt = Enumerable.Range(0, 40)
                    .Select(index => $"line {index} C:\\Users\\someone\\secrets\\file{index}.txt")
                    .ToList(),
            },
        };

        var json = System.Text.Encoding.UTF8.GetString(
            MauiFlowRunReportSerializer.SerializeToUtf8Bytes(report));

        Assert.DoesNotContain("C:\\Users\\someone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:/Users/someone", json, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(report.AppProcess.CrashExcerpt);
        Assert.True(report.AppProcess.CrashExcerpt!.Count <= 12);
    }

    /// <summary>
    /// Triage independently recomputes the exit category the runner should have written and scores
    /// the evidence insufficient when the two disagree. A crashed run must therefore satisfy that
    /// cross-check, otherwise every real crash reports itself as inadequately evidenced.
    /// </summary>
    [Fact]
    public void Triage_CrashedRunExitCategoryAgreesWithTheRunner()
    {
        var report = new MauiFlowRunReport
        {
            RunId = "run-4",
            StartedAt = new DateTimeOffset(2026, 8, 17, 13, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2026, 8, 17, 13, 1, 0, TimeSpan.Zero),
            Outcome = new MauiFlowRunOutcome
            {
                Status = MauiFlowRunOutcomes.InfrastructureError,
                Terminal = true,
                Verified = false,
            },
            Failure = new MauiFlowFailure
            {
                FailureId = "failure-run-4",
                Class = MauiFlowFailureClasses.AppCrash,
                Code = MauiFlowFailureClasses.AppCrash,
            },
            AppProcess = new MauiFlowAppProcessEvidence
            {
                Probed = true,
                Source = "android-adb",
                ProcessExited = true,
                ExitReason = MauiFlowAppExitReasons.Crash,
                CrashLogPresent = true,
            },
        };
        report.ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["exitCategory"] = JsonSerializer.SerializeToElement("test-failure"),
        };
        var manifest = new MauiTestExecutionManifest
        {
            Outcome = new MauiTestExecutionOutcomeFacts
            {
                Status = MauiFlowRunOutcomes.InfrastructureError,
                ExitCategory = "test-failure",
                Terminal = true,
                Verified = false,
                UnknownCompletion = false,
            },
        };

        var triage = MauiFlowTriageAnalyzer.Analyze(new MauiFlowTriageInput
        {
            Report = report,
            Manifest = manifest,
        });

        Assert.DoesNotContain("exit-category-match", triage.Evidence.MissingFacts);
        Assert.DoesNotContain("report-exit-category-match", triage.Evidence.MissingFacts);
    }

    /// <summary>
    /// The crash rule must not outrank a lost mutation: an orphaned run stays unknown-completion so
    /// the cross-check keeps demanding the fail-closed category.
    /// </summary>
    [Fact]
    public void Triage_CrashedButOrphanedRun_StillExpectsUnknownCompletion()
    {
        var report = new MauiFlowRunReport
        {
            RunId = "run-5",
            StartedAt = new DateTimeOffset(2026, 8, 17, 13, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2026, 8, 17, 13, 1, 0, TimeSpan.Zero),
            Outcome = new MauiFlowRunOutcome
            {
                Status = MauiFlowRunOutcomes.Orphaned,
                Terminal = true,
                Verified = false,
            },
            Failure = new MauiFlowFailure
            {
                FailureId = "failure-run-5",
                Class = MauiFlowFailureClasses.AppCrash,
                Code = MauiFlowFailureClasses.AppCrash,
            },
        };
        report.ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["exitCategory"] = JsonSerializer.SerializeToElement("unknown-completion"),
        };
        var manifest = new MauiTestExecutionManifest
        {
            Outcome = new MauiTestExecutionOutcomeFacts
            {
                Status = MauiFlowRunOutcomes.Orphaned,
                ExitCategory = "unknown-completion",
                Terminal = true,
                Verified = false,
                UnknownCompletion = true,
            },
        };

        var triage = MauiFlowTriageAnalyzer.Analyze(new MauiFlowTriageInput
        {
            Report = report,
            Manifest = manifest,
        });

        Assert.DoesNotContain("exit-category-match", triage.Evidence.MissingFacts);
    }

    /// <summary>
    /// A run that timed out never established what the app was doing when the clock ran out, so a
    /// crash record cannot convert that uncertainty into an accusation. This is the same fail-closed
    /// rule that protects <c>cancelled</c> and <c>unknown-completion</c>.
    /// </summary>
    [Fact]
    public void Analyze_TimedOutRunWithACrashRecord_StaysInconclusive()
    {
        var report = new MauiFlowRunReport
        {
            RunId = "run-timeout",
            Outcome = new MauiFlowRunOutcome
            {
                Status = MauiFlowRunOutcomes.TimedOut,
                Terminal = true,
                Verified = false,
            },
            Failure = new MauiFlowFailure
            {
                FailureId = "failure-run-timeout",
                Class = MauiFlowFailureClasses.Timeout,
                Code = MauiFlowFailureClasses.Timeout,
            },
            AppProcess = new MauiFlowAppProcessEvidence
            {
                Probed = true,
                ProcessExited = true,
                ExitReason = MauiFlowAppExitReasons.Crash,
                CrashLogPresent = true,
            },
        };

        var triage = MauiFlowTriageAnalyzer.Analyze(new MauiFlowTriageInput { Report = report });

        Assert.Equal(MauiFlowFailureClasses.Timeout, triage.Classification.FailureClass);
        Assert.Equal(MauiFlowTriageDispositions.Inconclusive, triage.Classification.Disposition);
    }

    /// <summary>
    /// The report contract owns one crash rule. If the convenience helper on the evidence type ever
    /// drifted from the classifier, one of them would start claiming a crash the other rejects.
    /// </summary>
    [Theory]
    [InlineData(null, null, null, false)]
    [InlineData(true, null, null, false)]
    [InlineData(true, MauiFlowAppExitReasons.UserRequested, null, false)]
    [InlineData(true, MauiFlowAppExitReasons.Signaled, null, false)]
    [InlineData(false, MauiFlowAppExitReasons.Crash, true, false)]
    [InlineData(true, MauiFlowAppExitReasons.Crash, null, true)]
    [InlineData(true, MauiFlowAppExitReasons.Anr, null, true)]
    [InlineData(true, null, true, true)]
    public void ProvesAbnormalExit_AgreesWithTheClassifierRule(
        bool? processExited,
        string? exitReason,
        bool? crashLogPresent,
        bool expected)
    {
        var evidence = new MauiFlowAppProcessEvidence
        {
            Probed = true,
            ProcessExited = processExited,
            ExitReason = exitReason,
            CrashLogPresent = crashLogPresent,
        };
        var facts = new MauiFlowFailureFacts
        {
            AppProcessExited = processExited,
            AppExitReason = exitReason,
            CrashLogPresent = crashLogPresent,
        };

        Assert.Equal(expected, evidence.ProvesAbnormalExit());
        Assert.Equal(expected, MauiFlowFailureClassifier.ProvesAppCrash(facts));
    }

    /// <summary>
    /// Android crash excerpts carry device-side absolute paths, which the "no absolute paths" rule
    /// covers just as much as host paths.
    /// </summary>
    [Theory]
    [InlineData("/data/user/0/com.contoso.app/files/state.db")]
    [InlineData("/data/app/~~abc==/com.contoso.app-1/base.apk")]
    [InlineData("/storage/emulated/0/Download/report.json")]
    public void SanitizeAppProcess_AndroidDevicePathsInACrashExcerpt_AreRedacted(string devicePath)
    {
        var report = NewReport();
        report.AppProcess = new MauiFlowAppProcessEvidence
        {
            Probed = true,
            ProcessExited = true,
            ExitReason = MauiFlowAppExitReasons.Crash,
            CrashLogPresent = true,
            CrashExcerpt = ["java.io.FileNotFoundException: " + devicePath + " (No such file)"],
        };

        MauiFlowRunReportSerializer.ApplyLimits(report, new MauiFlowRunReportLimits());

        var excerpt = Assert.Single(report.AppProcess!.CrashExcerpt!);
        Assert.DoesNotContain(devicePath, excerpt, StringComparison.Ordinal);
        Assert.Contains("<path>", excerpt, StringComparison.Ordinal);
    }

    private static MauiFlowRunReport NewReport() => new()
    {
        RunId = "run-redaction",
        Outcome = new MauiFlowRunOutcome
        {
            Status = MauiFlowRunOutcomes.InfrastructureError,
            Terminal = true,
            Verified = false,
        },
    };
}
