using Microsoft.Maui.Devices.Sensors;
using System.Globalization;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Sensors;

public sealed class LinuxOrientationSensor : LinuxSensor, IOrientationSensor
{
    private const string QuaternionRawFile = "in_rot_quaternion_raw";
    private const string ScaleFile = "in_rot_scale";

    private string? _quaternionPath;

    public event EventHandler<OrientationSensorChangedEventArgs>? ReadingChanged;

    public LinuxOrientationSensor() : base("Orientation sensor") { }

    protected override void ProcessReadings(string devicePath)
    {
        if (_quaternionPath is null)
            return;

        string text;
        try
        {
            text = File.ReadAllText(_quaternionPath).Trim();
        }
        catch
        {
            return;
        }

        if (string.IsNullOrEmpty(text))
            return;

        // Expected format: "x y z w" or "w x y z" (space/tab separated)
        // Most IIO quaternion drivers output x y z w
        var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            return;

        if (!TryParse(parts[0], out var x) ||
            !TryParse(parts[1], out var y) ||
            !TryParse(parts[2], out var z) ||
            !TryParse(parts[3], out var w))
        {
            return;
        }

        // Correct – preserve IIO order (x y z w) which matches OrientationSensorData(x, y, z, w)
        var data = new OrientationSensorData(x * Scale, y * Scale, z * Scale, w * Scale);
        ReadingChanged?.Invoke(this, new OrientationSensorChangedEventArgs(data));
    }

    protected override string? TryLocateDevice(string dir)
    {
        var path = Path.Combine(dir, QuaternionRawFile);

        if (!File.Exists(path))
            return null;

        _quaternionPath = path;
        Scale = ReadDoubleOrDefault(Path.Combine(dir, ScaleFile), 1.0);

        return dir;
    }

    private static bool TryParse(string value, out double result)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}