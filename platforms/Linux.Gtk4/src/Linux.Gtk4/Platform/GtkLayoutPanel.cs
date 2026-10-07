using System.Runtime.InteropServices;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

/// <summary>
/// A custom GTK4 container that delegates layout to MAUI's cross-platform layout engine.
/// Extends Gtk.Fixed but replaces its FixedLayout with a CustomLayout so children are
/// always allocated at their MAUI-arranged sizes (not at GTK minimums, which would cause jitter).
/// IMPORTANT: Do NOT use Fixed.Put/Move/Remove on this widget — use AddChild/MoveChild/RemoveChild.
/// </summary>
public class GtkLayoutPanel : Gtk.Fixed
{
	ICrossPlatformLayout? _crossPlatformLayout;
	readonly Dictionary<Gtk.Widget, Rect> _childBounds = new();
	readonly Dictionary<Gtk.Widget, Gsk.Transform?> _childTransforms = new();

	/// <summary>
	/// Set to true when children are added/removed so the root tick callback
	/// knows to re-measure and re-arrange even if the window size hasn't changed.
	/// </summary>
	public bool LayoutDirty { get; set; }

	/// <summary>
	/// When true, this panel is driven by an external layout (e.g., CollectionView template)
	/// and should not run its own idle/tick layout passes.
	/// </summary>
	public bool IsExternallyManaged { get; set; }

	internal static void InvalidateLayout(Gtk.Widget widget)
	{
		for (Gtk.Widget? current = widget; current != null; current = current.GetParent())
		{
			if (current is GtkLayoutPanel panel)
				panel.LayoutDirty = true;
		}
		widget.QueueResize();
	}

	// Instance tracking: native widget pointer → managed panel.
	// Used by static P/Invoke callbacks to find the managed instance.
	static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, GtkLayoutPanel> s_instances = new();
	internal static int TrackedInstanceCount => s_instances.Count;

	internal void ReleaseLayout()
	{
		s_instances.TryRemove(Handle.DangerousGetHandle(), out _);
		_crossPlatformLayout = null;
		_childBounds.Clear();
		_childTransforms.Clear();
	}

	// Native callback delegates — pinned as static fields to prevent GC.
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	delegate int RequestModeFuncNative(IntPtr widget);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	delegate void MeasureFuncNative(IntPtr widget, int orientation, int forSize,
		out int minimum, out int natural, out int minimumBaseline, out int naturalBaseline);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	delegate void AllocateFuncNative(IntPtr widget, int width, int height, int baseline);

	static readonly RequestModeFuncNative s_reqMode = NativeRequestMode;
	static readonly MeasureFuncNative s_measure = NativeMeasure;
	static readonly AllocateFuncNative s_allocate = NativeAllocate;

	[DllImport("libgtk-4.so.1")]
	static extern IntPtr gtk_custom_layout_new(IntPtr requestMode, IntPtr measure, IntPtr allocate);
	[DllImport("libgtk-4.so.1")]
	static extern void gtk_widget_set_layout_manager(IntPtr widget, IntPtr layoutManager);
	[DllImport("libgtk-4.so.1")]
	static extern IntPtr gtk_widget_get_first_child(IntPtr widget);
	[DllImport("libgtk-4.so.1")]
	static extern IntPtr gtk_widget_get_next_sibling(IntPtr widget);

	public GtkLayoutPanel() : base()
	{
		SetHexpand(true);
		SetVexpand(true);

		var nativeHandle = Handle.DangerousGetHandle();
		s_instances[nativeHandle] = this;

		// Replace GTK's default FixedLayout with a custom one that allocates
		// children at MAUI-arranged sizes instead of GTK minimums.
		var layoutPtr = gtk_custom_layout_new(
			Marshal.GetFunctionPointerForDelegate(s_reqMode),
			Marshal.GetFunctionPointerForDelegate(s_measure),
			Marshal.GetFunctionPointerForDelegate(s_allocate));
		gtk_widget_set_layout_manager(nativeHandle, layoutPtr);
	}

