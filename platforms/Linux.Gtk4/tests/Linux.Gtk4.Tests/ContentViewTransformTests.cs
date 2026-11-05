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
public class ContentViewTransformTests(ITestOutputHelper output)
{
	[GtkRuntimeFact]
	public void Content_TransformsChangeNativeCoordinates_AndSurviveArrange()
	{
		Gtk.Module.Initialize();
		Gtk.Functions.Init();
		using var app = MauiApp.CreateBuilder().UseMauiAppLinuxGtk4<Application>().Build();
		var context = new GtkMauiContext(app.Services);
		CheckTransforms(new AbsoluteLayout(), context);
		CheckTransforms(new Microsoft.Maui.Controls.Shapes.Rectangle(), context);
		CheckTransforms(new ContentView { Content = new Microsoft.Maui.Controls.Shapes.Rectangle() }, context);
		CheckRootLayout(context);
		CheckExternalContentReplacement(context);
	}

	void CheckTransforms(View child, GtkMauiContext context)
	{
		output.WriteLine($"Child: {child.GetType().Name}");
		child.WidthRequest = 200;
		child.HeightRequest = 120;
		child.HorizontalOptions = LayoutOptions.Start;
		child.VerticalOptions = LayoutOptions.Start;
		var content = new ContentView { Content = child };
		var host = (Gtk.Widget)content.ToPlatform(context);
		var childWidget = (Gtk.Widget)child.Handler!.PlatformView!;
		using var window = Gtk.Window.New();
		window.SetChild(host);
		window.SetDefaultSize(400, 300);
		window.Present();

		try
		{
			Arrange();
			output.WriteLine($"Frame: {child.Frame}");
			Assert.Equal(200, child.Width);
			Assert.Equal(120, child.Height);
			AssertPoint(0, 0, 0, 0);
			child.TranslationX = 35;
			child.TranslationY = -12;
			Allocate();
			AssertPoint(0, 0, 35, -12);
			Arrange();
			AssertPoint(0, 0, 35, -12);

			child.AnchorX = 0;
			child.AnchorY = 0;
			child.Scale = 2;
			child.ScaleX = 1.5;
			child.ScaleY = 0.5;
			Allocate();
			AssertPoint(10, 20, 65, 8);
			child.Rotation = 90;
			Allocate();
			AssertPoint(10, 20, 15, 18);
			Arrange();
			AssertPoint(10, 20, 15, 18);

			child.Scale = child.ScaleX = child.ScaleY = 1;
			child.Rotation = child.TranslationX = child.TranslationY = 0;
			Arrange();
			AssertPoint(10, 20, 10, 20);

			content.Padding = new Thickness(7, 11, 0, 0);
			child.AnchorX = child.AnchorY = 0.5;
			child.Scale = 2;
			Arrange();
			AssertPoint(100, 60, 107, 71);
			AssertPoint(0, 0, -93, -49);

			content.Content = null;
			Assert.Null(childWidget.GetParent());
			child.TranslationX = 15;
			content.Content = child;
			Arrange();
			AssertPoint(100, 60, 122, 71);
			content.Content = null;
			Assert.Null(host.GetFirstChild());
		}
		finally
		{
			DrainPending();
			window.SetChild(null);
			window.Destroy();
			child.Handler?.DisconnectHandler();
			content.Handler?.DisconnectHandler();
			childWidget.Dispose();
			host.Dispose();
		}

		void Arrange()
		{
			((IView)content).Measure(400, 300);
			((IView)content).Arrange(new Rect(0, 0, 400, 300));
			Allocate();
		}

		void Allocate()
		{
			host.Allocate(400, 300, -1, null);
		}

		void AssertPoint(float x, float y, float expectedX, float expectedY)
		{
			var source = new NativePoint { X = x, Y = y };
			var widget = (Gtk.Widget)child.Handler!.PlatformView!;
			Assert.True(gtk_widget_compute_point(widget.Handle.DangerousGetHandle(),
				host.Handle.DangerousGetHandle(), ref source, out var actual));
			output.WriteLine($"({x},{y}) -> ({actual.X},{actual.Y}), expected ({expectedX},{expectedY})");
			Assert.Equal(expectedX, actual.X, 3);
			Assert.Equal(expectedY, actual.Y, 3);
		}
	}

