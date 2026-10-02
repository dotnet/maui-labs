using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Accessibility;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Media;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms;

// Compiled into each backend, so the shipping packages need no additional dependency.
internal sealed class EssentialsInitializer : IMauiInitializeService
{
    public void Initialize(IServiceProvider services)
    {
        Set<IAppInfo>(typeof(AppInfo), services);
        Set<IPreferences>(typeof(Preferences), services);
        Set<IFileSystem>(typeof(FileSystem), services);
        Set<IDeviceInfo>(typeof(DeviceInfo), services);
        Set<IDeviceDisplay>(typeof(DeviceDisplay), services);
        Set<IConnectivity>(typeof(Connectivity), services);
        Set<IBattery>(typeof(Battery), services);
        Set<ISecureStorage>(typeof(SecureStorage), services);
        Set<IFilePicker>(typeof(FilePicker), services);
        Set<IMediaPicker>(typeof(MediaPicker), services);
        Set<IScreenshot>(typeof(Screenshot), services);
        Set<ITextToSpeech>(typeof(TextToSpeech), services);
        Set<IClipboard>(typeof(Clipboard), services);
        Set<IBrowser>(typeof(Browser), services);
        Set<IShare>(typeof(Share), services);
        Set<ILauncher>(typeof(Launcher), services);
        Set<IMap>(typeof(Map), services);
        Set<IVersionTracking>(typeof(VersionTracking), services);
        Set<IEmail>(typeof(Email), services);
        Set<IPhoneDialer>(typeof(PhoneDialer), services);
        Set<ISms>(typeof(Sms), services);
        Set<IContacts>(typeof(Contacts), services);
        Set<IAccelerometer>(typeof(Accelerometer), services);
        Set<IBarometer>(typeof(Barometer), services);
        Set<ICompass>(typeof(Compass), services);
        Set<IGyroscope>(typeof(Gyroscope), services);
        Set<IMagnetometer>(typeof(Magnetometer), services);
        Set<IOrientationSensor>(typeof(OrientationSensor), services);
        Set<IGeolocation>(typeof(Geolocation), services);
        Set<IGeocoding>(typeof(Geocoding), services);
        Set<IFlashlight>(typeof(Flashlight), services);
        Set<IHapticFeedback>(typeof(HapticFeedback), services);
        Set<IVibration>(typeof(Vibration), services);
        Set<ISemanticScreenReader>(typeof(SemanticScreenReader), services);
        Set<IAppActions>(typeof(AppActions), services);

        // GTK implements web authentication; WPF does not.
        if (services.GetService<IWebAuthenticator>() is { } authenticator)
            Set(typeof(WebAuthenticator), authenticator);
    }

    static void Set<T>(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.NonPublicMethods)] Type facade,
        IServiceProvider services) where T : class
        => Set(facade, services.GetRequiredService<T>());

    static void Set<T>(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.NonPublicMethods)] Type facade,
        T implementation) where T : class
    {
        // MAUI has no public backend registration API. Preserve its internal setters
        // when trimming, and fail explicitly if a future MAUI version changes them.
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var setter = facade.GetMethod("SetDefault", flags, [typeof(T)])
            ?? facade.GetMethod("SetCurrent", flags, [typeof(T)])
            ?? throw new MissingMethodException(facade.FullName, "SetDefault/SetCurrent");
        setter.Invoke(null, [implementation]);
    }
}
