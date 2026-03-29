using Microsoft.Maui;
using Platform.Maui.Linux.Gtk4.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class ImageButtonHandler : GtkViewHandler<IImageButton, Gtk.Button>
{
	CancellationTokenSource? _imageSourceCts;

	public static new IPropertyMapper<IImageButton, ImageButtonHandler> Mapper =
		new PropertyMapper<IImageButton, ImageButtonHandler>(ViewMapper)
		{
			[nameof(IImageButton.Padding)] = MapPadding,
			[nameof(IImageButton.Source)] = MapSource,
			[nameof(IImageButton.Aspect)] = MapAspect,
			[nameof(IImageButton.IsAnimationPlaying)] = MapIsAnimationPlaying,
			[nameof(IButtonStroke.CornerRadius)] = MapCornerRadius,
			[nameof(IButtonStroke.StrokeColor)] = MapStrokeColor,
			[nameof(IButtonStroke.StrokeThickness)] = MapStrokeThickness,
		};

	public ImageButtonHandler() : base(Mapper)
	{
	}

	protected override Gtk.Button CreatePlatformView()
	{
		var button = Gtk.Button.New();
		var picture = Gtk.Picture.New();
		picture.SetCanShrink(true);
		picture.SetContentFit(Gtk.ContentFit.Contain);
		button.SetChild(picture);
		return button;
	}

	protected override void ConnectHandler(Gtk.Button platformView)
	{
		base.ConnectHandler(platformView);
		platformView.OnClicked += OnClicked;
	}

	protected override void DisconnectHandler(Gtk.Button platformView)
	{
		_imageSourceCts?.Cancel();
		_imageSourceCts?.Dispose();
		_imageSourceCts = null;
		platformView.OnClicked -= OnClicked;
		base.DisconnectHandler(platformView);
	}

	void OnClicked(Gtk.Button sender, EventArgs args)
	{
		VirtualView?.Clicked();
		VirtualView?.Released();
	}

	public static void MapPadding(ImageButtonHandler handler, IImageButton imageButton)
	{
		var p = imageButton.Padding;
		handler.PlatformView?.SetMarginStart((int)p.Left);
		handler.PlatformView?.SetMarginEnd((int)p.Right);
		handler.PlatformView?.SetMarginTop((int)p.Top);
		handler.PlatformView?.SetMarginBottom((int)p.Bottom);
	}

	public static void MapSource(ImageButtonHandler handler, IImageButton imageButton)
	{
		handler.UpdateSourceAsync(imageButton.Source);
	}

	public static void MapAspect(ImageButtonHandler handler, IImageButton imageButton)
	{
		if (handler.PlatformView?.GetChild() is Gtk.Picture picture)
		{
			picture.SetContentFit(imageButton.Aspect switch
			{
				Aspect.AspectFit => Gtk.ContentFit.Contain,
				Aspect.AspectFill => Gtk.ContentFit.Cover,
				Aspect.Fill => Gtk.ContentFit.Fill,
				_ => Gtk.ContentFit.Contain
			});
		}
	}

	public static void MapIsAnimationPlaying(ImageButtonHandler handler, IImageButton imageButton)
	{
		handler.UpdateSourceAsync(imageButton.Source);
	}

	public static void MapCornerRadius(ImageButtonHandler handler, IImageButton imageButton)
	{
		ApplyBorderCss(handler, imageButton);
	}

	public static void MapStrokeColor(ImageButtonHandler handler, IImageButton imageButton)
	{
		ApplyBorderCss(handler, imageButton);
	}

	public static void MapStrokeThickness(ImageButtonHandler handler, IImageButton imageButton)
	{
		ApplyBorderCss(handler, imageButton);
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
				if (PlatformView?.GetChild() is Gtk.Picture picture)
					picture.SetPaintable(texture);
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
				if (PlatformView?.GetChild() is Gtk.Picture picture)
					picture.SetPaintable(null);
				return false;
			});
		}
	}

	static void ApplyBorderCss(ImageButtonHandler handler, IImageButton imageButton)
	{
		var css = string.Empty;
		if (imageButton.CornerRadius > 0)
			css += $"border-radius: {(int)imageButton.CornerRadius}px; ";

		if (imageButton.StrokeColor != null && imageButton.StrokeThickness > 0)
			css += $"border: {imageButton.StrokeThickness}px solid {ToGtkColor(imageButton.StrokeColor)}; ";
		else if (imageButton.StrokeThickness <= 0)
			css += "border: none; ";

		if (!string.IsNullOrWhiteSpace(css))
			handler.ApplyCss(handler.PlatformView, css);
	}
}
