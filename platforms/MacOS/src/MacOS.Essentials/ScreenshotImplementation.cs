using AppKit;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Media;
using ObjCRuntime;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class ScreenshotImplementation : IScreenshot
{
	public bool IsCaptureSupported => true;

	public Task<IScreenshotResult> CaptureAsync()
	{
		var window = NSApplication.SharedApplication.KeyWindow
			?? NSApplication.SharedApplication.MainWindow;

		if (window == null)
			throw new InvalidOperationException("No window available to capture.");

		// Capture the window using its CGWindowID
		var windowId = (uint)window.WindowNumber;
		var cgImagePtr = CGWindowListCreateImage(
			CGRect.Null,
			CGWindowListOption.IncludingWindow,
			windowId,
			CGWindowImageOption.BoundsIgnoreFraming);

		if (cgImagePtr == IntPtr.Zero)
			throw new InvalidOperationException("Failed to capture window.");

		var cgImage = Runtime.GetINativeObject<CGImage>(cgImagePtr, owns: true)!;
		var nsImage = new NSImage(cgImage, new CGSize(cgImage.Width, cgImage.Height));
		var tiffData = nsImage.AsTiff();
		if (tiffData == null)
			throw new InvalidOperationException("Failed to convert screenshot.");

		var rep = new NSBitmapImageRep(tiffData);
		var width = (int)rep.PixelsWide;
		var height = (int)rep.PixelsHigh;

		IScreenshotResult result = new MacOSScreenshotResult(rep, width, height);
		return Task.FromResult(result);
	}

	/// <summary>
	/// Captures a screenshot of a specific NSView.
	/// Uses CacheDisplay to render the view hierarchy into a bitmap.
	/// </summary>
	public Task<IScreenshotResult?> CaptureAsync(NSView view)
	{
		ArgumentNullException.ThrowIfNull(view);

		var bounds = view.Bounds;
		if (bounds.Width <= 0 || bounds.Height <= 0)
			return Task.FromResult<IScreenshotResult?>(null);

		var scale = view.Window?.BackingScaleFactor ?? 2.0;
		var pixelWidth = (int)(bounds.Width * scale);
		var pixelHeight = (int)(bounds.Height * scale);

		var rep = new NSBitmapImageRep(
			IntPtr.Zero,
			pixelWidth,
			pixelHeight,
			8,       // bits per sample
			4,       // samples per pixel (RGBA)
			true,    // has alpha
			false,   // is planar
			NSColorSpace.DeviceRGB,
			0,       // bytes per row (auto)
			0);      // bits per pixel (auto)

		if (rep == null)
			return Task.FromResult<IScreenshotResult?>(null);

		rep.Size = new CGSize(bounds.Width, bounds.Height);

		NSGraphicsContext.GlobalSaveGraphicsState();
		try
		{
			var context = NSGraphicsContext.FromBitmap(rep);
			if (context == null)
				return Task.FromResult<IScreenshotResult?>(null);

			NSGraphicsContext.CurrentContext = context;
			view.CacheDisplay(bounds, rep);
		}
		finally
		{
			NSGraphicsContext.GlobalRestoreGraphicsState();
		}

		var result = new MacOSScreenshotResult(rep, pixelWidth, pixelHeight);
		return Task.FromResult<IScreenshotResult?>(result);
	}

	[System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
	static extern IntPtr CGWindowListCreateImage(CGRect screenBounds, CGWindowListOption listOption, uint windowId, CGWindowImageOption imageOption);
}

class MacOSScreenshotResult : IScreenshotResult
{
	readonly NSBitmapImageRep _rep;

	public MacOSScreenshotResult(NSBitmapImageRep rep, int width, int height)
	{
		_rep = rep;
		Width = width;
		Height = height;
	}

	public int Width { get; }
	public int Height { get; }

	public Task<Stream> OpenReadAsync(ScreenshotFormat format = ScreenshotFormat.Png, int quality = 100)
	{
		var data = GetImageData(format, quality);
		Stream stream = data.AsStream();
		return Task.FromResult(stream);
	}

	public async Task CopyToAsync(Stream destination, ScreenshotFormat format = ScreenshotFormat.Png, int quality = 100)
	{
		using var stream = await OpenReadAsync(format, quality);
		await stream.CopyToAsync(destination);
	}

	NSData GetImageData(ScreenshotFormat format, int quality)
	{
		if (format == ScreenshotFormat.Jpeg)
		{
			var props = new NSDictionary(
				NSBitmapImageRep.CompressionFactor,
				new NSNumber(quality / 100.0));
			return _rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Jpeg, props)
				?? throw new InvalidOperationException("Failed to encode JPEG.");
		}

		return _rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png)
			?? throw new InvalidOperationException("Failed to encode PNG.");
	}
}
