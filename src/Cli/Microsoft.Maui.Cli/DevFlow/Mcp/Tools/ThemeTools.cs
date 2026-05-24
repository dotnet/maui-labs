using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class ThemeTools
{
    [McpServerTool(Name = "maui_get_theme"),
     Description("Get the current app-scoped light/dark theme reported by the connected MAUI DevFlow agent.")]
    public static async Task<string> GetTheme(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        var result = await agent.GetThemeAsync();
        if (result == null)
            throw new McpException("Unable to retrieve theme. The agent may not be running or may not support theme switching.");

        return CliJson.SerializeUntyped(result, indented: false);
    }

    [McpServerTool(Name = "maui_set_theme"),
     Description("Set the running app or disposable emulator/simulator to light, dark, or system theme. Scope defaults to auto: uses system scope only for Android emulators and iOS simulators, and app scope for desktops or physical devices.")]
    public static async Task<string> SetTheme(
        McpAgentSession session,
        [Description("Theme to apply: light, dark, or system. System means follow the OS in app scope; iOS Simulator host scope supports only light and dark.")] string theme,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
        [Description("Where to apply the theme: auto, app, or system. Auto uses host-side switching only for Android emulators and iOS simulators.")] string scope = "auto",
        [Description("Android adb device/emulator serial for system-scope changes. Optional when adb has exactly one target.")] string? androidDevice = null,
        [Description("iOS Simulator UDID for system-scope changes. If omitted for simulator targets, the booted simulator is auto-detected.")] string? simulatorUdid = null)
    {
        if (!ThemeExtensions.TryParseTheme(theme, out var parsedTheme))
            throw new McpException($"Invalid theme '{theme}'. Use light, dark, or system.");

        if (!ThemeExtensions.TryParseScope(scope, out var parsedScope))
            throw new McpException($"Invalid scope '{scope}'. Use auto, app, or system.");

        using var agent = await session.GetAgentClientAsync(agentPort);
        var status = await agent.GetStatusAsync();

        var platform = status?.Platform;
        var deviceType = status?.DeviceType;
        var useHost = parsedScope == ThemeSetScope.System
            || ThemeHostSelector.ShouldUseHostThemeScopeAutomatically(platform, deviceType, parsedTheme, androidDevice, simulatorUdid);

        ThemeResult result = useHost
            ? await ThemeHostSelector.SetHostThemeAsync(platform, deviceType, parsedTheme, androidDevice, simulatorUdid, "app scope")
            : await agent.SetThemeAsync(parsedTheme);

        if (!result.Success)
            throw new McpException(result.Message ?? "Failed to set theme.");

        return CliJson.SerializeUntyped(result, indented: false);
    }
}
