using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using Xunit.Abstractions;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[Collection("GTK runtime")]
public class ContentViewClippingTests(ITestOutputHelper output)
{
	[GtkRuntimeFact]
	public void TranslatedContent_ClipsPaintAtBounds_AndRespondsToChanges()
	{
		Gtk.Module.Initialize();
		Gtk.Functions.Init();
		using var app = MauiApp.CreateBuilder().UseMauiAppLinuxGtk4<Application>().Build();
		var context = new GtkMauiContext(app.Services);
		var child = new Microsoft.Maui.Controls.Shapes.Rectangle
		{
			Fill = new SolidColorBrush(Colors.Red),
			TranslationX = -30,
			TranslationY = -20
		};
		var content = new ContentView
		{
			Content = child,
			IsClippedToBounds = true,
			WidthRequest = 100,
			HeightRequest = 80
		};
		var host = (Gtk.Widget)content.ToPlatform(context);
		var childWidget = (Gtk.Widget)child.Handler!.PlatformView!;
		using var root = Gtk.Fixed.New();
		using var background = Gtk.DrawingArea.New();
		background.SetContentWidth(300);
		background.SetContentHeight(200);
		background.SetDrawFunc((area, cr, width, height) =>
		{
			cr.SetSourceRgba(1, 1, 1, 1);
			cr.Rectangle(0, 0, width, height);
			cr.Fill();
		});
		root.Put(background, 0, 0);
		root.Put(host, 80, 60);
		using var window = Gtk.Window.New();
		window.SetChild(root);
		window.SetDefaultSize(300, 200);
		((IView)content).Measure(100, 80);
		content.Handler!.PlatformArrange(new Rect(0, 0, 100, 80));
		window.Present();

		try
		{
			AssertPaint("initial-clipped", clipped: true);
			content.IsClippedToBounds = false;
			AssertPaint("unclipped", clipped: false);
			content.IsClippedToBounds = true;
			AssertPaint("clipped-again", clipped: true);

			content.Clip = new Microsoft.Maui.Controls.Shapes.RectangleGeometry(new Rect(0, 0, 100, 80));
			content.IsClippedToBounds = false;
			AssertPaint("explicit-clip", clipped: true);
			content.IsClippedToBounds = true;
			content.Clip = null;
			AssertPaint("bounds-clip-after-clearing-geometry", clipped: true);
			content.IsClippedToBounds = false;
			AssertPaint("all-clipping-cleared", clipped: false);
		}
		finally
		{
			window.SetChild(null);
			window.Destroy();
			content.Handler?.DisconnectHandler();
			child.Handler?.DisconnectHandler();
			root.Remove(host);
			host.Dispose();
			childWidget.Dispose();
		}

		void AssertPaint(string phase, bool clipped)
		{
			var timer = Stopwatch.StartNew();
			using var paintable = Gtk.WidgetPaintable.New(root);
			var bounds = Graphene.Rect.Alloc();
			bounds.Init(0, 0, 300, 200);
			byte[]? lastPixels = null;
			int width = 0;
			bool matches = false;
			string state = "waiting for GTK snapshot";
			while (timer.Elapsed < TimeSpan.FromSeconds(5))
			{
				g_main_context_iteration(IntPtr.Zero, false);
				using var snapshot = Gtk.Snapshot.New();
				paintable.Snapshot(snapshot, 300, 200);
				var node = snapshot.ToNode();
				var renderer = root.GetNative()?.GetRenderer();
				state = $"root={root.GetAllocatedWidth()}x{root.GetAllocatedHeight()}, mapped={root.GetMapped()}, node={node != null}, renderer={renderer != null}";
				if (node == null || renderer == null)
					continue;

				using var texture = renderer.RenderTexture(node, bounds);
				width = texture.GetWidth();
				var height = texture.GetHeight();
				if (width != 300 || height != 200)
					continue;

				lastPixels = new byte[width * height * 4];
				gdk_texture_download(texture.Handle.DangerousGetHandle(), lastPixels, (nuint)(width * 4));
				matches = IsWhite(lastPixels, width, 20, 20) && IsRed(lastPixels, width, 100, 90) &&
					(clipped ? IsWhite(lastPixels, width, 60, 45) : IsRed(lastPixels, width, 60, 45));
				if (matches || timer.Elapsed > TimeSpan.FromSeconds(4.5))
				{
					var artifacts = Environment.GetEnvironmentVariable("GTK_TEST_ARTIFACTS");
					if (!string.IsNullOrEmpty(artifacts))
					{
						Directory.CreateDirectory(artifacts);
						Assert.True(texture.SaveToPng(Path.Combine(artifacts, $"{phase}.png")));
					}
				}
				if (matches)
					break;
				Thread.Sleep(1);
			}

			output.WriteLine($"{phase}: background={Pixel(lastPixels, width, 20, 20)}, inside={Pixel(lastPixels, width, 100, 90)}, header={Pixel(lastPixels, width, 60, 45)}, clipped={clipped}; {state}");
			Assert.True(lastPixels != null && IsWhite(lastPixels, width, 20, 20), "Native test background did not paint white.");
			Assert.True(matches, $"Native paint did not respect clipping during {phase}; header={Pixel(lastPixels, width, 60, 45)}.");
		}
	}

	static bool IsRed(byte[] pixels, int width, int x, int y)
	{
		int offset = (y * width + x) * 4;
		return pixels[offset + 2] > 240 && pixels[offset + 1] < 15 && pixels[offset] < 15 && pixels[offset + 3] > 240;
	}

	static bool IsWhite(byte[] pixels, int width, int x, int y)
	{
		int offset = (y * width + x) * 4;
		return pixels[offset] > 240 && pixels[offset + 1] > 240 && pixels[offset + 2] > 240 && pixels[offset + 3] > 240;
	}

	static string Pixel(byte[]? pixels, int width, int x, int y) =>
		pixels == null ? "no snapshot" : Convert.ToHexString(pixels.AsSpan((y * width + x) * 4, 4));

	[DllImport("libgtk-4.so.1")]
	static extern void gdk_texture_download(IntPtr texture, [Out] byte[] data, nuint stride);

	[DllImport("libglib-2.0.so.0")]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool g_main_context_iteration(IntPtr context, [MarshalAs(UnmanagedType.Bool)] bool mayBlock);
}
