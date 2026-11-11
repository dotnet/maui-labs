using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

internal sealed class ThemeOperations(AgentClient client)
{
    internal Task<ThemeResult?> GetAsync() =>
        client.GetThemeAsync();

    internal async Task<ThemeResult> SetAsync(
        string platform, DevFlowTheme theme, ThemeSetScope scope, string? androidDevice, string? simulatorUdid)
    {
        var status = await client.GetStatusAsync();
        var platformName = status?.Platform ?? platform;
        var deviceType = status?.DeviceType;

        var useHost = scope == ThemeSetScope.System
            || ThemeHostSelector.ShouldUseHostThemeScopeAutomatically(platformName, deviceType, theme, androidDevice, simulatorUdid);

        return useHost
            ? await ThemeHostSelector.SetHostThemeAsync(platformName, deviceType, theme, androidDevice, simulatorUdid, "--scope app")
            : await client.SetThemeAsync(theme);
    }
}
