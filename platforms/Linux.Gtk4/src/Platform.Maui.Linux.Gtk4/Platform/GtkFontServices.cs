using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;

namespace Platform.Maui.Linux.Gtk4.Platform;

internal interface IGtkFontRegistry
{
	bool TryGetFontPath(string fontKey, out string fontPath);
	IEnumerable<string> GetAllRegisteredKeys();
}

internal interface IGtkFontManager
{
	string BuildFontCss(Microsoft.Maui.Font font);
	void EagerlyRegisterAllFonts();
}

internal sealed class GtkFontRegistrar : IFontRegistrar, IGtkFontRegistry
{
	static readonly string[] FontExtensions = [".ttf", ".otf", ".ttc"];

	readonly ConcurrentDictionary<string, RegisteredFont> _registeredFonts = new(StringComparer.OrdinalIgnoreCase);
	readonly ConcurrentDictionary<string, string?> _fontLookupCache = new(StringComparer.OrdinalIgnoreCase);
	readonly IEmbeddedFontLoader _embeddedFontLoader;
	readonly ILogger<GtkFontRegistrar> _logger;

	public GtkFontRegistrar(IEmbeddedFontLoader embeddedFontLoader, ILogger<GtkFontRegistrar> logger)
	{
		_embeddedFontLoader = embeddedFontLoader;
		_logger = logger;
	}

	public void Register(string filename, string? alias, Assembly assembly) => RegisterCore(filename, alias, assembly);

	public void Register(string filename, string? alias) => RegisterCore(filename, alias, null);

	public string? GetFont(string font)
	{
		if (string.IsNullOrWhiteSpace(font))
			return null;

		if (_fontLookupCache.TryGetValue(font, out var cachedPath))
			return cachedPath;

		var resolvedPath = ResolveFontPath(font);
		_fontLookupCache[font] = resolvedPath;
		return resolvedPath;
	}

	public bool TryGetFontPath(string fontKey, out string fontPath)
	{
		var resolvedPath = GetFont(fontKey);
		if (!string.IsNullOrWhiteSpace(resolvedPath))
		{
			fontPath = resolvedPath;
			return true;
		}

		fontPath = string.Empty;
		return false;
	}

	public IEnumerable<string> GetAllRegisteredKeys() => _registeredFonts.Keys;

	void RegisterCore(string filename, string? alias, Assembly? assembly)
	{
		var registration = new RegisteredFont(filename, assembly);
		_registeredFonts[filename] = registration;

		if (!string.IsNullOrWhiteSpace(alias))
			_registeredFonts[alias] = registration;
	}

	string? ResolveFontPath(string key)
	{
		try
		{
			if (File.Exists(key))
				return Path.GetFullPath(key);

			if (_registeredFonts.TryGetValue(key, out var registration))
				return ResolveRegisteredFont(registration);

			return ResolveNativeFontPath(key);
		}
		catch (FileNotFoundException ex)
		{
			_logger.LogWarning(ex, "Unable to resolve font path for '{FontKey}'.", key);
			return null;
		}
		catch (IOException ex)
		{
			_logger.LogWarning(ex, "Unable to resolve font path for '{FontKey}'.", key);
			return null;
		}
		catch (UnauthorizedAccessException ex)
		{
			_logger.LogWarning(ex, "Unable to resolve font path for '{FontKey}'.", key);
			return null;
		}
		catch (InvalidOperationException ex)
		{
			_logger.LogWarning(ex, "Unable to resolve font path for '{FontKey}'.", key);
			return null;
		}
	}

	string? ResolveRegisteredFont(RegisteredFont registration)
	{
		if (registration.Assembly is Assembly assembly)
		{
			using var stream = GetEmbeddedResourceStream(registration.Filename, assembly);
			var embeddedFont = new EmbeddedFont
			{
				FontName = Path.GetFileName(registration.Filename),
				ResourceStream = stream
			};

			return _embeddedFontLoader.LoadFont(embeddedFont);
		}

		return ResolveNativeFontPath(registration.Filename);
	}

