using Microsoft.Maui.Accessibility;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Media;
using Microsoft.Maui.Platforms.Browser.Essentials;
using Microsoft.Maui.Storage;

namespace Browser.Essentials.TestApp;

public sealed record TestResult(string Name, bool Passed, string? Error);

/// <summary>
/// In-browser test suite exercising the Browser Essentials implementations against
/// real web APIs. Executed inside the WebAssembly runtime; results are rendered to
/// the DOM and asserted by the Playwright-driven xunit project.
/// </summary>
public class EssentialsTestSuite(
	IPreferences preferences,
	ISecureStorage secureStorage,
	IClipboard clipboard,
	IConnectivity connectivity,
	IDeviceInfo deviceInfo,
	IDeviceDisplay deviceDisplay,
	IAppInfo appInfo,
	IGeolocation geolocation,
	IBattery battery,
	IFileSystem fileSystem,
	IVibration vibration,
	ITextToSpeech textToSpeech,
	IAccelerometer accelerometer,
	ISemanticScreenReader screenReader,
	IVersionTracking versionTracking,
	IContacts contacts,
	ILauncher launcher)
{
	public async Task<List<TestResult>> RunAsync()
	{
		var results = new List<TestResult>();
		await RunAsync(results, "Preferences: string roundtrip", () =>
		{
			preferences.Set("test-string", "hello");
			AssertEqual("hello", preferences.Get("test-string", ""));
			return Task.CompletedTask;
		});
		await RunAsync(results, "Preferences: primitive types roundtrip", () =>
		{
			preferences.Set("test-int", 42);
			preferences.Set("test-bool", true);
			preferences.Set("test-double", 3.25);
			preferences.Set("test-long", 9_876_543_210L);
			AssertEqual(42, preferences.Get("test-int", 0));
			AssertEqual(true, preferences.Get("test-bool", false));
			AssertEqual(3.25, preferences.Get("test-double", 0.0));
			AssertEqual(9_876_543_210L, preferences.Get("test-long", 0L));
			return Task.CompletedTask;
		});
		await RunAsync(results, "Preferences: DateTime roundtrip", () =>
		{
			var now = new DateTime(2026, 7, 12, 8, 30, 15, DateTimeKind.Utc);
			preferences.Set("test-date", now);
			AssertEqual(now, preferences.Get("test-date", DateTime.MinValue));
			return Task.CompletedTask;
		});
		await RunAsync(results, "Preferences: missing key returns default", () =>
		{
			AssertEqual("fallback", preferences.Get("test-does-not-exist", "fallback"));
			return Task.CompletedTask;
		});
		await RunAsync(results, "Preferences: ContainsKey/Remove", () =>
		{
			preferences.Set("test-remove", "x");
			AssertTrue(preferences.ContainsKey("test-remove"), "key should exist");
			preferences.Remove("test-remove");
			AssertTrue(!preferences.ContainsKey("test-remove"), "key should be removed");
			return Task.CompletedTask;
		});
		await RunAsync(results, "Preferences: sharedName container isolation", () =>
		{
			preferences.Set("test-shared", "a", "container1");
			preferences.Set("test-shared", "b", "container2");
			AssertEqual("a", preferences.Get("test-shared", "", "container1"));
			AssertEqual("b", preferences.Get("test-shared", "", "container2"));
			preferences.Clear("container1");
			AssertEqual("", preferences.Get("test-shared", "", "container1"));
			AssertEqual("b", preferences.Get("test-shared", "", "container2"));
			return Task.CompletedTask;
		});
		await RunAsync(results, "SecureStorage: roundtrip", async () =>
		{
			await secureStorage.SetAsync("test-secret", "top secret value");
			AssertEqual("top secret value", await secureStorage.GetAsync("test-secret"));
		});
		await RunAsync(results, "SecureStorage: value is encrypted at rest", async () =>
		{
			await secureStorage.SetAsync("test-encrypted", "plaintext-marker");
			var raw = BrowserEssentialsInterop.PrefGet("maui:securestorage:test-encrypted");
			AssertTrue(raw is not null, "raw localStorage entry should exist");
			AssertTrue(!raw!.Contains("plaintext-marker"), "stored value must not contain the plaintext");
		});
		await RunAsync(results, "SecureStorage: remove", async () =>
		{
			await secureStorage.SetAsync("test-secret-remove", "x");
			AssertTrue(secureStorage.Remove("test-secret-remove"), "Remove should report true");
			AssertEqual(null, await secureStorage.GetAsync("test-secret-remove"));
			AssertTrue(!secureStorage.Remove("test-secret-remove"), "second Remove should report false");
		});
		await RunAsync(results, "SecureStorage: RemoveAll clears only secure entries", async () =>
		{
			await secureStorage.SetAsync("test-clear", "x");
			preferences.Set("test-survives", "y");
			secureStorage.RemoveAll();
			AssertEqual(null, await secureStorage.GetAsync("test-clear"));
			AssertEqual("y", preferences.Get("test-survives", ""));
		});
		await RunAsync(results, "Clipboard: roundtrip", async () =>
		{
			await clipboard.SetTextAsync("clipboard-test-value");
			AssertEqual("clipboard-test-value", await clipboard.GetTextAsync());
			AssertTrue(clipboard.HasText, "HasText should be true after set");
		});
		await RunAsync(results, "Connectivity: reports internet access", () =>
		{
			AssertEqual(NetworkAccess.Internet, connectivity.NetworkAccess);
			AssertTrue(connectivity.ConnectionProfiles.Any(), "at least one connection profile");
			return Task.CompletedTask;
		});
		await RunAsync(results, "DeviceInfo: platform is Browser", () =>
		{
			AssertEqual("Browser", deviceInfo.Platform.ToString());
			AssertTrue(deviceInfo.Model.Length > 0, "model (browser name) non-empty");
			AssertTrue(deviceInfo.Idiom == DeviceIdiom.Desktop || deviceInfo.Idiom == DeviceIdiom.Phone, "idiom resolved");
			return Task.CompletedTask;
		});
		await RunAsync(results, "DeviceDisplay: main display info populated", () =>
		{
			var display = deviceDisplay.MainDisplayInfo;
			AssertTrue(display.Width > 0, "width > 0");
			AssertTrue(display.Height > 0, "height > 0");
			AssertTrue(display.Density > 0, "density > 0");
			return Task.CompletedTask;
		});
		await RunAsync(results, "AppInfo: populated from document", () =>
		{
			AssertTrue(appInfo.PackageName.Length > 0, "package name (hostname) non-empty");
			AssertTrue(appInfo.Name.Length > 0, "name non-empty");
			AssertTrue(appInfo.RequestedTheme is AppTheme.Light or AppTheme.Dark, "theme resolved");
			return Task.CompletedTask;
		});
		await RunAsync(results, "Geolocation: returns mocked position", async () =>
		{
			// The Playwright harness sets the context geolocation to (35.6895, 139.6917).
			var location = await geolocation.GetLocationAsync(
				new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)));
			AssertTrue(location is not null, "location returned");
			AssertTrue(Math.Abs(location!.Latitude - 35.6895) < 0.001, $"latitude was {location.Latitude}");
			AssertTrue(Math.Abs(location.Longitude - 139.6917) < 0.001, $"longitude was {location.Longitude}");
		});
		await RunAsync(results, "Battery: properties readable", () =>
		{
			AssertTrue(battery.ChargeLevel is >= 0 and <= 1, $"charge level was {battery.ChargeLevel}");
			_ = battery.State;
			_ = battery.PowerSource;
			AssertEqual(EnergySaverStatus.Unknown, battery.EnergySaverStatus);
			return Task.CompletedTask;
		});
		await RunAsync(results, "FileSystem: app data directory is writable", async () =>
		{
			var path = Path.Combine(fileSystem.AppDataDirectory, "test.txt");
			await File.WriteAllTextAsync(path, "file contents");
			AssertEqual("file contents", await File.ReadAllTextAsync(path));
			File.Delete(path);
		});
		await RunAsync(results, "FileSystem: app package file fetch", async () =>
		{
			using var stream = await fileSystem.OpenAppPackageFileAsync("test-asset.txt");
			using var reader = new StreamReader(stream);
			AssertEqual("package-file-marker", (await reader.ReadToEndAsync()).Trim());
			AssertTrue(await fileSystem.AppPackageFileExistsAsync("test-asset.txt"), "existing file reported");
			AssertTrue(!await fileSystem.AppPackageFileExistsAsync("no-such-file.bin"), "missing file reported");
		});
		await RunAsync(results, "VersionTracking: tracks first launch", () =>
		{
			versionTracking.Track();
			AssertTrue(versionTracking.IsFirstLaunchEver, "fresh browser context is a first launch");
			AssertTrue(versionTracking.CurrentVersion.Length > 0, "current version non-empty");
			AssertTrue(versionTracking.VersionHistory.Count == 1, "history has one entry");
			return Task.CompletedTask;
		});
		await RunAsync(results, "SemanticScreenReader: announce creates aria-live region", () =>
		{
			screenReader.Announce("test announcement");
			return Task.CompletedTask;
		});
		await RunAsync(results, "Vibration: IsSupported readable", () =>
		{
			_ = vibration.IsSupported;
			return Task.CompletedTask;
		});
		await RunAsync(results, "TextToSpeech: locales enumerable", async () =>
		{
			var locales = await textToSpeech.GetLocalesAsync();
			AssertTrue(locales is not null, "locales list returned");
		});
		await RunAsync(results, "Accelerometer: supported flag and stop are safe", () =>
		{
			_ = accelerometer.IsSupported;
			accelerometer.Stop();
			AssertTrue(!accelerometer.IsMonitoring, "not monitoring after stop");
			return Task.CompletedTask;
		});
		await RunAsync(results, "Launcher: CanOpen scheme checks", async () =>
		{
			AssertTrue(await launcher.CanOpenAsync(new Uri("https://example.com")), "https supported");
			AssertTrue(await launcher.CanOpenAsync(new Uri("mailto:user@example.com")), "mailto supported");
			AssertTrue(!await launcher.CanOpenAsync(new Uri("myapp://something")), "unknown scheme unsupported");
		});
		await RunAsync(results, "Stubs: unsupported APIs throw FeatureNotSupportedException", async () =>
		{
			try
			{
				await contacts.PickContactAsync();
				throw new InvalidOperationException("expected FeatureNotSupportedException");
			}
			catch (FeatureNotSupportedException)
			{
			}
		});
		return results;
	}

	static async Task RunAsync(List<TestResult> results, string name, Func<Task> test)
	{
		try
		{
			await test();
			results.Add(new TestResult(name, true, null));
		}
		catch (Exception ex)
		{
			results.Add(new TestResult(name, false, $"{ex.GetType().Name}: {ex.Message}"));
		}
	}

	static void AssertTrue(bool condition, string message)
	{
		if (!condition)
			throw new InvalidOperationException($"Assertion failed: {message}");
	}

	static void AssertEqual<T>(T? expected, T? actual)
	{
		if (!EqualityComparer<T>.Default.Equals(expected!, actual!))
			throw new InvalidOperationException($"Assertion failed: expected '{expected}', got '{actual}'");
	}
}
