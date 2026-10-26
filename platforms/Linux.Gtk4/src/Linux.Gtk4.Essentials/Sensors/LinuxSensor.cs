using System.Globalization;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Sensors;

/// <summary>
/// Base class for Linux IIO (Industrial I/O) sensors used by MAUI Essentials.
/// </summary>
public abstract class LinuxSensor : IDisposable
{
    private const string IioBasePath = "/sys/bus/iio/devices";

    protected readonly byte[] ReadBuffer = new byte[128];
    protected double Scale = 1.0;

    private string? _devicePath;
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private bool _disposed;

    protected LinuxSensor(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Name { get; }

    public bool IsMonitoring { get; private set; }

    /// <summary>
    /// Returns whether a matching IIO device was found on the system.
    /// Discovery is performed lazily and cached.
    /// </summary>
    public bool IsSupported => EnsureDevicePath() is not null;

    /// <summary>
    /// Locates the concrete IIO device directory for this sensor type.
    /// </summary>
    /// <param name="dir">Candidate device directory under /sys/bus/iio/devices.</param>
    /// <returns>Full path to the device directory, or null if it does not match.</returns>
    protected abstract string? TryLocateDevice(string dir);

    /// <summary>
    /// Called periodically to read and process sensor values.
    /// </summary>
    /// <param name="devicePath">Path previously returned by <see cref="TryLocateDevice"/>.</param>
    protected abstract void ProcessReadings(string devicePath);

    protected static TimeSpan SensorSpeedToInterval(SensorSpeed speed) => speed switch
    {
        SensorSpeed.Fastest => TimeSpan.FromMilliseconds(16),   // ~60 Hz
        SensorSpeed.Game => TimeSpan.FromMilliseconds(20),   // ~50 Hz
        SensorSpeed.UI => TimeSpan.FromMilliseconds(60),   // ~16 Hz
        _ => TimeSpan.FromMilliseconds(200),  // ~5 Hz
    };

    public void Start(SensorSpeed sensorSpeed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_syncRoot)
        {
            if (IsMonitoring)
                throw new InvalidOperationException($"{Name} has already been started.");

            var devicePath = EnsureDevicePath()
                ?? throw new FeatureNotSupportedException($"No IIO {Name} device was found on this system.");

            IsMonitoring = true;
            _cts = new CancellationTokenSource();
            var interval = SensorSpeedToInterval(sensorSpeed);
            var token = _cts.Token;

            _pollTask = Task.Run(() => PollLoopAsync(devicePath, interval, token), token);
        }
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            if (!IsMonitoring)
                return;

            _cts?.Cancel();

            try
            {
                // Give the poll loop a short time to exit cleanly
                _pollTask?.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
            {
                // Expected
            }

            _cts?.Dispose();
            _cts = null;
            _pollTask = null;
            IsMonitoring = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Reads a double value from a sysfs file, returning <paramref name="fallback"/> on any error.
    /// </summary>
    protected double ReadDoubleOrDefault(string path, double fallback = double.NaN)
    {
        return TryReadValue(path) ?? fallback;
    }

    protected double? TryReadValue(string path)
    {
        try
        {
            Span<byte> buffer = ReadBuffer;
            using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                buffer.Length, FileOptions.SequentialScan);

            int bytesRead = fs.Read(buffer);
            if (bytesRead == 0)
                return null;

            // sysfs is ASCII + trailing '\n'
            var text = System.Text.Encoding.ASCII.GetString(buffer[..bytesRead]).Trim();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }
        catch
        {
            return null;
        }
    }

    private string? EnsureDevicePath()
    {
        if (_devicePath is not null)
            return Directory.Exists(_devicePath) ? _devicePath : null;

        if (!Directory.Exists(IioBasePath))
            return null;

        foreach (var dir in Directory.EnumerateDirectories(IioBasePath, "iio:device*"))
        {
            var path = TryLocateDevice(dir);
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                _devicePath = path;
                return path;
            }
        }

        return null;
    }

    private async Task PollLoopAsync(string devicePath, TimeSpan interval, CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(interval);

            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                try
                {
                    ProcessReadings(devicePath);
                }
                catch
                {
                    // Ignore transient sysfs read errors (device may be temporarily unavailable)
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on Stop()
        }
    }
}