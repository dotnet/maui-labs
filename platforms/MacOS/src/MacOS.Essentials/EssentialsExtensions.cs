using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
#if !NET11_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
#endif
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
    public static MauiAppBuilder AddMacOSEssentials(this MauiAppBuilder builder)
    {
        builder.Services.TryAddSingleton<IAppInfo, AppInfoImplementation>();
        builder.Services.TryAddSingleton<IDeviceInfo, DeviceInfoImplementation>();
        builder.Services.TryAddSingleton<IConnectivity, ConnectivityImplementation>();
        builder.Services.TryAddSingleton<IBattery, BatteryImplementation>();
        builder.Services.TryAddSingleton<IDeviceDisplay, DeviceDisplayImplementation>();
#if NET11_0_OR_GREATER
        builder.Services.TryAddSingleton<IFileSystem, FileSystemImplementation>();
#else
        RegisterLegacyFileSystem(builder.Services);
#endif
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

        // New implementations
        builder.Services.TryAddSingleton<IEmail, EmailImplementation>();
        builder.Services.TryAddSingleton<IHapticFeedback, HapticFeedbackImplementation>();
        builder.Services.TryAddSingleton<IScreenshot, ScreenshotImplementation>();
#if !NET11_0_OR_GREATER
        builder.Services.TryAddKeyedSingleton<Func<object, Task<IScreenshotResult?>>>(
            "Microsoft.Maui.ViewCapture",
            (services, _) => platformView =>
                ((ScreenshotImplementation)services.GetRequiredService<IScreenshot>())
                    .CapturePlatformAsync(platformView));
        builder.Services.TryAddKeyedSingleton<Func<object, Task<IScreenshotResult?>>>(
            "Microsoft.Maui.WindowCapture",
            (services, _) => platformView =>
                ((ScreenshotImplementation)services.GetRequiredService<IScreenshot>())
                    .CapturePlatformAsync(platformView));
#endif

        // Stubs for APIs not available on macOS desktop
        builder.Services.TryAddSingleton<IPhoneDialer, PhoneDialerImplementation>();
        builder.Services.TryAddSingleton<ISms, SmsImplementation>();
        builder.Services.TryAddSingleton<IFlashlight, FlashlightImplementation>();
        builder.Services.TryAddSingleton<IAccelerometer, AccelerometerImplementation>();
        builder.Services.TryAddSingleton<IGyroscope, GyroscopeImplementation>();
        builder.Services.TryAddSingleton<ICompass, CompassImplementation>();
        builder.Services.TryAddSingleton<IBarometer, BarometerImplementation>();
        builder.Services.TryAddSingleton<IMagnetometer, MagnetometerImplementation>();

#if !NET11_0_OR_GREATER
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Transient<IMauiInitializeService, LegacyEssentialsInitializer>());
#endif
        return builder;
    }

#if !NET11_0_OR_GREATER
    sealed class LegacyEssentialsInitializer : IMauiInitializeService
    {
        public void Initialize(IServiceProvider services)
        {
            Set<IAppInfo>(typeof(AppInfo), services);
            Set<IDeviceInfo>(typeof(DeviceInfo), services);
            Set<IConnectivity>(typeof(Connectivity), services);
            Set<IBattery>(typeof(Battery), services);
            Set<IDeviceDisplay>(typeof(DeviceDisplay), services);
            Set<IFileSystem>(typeof(FileSystem), services);
            Set<IPreferences>(typeof(Preferences), services);
            Set<ISecureStorage>(typeof(SecureStorage), services);
            Set<IFilePicker>(typeof(FilePicker), services);
            Set<IMediaPicker>(typeof(MediaPicker), services);
            Set<ITextToSpeech>(typeof(TextToSpeech), services);
            Set<IClipboard>(typeof(Clipboard), services);
            Set<IBrowser>(typeof(Browser), services);
            Set<IShare>(typeof(Share), services);
            Set<ILauncher>(typeof(Launcher), services);
            Set<IMap>(typeof(ApplicationModel.Map), services);
            Set<IVibration>(typeof(Vibration), services);
            Set<ISemanticScreenReader>(typeof(SemanticScreenReader), services);
            Set<IGeolocation>(typeof(Geolocation), services);
            Set<IEmail>(typeof(Email), services);
            Set<IHapticFeedback>(typeof(HapticFeedback), services);
            Set<IScreenshot>(typeof(Screenshot), services);
            Set<IPhoneDialer>(typeof(PhoneDialer), services);
            Set<ISms>(typeof(Sms), services);
            Set<IFlashlight>(typeof(Flashlight), services);
            Set<IAccelerometer>(typeof(Accelerometer), services);
            Set<IGyroscope>(typeof(Gyroscope), services);
            Set<ICompass>(typeof(Compass), services);
            Set<IBarometer>(typeof(Barometer), services);
            Set<IMagnetometer>(typeof(Magnetometer), services);
            VersionTracking.Track();
        }
    }

    static void RegisterLegacyFileSystem(IServiceCollection services)
    {
        var registration = services.LastOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IFileSystem));

        if (registration?.ImplementationInstance is IFileSystem existing)
        {
            Set(typeof(FileSystem), existing);
            return;
        }

        if (registration is not null)
            return;

        IFileSystem implementation = new FileSystemImplementation();
        services.AddSingleton(implementation);
        Set(typeof(FileSystem), implementation);
    }

    static void Set<T>(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.NonPublicMethods)] Type facade,
        IServiceProvider services)
        where T : class
        => Set(facade, services.GetRequiredService<T>());

    static void Set<T>(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.NonPublicMethods)] Type facade,
        T implementation)
        where T : class
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var setter = facade.GetMethod("SetDefault", flags, [typeof(T)])
            ?? facade.GetMethod("SetCurrent", flags, [typeof(T)])
            ?? throw new MissingMethodException(facade.FullName, "SetDefault/SetCurrent");
        setter.Invoke(null, [implementation]);
    }
#endif
}
