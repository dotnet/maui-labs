using System.Runtime.Versioning;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

// APIs with no usable web platform equivalent. All throw FeatureNotSupportedException
// (per MAUI convention) so callers can detect the missing capability; Is*Supported
// properties return false where the interface exposes them.

/// <summary>Contacts are not accessible from the browser (the Contact Picker API is not broadly available).</summary>
[SupportedOSPlatform("browser")]
public class BrowserContacts : IContacts
{
	public Task<Contact?> PickContactAsync() =>
		throw new FeatureNotSupportedException("Contacts are not available in the browser.");

	public Task<IEnumerable<Contact>> GetAllAsync(CancellationToken cancellationToken = default) =>
		throw new FeatureNotSupportedException("Contacts are not available in the browser.");
}

/// <summary>Map apps cannot be launched from the browser backend.</summary>
[SupportedOSPlatform("browser")]
public class BrowserMap : IMap
{
	public Task OpenAsync(double latitude, double longitude, MapLaunchOptions options) =>
		throw new FeatureNotSupportedException("Opening a maps app is not supported in the browser.");

	public Task OpenAsync(Placemark placemark, MapLaunchOptions options) =>
		throw new FeatureNotSupportedException("Opening a maps app is not supported in the browser.");

	public Task<bool> TryOpenAsync(double latitude, double longitude, MapLaunchOptions options) =>
		Task.FromResult(false);

	public Task<bool> TryOpenAsync(Placemark placemark, MapLaunchOptions options) =>
		Task.FromResult(false);
}

/// <summary>Media capture is not implemented for the browser backend.</summary>
[SupportedOSPlatform("browser")]
public class BrowserMediaPicker : IMediaPicker
{
	public bool IsCaptureSupported => false;

	public Task<FileResult?> PickPhotoAsync(MediaPickerOptions? options = null) =>
		throw new FeatureNotSupportedException("Use IFilePicker to pick files in the browser.");

	public Task<List<FileResult>> PickPhotosAsync(MediaPickerOptions? options = null) =>
		throw new FeatureNotSupportedException("Use IFilePicker to pick files in the browser.");

	public Task<FileResult?> CapturePhotoAsync(MediaPickerOptions? options = null) =>
		throw new FeatureNotSupportedException("Camera capture is not supported in the browser backend.");

	public Task<FileResult?> PickVideoAsync(MediaPickerOptions? options = null) =>
		throw new FeatureNotSupportedException("Use IFilePicker to pick files in the browser.");

	public Task<List<FileResult>> PickVideosAsync(MediaPickerOptions? options = null) =>
		throw new FeatureNotSupportedException("Use IFilePicker to pick files in the browser.");

	public Task<FileResult?> CaptureVideoAsync(MediaPickerOptions? options = null) =>
		throw new FeatureNotSupportedException("Camera capture is not supported in the browser backend.");
}

/// <summary>Screenshots of the page cannot be captured from within the browser sandbox.</summary>
[SupportedOSPlatform("browser")]
public class BrowserScreenshot : IScreenshot
{
	public bool IsCaptureSupported => false;

	public Task<IScreenshotResult> CaptureAsync() =>
		throw new FeatureNotSupportedException("Screenshots are not supported in the browser.");
}

/// <summary>The camera torch is not reachable from the browser.</summary>
[SupportedOSPlatform("browser")]
public class BrowserFlashlight : IFlashlight
{
	public Task<bool> IsSupportedAsync() => Task.FromResult(false);

	public Task TurnOnAsync() =>
		throw new FeatureNotSupportedException("The flashlight is not available in the browser.");

	public Task TurnOffAsync() =>
		throw new FeatureNotSupportedException("The flashlight is not available in the browser.");
}

/// <summary>No barometric pressure sensor is exposed to web content.</summary>
[SupportedOSPlatform("browser")]
public class BrowserBarometer : IBarometer
{
	public bool IsSupported => false;

	public bool IsMonitoring => false;

	public event EventHandler<BarometerChangedEventArgs>? ReadingChanged
	{
		add { }
		remove { }
	}

	public void Start(SensorSpeed sensorSpeed) =>
		throw new FeatureNotSupportedException("The barometer is not available in the browser.");

	public void Stop()
	{
	}
}

/// <summary>The Magnetometer sensor API is not broadly available to web content.</summary>
[SupportedOSPlatform("browser")]
public class BrowserMagnetometer : IMagnetometer
{
	public bool IsSupported => false;

	public bool IsMonitoring => false;

	public event EventHandler<MagnetometerChangedEventArgs>? ReadingChanged
	{
		add { }
		remove { }
	}

	public void Start(SensorSpeed sensorSpeed) =>
		throw new FeatureNotSupportedException("The magnetometer is not available in the browser.");

	public void Stop()
	{
	}
}

/// <summary>App shortcuts have no web equivalent.</summary>
[SupportedOSPlatform("browser")]
public class BrowserAppActions : IAppActions
{
	public bool IsSupported => false;

	public event EventHandler<AppActionEventArgs>? AppActionActivated
	{
		add { }
		remove { }
	}

	public Task<IEnumerable<AppAction>> GetAsync() =>
		throw new FeatureNotSupportedException("App actions are not supported in the browser.");

	public Task SetAsync(IEnumerable<AppAction> actions) =>
		throw new FeatureNotSupportedException("App actions are not supported in the browser.");
}

/// <summary>Geocoding requires a service backend; none is available in the browser.</summary>
[SupportedOSPlatform("browser")]
public class BrowserGeocoding : IGeocoding
{
	public Task<IEnumerable<Placemark>> GetPlacemarksAsync(double latitude, double longitude) =>
		throw new FeatureNotSupportedException("Geocoding is not supported in the browser.");

	public Task<IEnumerable<Location>> GetLocationsAsync(string address) =>
		throw new FeatureNotSupportedException("Geocoding is not supported in the browser.");
}
