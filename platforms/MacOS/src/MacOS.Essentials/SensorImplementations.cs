using Microsoft.Maui.Devices.Sensors;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

// macOS desktop hardware does not include motion/environmental sensors.
// These stubs report IsSupported = false and no-op for start/stop.

class AccelerometerImplementation : IAccelerometer
{
	public bool IsSupported => false;
	public bool IsMonitoring => false;
	public event EventHandler<AccelerometerChangedEventArgs>? ReadingChanged { add { } remove { } }
	public event EventHandler? ShakeDetected { add { } remove { } }
	public void Start(SensorSpeed sensorSpeed) { }
	public void Stop() { }
}

class GyroscopeImplementation : IGyroscope
{
	public bool IsSupported => false;
	public bool IsMonitoring => false;
	public event EventHandler<GyroscopeChangedEventArgs>? ReadingChanged { add { } remove { } }
	public void Start(SensorSpeed sensorSpeed) { }
	public void Stop() { }
}

class CompassImplementation : ICompass
{
	public bool IsSupported => false;
	public bool IsMonitoring => false;
	public event EventHandler<CompassChangedEventArgs>? ReadingChanged { add { } remove { } }
	public void Start(SensorSpeed sensorSpeed) { }
	public void Start(SensorSpeed sensorSpeed, bool applyLowPassFilter) { }
	public void Stop() { }
}

class BarometerImplementation : IBarometer
{
	public bool IsSupported => false;
	public bool IsMonitoring => false;
	public event EventHandler<BarometerChangedEventArgs>? ReadingChanged { add { } remove { } }
	public void Start(SensorSpeed sensorSpeed) { }
	public void Stop() { }
}

class MagnetometerImplementation : IMagnetometer
{
	public bool IsSupported => false;
	public bool IsMonitoring => false;
	public event EventHandler<MagnetometerChangedEventArgs>? ReadingChanged { add { } remove { } }
	public void Start(SensorSpeed sensorSpeed) { }
	public void Stop() { }
}
