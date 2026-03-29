using System.Net.Http;
using Microsoft.Maui;
using Platform.Maui.Linux.Gtk4.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

internal static class GtkImageSourceLoader
{
	static readonly HttpClient HttpClient = new();

	[System.Runtime.InteropServices.DllImport("libcairo.so.2")]
	static extern int cairo_surface_write_to_png(nint surface,
		[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string filename);

	public static async Task<Gdk.Texture?> LoadTextureAsync(IImageSource? source, CancellationToken cancellationToken,
		IGtkFontManager? fontManager = null)
	{
		if (source == null)
			return null;

		return source switch
		{
			IFontImageSource fontSource => LoadFromFontImageSource(fontSource, fontManager),
			IFileImageSource fileSource => await LoadFromFileAsync(fileSource, cancellationToken),
			IUriImageSource uriSource => await LoadFromUriAsync(uriSource, cancellationToken),
			IStreamImageSource streamSource => await LoadFromStreamAsync(streamSource, cancellationToken),
			_ => null,
		};
	}

	static Gdk.Texture? LoadFromFontImageSource(IFontImageSource fontSource, IGtkFontManager? fontManager)
	{
		var glyph = fontSource.Glyph;
		if (string.IsNullOrEmpty(glyph))
			return null;

		var size = fontSource.Font.Size > 0 ? fontSource.Font.Size : 24;
		var pixelSize = (int)(size * 1.4);
		int surfaceSize = Math.Max(pixelSize + 4, 16);

		var surface = new Cairo.ImageSurface(Cairo.Format.Argb32, surfaceSize, surfaceSize);
		var cr = new Cairo.Context(surface);

		var color = fontSource.Color;
		if (color != null)
			Cairo.Internal.Context.SetSourceRgba(cr.Handle, color.Red, color.Green, color.Blue, color.Alpha);
		else
			Cairo.Internal.Context.SetSourceRgba(cr.Handle, 0, 0, 0, 1);

		var layout = PangoCairo.Functions.CreateLayout(cr);
		var fontDesc = Pango.FontDescription.New();

		// Resolve font family through the font manager so that registered
		// embedded fonts (e.g. FontAwesome) are found via fontconfig
		var fontFamily = fontSource.Font.Family;
		if (!string.IsNullOrEmpty(fontFamily) && fontManager != null)
		{
			var css = fontManager.BuildFontCss(fontSource.Font);
			var resolved = ExtractFontFamily(css);
			if (!string.IsNullOrEmpty(resolved))
				fontFamily = resolved;
		}
		if (!string.IsNullOrEmpty(fontFamily))
			fontDesc.SetFamily(fontFamily);

		fontDesc.SetAbsoluteSize(size * Pango.Constants.SCALE);
		layout.SetFontDescription(fontDesc);
		layout.SetText(glyph, -1);

		layout.GetPixelSize(out int textW, out int textH);
		double offsetX = (surfaceSize - textW) / 2.0;
		double offsetY = (surfaceSize - textH) / 2.0;
		Cairo.Internal.Context.MoveTo(cr.Handle, offsetX, offsetY);

		PangoCairo.Functions.ShowLayout(cr, layout);
		Cairo.Internal.Surface.Flush(surface.Handle);

		// Render to PNG and load as Gdk.Texture (MemoryTextureBuilder has
		// issues in GirCore 0.7.0, so we use cairo_surface_write_to_png)
		var tmpPath = Path.Combine(Path.GetTempPath(), $"maui_font_{Guid.NewGuid():N}.png");
		try
		{
			cairo_surface_write_to_png(surface.Handle.DangerousGetHandle(), tmpPath);
			cr.Dispose();
			surface.Dispose();
			return Gdk.Texture.NewFromFilename(tmpPath);
		}
		catch
		{
			return null;
		}
		finally
		{
			try { File.Delete(tmpPath); } catch { }
		}
	}

	static Task<Gdk.Texture?> LoadFromFileAsync(IFileImageSource fileSource, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (string.IsNullOrWhiteSpace(fileSource.File))
			return Task.FromResult<Gdk.Texture?>(null);

		var filePath = ResolveFilePath(fileSource.File);
		if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
			return Task.FromResult<Gdk.Texture?>(null);

		return Task.FromResult<Gdk.Texture?>(Gdk.Texture.NewFromFilename(filePath));
	}

	static async Task<Gdk.Texture?> LoadFromUriAsync(IUriImageSource uriSource, CancellationToken cancellationToken)
	{
		var uri = uriSource.Uri;
		if (uri == null)
			return null;

		if (uri.IsFile && !string.IsNullOrWhiteSpace(uri.LocalPath) && File.Exists(uri.LocalPath))
		{
			return Gdk.Texture.NewFromFilename(uri.LocalPath);
		}

		var bytes = await HttpClient.GetByteArrayAsync(uri, cancellationToken);
		if (bytes.Length == 0)
			return null;

		var glibBytes = GLib.Bytes.New(bytes.AsSpan());
		return Gdk.Texture.NewFromBytes(glibBytes);
	}

	static async Task<Gdk.Texture?> LoadFromStreamAsync(IStreamImageSource streamSource, CancellationToken cancellationToken)
	{
		await using var stream = await streamSource.GetStreamAsync(cancellationToken);
		if (stream == null)
			return null;

		await using var ms = new MemoryStream();
		await stream.CopyToAsync(ms, cancellationToken);
		var bytes = ms.ToArray();
		if (bytes.Length == 0)
			return null;

		var glibBytes = GLib.Bytes.New(bytes.AsSpan());
		return Gdk.Texture.NewFromBytes(glibBytes);
	}

	static string ResolveFilePath(string source)
	{
		if (Path.IsPathRooted(source))
			return source;

		var appBasePath = Path.Combine(AppContext.BaseDirectory, source);
		if (File.Exists(appBasePath))
			return appBasePath;

		var cwdPath = Path.GetFullPath(source);
		if (File.Exists(cwdPath))
			return cwdPath;

		return source;
	}

	// Extract font-family value from CSS like: font-family: "Font Awesome 6 Free Solid";
	static string? ExtractFontFamily(string css)
	{
		const string prefix = "font-family: ";
		var idx = css.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
		if (idx < 0) return null;
		idx += prefix.Length;

		// Strip quotes
		if (idx < css.Length && css[idx] == '"')
		{
			idx++;
			var end = css.IndexOf('"', idx);
			return end > idx ? css[idx..end] : null;
		}

		var semi = css.IndexOf(';', idx);
		return semi > idx ? css[idx..semi].Trim() : css[idx..].Trim();
	}
}
