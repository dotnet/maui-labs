using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>Geolocation backed by navigator.geolocation (permission-prompted by the browser).</summary>
[SupportedOSPlatform("browser")]
public class BrowserGeolocation : IGeolocation
{
	Location? lastKnownLocation;
	int watchId = -1;

	public bool IsListeningForeground => watchId >= 0;

	// Browsers only reveal permission state at request time; assume available.
	public bool IsEnabled => true;

	public event EventHandler<GeolocationLocationChangedEventArgs>? LocationChanged;

	public event EventHandler<GeolocationListeningFailedEventArgs>? ListeningFailed;

	public Task<Location?> GetLastKnownLocationAsync() => Task.FromResult(lastKnownLocation);

	public async Task<Location?> GetLocationAsync(GeolocationRequest request, CancellationToken cancelToken = default)
	{
		await BrowserEssentials.WhenInitializedAsync().ConfigureAwait(false);
		cancelToken.ThrowIfCancellationRequested();
		try
		{
			var json = await BrowserEssentialsInterop.GeoGetCurrentPosition(
				UseHighAccuracy(request), request.Timeout.TotalMilliseconds).ConfigureAwait(false);
			return lastKnownLocation = ParseLocation(json);
		}
		catch (Exception ex) when (ex.Message.Contains("permission", StringComparison.OrdinalIgnoreCase))
		{
			throw new PermissionException("Geolocation permission was denied by the user or browser policy.");
		}
		catch (Exception ex) when (ex.Message.Contains("unsupported", StringComparison.OrdinalIgnoreCase))
		{
			throw new FeatureNotSupportedException("Geolocation is not supported by this browser.");
		}
	}

	public async Task<bool> StartListeningForegroundAsync(GeolocationListeningRequest request)
	{
		await BrowserEssentials.WhenInitializedAsync().ConfigureAwait(false);
		if (IsListeningForeground)
			throw new InvalidOperationException("Already listening for location changes.");

		watchId = BrowserEssentialsInterop.GeoWatchStart(
			request.DesiredAccuracy is GeolocationAccuracy.Best or GeolocationAccuracy.High,
			json =>
			{
				var location = ParseLocation(json);
				lastKnownLocation = location;
				LocationChanged?.Invoke(this, new GeolocationLocationChangedEventArgs(location));
			},
			error => ListeningFailed?.Invoke(this, new GeolocationListeningFailedEventArgs(
				error.Contains("permission", StringComparison.OrdinalIgnoreCase)
					? GeolocationError.Unauthorized
					: GeolocationError.PositionUnavailable)));
		return watchId >= 0;
	}

	public void StopListeningForeground()
	{
		if (!IsListeningForeground)
			return;
		BrowserEssentials.EnsureInitialized();
		BrowserEssentialsInterop.GeoWatchStop(watchId);
		watchId = -1;
	}

	static bool UseHighAccuracy(GeolocationRequest request) =>
		request.DesiredAccuracy is GeolocationAccuracy.Best or GeolocationAccuracy.High;

	static Location ParseLocation(string json)
	{
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		var location = new Location(
			root.GetProperty("latitude").GetDouble(),
			root.GetProperty("longitude").GetDouble())
		{
			Accuracy = GetNullableDouble(root, "accuracy"),
			Altitude = GetNullableDouble(root, "altitude"),
			VerticalAccuracy = GetNullableDouble(root, "altitudeAccuracy"),
			Course = GetNullableDouble(root, "heading"),
			Speed = GetNullableDouble(root, "speed"),
			Timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)root.GetProperty("timestamp").GetDouble()),
		};
		return location;
	}

	static double? GetNullableDouble(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
			? value.GetDouble()
			: null;
}
