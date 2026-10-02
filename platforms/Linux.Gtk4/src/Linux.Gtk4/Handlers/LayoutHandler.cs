using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using ILayout = Microsoft.Maui.ILayout;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;

public class LayoutHandler : GtkViewHandler<ILayout, GtkLayoutPanel>, ILayoutHandler
{
	uint _rootLayoutTickId;
	int _rootWidth = -1;
	int _rootHeight = -1;
	ScrollOrientation? _rootScrollOrientation;

	object ILayoutHandler.PlatformView => base.PlatformView!;

	public static IPropertyMapper<ILayout, LayoutHandler> Mapper =
		new PropertyMapper<ILayout, LayoutHandler>(ViewMapper)
		{
			[nameof(ILayout.Background)] = MapBackground,
			[nameof(ILayout.ClipsToBounds)] = MapClipsToBounds,
		};

	public static CommandMapper<ILayout, LayoutHandler> CommandMapper = new(ViewCommandMapper)
	{
		[nameof(ILayoutHandler.Add)] = MapAdd,
		[nameof(ILayoutHandler.Remove)] = MapRemove,
		[nameof(ILayoutHandler.Clear)] = MapClear,
		[nameof(ILayoutHandler.Insert)] = MapInsert,
		[nameof(ILayoutHandler.Update)] = MapUpdate,
		[nameof(ILayoutHandler.UpdateZIndex)] = MapUpdateZIndex,
	};

	public LayoutHandler() : base(Mapper, CommandMapper)
	{
	}

	public LayoutHandler(IPropertyMapper? mapper = null, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
	{
	}

	protected override GtkLayoutPanel CreatePlatformView()
	{
		return new GtkLayoutPanel();
	}

	public override void SetVirtualView(IView view)
	{
		base.SetVirtualView(view);

		// MAUI doesn't automatically call Add for pre-existing children.
		// We must add them manually when the handler is first connected.
		var layout = (ILayout)view;
		for (int i = 0; i < layout.Count; i++)
		{
			Add(layout[i]);
		}
	}

	protected override void ConnectHandler(GtkLayoutPanel platformView)
	{
		base.ConnectHandler(platformView);

		if (VirtualView is ICrossPlatformLayout layout)
			platformView.CrossPlatformLayout = layout;

		platformView.OnNotify += OnPlatformNotify;
		platformView.OnMap += OnPlatformMap;
		StartRootLayoutTick(platformView);
	}

	void OnPlatformNotify(GObject.Object sender, GObject.Object.NotifySignalArgs args)
	{
		if (args.Pspec.GetName() == "parent")
			StartRootLayoutTick(PlatformView);
	}

	void OnPlatformMap(Gtk.Widget sender, EventArgs args) => StartRootLayoutTick(PlatformView);

	void StartRootLayoutTick(GtkLayoutPanel platformView)
	{
		if (_rootLayoutTickId != 0)
			return;

		_rootWidth = _rootHeight = -1;
		_rootLayoutTickId = platformView.AddTickCallback((widget, clock) =>
		{
			if (VirtualView == null)
			{
				_rootLayoutTickId = 0;
				return false;
			}
			if (platformView.IsExternallyManaged || HasAncestorLayoutPanel(platformView))
			{
				_rootLayoutTickId = 0;
				return false;
			}

			// A default-size notification precedes allocation and misses compositor
			// and container resizes. Use this panel's actual space, excluding native chrome.
			var width = platformView.GetAllocatedWidth();
			var height = platformView.GetAllocatedHeight();
			ScrollOrientation? scrollOrientation = null;
			if (VirtualView.Parent is IScrollView scrollView && platformView.GetParent() is Gtk.Viewport viewport)
			{
				// The viewport can keep its child at the previous natural extent.
				// Reflow the non-scrolling axis against the viewport, not that stale extent.
				width = viewport.GetAllocatedWidth();
				height = viewport.GetAllocatedHeight();
				scrollOrientation = scrollView.Orientation;
			}
			if (width <= 0 || height <= 0 ||
				(width == _rootWidth && height == _rootHeight &&
					scrollOrientation == _rootScrollOrientation && !platformView.LayoutDirty))
				return true;

			try
			{
				_rootWidth = width;
				_rootHeight = height;
				_rootScrollOrientation = scrollOrientation;
				platformView.LayoutDirty = false;
				(VirtualView as Microsoft.Maui.Controls.VisualElement)?.InvalidateMeasure();
				var scrollsHorizontally = scrollOrientation is ScrollOrientation.Horizontal or ScrollOrientation.Both;
				var scrollsVertically = scrollOrientation is ScrollOrientation.Vertical or ScrollOrientation.Both;
				var measured = VirtualView.Measure(
					scrollsHorizontally ? double.PositiveInfinity : width,
					scrollsVertically ? double.PositiveInfinity : height);
				VirtualView.Arrange(new Rect(0, 0,
					scrollsHorizontally ? Math.Max(width, measured.Width) : width,
					scrollsVertically ? Math.Max(height, measured.Height) : height));
			}
			catch (Exception ex)
			{
				var logger = MauiContext?.Services.GetService(typeof(ILogger<LayoutHandler>)) as ILogger<LayoutHandler>;
				if (logger != null)
					logger.LogError(ex, "GTK root layout failed at {Width}x{Height}", width, height);
				else
					Console.Error.WriteLine($"[Microsoft.Maui.Platforms.Linux.Gtk4] Root layout failed: {ex}");
			}
			return true;
		});
	}

	protected override void DisconnectHandler(GtkLayoutPanel platformView)
	{
		platformView.OnNotify -= OnPlatformNotify;
		platformView.OnMap -= OnPlatformMap;
		if (_rootLayoutTickId != 0)
			platformView.RemoveTickCallback(_rootLayoutTickId);
		_rootLayoutTickId = 0;
		_rootWidth = _rootHeight = -1;
		_rootScrollOrientation = null;
		platformView.CrossPlatformLayout = null;
		base.DisconnectHandler(platformView);
	}

	/// <summary>
	/// Walks up the GTK widget tree to check if any ancestor is a GtkLayoutPanel.
	/// If so, this panel is nested and should be driven by the parent layout pass.
	/// </summary>
	private static bool HasAncestorLayoutPanel(Gtk.Widget widget)
	{
		var current = widget.GetParent();
		while (current != null && current is not Gtk.Window)
		{
			if (current is GtkLayoutPanel)
				return true;
			current = current.GetParent();
		}
		return false;
	}

	public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
	{
		return PlatformView.CrossPlatformMeasure(widthConstraint, heightConstraint);
	}

	public override void PlatformArrange(Rect rect)
	{
		if (PlatformView.GetParent() is Platform.GtkLayoutPanel lp)
		{
			lp.SetChildBounds(PlatformView, rect.X, rect.Y, (int)rect.Width, (int)rect.Height);
		}

		// Arrange children relative to the panel (origin at 0,0)
		PlatformView.CrossPlatformArrange(new Rect(0, 0, rect.Width, rect.Height));
	}

	public void Add(IView child)
	{
		_ = MauiContext ?? throw new InvalidOperationException("MauiContext not set.");
		try
		{
			var platformChild = (Gtk.Widget)child.ToPlatform(MauiContext);
			PlatformView.AddChild(platformChild);
			MarkLayoutDirty();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.Platforms.Linux.Gtk4] Failed to add {child.GetType().Name}: {ex.Message}");
		}
	}