	static Stream GetEmbeddedResourceStream(string filename, Assembly assembly)
	{
		var resourceNames = assembly.GetManifestResourceNames();
		var filenameOnly = Path.GetFileName(filename);
		var fullSearchName = "." + filename.Replace('\\', '.').Replace('/', '.');
		var fileOnlySearchName = "." + filenameOnly;

		foreach (var name in resourceNames)
		{
			if (name.EndsWith(fullSearchName, StringComparison.OrdinalIgnoreCase) ||
				name.EndsWith(fileOnlySearchName, StringComparison.OrdinalIgnoreCase))
			{
				return assembly.GetManifestResourceStream(name)!;
			}
		}

		throw new FileNotFoundException($"Resource ending with {filename} not found.");
	}

	static string? ResolveNativeFontPath(string filename)
	{
		foreach (var candidatePath in BuildPathCandidates(filename))
		{
			if (File.Exists(candidatePath))
				return Path.GetFullPath(candidatePath);
		}

		return null;
	}

	static IEnumerable<string> BuildPathCandidates(string filename)
	{
		var normalized = filename.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
		var fileNameOnly = Path.GetFileName(normalized);
		var hasExtension = Path.HasExtension(normalized);
		var baseDirectory = AppContext.BaseDirectory;

		if (Path.IsPathRooted(normalized))
		{
			yield return normalized;

			if (!hasExtension)
			{
				foreach (var extension in FontExtensions)
					yield return normalized + extension;
			}

			yield break;
		}

		yield return Path.Combine(baseDirectory, normalized);
		yield return Path.Combine(baseDirectory, fileNameOnly);

		if (!hasExtension)
		{
			foreach (var extension in FontExtensions)
			{
				yield return Path.Combine(baseDirectory, normalized + extension);
				yield return Path.Combine(baseDirectory, fileNameOnly + extension);
			}
		}
	}

	readonly struct RegisteredFont
	{
		public RegisteredFont(string filename, Assembly? assembly)
		{
			Filename = filename;
			Assembly = assembly;
		}

		public string Filename { get; }
		public Assembly? Assembly { get; }
	}
}

internal sealed class GtkFontManager : IFontManager, IGtkFontManager
{
	readonly ConcurrentDictionary<string, string> _fontFamilyCache = new(StringComparer.OrdinalIgnoreCase);
	readonly ConcurrentDictionary<string, byte> _registeredFontFiles = new(StringComparer.OrdinalIgnoreCase);
	readonly IGtkFontRegistry _fontRegistry;
	readonly ILogger<GtkFontManager> _logger;

	public GtkFontManager(IGtkFontRegistry fontRegistry, ILogger<GtkFontManager> logger)
	{
		_fontRegistry = fontRegistry;
		_logger = logger;
	}

	public double DefaultFontSize => 14;

	/// <summary>
	/// Eagerly extract, install, and register all known embedded fonts with
	/// fontconfig/Pango so they are available before any widget is rendered.
	/// </summary>
	public void EagerlyRegisterAllFonts()
	{
		var anyRegistered = false;
		foreach (var key in _fontRegistry.GetAllRegisteredKeys())
		{
			if (_fontRegistry.TryGetFontPath(key, out var fontPath) &&
				!string.IsNullOrWhiteSpace(fontPath))
			{
				if (EnsureFontRegistered(fontPath))
					anyRegistered = true;
			}
		}

		if (anyRegistered)
			FontConfigNative.NotifyFontMapChanged();
	}

	public string BuildFontCss(Microsoft.Maui.Font font)
	{
		if (font == Microsoft.Maui.Font.Default)
			return string.Empty;

		var css = new StringBuilder();

		if (font.Size > 0 && !double.IsNaN(font.Size))
			css.Append($"font-size: {font.Size}pt; ");

		var family = ResolveFontFamily(font.Family);
		if (!string.IsNullOrWhiteSpace(family))
			css.Append($"font-family: \"{EscapeCss(family)}\"; ");

		AppendWeightAndSlant(css, font);

		return css.ToString();
	}