	static int NativeRequestMode(IntPtr widget) =>
		s_instances.TryGetValue(widget, out var panel) && panel.IsExternallyManaged && panel.CrossPlatformLayout != null
			? 0 // GTK_SIZE_REQUEST_HEIGHT_FOR_WIDTH
			: 2; // GTK_SIZE_REQUEST_CONSTANT_SIZE

	/// <summary>
	/// Reports 0 minimum height so the panel never pushes the window to grow.
	/// Natural size reflects the extent of all arranged children.
	/// </summary>
	static void NativeMeasure(IntPtr widget, int orientation, int forSize,
		out int minimum, out int natural, out int minimumBaseline, out int naturalBaseline)
	{
		minimum = 0;
		natural = 0;
		minimumBaseline = -1;
		naturalBaseline = -1;

		if (!s_instances.TryGetValue(widget, out var panel)) return;

		if (panel.IsExternallyManaged && panel.CrossPlatformLayout != null)
		{
			try
			{
				var measured = panel.CrossPlatformMeasure(orientation == 1 && forSize >= 0 ? forSize : double.PositiveInfinity,
					double.PositiveInfinity);
				var extent = orientation == 0 ? measured.Width : measured.Height;
				if (!double.IsFinite(extent) || extent < 0)
					throw new InvalidOperationException($"GTK template returned an invalid measured extent: {extent}.");
				natural = (int)Math.Min(int.MaxValue, Math.Ceiling(extent));
				minimum = orientation == 0 ? 0 : natural;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[Microsoft.Maui.Platforms.Linux.Gtk4] Native template measure failed: {ex}");
			}
			return;
		}

		foreach (var kvp in panel._childBounds)
		{
			var child = kvp.Key;
			if (!child.GetVisible()) continue;
			var bounds = kvp.Value;

			if (orientation == 0) // GTK_ORIENTATION_HORIZONTAL
				natural = Math.Max(natural, (int)(bounds.X + bounds.Width));
			else
				natural = Math.Max(natural, (int)(bounds.Y + bounds.Height));
		}
	}

	/// <summary>
	/// Allocates each child at its MAUI-arranged size and position.
	/// Position is applied as a GskTransform translate, combined with any
	/// visual transforms (rotation, scale, etc.) stored via SetChildTransform.
	/// </summary>
	static void NativeAllocate(IntPtr widget, int width, int height, int baseline)
	{
		if (!s_instances.TryGetValue(widget, out var panel)) return;
		try
		{
			panel.AllocateChildren(width, height);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[Microsoft.Maui.Platforms.Linux.Gtk4] Native layout allocation failed at {width}x{height}: {ex}");
		}
	}

	void AllocateChildren(int width, int height)
	{
		if (CrossPlatformLayout != null)
		{
			CrossPlatformMeasure(width, height);
			CrossPlatformArrange(new Rect(0, 0, width, height));
		}

		for (var child = GetFirstChild(); child != null; child = child.GetNextSibling())
		{
			if (!child.GetVisible()) continue;

			if (_childBounds.TryGetValue(child, out var bounds))
			{
				child.SetChildVisible(bounds.Width > 0 && bounds.Height > 0);
				if (bounds.Width <= 0 || bounds.Height <= 0)
					continue;

				Gsk.Transform? transform;
				if (_childTransforms.TryGetValue(child, out var customTransform) && customTransform != null)
				{
					transform = customTransform;
				}
				else if (bounds.X != 0 || bounds.Y != 0)
				{
					var pt = Graphene.Point.Alloc();
					pt.Init((float)bounds.X, (float)bounds.Y);
					transform = Gsk.Transform.New().Translate(pt);
				}
				else
				{
					transform = null;
				}

				child.Measure(Gtk.Orientation.Horizontal, -1, out _, out _, out _, out _);
				child.Measure(Gtk.Orientation.Vertical, (int)bounds.Width, out _, out _, out _, out _);
				child.Allocate((int)bounds.Width, (int)bounds.Height, -1, transform);
			}
			else
			{
				child.Measure(Gtk.Orientation.Horizontal, -1, out int minW, out _, out _, out _);
				child.Measure(Gtk.Orientation.Vertical, -1, out int minH, out _, out _, out _);
				child.Allocate(Math.Max(1, minW), Math.Max(1, minH), -1, null);
			}
		}
	}

