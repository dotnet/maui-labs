using System.Numerics;
using Microsoft.Maui.Devices.Sensors;
namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Sensors;

public sealed class LinuxAccelerometer : LinuxSensor, IAccelerometer
{
    private const double Gravity = 9.80665;
    private const int ShakeHistorySize = 12;
    private static readonly TimeSpan ShakeCheckInterval = TimeSpan.FromMilliseconds(80);

    // Cached sysfs paths (set once during discovery)
    private string? _xPath;
    private string? _yPath;
    private string? _zPath;

    private double _xScale = 1.0, _yScale = 1.0, _zScale = 1.0;
    private double _xOffset, _yOffset, _zOffset;

    // Shake detection state
    private double[]? _magnitudeHistory;
    private int _historyIndex;
    private DateTime _lastShakeCheck = DateTime.MinValue;

    public event EventHandler<AccelerometerChangedEventArgs>? ReadingChanged;
    public event EventHandler? ShakeDetected;

    /// <summary>
    /// Peak-to-peak magnitude threshold (in G) that triggers <see cref="ShakeDetected"/>.
    /// </summary>
    public double ShakeThresholdG { get; set; } = 1.5;

    public LinuxAccelerometer() : base("Accelerometer") { }

    protected override void ProcessReadings(string devicePath)
    {
        // Paths are guaranteed to be set if we reached ProcessReadings
        if (_xPath is null || _yPath is null || _zPath is null)
            return;

        var rawX = ReadDoubleOrDefault(_xPath);
        var rawY = ReadDoubleOrDefault(_yPath);
        var rawZ = ReadDoubleOrDefault(_zPath);

        if (double.IsNaN(rawX) || double.IsNaN(rawY) || double.IsNaN(rawZ))
            return;

        // Convert raw → m/s² → G (MAUI convention)
        var x = ((rawX + _xOffset) * _xScale) / Gravity;
        var y = ((rawY + _yOffset) * _yScale) / Gravity;
        var z = ((rawZ + _zOffset) * _zScale) / Gravity;

        var data = new AccelerometerData(x, y, z);
        ReadingChanged?.Invoke(this, new AccelerometerChangedEventArgs(data));

        CheckForShake(data.Acceleration);
    }

    private void CheckForShake(Vector3 acceleration)
    {
        _magnitudeHistory ??= new double[ShakeHistorySize];

        var magnitude = acceleration.Length();
        _magnitudeHistory[_historyIndex % ShakeHistorySize] = magnitude;
        _historyIndex++;

        var now = DateTime.UtcNow;
        if (now - _lastShakeCheck < ShakeCheckInterval)
            return;

        _lastShakeCheck = now;

        // Simple peak-to-peak over the recent history window
        double min = double.MaxValue;
        double max = double.MinValue;

        foreach (var v in _magnitudeHistory)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }

        if (max - min > ShakeThresholdG)
            ShakeDetected?.Invoke(this, EventArgs.Empty);
    }

    protected override string? TryLocateDevice(string dir)
    {
        var xPath = Path.Combine(dir, "in_accel_x_raw");
        var yPath = Path.Combine(dir, "in_accel_y_raw");
        var zPath = Path.Combine(dir, "in_accel_z_raw");

        if (!File.Exists(xPath) || !File.Exists(yPath) || !File.Exists(zPath))
            return null;

        // Cache the paths
        _xPath = xPath;
        _yPath = yPath;
        _zPath = zPath;

        // Prefer per-axis scale, fall back to common scale
        var commonScale = ReadDoubleOrDefault(Path.Combine(dir, "in_accel_scale"), 1.0);

        _xScale = ReadDoubleOrDefault(Path.Combine(dir, "in_accel_x_scale"), commonScale);
        _yScale = ReadDoubleOrDefault(Path.Combine(dir, "in_accel_y_scale"), commonScale);
        _zScale = ReadDoubleOrDefault(Path.Combine(dir, "in_accel_z_scale"), commonScale);

        _xOffset = ReadDoubleOrDefault(Path.Combine(dir, "in_accel_x_offset"), 0.0);
        _yOffset = ReadDoubleOrDefault(Path.Combine(dir, "in_accel_y_offset"), 0.0);
        _zOffset = ReadDoubleOrDefault(Path.Combine(dir, "in_accel_z_offset"), 0.0);

        return dir;
    }
}