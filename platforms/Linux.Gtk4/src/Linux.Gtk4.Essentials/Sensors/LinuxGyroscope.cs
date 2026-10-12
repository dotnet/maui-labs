using Microsoft.Maui.Devices.Sensors;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Sensors;

public sealed class LinuxGyroscope : LinuxSensor, IGyroscope
{
    private const string XRawFile = "in_anglvel_x_raw";
    private const string YRawFile = "in_anglvel_y_raw";
    private const string ZRawFile = "in_anglvel_z_raw";
    private const string ScaleFile = "in_anglvel_scale";

    private string? _xPath;
    private string? _yPath;
    private string? _zPath;

    public event EventHandler<GyroscopeChangedEventArgs>? ReadingChanged;

    public LinuxGyroscope() : base("Gyroscope") { }

    protected override void ProcessReadings(string devicePath)
    {
        if (_xPath is null || _yPath is null || _zPath is null)
            return;

        var x = ReadDoubleOrDefault(_xPath);
        var y = ReadDoubleOrDefault(_yPath);
        var z = ReadDoubleOrDefault(_zPath);

        if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z))
            return;

        // Apply common scale (rad/s)
        var data = new GyroscopeData(x * Scale, y * Scale, z * Scale);
        ReadingChanged?.Invoke(this, new GyroscopeChangedEventArgs(data));
    }

    protected override string? TryLocateDevice(string dir)
    {
        var xPath = Path.Combine(dir, XRawFile);
        var yPath = Path.Combine(dir, YRawFile);
        var zPath = Path.Combine(dir, ZRawFile);

        if (!File.Exists(xPath) || !File.Exists(yPath) || !File.Exists(zPath))
            return null;

        _xPath = xPath;
        _yPath = yPath;
        _zPath = zPath;
        Scale = ReadDoubleOrDefault(Path.Combine(dir, ScaleFile), 1.0);

        return dir;
    }
}