using Microsoft.Maui.Media;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Media;

public class LinuxScreenshot : IScreenshot
#if NET11_0_OR_GREATER
	, IViewScreenshot
#endif
{
	readonly IGtkUiDispatcher _dispatcher;

	public LinuxScreenshot()
		: this(new GtkUiDispatcher())
	{
	}

	internal LinuxScreenshot(IGtkUiDispatcher dispatcher) =>
		_dispatcher = dispatcher;

	public bool IsCaptureSupported => true;

	public async Task<IScreenshotResult> CaptureAsync() =>
		await _dispatcher.InvokeAsync<IScreenshotResult>(() =>
		{
			var window = (Gtk.Application.GetDefault() as Gtk.Application)?.GetActiveWindow()
				?? throw new InvalidOperationException(
					"Cannot capture a screenshot because the GTK application has no active window.");
			return CaptureWidget(window);
		}).ConfigureAwait(false);

#if NET11_0_OR_GREATER
	public async Task<IScreenshotResult?> CaptureViewAsync(object platformView)
	{
		if (platformView is not Gtk.Widget widget)
			return null;

		return await _dispatcher.InvokeAsync<IScreenshotResult?>(
			() => CaptureWidget(widget)).ConfigureAwait(false);
	}
#endif

	internal static LinuxScreenshotResult CaptureWidget(Gtk.Widget widget)
	{
		var width = widget.GetWidth();
		var height = widget.GetHeight();
		if (width <= 0 || height <= 0)
			throw new InvalidOperationException("Cannot capture a GTK widget before it has a non-zero allocation.");

		var renderer = widget.GetNative()?.GetRenderer()
			?? throw new InvalidOperationException("Cannot capture a GTK widget that is not attached to a native renderer.");

		var snapshot = Gtk.Snapshot.New();
		if (widget is Gtk.Window window)
		{
			var child = window.GetChild()
				?? throw new InvalidOperationException("Cannot capture a GTK window without content.");
			window.SnapshotChild(child, snapshot);
		}
		else if (widget.GetParent() is Gtk.Widget parent)
		{
			parent.SnapshotChild(widget, snapshot);
		}
		else
		{
			throw new InvalidOperationException("Cannot capture a GTK widget that is not attached to a parent.");
		}

		var node = snapshot.FreeToNode()
			?? throw new InvalidOperationException("GTK did not produce a render node for the requested screenshot.");
		using var texture = renderer.RenderTexture(node, null);
		using var png = texture.SaveToPngBytes()
			?? throw new InvalidOperationException("GTK could not encode the screenshot as PNG.");
		var pngSize = png.GetSize();
		if (pngSize == 0)
			throw new InvalidOperationException("GTK encoded an empty screenshot.");

		return new LinuxScreenshotResult(
			png.GetRegionSpan<byte>(0, pngSize).ToArray(),
			texture.GetWidth(),
			texture.GetHeight());
	}
}

internal interface IGtkUiDispatcher
{
	Task<T> InvokeAsync<T>(Func<T> action);
}

internal sealed class GtkUiDispatcher : IGtkUiDispatcher
{
	readonly Func<bool> _isOwner;
	readonly Action<Func<bool>> _schedule;

	public GtkUiDispatcher()
		: this(
			() => GLib.Functions.MainContextDefault().IsOwner(),
			callback => GLib.Functions.IdleAdd(0, () => callback()))
	{
	}

	internal GtkUiDispatcher(
		Func<bool> isOwner,
		Action<Func<bool>> schedule)
	{
		_isOwner = isOwner;
		_schedule = schedule;
	}

	public Task<T> InvokeAsync<T>(Func<T> action)
	{
		if (_isOwner())
		{
			try
			{
				return Task.FromResult(action());
			}
			catch (Exception ex)
			{
				return Task.FromException<T>(ex);
			}
		}

		var completion = new TaskCompletionSource<T>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		_schedule(() =>
		{
			try
			{
				completion.TrySetResult(action());
			}
			catch (Exception ex)
			{
				completion.TrySetException(ex);
			}
			return false;
		});
		return completion.Task;
	}
}

internal sealed class LinuxScreenshotResult : IScreenshotResult
{
	readonly byte[] _png;

	public LinuxScreenshotResult(byte[] png, int width, int height)
	{
		_png = png;
		Width = width;
		Height = height;
	}

	public int Width { get; }
	public int Height { get; }

	public Task<Stream> OpenReadAsync(ScreenshotFormat format = ScreenshotFormat.Png, int quality = 100)
	{
		EnsurePng(format);
		return Task.FromResult<Stream>(new MemoryStream(_png, writable: false));
	}

	public Task CopyToAsync(Stream destination, ScreenshotFormat format = ScreenshotFormat.Png, int quality = 100)
	{
		ArgumentNullException.ThrowIfNull(destination);
		EnsurePng(format);
		return destination.WriteAsync(_png).AsTask();
	}

	static void EnsurePng(ScreenshotFormat format)
	{
		if (format != ScreenshotFormat.Png)
			throw new NotSupportedException("GTK4 screenshot capture currently supports PNG output only.");
	}
}
