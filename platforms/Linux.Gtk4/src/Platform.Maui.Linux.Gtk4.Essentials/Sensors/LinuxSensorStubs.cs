using Microsoft.Maui.Devices.Sensors;

namespace Platform.Maui.Linux.Gtk4.Essentials.Sensors;

public class LinuxAccelerometer : IAccelerometer
{
	public bool IsSupported => false;
	public bool IsMonitoring => false;
	public void Start(SensorSpeed sensorSpeed) =>
		throw new PlatformNotSupportedException("Accelerometer is not available on Linux desktop.");
	public void Stop() { }
	public event EventHandler<AccelerometerChangedEventArgs>? ReadingChanged { add { } remove { } }
	public event EventHandler? ShakeDetected { add { } remove { } }
}

public class LinuxBarometer : IBarometer
{
	public bool IsSupported => false;
	public bool IsMonitoring => false;
	public void Start(SensorSpeed sensorSpeed) =>
		throw new PlatformNotSupportedException("Barometer is not available on Linux desktop.");
	public void Stop() { }
	public event EventHandler<BarometerChangedEventArgs>? ReadingChanged { add { } remove { } }
}

public class LinuxCompass : ICompass
{
	public bool IsSupported => false;
	public bool IsMonitoring => false;
	public void Start(SensorSpeed sensorSpeed) =>
		throw new PlatformNotSupportedException("Compass is not available on Linux desktop.");
	public void Start(SensorSpeed sensorSpeed, bool applyLowPassFilter) =>
		throw new PlatformNotSupportedException("Compass is not available on Linux desktop.");
	public void Stop() { }
	public event EventHandler<CompassChangedEventArgs>? ReadingChanged { add { } remove { } }
}

public class LinuxGyroscope : IGyroscope
{
	public bool IsSupported => false;
	public bool IsMonitoring => false;
	public void Start(SensorSpeed sensorSpeed) =>
		throw new PlatformNotSupportedException("Gyroscope is not available on Linux desktop.");
	public void Stop() { }
	public event EventHandler<GyroscopeChangedEventArgs>? ReadingChanged { add { } remove { } }
}

public class LinuxMagnetometer : IMagnetometer
{
	public bool IsSupported => false;
	public bool IsMonitoring => false;
	public void Start(SensorSpeed sensorSpeed) =>
		throw new PlatformNotSupportedException("Magnetometer is not available on Linux desktop.");
	public void Stop() { }
	public event EventHandler<MagnetometerChangedEventArgs>? ReadingChanged { add { } remove { } }
}

public class LinuxOrientationSensor : IOrientationSensor
{
	public bool IsSupported => false;
	public bool IsMonitoring => false;
	public void Start(SensorSpeed sensorSpeed) =>
		throw new PlatformNotSupportedException("Orientation sensor is not available on Linux desktop.");
	public void Stop() { }
	public event EventHandler<OrientationSensorChangedEventArgs>? ReadingChanged { add { } remove { } }
}

public class LinuxGeocoding : IGeocoding
{
	public Task<IEnumerable<Placemark>> GetPlacemarksAsync(double latitude, double longitude) =>
		throw new PlatformNotSupportedException("Geocoding is not available on Linux desktop.");

	public Task<IEnumerable<Location>> GetLocationsAsync(string address) =>
		throw new PlatformNotSupportedException("Geocoding is not available on Linux desktop.");
}
