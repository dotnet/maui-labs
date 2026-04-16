using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Maui.Devices.Sensors;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Manages MAUI sensor subscriptions and broadcasts readings to connected WebSocket clients.
/// </summary>
public class SensorManager : IDisposable
{
    private readonly HashSet<string> _activeSensors = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<ConcurrentQueue<string>>> _subscribers = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastBroadcast = new();
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>
    /// Minimum interval between broadcasts per sensor. Readings arriving faster are dropped.
    /// Default 100ms (~10 readings/sec). Configurable via Start() or the throttleMs query param.
    /// </summary>
    public int ThrottleMs { get; set; } = 100;

    private static readonly string[] AllSensorNames =
        ["accelerometer", "barometer", "compass", "gyroscope", "magnetometer", "orientation"];

    public IReadOnlyCollection<string> SupportedSensors => AllSensorNames;

    public object GetStatus()
    {
        lock (_gate)
        {
            return AllSensorNames.Select(name => new
            {
                sensor = name,
                active = _activeSensors.Contains(name),
                supported = IsSensorSupported(name),
                subscribers = _subscribers.TryGetValue(name, out var subs) ? subs.Count : 0
            }).ToList();
        }
    }

    public bool IsActive(string sensorName)
    {
        lock (_gate) return _activeSensors.Contains(sensorName);
    }

    public string? Start(string sensorName, SensorSpeed speed = SensorSpeed.UI)
    {
        sensorName = sensorName.ToLowerInvariant();
        lock (_gate)
        {
            if (_activeSensors.Contains(sensorName))
                return null; // already running

            try
            {
                switch (sensorName)
                {
                    case "accelerometer":
                        if (!Accelerometer.IsSupported) return "Accelerometer not supported on this device";
                        Accelerometer.ReadingChanged += OnAccelerometerReading;
                        Accelerometer.Start(speed);
                        break;
                    case "barometer":
                        if (!Barometer.IsSupported) return "Barometer not supported on this device";
                        Barometer.ReadingChanged += OnBarometerReading;
                        Barometer.Start(speed);
                        break;
                    case "compass":
                        if (!Compass.IsSupported) return "Compass not supported on this device";
                        Compass.ReadingChanged += OnCompassReading;
                        Compass.Start(speed);
                        break;
                    case "gyroscope":
                        if (!Gyroscope.IsSupported) return "Gyroscope not supported on this device";
                        Gyroscope.ReadingChanged += OnGyroscopeReading;
                        Gyroscope.Start(speed);
                        break;
                    case "magnetometer":
                        if (!Magnetometer.IsSupported) return "Magnetometer not supported on this device";
                        Magnetometer.ReadingChanged += OnMagnetometerReading;
                        Magnetometer.Start(speed);
                        break;
                    case "orientation":
                        if (!OrientationSensor.IsSupported) return "Orientation sensor not supported on this device";
                        OrientationSensor.ReadingChanged += OnOrientationReading;
                        OrientationSensor.Start(speed);
                        break;
                    default:
                        return $"Unknown sensor: {sensorName}. Valid: {string.Join(", ", AllSensorNames)}";
                }
                _activeSensors.Add(sensorName);
                return null; // success
            }
            catch (Exception ex)
            {
                return $"Failed to start {sensorName}: {ex.Message}";
            }
        }
    }