	void CheckRootLayout(GtkMauiContext context)
	{
		var child = new AbsoluteLayout();
		var content = new ContentView { Content = child };
		var host = (Gtk.Widget)content.ToPlatform(context);
		var childWidget = (Gtk.Widget)child.Handler!.PlatformView!;
		using var window = Gtk.Window.New();
		window.SetChild(host);
		window.SetDefaultSize(400, 300);
		window.Present();
		try
		{
			WaitUntil(() => child.Width == 400 && child.Height == 300);
			window.SetDefaultSize(500, 350);
			WaitUntil(() => child.Width == 500 && child.Height == 350);
			window.SetDefaultSize(300, 200);
			WaitUntil(() => child.Width == 300 && child.Height == 200);
			output.WriteLine("Root ContentView: GTK frame-clock layout and grow/shrink verified.");
		}
		finally
		{
			DrainPending();
			window.SetChild(null);
			window.Destroy();
			child.Handler?.DisconnectHandler();
			content.Handler?.DisconnectHandler();
			host.Dispose();
			childWidget.Dispose();
		}
	}

	void CheckExternalContentReplacement(GtkMauiContext context)
	{
		var originalView = new AbsoluteLayout();
		var content = new ContentView { Content = originalView };
		var host = (GtkLayoutPanel)content.ToPlatform(context);
		var original = (Gtk.Widget)content.Content.Handler!.PlatformView!;
		host.IsExternallyManaged = true;
		content.Handler!.PlatformArrange(new Rect(0, 0, 200, 120));
		host.Allocate(200, 120, -1, null);
		var replacement = new ContentView { Content = new AbsoluteLayout() };
		try
		{
			content.Content = replacement;
			host.Allocate(200, 120, -1, null);
			Assert.Null(original.GetParent());
			Assert.Equal(200, replacement.Width);
			Assert.Equal(120, replacement.Height);
			var replacementPanel = Assert.IsType<GtkLayoutPanel>(replacement.Handler!.PlatformView);
			Assert.True(replacementPanel.IsExternallyManaged);
			Assert.True(Assert.IsType<GtkLayoutPanel>(replacement.Content.Handler!.PlatformView).IsExternallyManaged);
			Assert.Equal(200, replacementPanel.GetAllocatedWidth());
			Assert.Equal(120, replacementPanel.GetAllocatedHeight());
			output.WriteLine("Externally managed ContentView: replacement arranged without external re-layout.");
		}
		finally
		{
			DrainPending();
			replacement.Content.Handler?.DisconnectHandler();
			replacement.Handler?.DisconnectHandler();
			originalView.Handler?.DisconnectHandler();
			content.Handler?.DisconnectHandler();
			host.Dispose();
			original.Dispose();
		}
	}

	static void DrainPending()
	{
		var timeout = Stopwatch.StartNew();
		while (g_main_context_pending(IntPtr.Zero) && timeout.Elapsed < TimeSpan.FromSeconds(5))
			g_main_context_iteration(IntPtr.Zero, false);
	}

	static void WaitUntil(Func<bool> condition)
	{
		var timeout = Stopwatch.StartNew();
		while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(10))
		{
			g_main_context_iteration(IntPtr.Zero, false);
			Thread.Sleep(1);
		}
		Assert.True(condition(), "GTK did not complete the expected layout within 10 seconds.");
	}

	[StructLayout(LayoutKind.Sequential)]
	struct NativePoint
	{
		public float X;
		public float Y;
	}

	[DllImport("libgtk-4.so.1")]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool gtk_widget_compute_point(IntPtr widget, IntPtr target, ref NativePoint point, out NativePoint result);

	[DllImport("libglib-2.0.so.0")]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool g_main_context_iteration(IntPtr context, [MarshalAs(UnmanagedType.Bool)] bool mayBlock);

	[DllImport("libglib-2.0.so.0")]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool g_main_context_pending(IntPtr context);
}
