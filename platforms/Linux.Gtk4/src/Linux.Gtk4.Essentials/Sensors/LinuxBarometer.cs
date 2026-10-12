using Microsoft.Maui.Devices.Sensors;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Sensors;

public sealed class LinuxBarometer : LinuxSensor, IBarometer
{
    private const string PressureRawFile = "in_pressure_raw";
    private const string PressureScaleFile = "in_pressure_scale";

    private string? _pressureRawPath;

    public event EventHandler<BarometerChangedEventArgs>? ReadingChanged;

    public LinuxBarometer() : base("Barometer") { }

    protected override void ProcessReadings(string devicePath)
    {
        if (_pressureRawPath is null)
            return;

        var raw = ReadDoubleOrDefault(_pressureRawPath);
        if (double.IsNaN(raw))
            return;

        // Apply scale (hPa or kPa depending on the driver; MAUI expects the value as-is after scaling)
        var pressure = raw * Scale;

        var data = new BarometerData(pressure);
        ReadingChanged?.Invoke(this, new BarometerChangedEventArgs(data));
    }

    protected override string? TryLocateDevice(string dir)
    {
        var rawPath = Path.Combine(dir, PressureRawFile);

        if (!File.Exists(rawPath))
            return null;

        _pressureRawPath = rawPath;
        Scale = ReadDoubleOrDefault(Path.Combine(dir, PressureScaleFile), 1.0);

        return dir;
    }
}