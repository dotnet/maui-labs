using System.IO;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Media;

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

	public class WPFGeolocation : IGeolocation
	{
		public Task<Location?> GetLastKnownLocationAsync() => Task.FromResult<Location?>(null);
		public Task<Location?> GetLocationAsync(GeolocationRequest request, CancellationToken cancelToken = default)
			=> Task.FromResult<Location?>(null);
		public bool IsListeningForeground => false;
		public bool IsEnabled => false;
		public bool StartListeningForeground(GeolocationListeningRequest request) => false;
		public Task<bool> StartListeningForegroundAsync(GeolocationListeningRequest request) => Task.FromResult(false);
		public void StopListeningForeground() { }
		public event EventHandler<GeolocationLocationChangedEventArgs>? LocationChanged;
		public event EventHandler<GeolocationListeningFailedEventArgs>? ListeningFailed;
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

	public class WPFBattery : IBattery
	{
		public double ChargeLevel => 1.0;
		public BatteryState State => BatteryState.Full;
		public BatteryPowerSource PowerSource => BatteryPowerSource.AC;
		public EnergySaverStatus EnergySaverStatus => EnergySaverStatus.Off;
		public event EventHandler<BatteryInfoChangedEventArgs>? BatteryInfoChanged;
		public event EventHandler<EnergySaverStatusChangedEventArgs>? EnergySaverStatusChanged;
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

	public class WPFSemanticScreenReader : Accessibility.ISemanticScreenReader
	{
		public void Announce(string text) { }
	}

	public class WPFAppActions : IAppActions
	{
		public bool IsSupported => false;
		public Task<IEnumerable<AppAction>> GetAsync() => Task.FromResult<IEnumerable<AppAction>>(Array.Empty<AppAction>());
		public Task SetAsync(IEnumerable<AppAction> actions) => Task.CompletedTask;
		public event EventHandler<AppActionEventArgs>? AppActionActivated;
	}

	public class WPFContacts : IContacts
	{
		public Task<Contact?> PickContactAsync() => Task.FromResult<Contact?>(null);
		public Task<IEnumerable<Contact>> GetAllAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult<IEnumerable<Contact>>(Array.Empty<Contact>());
	}

	public class WPFMediaPicker : IMediaPicker
	{
		public bool IsCaptureSupported => false;
		public Task<FileResult?> PickPhotoAsync(MediaPickerOptions? options = null) => Task.FromResult<FileResult?>(null);
		public Task<List<FileResult>> PickPhotosAsync(MediaPickerOptions? options = null) => Task.FromResult(new List<FileResult>());
		public Task<FileResult?> CapturePhotoAsync(MediaPickerOptions? options = null) => Task.FromResult<FileResult?>(null);
		public Task<FileResult?> PickVideoAsync(MediaPickerOptions? options = null) => Task.FromResult<FileResult?>(null);
		public Task<List<FileResult>> PickVideosAsync(MediaPickerOptions? options = null) => Task.FromResult(new List<FileResult>());
		public Task<FileResult?> CaptureVideoAsync(MediaPickerOptions? options = null) => Task.FromResult<FileResult?>(null);
	}

	public class WPFScreenshot : IScreenshot
	{
		public bool IsCaptureSupported => true;

		public Task<IScreenshotResult> CaptureAsync()
		{
			try
			{
				var window = System.Windows.Application.Current?.MainWindow;
				if (window == null) return Task.FromResult<IScreenshotResult>(new WPFScreenshotResult(new MemoryStream()));

				var dpiX = System.Windows.Media.VisualTreeHelper.GetDpi(window).PixelsPerInchX;
				var dpiY = System.Windows.Media.VisualTreeHelper.GetDpi(window).PixelsPerInchY;
				var width = (int)(window.ActualWidth * dpiX / 96.0);
				var height = (int)(window.ActualHeight * dpiY / 96.0);

				if (width <= 0 || height <= 0) return Task.FromResult<IScreenshotResult>(new WPFScreenshotResult(new MemoryStream()));

				var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, dpiX, dpiY, System.Windows.Media.PixelFormats.Pbgra32);
				bitmap.Render(window);

				var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
				encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

				var stream = new MemoryStream();
				encoder.Save(stream);
				stream.Position = 0;

				return Task.FromResult<IScreenshotResult>(new WPFScreenshotResult(stream));
			}
			catch
			{
				return Task.FromResult<IScreenshotResult>(new WPFScreenshotResult(new MemoryStream()));
			}
		}
	}

	public class WPFScreenshotResult : IScreenshotResult
	{
		readonly MemoryStream _stream;

		public WPFScreenshotResult(MemoryStream stream)
		{
			_stream = stream;
			Width = 0;
			Height = 0;
		}

		public int Width { get; }
		public int Height { get; }

		public Task<Stream> OpenReadAsync(ScreenshotFormat format = ScreenshotFormat.Png, int quality = 100)
		{
			_stream.Position = 0;
			return Task.FromResult<Stream>(_stream);
		}

		public Task CopyToAsync(Stream destination, ScreenshotFormat format = ScreenshotFormat.Png, int quality = 100)
		{
			_stream.Position = 0;
			return _stream.CopyToAsync(destination);
		}
	}

	public class WPFTextToSpeech : ITextToSpeech
	{
		public Task<IEnumerable<Locale>> GetLocalesAsync()
		{
			// Return empty locale list (Locale constructor is internal in MAUI 10)
			return Task.FromResult<IEnumerable<Locale>>(Array.Empty<Locale>());
		}

		public async Task SpeakAsync(string text, SpeechOptions? options = null, CancellationToken cancelToken = default)
		{
			if (string.IsNullOrWhiteSpace(text)) return;

			// Use System.Speech.Synthesis.SpeechSynthesizer via reflection to avoid
			// spawning PowerShell (which would be vulnerable to command injection).
			// System.Speech.dll is available on all Windows desktop installations.
			await Task.Run(() =>
			{
				try
				{
					var speechAssembly = System.Reflection.Assembly.Load("System.Speech, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
					var synthType = speechAssembly.GetType("System.Speech.Synthesis.SpeechSynthesizer");
					if (synthType == null) return;

					var synth = Activator.CreateInstance(synthType);
					if (synth == null) return;

					try
					{
						var speakMethod = synthType.GetMethod("Speak", new[] { typeof(string) });
						speakMethod?.Invoke(synth, new object[] { text });
					}
					finally
					{
						(synth as IDisposable)?.Dispose();
					}
				}
				catch { }
			}, cancelToken);
		}
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

		public Task<IEnumerable<FileResult?>> PickMultipleAsync(PickOptions? options = null)
		{
			var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
			if (dialog.ShowDialog() == true)
				return Task.FromResult<IEnumerable<FileResult?>>(dialog.FileNames.Select(f => (FileResult?)new FileResult(f)));
			return Task.FromResult<IEnumerable<FileResult?>>(Array.Empty<FileResult?>());
		}
	}
}
