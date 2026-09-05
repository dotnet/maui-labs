using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

[SupportedOSPlatform("browser")]
internal static partial class BrowserEssentialsInterop
{
	const string Module = BrowserEssentials.ModuleName;

	// Preferences (localStorage)
	[JSImport("prefGet", Module)]
	internal static partial string? PrefGet(string key);

	[JSImport("prefSet", Module)]
	internal static partial void PrefSet(string key, string value);

	[JSImport("prefRemove", Module)]
	internal static partial void PrefRemove(string key);

	[JSImport("prefKeys", Module)]
	internal static partial string[] PrefKeys(string prefix);

	// Secure storage
	[JSImport("secureSet", Module)]
	internal static partial Task SecureSet(string key, string value);

	[JSImport("secureGet", Module)]
	internal static partial Task<string?> SecureGet(string key);

	// Clipboard
	[JSImport("clipboardWriteText", Module)]
	internal static partial Task ClipboardWriteText(string text);

	[JSImport("clipboardReadText", Module)]
	internal static partial Task<string> ClipboardReadText();

	// Connectivity
	[JSImport("isOnline", Module)]
	internal static partial bool IsOnline();

	[JSImport("getConnectionType", Module)]
	internal static partial string GetConnectionType();

	[JSImport("watchConnectivity", Module)]
	internal static partial void WatchConnectivity([JSMarshalAs<JSType.Function<JSType.Boolean>>] Action<bool> callback);

	// Device info
	[JSImport("getDeviceInfo", Module)]
	internal static partial string GetDeviceInfo();

	// Display
	[JSImport("getDisplayInfo", Module)]
	internal static partial string GetDisplayInfo();

	[JSImport("watchDisplay", Module)]
	internal static partial void WatchDisplay([JSMarshalAs<JSType.Function>] Action callback);

	[JSImport("setWakeLock", Module)]
	internal static partial Task<bool> SetWakeLock(bool enabled);

	[JSImport("getWakeLock", Module)]
	internal static partial bool GetWakeLock();

	// App info / theme
	[JSImport("getAppInfo", Module)]
	internal static partial string GetAppInfo();

	[JSImport("prefersDark", Module)]
	internal static partial bool PrefersDark();

	// Geolocation
	[JSImport("geoGetCurrentPosition", Module)]
	internal static partial Task<string> GeoGetCurrentPosition(bool enableHighAccuracy, double timeoutMs);

	[JSImport("geoWatchStart", Module)]
	internal static partial int GeoWatchStart(
		bool enableHighAccuracy,
		[JSMarshalAs<JSType.Function<JSType.String>>] Action<string> callback,
		[JSMarshalAs<JSType.Function<JSType.String>>] Action<string> errorCallback);

	[JSImport("geoWatchStop", Module)]
	internal static partial void GeoWatchStop(int watchId);

	// Battery
	[JSImport("batteryStart", Module)]
	internal static partial Task<string?> BatteryStart([JSMarshalAs<JSType.Function<JSType.String>>] Action<string> callback);

	// Vibration
	[JSImport("vibrationIsSupported", Module)]
	internal static partial bool VibrationIsSupported();

	[JSImport("vibrate", Module)]
	internal static partial void Vibrate(double durationMs);

	// Share
	[JSImport("shareIsSupported", Module)]
	internal static partial bool ShareIsSupported();

	[JSImport("share", Module)]
	internal static partial Task Share(string? title, string? text, string? url);

	[JSImport("shareFiles", Module)]
	internal static partial Task ShareFiles(string? title, string namesJson, string typesJson, string base64Json);

	// Launcher / browser
	[JSImport("openUrl", Module)]
	internal static partial bool OpenUrl(string url);

	[JSImport("navigateTo", Module)]
	internal static partial bool NavigateTo(string url);

	[JSImport("openFileBlob", Module)]
	internal static partial bool OpenFileBlob(string base64, string? contentType, string name);

	// File picker
	[JSImport("pickFiles", Module)]
	internal static partial Task<string> PickFiles(string? accept, bool multiple);

	// Text to speech
	[JSImport("speechGetVoices", Module)]
	internal static partial string SpeechGetVoices();

	[JSImport("speak", Module)]
	internal static partial Task Speak(string text, string? lang, double pitch, double rate, double volume);

	[JSImport("speechCancel", Module)]
	internal static partial void SpeechCancel();

	// Sensors
	[JSImport("sensorIsSupported", Module)]
	internal static partial bool SensorIsSupported(string kind);

	[JSImport("sensorStart", Module)]
	internal static partial Task<bool> SensorStart(string kind, [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> callback);

	[JSImport("sensorStop", Module)]
	internal static partial void SensorStop(string kind);

	// App package files
	[JSImport("fetchAppFile", Module)]
	internal static partial Task<string?> FetchAppFile(string path);

	[JSImport("appFileExists", Module)]
	internal static partial Task<bool> AppFileExists(string path);

	// Screen reader
	[JSImport("announce", Module)]
	internal static partial void Announce(string text);
}
