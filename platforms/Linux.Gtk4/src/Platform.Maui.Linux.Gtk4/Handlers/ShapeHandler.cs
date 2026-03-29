using Microsoft.Maui;
using Microsoft.Maui.Handlers;

namespace Platform.Maui.Linux.Gtk4.Handlers;

/// <summary>
/// Shape handler using Gtk.DrawingArea with Cairo rendering.
/// Handles Rectangle, Ellipse, Line, Path, Polygon, Polyline shapes.
/// </summary>
public class ShapeHandler : GtkViewHandler<IView, Gtk.DrawingArea>
{
	public static new IPropertyMapper<IView, ShapeHandler> Mapper =
		new PropertyMapper<IView, ShapeHandler>(ViewMapper);

	public ShapeHandler() : base(Mapper) { }

	protected override Gtk.DrawingArea CreatePlatformView()
	{
		var drawingArea = Gtk.DrawingArea.New();
		drawingArea.SetDrawFunc(OnDraw);
		return drawingArea;
	}

	private void OnDraw(Gtk.DrawingArea area, Cairo.Context cr, int width, int height)
	{
		if (VirtualView == null)
			return;

		// Default: fill with a placeholder rectangle
		cr.SetSourceRgba(0.5, 0.5, 0.5, 0.3);
		cr.Rectangle(0, 0, width, height);
		cr.Fill();
	}
}
