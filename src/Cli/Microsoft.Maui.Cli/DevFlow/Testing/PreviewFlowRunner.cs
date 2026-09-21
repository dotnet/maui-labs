using Microsoft.Maui.Cli.DevFlow.Flows;

namespace Microsoft.Maui.Cli.DevFlow.Testing;

internal enum PreviewFlowOutcome { Passed, Failed, UnknownCompletion }

internal sealed record PreviewFlowEvidence(
    string PlanDigest, string FlowDigest, PreviewFlowOutcome Outcome,
    bool IndependentlyVerified, bool CleanupComplete, string? FailureCode)
{
    public string Qualification => "not-qualified";
    public bool DiagnosticOnly => true;
    public bool RepairAuthority => false;
}

internal sealed record PreviewOracleResult(bool Independent, bool Succeeded);

/// <summary>
/// A trusted local host must atomically consume a native-client-issued, single-use grant against
/// the plan's exact target, build, seed, checkpoint, selectors and actions. No issuer ships here.
/// </summary>
internal interface IPreviewFlowHost
{
    Task<bool> ConsumeRunGrantAsync(PreviewFlowPlan plan, CancellationToken cancellationToken);
    Task<FlowReplayReport> ReplayAsync(MauiFlow snapshot, CancellationToken cancellationToken);
    Task<PreviewOracleResult> VerifyBusinessOutcomeAsync(CancellationToken cancellationToken);
    Task<bool> CleanupAsync(CancellationToken cancellationToken);
}

/// <summary>Host-only, single-attempt execution. Not wired to CLI, MCP or Inspector mutation routes.</summary>
internal sealed class PreviewFlowRunner(IPreviewFlowHost host, bool enabled = false)
{
    private int _dispatched;

    public async Task<PreviewFlowEvidence> RunAsync(PreviewFlowPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!enabled)
            throw new InvalidOperationException("The Goal 4 execution preview is disabled.");
        if (Interlocked.CompareExchange(ref _dispatched, 1, 0) != 0)
            throw new InvalidOperationException("A run cannot be retried or continued.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!await host.ConsumeRunGrantAsync(plan, cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("A current exact-scope native human run grant is required.");

        PreviewFlowEvidence evidence;
        try
        {
            var report = await host.ReplayAsync(plan.CopyFlow(), cancellationToken).ConfigureAwait(false);
            var passed = report.Ok && report.Failed == 0 && report.Total == plan.CopyFlow().Steps.Count &&
                report.Passed == report.Total && report.Results.Count == report.Total &&
                report.Results.All(step => step.Ok);
            var oracle = passed
                ? await host.VerifyBusinessOutcomeAsync(cancellationToken).ConfigureAwait(false)
                : new PreviewOracleResult(false, false);
            evidence = new(plan.PlanDigest, plan.FlowDigest,
                passed ? PreviewFlowOutcome.Passed : PreviewFlowOutcome.Failed,
                passed && oracle.Independent && oracle.Succeeded, false, passed ? null : "flow-failed");
        }
        catch (OperationCanceledException)
        {
            evidence = new(plan.PlanDigest, plan.FlowDigest, PreviewFlowOutcome.UnknownCompletion,
                false, false, "unknown-completion");
        }
        // Cleanup is a separate result, never a replacement for the immutable primary outcome.
        var cleaned = await host.CleanupAsync(CancellationToken.None).ConfigureAwait(false);
        return evidence with { CleanupComplete = cleaned };
    }
}
