using System.Text.Json;
using Microsoft.Maui.Cli.Utils;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

internal static class ThemeHostSelector
{
    public static bool ShouldUseHostThemeScopeAutomatically(
        string? platform,
        string? deviceType,
        DevFlowTheme theme,
        string? androidDevice,
        string? simulatorUdid)
    {
        if (IsAndroidTarget(platform, androidDevice))
            return IsVirtualDevice(deviceType) || IsAndroidEmulatorSerial(androidDevice);

        if (theme == DevFlowTheme.System)
            return false;

        return IsIosSimulatorTarget(platform, deviceType, simulatorUdid);
    }

    public static async Task<ThemeResult> SetHostThemeAsync(
        string? platform,
        string? deviceType,
        DevFlowTheme theme,
        string? androidDevice,
        string? simulatorUdid,
        string appScopeSuggestion)
    {
        if (IsAndroidTarget(platform, androidDevice))
        {
            var driver = new AndroidAppDriver { Serial = androidDevice };
            return await driver.SetThemeAsync(theme, ThemeSetScope.System);
        }

        if (IsIosSimulatorTarget(platform, deviceType, simulatorUdid))
        {
            var resolvedUdid = await ResolveUdidAsync(simulatorUdid);
            var driver = new iOSSimulatorAppDriver { DeviceUdid = resolvedUdid };
            return await driver.SetThemeAsync(theme, ThemeSetScope.System);
        }

        return new ThemeResult
        {
            Theme = theme,
            RequestedTheme = theme,
            Source = "system",
            Success = false,
            Message = $"System theme switching is not supported for platform '{platform ?? "unknown"}'. Use {appScopeSuggestion}.",
        };
    }

    private static async Task<string> ResolveUdidAsync(string? udid)
    {
        if (!string.IsNullOrWhiteSpace(udid))
            return udid;

        var result = await ProcessRunner.RunAsync("xcrun", ["simctl", "list", "devices", "booted", "-j"]);
        if (!result.Success || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"xcrun simctl list devices booted -j exited with code {result.ExitCode}."
                : result.StandardError.Trim();
            throw new InvalidOperationException($"Failed to resolve simulator UDID: {detail}");
        }

        using var document = JsonDocument.Parse(result.StandardOutput);
        if (document.RootElement.TryGetProperty("devices", out var devices))
        {
            foreach (var runtime in devices.EnumerateObject())
            {
                foreach (var device in runtime.Value.EnumerateArray())
                {
                    var state = device.TryGetProperty("state", out var stateElement) ? stateElement.GetString() : null;
                    if (state == "Booted")
                        return device.GetProperty("udid").GetString()!;
                }
            }
        }

        throw new InvalidOperationException("No booted simulator found. Pass --udid or boot a simulator.");
    }

    private static bool IsAndroidTarget(string? platform, string? androidDevice)
        => !string.IsNullOrWhiteSpace(androidDevice)
            || (platform?.Contains("android", StringComparison.OrdinalIgnoreCase) == true);

    private static bool IsIosSimulatorTarget(string? platform, string? deviceType, string? simulatorUdid)
    {
        if (!string.IsNullOrWhiteSpace(simulatorUdid))
            return true;

        if (!IsVirtualDevice(deviceType))
            return false;

        return platform?.Equals("ios", StringComparison.OrdinalIgnoreCase) == true
            || platform?.Contains("iossimulator", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsVirtualDevice(string? deviceType)
        => deviceType?.Equals("Virtual", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsAndroidEmulatorSerial(string? androidDevice)
        => androidDevice?.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase) == true;
}
