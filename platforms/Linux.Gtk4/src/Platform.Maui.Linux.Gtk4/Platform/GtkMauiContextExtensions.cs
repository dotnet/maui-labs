using Microsoft.Maui;

namespace Platform.Maui.Linux.Gtk4.Platform;

public static class GtkMauiContextExtensions
{
	public static GtkMauiContext MakeApplicationScope(this GtkMauiContext context, IPlatformApplication platformApp)
	{
		var appContext = new GtkMauiContext(context.Services);
		appContext.AddSpecific<IPlatformApplication>(platformApp);
		return appContext;
	}

	public static GtkMauiContext MakeWindowScope(this GtkMauiContext context, Gtk.Window platformWindow)
	{
		var windowContext = new GtkMauiContext(context.Services);
		windowContext.AddWeakSpecific(platformWindow);
		return windowContext;
	}
}
