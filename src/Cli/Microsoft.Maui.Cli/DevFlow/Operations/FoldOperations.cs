using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

internal static class FoldOperations
{
    internal static FoldableDevice CreateDevice(string? androidDevice, string? simulatorUdid, bool experimental)
    {
        if (string.IsNullOrWhiteSpace(androidDevice) == string.IsNullOrWhiteSpace(simulatorUdid))
            throw new ArgumentException("Specify exactly one Android emulator serial (--device / androidDevice) or iOS Simulator UDID (--udid / simulatorUdid). No app agent is required.");
        return !string.IsNullOrWhiteSpace(androidDevice)
            ? new FoldableDevice("android", androidDevice, experimental)
            : new FoldableDevice("ios", simulatorUdid!, experimental);
    }

    internal static FoldMode ParseMode(string value)
        => FoldModes.TryParse(value, out var mode) && mode != FoldMode.Unknown ? mode
            : throw new ArgumentException("Use folded, partially-folded, or unfolded.", nameof(value));
}
