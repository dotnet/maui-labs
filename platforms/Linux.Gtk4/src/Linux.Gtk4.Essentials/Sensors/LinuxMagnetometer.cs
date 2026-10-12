using Microsoft.Maui.Devices.Sensors;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Sensors;

public sealed class LinuxMagnetometer : LinuxSensor, IMagnetometer
{
    // IIO reports magnetic field in Gauss after scaling; MAUI expects microteslas (1 G = 100 µT)
    const double GaussToMicrotesla = 100.0;
    private const string XRawFile = "in_magn_x_raw";
    private const string YRawFile = "in_magn_y_raw";
    private const string ZRawFile = "in_magn_z_raw";
    private const string ScaleFile = "in_magn_scale";

    private string? _xPath;
    private string? _yPath;
    private string? _zPath;

    public event EventHandler<MagnetometerChangedEventArgs>? ReadingChanged;

    public LinuxMagnetometer() : base("Magnetometer") { }

    protected override void ProcessReadings(string devicePath)
    {
        if (_xPath is null || _yPath is null || _zPath is null)
            return;

        var x = ReadDoubleOrDefault(_xPath);
        var y = ReadDoubleOrDefault(_yPath);
        var z = ReadDoubleOrDefault(_zPath);

        if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z))
            return;

        // IIO reports magnetic field in Gauss after scaling; MAUI expects microteslas (1 G = 100 µT)
        var data = new MagnetometerData(
                x * Scale * GaussToMicrotesla,
                y * Scale * GaussToMicrotesla,
                z * Scale * GaussToMicrotesla);
        ReadingChanged?.Invoke(this, new MagnetometerChangedEventArgs(data));
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