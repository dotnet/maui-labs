using System.Runtime.InteropServices;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[Collection("GTK runtime")]
public sealed class ResourceImageRuntimeTests
{
	[GtkRuntimeFact]
	public void ProcessedSvgImages_HaveDistinctExpectedPixels()
	{
		Gtk.Module.Initialize();
		Gtk.Functions.Init();

		AssertTexture("red.png", red: true);
		AssertTexture("blue.png", red: false);
	}

	static void AssertTexture(string name, bool red)
	{
		var path = Path.Combine(AppContext.BaseDirectory, name);
		Assert.True(File.Exists(path), $"Processed image was not copied to '{path}'.");

		using var texture = Gdk.Texture.NewFromFilename(path);
		Assert.Equal(16, texture.GetWidth());
		Assert.Equal(16, texture.GetHeight());

		var pixels = new byte[16 * 16 * 4];
		gdk_texture_download(texture.Handle.DangerousGetHandle(), pixels, 16 * 4);
		var offset = ((8 * 16) + 8) * 4;
		if (red)
		{
			Assert.True(pixels[offset + 2] > 240 && pixels[offset + 1] < 15 && pixels[offset] < 15,
				$"Expected a red center pixel, got {Convert.ToHexString(pixels.AsSpan(offset, 4))}.");
		}
		else
		{
			Assert.True(pixels[offset] > 240 && pixels[offset + 1] < 15 && pixels[offset + 2] < 15,
				$"Expected a blue center pixel, got {Convert.ToHexString(pixels.AsSpan(offset, 4))}.");
		}
	}

	[DllImport("libgtk-4.so.1")]
	static extern void gdk_texture_download(IntPtr texture, [Out] byte[] data, nuint stride);
}
