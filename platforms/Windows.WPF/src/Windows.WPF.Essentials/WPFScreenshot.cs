using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Maui.Media;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials;

public sealed class WPFScreenshot : IScreenshot
#if !NET10_0
	, IViewScreenshot
#endif
{
	public bool IsCaptureSupported => true;

	public async Task<IScreenshotResult> CaptureAsync()
	{
		var application = System.Windows.Application.Current
			?? throw new InvalidOperationException("A WPF application is required to capture a screenshot.");
		return await application.Dispatcher.InvokeAsync(() =>
		{
			var window = application.MainWindow
				?? throw new InvalidOperationException("A WPF main window is required to capture a screenshot.");
			return CaptureElement(window);
		});
	}

#if !NET10_0
	public async Task<IScreenshotResult?> CaptureViewAsync(object platformView)
	{
		if (platformView is not FrameworkElement element)
			throw new ArgumentException("The platform view must be a WPF FrameworkElement.", nameof(platformView));
		return await CaptureElementAsync(element);
	}
#endif

	static async Task<IScreenshotResult> CaptureElementAsync(FrameworkElement element)
	{
		if (!element.Dispatcher.CheckAccess())
			return await element.Dispatcher.InvokeAsync(() => CaptureElement(element));
		return CaptureElement(element);
	}

	static IScreenshotResult CaptureElement(FrameworkElement element)
	{
		if (!element.IsLoaded)
			throw new InvalidOperationException("The WPF element must be loaded before it can be captured.");

		var dpi = VisualTreeHelper.GetDpi(element);
		var width = checked((int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX));
		var height = checked((int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY));
		if (width <= 0 || height <= 0)
			throw new InvalidOperationException("The WPF element has no rendered size.");

		var bitmap = new RenderTargetBitmap(
			width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
		bitmap.Render(element);
		bitmap.Freeze();
		return new WPFScreenshotResult(bitmap);
	}
}

public sealed class WPFScreenshotResult : IScreenshotResult
{
	readonly BitmapSource _bitmap;

	public WPFScreenshotResult(BitmapSource bitmap)
	{
		_bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
		Width = bitmap.PixelWidth;
		Height = bitmap.PixelHeight;
	}

	public int Width { get; }
	public int Height { get; }

	public Task<Stream> OpenReadAsync(ScreenshotFormat format = ScreenshotFormat.Png, int quality = 100)
	{
		var stream = new MemoryStream();
		CreateEncoder(format, quality).Save(stream);
		stream.Position = 0;
		return Task.FromResult<Stream>(stream);
	}

	public async Task CopyToAsync(Stream destination, ScreenshotFormat format = ScreenshotFormat.Png, int quality = 100)
	{
		ArgumentNullException.ThrowIfNull(destination);
		await using var source = await OpenReadAsync(format, quality);
		await source.CopyToAsync(destination);
	}

	BitmapEncoder CreateEncoder(ScreenshotFormat format, int quality)
	{
		BitmapEncoder encoder = format switch
		{
			ScreenshotFormat.Png => new PngBitmapEncoder(),
			ScreenshotFormat.Jpeg when quality is >= 1 and <= 100 => new JpegBitmapEncoder { QualityLevel = quality },
			ScreenshotFormat.Jpeg => throw new ArgumentOutOfRangeException(nameof(quality), quality, "JPEG quality must be between 1 and 100."),
			_ => throw new NotSupportedException($"Screenshot format '{format}' is not supported."),
		};
		encoder.Frames.Add(BitmapFrame.Create(_bitmap));
		return encoder;
	}
}
