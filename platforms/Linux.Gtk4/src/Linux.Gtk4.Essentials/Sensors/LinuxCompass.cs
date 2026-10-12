namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Sensors;

public sealed class LinuxCompass : LinuxSensor, ICompass
{
    // Preferred order: tilt-compensated magnetic → plain magnetic → tilt-compensated true north
    private static readonly string[] HeadingCandidates =
    {
        "in_rot_from_north_magnetic_tilt_comp_raw",
        "in_rot_from_north_magnetic_raw",
        "in_rot_from_north_true_tilt_comp_raw"
    };

    private const string ScaleFile = "in_rot_scale";

    private string? _headingPath;

    public event EventHandler<CompassChangedEventArgs>? ReadingChanged;

    public LinuxCompass() : base("Compass") { }

    // MAUI interface requires this overload; low-pass filter is not implemented on Linux yet
    public void Start(SensorSpeed sensorSpeed, bool applyLowPassFilter)
    {
        Start(sensorSpeed);
    }

    protected override void ProcessReadings(string devicePath)
    {
        if (_headingPath is null)
            return;

        var heading = ReadDoubleOrDefault(_headingPath);
        if (double.IsNaN(heading))
            return;

        // Most IIO drivers report degrees; apply scale if present
        var data = new CompassData(heading * Scale);
        ReadingChanged?.Invoke(this, new CompassChangedEventArgs(data));
    }

    protected override string? TryLocateDevice(string dir)
    {
        foreach (var attr in HeadingCandidates)
        {
            var path = Path.Combine(dir, attr);
            if (File.Exists(path))
            {
                _headingPath = path;
                Scale = ReadDoubleOrDefault(Path.Combine(dir, ScaleFile), 1.0);
                return dir;
            }
        }

        return null;
    }
}