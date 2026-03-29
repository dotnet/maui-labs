using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class FrameHandler : GtkViewHandler<IContentView, Gtk.Frame>
{
	public static new IPropertyMapper<IContentView, FrameHandler> Mapper =
		new PropertyMapper<IContentView, FrameHandler>(ViewMapper)
		{
			[nameof(IContentView.Content)] = MapContent,
		};

	public FrameHandler() : base(Mapper)
	{
	}

	protected override Gtk.Frame CreatePlatformView()
	{
		return Gtk.Frame.New(null);
	}

	public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
	{
		if (VirtualView is ICrossPlatformLayout crossPlatform)
			return crossPlatform.CrossPlatformMeasure(widthConstraint, heightConstraint);

		return base.GetDesiredSize(widthConstraint, heightConstraint);
	}

	public override void PlatformArrange(Rect rect)
	{
		base.PlatformArrange(rect);
		if (VirtualView is ICrossPlatformLayout crossPlatform)
			crossPlatform.CrossPlatformArrange(rect);
	}

	public static void MapContent(FrameHandler handler, IContentView view)
	{
		_ = handler.MauiContext ?? throw new InvalidOperationException("MauiContext not set.");

		if (view.PresentedContent != null)
		{
			var platformContent = (Gtk.Widget)view.PresentedContent.ToPlatform(handler.MauiContext);
			handler.PlatformView?.SetChild(platformContent);
		}
	}
}
