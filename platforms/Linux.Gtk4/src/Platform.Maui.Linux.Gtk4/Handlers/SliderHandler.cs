using Microsoft.Maui;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class SliderHandler : GtkViewHandler<ISlider, Gtk.Scale>
{
	public static new IPropertyMapper<ISlider, SliderHandler> Mapper =
		new PropertyMapper<ISlider, SliderHandler>(ViewMapper)
		{
			[nameof(ISlider.Minimum)] = MapMinimum,
			[nameof(ISlider.Maximum)] = MapMaximum,
			[nameof(ISlider.Value)] = MapValue,
			[nameof(ISlider.MinimumTrackColor)] = MapMinimumTrackColor,
			[nameof(ISlider.MaximumTrackColor)] = MapMaximumTrackColor,
			[nameof(ISlider.ThumbColor)] = MapThumbColor,
			[nameof(ISlider.ThumbImageSource)] = MapThumbImageSource,
		};

	public SliderHandler() : base(Mapper)
	{
	}

	protected override Gtk.Scale CreatePlatformView()
	{
		var scale = Gtk.Scale.NewWithRange(Gtk.Orientation.Horizontal, 0, 1, 0.01);
		return scale;
	}

	protected override void ConnectHandler(Gtk.Scale platformView)
	{
		base.ConnectHandler(platformView);
		platformView.OnChangeValue += OnChangeValue;
	}

	protected override void DisconnectHandler(Gtk.Scale platformView)
	{
		platformView.OnChangeValue -= OnChangeValue;
		base.DisconnectHandler(platformView);
	}

	bool OnChangeValue(Gtk.Range sender, Gtk.Range.ChangeValueSignalArgs args)
	{
		if (VirtualView != null)
			VirtualView.Value = args.Value;
		return false;
	}

	public static void MapMinimum(SliderHandler handler, ISlider slider)
	{
		handler.PlatformView?.GetAdjustment()?.SetLower(slider.Minimum);
	}

	public static void MapMaximum(SliderHandler handler, ISlider slider)
	{
		handler.PlatformView?.GetAdjustment()?.SetUpper(slider.Maximum);
	}

	public static void MapValue(SliderHandler handler, ISlider slider)
	{
		handler.PlatformView?.SetValue(slider.Value);
	}

	public static void MapMinimumTrackColor(SliderHandler handler, ISlider slider)
	{
		if (slider.MinimumTrackColor != null)
			handler.ApplyCssWithSelector(handler.PlatformView, "* > trough > highlight",
				$"background-color: {ToGtkColor(slider.MinimumTrackColor)};");
	}

	public static void MapMaximumTrackColor(SliderHandler handler, ISlider slider)
	{
		if (slider.MaximumTrackColor != null)
			handler.ApplyCssWithSelector(handler.PlatformView, "* > trough",
				$"background-color: {ToGtkColor(slider.MaximumTrackColor)};");
	}

	public static void MapThumbColor(SliderHandler handler, ISlider slider)
	{
		if (slider.ThumbColor != null)
			handler.ApplyCssWithSelector(handler.PlatformView, "* > trough > slider",
				$"background-color: {ToGtkColor(slider.ThumbColor)}; background-image: none;");
	}

	public static void MapThumbImageSource(SliderHandler handler, ISlider slider)
	{
		// Custom thumb images require async image loading infrastructure; not yet wired.
	}
}
