using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Graphics;

namespace Platform.Maui.Linux.Gtk4.Handlers;

/// <summary>
/// Handler for BoxView (renders as a colored rectangle via Gtk.DrawingArea).
/// In MAUI, BoxView is backed by a ShapeView internally, so we need our own handler.
/// </summary>
public class BoxViewHandler : GtkViewHandler<IView, Gtk.DrawingArea>
{
	public static new IPropertyMapper<IView, BoxViewHandler> Mapper =
		new PropertyMapper<IView, BoxViewHandler>(ViewMapper);

	public BoxViewHandler() : base(Mapper) { }

	protected override Gtk.DrawingArea CreatePlatformView()
	{
		var area = Gtk.DrawingArea.New();
		area.SetDrawFunc(OnDraw);
		return area;
	}

	public override void PlatformArrange(Rect rect)
	{
		base.PlatformArrange(rect);

		if (PlatformView != null)
		{
			PlatformView.SetContentWidth((int)rect.Width);
			PlatformView.SetContentHeight((int)rect.Height);
			PlatformView.QueueDraw();
		}
	}

	private void OnDraw(Gtk.DrawingArea area, Cairo.Context cr, int width, int height)
	{
		if (VirtualView is Microsoft.Maui.Controls.BoxView boxView && boxView.Color != null)
		{
			var c = boxView.Color;
			cr.SetSourceRgba(c.Red, c.Green, c.Blue, c.Alpha);
			cr.Rectangle(0, 0, width, height);
			cr.Fill();
		}
		else
		{
			cr.SetSourceRgba(0.85, 0.85, 0.85, 1.0);
			cr.Rectangle(0, 0, width, height);
			cr.Fill();
		}
	}
}
