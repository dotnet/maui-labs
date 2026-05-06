using AppKit;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

public static class MacOSMauiContextExtensions
{
    public static MacOSMauiContext MakeApplicationScope(this MacOSMauiContext context, IPlatformApplication platformApp)
    {
        var appContext = new MacOSMauiContext(context.Services);
        appContext.AddSpecific<IPlatformApplication>(platformApp);
        return appContext;
    }

    public static MacOSMauiContext MakeWindowScope(this MacOSMauiContext context, NSWindow platformWindow)
    {
        var windowContext = new MacOSMauiContext(context.Services);
        windowContext.AddWeakSpecific(platformWindow);
        return windowContext;
    }
}
