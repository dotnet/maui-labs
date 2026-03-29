using Microsoft.Maui.Media;

namespace Platform.Maui.Linux.Gtk4.Essentials.Media;

public class LinuxScreenshot : IScreenshot
{
	public bool IsCaptureSupported => true;

	public Task<IScreenshotResult?> CaptureAsync()
	{
		try
		{
			var window = (Gtk.Application.GetDefault() as Gtk.Application)?.GetActiveWindow();
			if (window is null)
				return Task.FromResult<IScreenshotResult?>(null);

			var renderer = window.GetRenderer();
			if (renderer is null)
				return Task.FromResult<IScreenshotResult?>(null);

			// Use a GtkSnapshot to capture the window content
			var snapshot = Gtk.Snapshot.New();
			var width = window.GetWidth();
			var height = window.GetHeight();

			if (width <= 0 || height <= 0)
				return Task.FromResult<IScreenshotResult?>(null);

			// Snapshot the window's child (the content)
			var child = window.GetChild();
			if (child is not null)
				window.SnapshotChild(child, snapshot);

			var node = snapshot.FreeToNode();
			if (node is null)
				return Task.FromResult<IScreenshotResult?>(null);

			var texture = renderer.RenderTexture(node, null);
			var tempPath = Path.Combine(Path.GetTempPath(), $"screenshot_{Guid.NewGuid()}.png");
			texture.SaveToPng(tempPath);

			return Task.FromResult<IScreenshotResult?>(new LinuxScreenshotResult(tempPath, width, height));
		}
		catch
		{
			return Task.FromResult<IScreenshotResult?>(null);
		}
	}
}

internal class LinuxScreenshotResult : IScreenshotResult
{
	private readonly string _filePath;

	public LinuxScreenshotResult(string filePath, int width, int height)
	{
		_filePath = filePath;
		Width = width;
		Height = height;
	}

	public int Width { get; }
	public int Height { get; }

	public Task<Stream> OpenReadAsync(ScreenshotFormat format = ScreenshotFormat.Png, int quality = 100)
	{
		return Task.FromResult<Stream>(File.OpenRead(_filePath));
	}

	public Task CopyToAsync(Stream destination, ScreenshotFormat format = ScreenshotFormat.Png, int quality = 100)
	{
		using var source = File.OpenRead(_filePath);
		return source.CopyToAsync(destination);
	}
}
