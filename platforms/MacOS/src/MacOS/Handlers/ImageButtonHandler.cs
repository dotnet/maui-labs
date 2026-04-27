using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class ImageButtonHandler : MacOSViewHandler<IImageButton, NSButton>
{
    public static readonly IPropertyMapper<IImageButton, ImageButtonHandler> Mapper =
        new PropertyMapper<IImageButton, ImageButtonHandler>(ViewMapper)
        {
            [nameof(IImage.Source)] = MapSource,
            [nameof(IImageButton.Padding)] = MapPadding,
            [nameof(IView.Background)] = MapBackground,
            [nameof(IButtonStroke.CornerRadius)] = MapCornerRadius,
            [nameof(IButtonStroke.StrokeColor)] = MapStrokeColor,
            [nameof(IButtonStroke.StrokeThickness)] = MapStrokeThickness,
        };

    public ImageButtonHandler() : base(Mapper)
    {
    }

    protected override NSButton CreatePlatformView()
    {
        return new NSButton
        {
            BezelStyle = NSBezelStyle.Rounded,
            Title = string.Empty,
            ImagePosition = NSCellImagePosition.ImageOnly,
            Bordered = true,
        };
    }

    protected override void ConnectHandler(NSButton platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Activated += OnActivated;
    }

    protected override void DisconnectHandler(NSButton platformView)
    {
        platformView.Activated -= OnActivated;
        base.DisconnectHandler(platformView);
    }

    void OnActivated(object? sender, EventArgs e)
    {
        VirtualView?.Clicked();
    }

    public static void MapSource(ImageButtonHandler handler, IImageButton imageButton)
    {
        if (imageButton.Source is IFileImageSource fileSource)
        {
            var image = new NSImage(fileSource.File);
            handler.PlatformView.Image = image;
        }
        else if (imageButton.Source is IUriImageSource uriSource)
        {
            _ = LoadImageFromUri(handler, uriSource.Uri);
        }
        else if (imageButton.Source is IFontImageSource fontSource)
        {
            handler.PlatformView.Image = FontImageSourceHelper.CreateImage(fontSource, handler.MauiContext);
        }
        handler.PlatformView.InvalidateIntrinsicContentSize();
    }

    static async Task LoadImageFromUri(ImageButtonHandler handler, Uri uri)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient();
            var data = await client.GetByteArrayAsync(uri);
            var nsImage = new NSImage(Foundation.NSData.FromArray(data));
            handler.PlatformView.Image = nsImage;
            handler.PlatformView.InvalidateIntrinsicContentSize();
        }
        catch
        {
            // Image load failed — leave button without image
        }
    }

    public static void MapPadding(ImageButtonHandler handler, IImageButton imageButton)
    {
        // NSButton doesn't expose direct padding
    }

    public static void MapBackground(ImageButtonHandler handler, IImageButton imageButton)
    {
        if (imageButton.Background is Graphics.SolidPaint solidPaint && solidPaint.Color != null)
        {
            handler.PlatformView.WantsLayer = true;
            handler.PlatformView.Bordered = false;
            handler.PlatformView.Layer!.BackgroundColor = solidPaint.Color.ToPlatformColor().CGColor;
        }
        else
        {
            handler.PlatformView.Bordered = true;
        }
    }

    public static void MapCornerRadius(ImageButtonHandler handler, IImageButton imageButton)
    {
        if (imageButton is IButtonStroke stroke && stroke.CornerRadius >= 0)
        {
            handler.PlatformView.WantsLayer = true;
            handler.PlatformView.Layer!.CornerRadius = (nfloat)stroke.CornerRadius;
        }
    }

    public static void MapStrokeColor(ImageButtonHandler handler, IImageButton imageButton)
    {
        if (imageButton is IButtonStroke stroke && stroke.StrokeColor != null)
        {
            handler.PlatformView.WantsLayer = true;
            handler.PlatformView.Layer!.BorderColor = stroke.StrokeColor.ToPlatformColor().CGColor;
        }
    }

    public static void MapStrokeThickness(ImageButtonHandler handler, IImageButton imageButton)
    {
        if (imageButton is IButtonStroke stroke && stroke.StrokeThickness >= 0)
        {
            handler.PlatformView.WantsLayer = true;
            handler.PlatformView.Layer!.BorderWidth = (nfloat)stroke.StrokeThickness;
        }
    }
}
