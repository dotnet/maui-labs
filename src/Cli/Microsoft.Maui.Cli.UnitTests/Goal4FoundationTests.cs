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
    }

    [Fact]
    public async Task Runner_MissingAssertionReceiptCannotPass()
    {
        var report = await new PreviewFlowRunner(new Host { OmitAssertion = true }, true).RunAsync(Plan());
        Assert.Equal(PreviewFlowOutcome.Failed, report.Outcome);
        Assert.False(report.IndependentlyVerified);
    }

    [Fact]
    public async Task Runner_HostFailureStillAttemptsCleanupAndDoesNotLeakErrorText()
    {
        var report = await new PreviewFlowRunner(new Host { Throw = true, CleanupThrows = true }, true).RunAsync(Plan());
        Assert.Equal(PreviewFlowOutcome.UnknownCompletion, report.Outcome);
        Assert.Equal("host-execution-failed", report.FailureCode);
        Assert.Equal("cleanup-failed", report.CleanupFailureCode);
    }

    private sealed class Host : IPreviewFlowHost
    {
        public bool Approve = true, Cancel, Clean = true, Independent = true, OmitAssertion, Throw, CleanupThrows;
        public int Dispatches, Grants;
        public Task<bool> ConsumeRunGrantAsync(PreviewFlowPlan plan, CancellationToken cancellationToken)
        { Grants++; return Task.FromResult(Approve); }
        public Task<FlowReplayReport> ReplayAsync(MauiFlow snapshot, CancellationToken cancellationToken)
        {
            Dispatches++;
            if (Cancel) throw new OperationCanceledException();
            if (Throw) throw new IOException("private host text must not be exported");
            return Task.FromResult(new FlowReplayReport
            { Ok = true, Total = 1, Passed = 1, Results = [new() { Seq = 1, Action = FlowActions.Assert,
                Ok = true, Asserts = OmitAssertion ? [] : [new() { Kind = "exists", Ok = true }] }] });
        }
        public Task<PreviewOracleResult> VerifyBusinessOutcomeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PreviewOracleResult(Independent, true));
        public Task<bool> CleanupAsync(CancellationToken cancellationToken) =>
            CleanupThrows ? throw new IOException("private cleanup error") : Task.FromResult(Clean);
    }
}
