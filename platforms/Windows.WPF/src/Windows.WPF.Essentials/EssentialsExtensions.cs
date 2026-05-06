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
		public static MauiAppBuilder UseWPFEssentials(this MauiAppBuilder builder)
		{
			// Core services
			builder.Services.TryAddSingleton<IClipboard, WPFClipboard>();
			builder.Services.TryAddSingleton<IConnectivity, WPFConnectivity>();
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

			return builder;
		}
	}
}
