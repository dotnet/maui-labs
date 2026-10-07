using Microsoft.Extensions.Logging;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

internal sealed class GtkRootLayoutDriver : IDisposable
{
	readonly GtkLayoutPanel _panel;
	readonly Func<IView?> _getView;
	readonly Func<ILogger?> _getLogger;
	uint _tick;
	GtkRootLayoutConstraints? _lastConstraints;
	bool _disposed;

	public GtkRootLayoutDriver(GtkLayoutPanel panel, Func<IView?> getView, Func<ILogger?> getLogger)
	{
		_panel = panel;
		_getView = getView;
		_getLogger = getLogger;
		_panel.OnNotify += OnNotify;
		_panel.OnMap += OnMap;
		Start();
	}

	void OnNotify(GObject.Object sender, GObject.Object.NotifySignalArgs args)
	{
		if (args.Pspec.GetName() == "parent")
		{
			_lastConstraints = null;
			Start();
		}
	}

	void OnMap(Gtk.Widget sender, EventArgs args)
	{
		_lastConstraints = null;
		Start();
	}

	void Start()
	{
		if (_disposed || _tick != 0)
			return;
		_tick = _panel.AddTickCallback((widget, clock) => Tick());
	}

	bool Tick()
	{
		var view = _getView();
		if (view is not ICrossPlatformLayout layout || _panel.IsExternallyManaged || HasLayoutAncestor())
		{
			_tick = 0;
			_lastConstraints = null;
			return false;
		}

		var width = _panel.GetAllocatedWidth();
		var height = _panel.GetAllocatedHeight();
		ScrollOrientation? orientation = null;
		if (view.Parent is IScrollView scrollView && _panel.GetParent() is Gtk.Viewport viewport)
		{
			width = viewport.GetAllocatedWidth();
			height = viewport.GetAllocatedHeight();
			orientation = scrollView.Orientation;
		}
		var constraints = new GtkRootLayoutConstraints(width, height, orientation);
		if (width <= 0 || height <= 0 || (_lastConstraints == constraints && !_panel.LayoutDirty))
			return true;

		// Record this attempt before entering application layout code. A failed
		// layout is retried on a new allocation or invalidation, not every frame.
		_lastConstraints = constraints;
		_panel.LayoutDirty = false;
		try
		{
			(view as Microsoft.Maui.Controls.VisualElement)?.InvalidateMeasure();
			var measure = constraints.MeasureConstraints;
			var measured = layout.CrossPlatformMeasure(measure.Width, measure.Height);
			layout.CrossPlatformArrange(constraints.ArrangeBounds(measured));
		}
		catch (Exception ex)
		{
			var logger = _getLogger();
			if (logger != null)
				logger.LogError(ex, "GTK root layout failed at {Width}x{Height}", width, height);
			else
				Console.Error.WriteLine($"[Microsoft.Maui.Platforms.Linux.Gtk4] Root layout failed: {ex}");
		}
		return true;
	}

	bool HasLayoutAncestor()
	{
		for (var parent = _panel.GetParent(); parent != null && parent is not Gtk.Window; parent = parent.GetParent())
			if (parent is GtkLayoutPanel)
				return true;
		return false;
	}

	public void Dispose()
	{
		if (_disposed)
			return;
		_disposed = true;
		_panel.OnNotify -= OnNotify;
		_panel.OnMap -= OnMap;
		if (_tick != 0)
			_panel.RemoveTickCallback(_tick);
		_tick = 0;
	}
}

internal readonly record struct GtkRootLayoutConstraints(int Width, int Height, ScrollOrientation? Orientation)
{
	bool ScrollsHorizontally => Orientation is ScrollOrientation.Horizontal or ScrollOrientation.Both;
	bool ScrollsVertically => Orientation is ScrollOrientation.Vertical or ScrollOrientation.Both;

	public Size MeasureConstraints => new(
		ScrollsHorizontally ? double.PositiveInfinity : Width,
		ScrollsVertically ? double.PositiveInfinity : Height);

	public Rect ArrangeBounds(Size measured) => new(0, 0,
		ScrollsHorizontally ? Math.Max(Width, measured.Width) : Width,
		ScrollsVertically ? Math.Max(Height, measured.Height) : Height);
}
