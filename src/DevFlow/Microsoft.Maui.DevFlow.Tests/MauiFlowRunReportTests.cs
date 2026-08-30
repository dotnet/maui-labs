using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class MauiFlowRunReportTests : IDisposable
{
    private readonly string _root = Path.Combine(
        AppContext.BaseDirectory,
        "flow-run-report-tests",
        Guid.NewGuid().ToString("N"));

    public MauiFlowRunReportTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void BuildFingerprint_UnknownPlaceholdersDoNotCreateFalsePreconditions()
    {
        Assert.Null(MauiFlowRunner.BuildFingerprint(new AgentStatus
        {
            App = new AppDescriptor { Version = "unknown", Build = "unknown" },
        }));
        Assert.Equal("unknown:42", MauiFlowRunner.BuildFingerprint(new AgentStatus
        {
            App = new AppDescriptor { Build = "42" },
        }));
    }

    [Fact]
    public void Validate_PassedExecutionMayRemainIndependentlyUnverified()
    {
        var report = new MauiFlowRunReport
        {
            RunId = "run-unverified",
            FlowDigest = new string('a', 64),
            SideEffectPolicy = MauiFlowSideEffectPolicies.None,
            StartedAt = DateTimeOffset.UnixEpoch,
            EndedAt = DateTimeOffset.UnixEpoch,
            Outcome = new MauiFlowRunOutcome
            {
                Status = MauiFlowRunOutcomes.Passed,
                Terminal = true,
                Verified = false,
            },
            Verification = new MauiFlowRunVerification { Verified = false },
            ReplayEligibility = new MauiFlowReplayEligibilityDecision
            {
                SideEffectPolicy = MauiFlowSideEffectPolicies.None,
                RunVerificationAllowed = false,
            },
        };

        var validation = MauiFlowRunReportSerializer.Validate(report);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
    }

    [Fact]
    public void Validate_VerifiedStatusMustMatchPassedExecutionAndCompatibilityMirror()
    {
        var report = new MauiFlowRunReport
        {
            RunId = "run-invalid-verification",
            FlowDigest = new string('a', 64),
            SideEffectPolicy = MauiFlowSideEffectPolicies.None,
            StartedAt = DateTimeOffset.UnixEpoch,
            EndedAt = DateTimeOffset.UnixEpoch,
            Outcome = new MauiFlowRunOutcome
            {
                Status = MauiFlowRunOutcomes.Failed,
                Terminal = true,
                Verified = false,
            },
            Verification = new MauiFlowRunVerification { Verified = true },
            Failure = new MauiFlowFailure
            {
                Class = MauiFlowFailureClasses.AssertionFailed,
                Code = MauiFlowFailureClasses.AssertionFailed,
                Retryable = false,
            },
            ReplayEligibility = new MauiFlowReplayEligibilityDecision
            {
                SideEffectPolicy = MauiFlowSideEffectPolicies.None,
                RunVerificationAllowed = true,
            },
        };

        var validation = MauiFlowRunReportSerializer.Validate(report);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains("passed execution", StringComparison.Ordinal));
        Assert.Contains(validation.Errors, error => error.Contains("must match", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_PreflightFailureWithoutFlowDigest_IsSchemaValid()
    {
        var report = new MauiFlowRunReport
        {
            RunId = "run-preflight",
            StartedAt = DateTimeOffset.UnixEpoch,
            EndedAt = DateTimeOffset.UnixEpoch.AddSeconds(1),
            Outcome = new MauiFlowRunOutcome
            {
                Status = MauiFlowRunOutcomes.Failed,
                Terminal = true,
                Verified = false,
            },
            Failure = new MauiFlowFailure
            {
                Class = MauiFlowFailureClasses.FlowInvalid,
                Code = "project-path-missing",
                Category = "flow",
                Phase = "validation",
                Retryable = false,
            },
        };

        var validation = MauiFlowRunReportSerializer.Validate(report);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
    }

    [Fact]
    public async Task RunAsync_Mutation_RecordsOrderedActionabilityAndWorkflowReceipt()
    {
        var driver = new FakeDriver { EmitReceipt = true };
        var report = await new MauiFlowRunner(
            driver,
            new MauiFlowRunnerOptions
            {
                RunId = "run-report",
                PollTries = 1,
                PollGapMs = 0,
            }).RunAsync(TapFlow());

        Assert.Equal(MauiFlowRunOutcomes.Passed, report.Outcome!.Status);
        Assert.Equal("run-report", report.RunId);
        Assert.Equal("agent-instance", report.Target!.AgentInstanceId);
        Assert.NotNull(report.FlowDigest);
        Assert.True(MauiFlowRunReportSerializer.Validate(report).IsValid);

        var step = Assert.Single(report.Steps);
        Assert.Equal("1", step.StepId);
        Assert.Equal(1, step.CandidateCount);
        Assert.Equal("resolved", step.TargetResolution!.Status);
        Assert.NotEmpty(step.Actionability);
        Assert.Equal(
            Enumerable.Range(1, step.Actionability.Count),
            step.Actionability.Select(attempt => attempt.Sequence!.Value).ToArray());
        Assert.All(step.Actionability, attempt => Assert.NotNull(attempt.WaitDurationMs));
        Assert.NotNull(step.Dispatch);
        Assert.Equal(7, step.Dispatch!.Sequence);
        Assert.Equal("cmd_1", step.Dispatch.CommandId);
        Assert.Equal("completed", step.Dispatch.CompletionCertainty);
        Assert.Equal("completed", step.CompletionCertainty);
    }

    [Fact]
    public async Task RunAsync_StableStepId_PreservesIdentityAndLegacySequence()
    {
        var flow = TapFlow();
        flow.Steps[0].StepId = "submit-order";

        var result = await new MauiFlowRunner(
            new FakeDriver(),
            new MauiFlowRunnerOptions { PollTries = 1, PollGapMs = 0 })
            .RunWithLegacyAsync(flow);

        Assert.Equal("submit-order", Assert.Single(result.Report.Steps).StepId);
        Assert.Equal(1, Assert.Single(result.LegacyReport.Results).Seq);
    }

    [Fact]
    public async Task RunWithLegacyAsync_SensitiveAssertion_RedactsStructuredValuesAndPreservesLegacyShape()
    {
        const string secret = "CorrectHorseBatteryStaple";
        var driver = new FakeDriver { PropertyValue = secret };
        var flow = new MauiFlow
        {
            Name = "assert-password",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Assert,
                    Asserts =
                    [
                        new FlowAssert
                        {
                            Kind = "propEquals",
                            Verify = true,
                            Name = "Password",
                            Expected = secret,
                            Selector = new FlowSelector { AutomationId = "submit" },
                        },
                    ],
                },
            },
        };

        var result = await new MauiFlowRunner(driver, new MauiFlowRunnerOptions
        {
            PollTries = 1,
            PollGapMs = 0,
        }).RunWithLegacyAsync(flow);

        var assertion = Assert.Single(Assert.Single(result.Report.Steps).Assertions);
        Assert.True(assertion.Passed);
        Assert.Null(assertion.Expected);
        Assert.Null(assertion.Actual);
        Assert.Equal("redacted", assertion.ExpectedDisclosure!.State);
        Assert.Equal("redacted", assertion.ActualDisclosure!.State);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(result.Report, MauiTestingJsonContext.Default.MauiFlowRunReport));
        Assert.Equal(secret, result.LegacyReport.Results[0].Asserts[0].Actual);
        Assert.NotNull(result.LegacyReport.StructuredReport);
    }

    [Fact]
    public async Task RunWithLegacyAsync_FailedAssertionOnOrdinaryText_DisclosesActualToTheAuthor()
    {
        // Regression: a failed assertion used to emit the expected value in the clear while
        // redacting the observed one to {state:redacted,length:N,digest:...}. The tree carries no
        // text, so the only copy of the value the author needed was inside an opt-in screenshot.
        const string observed = "4 items, 0 completed";
        var driver = new FakeDriver { PropertyValue = observed };
        var flow = AssertTextFlow("Text", expected: "5 items, 0 completed");

        var result = await new MauiFlowRunner(driver, new MauiFlowRunnerOptions
        {
            PollTries = 1,
            PollGapMs = 0,
        }).RunWithLegacyAsync(flow);

        var assertion = Assert.Single(Assert.Single(result.Report.Steps).Assertions);
        Assert.False(assertion.Passed);
        Assert.Equal(observed, assertion.Actual);
        Assert.Equal("disclosed", assertion.ActualDisclosure!.State);
        Assert.Equal("5 items, 0 completed", assertion.Expected);
    }

    [Fact]
    public async Task RunWithLegacyAsync_PassingAssertionOnOrdinaryText_StillRedactsActual()
    {
        // The disclosure is scoped to failures. A passing assertion tells the author nothing they
        // did not already write down, so there is no reason to copy app state into the report.
        const string observed = "4 items, 0 completed";
        var driver = new FakeDriver { PropertyValue = observed };
        var flow = AssertTextFlow("Text", expected: observed);

        var result = await new MauiFlowRunner(driver, new MauiFlowRunnerOptions
        {
            PollTries = 1,
            PollGapMs = 0,
        }).RunWithLegacyAsync(flow);

        var assertion = Assert.Single(Assert.Single(result.Report.Steps).Assertions);
        Assert.True(assertion.Passed);
        Assert.Null(assertion.Actual);
        Assert.Equal("redacted", assertion.ActualDisclosure!.State);
    }

    [Fact]
    public async Task RunWithLegacyAsync_FailedAssertionOnSensitiveName_StillRedactsActual()
    {
        const string observed = "hunter2-not-the-expected-one";
        var driver = new FakeDriver { PropertyValue = observed };
        var flow = AssertTextFlow("Password", expected: "something-else");

        var result = await new MauiFlowRunner(driver, new MauiFlowRunnerOptions
        {
            PollTries = 1,
            PollGapMs = 0,
        }).RunWithLegacyAsync(flow);

        var assertion = Assert.Single(Assert.Single(result.Report.Steps).Assertions);
        Assert.False(assertion.Passed);
        Assert.Null(assertion.Actual);
        Assert.Equal("redacted", assertion.ActualDisclosure!.State);
        Assert.DoesNotContain(
            observed,
            JsonSerializer.Serialize(result.Report, MauiTestingJsonContext.Default.MauiFlowRunReport),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunWithLegacyAsync_FailedAssertionOnSensitiveSelector_StillRedactsActual()
    {
        // The assert name alone is not enough: `Text` on a PasswordEntry is still a password. The
        // selector the author aimed at is carried into the sensitivity decision.
        const string observed = "hunter2-not-the-expected-one";
        var driver = new FakeDriver { PropertyValue = observed, ElementAutomationId = "PasswordEntry" };
        var flow = AssertTextFlow("Text", expected: "something-else", automationId: "PasswordEntry");

        var result = await new MauiFlowRunner(driver, new MauiFlowRunnerOptions
        {
            PollTries = 1,
            PollGapMs = 0,
        }).RunWithLegacyAsync(flow);

        var assertion = Assert.Single(Assert.Single(result.Report.Steps).Assertions);
        Assert.False(assertion.Passed);
        Assert.Null(assertion.Actual);
        Assert.Equal("redacted", assertion.ActualDisclosure!.State);
    }

    [Theory]
    // An address, an absolute path, a bearer token and an opaque blob all stay redacted because
    // the disclosure predicate uses the existing redaction pipeline as its own oracle: a value is
    // disclosed only when redacting it is a byte-for-byte no-op.
    [InlineData("someone@contoso.com")]
    [InlineData(@"C:\Users\alice\Documents\secret-plan.txt")]
    [InlineData("Bearer abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("ZmFrZS1vcGFxdWUtYmxvYi12YWx1ZS1mb3ItdGVzdGluZy0xMjM0NTY3ODkw")]
    public async Task RunWithLegacyAsync_FailedAssertionOnUnsafeText_StillRedactsActual(string observed)
    {
        var driver = new FakeDriver { PropertyValue = observed };
        var flow = AssertTextFlow("Text", expected: "something-else");

        var result = await new MauiFlowRunner(driver, new MauiFlowRunnerOptions
        {
            PollTries = 1,
            PollGapMs = 0,
        }).RunWithLegacyAsync(flow);

        var assertion = Assert.Single(Assert.Single(result.Report.Steps).Assertions);
        Assert.False(assertion.Passed);
        Assert.Null(assertion.Actual);
        Assert.Equal("redacted", assertion.ActualDisclosure!.State);
        Assert.DoesNotContain(
            observed,
            JsonSerializer.Serialize(result.Report, MauiTestingJsonContext.Default.MauiFlowRunReport),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunWithLegacyAsync_FailedAssertionOnOverlongText_StillRedactsActual()
    {
        var observed = new string('x', 600);
        var driver = new FakeDriver { PropertyValue = observed };
        var flow = AssertTextFlow("Text", expected: "something-else");

        var result = await new MauiFlowRunner(driver, new MauiFlowRunnerOptions
        {
            PollTries = 1,
            PollGapMs = 0,
        }).RunWithLegacyAsync(flow);

        var assertion = Assert.Single(Assert.Single(result.Report.Steps).Assertions);
        Assert.False(assertion.Passed);
        Assert.Null(assertion.Actual);
        Assert.Equal("redacted", assertion.ActualDisclosure!.State);
    }

    [Fact]
    public async Task ApplyLimits_FailedAssertionDisclosure_SurvivesReimport()
    {
        // ApplyLimits re-normalizes every disclosure on import, so a producer-side-only fix would
        // be silently undone by any consumer that round-trips the report.
        const string observed = "4 items, 0 completed";
        var driver = new FakeDriver { PropertyValue = observed };
        var flow = AssertTextFlow("Text", expected: "5 items, 0 completed");

        var result = await new MauiFlowRunner(driver, new MauiFlowRunnerOptions
        {
            PollTries = 1,
            PollGapMs = 0,
        }).RunWithLegacyAsync(flow);

        var json = JsonSerializer.Serialize(result.Report, MauiTestingJsonContext.Default.MauiFlowRunReport);
        var reimported = JsonSerializer.Deserialize(json, MauiTestingJsonContext.Default.MauiFlowRunReport);
        MauiFlowRunReportSerializer.ApplyLimits(reimported!, new MauiFlowRunReportLimits());
        var assertion = Assert.Single(Assert.Single(reimported!.Steps).Assertions);

        Assert.Equal(observed, assertion.Actual);
        Assert.Equal("disclosed", assertion.ActualDisclosure!.State);
    }

    /// <summary>
    /// The import path cannot see what the producer saw: <see cref="MauiFlowAssertionResult"/>
    /// carries no assertion name and no selector, so it cannot tell that the value came from a
    /// <c>PasswordEntry</c>. A producer that redacted therefore made the better-informed decision,
    /// and import must never widen it — not even for a failed assertion, and not even when the raw
    /// value happens to still be present alongside the redacted disclosure.
    /// </summary>
    [Theory]
    [InlineData("redacted")]
    [InlineData("omitted")]
    public void ApplyLimits_ProducerWithheldValue_IsNotReopenedOnImport(string producerState)
    {
        var report = new MauiFlowRunReport
        {
            Steps =
            {
                new MauiFlowStepAttempt
                {
                    Assertions =
                    {
                        new MauiFlowAssertionResult
                        {
                            Kind = "propEquals",
                            Passed = false,
                            Skipped = false,
                            Actual = "correct horse battery staple",
                            ActualDisclosure = new MauiFlowValueDisclosure
                            {
                                State = producerState,
                                Type = "string",
                                Length = 27,
                                Digest = "sha256:" + new string('a', 64),
                            },
                        },
                    },
                },
            },
        };

        MauiFlowRunReportSerializer.ApplyLimits(report, new MauiFlowRunReportLimits());
        var assertion = Assert.Single(Assert.Single(report.Steps).Assertions);

        Assert.Equal(producerState, assertion.ActualDisclosure!.State);
        Assert.Null(assertion.ActualDisclosure.Value);
        Assert.Null(assertion.Actual);
        Assert.Equal(27, assertion.ActualDisclosure.Length);
    }

    /// <summary>
    /// A selector naming both a benign AutomationId and a sensitive type must still be screened as
    /// sensitive; taking only the first non-null selector field let the type go untested.
    /// </summary>
    [Fact]
    public async Task FailedAssertion_OnASelectorWhoseTypeIsSensitive_IsStillRedacted()
    {
        var driver = new FakeDriver { PropertyValue = "hunter2", ElementAutomationId = "LoginField" };
        var flow = AssertTextFlow("Text", expected: "other", automationId: "LoginField");
        flow.Steps[0].Asserts[0].Selector!.Type = "PasswordEntry";

        var result = await new MauiFlowRunner(driver, new MauiFlowRunnerOptions
        {
            PollTries = 1,
            PollGapMs = 0,
        }).RunWithLegacyAsync(flow);

        var assertion = Assert.Single(Assert.Single(result.Report.Steps).Assertions);
        Assert.False(assertion.Passed);
        Assert.Equal("redacted", assertion.ActualDisclosure!.State);
        Assert.Null(assertion.ActualDisclosure.Value);
        Assert.Null(assertion.Actual);
    }

    private static MauiFlow AssertTextFlow(string name, string expected, string automationId = "submit")
        => new()
        {
            Name = "assert-text",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Assert,
                    Asserts =
                    [
                        new FlowAssert
                        {
                            Kind = "propEquals",
                            Verify = true,
                            Name = name,
                            Expected = expected,
                            Selector = new FlowSelector { AutomationId = automationId },
                        },
                    ],
                },
            },
        };

    [Fact]
    public async Task RunAsync_BoundedEventsAndActionability_RecordsExplicitTruncation()
    {
        var report = await new MauiFlowRunner(
            new FakeDriver(),
            new MauiFlowRunnerOptions
            {
                PollTries = 3,
                PollGapMs = 0,
                ReportLimits = new MauiFlowRunReportLimits
                {
                    MaxEvents = 1,
                    MaxActionabilityAttemptsPerStep = 1,
                    MaxAssertionsPerStep = 1,
                    MaxArtifacts = 1,
                    MaxJsonBytes = 16 * 1024,
                },
            }).RunAsync(TapFlow());

        Assert.True(report.Truncated);
        Assert.Single(report.Events);
        Assert.Single(report.Steps[0].Actionability);
        Assert.Contains(report.Omissions, omission => omission.Kind is "events" or "actionability");
    }

    [Fact]
    public void UnexpectedInfrastructureFailure_UsesFixedPublicSafeMessage()
    {
        Assert.Equal(
            "The flow runner encountered an infrastructure error.",
            MauiFlowRunner.InfrastructureErrorMessage);
    }

    [Fact]
    public async Task RunAsync_StepObservationDelay_PausesOnlyBetweenSteps()
    {
        var flow = TapFlow();
        flow.Steps.Add(new FlowStep
        {
            Seq = 2,
            Action = FlowActions.Tap,
            Target = new FlowSelector { AutomationId = "submit" },
        });
        var stopwatch = Stopwatch.StartNew();

        var report = await new MauiFlowRunner(
            new FakeDriver(),
            new MauiFlowRunnerOptions
            {
                PollTries = 1,
                PollGapMs = 0,
                StepObservationDelayMs = 50,
            }).RunAsync(flow);

        Assert.Equal(MauiFlowRunOutcomes.Passed, report.Outcome!.Status);
        Assert.True(stopwatch.ElapsedMilliseconds >= 40, $"Observed {stopwatch.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public void ApplyLimits_OversizedStepSet_RetainsBoundedTerminalSummary()
    {
        const int maximum = 16 * 1024;
        var report = new MauiFlowRunReport
        {
            RunId = "run-size-limit",
            DivergenceStepId = "step-399",
            Outcome = new MauiFlowRunOutcome { Status = MauiFlowRunOutcomes.Failed, Terminal = true },
            Steps = Enumerable.Range(1, 400)
                .Select(index => new MauiFlowStepAttempt
                {
                    StepId = $"step-{index}",
                    Sequence = index,
                    Action = new string('a', 256),
                    Intent = new string('i', 1024),
                    Assertions =
                    [
                        new MauiFlowAssertionResult
                        {
                            Kind = "property",
                            Message = new string('m', 1024),
                        },
                    ],
                })
                .ToList(),
        };

        MauiFlowRunReportSerializer.ApplyLimits(
            report,
            new MauiFlowRunReportLimits { MaxJsonBytes = maximum });
        var bytes = MauiFlowRunReportSerializer.SerializeToUtf8Bytes(report);

        Assert.True(bytes.Length <= maximum);
        Assert.True(report.Truncated);
        Assert.Single(report.Steps);
        Assert.Equal("step-399", report.Steps[0].StepId);
    }

    [Fact]
    public void ApplyLimits_RealManagedTypeName_SurvivesAsAStableDigestSoFingerprintsStillCompare()
    {
        // The generic identifier rules treat any long mixed-case token as an opaque secret, and
        // "Microsoft.Maui.Controls.Button" is exactly that shape. Dropping it made a redacted
        // fingerprint fail to match itself, because the comparer requires both sides to be present.
        static MauiFlowRunReport Redact(string fullType)
        {
            var report = new MauiFlowRunReport
            {
                RunId = "run-managed-type",
                Steps =
                [
                    new MauiFlowStepAttempt
                    {
                        StepId = "step-1",
                        Sequence = 1,
                        Fingerprint = new MauiElementFingerprint
                        {
                            FingerprintId = "fp1_managed",
                            Context = new MauiElementFingerprintContext
                            {
                                AppId = "com.example.app",
                                Platform = "android",
                                Route = "//checkout",
                                Window = "main",
                                Modal = "none",
                                Locale = "en-US",
                                Theme = "light",
                                Orientation = "portrait",
                                DisplayProfile = "phone",
                            },
                            Managed = new MauiManagedElementIdentity
                            {
                                Type = "Button",
                                FullType = fullType,
                                Role = "button",
                                AutomationId = "save",
                            },
                            Topology = new MauiTopologySignature
                            {
                                AncestorHash = "4528a7b8b310ec23cad92096",
                                SiblingHash = "e4874adab5ee70e7fc282772",
                            },
                        },
                    },
                ],
            };
            MauiFlowRunReportSerializer.ApplyLimits(report, new MauiFlowRunReportLimits());
            return report;
        }

        var redacted = Redact("Microsoft.Maui.Controls.Button");
        var managed = redacted.Steps[0].Fingerprint!.Managed!;

        Assert.False(string.IsNullOrWhiteSpace(managed.FullType));
        Assert.DoesNotContain("Microsoft.Maui.Controls.Button", managed.FullType, StringComparison.Ordinal);
        Assert.Equal(
            managed.FullType,
            Redact("Microsoft.Maui.Controls.Button").Steps[0].Fingerprint!.Managed!.FullType);
        Assert.NotEqual(
            managed.FullType,
            Redact("Microsoft.Maui.Controls.Editor").Steps[0].Fingerprint!.Managed!.FullType);
        Assert.True(MauiRepairFingerprintComparer.SemanticallyMatches(
            redacted.Steps[0].Fingerprint,
            Redact("Microsoft.Maui.Controls.Button").Steps[0].Fingerprint));

        // Only a value that is recognisably a type name earns a digest. Anything else is still
        // dropped, so the digest never becomes an equality oracle for an arbitrary app-supplied
        // string the redactor refused to publish.
        // Only a value that is recognisably a namespace-qualified type name earns a digest, so the
        // single-token shape that API keys and access tokens take is dropped rather than committed
        // to. A digest is a commitment: it confirms a guessed value and links reports.
        Assert.Null(Redact("someone.private@contoso.example").Steps[0].Fingerprint!.Managed!.FullType);
        Assert.Null(Redact("C:\\Users\\alice\\Secrets\\AppState").Steps[0].Fingerprint!.Managed!.FullType);
        Assert.Null(Redact(new string('A', 80) + "b").Steps[0].Fingerprint!.Managed!.FullType);
        Assert.Null(Redact("sk" + "_live_" + "4eC39HqLyjWDarjtT1zdp7dc").Steps[0].Fingerprint!.Managed!.FullType);
        Assert.Null(Redact("Ab3xK9mQ2pL7wR4tY8uZ").Steps[0].Fingerprint!.Managed!.FullType);
        Assert.Null(Redact("Contoso.SessionTokenForCheckoutFlow").Steps[0].Fingerprint!.Managed!.FullType);
    }

    /// <summary>
    /// Pins a known, unresolved gap rather than asserting a desired behaviour. The opaque-secret
    /// heuristic classifies any whitespace-free mixed-case token of twenty or more alphanumeric
    /// characters as a secret, so a realistic PascalCase AutomationId is dropped from the report.
    /// A managed type name recovers as a digest because a type only needs to be <em>comparable</em>;
    /// an AutomationId cannot, because a selector must stay <em>executable</em>, which means the raw
    /// value has to survive. Repair therefore still abstains for apps whose AutomationIds are that
    /// long. Loosening the classifier would change what every report may publish, so it is a
    /// separate, deliberate decision rather than a side effect of enabling repair.
    /// </summary>
    [Fact]
    public void ApplyLimits_RealisticLongAutomationId_IsStillDroppedWhichBlocksSelectorRepair()
    {
        static MauiFlowRunReport Redact(string automationId)
        {
            var report = new MauiFlowRunReport
            {
                RunId = "run-automation-id",
                Steps =
                [
                    new MauiFlowStepAttempt
                    {
                        StepId = "step-1",
                        Sequence = 1,
                        Fingerprint = new MauiElementFingerprint
                        {
                            FingerprintId = "fp1_automation",
                            Managed = new MauiManagedElementIdentity
                            {
                                Type = "Button",
                                AutomationId = automationId,
                            },
                        },
                    },
                ],
            };
            MauiFlowRunReportSerializer.ApplyLimits(report, new MauiFlowRunReportLimits());
            return report;
        }

        Assert.Null(Redact("CheckoutSaveOrderButton").Steps[0].Fingerprint!.Managed!.AutomationId);
        // The same id in the same position survives when it is shorter, which is what makes this a
        // length artefact rather than a deliberate policy about AutomationIds.
        Assert.Equal("SaveOrder", Redact("SaveOrder").Steps[0].Fingerprint!.Managed!.AutomationId);
    }

    [Fact]
    public async Task RunAsync_UnknownCompletion_UsesTypedFailureAndLegacyMapping()
    {
        var receipt = new WorkflowCommandReceipt
        {
            RunId = "run",
            Sequence = 3,
            CommandId = "cmd_unknown",
            ActionDigest = "digest",
            AuthorityEpoch = 1,
            AcknowledgementState = "prepared",
        };
        var driver = new FakeDriver
        {
            TapFailure = new WorkflowCommandException(
                "workflow-unknown-completion",
                receipt: receipt),
        };

        var result = await new MauiFlowRunner(driver, new MauiFlowRunnerOptions
        {
            PollTries = 1,
            PollGapMs = 0,
        }).RunWithLegacyAsync(TapFlow());

        Assert.Equal(MauiFlowRunOutcomes.Failed, result.Report.Outcome!.Status);
        Assert.Equal(MauiFlowFailureClasses.UnknownCompletion, result.Report.Failure!.Code);
        Assert.Equal(FlowFailureKinds.UnknownCompletion, result.LegacyReport.Results[0].FailureKind);
        Assert.Equal("unknown", result.Report.Steps[0].Dispatch!.CompletionCertainty);
    }

    [Fact]
    public void FailureClassifier_OnlyMarksVerifiedPreDispatchLocatorFailureRepairEligible()
    {
        var eligible = MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
        {
            LegacyFailureKind = FlowFailureKinds.NotFound,
            BeforeDispatch = true,
            CheckpointVerified = true,
            CheckpointMatches = true,
            RouteMatches = true,
        });
        var routeDrift = MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
        {
            LegacyFailureKind = FlowFailureKinds.NotFound,
            BeforeDispatch = true,
            CheckpointVerified = true,
            CheckpointMatches = false,
            RouteMatches = false,
        });

        Assert.Equal(MauiFlowFailureClasses.LocatorNotFound, eligible.Code);
        Assert.True(eligible.RepairEligible);
        Assert.Equal(MauiFlowFailureClasses.RouteStateDrift, routeDrift.Code);
        Assert.False(routeDrift.RepairEligible);
    }

    [Fact]
    public void WriteAtomic_WritesBoundedReportAndArtifactReference()
    {
        var report = new MauiFlowRunReport
        {
            RunId = "run-write",
            FlowDigest = new string('a', 64),
            StartedAt = DateTimeOffset.UtcNow,
            EndedAt = DateTimeOffset.UtcNow,
            Outcome = new MauiFlowRunOutcome
            {
                Status = MauiFlowRunOutcomes.Passed,
                Terminal = true,
            },
        };

        var write = MauiFlowRunReportSerializer.WriteAtomic(report, _root);

        Assert.True(write.Ok, write.Error);
        Assert.NotNull(write.Path);
        Assert.True(File.Exists(write.Path));
        Assert.NotNull(write.Digest);
        Assert.Contains(report.Artifacts, artifact => artifact.Kind == "flow-run-report");
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(write.Path!)!, "*.tmp"));
        var persisted = JsonSerializer.Deserialize(
            File.ReadAllText(write.Path!),
            MauiTestingJsonContext.Default.MauiFlowRunReport);
        Assert.NotNull(persisted);
        Assert.Equal("run-write", persisted!.RunId);
        Assert.Equal(write.Digest, persisted.ReportDigest);
    }

    [Fact]
    public void LegacyAdapter_MapsTypedLocatorFailureWithoutChangingLegacyKind()
    {
        var report = new MauiFlowRunReport
        {
            RunId = "run-adapter",
            FlowDigest = new string('b', 64),
            Outcome = new MauiFlowRunOutcome { Status = MauiFlowRunOutcomes.Failed, Terminal = true },
            DivergenceStepId = "4",
            Steps =
            [
                new MauiFlowStepAttempt
                {
                    StepId = "4",
                    Sequence = 4,
                    Action = FlowActions.Tap,
                    FailureClass = MauiFlowFailureClasses.LocatorNotFound,
                },
            ],
        };

        var legacy = FlowReplayReportAdapter.ToLegacy(report, "legacy");

        Assert.False(legacy.Ok);
        Assert.Equal(4, legacy.DivergencePoint);
        Assert.Equal(FlowFailureKinds.NotFound, legacy.Results[0].FailureKind);
        Assert.Same(report, legacy.StructuredReport);
    }

    [Fact]
    public void LegacyAdapter_StableStepId_UsesRecordedSequenceFallback()
    {
        var report = new MauiFlowRunReport
        {
            Outcome = new MauiFlowRunOutcome { Status = MauiFlowRunOutcomes.Failed, Terminal = true },
            DivergenceStepId = "save-order",
            Steps =
            [
                new MauiFlowStepAttempt
                {
                    StepId = "save-order",
                    Sequence = 4,
                    Action = FlowActions.Tap,
                    FailureClass = MauiFlowFailureClasses.LocatorNotFound,
                },
            ],
        };

        var legacy = FlowReplayReportAdapter.ToLegacy(report);

        Assert.Equal(4, legacy.DivergencePoint);
        Assert.Equal(4, Assert.Single(legacy.Results).Seq);
    }

    [Fact]
    public void LegacyAdapter_PreStepFailure_ExplainsWhyNoStepsRan()
    {
        var report = new MauiFlowRunReport
        {
            RunId = "run-admission-failure",
            FlowDigest = new string('d', 64),
            Outcome = new MauiFlowRunOutcome
            {
                Status = MauiFlowRunOutcomes.Failed,
                Summary = "The target agent is already held by another mutation lease.",
                Terminal = true,
            },
            Failure = new MauiFlowFailure
            {
                Class = MauiFlowFailureClasses.LeaseConflict,
                Message = "The target agent is already held by another mutation lease.",
            },
        };

        var legacy = FlowReplayReportAdapter.ToLegacy(report, "legacy");

        Assert.False(legacy.Ok);
        Assert.Equal(1, legacy.Total);
        Assert.Equal(1, legacy.Failed);
        var result = Assert.Single(legacy.Results);
        Assert.Equal("run", result.Action);
        Assert.Equal("Prepare run", result.Label);
        Assert.Equal(FlowFailureKinds.Drive, result.FailureKind);
        Assert.Contains("mutation lease", result.Error);
        Assert.Same(report, legacy.StructuredReport);
    }

    [Fact]
    public void SerializeToUtf8Bytes_HostileSensitiveValues_AreDescriptorsOrOmitted()
    {
        const string secret = "CorrectHorseBatteryStaple";
        var report = new MauiFlowRunReport
        {
            RunId = "run-sensitive",
            FlowDigest = new string('c', 64),
            StartedAt = DateTimeOffset.UtcNow,
            EndedAt = DateTimeOffset.UtcNow,
            Outcome = new MauiFlowRunOutcome { Status = MauiFlowRunOutcomes.Failed, Terminal = true },
            Events =
            [
                new MauiFlowRunEvent
                {
                    Kind = "failure",
                    Message = secret,
                    Data = JsonSerializer.SerializeToElement(new { secret }),
                },
            ],
            Steps =
            [
                new MauiFlowStepAttempt
                {
                    StepId = "1",
                    Selector = new FlowSelector { Text = secret },
                    Assertions =
                    [
                        new MauiFlowAssertionResult
                        {
                            Kind = "propEquals",
                            Expected = secret,
                            Actual = secret,
                        },
                    ],
                },
            ],
        };

        var json = System.Text.Encoding.UTF8.GetString(MauiFlowRunReportSerializer.SerializeToUtf8Bytes(report));

        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.Contains("redacted", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"data\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeToUtf8Bytes_StripsExtensionsAbsolutePathsAndPersonalMessages()
    {
        const string email = "person@example.test";
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.signature123";
        var report = new MauiFlowRunReport
        {
            RunId = "run-redaction",
            FlowDigest = new string('a', 64),
            StartedAt = DateTimeOffset.UnixEpoch,
            EndedAt = DateTimeOffset.UnixEpoch.AddSeconds(1),
            ReportPath = @"C:\Users\person\flow-run.json",
            Outcome = new MauiFlowRunOutcome
            {
                Status = MauiFlowRunOutcomes.Failed,
                Terminal = true,
                Summary = $"Contact {email}; token={jwt}; path=C:\\Users\\person\\secret.txt",
                ExtensionData = new Dictionary<string, JsonElement>
                {
                    ["prompt"] = JsonSerializer.SerializeToElement("untrusted"),
                },
            },
            Failure = new MauiFlowFailure
            {
                Class = MauiFlowFailureClasses.AssertionFailed,
                Code = MauiFlowFailureClasses.AssertionFailed,
                Retryable = false,
            },
            Artifacts =
            [
                new MauiFlowArtifactReference
                {
                    Kind = "raw-log",
                    Path = @"C:\Users\person\raw.log",
                },
            ],
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["x-report"] = JsonSerializer.SerializeToElement(true),
            },
        };

        var json = Encoding.UTF8.GetString(MauiFlowRunReportSerializer.SerializeToUtf8Bytes(report));

        Assert.DoesNotContain(email, json, StringComparison.Ordinal);
        Assert.DoesNotContain(jwt, json, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"prompt\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"x-report\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"reportPath\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"path\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunWithLegacyAsync_DetailedEvidence_BindsEvidenceArtifactToFinalReport()
    {
        var evidence = new DetailedEvidenceCapture();
        var result = await new MauiFlowRunner(
            new FakeDriver { ElementExists = false },
            new MauiFlowRunnerOptions { PollTries = 1, PollGapMs = 0 },
            evidence).RunWithLegacyAsync(TapFlow());

        Assert.NotNull(evidence.Context);
        Assert.Equal(result.Report.RunId, evidence.Context!.Report.RunId);
        Assert.Equal(MauiFlowFailureClasses.LocatorNotFound, evidence.Context.Report.Failure!.Code);
        Assert.NotNull(evidence.Context.ReportDigest);
        Assert.Contains(result.Report.Artifacts, artifact =>
            artifact.Kind == "mauitrace" &&
            artifact.Digest == "sha256:evidence");
    }

    private static MauiFlow TapFlow() => new()
    {
        Name = "tap",
        Steps =
        [
            new FlowStep
            {
                Seq = 1,
                Action = FlowActions.Tap,
                Args = new FlowStepArgs
                {
                    Selector = new FlowSelector { AutomationId = "submit" },
                },
            },
        ],
    };

    private sealed class FakeDriver : IMauiFlowDriver
    {
        private readonly ElementInfo _element = new()
        {
            Id = "submit-id",
            AutomationId = "submit",
            Type = "Button",
            IsVisible = true,
            IsEnabled = true,
            Bounds = new BoundsInfo { X = 1, Y = 2, Width = 100, Height = 40 },
        };

        public bool EmitReceipt { get; set; }
        public bool ElementExists { get; set; } = true;
        public Exception? TapFailure { get; set; }
        public string? PropertyValue { get; set; } = "value";
        public WorkflowCommandReceipt? LastWorkflowCommandReceipt { get; private set; }

        public string ElementAutomationId
        {
            get => _element.AutomationId!;
            set => _element.AutomationId = value;
        }

        public Task<List<ElementInfo>> QueryAsync(string? type = null, string? automationId = null, string? text = null)
            => Task.FromResult(
                ElementExists &&
                (automationId is null || string.Equals(automationId, _element.AutomationId, StringComparison.Ordinal))
                    ? new List<ElementInfo> { _element }
                    : new List<ElementInfo>());

        public Task<ElementInfo?> GetElementAsync(string id)
            => Task.FromResult<ElementInfo?>(
                ElementExists && string.Equals(id, _element.Id, StringComparison.Ordinal) ? _element : null);

        public Task<bool> TapAsync(string elementId)
        {
            if (TapFailure is not null)
                throw TapFailure;
            if (EmitReceipt)
            {
                LastWorkflowCommandReceipt = new WorkflowCommandReceipt
                {
                    RunId = "run-report",
                    Sequence = 7,
                    CommandId = "cmd_1",
                    ActionDigest = "digest_1",
                    AuthorityEpoch = 2,
                    AcknowledgementState = "acknowledged",
                };
            }
            return Task.FromResult(true);
        }

        public Task<bool> FillAsync(string elementId, string text) => Task.FromResult(true);
        public Task<bool> SetPropertyAsync(string elementId, string propertyName, string value) => Task.FromResult(true);
        public Task<bool> ScrollAsync(string? elementId = null, double deltaX = 0, double deltaY = 0, bool animated = true, int? itemIndex = null, string? scrollToPosition = null) => Task.FromResult(true);
        public Task<bool> NavigateAsync(string route) => Task.FromResult(true);
        public Task<bool> BackAsync() => Task.FromResult(true);
        public Task<ThemeResult> SetThemeAsync(DevFlowTheme theme) => Task.FromResult(new ThemeResult { Success = true });
        public Task<string?> GetPropertyAsync(string elementId, string propertyName) => Task.FromResult(PropertyValue);

        public Task<AgentStatus?> GetStatusAsync() => Task.FromResult<AgentStatus?>(new AgentStatus
        {
            Route = "/home",
            Agent = new AgentDescriptor { InstanceId = "agent-instance" },
            Device = new DeviceDescriptor { Platform = "windows", WindowWidth = 800, WindowHeight = 600 },
            App = new AppDescriptor { PackageId = "com.example.app", Version = "1.0", Build = "42" },
        });
    }

    private sealed class DetailedEvidenceCapture : IFlowRunEvidenceCapture
    {
        public MauiFlowRunEvidenceContext? Context { get; private set; }
        public MauiFlowArtifactReference? CapturedArtifact { get; private set; }

        public Task CaptureOnFailureAsync(
            MauiFlow flow,
            FlowStep failedStep,
            FlowStepResult result,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task CaptureOnRunFailureAsync(
            MauiFlowRunEvidenceContext context,
            CancellationToken cancellationToken)
        {
            Context = context;
            CapturedArtifact = new MauiFlowArtifactReference
            {
                ArtifactId = "evidence",
                Kind = "mauitrace",
                Digest = "sha256:evidence",
                Redacted = true,
            };
            return Task.CompletedTask;
        }
    }
}
