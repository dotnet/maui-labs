namespace Microsoft.Maui.Testing;

internal sealed record MauiTestRunResult(
    int ExitCode,
    int Passed,
    int Failed,
    int Skipped,
    string? TrxReportPath);

internal sealed record MauiTestCompletedEvent(
    string Uid,
    string Name,
    string? ClassName,
    string Outcome,
    string? Message,
    string? StackTrace);
