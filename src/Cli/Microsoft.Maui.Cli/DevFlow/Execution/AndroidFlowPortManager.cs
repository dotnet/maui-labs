using Microsoft.Maui.Cli.DevFlow.Android;
using Microsoft.Maui.Cli.Providers.Android;
using Xamarin.Android.Tools;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal interface IAndroidFlowPortManager
{
    Task<AndroidDevFlowForwardingReport> EnsureAsync(
        AndroidDevFlowForwardingRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveReverseAsync(
        string deviceSerial,
        int port,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveForwardAsync(
        string deviceSerial,
        int port,
        CancellationToken cancellationToken = default);
}

internal sealed class AndroidFlowPortManager : IAndroidFlowPortManager
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(15);
    private readonly IAndroidProvider _androidProvider;
    private readonly IExecutionProcessRunner _processRunner;

    public AndroidFlowPortManager(
        IAndroidProvider androidProvider,
        IExecutionProcessRunner processRunner)
    {
        _androidProvider = androidProvider ?? throw new ArgumentNullException(nameof(androidProvider));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public Task<AndroidDevFlowForwardingReport> EnsureAsync(
        AndroidDevFlowForwardingRequest request,
        CancellationToken cancellationToken = default)
        => CreateForwarder().EnsureAsync(request, cancellationToken);

    public Task<bool> RemoveReverseAsync(
        string deviceSerial,
        int port,
        CancellationToken cancellationToken = default)
        => RemoveAsync(deviceSerial, "reverse", port, cancellationToken);

    public Task<bool> RemoveForwardAsync(
        string deviceSerial,
        int port,
        CancellationToken cancellationToken = default)
        => RemoveAsync(deviceSerial, "forward", port, cancellationToken);

    private async Task<bool> RemoveAsync(
        string deviceSerial,
        string direction,
        int port,
        CancellationToken cancellationToken)
    {
        var adbPath = ResolveAdbPath();
        var result = await _processRunner.RunAsync(
            adbPath,
            ["-s", deviceSerial, direction, "--remove", $"tcp:{port}"],
            timeout: CleanupTimeout,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    private AndroidDevFlowPortForwarder CreateForwarder()
    {
        var environment = AndroidEnvironment.BuildEnvironmentVariables(
            _androidProvider.SdkPath,
            _androidProvider.JdkPath);
        var adb = new Adb(() => _androidProvider.SdkPath, environment);
        if (string.IsNullOrWhiteSpace(adb.AdbPath) || adb.Runner is null)
        {
            throw FlowExecutionException.Infrastructure(
                "android-adb-not-found",
                "ADB was not found in the configured Android SDK.");
        }
        return new AndroidDevFlowPortForwarder(
            _androidProvider,
            adb.AdbPath,
            (AdbRunner)adb.Runner);
    }

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
}
