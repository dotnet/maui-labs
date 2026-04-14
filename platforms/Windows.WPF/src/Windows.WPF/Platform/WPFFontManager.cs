using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	/// <summary>
	/// WPF implementation of IFontManager — resolves MAUI Font to WPF FontFamily + size + weight + style.
	/// </summary>
	public class WPFFontManager : IFontManager
	{
		readonly ConcurrentDictionary<string, FontFamily> _fontCache = new();
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
		{
			if (string.IsNullOrEmpty(font.Family))
				return DefaultFontFamily;

			return _fontCache.GetOrAdd(font.Family, family =>
			{
				var fontPath = _fontRegistrar.GetFont(family);
				if (!string.IsNullOrEmpty(fontPath))
				{
					try
					{
						if (File.Exists(fontPath))
						{
							var dir = Path.GetDirectoryName(fontPath)!;
							var fontName = Path.GetFileNameWithoutExtension(fontPath);
							return new FontFamily(new Uri(dir + "/"), $"./{Path.GetFileName(fontPath)}#{fontName}");
						}
					}
					catch (Exception ex)
					{
						_logger?.LogWarning(ex, "Failed to load embedded font: {FontPath}", fontPath);
					}
				}

				try
				{
					return new FontFamily(family);
				}
				catch
				{
					_logger?.LogWarning("Font family not found: {Family}", family);
					return DefaultFontFamily;
				}
			});
		}

		public double GetFontSize(Font font, double defaultFontSize = 0)
		{
			if (font.Size > 0)
				return font.Size;
			return defaultFontSize > 0 ? defaultFontSize : DefaultFontSize;
		}
	}

	/// <summary>
	/// WPF implementation of IFontRegistrar — manages embedded font registration.
	/// </summary>
	public class WPFFontRegistrar : IFontRegistrar
	{
		readonly ConcurrentDictionary<string, string> _fonts = new();
		readonly ConcurrentDictionary<string, string> _aliasFonts = new();
		string? _fontDir;

		string FontDir
		{
			get
			{
				if (_fontDir == null)
				{
					_fontDir = Path.Combine(Path.GetTempPath(), "MauiWPFFonts", AppDomain.CurrentDomain.FriendlyName);
					Directory.CreateDirectory(_fontDir);
				}
				return _fontDir;
			}
		}

		public string? GetFont(string font)
		{
			if (_aliasFonts.TryGetValue(font, out var aliasPath))
				return aliasPath;
			if (_fonts.TryGetValue(font, out var path))
				return path;
			return null;
		}

		public void Register(string filename, string? alias, System.Reflection.Assembly assembly)
		{
			if (!string.IsNullOrEmpty(alias))
				_aliasFonts[alias!] = filename;
			_fonts[filename] = filename;
		}

		public void Register(string filename, string? alias)
		{
			if (!string.IsNullOrEmpty(alias))
				_aliasFonts[alias!] = filename;
			_fonts[filename] = filename;
		}
	}

	/// <summary>
	/// WPF implementation of IEmbeddedFontLoader.
	/// </summary>
	public class WPFEmbeddedFontLoader : IEmbeddedFontLoader
	{
		readonly IFontRegistrar _registrar;

		public WPFEmbeddedFontLoader(IFontRegistrar registrar)
		{
			_registrar = registrar;
		}

		public string? LoadFont(EmbeddedFont font)
		{
			try
			{
				if (font.ResourceStream == null || string.IsNullOrEmpty(font.FontName))
					return null;

				var fontDir = Path.Combine(Path.GetTempPath(), "MauiWPFFonts", AppDomain.CurrentDomain.FriendlyName);
				Directory.CreateDirectory(fontDir);

				var targetPath = Path.Combine(fontDir, font.FontName);
				if (!File.Exists(targetPath))
				{
					using var fs = File.Create(targetPath);
					font.ResourceStream.CopyTo(fs);
				}

				return targetPath;
			}
			catch
			{
				return null;
			}
		}
	}
}
