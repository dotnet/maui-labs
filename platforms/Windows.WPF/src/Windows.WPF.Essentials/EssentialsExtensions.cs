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

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials
{
	public static class EssentialsExtensions
	{
		/// <summary>
		/// Registers the WPF Essentials implementations in dependency injection.
		/// MAUI's Essentials initializer (applied by <c>ConfigureEssentials</c>) assigns each
		/// registration to its static facade (<c>AppInfo.Current</c>, <c>Preferences.Default</c>, ...)
		/// when the app is built, so the static APIs and dependency injection share one instance.
		/// Registrations use TryAdd: an app can register its own implementation before calling
		/// this method, or after it with <c>AddSingleton</c>.
		/// </summary>
		public static MauiAppBuilder UseWPFEssentials(this MauiAppBuilder builder)
		{
			// Core services
			builder.Services.TryAddSingleton<IClipboard, WPFClipboard>();
			builder.Services.TryAddSingleton<IConnectivity, WPFConnectivity>();
			RemoveBackendFallback<IDeviceInfo>(builder.Services);
			builder.Services.TryAddSingleton<IDeviceInfo, WPFDeviceInfo>();
			builder.Services.TryAddSingleton<IDeviceDisplay, WPFDeviceDisplay>();
			builder.Services.TryAddSingleton<IAppInfo, WPFAppInfo>();
			builder.Services.TryAddSingleton<IPreferences, WPFPreferences>();
			builder.Services.TryAddSingleton<ISecureStorage, WPFSecureStorage>();
			builder.Services.TryAddSingleton<IFileSystem, WPFFileSystem>();
			builder.Services.TryAddSingleton<ILauncher, WPFLauncher>();
			builder.Services.TryAddSingleton<IBrowser, WPFBrowser>();
			builder.Services.TryAddSingleton<IShare, WPFShare>();
			builder.Services.TryAddSingleton<IVersionTracking, WPFVersionTracking>();

			// Communication
			builder.Services.TryAddSingleton<IEmail, WPFEmail>();
			builder.Services.TryAddSingleton<IPhoneDialer, WPFPhoneDialer>();
			builder.Services.TryAddSingleton<ISms, WPFSms>();
			builder.Services.TryAddSingleton<IContacts, WPFContacts>();

			// Sensors (stubs)
			builder.Services.TryAddSingleton<IAccelerometer, WPFAccelerometer>();
			builder.Services.TryAddSingleton<IBarometer, WPFBarometer>();
			builder.Services.TryAddSingleton<ICompass, WPFCompass>();
			builder.Services.TryAddSingleton<IGyroscope, WPFGyroscope>();
			builder.Services.TryAddSingleton<IMagnetometer, WPFMagnetometer>();
			builder.Services.TryAddSingleton<IOrientationSensor, WPFOrientationSensor>();
			builder.Services.TryAddSingleton<IGeolocation, WPFGeolocation>();
			builder.Services.TryAddSingleton<IGeocoding, WPFGeocoding>();

			// Device features (stubs)
			builder.Services.TryAddSingleton<IBattery, WPFBattery>();
			builder.Services.TryAddSingleton<IVibration, WPFVibration>();
			builder.Services.TryAddSingleton<IFlashlight, WPFFlashlight>();
			builder.Services.TryAddSingleton<IHapticFeedback, WPFHapticFeedback>();

			// Maps & Navigation
			builder.Services.TryAddSingleton<IMap, WPFMap>();

			// Media
			builder.Services.TryAddSingleton<IMediaPicker, WPFMediaPicker>();
			builder.Services.TryAddSingleton<IScreenshot, WPFScreenshot>();
			builder.Services.TryAddSingleton<ITextToSpeech, WPFTextToSpeech>();
			builder.Services.TryAddSingleton<IFilePicker, WPFFilePicker>();

			// Accessibility
			builder.Services.TryAddSingleton<ISemanticScreenReader, WPFSemanticScreenReader>();

			// App Actions
			builder.Services.TryAddSingleton<IAppActions, WPFAppActions>();

			builder.ConfigureEssentials();

			return builder;
		}

		// UseMauiAppWPF() registers a minimal IDeviceInfo so OnIdiom works without this package.
		// Drop that fallback (any registration implemented in the backend assembly) so the full
		// implementation wins whichever extension runs first; app registrations are kept.
		static void RemoveBackendFallback<TService>(IServiceCollection services)
		{
			var backend = typeof(Microsoft.Maui.Platforms.Windows.WPF.MauiWPFApplication).Assembly;
			var fallbacks = services
				.Where(d => d.ServiceType == typeof(TService) && !d.IsKeyedService && d.ImplementationType?.Assembly == backend)
				.ToList();
			foreach (var fallback in fallbacks)
				services.Remove(fallback);
		}
	}
}
