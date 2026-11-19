using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Microsoft.Maui.DevFlow.Driver;

public sealed partial class FoldableDevice
{
    private Task<string> AdbAsync(CancellationToken token, params string[] arguments)
        => _run(_adbPath, ["-s", DeviceId, .. arguments], token);

    private async Task<int[]> GetAndroidHingesAsync(CancellationToken token)
    {
        var devices = await _run(_adbPath, ["devices"], token).ConfigureAwait(false);
        if (!devices.Split('\n').Any(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            is [var serial, "device", ..] && serial == DeviceId))
            throw new InvalidOperationException($"Android emulator {DeviceId} is not connected and online.");
        var status = await AdbAsync(token, "emu", "sensor", "status").ConfigureAwait(false);
        return ParseAndroidHinges(status);
    }

    internal static int[] ParseAndroidHinges(string status)
        => Regex.Matches(status, @"(?m)^hinge-angle(\d+): enabled\.\s*$")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).Order().ToArray();

    private async Task<FoldHinge[]> ReadAndroidAnglesAsync(int[] indices, CancellationToken token)
    {
        var values = new List<FoldHinge>();
        foreach (var index in indices)
        {
            var sensor = $"hinge-angle{index}";
            var output = await AdbAsync(token, "emu", "sensor", "get", sensor).ConfigureAwait(false);
            values.Add(new FoldHinge { Index = index, AngleDegrees = ParseAndroidAngle(output, sensor) });
        }
        return values.ToArray();
    }

    internal static double ParseAndroidAngle(string output, string sensor)
    {
        var match = Regex.Match(output, $@"(?m)^{Regex.Escape(sensor)}\s*=\s*([-+0-9.eE]+)(?=[:\s]|$)");
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new InvalidOperationException($"Malformed angle response for {sensor}: {output.Trim()}");
        return value;
    }

    private async Task SetAndroidAngleAsync(double start, double angle, int index, double duration, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            var progress = duration == 0 ? 1 : Math.Min(1, watch.Elapsed.TotalSeconds / duration);
            var value = start + (angle - start) * progress;
            await AdbAsync(token, "emu", "sensor", "set", $"hinge-angle{index}",
                value.ToString("R", CultureInfo.InvariantCulture)).ConfigureAwait(false);
            if (progress >= 1) break;
            await Task.Delay(50, token).ConfigureAwait(false);
        } while (true);
    }
}
