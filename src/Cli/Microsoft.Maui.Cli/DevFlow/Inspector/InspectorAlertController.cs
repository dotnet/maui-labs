using Microsoft.Maui.Cli.DevFlow.Android;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Inspector;

internal sealed record InspectorAlertResult(
    bool Ok,
    bool Supported,
    AlertInfo? Alert = null,
    string? Error = null,
    bool Dismissed = false);

internal sealed class InspectorAlertController
{
    private readonly string _agentHost;
    private readonly int _agentPort;
    private readonly string? _appName;
    private readonly string? _platform;
    private int? _processId;
    private readonly Func<string, string?, int?, string?, string?, IAlertDriver?> _createDriver;
    private readonly Func<int, Task<AndroidDevFlowDeviceResolution>> _resolveAndroidDevice;
    private readonly Func<Task<int?>> _resolveProcessId;

    public InspectorAlertController(
        string agentHost,
        int agentPort,
        string? appName,
        string? platform,
        int? processId = null,
        Func<string, string?, int?, string?, string?, IAlertDriver?>? createDriver = null,
        Func<int, Task<AndroidDevFlowDeviceResolution>>? resolveAndroidDevice = null,
        Func<Task<int?>>? resolveProcessId = null)
    {
        _agentHost = agentHost;
        _agentPort = agentPort;
        _appName = appName;
        _platform = platform;
        _processId = processId;
        _createDriver = createDriver ?? CreateDriver;
        _resolveAndroidDevice = resolveAndroidDevice ?? ResolveAndroidDeviceAsync;
        _resolveProcessId = resolveProcessId ?? ResolveProcessIdFromAgentAsync;
    }

    public async Task<InspectorAlertResult> DetectAsync()
    {
        var target = NormalizePlatform(_platform);
        var processId = await ResolveProcessIdAsync(target);
        var androidDevice = await ResolveAndroidDeviceAsync(target);
        if (androidDevice.Error is not null)
            return new(false, true, Error: androidDevice.Error);
        if (!HasRequiredTargetIdentity(target, processId, androidDevice.Serial))
            return new(false, false, Error: UnsupportedMessage(target, processId));
        var driver = _createDriver(target, _appName, processId, androidDevice.Serial, androidDevice.AdbPath);
        if (driver is null)
            return new(false, false, Error: UnsupportedMessage(target, processId));

        using (driver)
        {
            try
            {
                if (driver is LinuxAppDriver)
                    await ((AppDriverBase)driver).ConnectAsync(_agentHost, _agentPort);
                var alert = await driver.DetectAlertAsync();
                return new(true, true, Alert: alert);
            }
            catch (Exception ex)
            {
                return new(false, true, Error: $"Native alert inspection failed: {ex.Message}");
            }
        }
    }

    public async Task<InspectorAlertResult> DismissAsync(string? buttonLabel)
    {
        var target = NormalizePlatform(_platform);
        var processId = await ResolveProcessIdAsync(target);
        var androidDevice = await ResolveAndroidDeviceAsync(target);
        if (androidDevice.Error is not null)
            return new(false, true, Error: androidDevice.Error);
        if (!HasRequiredTargetIdentity(target, processId, androidDevice.Serial))
            return new(false, false, Error: UnsupportedMessage(target, processId));
        var driver = _createDriver(target, _appName, processId, androidDevice.Serial, androidDevice.AdbPath);
        if (driver is null)
            return new(false, false, Error: UnsupportedMessage(target, processId));

        using (driver)
        {
            try
            {
                if (driver is LinuxAppDriver)
                    await ((AppDriverBase)driver).ConnectAsync(_agentHost, _agentPort);
                var alert = await driver.HandleAlertIfPresentAsync(buttonLabel);
                return alert is not null
                    ? new(true, true, Alert: alert, Dismissed: true)
                    : new(true, true, Alert: null, Error: "No native alert is visible.", Dismissed: false);
            }
            catch (Exception ex)
            {
                return new(false, true, Error: $"Native alert dismissal failed: {ex.Message}");
            }
        }
    }

    private static IAlertDriver? CreateDriver(
        string platform,
        string? appName,
        int? processId,
        string? androidSerial,
        string? androidAdbPath)
        => platform switch
        {
            "android" when !string.IsNullOrWhiteSpace(androidSerial) => new AndroidAppDriver
            {
                Serial = androidSerial,
                AdbPath = string.IsNullOrWhiteSpace(androidAdbPath) ? "adb" : androidAdbPath
            },
            "windows" when processId.HasValue => new WindowsAppDriver { ProcessId = processId },
            "maccatalyst" when processId.HasValue => new MacCatalystAppDriver { ProcessId = processId },
            "linux" => new LinuxAppDriver(),
            _ => null
        };

    private static bool HasRequiredTargetIdentity(
        string platform,
        int? processId,
        string? androidSerial)
        => platform switch
        {
            "android" => !string.IsNullOrWhiteSpace(androidSerial),
            "windows" or "maccatalyst" => processId.HasValue,
            "linux" => true,
            _ => false
        };

    private async Task<AndroidDevFlowDeviceResolution> ResolveAndroidDeviceAsync(string platform)
        => platform == "android"
            ? await _resolveAndroidDevice(_agentPort)
            : AndroidDevFlowDeviceResolution.Resolved("not-android");

    private static async Task<AndroidDevFlowDeviceResolution> ResolveAndroidDeviceAsync(int agentPort)
    {
        try
        {
            return await AndroidDevFlowPortForwarder.CreateDefault().ResolveDeviceForForwardedPortAsync(agentPort);
        }
        catch (Exception ex)
        {
            return AndroidDevFlowDeviceResolution.Failed($"Could not resolve the Android device: {ex.Message}");
        }
    }

    private static string NormalizePlatform(string? platform)
    {
        var value = (platform ?? string.Empty).ToLowerInvariant();
        if (value.Contains("android")) return "android";
        if (value.Contains("windows") || value.Contains("winui")) return "windows";
        if (value.Contains("maccatalyst") || value.Contains("mac catalyst")) return "maccatalyst";
        if (value.Contains("linux") || value.Contains("gtk")) return "linux";
        if (value.Contains("ios")) return "ios";
        return value;
    }

    private async Task<int?> ResolveProcessIdAsync(string platform)
    {
        if (platform is not ("windows" or "maccatalyst"))
            return _processId;

        return await _resolveProcessId();
    }

    private async Task<int?> ResolveProcessIdFromAgentAsync()
    {
        using var client = new AgentClient(_agentHost, _agentPort);
        return (await client.GetStatusAsync())?.App?.ProcessId;
    }

    private static string UnsupportedMessage(string platform, int? processId)
        => platform == "ios"
            ? "Inspector alert control for iOS requires a simulator UDID. Use 'maui devflow ui alert' with --device."
            : platform is "windows" or "maccatalyst" && !processId.HasValue
                ? "Inspector alert control requires process identity from the running agent. Upgrade the agent and reconnect."
            : $"Inspector alert control is not available for platform '{platform}'.";
}
