using System.ComponentModel;
using Microsoft.Maui.DevFlow.Driver;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class FoldTools
{
    [McpServerTool(Name = "maui_fold_capabilities"), Description("Discover fold/hinge support on an explicitly selected Android emulator or iOS simulator. Host-side: no connected app agent required. iOS private APIs require explicit experimental opt-in.")]
    public static async Task<string> Capabilities(
        McpAgentSession session,
        [Description("Explicit Android emulator serial, e.g. emulator-5554. Specify exactly one of androidDevice or simulatorUdid. Physical devices are not supported.")] string? androidDevice = null,
        [Description("Explicit booted iOS Simulator UDID. Mutually exclusive with androidDevice.")] string? simulatorUdid = null,
        [Description("Opt into private iOS simulator hinge APIs. Requires Apple Silicon and Xcode 27.1+. Default false; does not affect Android.")] bool allowExperimentalIos = false,
        [Description("Cancellation of the host operation.")] CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await FoldOperations.CreateDevice(androidDevice, simulatorUdid, allowExperimentalIos).GetCapabilitiesAsync(cancellationToken);
            return CliJson.SerializeUntyped(result, indented: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw new McpException(ex.Message, ex); }
    }

    [McpServerTool(Name = "maui_get_fold_state"), Description("Read hinge angles in degrees and observed fold mode without requiring an app agent. iOS also returns active/primary display IDs, UUIDs, dimensions, scale and rotation. This does not set the hinge.")]
    public static async Task<string> GetState(
        McpAgentSession session,
        [Description("Explicit Android emulator serial. Specify exactly one of androidDevice or simulatorUdid.")] string? androidDevice = null,
        [Description("Explicit booted iOS Simulator UDID; mutually exclusive with androidDevice.")] string? simulatorUdid = null,
        [Description("Opt into private iOS simulator hinge observation; defaults false.")] bool allowExperimentalIos = false,
        [Description("Cancellation of the host operation.")] CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await FoldOperations.CreateDevice(androidDevice, simulatorUdid, allowExperimentalIos).GetStateAsync(cancellationToken);
            return CliJson.SerializeUntyped(result, indented: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw new McpException(ex.Message, ex); }
    }

    [McpServerTool(Name = "maui_set_fold_state"), Description("Set and verify an emulator/simulator hinge mode or angle. Changes the whole virtual device, not just one app. No agent required. Never controls physical devices. iOS is experimental; timeouts may leave a partially changed device, so read state before retrying.")]
    public static async Task<string> SetState(
        McpAgentSession session,
        [Description("folded (0 degrees), unfolded (180 degrees), or partially-folded (90 degrees unless angleDegrees is specified). Presets affect the selected hinge only.")] string mode,
        [Description("Explicit Android emulator serial; specify exactly one of androidDevice or simulatorUdid.")] string? androidDevice = null,
        [Description("Explicit booted iOS Simulator UDID; mutually exclusive with androidDevice.")] string? simulatorUdid = null,
        [Description("Explicit degrees strictly between 0 and 180, valid only with partially-folded. No silent clamping.")] double? angleDegrees = null,
        [Description("Enabled hinge index; defaults to 0. iOS supports only index 0.")] int hingeIndex = 0,
        [Description("Animate from the observed angle over 0..30 seconds; default 0 is immediate.")] double durationSeconds = 0,
        [Description("Overall timeout including preparation and verification: 1..120 seconds, greater than durationSeconds.")] int timeoutSeconds = 30,
        [Description("Opt into private iOS simulator control. Defaults false. Requires Apple Silicon and Xcode 27.1+.")] bool allowExperimentalIos = false,
        [Description("Cancellation of the host operation.")] CancellationToken cancellationToken = default)
    {
        try
        {
            var parsedMode = FoldOperations.ParseMode(mode);
            var result = await FoldOperations.CreateDevice(androidDevice, simulatorUdid, allowExperimentalIos)
                .SetAsync(parsedMode, angleDegrees, hingeIndex, durationSeconds, timeoutSeconds, cancellationToken);
            return CliJson.SerializeUntyped(result, indented: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw new McpException(ex.Message, ex); }
    }
}