	internal static string BuildFallbackFontCss(Microsoft.Maui.Font font)
	{
		if (font == Microsoft.Maui.Font.Default)
			return string.Empty;

		var css = new StringBuilder();

		if (font.Size > 0 && !double.IsNaN(font.Size))
			css.Append($"font-size: {font.Size}pt; ");

		if (!string.IsNullOrWhiteSpace(font.Family))
			css.Append($"font-family: \"{EscapeCss(font.Family)}\"; ");

		AppendWeightAndSlant(css, font);

		return css.ToString();
	}

	string? ResolveFontFamily(string? fontFamily)
	{
		if (string.IsNullOrWhiteSpace(fontFamily))
			return null;

		return _fontFamilyCache.GetOrAdd(fontFamily, ResolveFontFamilyCore);
	}

	string ResolveFontFamilyCore(string fontFamily)
	{
		if (!_fontRegistry.TryGetFontPath(fontFamily, out var fontPath))
			return fontFamily;

		if (EnsureFontRegistered(fontPath))
			FontConfigNative.NotifyFontMapChanged();

		try
		{
			return TryReadFontFamilyName(fontPath) ?? Path.GetFileNameWithoutExtension(fontPath) ?? fontFamily;
		}
		catch (IOException ex)
		{
			_logger.LogWarning(ex, "Unable to parse font metadata for '{FontPath}'.", fontPath);
		}
		catch (InvalidDataException ex)
		{
			_logger.LogWarning(ex, "Unable to parse font metadata for '{FontPath}'.", fontPath);
		}
		catch (DecoderFallbackException ex)
		{
			_logger.LogWarning(ex, "Unable to decode font metadata for '{FontPath}'.", fontPath);
		}

		return Path.GetFileNameWithoutExtension(fontPath) ?? fontFamily;
	}

	bool EnsureFontRegistered(string fontPath)
	{
		if (!File.Exists(fontPath) || !_registeredFontFiles.TryAdd(fontPath, 0))
			return false;

		// Install to user font directory so fontconfig/Pango picks it up
		// reliably (FcConfigAppFontAddFile may not propagate to GTK4's
		// Pango context which caches fontconfig state at startup).
		try
		{
			var userFontDir = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"fonts");
			Directory.CreateDirectory(userFontDir);
			var destPath = Path.Combine(userFontDir, Path.GetFileName(fontPath));
			if (!File.Exists(destPath))
				File.Copy(fontPath, destPath, overwrite: false);
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Could not install font to user directory; falling back to FcConfigAppFontAddFile.");
		}

		if (!FontConfigNative.TryAddAppFont(fontPath))
			_logger.LogWarning("Unable to register app font '{FontPath}' with fontconfig.", fontPath);

