using Foundation;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Microsoft.Maui.Platforms.MacOS.Platform;
using AppKit;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class ApplicationHandler : ElementHandler<IApplication, NSObject>
{
    public static readonly IPropertyMapper<IApplication, ApplicationHandler> Mapper =
        new PropertyMapper<IApplication, ApplicationHandler>(ElementMapper)
        {
            [nameof(IApplication.UserAppTheme)] = MapAppTheme,
        };

    public static readonly CommandMapper<IApplication, ApplicationHandler> CommandMapper =
        new(ElementCommandMapper)
        {
            [nameof(IApplication.OpenWindow)] = MapOpenWindow,
            [nameof(IApplication.CloseWindow)] = MapCloseWindow,
        };

    public ApplicationHandler() : base(Mapper, CommandMapper)
    {
    }

    protected override NSObject CreatePlatformElement()
    {
        return new NSObject();
    }

    public static void MapOpenWindow(ApplicationHandler handler, IApplication application, object? args)
    {
        if (IPlatformApplication.Current is not MacOSMauiApplication macApp)
            return;

        var appContext = macApp.ApplicationContext;
        if (appContext == null)
            return;

        // Defer window creation to the next run loop iteration so the current
        // UI event (e.g. button click) completes before focus is stolen by the new window.
        var request = args as OpenWindowRequest;
        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            macApp.CreatePlatformWindow(appContext, request);
        });
    }

    public static void MapCloseWindow(ApplicationHandler handler, IApplication application, object? args)
    {
        if (args is not IWindow window)
            return;

        if (window.Handler?.PlatformView is NSWindow nsWindow)
        {
            nsWindow.Close();
        }
    }

    public static void MapAppTheme(ApplicationHandler handler, IApplication application)
    {
        // Override the app-wide appearance to match the requested theme.
        // Setting Appearance to null reverts to the system default.
        NSApplication.SharedApplication.Appearance = application.UserAppTheme switch
        {
            AppTheme.Light => NSAppearance.GetAppearance(NSAppearance.NameAqua),
            AppTheme.Dark => NSAppearance.GetAppearance(NSAppearance.NameDarkAqua),
            _ => null,
        };

        // Defer ThemeChanged to the next runloop iteration so AppKit has time
        // to update EffectiveAppearance after we clear the override.
        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            application.ThemeChanged();
        });
    }
}
