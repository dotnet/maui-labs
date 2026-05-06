using CoreGraphics;
using Foundation;
using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// NSImageView subclass that uses flipped coordinates and reports intrinsic content size.
/// </summary>
internal class FlippedNSImageView : NSImageView
{
    public FlippedNSImageView()
    {
        WantsLayer = true;
        ImageScaling = NSImageScale.ProportionallyUpOrDown;
    }

    public override bool IsFlipped => true;
}

public partial class ImageHandler : MacOSViewHandler<IImage, NSImageView>
{
    public static readonly IPropertyMapper<IImage, ImageHandler> Mapper =
        new PropertyMapper<IImage, ImageHandler>(ViewMapper)
        {
            [nameof(IImage.Aspect)] = MapAspect,
            [nameof(IImage.IsOpaque)] = MapIsOpaque,
            [nameof(IImageSourcePart.Source)] = MapSource,
        };

    public ImageHandler() : base(Mapper)
    {
    }

    protected override NSImageView CreatePlatformView()
    {
        return new FlippedNSImageView();
    }

    public static void MapAspect(ImageHandler handler, IImage image)
    {
        handler.PlatformView.ImageScaling = image.Aspect switch
        {
            Aspect.AspectFill => NSImageScale.ProportionallyUpOrDown,
            Aspect.Fill => NSImageScale.AxesIndependently,
            Aspect.Center => NSImageScale.None,
            _ => NSImageScale.ProportionallyDown, // AspectFit
        };
    }

    public static void MapIsOpaque(ImageHandler handler, IImage image)
    {
        // NSImageView doesn't have a direct Opaque property like UIKit
        // WantsLayer + layer.Opaque is the closest equivalent
        if (handler.PlatformView.Layer != null)
            handler.PlatformView.Layer.Opaque = image.IsOpaque;
    }

    public static void MapSource(ImageHandler handler, IImage image)
    {
        if (image is not IImageSourcePart imageSourcePart)
            return;

        handler.LoadImageSource(imageSourcePart);
    }

    void LoadImageSource(IImageSourcePart imageSourcePart)
    {
        var source = imageSourcePart.Source;
        if (source == null)
        {
            PlatformView.Image = null;
            PlatformView.InvalidateIntrinsicContentSize();
            imageSourcePart.UpdateIsLoading(false);
            return;
        }

        imageSourcePart.UpdateIsLoading(true);

        if (source is IFileImageSource fileImageSource)
        {
            var fileName = fileImageSource.File;
            if (!string.IsNullOrEmpty(fileName))
            {
                var nsImage = FindBundleImage(fileName);
                PlatformView.Image = nsImage;
            }

            PlatformView.InvalidateIntrinsicContentSize();
            imageSourcePart.UpdateIsLoading(false);
        }
        else if (source is IUriImageSource uriImageSource)
        {
            var uri = uriImageSource.Uri;
            if (uri != null)
            {
                LoadFromUri(uri, imageSourcePart);
            }
            else
            {
                PlatformView.Image = null;
                PlatformView.InvalidateIntrinsicContentSize();
                imageSourcePart.UpdateIsLoading(false);
            }
        }
        else if (source is IStreamImageSource streamImageSource)
        {
            LoadFromStream(streamImageSource, imageSourcePart);
        }
        else if (source is IFontImageSource fontImageSource)
        {
            LoadFromFontGlyph(fontImageSource, imageSourcePart);
        }
        else
        {
            PlatformView.Image = null;
            PlatformView.InvalidateIntrinsicContentSize();
            imageSourcePart.UpdateIsLoading(false);
        }
    }

    /// <summary>
    /// Searches for an image in the app bundle, trying the exact name/extension first,
    /// then alternate extensions (.svg, .pdf) and subdirectories (Images/).
    /// MAUI MauiImage resources may be SVGs that aren't converted to PNGs on macOS.
    /// </summary>
    static NSImage? FindBundleImage(string fileName)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var ext = System.IO.Path.GetExtension(fileName)?.TrimStart('.');

        // Extensions to try: requested extension first, then common MAUI image formats
        var extensions = new List<string>();
        if (!string.IsNullOrEmpty(ext))
            extensions.Add(ext);
        foreach (var alt in new[] { "png", "svg", "pdf", "jpg", "jpeg" })
        {
            if (!extensions.Contains(alt, StringComparer.OrdinalIgnoreCase))
                extensions.Add(alt);
        }

        // Subdirectories to try: root first, then Images/ and any child dirs
        var subdirs = new List<string?> { null, "Images" };

        // Discover nested subdirectories under Images/ (e.g. Images/toolbox/)
        var resourcePath = NSBundle.MainBundle.ResourcePath;
        if (resourcePath != null)
        {
            var imagesDir = System.IO.Path.Combine(resourcePath, "Images");
            if (System.IO.Directory.Exists(imagesDir))
            {
                foreach (var dir in System.IO.Directory.GetDirectories(imagesDir, "*", System.IO.SearchOption.AllDirectories))
                {
                    var rel = System.IO.Path.GetRelativePath(resourcePath, dir);
                    if (!subdirs.Contains(rel))
                        subdirs.Add(rel);
                }
            }
        }

        foreach (var subdir in subdirs)
        {
            foreach (var tryExt in extensions)
            {
                var path = subdir == null
                    ? NSBundle.MainBundle.PathForResource(name, tryExt)
                    : NSBundle.MainBundle.PathForResource(name, tryExt, subdir);

                if (path != null)
                {
                    try { return new NSImage(path); } catch { }
                }
            }
        }

        // Try direct file path
        if (System.IO.File.Exists(fileName))
        {
            try { return new NSImage(fileName); } catch { }
        }

        // Last resort: NSImage.ImageNamed (searches asset catalogs)
        return NSImage.ImageNamed(fileName);
    }

    async void LoadFromUri(Uri uri, IImageSourcePart imageSourcePart)
    {
        try
        {
            using var client = new HttpClient();
            var data = await client.GetByteArrayAsync(uri);
            var nsData = NSData.FromArray(data);
            var nsImage = new NSImage(nsData);

            if (PlatformView != null)
            {
                PlatformView.Image = nsImage;
                PlatformView.InvalidateIntrinsicContentSize();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load image from URI: {ex.Message}");
        }
        finally
        {
            imageSourcePart.UpdateIsLoading(false);
        }
    }

    async void LoadFromStream(IStreamImageSource streamSource, IImageSourcePart imageSourcePart)
    {
        try
        {
            var stream = await streamSource.GetStreamAsync(CancellationToken.None);
            if (stream != null)
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                var nsData = NSData.FromArray(ms.ToArray());
                var nsImage = new NSImage(nsData);

                if (PlatformView != null)
                {
                    PlatformView.Image = nsImage;
                    PlatformView.InvalidateIntrinsicContentSize();
                }

                stream.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load image from stream: {ex.Message}");
        }
        finally
        {
            imageSourcePart.UpdateIsLoading(false);
        }
    }

    void LoadFromFontGlyph(IFontImageSource fontImageSource, IImageSourcePart imageSourcePart)
    {
        var image = FontImageSourceHelper.CreateImage(fontImageSource, MauiContext);
        PlatformView.Image = image;
        PlatformView.InvalidateIntrinsicContentSize();
        imageSourcePart.UpdateIsLoading(false);
    }
}
