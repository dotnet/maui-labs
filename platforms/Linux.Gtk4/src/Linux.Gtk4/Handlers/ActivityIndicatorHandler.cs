using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;

public class ActivityIndicatorHandler : GtkViewHandler<IActivityIndicator, Gtk.Spinner>
{
	public static IPropertyMapper<IActivityIndicator, ActivityIndicatorHandler> Mapper =
		new PropertyMapper<IActivityIndicator, ActivityIndicatorHandler>(ViewMapper)
		{
			[nameof(IActivityIndicator.IsRunning)] = MapIsRunning,
			[nameof(IActivityIndicator.Color)] = MapColor,
		};

	public ActivityIndicatorHandler() : base(Mapper)
	{
	}

	protected override Gtk.Spinner CreatePlatformView()
	{
		return Gtk.Spinner.New();
	}

	public static void MapIsRunning(ActivityIndicatorHandler handler, IActivityIndicator indicator)
	{
		handler.PlatformView?.SetSpinning(indicator.IsRunning);
	}

	public static void MapColor(ActivityIndicatorHandler handler, IActivityIndicator indicator)
	{
		if (indicator.Color != null)
			handler.ApplyCss(handler.PlatformView, $"color: {ToGtkColor(indicator.Color)};");
	}
}
