using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Flows;
using Microsoft.Maui.Cli.DevFlow.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class Goal4FoundationTests
{
    internal static MauiFlow Flow() => new()
    {
        Steps = [new() { Seq = 1, Action = FlowActions.Assert,
            Asserts = [new() { Kind = "exists", Verify = true, Selector = new() { AutomationId = "result" } }] }]
    };

    internal static PreviewFlowPlan Plan() => PreviewFlowPlan.Create("plan", 1,
        new("agent", "instance", "build", "seed", "checkpoint", "none"), Flow());

    [Fact]
    public void Plan_SnapshotsNestedInputAndReturnedCopies()
    {
        var flow = Flow();
        var plan = PreviewFlowPlan.Create("plan", 1, Plan().Binding, flow);
        flow.Steps[0].Asserts![0].Selector!.AutomationId = "changed";
        plan.CopyFlow().Steps.Clear();
        Assert.Equal("result", plan.CopyFlow().Steps[0].Asserts![0].Selector!.AutomationId);
        Assert.Equal(Plan().PlanDigest, plan.PlanDigest);
    }

    [Fact]
    public void Plan_BindsRevisionAndTargetSeparatelyFromFlow()
    {
        var plan = Plan();
        var changed = PreviewFlowPlan.Create("plan", 2, plan.Binding with { AgentInstanceId = "replacement" }, Flow());
        Assert.Equal(plan.FlowDigest, changed.FlowDigest);
        Assert.NotEqual(plan.PlanDigest, changed.PlanDigest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Plan_RejectsInvalidRevision(int revision) =>
        Assert.Throws<ArgumentException>(() => PreviewFlowPlan.Create("plan", revision, Plan().Binding, Flow()));

    [Fact]
    public void Plan_RejectsMissingHardAssertion()
    {
        var flow = Flow();
        flow.Steps[0].Asserts![0].Verify = false;
        Assert.Throws<ArgumentException>(() => PreviewFlowPlan.Create("plan", 1, Plan().Binding, flow));
    }

    [Theory]
    [InlineData("non-replayable")]
    [InlineData("test-tenant-resettable")]
    [InlineData("")]
    public void Plan_RejectsUnsupportedSideEffectPolicy(string policy) =>
        Assert.Throws<ArgumentException>(() =>
            PreviewFlowPlan.Create("plan", 1, Plan().Binding with { SideEffectPolicy = policy }, Flow()));

    [Fact]
    public void Plan_RejectsFallbackAndRepeatedItemSelectors()
    {
        var flow = Flow();
        flow.Steps[0].Asserts![0].Selector!.Text = "fallback";
        Assert.Throws<ArgumentException>(() => PreviewFlowPlan.Create("plan", 1, Plan().Binding, flow));
        flow = Flow();
        flow.Steps[0].Args = new() { ItemIndex = 0 };
        Assert.Throws<ArgumentException>(() => PreviewFlowPlan.Create("plan", 1, Plan().Binding, flow));
    }

    [Fact]
    public async Task Runner_DefaultIsDisabled()
    {
        var host = new Host();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PreviewFlowRunner(host).RunAsync(Plan()));
        Assert.Equal(0, host.Dispatches);
        Assert.Equal(0, host.Grants);
    }

    [Fact]
    public async Task Runner_RequiresSeparateHostGrant()
    {
        var host = new Host { Approve = false };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new PreviewFlowRunner(host, true).RunAsync(Plan()));
        Assert.Equal(0, host.Dispatches);
    }

    [Fact]
    public async Task Runner_IsSingleAttemptAndNeverConfersRepairAuthority()
    {
        var host = new Host();
        var runner = new PreviewFlowRunner(host, true);
        var report = await runner.RunAsync(Plan());
        Assert.True(report.IndependentlyVerified);
        Assert.Equal("not-qualified", report.Qualification);
        Assert.True(report.DiagnosticOnly);
        Assert.False(report.RepairAuthority);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(Plan()));
        Assert.Equal(1, host.Dispatches);
    }

    [Fact]
    public async Task Runner_LostResponseIsUnknownCompletionWithoutRetry()
    {
        var host = new Host { Cancel = true };
        var report = await new PreviewFlowRunner(host, true).RunAsync(Plan());
        Assert.Equal(PreviewFlowOutcome.UnknownCompletion, report.Outcome);
        Assert.False(report.IndependentlyVerified);
        Assert.True(report.CleanupComplete);
        Assert.Equal(1, host.Dispatches);
    }

    [Fact]
    public async Task Runner_CleanupFailureDoesNotRewritePrimaryOutcome()
    {
        var report = await new PreviewFlowRunner(new Host { Clean = false }, true).RunAsync(Plan());
        Assert.Equal(PreviewFlowOutcome.Passed, report.Outcome);
        Assert.False(report.CleanupComplete);
    }

    [Fact]
    public async Task Runner_UiPassWithoutIndependentOracleIsUnverified()
    {
        var report = await new PreviewFlowRunner(new Host { Independent = false }, true).RunAsync(Plan());
        Assert.Equal(PreviewFlowOutcome.Passed, report.Outcome);
        Assert.False(report.IndependentlyVerified);
        Assert.Equal("oracle-not-independent", report.VerificationFailureCode);
    }

    [Fact]
    public async Task Runner_MissingAssertionReceiptCannotPass()
    {
        var host = new Host { OmitAssertion = true };
        var report = await new PreviewFlowRunner(host, true).RunAsync(Plan());
        Assert.Equal(PreviewFlowOutcome.Failed, report.Outcome);
        Assert.False(report.IndependentlyVerified);
        Assert.Equal(0, host.Verifications);
        Assert.Null(report.VerificationFailureCode);
    }

    [Fact]
    public async Task Runner_HostFailureStillAttemptsCleanupAndDoesNotLeakErrorText()
    {
        var report = await new PreviewFlowRunner(new Host { Throw = true, CleanupThrows = true }, true).RunAsync(Plan());
        Assert.Equal(PreviewFlowOutcome.UnknownCompletion, report.Outcome);
        Assert.Equal("host-execution-failed", report.FailureCode);
        Assert.Equal("cleanup-failed", report.CleanupFailureCode);
        Assert.False(report.IndependentlyVerified);
    }

    private static PreviewFlowPlan PropertyPlan()
    {
        var flow = Flow();
        flow.Steps[0].Asserts![0].Kind = "propEquals";
        flow.Steps[0].Asserts![0].Name = "Status";
        flow.Steps[0].Asserts![0].Expected = "Paid";
        return PreviewFlowPlan.Create("plan", 1, Plan().Binding, flow);
    }

    [Fact]
    public async Task Runner_ExactPropertyAndSelectorReceiptPasses()
    {
        var report = await new PreviewFlowRunner(new Host(), true).RunAsync(PropertyPlan());
        Assert.Equal(PreviewFlowOutcome.Passed, report.Outcome);
        Assert.True(report.IndependentlyVerified);
        Assert.Null(report.VerificationFailureCode);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("selector")]
    [InlineData("missing-selector")]
    [InlineData("name")]
    [InlineData("missing-name")]
    [InlineData("expected")]
    [InlineData("missing-expected")]
    [InlineData("failed")]
    [InlineData("not-evaluated")]
    [InlineData("skipped")]
    public async Task Runner_MismatchedHardReceiptFailsWithoutCallingOracle(string mismatch)
    {
        var host = new Host
        {
            ConfigureReport = report =>
            {
                var receipt = report.Results[0].Asserts[0];
                switch (mismatch)
                {
                    case "kind": receipt.Kind = "exists"; break;
                    case "selector": receipt.AutomationId = "other-result"; break;
                    case "missing-selector": receipt.AutomationId = null; break;
                    case "name": receipt.Name = "Visible"; break;
                    case "missing-name": receipt.Name = null; break;
                    case "expected": receipt.Expected = "Unpaid"; break;
                    case "missing-expected": receipt.Expected = null; break;
                    case "failed": receipt.Ok = false; break;
                    case "not-evaluated": receipt.Ok = null; break;
                    case "skipped": receipt.Skipped = true; break;
                }
            }
        };
        var evidence = await new PreviewFlowRunner(host, true).RunAsync(PropertyPlan());
        Assert.Equal(PreviewFlowOutcome.Failed, evidence.Outcome);
        Assert.Equal("flow-failed", evidence.FailureCode);
        Assert.False(evidence.IndependentlyVerified);
        Assert.True(evidence.CleanupComplete);
        Assert.Equal(0, host.Verifications);
        Assert.Equal(1, host.Cleanups);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("failed")]
    [InlineData("total")]
    [InlineData("passed")]
    [InlineData("results")]
    [InlineData("seq")]
    [InlineData("action")]
    [InlineData("step-ok")]
    [InlineData("extra-assertion")]
    public async Task Runner_InconsistentReplayReportFails(string mismatch)
    {
        var host = new Host
        {
            ConfigureReport = report =>
            {
                switch (mismatch)
                {
                    case "ok": report.Ok = false; break;
                    case "failed": report.Failed = 1; break;
                    case "total": report.Total++; break;
                    case "passed": report.Passed = 0; break;
                    case "results": report.Results.Clear(); break;
                    case "seq": report.Results[0].Seq++; break;
                    case "action": report.Results[0].Action = FlowActions.Tap; break;
                    case "step-ok": report.Results[0].Ok = false; break;
                    case "extra-assertion": report.Results[0].Asserts.Add(new()); break;
                }
            }
        };
        var evidence = await new PreviewFlowRunner(host, true).RunAsync(Plan());
        Assert.Equal(PreviewFlowOutcome.Failed, evidence.Outcome);
        Assert.Equal(0, host.Verifications);
    }

    [Fact]
    public async Task Runner_SameKindAssertionsOnDifferentSelectorsCannotBeSwapped()
    {
        var flow = Flow();
        flow.Steps[0].Asserts!.Add(new()
        { Kind = "exists", Verify = true, Selector = new() { AutomationId = "other-result" } });
        var plan = PreviewFlowPlan.Create("plan", 1, Plan().Binding, flow);
        var host = new Host { ConfigureReport = report => report.Results[0].Asserts.Reverse() };
        var evidence = await new PreviewFlowRunner(host, true).RunAsync(plan);
        Assert.Equal(PreviewFlowOutcome.Failed, evidence.Outcome);
        Assert.Equal(0, host.Verifications);
    }

    [Fact]
    public async Task Runner_ReportOnlyObservationDoesNotCountAsHardVerification()
    {
        var flow = Flow();
        flow.Steps[0].Asserts!.Add(new() { Kind = "routeIs", Verify = false, Expected = "//home" });
        var plan = PreviewFlowPlan.Create("plan", 1, Plan().Binding, flow);
        var evidence = await new PreviewFlowRunner(new Host(), true).RunAsync(plan);
        Assert.Equal(PreviewFlowOutcome.Passed, evidence.Outcome);
        Assert.True(evidence.IndependentlyVerified);
    }

    [Theory]
    [InlineData(false, "oracle-failed")]
    [InlineData(true, "oracle-cancelled")]
    public async Task Runner_OracleExceptionPreservesKnownReplayOutcome(bool cancelled, string failureCode)
    {
        var host = new Host { OracleThrows = !cancelled, OracleCancels = cancelled };
        var runner = new PreviewFlowRunner(host, true);
        var evidence = await runner.RunAsync(PropertyPlan());
        Assert.Equal(PreviewFlowOutcome.Passed, evidence.Outcome);
        Assert.Null(evidence.FailureCode);
        Assert.False(evidence.IndependentlyVerified);
        Assert.Equal(failureCode, evidence.VerificationFailureCode);
        Assert.True(evidence.CleanupComplete);
        Assert.Equal(1, host.Verifications);
        Assert.Equal(1, host.Cleanups);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(PropertyPlan()));
        Assert.Equal(1, host.Dispatches);
        var json = JsonSerializer.Serialize(evidence, PreviewTestingJsonContext.Default.PreviewFlowEvidence);
        Assert.DoesNotContain("private", json, StringComparison.Ordinal);
        Assert.Contains(failureCode, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runner_CancellationAfterCompletedReplayDoesNotEraseUiPass()
    {
        using var cancellation = new CancellationTokenSource();
        var host = new Host { ConfigureReport = _ => cancellation.Cancel() };
        var evidence = await new PreviewFlowRunner(host, true).RunAsync(Plan(), cancellation.Token);
        Assert.Equal(PreviewFlowOutcome.Passed, evidence.Outcome);
        Assert.False(evidence.IndependentlyVerified);
        Assert.Equal("oracle-cancelled", evidence.VerificationFailureCode);
        Assert.Null(evidence.FailureCode);
        Assert.Equal(0, host.Verifications);
        Assert.Equal(1, host.Cleanups);
        Assert.False(host.CleanupTokenCanBeCancelled);
    }

    [Fact]
    public async Task Runner_CancellationBeforeDispatchDoesNotConsumeGrantOrContactHost()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var host = new Host();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PreviewFlowRunner(host, true).RunAsync(Plan(), cancellation.Token));
        Assert.Equal(0, host.Grants);
        Assert.Equal(0, host.Dispatches);
        Assert.Equal(0, host.Cleanups);
    }

    [Fact]
    public async Task Runner_OracleRejectionIsSeparateFromUiPassAndCleanupFailure()
    {
        var host = new Host { OracleSucceeded = false, CleanupThrows = true };
        var evidence = await new PreviewFlowRunner(host, true).RunAsync(Plan());
        Assert.Equal(PreviewFlowOutcome.Passed, evidence.Outcome);
        Assert.Null(evidence.FailureCode);
        Assert.False(evidence.IndependentlyVerified);
        Assert.Equal("oracle-rejected", evidence.VerificationFailureCode);
        Assert.Equal("cleanup-failed", evidence.CleanupFailureCode);
    }

    private sealed class Host : IPreviewFlowHost
    {
        public bool Approve = true, Cancel, Clean = true, Independent = true, OmitAssertion, Throw, CleanupThrows;
        public bool OracleSucceeded = true, OracleThrows, OracleCancels, CleanupTokenCanBeCancelled;
        public int Dispatches, Grants, Verifications, Cleanups;
        public Action<FlowReplayReport>? ConfigureReport;
        public Task<bool> ConsumeRunGrantAsync(PreviewFlowPlan plan, CancellationToken cancellationToken)
        { Grants++; return Task.FromResult(Approve); }
        public Task<FlowReplayReport> ReplayAsync(MauiFlow snapshot, CancellationToken cancellationToken)
        {
            Dispatches++;
            if (Cancel) throw new OperationCanceledException();
            if (Throw) throw new IOException("private host text must not be exported");
            var report = new FlowReplayReport
            {
                Ok = true, Total = snapshot.Steps.Count, Passed = snapshot.Steps.Count,
                Results = snapshot.Steps.Select(step => new FlowStepResult
                {
                    Seq = step.Seq, Action = step.Action, Ok = true,
                    Asserts = OmitAssertion ? [] : (step.Asserts ?? []).Select(assertion => new FlowAssertResult
                    {
                        Kind = assertion.Kind, AutomationId = assertion.Selector?.AutomationId,
                        Name = assertion.Name, Expected = assertion.Expected,
                        Ok = assertion.Verify ? true : null, Skipped = !assertion.Verify
                    }).ToList()
                }).ToList()
            };
            ConfigureReport?.Invoke(report);
            return Task.FromResult(report);
        }
        public Task<PreviewOracleResult> VerifyBusinessOutcomeAsync(CancellationToken cancellationToken)
        {
            Verifications++;
            if (OracleThrows) throw new IOException("private oracle error");
            if (OracleCancels) throw new OperationCanceledException("private oracle cancellation");
            return Task.FromResult(new PreviewOracleResult(Independent, OracleSucceeded));
        }
        public Task<bool> CleanupAsync(CancellationToken cancellationToken)
        {
            Cleanups++;
            CleanupTokenCanBeCancelled = cancellationToken.CanBeCanceled;
            return CleanupThrows ? throw new IOException("private cleanup error") : Task.FromResult(Clean);
        }
    }
}
