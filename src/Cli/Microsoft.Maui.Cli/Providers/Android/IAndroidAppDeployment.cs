namespace Microsoft.Maui.Cli.Providers.Android;

internal sealed record AndroidAppDeploymentRequest
{
    public required string DeviceSerial { get; init; }
    public required string ApkPath { get; init; }
    public required string PackageId { get; init; }
}

internal sealed record AndroidAppDeploymentSession
{
    public required string DeviceSerial { get; init; }
    public required string PackageId { get; init; }
    public required string Activity { get; init; }
    public int? ProcessId { get; init; }
    public bool PackageWasInstalledBefore { get; init; }
    public bool InstalledByInvocation { get; init; }
    public bool InstallAttempted { get; init; }
    public bool InstallationCompletionUnknown { get; init; }
    public bool LaunchedByInvocation { get; init; }
}

internal sealed record AndroidAppDeploymentCleanupResult
{
    public bool Succeeded { get; init; }
    public bool PackageStopped { get; init; }
    public bool PackageUninstalled { get; init; }
    public bool UninstallSkippedNotOwned { get; init; }
    public string? DetailCode { get; init; }
}

internal sealed class AndroidAppDeploymentException : Exception
{
    public AndroidAppDeploymentException(
        Microsoft.Maui.Cli.DevFlow.Execution.FlowExecutionException failure,
        AndroidAppDeploymentSession session)
        : base(failure.Message, failure)
    {
        Failure = failure;
        Session = session;
    }

    public Microsoft.Maui.Cli.DevFlow.Execution.FlowExecutionException Failure { get; }
    public AndroidAppDeploymentSession Session { get; }
}

internal interface IAndroidAppDeployment
{
    Task<AndroidAppDeploymentSession> DeployAndLaunchAsync(
        AndroidAppDeploymentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports whether the exact device already holds the package, without installing anything.
    /// Used to refuse an inadmissible run before the app build rather than after it.
    /// </summary>
    Task<bool> IsPackageInstalledAsync(
        string deviceSerial,
        string packageId,
        CancellationToken cancellationToken = default);

    Task<AndroidAppDeploymentCleanupResult> CleanupAsync(
        AndroidAppDeploymentSession session,
        string cleanupPolicy,
        CancellationToken cancellationToken = default);
}
