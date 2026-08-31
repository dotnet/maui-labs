using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.DevFlow.Execution;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli.Providers.Android;

internal sealed partial class AndroidAppDeployment : IAndroidAppDeployment
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(45);
    private const int MaximumUserListCharacters = 64 * 1024;
    private readonly IAndroidProvider _androidProvider;
    private readonly IExecutionProcessRunner _processRunner;

    public AndroidAppDeployment(
        IAndroidProvider androidProvider,
        IExecutionProcessRunner processRunner)
    {
        _androidProvider = androidProvider ?? throw new ArgumentNullException(nameof(androidProvider));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<AndroidAppDeploymentSession> DeployAndLaunchAsync(
        AndroidAppDeploymentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateSerial(request.DeviceSerial);
        ValidatePackageId(request.PackageId);
        var apkPath = Path.GetFullPath(request.ApkPath);
        if (!File.Exists(apkPath) ||
            !string.Equals(Path.GetExtension(apkPath), ".apk", StringComparison.OrdinalIgnoreCase))
        {
            throw FlowExecutionException.Invalid("android-apk-missing", "The Android deployment artifact must be an existing APK.");
        }

        var adbPath = ResolveAdbPath();
        await EnsureSingleUserDeviceAsync(
            adbPath,
            request.DeviceSerial,
            cancellationToken).ConfigureAwait(false);
        var packageWasInstalled = await IsPackageInstalledAsync(
            adbPath,
            request.DeviceSerial,
            request.PackageId,
            cancellationToken).ConfigureAwait(false);
        if (packageWasInstalled)
        {
            throw FlowExecutionException.Unsupported(
                "android-preexisting-app-unsafe",
                "The exact Android device already contains this package. Flow run v1 refuses to replace app state it does not own.");
        }

        var partial = new AndroidAppDeploymentSession
        {
            DeviceSerial = request.DeviceSerial,
            PackageId = request.PackageId,
            Activity = "",
            PackageWasInstalledBefore = false,
            InstalledByInvocation = false,
            LaunchedByInvocation = false,
        };
        try
        {
            partial = partial with { InstallAttempted = true };
            var install = await RunCheckedAsync(
                adbPath,
                request.DeviceSerial,
                ["install", "-r", "-t", apkPath],
                "android-install-failed",
                InstallTimeout,
                cancellationToken).ConfigureAwait(false);
            if (install.StandardOutput.Contains("Failure", StringComparison.OrdinalIgnoreCase) ||
                install.StandardError.Contains("Failure", StringComparison.OrdinalIgnoreCase))
            {
                throw FlowExecutionException.Infrastructure(
                    "android-install-failed",
                    "ADB reported that the Android APK installation failed.");
            }
            partial = partial with
            {
                InstalledByInvocation = !partial.PackageWasInstalledBefore,
            };

            var resolution = await RunCheckedAsync(
                adbPath,
                request.DeviceSerial,
                [
                    "shell", "cmd", "package", "resolve-activity", "--brief",
                    "-c", "android.intent.category.LAUNCHER", request.PackageId,
                ],
                "android-launch-activity-unresolved",
                ReadTimeout,
                cancellationToken).ConfigureAwait(false);
            var activity = resolution.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault(static value => value.Contains('/'));
            if (string.IsNullOrWhiteSpace(activity) ||
                !ActivityPattern().IsMatch(activity) ||
                !string.Equals(activity[..activity.IndexOf('/')], request.PackageId, StringComparison.Ordinal))
            {
                throw FlowExecutionException.Invalid(
                    "android-launch-activity-mismatch",
                    "ADB did not prove an exact launcher activity for the resolved Android package.");
            }
            partial = partial with { Activity = activity };

            await RunCheckedAsync(
                adbPath,
                request.DeviceSerial,
                ["shell", "am", "force-stop", request.PackageId],
                "android-prelaunch-stop-failed",
                ReadTimeout,
                cancellationToken).ConfigureAwait(false);
            var launch = await RunCheckedAsync(
                adbPath,
                request.DeviceSerial,
                ["shell", "am", "start", "-W", "-n", activity],
                "android-launch-failed",
                LaunchTimeout,
                cancellationToken).ConfigureAwait(false);
            if (launch.StandardOutput.Contains("Error:", StringComparison.OrdinalIgnoreCase) ||
                launch.StandardError.Contains("Error:", StringComparison.OrdinalIgnoreCase))
            {
                throw FlowExecutionException.Infrastructure(
                    "android-launch-failed",
                    "ADB reported that the Android activity launch failed.");
            }
            partial = partial with { LaunchedByInvocation = true };

            var processId = await WaitForProcessAsync(
                adbPath,
                request.DeviceSerial,
                request.PackageId,
                cancellationToken).ConfigureAwait(false);
            return partial with { ProcessId = processId };
        }
        catch (FlowExecutionException ex)
        {
            throw new AndroidAppDeploymentException(ex, partial);
        }
        catch (OperationCanceledException ex)
        {
            partial = await ReconcileCancelledInstallAsync(
                adbPath,
                partial).ConfigureAwait(false);
            var mutationMayHaveCompleted =
                partial.LaunchedByInvocation ||
                partial.InstalledByInvocation ||
                partial.InstallationCompletionUnknown ||
                (partial.PackageWasInstalledBefore && partial.InstallAttempted);
            var failure = mutationMayHaveCompleted
                ? FlowExecutionException.UnknownCompletion(
                    partial.LaunchedByInvocation
                        ? "android-deployment-cancelled-after-launch"
                        : "android-install-cancelled-unknown",
                    partial.LaunchedByInvocation
                        ? "Android deployment was cancelled after launch; completion cannot be proven."
                        : "Android deployment was cancelled after installation began; package ownership was preserved for cleanup.",
                    ex)
                : FlowExecutionException.Infrastructure(
                    "android-deployment-cancelled",
                    "Android deployment was cancelled before launch completed.",
                    ex);
            throw new AndroidAppDeploymentException(failure, partial);
        }
        catch (Exception ex)
        {
            throw new AndroidAppDeploymentException(
                FlowExecutionException.Infrastructure(
                    "android-deployment-failed",
                    "Android deployment failed after the package was installed.",
                    ex),
                partial);
        }
    }

    public async Task<AndroidAppDeploymentCleanupResult> CleanupAsync(
        AndroidAppDeploymentSession session,
        string cleanupPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!FlowExecutionCleanupPolicies.IsKnown(cleanupPolicy))
        {
            return new AndroidAppDeploymentCleanupResult
            {
                Succeeded = false,
                DetailCode = "cleanup-policy-invalid",
            };
        }

        var adbPath = ResolveAdbPath();
        var installedByInvocation = session.InstalledByInvocation;
        if (session.InstallationCompletionUnknown && !session.PackageWasInstalledBefore)
        {
            try
            {
                installedByInvocation = await IsPackageInstalledAsync(
                    adbPath,
                    session.DeviceSerial,
                    session.PackageId,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (FlowExecutionException ex)
            {
                return new AndroidAppDeploymentCleanupResult
                {
                    Succeeded = false,
                    DetailCode = ex.Code,
                };
            }
        }

        if (cleanupPolicy == FlowExecutionCleanupPolicies.None)
        {
            return new AndroidAppDeploymentCleanupResult
            {
                Succeeded = true,
                DetailCode = installedByInvocation
                    ? "cleanup-none-owned-install-retained"
                    : "cleanup-none",
            };
        }

        var stopped = false;
        var uninstalled = false;
        var skipped = false;
        try
        {
            if (session.LaunchedByInvocation)
            {
                await RunCheckedAsync(
                    adbPath,
                    session.DeviceSerial,
                    ["shell", "am", "force-stop", session.PackageId],
                    "android-cleanup-stop-failed",
                    ReadTimeout,
                    cancellationToken).ConfigureAwait(false);
                stopped = true;
            }

            if (cleanupPolicy == FlowExecutionCleanupPolicies.Uninstall)
            {
                if (installedByInvocation)
                {
                    await RunCheckedAsync(
                        adbPath,
                        session.DeviceSerial,
                        ["uninstall", session.PackageId],
                        "android-cleanup-uninstall-failed",
                        TimeSpan.FromMinutes(1),
                        cancellationToken).ConfigureAwait(false);
                    uninstalled = true;
                }
                else
                {
                    skipped = true;
                }
            }

            return new AndroidAppDeploymentCleanupResult
            {
                Succeeded = true,
                PackageStopped = stopped,
                PackageUninstalled = uninstalled,
                UninstallSkippedNotOwned = skipped,
                DetailCode = skipped ? "uninstall-skipped-not-owned" : "cleanup-complete",
            };
        }
        catch (FlowExecutionException ex)
        {
            return new AndroidAppDeploymentCleanupResult
            {
                Succeeded = false,
                PackageStopped = stopped,
                PackageUninstalled = uninstalled,
                UninstallSkippedNotOwned = skipped,
                DetailCode = ex.Code,
            };
        }
    }

    public Task<bool> IsPackageInstalledAsync(
        string deviceSerial,
        string packageId,
        CancellationToken cancellationToken = default)
    {
        ValidateSerial(deviceSerial);
        ValidatePackageId(packageId);
        return IsPackageInstalledAsync(ResolveAdbPath(), deviceSerial, packageId, cancellationToken);
    }

    private async Task<bool> IsPackageInstalledAsync(
        string adbPath,
        string serial,
        string packageId,
        CancellationToken cancellationToken)    {
        var result = await RunAsync(
            adbPath,
            serial,
            ["shell", "pm", "path", packageId],
            ReadTimeout,
            cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            return result.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(static line => line.StartsWith("package:", StringComparison.Ordinal));
        }
        if (IsNormalPackageAbsence(result, packageId))
            return false;

        throw FlowExecutionException.Infrastructure(
            "android-package-query-failed",
            $"ADB failed while querying the Android package state (exit code {result.ExitCode}).");
    }

    private async Task<AndroidAppDeploymentSession> ReconcileCancelledInstallAsync(
        string adbPath,
        AndroidAppDeploymentSession session)
    {
        using var timeout = new CancellationTokenSource(ReadTimeout);
        try
        {
            var installed = await IsPackageInstalledAsync(
                adbPath,
                session.DeviceSerial,
                session.PackageId,
                timeout.Token).ConfigureAwait(false);
            if (!session.PackageWasInstalledBefore)
            {
                return session with
                {
                    InstalledByInvocation = installed,
                    InstallationCompletionUnknown = false,
                };
            }

            return session with { InstallationCompletionUnknown = true };
        }
        catch
        {
            return session with { InstallationCompletionUnknown = true };
        }
    }

    private static bool IsNormalPackageAbsence(ProcessResult result, string packageId)
    {
        if (result.ExitCode == 1 &&
            string.IsNullOrWhiteSpace(result.StandardOutput) &&
            string.IsNullOrWhiteSpace(result.StandardError))
        {
            return true;
        }

        var expected = $"Error: package {packageId} was not found";
        return result.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Concat(result.StandardError.Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Any(line => string.Equals(line, expected, StringComparison.OrdinalIgnoreCase));
    }

    private async Task EnsureSingleUserDeviceAsync(
        string adbPath,
        string serial,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            adbPath,
            serial,
            ["shell", "pm", "list", "users"],
            ReadTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!result.Success ||
            result.StandardOutput.Length > MaximumUserListCharacters ||
            result.StandardError.Length > MaximumUserListCharacters)
        {
            throw FlowExecutionException.Infrastructure(
                "android-user-topology-query-failed",
                "ADB could not prove a bounded single-user Android device state.");
        }

        var userIds = UserInfoPattern()
            .Matches(result.StandardOutput)
            .Cast<Match>()
            .Select(static match => match.Groups["id"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (userIds.Length == 0)
        {
            throw FlowExecutionException.Infrastructure(
                "android-user-topology-query-invalid",
                "ADB returned an unrecognized Android user topology.");
        }
        if (userIds.Length != 1 ||
            !string.Equals(userIds[0], "0", StringComparison.Ordinal))
        {
            throw FlowExecutionException.Unsupported(
                "android-multi-user-unsupported",
                "Android flow run v1 supports only a single system user (user 0) and refuses multi-user device state.");
        }
    }

    private async Task<int> WaitForProcessAsync(
        string adbPath,
        string serial,
        string packageId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + LaunchTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;
            var result = await RunAsync(
                adbPath,
                serial,
                ["shell", "pidof", packageId],
                remaining < ReadTimeout ? remaining : ReadTimeout,
                cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                var candidate = result.StandardOutput
                    .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
                if (int.TryParse(candidate, out var processId) && processId > 0)
                    return processId;
            }
            remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(
                    remaining < TimeSpan.FromMilliseconds(250)
                        ? remaining
                        : TimeSpan.FromMilliseconds(250),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        throw FlowExecutionException.Infrastructure(
            "android-process-timeout",
            "The launched Android package did not expose a process before the timeout.");
    }

    private async Task<ProcessResult> RunCheckedAsync(
        string adbPath,
        string serial,
        IReadOnlyList<string> arguments,
        string code,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(adbPath, serial, arguments, timeout, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw FlowExecutionException.Infrastructure(
                code,
                $"ADB failed during Android app deployment (exit code {result.ExitCode}).");
        }
        return result;
    }

    private Task<ProcessResult> RunAsync(
        string adbPath,
        string serial,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        => _processRunner.RunAsync(
            adbPath,
            ["-s", serial, .. arguments],
            timeout: timeout,
            cancellationToken: cancellationToken);

    private string ResolveAdbPath()
    {
        var sdkPath = _androidProvider.SdkPath;
        var executable = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        var path = string.IsNullOrWhiteSpace(sdkPath)
            ? null
            : Path.Combine(sdkPath, "platform-tools", executable);
        if (path is null || !File.Exists(path))
        {
            throw FlowExecutionException.Infrastructure(
                "android-adb-not-found",
                "ADB was not found in the configured Android SDK.");
        }
        return path;
    }

    private static void ValidateSerial(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial) ||
            serial.Length > 256 ||
            serial.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or ':')))
        {
            throw FlowExecutionException.Invalid("android-serial-invalid", "The Android device serial is invalid.");
        }
    }

    private static void ValidatePackageId(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId) || !PackagePattern().IsMatch(packageId))
            throw FlowExecutionException.Invalid("android-package-invalid", "The Android package identity is invalid.");
    }

    [GeneratedRegex(@"^[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PackagePattern();

    [GeneratedRegex(@"^[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+/[A-Za-z0-9_.$]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ActivityPattern();

    [GeneratedRegex(@"UserInfo\{(?<id>[0-9]+):", RegexOptions.CultureInvariant)]
    private static partial Regex UserInfoPattern();
}
