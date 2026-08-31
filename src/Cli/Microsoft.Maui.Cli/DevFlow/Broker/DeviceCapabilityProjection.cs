using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Projects only the device operations the DevFlow broker can execute today.
/// </summary>
internal static class DeviceCapabilityProjection
{
    public static JsonObject Create(DeviceCapabilities hostCapabilities) =>
        new()
        {
            ["boot"] = hostCapabilities.Boot,
            ["shutdown"] = hostCapabilities.Shutdown,
            ["restart"] = hostCapabilities.Restart,
            ["erase"] = hostCapabilities.Erase,
            ["delete"] = hostCapabilities.Delete,
            ["reveal"] = hostCapabilities.Reveal,
            ["tap"] = hostCapabilities.Tap,
            ["longPress"] = hostCapabilities.LongPress,
            ["swipe"] = hostCapabilities.Swipe,
            // The integrated Inspector exposes exact swipes; it does not route the host's
            // separate scroll primitive, so never advertise that route as executable here.
            ["scroll"] = false,
            ["text"] = hostCapabilities.Text,
            ["key"] = hostCapabilities.Key,
            ["button"] = hostCapabilities.Button,
            ["rotate"] = hostCapabilities.Rotate,
            ["screenshot"] = hostCapabilities.Screenshot,
            ["liveStream"] = hostCapabilities.LiveStream,
            ["recording"] = hostCapabilities.Recording,
        };
}
