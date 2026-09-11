using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>
/// Shared plumbing for sensors backed by devicemotion/deviceorientation events.
/// SensorSpeed is ignored — browsers deliver events at their own fixed rate.
/// On iOS Safari the first Start triggers the motion-permission prompt and must be
/// called from a user gesture.
/// </summary>
[SupportedOSPlatform("browser")]
public abstract class BrowserSensorBase
{
	readonly string kind;

	private protected BrowserSensorBase(string kind) => this.kind = kind;

	public bool IsSupported
	{
		get
		{
			BrowserEssentials.EnsureInitialized();
			return BrowserEssentialsInterop.SensorIsSupported(kind);
		}
	}

	public bool IsMonitoring { get; private set; }

	private protected void StartCore(SensorSpeed sensorSpeed)
	{
		BrowserEssentials.EnsureInitialized();
		if (!IsSupported)
			throw new FeatureNotSupportedException($"The {kind} sensor is not available in this browser.");
		if (IsMonitoring)
			throw new InvalidOperationException($"The {kind} sensor is already being monitored.");
		IsMonitoring = true;
		_ = StartListenerAsync();
	}

	async Task StartListenerAsync()
	{
		var started = await BrowserEssentialsInterop.SensorStart(kind, OnReading).ConfigureAwait(false);
		if (!started)
			IsMonitoring = false;
	}

	public void Stop()
	{
		if (!IsMonitoring)
			return;
		BrowserEssentials.EnsureInitialized();
		BrowserEssentialsInterop.SensorStop(kind);
		IsMonitoring = false;
	}

	private protected abstract void OnReading(string json);
}

/// <summary>Accelerometer from devicemotion accelerationIncludingGravity, reported in g.</summary>
[SupportedOSPlatform("browser")]
public class BrowserAccelerometer : BrowserSensorBase, IAccelerometer
{
	public BrowserAccelerometer() : base("accelerometer") { }

	public event EventHandler<AccelerometerChangedEventArgs>? ReadingChanged;

	public event EventHandler? ShakeDetected
	{
		add { } // Shake detection is not implemented for the browser.
		remove { }
	}

	public void Start(SensorSpeed sensorSpeed) => StartCore(sensorSpeed);

	private protected override void OnReading(string json)
	{
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		ReadingChanged?.Invoke(this, new AccelerometerChangedEventArgs(new AccelerometerData(
			root.GetProperty("x").GetDouble(),
			root.GetProperty("y").GetDouble(),
			root.GetProperty("z").GetDouble())));
	}
}

/// <summary>Gyroscope from devicemotion rotationRate, converted to rad/s.</summary>
[SupportedOSPlatform("browser")]
public class BrowserGyroscope : BrowserSensorBase, IGyroscope
{
	public BrowserGyroscope() : base("gyroscope") { }

	public event EventHandler<GyroscopeChangedEventArgs>? ReadingChanged;

	public void Start(SensorSpeed sensorSpeed) => StartCore(sensorSpeed);

	private protected override void OnReading(string json)
	{
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		ReadingChanged?.Invoke(this, new GyroscopeChangedEventArgs(new GyroscopeData(
			root.GetProperty("x").GetDouble(),
			root.GetProperty("y").GetDouble(),
			root.GetProperty("z").GetDouble())));
	}
}

/// <summary>Orientation quaternion derived from deviceorientation Euler angles.</summary>
[SupportedOSPlatform("browser")]
public class BrowserOrientationSensor : BrowserSensorBase, IOrientationSensor
{
	public BrowserOrientationSensor() : base("orientation") { }

	public event EventHandler<OrientationSensorChangedEventArgs>? ReadingChanged;

	public void Start(SensorSpeed sensorSpeed) => StartCore(sensorSpeed);

	private protected override void OnReading(string json)
	{
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		ReadingChanged?.Invoke(this, new OrientationSensorChangedEventArgs(new OrientationSensorData(
			root.GetProperty("x").GetDouble(),
			root.GetProperty("y").GetDouble(),
			root.GetProperty("z").GetDouble(),
			root.GetProperty("w").GetDouble())));
	}
}

/// <summary>Compass heading from deviceorientationabsolute (or webkitCompassHeading on iOS).</summary>
[SupportedOSPlatform("browser")]
public class BrowserCompass : BrowserSensorBase, ICompass
{
	public BrowserCompass() : base("compass") { }

	public event EventHandler<CompassChangedEventArgs>? ReadingChanged;

	public void Start(SensorSpeed sensorSpeed) => StartCore(sensorSpeed);

	public void Start(SensorSpeed sensorSpeed, bool applyLowPassFilter) => StartCore(sensorSpeed);

	private protected override void OnReading(string json)
	{
		using var doc = JsonDocument.Parse(json);
		ReadingChanged?.Invoke(this, new CompassChangedEventArgs(new CompassData(
			doc.RootElement.GetProperty("heading").GetDouble())));
	}
}
