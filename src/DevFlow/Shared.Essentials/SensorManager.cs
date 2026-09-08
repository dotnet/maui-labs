using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
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

    /// <summary>
    /// Serialises a reading and fans it out to subscribers.
    /// </summary>
    /// <param name="writeValues">
    /// Writes the reading's values as properties of an already-open JSON object. This is a
    /// delegate rather than an object because reflection-based serialisation is not trim-safe
    /// (IL2026), and because widening the sensor floats to double here would change the emitted
    /// numbers — <c>0.1f</c> serialises as <c>0.1</c>, but <c>(double)0.1f</c> serialises as
    /// <c>0.10000000149011612</c>.
    /// </param>
    private void Broadcast(string sensorName, Action<Utf8JsonWriter> writeValues)
    {
        // Throttle: drop readings that arrive faster than ThrottleMs
        var now = DateTime.UtcNow;
        var last = _lastBroadcast.GetOrAdd(sensorName, DateTime.MinValue);
        if ((now - last).TotalMilliseconds < ThrottleMs)
            return;
        _lastBroadcast[sensorName] = now;

        var timestamp = now.ToString("O");

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "reading");
            writer.WriteString("timestamp", timestamp);
            writer.WriteString("sensor", sensorName);

            writer.WritePropertyName("data");
            writer.WriteStartObject();
            writeValues(writer);
            writer.WriteEndObject();

            writer.WritePropertyName("reading");
            writer.WriteStartObject();
            writer.WriteString("sensor", sensorName);
            writer.WriteString("timestamp", timestamp);
            writer.WritePropertyName("values");
            writer.WriteStartObject();
            writeValues(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        var json = Encoding.UTF8.GetString(buffer.WrittenSpan);

        if (_subscribers.TryGetValue(sensorName, out var subs))
        {
            List<ConcurrentQueue<string>> snapshot;
            lock (subs) { snapshot = new List<ConcurrentQueue<string>>(subs); }
            foreach (var q in snapshot)
                q.Enqueue(json);
        }
    }

    private static bool IsSensorSupported(string name)
    {
        try
        {
            return name.ToLowerInvariant() switch
            {
                "accelerometer" => Accelerometer.IsSupported,
                "barometer" => Barometer.IsSupported,
                "compass" => Compass.IsSupported,
                "gyroscope" => Gyroscope.IsSupported,
                "magnetometer" => Magnetometer.IsSupported,
                "orientation" => OrientationSensor.IsSupported,
                _ => false
            };
        }
        catch (Exception ex) when (ex is NotSupportedException or PlatformNotSupportedException ||
                                   ex.GetType().Name == "NotImplementedInReferenceAssemblyException")
        {
            return false;
        }
    }

    public static SensorSpeed ParseSpeed(string? speed) => speed?.ToLowerInvariant() switch
    {
        "game" => SensorSpeed.Game,
        "fastest" => SensorSpeed.Fastest,
        "default" => SensorSpeed.Default,
        _ => SensorSpeed.UI
    };

    // ── Sensor event handlers ──

    private void OnAccelerometerReading(object? sender, AccelerometerChangedEventArgs e)
    {
        var v = e.Reading.Acceleration;
        Broadcast("accelerometer", w => WriteXyz(w, v.X, v.Y, v.Z));
    }

    private void OnBarometerReading(object? sender, BarometerChangedEventArgs e)
    {
        var pressure = e.Reading.PressureInHectopascals;
        Broadcast("barometer", w => w.WriteNumber("pressureInHectopascals", pressure));
    }

    private void OnCompassReading(object? sender, CompassChangedEventArgs e)
    {
        var heading = e.Reading.HeadingMagneticNorth;
        Broadcast("compass", w => w.WriteNumber("headingMagneticNorth", heading));
    }

    private void OnGyroscopeReading(object? sender, GyroscopeChangedEventArgs e)
    {
        var v = e.Reading.AngularVelocity;
        Broadcast("gyroscope", w => WriteXyz(w, v.X, v.Y, v.Z));
    }

    private void OnMagnetometerReading(object? sender, MagnetometerChangedEventArgs e)
    {
        var v = e.Reading.MagneticField;
        Broadcast("magnetometer", w => WriteXyz(w, v.X, v.Y, v.Z));
    }

    private void OnOrientationReading(object? sender, OrientationSensorChangedEventArgs e)
    {
        var q = e.Reading.Orientation;
        Broadcast("orientation", w =>
        {
            WriteXyz(w, q.X, q.Y, q.Z);
            w.WriteNumber("W", q.W);
        });
    }

    private static void WriteXyz(Utf8JsonWriter writer, float x, float y, float z)
    {
        writer.WriteNumber("X", x);
        writer.WriteNumber("Y", y);
        writer.WriteNumber("Z", z);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var name in _activeSensors.ToList())
            Stop(name);
    }
}
