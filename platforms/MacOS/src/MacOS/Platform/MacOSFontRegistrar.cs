using System.Reflection;
using CoreGraphics;
using CoreText;
using Foundation;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// macOS-specific font registrar that wraps the default MAUI FontRegistrar.
/// MAUI's default FindFont() can't locate font files in the macOS bundle's
/// Resources/Fonts/ subdirectory. This registrar pre-loads all bundle fonts
/// at first use, then resolves aliases to PostScript names directly.
/// </summary>
public class MacOSFontRegistrar : IFontRegistrar
{
    readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _resolvedFonts = new(StringComparer.OrdinalIgnoreCase);
    readonly IEmbeddedFontLoader _fontLoader;
    bool _bundleFontsLoaded;

    public MacOSFontRegistrar(IEmbeddedFontLoader fontLoader)
    {
        _fontLoader = fontLoader;
    }

    public void Register(string filename, string? alias)
    {
        _aliases[filename] = filename;
        if (!string.IsNullOrEmpty(alias))
            _aliases[alias] = filename;
    }

    public void Register(string filename, string? alias, Assembly assembly)
        => Register(filename, alias);

    public string? GetFont(string font)
    {
        // Check already-resolved cache
        if (_resolvedFonts.TryGetValue(font, out var cached))
            return cached;

        // Ensure bundle fonts are loaded
        if (!_bundleFontsLoaded)
            LoadBundleFonts();

        // Try again after loading
        if (_resolvedFonts.TryGetValue(font, out cached))
            return cached;

        // Try direct NSFont lookup (system fonts)
        var nsFont = AppKit.NSFont.FromFontName(font, 13);
        if (nsFont != null)
        {
            _resolvedFonts[font] = font;
            return font;
        }

        return null;
    }

    void LoadBundleFonts()
    {
        _bundleFontsLoaded = true;

        var fontsDir = System.IO.Path.Combine(
            NSBundle.MainBundle.ResourcePath ?? string.Empty, "Fonts");

        if (!System.IO.Directory.Exists(fontsDir))
            return;

        foreach (var fontFile in System.IO.Directory.GetFiles(fontsDir, "*.ttf")
            .Concat(System.IO.Directory.GetFiles(fontsDir, "*.otf")))
        {
            try
            {
                var provider = new CGDataProvider(fontFile);
                var cgFont = CGFont.CreateFromProvider(provider);
                if (cgFont?.PostScriptName == null) continue;

                var postScriptName = cgFont.PostScriptName;

#pragma warning disable CA1416
#pragma warning disable CA1422
                CTFontManager.RegisterGraphicsFont(cgFont, out _);
#pragma warning restore CA1422
#pragma warning restore CA1416

                var fileBaseName = System.IO.Path.GetFileNameWithoutExtension(fontFile);
                var fileName = System.IO.Path.GetFileName(fontFile);

                // Map filename (with and without extension) → PostScript name
                _resolvedFonts.TryAdd(postScriptName, postScriptName);
                _resolvedFonts.TryAdd(fileName, postScriptName);
                _resolvedFonts.TryAdd(fileBaseName, postScriptName);

                // Map any registered alias that points to this filename → PostScript name
                foreach (var kvp in _aliases)
                {
                    if (string.Equals(kvp.Value, fileName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(kvp.Value, fileBaseName, StringComparison.OrdinalIgnoreCase))
                    {
                        _resolvedFonts.TryAdd(kvp.Key, postScriptName);
                    }
                }
            }
            catch
            {
                // Skip unloadable fonts
            }
        }
    }
}
