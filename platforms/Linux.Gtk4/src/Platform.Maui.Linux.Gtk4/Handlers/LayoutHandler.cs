using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Platform.Maui.Linux.Gtk4.Platform;
using ILayout = Microsoft.Maui.ILayout;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class LayoutHandler : GtkViewHandler<ILayout, GtkLayoutPanel>, ILayoutHandler
{
	public static new IPropertyMapper<ILayout, LayoutHandler> Mapper =
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

		// Trigger initial layout after GTK has allocated sizes
		GLib.Functions.IdleAdd(0, () =>
		{
			if (VirtualView == null || PlatformView == null) return false;
			if (PlatformView.IsExternallyManaged) return false;

			var (w, h) = GetConstrainedSize(PlatformView);
			if (w > 1 && h > 1)
			{
				PlatformView.CrossPlatformMeasure(w, h);
				PlatformView.CrossPlatformArrange(new Rect(0, 0, w, h));
			}
			return false;
		});
	}

	protected override void ConnectHandler(GtkLayoutPanel platformView)
	{
		base.ConnectHandler(platformView);

		if (VirtualView is ICrossPlatformLayout layout)
			platformView.CrossPlatformLayout = layout;

		// Only the outermost layout installs a resize handler.
		// Nested layouts are driven by their parent's arrange pass.
		GLib.Functions.IdleAdd(0, () =>
		{
			if (HasAncestorLayoutPanel(platformView))
				return false; // nested — parent drives layout

			if (platformView.IsExternallyManaged)
				return false; // template-driven — CollectionView manages layout

			// Find the window and listen for size changes
			Gtk.Widget? cur = platformView;
			while (cur != null && cur is not Gtk.Window) cur = cur.GetParent();
			if (cur is not Gtk.Window window) return false;

			// Capture the initial window size from allocation (actual rendered
			// size), not default size. On Wayland, the compositor may constrain
			// the window smaller than the default. Content-driven size changes
			// (from SetSizeRequest) can push the window to grow, so only
			// user-initiated resizes should update the constraint.
			int constraintW = window.GetAllocatedWidth();
			int constraintH = window.GetAllocatedHeight();
			if (constraintW < 1 || constraintH < 1)
			{
				window.GetDefaultSize(out constraintW, out constraintH);
			}
			if (constraintW < 1) constraintW = 800;
			if (constraintH < 1) constraintH = 600;

			void DoLayout()
			{
				if (VirtualView == null) return;
				var (dw, dh) = GetConstrainedSize(platformView, constraintW, constraintH);
				if (dw < 1 || dh < 1) return;

				// Invalidate cached measurements so MAUI re-measures the
				// entire tree with the new constraints, not just the root.
				(VirtualView as Microsoft.Maui.Controls.VisualElement)?.InvalidateMeasure();

				platformView.CrossPlatformMeasure(dw, dh);
				platformView.CrossPlatformArrange(new Rect(0, 0, dw, dh));
			}

			// Initial layout
			DoLayout();

			// Re-layout on window resize via property notification
			window.OnNotify += (sender, args) =>
			{
				if (args.Pspec.GetName() is "default-width" or "default-height")
				{
					// Update constraint from actual allocated size
					var aw = window.GetAllocatedWidth();
					var ah = window.GetAllocatedHeight();
					if (aw > 0) constraintW = aw;
					if (ah > 0) constraintH = ah;
					DoLayout();
				}
			};

			// Re-layout when ancestor Paned divider is moved
			Gtk.Widget? ancestor = platformView.GetParent();
			while (ancestor != null && ancestor is not Gtk.Window)
			{
				if (ancestor is Gtk.Paned paned)
				{
					paned.OnNotify += (sender, args) =>
					{
						if (args.Pspec.GetName() == "position")
						{
							DoLayout();
						}
					};
					break;
				}
				ancestor = ancestor.GetParent();
			}

			// Also re-layout when content changes
			platformView.AddTickCallback((widget, clock) =>
			{
				if (VirtualView == null) return false;
				if (platformView.LayoutDirty)
				{
					platformView.LayoutDirty = false;
					DoLayout();
				}
				return true;
			});

			return false;
		});
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

	private static (int width, int height) GetConstrainedSize(Gtk.Widget widget)
	{
		// Find window to get initial size
		Gtk.Widget? cur = widget;
		while (cur != null && cur is not Gtk.Window) cur = cur.GetParent();
		if (cur is not Gtk.Window window)
			return (800, 600);

		window.GetDefaultSize(out var ww, out var wh);
		if (ww < 1) ww = window.GetAllocatedWidth();
		if (wh < 1) wh = window.GetAllocatedHeight();
		if (ww < 1 || wh < 1) return (800, 600);

		return GetConstrainedSize(widget, ww, wh);
	}

	private static (int width, int height) GetConstrainedSize(Gtk.Widget widget, int windowWidth, int windowHeight)
	{
		Gtk.Paned? paned = null;
		bool isStartChild = false;

		var current = widget.GetParent();
		while (current != null)
		{
			if (current is Gtk.Paned p && paned == null)
			{
				paned = p;
				var startChild = p.GetStartChild();
				var w = widget;
				while (w != null && w != p)
				{
					if (w == startChild)
					{
						isStartChild = true;
						break;
					}
					w = w.GetParent();
				}
			}
			if (current is Gtk.Window)
				break;
			current = current.GetParent();
		}

		if (paned != null)
		{
			var pos = paned.GetPosition();
			if (isStartChild)
				return (pos, windowHeight);
			else
				return (windowWidth - pos, windowHeight);
		}

		return (windowWidth, windowHeight);
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
			System.Diagnostics.Debug.WriteLine($"[Platform.Maui.Linux.Gtk4] Failed to add {child.GetType().Name}: {ex.Message}");
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
		if (layout.Background is Microsoft.Maui.Graphics.SolidPaint solidPaint && solidPaint.Color != null)
			handler.ApplyCss(handler.PlatformView,
				$"background-color: {ToGtkColor(solidPaint.Color)}; background-image: none;");
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
