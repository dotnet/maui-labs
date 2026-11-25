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
            var resolvedUdid = await HostOperations.ResolveUdidAsync(simulatorUdid, new AgentStatus
            {
                Device = new() { Platform = platform, DeviceType = deviceType }
            });
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

    private static bool IsAndroidTarget(string? platform, string? androidDevice)
        => !string.IsNullOrWhiteSpace(androidDevice)
            || (platform?.Contains("android", StringComparison.OrdinalIgnoreCase) == true);

    private static bool IsIosSimulatorTarget(string? platform, string? deviceType, string? simulatorUdid)
    {
        if (platform?.StartsWith("tvos", StringComparison.OrdinalIgnoreCase) == true)
            return false;

        if (!string.IsNullOrWhiteSpace(simulatorUdid))
            return true;

        if (!IsVirtualDevice(deviceType))
            return false;

        return HostOperations.IsAppleSimulatorPlatform(platform);
    }

    private static bool IsVirtualDevice(string? deviceType)
        => deviceType?.Equals("Virtual", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsAndroidEmulatorSerial(string? androidDevice)
        => androidDevice?.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase) == true;
}