	public void Remove(IView child)
	{
		if (child.Handler?.PlatformView is Gtk.Widget widget)
		{
			PlatformView.RemoveChild(widget);
			MarkLayoutDirty();
		}
	}

	public void Insert(int index, IView child)
	{
		Add(child);
	}

	public void Clear()
	{
		while (PlatformView.GetFirstChild() is Gtk.Widget child)
			PlatformView.RemoveChild(child);
		MarkLayoutDirty();
	}

	public void Update(int index, IView child)
	{
		Remove(child);
		Add(child);
	}

	/// <summary>
	/// Walk up the widget tree to the root GtkLayoutPanel and set its LayoutDirty flag
	/// so the tick callback re-measures and re-arranges on the next frame.
	/// </summary>
	void MarkLayoutDirty()
	{
		PlatformView.LayoutDirty = true;

		// Propagate to ancestor layout panels (root tick callback drives layout)
		Gtk.Widget? current = PlatformView.GetParent();
		while (current != null)
		{
			if (current is GtkLayoutPanel panel)
			{
				panel.LayoutDirty = true;
			}
			current = current.GetParent();
		}
	}

	public void UpdateZIndex(IView child)
	{
		// GTK4 Fixed doesn't have z-ordering, children render in order added
	}

	public static void MapBackground(LayoutHandler handler, ILayout layout)
	{
		handler.UpdateCss(handler.PlatformView,
			layout.Background is Microsoft.Maui.Graphics.SolidPaint { Color: not null } solidPaint
				? $"background-color: {ToGtkColor(solidPaint.Color)}; background-image: none;" : null);
	}

	public static void MapClipsToBounds(LayoutHandler handler, ILayout layout)
	{
		handler.PlatformView?.SetOverflow(layout.ClipsToBounds ? Gtk.Overflow.Hidden : Gtk.Overflow.Visible);
	}

	public static void MapAdd(LayoutHandler handler, ILayout layout, object? arg)
	{
		if (arg is LayoutHandlerUpdate args)
			handler.Add(args.View);
	}

	public static void MapRemove(LayoutHandler handler, ILayout layout, object? arg)
	{
		if (arg is LayoutHandlerUpdate args)
			handler.Remove(args.View);
	}

	public static void MapInsert(LayoutHandler handler, ILayout layout, object? arg)
	{
		if (arg is LayoutHandlerUpdate args)
			handler.Insert(args.Index, args.View);
	}

	public static void MapClear(LayoutHandler handler, ILayout layout, object? arg)
	{
		handler.Clear();
	}

	public static void MapUpdate(LayoutHandler handler, ILayout layout, object? arg)
	{
		if (arg is LayoutHandlerUpdate args)
			handler.Update(args.Index, args.View);
	}

	public static void MapUpdateZIndex(LayoutHandler handler, ILayout layout, object? arg)
	{
		if (arg is IView view)
			handler.UpdateZIndex(view);
	}
}
