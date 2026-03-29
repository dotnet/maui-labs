using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Platform.Maui.Linux.Gtk4.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class ApplicationHandler : ElementHandler<IApplication, Gtk.Application>
{
	public static IPropertyMapper<IApplication, ApplicationHandler> Mapper =
		new PropertyMapper<IApplication, ApplicationHandler>(ElementHandler.ElementMapper)
		{
			[nameof(IApplication.UserAppTheme)] = MapAppTheme,
		};

	public static CommandMapper<IApplication, ApplicationHandler> CommandMapper =
		new(ElementCommandMapper)
		{
			[nameof(IApplication.OpenWindow)] = MapOpenWindow,
			[nameof(IApplication.CloseWindow)] = MapCloseWindow,
		};

	public ApplicationHandler() : base(Mapper, CommandMapper)
	{
	}

	protected override Gtk.Application CreatePlatformElement()
	{
		// The Gtk.Application is created by GtkMauiApplication,
		// we use a placeholder since MAUI requires a platform element
		return Gtk.Application.New(null, Gio.ApplicationFlags.DefaultFlags);
	}

	static void MapOpenWindow(ApplicationHandler handler, IApplication app, object? arg)
	{
		GtkMauiApplication.Current.CreateAndShowNewWindow(app, arg as OpenWindowRequest);
	}

	static void MapCloseWindow(ApplicationHandler handler, IApplication app, object? arg)
	{
		if (arg is not IWindow virtualWindow) return;
		GtkMauiApplication.Current.CloseWindow(virtualWindow);
	}

	static void MapAppTheme(ApplicationHandler handler, IApplication app)
	{
		if (app.UserAppTheme == AppTheme.Unspecified)
			GtkThemeManager.RevertToSystemTheme();
		else
			GtkThemeManager.SetTheme(app.UserAppTheme);

		// Defer ThemeChanged so GTK has time to apply the new theme CSS
		GLib.Functions.IdleAdd(0, () =>
		{
			app.ThemeChanged();
			return false;
		});
	}
}
