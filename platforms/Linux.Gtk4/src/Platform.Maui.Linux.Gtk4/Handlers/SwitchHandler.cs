using Microsoft.Maui;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class SwitchHandler : GtkViewHandler<ISwitch, Gtk.Switch>
{
	public static new IPropertyMapper<ISwitch, SwitchHandler> Mapper =
		new PropertyMapper<ISwitch, SwitchHandler>(ViewMapper)
		{
			[nameof(ISwitch.IsOn)] = MapIsOn,
			[nameof(ISwitch.TrackColor)] = MapTrackColor,
			[nameof(ISwitch.ThumbColor)] = MapThumbColor,
		};

	public SwitchHandler() : base(Mapper)
	{
	}

	protected override Gtk.Switch CreatePlatformView()
	{
		return Gtk.Switch.New();
	}

	protected override void ConnectHandler(Gtk.Switch platformView)
	{
		base.ConnectHandler(platformView);
		platformView.OnStateSet += OnStateSet;
	}

	protected override void DisconnectHandler(Gtk.Switch platformView)
	{
		platformView.OnStateSet -= OnStateSet;
		base.DisconnectHandler(platformView);
	}

	bool OnStateSet(Gtk.Switch sender, Gtk.Switch.StateSetSignalArgs args)
	{
		if (VirtualView != null)
			VirtualView.IsOn = args.State;
		return false;
	}

	public static void MapIsOn(SwitchHandler handler, ISwitch @switch)
	{
		if (handler.PlatformView?.GetActive() != @switch.IsOn)
			handler.PlatformView?.SetActive(@switch.IsOn);
	}

	public static void MapTrackColor(SwitchHandler handler, ISwitch @switch)
	{
		if (@switch.TrackColor != null)
			handler.ApplyCss(handler.PlatformView,
				$"background-color: {ToGtkColor(@switch.TrackColor)}; background-image: none; border-radius: 9999px;");
	}

	public static void MapThumbColor(SwitchHandler handler, ISwitch @switch)
	{
		if (@switch.ThumbColor != null)
			handler.ApplyCssWithSelector(handler.PlatformView, "* > slider",
				$"background-color: {ToGtkColor(@switch.ThumbColor)}; background-image: none;");
	}
}