    public string? Stop(string sensorName)
    {
        sensorName = sensorName.ToLowerInvariant();
        lock (_gate)
        {
            if (!_activeSensors.Contains(sensorName))
                return null; // already stopped

            try
            {
                switch (sensorName)
                {
                    case "accelerometer":
                        Accelerometer.Stop();
                        Accelerometer.ReadingChanged -= OnAccelerometerReading;
                        break;
                    case "barometer":
                        Barometer.Stop();
                        Barometer.ReadingChanged -= OnBarometerReading;
                        break;
                    case "compass":
                        Compass.Stop();
                        Compass.ReadingChanged -= OnCompassReading;
                        break;
                    case "gyroscope":
                        Gyroscope.Stop();
                        Gyroscope.ReadingChanged -= OnGyroscopeReading;
                        break;
                    case "magnetometer":
                        Magnetometer.Stop();
                        Magnetometer.ReadingChanged -= OnMagnetometerReading;
                        break;
                    case "orientation":
                        OrientationSensor.Stop();
                        OrientationSensor.ReadingChanged -= OnOrientationReading;
                        break;
                }
                _activeSensors.Remove(sensorName);
                return null;
            }
            catch (Exception ex)
            {
                return $"Failed to stop {sensorName}: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Subscribe a WebSocket client's queue to a sensor's readings.
    /// Returns the queue that will receive serialized JSON readings.
    /// </summary>
    public ConcurrentQueue<string> Subscribe(string sensorName)
    {
        sensorName = sensorName.ToLowerInvariant();
        var queue = new ConcurrentQueue<string>();
        var subs = _subscribers.GetOrAdd(sensorName, _ => new List<ConcurrentQueue<string>>());
        lock (subs) { subs.Add(queue); }
        return queue;
    }

    public void Unsubscribe(string sensorName, ConcurrentQueue<string> queue)
    {
        sensorName = sensorName.ToLowerInvariant();
        if (_subscribers.TryGetValue(sensorName, out var subs))
        {
            lock (subs) { subs.Remove(queue); }
        }
    }

    private void Broadcast(string sensorName, object data)
    {
        // Throttle: drop readings that arrive faster than ThrottleMs
        var now = DateTime.UtcNow;
        var last = _lastBroadcast.GetOrAdd(sensorName, DateTime.MinValue);
        if ((now - last).TotalMilliseconds < ThrottleMs)
            return;
        _lastBroadcast[sensorName] = now;

        var timestamp = now.ToString("O");
        var json = JsonSerializer.Serialize(new
        {
            type = "reading",
            timestamp,
            sensor = sensorName,
            data,
            reading = new
            {
                sensor = sensorName,
                timestamp,
                values = data
            }
        });

        if (_subscribers.TryGetValue(sensorName, out var subs))
        {
            List<ConcurrentQueue<string>> snapshot;
            lock (subs) { snapshot = new List<ConcurrentQueue<string>>(subs); }
            foreach (var q in snapshot)
                q.Enqueue(json);
        }
    }

    private static bool IsSensorSupported(string name) => name.ToLowerInvariant() switch
    {
        "accelerometer" => Accelerometer.IsSupported,
        "barometer" => Barometer.IsSupported,
        "compass" => Compass.IsSupported,
        "gyroscope" => Gyroscope.IsSupported,
        "magnetometer" => Magnetometer.IsSupported,
        "orientation" => OrientationSensor.IsSupported,
        _ => false
    };

    public static SensorSpeed ParseSpeed(string? speed) => speed?.ToLowerInvariant() switch
    {
        "game" => SensorSpeed.Game,
        "fastest" => SensorSpeed.Fastest,
        "default" => SensorSpeed.Default,
        _ => SensorSpeed.UI
    };

    // ── Sensor event handlers ──

    private void OnAccelerometerReading(object? sender, AccelerometerChangedEventArgs e)
        => Broadcast("accelerometer", new { e.Reading.Acceleration.X, e.Reading.Acceleration.Y, e.Reading.Acceleration.Z });

    private void OnBarometerReading(object? sender, BarometerChangedEventArgs e)
        => Broadcast("barometer", new { pressureInHectopascals = e.Reading.PressureInHectopascals });

    private void OnCompassReading(object? sender, CompassChangedEventArgs e)
        => Broadcast("compass", new { headingMagneticNorth = e.Reading.HeadingMagneticNorth });

    private void OnGyroscopeReading(object? sender, GyroscopeChangedEventArgs e)
        => Broadcast("gyroscope", new { e.Reading.AngularVelocity.X, e.Reading.AngularVelocity.Y, e.Reading.AngularVelocity.Z });

    private void OnMagnetometerReading(object? sender, MagnetometerChangedEventArgs e)
        => Broadcast("magnetometer", new { e.Reading.MagneticField.X, e.Reading.MagneticField.Y, e.Reading.MagneticField.Z });

    private void OnOrientationReading(object? sender, OrientationSensorChangedEventArgs e)
        => Broadcast("orientation", new { e.Reading.Orientation.X, e.Reading.Orientation.Y, e.Reading.Orientation.Z, e.Reading.Orientation.W });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var name in _activeSensors.ToList())
            Stop(name);
    }
}
