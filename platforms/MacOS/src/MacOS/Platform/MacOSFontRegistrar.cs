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
    readonly Dictionary<string, RegisteredFont> _registeredFonts = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _resolvedFonts = new(StringComparer.OrdinalIgnoreCase);
    readonly IEmbeddedFontLoader _fontLoader;
    bool _bundleFontsLoaded;

    public MacOSFontRegistrar(IEmbeddedFontLoader fontLoader)
    {
        _fontLoader = fontLoader;
    }

    public void Register(string filename, string? alias)
    {
        RegisterCore(filename, alias, assembly: null);
    }

    public void Register(string filename, string? alias, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        RegisterCore(filename, alias, assembly);
    }

    void RegisterCore(string filename, string? alias, Assembly? assembly)
    {
        var registration = new RegisteredFont(filename, assembly);
        _registeredFonts[filename] = registration;
        if (!string.IsNullOrEmpty(alias))
            _registeredFonts[alias] = registration;
    }

    public string? GetFont(string font)
    {
        // Check already-resolved cache
        if (_resolvedFonts.TryGetValue(font, out var cached))
            return cached;

        if (_registeredFonts.TryGetValue(font, out var registeredFont) &&
            registeredFont.Assembly is Assembly assembly)
        {
            var embedded = LoadEmbeddedFont(registeredFont.Filename, assembly);
            if (embedded is not null)
            {
                CacheRegistration(registeredFont, embedded);
                return embedded;
            }
        }

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

    string? LoadEmbeddedFont(string filename, Assembly assembly)
    {
        var resourceNames = assembly.GetManifestResourceNames();
        var filenameOnly = System.IO.Path.GetFileName(filename);
        var fullSearchName = "." + filename.Replace('\\', '.').Replace('/', '.');
        var fileOnlySearchName = "." + filenameOnly;

        foreach (var resourceName in resourceNames)
        {
            if (!resourceName.EndsWith(fullSearchName, StringComparison.OrdinalIgnoreCase) &&
                !resourceName.EndsWith(fileOnlySearchName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                continue;
            return _fontLoader.LoadFont(new EmbeddedFont
            {
                FontName = filenameOnly,
                ResourceStream = stream
            });
        }

        return null;
    }

    void CacheRegistration(RegisteredFont registration, string postScriptName)
    {
        _resolvedFonts[registration.Filename] = postScriptName;
        _resolvedFonts[System.IO.Path.GetFileName(registration.Filename)] = postScriptName;
        _resolvedFonts[System.IO.Path.GetFileNameWithoutExtension(registration.Filename)] = postScriptName;

        foreach (var (key, registered) in _registeredFonts)
        {
            if (registered == registration)
                _resolvedFonts[key] = postScriptName;
        }
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
                foreach (var (key, registration) in _registeredFonts)
                {
                    if (registration.Assembly is null &&
                        (string.Equals(registration.Filename, fileName, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(registration.Filename, fileBaseName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _resolvedFonts.TryAdd(key, postScriptName);
                    }
                }
            }
            catch
            {
                // Skip unloadable fonts
            }
        }
    }

    readonly record struct RegisteredFont(string Filename, Assembly? Assembly);
}
