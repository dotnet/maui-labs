using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class PageHandler : GtkViewHandler<IContentView, Gtk.Box>
{
	public static new IPropertyMapper<IContentView, PageHandler> Mapper =
		new PropertyMapper<IContentView, PageHandler>(ViewMapper)
		{
			[nameof(IContentView.Content)] = MapContent,
			["Title"] = MapTitle,
		};

	public PageHandler() : base(Mapper)
	{
	}

	protected override Gtk.Box CreatePlatformView()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
		box.SetVexpand(true);
		box.SetHexpand(true);
		return box;
	}

	public static void MapContent(PageHandler handler, IContentView page)
	{
		_ = handler.MauiContext ?? throw new InvalidOperationException("MauiContext not set.");

		var box = handler.PlatformView;

		// Remove existing content
		while (box.GetFirstChild() is Gtk.Widget existing)
			box.Remove(existing);

		if (page.PresentedContent != null)
		{
			var platformContent = (Gtk.Widget)page.PresentedContent.ToPlatform(handler.MauiContext);
			platformContent.SetVexpand(true);
			platformContent.SetHexpand(true);
			box.Append(platformContent);
		}

		// Propagate layout dirty to ancestor layout panels
		Gtk.Widget? current = box.GetParent();
		while (current != null)
		{
			if (current is Platform.GtkLayoutPanel panel)
			{
				panel.LayoutDirty = true;
			}
			current = current.GetParent();
		}
	}

	public override void SetVirtualView(IView view)
	{
		base.SetVirtualView(view);

		// Trigger content mapping
		Mapper.UpdateProperties(this, VirtualView);
	}

	public override void PlatformArrange(Rect rect)
	{
		base.PlatformArrange(rect);

		// Propagate arrange to page content so nested layouts resize
		if (VirtualView is ICrossPlatformLayout crossPlatform)
			crossPlatform.CrossPlatformArrange(new Rect(0, 0, rect.Width, rect.Height));
	}

	public static void MapTitle(PageHandler handler, IContentView page)
	{
		if (page is not Microsoft.Maui.Controls.Page mauiPage)
			return;

		// Walk up to find the GTK window and update its title
		Gtk.Widget? current = handler.PlatformView;
		while (current != null)
		{
			if (current is Gtk.Window window)
			{
				window.SetTitle(mauiPage.Title ?? string.Empty);
				break;
			}
			current = current.GetParent();
		}
	}
}