		return true;
	}

	static string? TryReadFontFamilyName(string fontPath)
	{
		using var stream = File.OpenRead(fontPath);
		using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

		if (stream.Length < 12)
			return null;

		stream.Position = 4;
		var tableCount = ReadUInt16BigEndian(reader);
		stream.Position += 6;

		uint nameTableOffset = 0;
		uint nameTableLength = 0;

		for (var i = 0; i < tableCount; i++)
		{
			var tag = ReadTag(reader);
			stream.Position += 4; // checksum
			var offset = ReadUInt32BigEndian(reader);
			var length = ReadUInt32BigEndian(reader);

			if (tag == "name")
			{
				nameTableOffset = offset;
				nameTableLength = length;
				break;
			}
		}

		if (nameTableOffset == 0 || nameTableLength == 0 || nameTableOffset + nameTableLength > stream.Length)
			return null;

		stream.Position = nameTableOffset;
		_ = ReadUInt16BigEndian(reader); // format
		var nameRecordCount = ReadUInt16BigEndian(reader);
		var stringStorageOffset = ReadUInt16BigEndian(reader);
		var recordsOffset = stream.Position;

		string? firstFamily = null;
		for (var i = 0; i < nameRecordCount; i++)
		{
			stream.Position = recordsOffset + (i * 12);
			var platformId = ReadUInt16BigEndian(reader);
			var encodingId = ReadUInt16BigEndian(reader);
			var languageId = ReadUInt16BigEndian(reader);
			var nameId = ReadUInt16BigEndian(reader);
			var length = ReadUInt16BigEndian(reader);
			var offset = ReadUInt16BigEndian(reader);

			if (nameId != 1)
				continue;

			var absoluteStringOffset = nameTableOffset + stringStorageOffset + offset;
			if (absoluteStringOffset + length > stream.Length)
				continue;

			stream.Position = absoluteStringOffset;
			var data = reader.ReadBytes(length);
			var decodedFamily = DecodeNameRecord(platformId, encodingId, data);
			if (string.IsNullOrWhiteSpace(decodedFamily))
				continue;

			var normalized = decodedFamily.Trim();
			if (languageId == 0x0409)
				return normalized;

			firstFamily ??= normalized;
		}

		return firstFamily;
	}

	static string? DecodeNameRecord(ushort platformId, ushort encodingId, byte[] data)
	{
		if (data.Length == 0)
			return null;

		try
		{
			if (platformId == 0 || platformId == 3)
				return Encoding.BigEndianUnicode.GetString(data);

			if (platformId == 1 && encodingId == 0)
				return Encoding.ASCII.GetString(data);
		}
		catch (DecoderFallbackException)
		{
			return null;
		}
		catch (ArgumentException)
		{
			return null;
		}

		return null;
	}

	static void AppendWeightAndSlant(StringBuilder css, Microsoft.Maui.Font font)
	{
		if (font.Weight != FontWeight.Regular)
			css.Append($"font-weight: {(int)font.Weight}; ");

		if (font.Slant == FontSlant.Italic)
			css.Append("font-style: italic; ");
	}

	static string EscapeCss(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
		.Replace("\"", "\\\"", StringComparison.Ordinal);

	static ushort ReadUInt16BigEndian(BinaryReader reader)
	{
		var b1 = reader.ReadByte();
		var b2 = reader.ReadByte();
		return (ushort)((b1 << 8) | b2);
	}

	static uint ReadUInt32BigEndian(BinaryReader reader)
	{
		var b1 = (uint)reader.ReadByte();
		var b2 = (uint)reader.ReadByte();
		var b3 = (uint)reader.ReadByte();
		var b4 = (uint)reader.ReadByte();
		return (b1 << 24) | (b2 << 16) | (b3 << 8) | b4;
	}

	static string ReadTag(BinaryReader reader)
	{
		var bytes = reader.ReadBytes(4);
		return bytes.Length == 4 ? Encoding.ASCII.GetString(bytes) : string.Empty;
	}

	static class FontConfigNative
	{
		[DllImport("libfontconfig.so.1", EntryPoint = "FcConfigGetCurrent")]
		static extern IntPtr FcConfigGetCurrent();

		[DllImport("libfontconfig.so.1", EntryPoint = "FcConfigAppFontAddFile", CharSet = CharSet.Ansi)]
		[return: MarshalAs(UnmanagedType.I1)]
		static extern bool FcConfigAppFontAddFile(IntPtr config, string fileName);

		[DllImport("libpangocairo-1.0.so.0", EntryPoint = "pango_cairo_font_map_get_default")]
		static extern IntPtr PangoCairoFontMapGetDefault();

		[DllImport("libpangoft2-1.0.so.0", EntryPoint = "pango_fc_font_map_config_changed")]
		static extern void PangoFcFontMapConfigChanged(IntPtr fontMap);

		public static bool TryAddAppFont(string fontFilePath)
		{
			var config = FcConfigGetCurrent();
			if (config == IntPtr.Zero)
				return false;

			return FcConfigAppFontAddFile(config, fontFilePath);
		}

		/// <summary>
		/// Notify Pango that fontconfig configuration has changed so it
		/// rescans and picks up newly registered app fonts.
		/// </summary>
		public static void NotifyFontMapChanged()
		{
			try
			{
				var fontMap = PangoCairoFontMapGetDefault();
				if (fontMap != IntPtr.Zero)
					PangoFcFontMapConfigChanged(fontMap);
			}
			catch
			{
				// Best-effort; ignore if Pango libs are unavailable
			}
		}
	}
}
