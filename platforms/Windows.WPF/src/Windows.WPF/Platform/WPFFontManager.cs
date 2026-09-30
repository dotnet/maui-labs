using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.Platforms.Windows.WPF;

/// <summary>Resolves MAUI fonts to native WPF families.</summary>
public class WPFFontManager : IFontManager
{
	readonly ConcurrentDictionary<string, (FontFamily Family, bool Resolved)> _fontCache = new();
	readonly ConcurrentDictionary<string, byte> _warnedFamilies = new();
	readonly IFontRegistrar _fontRegistrar;
	readonly ILogger<WPFFontManager>? _logger;

	public WPFFontManager(IFontRegistrar fontRegistrar, ILogger<WPFFontManager>? logger = null)
	{
		_fontRegistrar = fontRegistrar;
		_logger = logger;
	}

	public double DefaultFontSize => 14.0;
	public FontFamily DefaultFontFamily => SystemFonts.MessageFontFamily;

	public FontFamily GetFontFamily(Font font)
		=> TryGetFontFamily(font, out var family) ? family : DefaultFontFamily;

	internal bool TryGetFontFamily(Font font, out FontFamily family)
	{
		family = DefaultFontFamily;
		if (string.IsNullOrEmpty(font.Family))
			return true;
		if (_fontCache.TryGetValue(font.Family, out var cached))
		{
			family = cached.Family;
			return cached.Resolved;
		}

		try
		{
			var path = _fontRegistrar.GetFont(font.Family);
			if (path != null)
			{
				// Let WPF determine its family name (which can differ from GlyphTypeface.FamilyNames).
				var absolutePath = Path.GetFullPath(path, AppContext.BaseDirectory);
				family = Fonts.GetFontFamilies(new Uri(absolutePath).AbsoluteUri).FirstOrDefault(candidate =>
					candidate.GetTypefaces().Any(face => face.TryGetGlyphTypeface(out var glyph) &&
						string.Equals(glyph.FontUri.LocalPath, absolutePath, StringComparison.OrdinalIgnoreCase)))
					?? throw new FileNotFoundException($"No native font family found in '{absolutePath}'.", absolutePath);
			}
			else
				family = new FontFamily(font.Family);

			if (!family.GetTypefaces().Any(face => face.TryGetGlyphTypeface(out _)))
				throw new FileNotFoundException($"No native typeface found for '{font.Family}'.", path);

			_fontCache.TryAdd(font.Family, (family, true));
			return true;
		}
		catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
		{
			family = DefaultFontFamily;
			if (ex is FileNotFoundException)
				_fontCache.TryAdd(font.Family, (family, false));
			if (_warnedFamilies.TryAdd(font.Family, 0))
			{
				if (_logger != null)
					_logger.LogWarning(ex, "Unable to resolve font {Family}; using the default font for text.", font.Family);
				else
					System.Diagnostics.Trace.TraceWarning("Unable to resolve font {0}: {1}", font.Family, ex.Message);
			}
			return false;
		}
	}

	internal static WPFFontManager FromContext(IMauiContext? context)
		=> context?.Services.GetRequiredService<IFontManager>() as WPFFontManager
			?? throw new InvalidOperationException("A WPF font manager is required to resolve fonts.");

	internal static void ApplyFontFamily(DependencyObject target, Font font, IMauiContext? context)
	{
		if (string.IsNullOrEmpty(font.Family))
			target.ClearValue(System.Windows.Documents.TextElement.FontFamilyProperty);
		else
			target.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, FromContext(context).GetFontFamily(font));
	}

	public double GetFontSize(Font font, double defaultFontSize = 0)
		=> font.Size > 0 ? font.Size : defaultFontSize > 0 ? defaultFontSize : DefaultFontSize;
}

/// <summary>Maps registered filenames and aliases to app-local or extracted font files.</summary>
public class WPFFontRegistrar : IFontRegistrar
{
	readonly ConcurrentDictionary<string, Lazy<string>> _fonts = new(StringComparer.Ordinal);
	readonly IEmbeddedFontLoader _loader;

	public WPFFontRegistrar() : this(new WPFEmbeddedFontLoader()) { }

	public WPFFontRegistrar(IEmbeddedFontLoader loader)
	{
		ArgumentNullException.ThrowIfNull(loader);
		_loader = loader;
	}

	public string? GetFont(string font) => _fonts.TryGetValue(font, out var path) ? path.Value : null;

	public void Register(string filename, string? alias, Assembly assembly)
	{
		var path = new Lazy<string>(() =>
		{
			var names = assembly.GetManifestResourceNames();
			var resource = names.FirstOrDefault(name => name.Equals(filename, StringComparison.Ordinal));
			if (resource == null)
			{
				var matches = names.Where(name => name.EndsWith("." + filename, StringComparison.OrdinalIgnoreCase)).ToArray();
				if (matches.Length > 1)
					throw new FileLoadException($"Embedded font '{filename}' is ambiguous: {string.Join(", ", matches)}. Register its full resource name.");
				resource = matches.SingleOrDefault();
			}
			if (resource == null)
				throw new FileNotFoundException($"Embedded font '{filename}' was not found in '{assembly.FullName}'.");
			using var stream = assembly.GetManifestResourceStream(resource)!;
			return _loader.LoadFont(new EmbeddedFont { FontName = filename, ResourceStream = stream })
				?? throw new IOException($"Unable to extract embedded font '{filename}'.");
		}, LazyThreadSafetyMode.PublicationOnly);
		Register(filename, alias, path);
	}

	public void Register(string filename, string? alias)
	{
		var path = new Lazy<string>(() =>
		{
			if (Path.IsPathFullyQualified(filename))
				return filename;
			var packagedPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts", Path.GetFileName(filename));
			return File.Exists(packagedPath) ? packagedPath : Path.GetFullPath(filename, AppContext.BaseDirectory);
		}, LazyThreadSafetyMode.PublicationOnly);
		Register(filename, alias, path);
	}

	void Register(string filename, string? alias, Lazy<string> path)
	{
		_fonts[filename] = path;
		if (!string.IsNullOrEmpty(alias))
			_fonts[alias] = path;
	}
}

/// <summary>Extracts embedded font content without installing system fonts.</summary>
public class WPFEmbeddedFontLoader : IEmbeddedFontLoader
{
	public WPFEmbeddedFontLoader() { }

	// Retained for callers of the original public constructor; extraction does not require a registrar.
	public WPFEmbeddedFontLoader(IFontRegistrar registrar) { }

	public string? LoadFont(EmbeddedFont font)
	{
		ArgumentNullException.ThrowIfNull(font);
		if (font.ResourceStream == null || string.IsNullOrEmpty(font.FontName))
			throw new ArgumentException("An embedded font requires a name and resource stream.", nameof(font));

		using var content = new MemoryStream();
		font.ResourceStream.CopyTo(content);
		var bytes = content.ToArray();
		var hash = Convert.ToHexString(SHA256.HashData(bytes));
		var directory = Path.Combine(Path.GetTempPath(), "MauiWPFFonts", hash);
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, Path.GetFileName(font.FontName));
		if (!File.Exists(path))
		{
			var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
			try
			{
				File.WriteAllBytes(temporary, bytes);
				try
				{
					File.Move(temporary, path);
				}
				catch (IOException) when (File.Exists(path))
				{
					// Another caller already published this same content-addressed font.
				}
			}
			finally
			{
				File.Delete(temporary);
			}
		}
		return path;
	}
}
