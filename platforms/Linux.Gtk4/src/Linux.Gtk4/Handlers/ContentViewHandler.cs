using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;

public class ContentViewHandler : GtkViewHandler<IContentView, Platform.GtkLayoutPanel>
{
	uint _layoutTick;
	Size _arrangedSize;

	public static IPropertyMapper<IContentView, ContentViewHandler> Mapper =
		new PropertyMapper<IContentView, ContentViewHandler>(ViewMapper)
		{
			[nameof(IContentView.Content)] = MapContent,
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
		platformView.OnNotify += OnPlatformNotify;
		StartLayoutTick(platformView);
	}

	void OnPlatformNotify(GObject.Object sender, GObject.Object.NotifySignalArgs args)
	{
		if (args.Pspec.GetName() == "parent" && _layoutTick == 0)
			StartLayoutTick(PlatformView);
	}

	void StartLayoutTick(Platform.GtkLayoutPanel platformView)
	{
		if (_layoutTick != 0)
			return;

		int lastWidth = -1, lastHeight = -1;
		_layoutTick = platformView.AddTickCallback((widget, clock) =>
		{
			if (((IElementHandler)this).VirtualView is not ICrossPlatformLayout layout || platformView.IsExternallyManaged)
			{
				_layoutTick = 0;
				return false;
			}

			// A root ContentView must drive layout; its panel prevents nested
			// LayoutHandlers from installing their own root layout callbacks.
			for (var parent = platformView.GetParent(); parent != null && parent is not Gtk.Window; parent = parent.GetParent())
				if (parent is Platform.GtkLayoutPanel)
				{
					_layoutTick = 0;
					return false;
				}

			int width = platformView.GetAllocatedWidth();
			int height = platformView.GetAllocatedHeight();
			if (width <= 0 || height <= 0)
				return true;
			if (width == lastWidth && height == lastHeight && !platformView.LayoutDirty)
				return true;

			lastWidth = width;
			lastHeight = height;
			platformView.LayoutDirty = false;
			(VirtualView as Microsoft.Maui.Controls.VisualElement)?.InvalidateMeasure();
			layout.CrossPlatformMeasure(width, height);
			layout.CrossPlatformArrange(new Rect(0, 0, width, height));
			return true;
		});
	}

	protected override void DisconnectHandler(Platform.GtkLayoutPanel platformView)
	{
		platformView.OnNotify -= OnPlatformNotify;
		if (_layoutTick != 0)
			platformView.RemoveTickCallback(_layoutTick);
		_layoutTick = 0;
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
