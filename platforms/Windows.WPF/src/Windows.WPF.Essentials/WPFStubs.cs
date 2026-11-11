using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;

// Suppress unused event warnings — these events are required by MAUI interfaces
// but are never fired in desktop stub implementations (no physical sensors).
#pragma warning disable CS0067

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials
{
	// Sensor stubs - WPF desktop doesn't have physical sensors

	public class WPFAccelerometer : IAccelerometer
	{
		public bool IsSupported => false;
		public bool IsMonitoring => false;
		public void Start(SensorSpeed sensorSpeed) { }
		public void Stop() { }
		public event EventHandler<AccelerometerChangedEventArgs>? ReadingChanged;
		public event EventHandler? ShakeDetected;
	}

	public class WPFBarometer : IBarometer
	{
		public bool IsSupported => false;
		public bool IsMonitoring => false;
		public void Start(SensorSpeed sensorSpeed) { }
		public void Stop() { }
		public event EventHandler<BarometerChangedEventArgs>? ReadingChanged;
	}

	public class WPFCompass : ICompass
	{
		public bool IsSupported => false;
		public bool IsMonitoring => false;
		public void Start(SensorSpeed sensorSpeed) { }
		public void Start(SensorSpeed sensorSpeed, bool applyLowPassFilter) { }
		public void Stop() { }
		public event EventHandler<CompassChangedEventArgs>? ReadingChanged;
	}

	public class WPFGyroscope : IGyroscope
	{
		public bool IsSupported => false;
		public bool IsMonitoring => false;
		public void Start(SensorSpeed sensorSpeed) { }
		public void Stop() { }
		public event EventHandler<GyroscopeChangedEventArgs>? ReadingChanged;
	}

	public class WPFMagnetometer : IMagnetometer
	{
		public bool IsSupported => false;
		public bool IsMonitoring => false;
		public void Start(SensorSpeed sensorSpeed) { }
		public void Stop() { }
		public event EventHandler<MagnetometerChangedEventArgs>? ReadingChanged;
	}

	public class WPFOrientationSensor : IOrientationSensor
	{
		public bool IsSupported => false;
		public bool IsMonitoring => false;
		public void Start(SensorSpeed sensorSpeed) { }
		public void Stop() { }
		public event EventHandler<OrientationSensorChangedEventArgs>? ReadingChanged;
	}

	public class WPFGeocoding : IGeocoding
	{
		public Task<IEnumerable<Placemark>> GetPlacemarksAsync(double latitude, double longitude)
			=> Task.FromResult<IEnumerable<Placemark>>(Array.Empty<Placemark>());
		public Task<IEnumerable<Location>> GetLocationsAsync(string address)
			=> Task.FromResult<IEnumerable<Location>>(Array.Empty<Location>());
	}

	public class WPFVibration : IVibration
	{
		public bool IsSupported => false;
		public void Vibrate() { }
		public void Vibrate(TimeSpan duration) { }
		public void Cancel() { }
	}

	public class WPFFlashlight : IFlashlight
	{
		public Task TurnOnAsync() => Task.CompletedTask;
		public Task TurnOffAsync() => Task.CompletedTask;
		public Task<bool> IsSupportedAsync() => Task.FromResult(false);
	}

	public class WPFHapticFeedback : IHapticFeedback
	{
		public bool IsSupported => false;
		public void Perform(HapticFeedbackType type = HapticFeedbackType.Click) { }
	}

	public class WPFPhoneDialer : IPhoneDialer
	{
		public bool IsSupported => false;
		public void Open(string number) { }
	}

	public class WPFSms : ISms
	{
		public bool IsComposeSupported => false;
		public Task ComposeAsync(SmsMessage? message) => Task.CompletedTask;
	}

	public class WPFEmail : IEmail
	{
		public bool IsComposeSupported => true;

		public Task ComposeAsync(EmailMessage? message)
		{
			if (message == null) return Task.CompletedTask;
			var uri = $"mailto:{string.Join(",", message.To ?? Enumerable.Empty<string>())}?subject={Uri.EscapeDataString(message.Subject ?? "")}&body={Uri.EscapeDataString(message.Body ?? "")}";
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
			return Task.CompletedTask;
		}
	}

	public class WPFMap : IMap
	{
		public Task OpenAsync(double latitude, double longitude, MapLaunchOptions options)
		{
			var uri = $"https://www.bing.com/maps?cp={latitude}~{longitude}";
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
			return Task.CompletedTask;
		}

		public Task OpenAsync(Placemark placemark, MapLaunchOptions options)
		{
			var address = Uri.EscapeDataString($"{placemark.Thoroughfare} {placemark.Locality} {placemark.AdminArea} {placemark.CountryName}");
			var uri = $"https://www.bing.com/maps?q={address}";
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
			return Task.CompletedTask;
		}

		public Task<bool> TryOpenAsync(double latitude, double longitude, MapLaunchOptions options)
		{
			OpenAsync(latitude, longitude, options);
			return Task.FromResult(true);
		}

		public Task<bool> TryOpenAsync(Placemark placemark, MapLaunchOptions options)
		{
			OpenAsync(placemark, options);
			return Task.FromResult(true);
		}
	}

	public class WPFContacts : IContacts
	{
		public Task<Contact?> PickContactAsync() => Task.FromResult<Contact?>(null);
		public Task<IEnumerable<Contact>> GetAllAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult<IEnumerable<Contact>>(Array.Empty<Contact>());
	}

	public class WPFFilePicker : IFilePicker
	{
		public Task<FileResult?> PickAsync(PickOptions? options = null)
		{
			var dialog = new Microsoft.Win32.OpenFileDialog();
			if (options?.FileTypes != null)
			{
				var extensions = string.Join(";", options.FileTypes.Value.Select(e => $"*{e}"));
				dialog.Filter = $"Allowed files ({extensions})|{extensions}";
			}
			if (dialog.ShowDialog() == true)
				return Task.FromResult<FileResult?>(new FileResult(dialog.FileName));
			return Task.FromResult<FileResult?>(null);
		}

#if NET11_0
		public Task<IEnumerable<FileResult>?> PickMultipleAsync(PickOptions? options = null)
#else
		public Task<IEnumerable<FileResult?>> PickMultipleAsync(PickOptions? options = null)
#endif
		{
			var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
			if (dialog.ShowDialog() == true)
#if NET11_0
				return Task.FromResult<IEnumerable<FileResult>?>(dialog.FileNames.Select(f => new FileResult(f)));
			return Task.FromResult<IEnumerable<FileResult>?>(Array.Empty<FileResult>());
#else
				return Task.FromResult<IEnumerable<FileResult?>>(dialog.FileNames.Select(f => (FileResult?)new FileResult(f)));
			return Task.FromResult<IEnumerable<FileResult?>>(Array.Empty<FileResult?>());
#endif
		}
	}
}
