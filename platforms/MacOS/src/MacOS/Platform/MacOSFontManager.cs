using AppKit;
using Foundation;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// macOS implementation of IFontManager. Maps MAUI Font abstractions to NSFont.
/// </summary>
public class MacOSFontManager : IFontManager
{
    readonly IFontRegistrar _fontRegistrar;

    public MacOSFontManager(IFontRegistrar fontRegistrar, IServiceProvider? serviceProvider = null)
    {
        _fontRegistrar = fontRegistrar;
    }

    public double DefaultFontSize => 13.0;

    public NSFont DefaultFont => NSFont.SystemFontOfSize((nfloat)DefaultFontSize);

    public NSFont GetFont(Font font, double defaultFontSize = 0)
    {
        var size = font.Size > 0 ? (nfloat)font.Size
                 : defaultFontSize > 0 ? (nfloat)defaultFontSize
                 : (nfloat)DefaultFontSize;

        NSFont? nsFont = null;

        if (!string.IsNullOrEmpty(font.Family))
        {
            // Try registered/embedded fonts (resolves aliases like "FluentUI")
            if (_fontRegistrar.GetFont(font.Family) is string registeredFont)
                nsFont = NSFont.FromFontName(registeredFont, size);

            // Fall back to direct system font lookup
            nsFont ??= NSFont.FromFontName(font.Family, size);
        }

        nsFont ??= NSFont.SystemFontOfSize(size);

        // Apply weight traits
        var manager = NSFontManager.SharedFontManager;
        if (font.Weight >= FontWeight.Bold)
            nsFont = manager.ConvertFont(nsFont, NSFontTraitMask.Bold) ?? nsFont;
        else if (font.Weight <= FontWeight.Light)
            nsFont = manager.ConvertFont(nsFont, NSFontTraitMask.Unbold) ?? nsFont;

        // Apply italic via slant
        if (font.Slant == FontSlant.Italic || font.Slant == FontSlant.Oblique)
            nsFont = manager.ConvertFont(nsFont, NSFontTraitMask.Italic) ?? nsFont;

        return nsFont;
    }
}
