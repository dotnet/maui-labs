using Microsoft.Maui;
using Microsoft.Maui.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class FlyoutPageHandler : GtkViewHandler<IFlyoutView, Gtk.Paned>
{
	int _lastPosition = 250;

	public static new IPropertyMapper<IFlyoutView, FlyoutPageHandler> Mapper =
		new PropertyMapper<IFlyoutView, FlyoutPageHandler>(ViewMapper)
		{
			[nameof(IFlyoutView.Flyout)] = MapFlyout,
			[nameof(IFlyoutView.Detail)] = MapDetail,
			[nameof(IFlyoutView.IsPresented)] = MapIsPresented,
		};

	public FlyoutPageHandler() : base(Mapper)
	{
	}

	protected override Gtk.Paned CreatePlatformView()
	{
		var paned = Gtk.Paned.New(Gtk.Orientation.Horizontal);
		paned.SetPosition(300);
		paned.SetVexpand(true);
		paned.SetHexpand(true);
		paned.SetResizeStartChild(false);
		paned.SetShrinkStartChild(false);
		paned.SetResizeEndChild(true);
		paned.SetShrinkEndChild(false);
		return paned;
	}

	public static void MapFlyout(FlyoutPageHandler handler, IFlyoutView flyoutView)
	{
		_ = handler.MauiContext ?? throw new InvalidOperationException("MauiContext not set.");

		if (flyoutView.Flyout != null)
		{
			var platformFlyout = (Gtk.Widget)flyoutView.Flyout.ToPlatform(handler.MauiContext);
			handler.PlatformView?.SetStartChild(platformFlyout);
		}
	}

	public static void MapDetail(FlyoutPageHandler handler, IFlyoutView flyoutView)
	{
		_ = handler.MauiContext ?? throw new InvalidOperationException("MauiContext not set.");

		if (flyoutView.Detail != null)
		{
			var platformDetail = (Gtk.Widget)flyoutView.Detail.ToPlatform(handler.MauiContext);
			handler.PlatformView?.SetEndChild(platformDetail);
		}
	}

	public static void MapIsPresented(FlyoutPageHandler handler, IFlyoutView flyoutView)
	{
		if (handler.PlatformView == null) return;

		var startChild = handler.PlatformView.GetStartChild();
		if (startChild == null) return;

		if (flyoutView.IsPresented)
		{
			startChild.SetVisible(true);
			handler.PlatformView.SetPosition(handler._lastPosition);
		}
		else
		{
			var currentPos = handler.PlatformView.GetPosition();
			if (currentPos > 0)
				handler._lastPosition = currentPos;
			startChild.SetVisible(false);
			handler.PlatformView.SetPosition(0);
		}
	}
}
