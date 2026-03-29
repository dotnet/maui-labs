using Microsoft.Maui;
using Platform.Maui.Linux.Gtk4.Platform;
using IImage = Microsoft.Maui.IImage;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class ImageHandler : GtkViewHandler<IImage, Gtk.Picture>
{
	CancellationTokenSource? _imageSourceCts;

	public static new IPropertyMapper<IImage, ImageHandler> Mapper =
		new PropertyMapper<IImage, ImageHandler>(ViewMapper)
		{
			[nameof(IImage.Aspect)] = MapAspect,
			[nameof(IImage.Source)] = MapSource,
			[nameof(IImage.IsAnimationPlaying)] = MapIsAnimationPlaying,
		};

	public ImageHandler() : base(Mapper)
	{
	}

	protected override Gtk.Picture CreatePlatformView()
	{
		var picture = Gtk.Picture.New();
		picture.SetCanShrink(true);
		return picture;
	}

	protected override void DisconnectHandler(Gtk.Picture platformView)
	{
		_imageSourceCts?.Cancel();
		_imageSourceCts?.Dispose();
		_imageSourceCts = null;
		base.DisconnectHandler(platformView);
	}

	public static void MapAspect(ImageHandler handler, IImage image)
	{
		handler.PlatformView?.SetContentFit(image.Aspect switch
		{
			Aspect.AspectFit => Gtk.ContentFit.Contain,
			Aspect.AspectFill => Gtk.ContentFit.Cover,
			Aspect.Fill => Gtk.ContentFit.Fill,
			_ => Gtk.ContentFit.Contain
		});
	}

	public static void MapSource(ImageHandler handler, IImage image)
	{
		handler.UpdateSourceAsync(image.Source);
	}

	public static void MapIsAnimationPlaying(ImageHandler handler, IImage image)
	{
		handler.UpdateSourceAsync(image.Source);
	}

	void UpdateSourceAsync(IImageSource? source)
	{
		_imageSourceCts?.Cancel();
		_imageSourceCts?.Dispose();
		_imageSourceCts = new CancellationTokenSource();
		_ = SetImageAsync(source, _imageSourceCts.Token);
	}

	async Task SetImageAsync(IImageSource? source, CancellationToken cancellationToken)
	{
		try
		{
			var fontManager = MauiContext?.Services.GetService(typeof(IGtkFontManager)) as IGtkFontManager;
			var texture = await GtkImageSourceLoader.LoadTextureAsync(source, cancellationToken, fontManager);
			if (cancellationToken.IsCancellationRequested)
				return;

			GLib.Functions.IdleAdd(0, () =>
			{
				PlatformView?.SetPaintable(texture);
				return false;
			});
		}
		catch (OperationCanceledException)
		{
		}
		catch
		{
			GLib.Functions.IdleAdd(0, () =>
			{
				PlatformView?.SetPaintable(null);
				return false;
			});
		}
	}
}
