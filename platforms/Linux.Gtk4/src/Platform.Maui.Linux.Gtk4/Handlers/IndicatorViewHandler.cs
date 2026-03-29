using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

/// <summary>
/// Handler for IndicatorView. Renders position indicators as a row of dot widgets
/// that highlight the current position (commonly used with CarouselView).
/// </summary>
public class IndicatorViewHandler : GtkViewHandler<IView, Gtk.Box>
{
	readonly List<Gtk.DrawingArea> _dots = new();

	public static new IPropertyMapper<IView, IndicatorViewHandler> Mapper =
		new PropertyMapper<IView, IndicatorViewHandler>(ViewMapper)
		{
			["Count"] = MapIndicators,
			["Position"] = MapPosition,
			["IndicatorColor"] = MapIndicatorColor,
			["SelectedIndicatorColor"] = MapIndicatorColor,
			["IndicatorSize"] = MapIndicators,
			["IndicatorsShape"] = MapIndicators,
			["MaximumVisible"] = MapIndicators,
			["HideSingle"] = MapIndicators,
		};

	public IndicatorViewHandler() : base(Mapper) { }

	protected override Gtk.Box CreatePlatformView()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Horizontal, 4);
		box.SetHalign(Gtk.Align.Center);
		box.SetValign(Gtk.Align.Center);
		return box;
	}

	protected override void ConnectHandler(Gtk.Box platformView)
	{
		base.ConnectHandler(platformView);
		// Rebuild dots on connect since Count may already be set
		if (VirtualView is IndicatorView iv)
			RebuildDots(iv);
	}

	public static void MapIndicators(IndicatorViewHandler handler, IView view)
	{
		handler.RebuildDots(view as IndicatorView);
	}

	public static void MapPosition(IndicatorViewHandler handler, IView view)
	{
		handler.UpdateSelection(view as IndicatorView);
	}

	public static void MapIndicatorColor(IndicatorViewHandler handler, IView view)
	{
		handler.UpdateSelection(view as IndicatorView);
	}

	void RebuildDots(IndicatorView? iv)
	{
		// Clear existing
		foreach (var dot in _dots)
			PlatformView.Remove(dot);
		_dots.Clear();

		if (iv == null) return;

		int count = iv.Count;
		if (iv.MaximumVisible > 0 && iv.MaximumVisible < count)
			count = iv.MaximumVisible;
		if (count <= 1 && iv.HideSingle)
			return;

		double size = iv.IndicatorSize > 0 ? iv.IndicatorSize : 8;

		for (int i = 0; i < count; i++)
		{
			int idx = i;
			var dot = Gtk.DrawingArea.New();
			dot.SetContentWidth((int)size);
			dot.SetContentHeight((int)size);
			dot.SetDrawFunc((area, cr, w, h) =>
			{
				DrawDot(cr, w, h, iv, idx);
			});

			// Allow clicking to change position
			var gesture = Gtk.GestureClick.New();
			int capturedIdx = i;
			gesture.OnReleased += (s, e) =>
			{
				if (iv != null) iv.Position = capturedIdx;
			};
			dot.AddController(gesture);

			_dots.Add(dot);
			PlatformView.Append(dot);
		}
	}

	void DrawDot(Cairo.Context cr, int w, int h, IndicatorView iv, int index)
	{
		bool selected = index == iv.Position;
		Color color = selected
			? (iv.SelectedIndicatorColor ?? Colors.DodgerBlue)
			: (iv.IndicatorColor ?? Colors.LightGray);

		cr.SetSourceRgba(color.Red, color.Green, color.Blue, color.Alpha);

		if (iv.IndicatorsShape == IndicatorShape.Square)
		{
			cr.Rectangle(1, 1, w - 2, h - 2);
		}
		else
		{
			double cx = w / 2.0, cy = h / 2.0, r = Math.Min(cx, cy) - 1;
			cr.Arc(cx, cy, r, 0, 2 * Math.PI);
		}

		cr.Fill();
	}

	void UpdateSelection(IndicatorView? iv)
	{
		if (iv == null) return;
		foreach (var dot in _dots)
			dot.QueueDraw();
	}
}
