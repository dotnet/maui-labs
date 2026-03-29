using Microsoft.Maui.Devices.Sensors;

namespace Platform.Maui.Linux.Gtk4.Essentials.Sensors;

public class LinuxGeolocation : IGeolocation
{
	private bool _isListening;
	private EventHandler<GeolocationLocationChangedEventArgs>? _locationChanged;
	private EventHandler<GeolocationListeningFailedEventArgs>? _listeningFailed;

	public bool IsListeningForeground => _isListening;
	public bool IsEnabled => File.Exists("/usr/libexec/geoclue") || File.Exists("/usr/lib/geoclue-2.0/geoclue");

	public Task<Location?> GetLastKnownLocationAsync() => Task.FromResult<Location?>(null);

	public async Task<Location?> GetLocationAsync(GeolocationRequest request, CancellationToken cancelToken)
	{
		// Attempt to read from GeoClue via CLI, or return null
		try
		{
			var psi = new System.Diagnostics.ProcessStartInfo("where-am-i")
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			using var process = System.Diagnostics.Process.Start(psi);
			if (process is null) return null;

			var output = await process.StandardOutput.ReadToEndAsync(cancelToken);
			await process.WaitForExitAsync(cancelToken);

			// Parse GeoClue where-am-i output
			double? lat = null, lon = null;
			foreach (var line in output.Split('\n'))
			{
				var trimmed = line.Trim();
				if (trimmed.StartsWith("Latitude:", StringComparison.OrdinalIgnoreCase))
					lat = double.TryParse(trimmed.Split(':')[1].Trim(), System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
				else if (trimmed.StartsWith("Longitude:", StringComparison.OrdinalIgnoreCase))
					lon = double.TryParse(trimmed.Split(':')[1].Trim(), System.Globalization.CultureInfo.InvariantCulture, out var v2) ? v2 : null;
			}

			if (lat.HasValue && lon.HasValue)
				return new Location(lat.Value, lon.Value);
		}
		catch { }
		return null;
	}

	public Task<bool> StartListeningForegroundAsync(GeolocationListeningRequest request)
	{
		_isListening = true;
		return Task.FromResult(true);
	}

	public void StopListeningForeground()
	{
		_isListening = false;
	}

	public event EventHandler<GeolocationLocationChangedEventArgs>? LocationChanged
	{
		add => _locationChanged += value;
		remove => _locationChanged -= value;
	}

	public event EventHandler<GeolocationListeningFailedEventArgs>? ListeningFailed
	{
		add => _listeningFailed += value;
		remove => _listeningFailed -= value;
	}
}
