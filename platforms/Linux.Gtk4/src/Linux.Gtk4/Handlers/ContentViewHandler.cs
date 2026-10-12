using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;

public class ContentViewHandler : GtkViewHandler<IContentView, Platform.GtkLayoutPanel>
{
	Platform.GtkRootLayoutDriver? _rootLayout;
	Size _arrangedSize;

	public static IPropertyMapper<IContentView, ContentViewHandler> Mapper =
		new PropertyMapper<IContentView, ContentViewHandler>(ViewMapper)
		{
			[nameof(IContentView.Content)] = MapContent,
			[nameof(ILayout.ClipsToBounds)] = MapClipsToBounds,
			[nameof(IView.Clip)] = MapContentClip,
		};

	public ContentViewHandler() : base(Mapper)
	{
	}

	protected override Platform.GtkLayoutPanel CreatePlatformView()
	{
		return new Platform.GtkLayoutPanel();
	}

	protected override void ConnectHandler(Platform.GtkLayoutPanel platformView)
	{
		base.ConnectHandler(platformView);
		_rootLayout?.Dispose();
		_rootLayout = new Platform.GtkRootLayoutDriver(platformView,
			() => ((IElementHandler)this).VirtualView as IView,
			() => MauiContext?.Services.GetService(typeof(ILogger<ContentViewHandler>)) as ILogger);
	}

	protected override void DisconnectHandler(Platform.GtkLayoutPanel platformView)
	{
		_rootLayout?.Dispose();
		_rootLayout = null;
		_arrangedSize = Size.Zero;
		base.DisconnectHandler(platformView);
	}

	public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
	{
		if (VirtualView is ICrossPlatformLayout crossPlatform)
		{
			var size = crossPlatform.CrossPlatformMeasure(widthConstraint, heightConstraint);
			return new Size(
				Microsoft.Maui.Layouts.LayoutManager.ResolveConstraints(widthConstraint, VirtualView.Width, size.Width, VirtualView.MinimumWidth, VirtualView.MaximumWidth),
				Microsoft.Maui.Layouts.LayoutManager.ResolveConstraints(heightConstraint, VirtualView.Height, size.Height, VirtualView.MinimumHeight, VirtualView.MaximumHeight));
		}

		return base.GetDesiredSize(widthConstraint, heightConstraint);
	}

	public override void PlatformArrange(Rect rect)
	{
		_arrangedSize = rect.Size;
		base.PlatformArrange(rect);
		if (VirtualView is ICrossPlatformLayout crossPlatform)
			crossPlatform.CrossPlatformArrange(new Rect(0, 0, rect.Width, rect.Height));
	}

	public static void MapClipsToBounds(ContentViewHandler handler, IContentView contentView)
	{
		// TemplatedView implements Controls.ILayout, not the core ILayout contract.
		handler.PlatformView.SetOverflow(contentView is Microsoft.Maui.Controls.TemplatedView { IsClippedToBounds: true }
			|| contentView is ILayout { ClipsToBounds: true } || contentView.Clip != null
			? Gtk.Overflow.Hidden : Gtk.Overflow.Visible);
	}

	static void MapContentClip(ContentViewHandler handler, IContentView contentView)
	{
		ViewMapper.UpdateProperty(handler, contentView, nameof(IView.Clip));
		MapClipsToBounds(handler, contentView);
	}

	public static void MapContent(ContentViewHandler handler, IContentView contentView)
	{
		_ = handler.MauiContext ?? throw new InvalidOperationException("MauiContext not set.");

		var panel = handler.PlatformView;
		while (panel.GetFirstChild() is Gtk.Widget child)
			panel.RemoveChild(child);

		if (contentView.PresentedContent != null)
		{
			var platformContent = (Gtk.Widget)contentView.PresentedContent.ToPlatform(handler.MauiContext);
			panel.AddChild(platformContent);
		}

		if (panel.IsExternallyManaged && handler._arrangedSize is { Width: > 0, Height: > 0 } size
			&& contentView is ICrossPlatformLayout layout)
		{
			layout.CrossPlatformMeasure(size.Width, size.Height);
			layout.CrossPlatformArrange(new Rect(0, 0, size.Width, size.Height));
		}

		// Propagate layout dirty to ancestor layout panels
		Gtk.Widget? current = panel;
		while (current != null)
		{
			if (current is Platform.GtkLayoutPanel layoutPanel)
			{
				layoutPanel.LayoutDirty = true;
			}
			current = current.GetParent();
		}
	}
}
