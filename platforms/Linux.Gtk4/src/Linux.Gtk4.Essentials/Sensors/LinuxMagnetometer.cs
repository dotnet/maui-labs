using Microsoft.Maui.Devices.Sensors;
using System.Globalization;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Sensors;

public sealed class LinuxMagnetometer : LinuxSensor, IMagnetometer
{
    // IIO reports magnetic field in Gauss after scaling; MAUI expects microteslas (1 G = 100 µT)
    const double GaussToMicrotesla = 100.0;
    private const string XRawFile = "in_magn_x_raw";
    private const string YRawFile = "in_magn_y_raw";
    private const string ZRawFile = "in_magn_z_raw";
    
    private string? _xPath;
    private string? _yPath;
    private string? _zPath;

    double _scaleX = 1.0;
    double _scaleY = 1.0;
    double _scaleZ = 1.0;

    public event EventHandler<MagnetometerChangedEventArgs>? ReadingChanged;

    public LinuxMagnetometer() : base("Magnetometer") { }

    protected override void ProcessReadings(string devicePath)
    {
        if (_xPath is null || _yPath is null || _zPath is null)
            return;

        var x = ReadSampleOrNaN(_xPath);
        var y = ReadSampleOrNaN(_yPath);
        var z = ReadSampleOrNaN(_zPath);

        if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z))
            return;

        // IIO reports magnetic field in Gauss after scaling; MAUI expects microteslas (1 G = 100 µT)
        var data = new MagnetometerData(
                x * _scaleX * GaussToMicrotesla,
                y * _scaleY * GaussToMicrotesla,
                z * _scaleZ * GaussToMicrotesla);
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

        // Prefer per-axis, fall back to common
        _scaleX = ReadScale(dir, "in_magn_x_scale")
               ?? ReadScale(dir, "in_magn_scale")
               ?? 1.0;

        _scaleY = ReadScale(dir, "in_magn_y_scale")
               ?? ReadScale(dir, "in_magn_scale")
               ?? 1.0;

        _scaleZ = ReadScale(dir, "in_magn_z_scale")
               ?? ReadScale(dir, "in_magn_scale")
               ?? 1.0;

        return dir;
    }

    static double? ReadScale(string devicePath, string attribute)
    {
        var path = Path.Combine(devicePath, attribute);
        if (!File.Exists(path))
            return null;

        if (double.TryParse(File.ReadAllText(path).Trim(),
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var value))
            return value;

        return null;
    }
}