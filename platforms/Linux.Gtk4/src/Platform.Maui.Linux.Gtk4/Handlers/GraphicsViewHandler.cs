using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Platform.Maui.Linux.Gtk4.Graphics;

namespace Platform.Maui.Linux.Gtk4.Handlers;

/// <summary>
/// Handler for GraphicsView using Gtk.DrawingArea with Cairo-backed ICanvas.
/// Renders MAUI.Graphics drawing commands via Cairo and forwards interaction events.
/// </summary>
public class GraphicsViewHandler : GtkViewHandler<IGraphicsView, Gtk.DrawingArea>
{
	public static new IPropertyMapper<IGraphicsView, GraphicsViewHandler> Mapper =
		new PropertyMapper<IGraphicsView, GraphicsViewHandler>(ViewMapper)
		{
			[nameof(IGraphicsView.Drawable)] = MapDrawable,
		};

	public static CommandMapper<IGraphicsView, GraphicsViewHandler> GfxCommandMapper = new(ViewCommandMapper)
	{
		[nameof(IGraphicsView.Invalidate)] = MapInvalidate,
	};

	public GraphicsViewHandler() : base(Mapper, GfxCommandMapper) { }

	protected override Gtk.DrawingArea CreatePlatformView()
	{
		var area = Gtk.DrawingArea.New();
		area.SetDrawFunc(OnDraw);
		ConnectInteractionEvents(area);
		return area;
	}

	private void OnDraw(Gtk.DrawingArea area, Cairo.Context cr, int width, int height)
	{
		if (VirtualView?.Drawable == null)
			return;

		var canvas = new CairoCanvas(cr);
		var dirtyRect = new RectF(0, 0, width, height);
		VirtualView.Drawable.Draw(canvas, dirtyRect);
	}

	private void ConnectInteractionEvents(Gtk.DrawingArea area)
	{
		// Click (press/release) for StartInteraction / EndInteraction
		var click = Gtk.GestureClick.New();
		click.SetButton(0); // all buttons
		click.OnPressed += (sender, args) =>
		{
			VirtualView?.StartInteraction(new[] { new PointF((float)args.X, (float)args.Y) });
		};
		click.OnReleased += (sender, args) =>
		{
			VirtualView?.EndInteraction(new[] { new PointF((float)args.X, (float)args.Y) }, true);
		};
		click.OnCancel += (sender, args) =>
		{
			VirtualView?.CancelInteraction();
		};
		area.AddController(click);

		// Drag for DragInteraction
		var drag = Gtk.GestureDrag.New();
		drag.OnDragUpdate += (sender, args) =>
		{
			drag.GetStartPoint(out double startX, out double startY);
			VirtualView?.DragInteraction(new[] { new PointF((float)(startX + args.OffsetX), (float)(startY + args.OffsetY)) });
		};
		area.AddController(drag);

		// Motion for hover events
		var motion = Gtk.EventControllerMotion.New();
		motion.OnEnter += (sender, args) =>
		{
			VirtualView?.StartHoverInteraction(new[] { new PointF((float)args.X, (float)args.Y) });
		};
		motion.OnMotion += (sender, args) =>
		{
			VirtualView?.MoveHoverInteraction(new[] { new PointF((float)args.X, (float)args.Y) });
		};
		motion.OnLeave += (sender, args) =>
		{
			VirtualView?.EndHoverInteraction();
		};
		area.AddController(motion);
	}

	public static void MapInvalidate(GraphicsViewHandler handler, IGraphicsView view, object? arg)
	{
		handler.PlatformView?.QueueDraw();
	}

	public static void MapDrawable(GraphicsViewHandler handler, IGraphicsView view)
	{
		handler.PlatformView?.QueueDraw();
	}
}