	/// <summary>
	/// Adds a child widget to this panel.
	/// Uses SetParent directly (NOT Fixed.Put, which requires FixedLayout).
	/// </summary>
	public void AddChild(Gtk.Widget child)
	{
		child.SetParent(this);
		_childBounds[child] = Rect.Zero;
	}

	/// <summary>
	/// Updates a child's position within this panel.
	/// </summary>
	public void MoveChild(Gtk.Widget child, double x, double y)
	{
		if (_childBounds.TryGetValue(child, out var bounds))
			_childBounds[child] = new Rect(x, y, bounds.Width, bounds.Height);
		else
			_childBounds[child] = new Rect(x, y, 0, 0);
		QueueAllocate();
	}

	/// <summary>
	/// Stores the MAUI-arranged bounds for a child widget.
	/// The CustomLayout allocates children at these exact sizes.
	/// </summary>
	public void SetChildBounds(Gtk.Widget child, double x, double y, int width, int height)
	{
		var bounds = new Rect(x, y, width, height);
		if (_childBounds.TryGetValue(child, out var previous) && previous == bounds)
			return;
		_childBounds[child] = bounds;
		// QueueResize (not QueueAllocate) so parent widgets like ScrolledWindow/Viewport
		// re-measure and discover the full content extent for scrolling.
		QueueResize();
	}

	/// <summary>
	/// Sets a custom visual transform (rotation, scale, translation) on a child.
	/// When set, this replaces the automatic position-based translate transform.
	/// The caller must include position translation in the transform.
	/// </summary>
	public new void SetChildTransform(Gtk.Widget child, Gsk.Transform? transform)
	{
		if (_childTransforms.TryGetValue(child, out var previous) && previous == transform)
			return;
		_childTransforms[child] = transform;
		QueueAllocate();
	}

	/// <summary>
	/// Removes a child widget from this panel.
	/// </summary>
	public void RemoveChild(Gtk.Widget child)
	{
		_childBounds.Remove(child);
		_childTransforms.Remove(child);
		child.Unparent();
	}

	public ICrossPlatformLayout? CrossPlatformLayout
	{
		get => _crossPlatformLayout;
		set
		{
			_crossPlatformLayout = value;
			QueueResize();
		}
	}

	public Size CrossPlatformMeasure(double widthConstraint, double heightConstraint)
	{
		if (_crossPlatformLayout == null)
			return Size.Zero;

		return _crossPlatformLayout.CrossPlatformMeasure(widthConstraint, heightConstraint);
	}

	public Size CrossPlatformArrange(Rect bounds)
	{
		if (_crossPlatformLayout == null)
			return bounds.Size;

		return _crossPlatformLayout.CrossPlatformArrange(bounds);
	}

	/// <summary>
	/// Arranges a child at the specified bounds. Used by externally-managed panels
	/// (e.g., CollectionView templates) that bypass the normal MAUI layout cycle.
	/// </summary>
	public void ArrangeChild(Gtk.Widget child, Rect bounds)
	{
		_childBounds[child] = bounds;
		QueueAllocate();
	}

	/// <summary>
	/// Clean up all children and instance tracking when disposing.
	/// </summary>
	public override void Dispose()
	{
		ReleaseLayout();
		while (GetFirstChild() is Gtk.Widget child)
		{
			child.Unparent();
		}
		base.Dispose();
	}
}
