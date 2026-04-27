using CoreGraphics;
using CoreText;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// macOS implementation of IEmbeddedFontLoader.
/// Loads fonts from streams or file paths and registers them with CoreText
/// via CTFontManager so they can be resolved by NSFont.FromFontName().
/// </summary>
public class MacOSEmbeddedFontLoader : IEmbeddedFontLoader
{
    readonly IServiceProvider? _serviceProvider;

    public MacOSEmbeddedFontLoader()
    {
    }

    public MacOSEmbeddedFontLoader(IServiceProvider? serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public string? LoadFont(EmbeddedFont font)
    {
        try
        {
            CGFont? cgFont;

            if (font.ResourceStream == null)
            {
                if (!System.IO.File.Exists(font.FontName))
                    throw new InvalidOperationException("ResourceStream was null.");

                var provider = new CGDataProvider(font.FontName);
                cgFont = CGFont.CreateFromProvider(provider);
            }
            else
            {
                var data = NSData.FromStream(font.ResourceStream);
                if (data == null)
                    throw new InvalidOperationException("Unable to load font stream data.");
                var provider = new CGDataProvider(data);
                cgFont = CGFont.CreateFromProvider(provider);
            }

            if (cgFont == null)
                throw new InvalidOperationException("Unable to load font from the stream.");

            var name = cgFont.PostScriptName;

#pragma warning disable CA1416
#pragma warning disable CA1422
            if (CTFontManager.RegisterGraphicsFont(cgFont, out var error))
                return name;
#pragma warning restore CA1422
#pragma warning restore CA1416

            // Font may already be registered — try to create it
            var nsFont = AppKit.NSFont.FromFontName(name, 10);
            if (nsFont != null)
                return name;

            if (error != null)
                throw new NSErrorException(error);
            else
                throw new InvalidOperationException("Unable to load font from the stream.");
        }
        catch (Exception ex)
        {
            _serviceProvider?.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()
                ?.CreateLogger<MacOSEmbeddedFontLoader>()
                ?.LogWarning(ex, "Unable to register font {Font} with the system.", font.FontName);
        }

        return null;
    }
}
