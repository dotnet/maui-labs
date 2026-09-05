using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Accessibility;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

public static class EssentialsExtensions
{
	/// <summary>
	/// Registers the browser implementations of the MAUI Essentials interfaces.
	/// Remember to call <c>await BrowserEssentials.InitializeAsync()</c> during startup
	/// (before the first Essentials call) to load the JS interop module.
	/// </summary>
	[SupportedOSPlatform("browser")]
	public static IServiceCollection AddBrowserEssentials(this IServiceCollection services)
	{
		// Storage
		services.TryAddSingleton<IPreferences, BrowserPreferences>();
		services.TryAddSingleton<ISecureStorage, BrowserSecureStorage>();
		services.TryAddSingleton<IFileSystem, BrowserFileSystem>();
		services.TryAddSingleton<IFilePicker, BrowserFilePicker>();

		// App model
		services.TryAddSingleton<IAppInfo, BrowserAppInfo>();
		services.TryAddSingleton<IBrowser, BrowserBrowser>();
		services.TryAddSingleton<ILauncher, BrowserLauncher>();
		services.TryAddSingleton<IVersionTracking, BrowserVersionTracking>();
		services.TryAddSingleton<IMap, BrowserMap>();
		services.TryAddSingleton<IAppActions, BrowserAppActions>();

		// Data transfer
		services.TryAddSingleton<IClipboard, BrowserClipboard>();
		services.TryAddSingleton<IShare, BrowserShare>();

		// Communication
		services.TryAddSingleton<IEmail, BrowserEmail>();
		services.TryAddSingleton<IPhoneDialer, BrowserPhoneDialer>();
		services.TryAddSingleton<ISms, BrowserSms>();
		services.TryAddSingleton<IContacts, BrowserContacts>();
		services.TryAddSingleton<IConnectivity, BrowserConnectivity>();

		// Device
		services.TryAddSingleton<IDeviceInfo, BrowserDeviceInfo>();
		services.TryAddSingleton<IDeviceDisplay, BrowserDeviceDisplay>();
		services.TryAddSingleton<IBattery, BrowserBattery>();
		services.TryAddSingleton<IVibration, BrowserVibration>();
		services.TryAddSingleton<IHapticFeedback, BrowserHapticFeedback>();
		services.TryAddSingleton<IFlashlight, BrowserFlashlight>();

		// Sensors
		services.TryAddSingleton<IAccelerometer, BrowserAccelerometer>();
		services.TryAddSingleton<IGyroscope, BrowserGyroscope>();
		services.TryAddSingleton<IOrientationSensor, BrowserOrientationSensor>();
		services.TryAddSingleton<ICompass, BrowserCompass>();
		services.TryAddSingleton<IBarometer, BrowserBarometer>();
		services.TryAddSingleton<IMagnetometer, BrowserMagnetometer>();
		services.TryAddSingleton<IGeolocation, BrowserGeolocation>();
		services.TryAddSingleton<IGeocoding, BrowserGeocoding>();

		// Media
		services.TryAddSingleton<ITextToSpeech, BrowserTextToSpeech>();
		services.TryAddSingleton<IMediaPicker, BrowserMediaPicker>();
		services.TryAddSingleton<IScreenshot, BrowserScreenshot>();

		// Accessibility
		services.TryAddSingleton<ISemanticScreenReader, BrowserSemanticScreenReader>();

		return services;
	}
}
