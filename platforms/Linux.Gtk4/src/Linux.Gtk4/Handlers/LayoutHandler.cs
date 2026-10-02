using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using ILayout = Microsoft.Maui.ILayout;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;

public class LayoutHandler : GtkViewHandler<ILayout, GtkLayoutPanel>, ILayoutHandler
{
	LayoutBinding? _layoutBinding;

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
		ArgumentNullException.ThrowIfNull(view);
		var layout = (ILayout)view;
		var previousPanel = ((IElementHandler)this).PlatformView as GtkLayoutPanel;
		if (_layoutBinding is { } current && current.Matches(view, previousPanel))
			return;

		RetireLayoutBinding();
		if (previousPanel != null)
			previousPanel.CrossPlatformLayout = null;
		base.SetVirtualView(view);

		var panel = PlatformView;
		if (ReferenceEquals(previousPanel, panel))
			while (panel.GetFirstChild() is { } child)
				panel.RemoveChild(child);
		panel.CrossPlatformLayout = layout as ICrossPlatformLayout;

		for (int i = 0; i < layout.Count; i++)
			Add(layout[i]);

		var binding = new LayoutBinding(this, layout, panel);
		_layoutBinding = binding;
		binding.Start();
	}

	protected override void DisconnectHandler(GtkLayoutPanel platformView)
	{
		RetireLayoutBinding();
		platformView.CrossPlatformLayout = null;
		base.DisconnectHandler(platformView);
	}

	void RetireLayoutBinding()
	{
		var binding = _layoutBinding;
		_layoutBinding = null;
		binding?.Dispose();
	}

	// The binding object is the generation token; retired callbacks cannot alter a newer binding's IDs.
	sealed class LayoutBinding(LayoutHandler handler, ILayout view, GtkLayoutPanel panel) : IDisposable
	{
		uint _initialLayoutSource;
		uint _resizeSetupSource;
		uint _tickCallback;
		Gtk.Window? _window;
		Gtk.Paned? _paned;
		int _constraintWidth;
		int _constraintHeight;

		public bool Matches(IView candidate, GtkLayoutPanel? candidatePanel) =>
			ReferenceEquals(view, candidate) && ReferenceEquals(panel, candidatePanel);

		bool IsCurrent =>
			ReferenceEquals(handler._layoutBinding, this) &&
			ReferenceEquals(((IElementHandler)handler).VirtualView, view) &&
			ReferenceEquals(((IElementHandler)handler).PlatformView, panel);

		public void Start()
		{
			_resizeSetupSource = GLib.Functions.IdleAdd(0, InstallResizeCallbacks);
			_initialLayoutSource = GLib.Functions.IdleAdd(0, InitialLayout);
		}

		bool InitialLayout()
		{
			_initialLayoutSource = 0;
			if (!IsCurrent || panel.IsExternallyManaged)
				return false;

			var (width, height) = GetConstrainedSize(panel);
			if (width > 1 && height > 1)
			{
				panel.CrossPlatformMeasure(width, height);
				if (IsCurrent)
					panel.CrossPlatformArrange(new Rect(0, 0, width, height));
			}
			return false;
		}

		bool InstallResizeCallbacks()
		{
			_resizeSetupSource = 0;
			if (!IsCurrent || HasAncestorLayoutPanel(panel) || panel.IsExternallyManaged)
				return false;

			Gtk.Widget? ancestor = panel;
			while (ancestor != null && ancestor is not Gtk.Window)
				ancestor = ancestor.GetParent();
			if (ancestor is not Gtk.Window window)
				return false;

			_constraintWidth = window.GetAllocatedWidth();
			_constraintHeight = window.GetAllocatedHeight();
			if (_constraintWidth < 1 || _constraintHeight < 1)
				window.GetDefaultSize(out _constraintWidth, out _constraintHeight);
			if (_constraintWidth < 1) _constraintWidth = 800;
			if (_constraintHeight < 1) _constraintHeight = 600;

			DoLayout();
			if (!IsCurrent)
				return false;
			_window = window;
			window.OnNotify += OnWindowNotify;

			ancestor = panel.GetParent();
			while (ancestor != null && ancestor is not Gtk.Window)
			{
				if (ancestor is Gtk.Paned paned)
				{
					_paned = paned;
					paned.OnNotify += OnPanedNotify;
					break;
				}
				ancestor = ancestor.GetParent();
			}

			_tickCallback = panel.AddTickCallback((widget, clock) =>
			{
				if (!IsCurrent || panel.IsExternallyManaged)
				{
					_tickCallback = 0;
					return false;
				}
				if (panel.LayoutDirty)
				{
					panel.LayoutDirty = false;
					DoLayout();
				}
				return IsCurrent;
			});
			return false;
		}

		void DoLayout()
		{
			if (!IsCurrent || panel.IsExternallyManaged)
				return;
			var (width, height) = GetConstrainedSize(panel, _constraintWidth, _constraintHeight);
			if (width < 1 || height < 1)
				return;

			(view as Microsoft.Maui.Controls.VisualElement)?.InvalidateMeasure();
			if (!IsCurrent)
				return;
			panel.CrossPlatformMeasure(width, height);
			if (IsCurrent)
				panel.CrossPlatformArrange(new Rect(0, 0, width, height));
		}

		void OnWindowNotify(GObject.Object sender, GObject.Object.NotifySignalArgs args)
		{
			if (!IsCurrent || _window is not { } window)
				return;
			if (args.Pspec.GetName() is "default-width" or "default-height")
			{
				var width = window.GetAllocatedWidth();
				var height = window.GetAllocatedHeight();
				if (width > 0) _constraintWidth = width;
				if (height > 0) _constraintHeight = height;
				DoLayout();
			}
		}

		void OnPanedNotify(GObject.Object sender, GObject.Object.NotifySignalArgs args)
		{
			if (IsCurrent && args.Pspec.GetName() == "position")
				DoLayout();
		}

		public void Dispose()
		{
			RemoveSource(ref _initialLayoutSource);
			RemoveSource(ref _resizeSetupSource);
			var tick = _tickCallback;
			_tickCallback = 0;
			if (tick != 0)
				panel.RemoveTickCallback(tick);
			if (_window != null)
			{
				_window.OnNotify -= OnWindowNotify;
				_window = null;
			}
			if (_paned != null)
			{
				_paned.OnNotify -= OnPanedNotify;
				_paned = null;
			}
		}

		static void RemoveSource(ref uint source)
		{
			var id = source;
			source = 0;
			if (id != 0)
				GLib.Functions.SourceRemove(id);
		}
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
