using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Broker;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

/// <summary>
/// The device layer: the emulator or simulator an app runs inside, as opposed to the app itself.
/// <para>
/// This is a deliberately small, curated set. The underlying device host exposes far more, but
/// every tool added here competes for the agent's attention against the ~79 existing
/// <c>maui_*</c> tools, and a bloated surface measurably degrades tool selection. These are the
/// operations that unblock work the in-app agent structurally cannot do.
/// </para>
/// <para>
/// <b>Opt-in.</b> The full profile registers this type only when
/// <c>DEVFLOW_PREVIEW_MOBILE_CANVAS=true</c>. The companion these tools drive is a separately
/// installed experimental binary the CLI does not ship, so advertising them to every session would
/// offer a capability that is usually absent. VS Code gates the companion's own separate MCP server
/// the same way, behind <c>mauiDevflow.registerMobileCanvasMcpServer</c>.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class DeviceTools
{
    [McpServerTool(Name = "maui_device_list"),
     Description("List the virtual devices (iOS simulators and Android emulators) available on this machine, " +
                 "each paired with the running app agent inside it when there is one. " +
                 "Use this to find a device to boot, or to learn which device a running app is on. " +
                 "This is the device as the HOST sees it — for the device as the running APP sees it " +
                 "(model, OS version, battery, connectivity), use maui_device_info instead. " +
                 "Returns an 'available' flag that is false when no device host is installed, which is normal " +
                 "on many machines and on desktop MAUI apps.")]
    public static async Task<string> ListDevices(McpAgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var brokerPort = await session.GetBrokerPortAsync();
        var payload = await BrokerClient.ListDevicesAsync(brokerPort);

        return payload ?? "No device layer is available: the DevFlow broker could not be reached.";
    }

    [McpServerTool(Name = "maui_device_boot"),
     Description("Boot a virtual device and wait until it is ready to be deployed to and driven. " +
                 "Use this before deploying an app when no suitable device is running. " +
                 "Safe to call on an already-booted device.")]
    public static async Task<string> BootDevice(
        McpAgentSession session,
        [Description("Device id from maui_device_list.")] string deviceId)
    {
        return await MutateAsync(session, deviceId, "boot");
    }

    [McpServerTool(Name = "maui_device_shutdown"),
     Description("Power off a virtual device without erasing or deleting it. " +
                 "Any app agent running inside it will disconnect.")]
    public static async Task<string> ShutdownDevice(
        McpAgentSession session,
        [Description("Device id from maui_device_list.")] string deviceId)
    {
        return await MutateAsync(session, deviceId, "shutdown");
    }

    [McpServerTool(Name = "maui_device_tap"),
     Description("Tap at a physical point on the device screen, in device-independent points from the " +
                 "top-left of the display. " +
                 "Use this ONLY for things the app's own visual tree cannot reach: system permission dialogs, " +
                 "share sheets, the soft keyboard, OS navigation, or any screen shown before the app has " +
                 "launched or after it has crashed. " +
                 "For anything inside the app, prefer maui_tap, which targets an element by selector and so " +
                 "keeps recorded tests durable — physical coordinates do not survive a layout change.")]
    public static async Task<string> TapDevice(
        McpAgentSession session,
        [Description("Device id from maui_device_list.")] string deviceId,
        [Description("X coordinate in device-independent points from the left edge of the display.")] double x,
        [Description("Y coordinate in device-independent points from the top edge of the display.")] double y)
    {
        ArgumentNullException.ThrowIfNull(session);
        RequireDeviceId(deviceId);

        var brokerPort = await session.GetBrokerPortAsync();
        var result = await BrokerClient.ControlDeviceAsync(brokerPort, deviceId, "tap", x, y);

        return Describe(result, $"Tapped {deviceId} at ({x}, {y}).");
    }

    private static async Task<string> MutateAsync(McpAgentSession session, string deviceId, string action)
    {
        ArgumentNullException.ThrowIfNull(session);
        RequireDeviceId(deviceId);

        var brokerPort = await session.GetBrokerPortAsync();
        var result = await BrokerClient.ControlDeviceAsync(brokerPort, deviceId, action);

        return Describe(result, $"Device {deviceId}: {action} succeeded.");
    }

    private static void RequireDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new McpException("A device id is required. Call maui_device_list to find one.");
    }

    /// <summary>
    /// Turns a device result into agent-readable text. A refusal is reported as a described
    /// outcome rather than thrown, because "this platform cannot do that" is an expected answer
    /// on most machines and the agent needs the reason to choose a different approach.
    /// </summary>
    private static string Describe(DeviceControlResult result, string successMessage) =>
        result.Success ? successMessage : result.Reason ?? "The device operation was refused.";
}
