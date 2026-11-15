using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Accessibility;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Media;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

public static class EssentialsExtensions
{
    /// <summary>
    /// Registers the macOS AppKit Essentials implementations in dependency injection.
    /// MAUI's Essentials initializer (applied by <c>ConfigureEssentials</c>) assigns each
    /// registration to its static facade (<c>AppInfo.Current</c>, <c>Preferences.Default</c>, ...)
    /// when the app is built, so the static APIs and dependency injection share one instance.
    /// Registrations use TryAdd: an app can register its own implementation before calling
    /// this method, or after it with <c>AddSingleton</c>.
    /// </summary>
    public static MauiAppBuilder AddMacOSEssentials(this MauiAppBuilder builder)
    {
        builder.Services.TryAddSingleton<IAppInfo, AppInfoImplementation>();
        builder.Services.TryAddSingleton<IDeviceInfo, DeviceInfoImplementation>();
        builder.Services.TryAddSingleton<IConnectivity, ConnectivityImplementation>();
        builder.Services.TryAddSingleton<IBattery, BatteryImplementation>();
        builder.Services.TryAddSingleton<IDeviceDisplay, DeviceDisplayImplementation>();
        builder.Services.TryAddSingleton<IFileSystem, FileSystemImplementation>();
        builder.Services.TryAddSingleton<IPreferences, PreferencesImplementation>();
        builder.Services.TryAddSingleton<ISecureStorage, SecureStorageImplementation>();
        builder.Services.TryAddSingleton<IFilePicker, FilePickerImplementation>();
        builder.Services.TryAddSingleton<IMediaPicker, MediaPickerImplementation>();
        builder.Services.TryAddSingleton<ITextToSpeech, TextToSpeechImplementation>();
        builder.Services.TryAddSingleton<IClipboard, ClipboardImplementation>();
        builder.Services.TryAddSingleton<IBrowser, BrowserImplementation>();
        builder.Services.TryAddSingleton<IShare, ShareImplementation>();
        builder.Services.TryAddSingleton<ILauncher, LauncherImplementation>();
        builder.Services.TryAddSingleton<IMap, MapImplementation>();
        builder.Services.TryAddSingleton<IVibration, VibrationImplementation>();
        builder.Services.TryAddSingleton<ISemanticScreenReader, SemanticScreenReaderImplementation>();
        builder.Services.TryAddSingleton<IGeolocation, GeolocationImplementation>();
        builder.Services.TryAddSingleton<IEmail, EmailImplementation>();
        builder.Services.TryAddSingleton<IHapticFeedback, HapticFeedbackImplementation>();
        builder.Services.TryAddSingleton<IScreenshot, ScreenshotImplementation>();

        // Stubs for APIs not available on macOS desktop
        builder.Services.TryAddSingleton<IPhoneDialer, PhoneDialerImplementation>();
        builder.Services.TryAddSingleton<ISms, SmsImplementation>();
        builder.Services.TryAddSingleton<IFlashlight, FlashlightImplementation>();
        builder.Services.TryAddSingleton<IAccelerometer, AccelerometerImplementation>();
        builder.Services.TryAddSingleton<IGyroscope, GyroscopeImplementation>();
        builder.Services.TryAddSingleton<ICompass, CompassImplementation>();
        builder.Services.TryAddSingleton<IBarometer, BarometerImplementation>();
        builder.Services.TryAddSingleton<IMagnetometer, MagnetometerImplementation>();

        // No IVersionTracking registration: MAUI backs VersionTracking with its own
        // implementation over the registered IPreferences and IAppInfo. UseVersionTracking()
        // records the current launch when the app is built, as the previous eager Track() did.
        builder.ConfigureEssentials(essentials => essentials.UseVersionTracking());

        return builder;
    }
}
