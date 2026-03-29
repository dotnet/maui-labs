using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Hosting;
using System.Reflection;
using Platform.Maui.Linux.Gtk4.Essentials.AppModel;
using Platform.Maui.Linux.Gtk4.Essentials.Communication;
using Platform.Maui.Linux.Gtk4.Essentials.DataTransfer;
using Platform.Maui.Linux.Gtk4.Essentials.Devices;
using Platform.Maui.Linux.Gtk4.Essentials.Sensors;
using Platform.Maui.Linux.Gtk4.Essentials.Media;
using Platform.Maui.Linux.Gtk4.Essentials.Networking;
using Platform.Maui.Linux.Gtk4.Essentials.Storage;
using Platform.Maui.Linux.Gtk4.Essentials.Accessibility;
using Platform.Maui.Linux.Gtk4.Essentials.Authentication;

namespace Platform.Maui.Linux.Gtk4.Essentials.Hosting;

public static class EssentialsHostBuilderExtensions
{
	public static MauiAppBuilder AddLinuxGtk4Essentials(this MauiAppBuilder builder)
	{
		// Tier 1 — Pure .NET
		builder.Services.TryAddSingleton<Microsoft.Maui.ApplicationModel.IAppInfo, LinuxAppInfo>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Devices.IDeviceInfo, LinuxDeviceInfo>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Storage.IFileSystem, LinuxFileSystem>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Storage.IPreferences, LinuxPreferences>();
		builder.Services.TryAddSingleton<Microsoft.Maui.ApplicationModel.IVersionTracking, LinuxVersionTracking>();
		builder.Services.TryAddSingleton<Microsoft.Maui.ApplicationModel.ILauncher, LinuxLauncher>();
		builder.Services.TryAddSingleton<Microsoft.Maui.ApplicationModel.IBrowser, LinuxBrowser>();
		builder.Services.TryAddSingleton<Microsoft.Maui.ApplicationModel.IMap, LinuxMap>();
		builder.Services.TryAddSingleton<Microsoft.Maui.ApplicationModel.Communication.IEmail, LinuxEmail>();

		// Tier 2 — GTK4
		builder.Services.TryAddSingleton<Microsoft.Maui.ApplicationModel.DataTransfer.IClipboard, LinuxClipboard>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Devices.IDeviceDisplay, LinuxDeviceDisplay>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Storage.IFilePicker, LinuxFilePicker>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Media.IMediaPicker, LinuxMediaPicker>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Media.IScreenshot, LinuxScreenshot>();

		// Tier 3 — DBus
		builder.Services.TryAddSingleton<Microsoft.Maui.Devices.IBattery, LinuxBattery>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Networking.IConnectivity, LinuxConnectivity>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Storage.ISecureStorage, LinuxSecureStorage>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Devices.Sensors.IGeolocation, LinuxGeolocation>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Media.ITextToSpeech, LinuxTextToSpeech>();

		// Tier 4 — Best-effort
		builder.Services.TryAddSingleton<Microsoft.Maui.ApplicationModel.DataTransfer.IShare, LinuxShare>();
		builder.Services.TryAddSingleton<Microsoft.Maui.ApplicationModel.IAppActions, LinuxAppActions>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Authentication.IWebAuthenticator, LinuxWebAuthenticator>();

		// Tier 5 — Stubs
		builder.Services.TryAddSingleton<Microsoft.Maui.Devices.IFlashlight, LinuxFlashlight>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Devices.IHapticFeedback, LinuxHapticFeedback>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Devices.IVibration, LinuxVibration>();
		builder.Services.TryAddSingleton<Microsoft.Maui.ApplicationModel.Communication.IPhoneDialer, LinuxPhoneDialer>();
		builder.Services.TryAddSingleton<Microsoft.Maui.ApplicationModel.Communication.ISms, LinuxSms>();
		builder.Services.TryAddSingleton<Microsoft.Maui.ApplicationModel.Communication.IContacts, LinuxContacts>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Devices.Sensors.IAccelerometer, LinuxAccelerometer>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Devices.Sensors.IBarometer, LinuxBarometer>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Devices.Sensors.ICompass, LinuxCompass>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Devices.Sensors.IGyroscope, LinuxGyroscope>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Devices.Sensors.IMagnetometer, LinuxMagnetometer>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Devices.Sensors.IOrientationSensor, LinuxOrientationSensor>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Devices.Sensors.IGeocoding, LinuxGeocoding>();
		builder.Services.TryAddSingleton<Microsoft.Maui.Accessibility.ISemanticScreenReader, LinuxSemanticScreenReader>();

		// Wire static Essentials APIs (Preferences.Default, FilePicker.Default, etc.)
		SetEssentialsDefaults();

		return builder;
	}

	private static void SetEssentialsDefaults()
	{
		SetDefault(typeof(Microsoft.Maui.Storage.Preferences), new LinuxPreferences());
		SetDefault(typeof(Microsoft.Maui.Storage.FilePicker), new LinuxFilePicker());
		SetDefault(typeof(Microsoft.Maui.Storage.SecureStorage), new LinuxSecureStorage());
		SetDefault(typeof(Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard), new LinuxClipboard());
		SetDefault(typeof(Microsoft.Maui.Media.MediaPicker), new LinuxMediaPicker());
	}

	private static void SetDefault(Type essentialsType, object implementation)
	{
		var setDefault = essentialsType.GetMethod("SetDefault", BindingFlags.Static | BindingFlags.NonPublic);
		setDefault?.Invoke(null, new[] { implementation });
	}
}
