using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

/// <summary>
/// Reports committed GTK allocations, rather than requested/default sizes.
/// Own one observer per connected native-managed view and dispose it on disconnect.
/// </summary>
internal sealed class GtkAllocationObserver : IDisposable
{
	readonly Gtk.Widget _widget;
	readonly Action<int, int> _allocated;
	readonly ILogger? _logger;
	uint _tickId;
	int _width = -1;
	int _height = -1;
	bool _disposed;

	public GtkAllocationObserver(Gtk.Widget widget, Action<int, int> allocated, ILogger? logger)
	{
		_widget = widget;
		_allocated = allocated;
		_logger = logger;
		widget.OnMap += OnMap;
		_tickId = widget.AddTickCallback((_, _) =>
		{
			if (_disposed)
				return false;
			var width = widget.GetAllocatedWidth();
			var height = widget.GetAllocatedHeight();
			if (!widget.GetMapped() || width <= 0 || height <= 0 || (width == _width && height == _height))
				return true;

			_width = width;
			_height = height;
			try
			{
				_allocated(width, height);
			}
			catch (Exception ex)
			{
				if (_logger != null)
					_logger.LogError(ex, "GTK allocation reporting failed at {Width}x{Height}", width, height);
				else
					Console.Error.WriteLine($"[Microsoft.Maui.Platforms.Linux.Gtk4] Allocation reporting failed: {ex}");
			}
			return !_disposed;
		});
	}

	public void Invalidate() => _width = _height = -1;

	void OnMap(Gtk.Widget sender, EventArgs args) => Invalidate();

	public void Dispose()
	{
		if (_disposed)
			return;
		_disposed = true;
		_widget.OnMap -= OnMap;
		if (_tickId != 0)
			_widget.RemoveTickCallback(_tickId);
		_tickId = 0;
	}
}
