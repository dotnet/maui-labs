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
public class ContentViewRootLayoutTests(ITestOutputHelper output)
{
	[GtkRuntimeFact]
	public void ScrollContent_UsesViewportConstraints_ThroughContentViewAndBorder()
	{
		Gtk.Module.Initialize();
		Gtk.Functions.Init();
		using var app = MauiApp.CreateBuilder().UseMauiAppLinuxGtk4<Application>().Build();
		var context = new GtkMauiContext(app.Services);
		foreach (bool layoutRoot in new[] { false, true })
			foreach (var orientation in new[] { ScrollOrientation.Vertical, ScrollOrientation.Horizontal, ScrollOrientation.Both, ScrollOrientation.Neither })
				CheckRoot(context, layoutRoot, orientation);
		foreach (bool layoutRoot in new[] { false, true })
			CheckMeasureInvalidation(context, layoutRoot);
	}

	void CheckMeasureInvalidation(GtkMauiContext context, bool layoutRoot)
	{
		var child = new BoxView { WidthRequest = 180, HeightRequest = 24 };
		var absolute = new AbsoluteLayout { WidthRequest = 120, HeightRequest = 40, Children = { child } };
		AbsoluteLayout.SetLayoutBounds(child, new Rect(0, 8, 180, 24));
		var probe = new DesiredSizeProbe { HeightRequest = 40, Content = new Label { Text = "Measured content" } };
		var stack = new VerticalStackLayout { Children = { absolute, probe } };
		View root = layoutRoot ? stack : new ContentView { Content = stack };
		var scroll = new ScrollView { Content = root };
		var native = (Gtk.Widget)scroll.ToPlatform(context);
		var nativeChild = (Gtk.Widget)child.Handler!.PlatformView!;
		var views = new View[] { scroll, root, stack, absolute, child, probe, (View)probe.Content }.Distinct().ToArray();
		var widgets = views.Select(view => (Gtk.Widget)view.Handler!.PlatformView!).ToArray();
		using var window = Gtk.Window.New();
		window.SetChild(native);
		window.SetDefaultSize(640, 480);
		window.Present();
		try
		{
			PumpUntil(() => child.Width == 180 && probe.Measures > 0 && nativeChild.GetAllocatedWidth() == 180);
			child.WidthRequest = 80;
			AbsoluteLayout.SetLayoutBounds(child, new Rect(0, 8, 80, 24));
			probe.ShowProblem = false;
			PumpUntil(() => child.Width == 80 && nativeChild.GetAllocatedWidth() == 80 &&
				probe.Width == 100 && ((IView)probe).DesiredSize.Width == 100);
			Assert.Equal(640, window.GetAllocatedWidth());
			Assert.Equal(480, window.GetAllocatedHeight());

			int frames = 0;
			var tick = native.AddTickCallback((sender, clock) => { frames++; return true; });
			try
			{
				PumpUntil(() => frames >= 3);
				var measured = probe.Measures;
				frames = 0;
				PumpUntil(() => frames >= 3);
				Assert.Equal(measured, probe.Measures);
			}
			finally
			{
				native.RemoveTickCallback(tick);
			}
			output.WriteLine($"root={root.GetType().Name}: fixed-size window remeasured child to {child.Width} and probe to {probe.Width}; idle frames did not remeasure.");
		}
		finally
		{
			window.SetChild(null);
			window.Destroy();
			foreach (var view in views)
				view.Handler?.DisconnectHandler();
			foreach (var widget in widgets)
				widget.Dispose();
		}
	}

	static void PumpUntil(Func<bool> condition)
	{
		var timer = Stopwatch.StartNew();
		while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(5))
		{
			g_main_context_iteration(IntPtr.Zero, false);
			Thread.Sleep(1);
		}
		Assert.True(condition(), "GTK layout did not converge before the deadline.");
	}

	sealed class DesiredSizeProbe : ContentView
	{
		bool _showProblem = true;
		public int Measures { get; private set; }
		public bool ShowProblem
		{
			set
			{
				_showProblem = value;
				WidthRequest = value ? -1 : 100;
				InvalidateMeasure();
			}
		}

		protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
		{
			Measures++;
			var measured = base.MeasureOverride(widthConstraint, heightConstraint);
			return _showProblem ? new Size(Math.Max(180, measured.Width), measured.Height) : measured;
		}
	}

	void CheckRoot(GtkMauiContext context, bool layoutRoot, ScrollOrientation orientation)
	{
		bool horizontal = orientation is ScrollOrientation.Horizontal or ScrollOrientation.Both;
		bool vertical = orientation is ScrollOrientation.Vertical or ScrollOrientation.Both;
		var child = new Label
		{
			Text = "Viewport reflow",
			WidthRequest = horizontal ? 1200 : -1,
			HeightRequest = vertical ? 900 : -1
		};
		var grid = new Grid { Children = { child } };
		var border = new Border { StrokeThickness = 0, Content = grid };
		var wrapper = new ContentView { Content = border };
		View root = layoutRoot ? new Grid { Children = { wrapper } } : wrapper;
		var scroll = new ScrollView { Content = root, Orientation = orientation };
		var native = (Gtk.ScrolledWindow)scroll.ToPlatform(context);
		var viewport = Assert.IsType<Gtk.Viewport>(native.GetFirstChild());
		var nativeChild = (Gtk.Widget)child.Handler!.PlatformView!;
		var views = new View[] { scroll, root, wrapper, border, grid, child }.Distinct().ToArray();
		var widgets = views.Select(view => (Gtk.Widget)view.Handler!.PlatformView!).ToArray();
		using var window = Gtk.Window.New();
		window.SetChild(native);
		window.SetDefaultSize(640, 480);
		window.Present();
		try
		{
			foreach (var (width, height) in new[] { (640, 480), (320, 240), (700, 520), (280, 220) })
			{
				window.SetDefaultSize(width, height);
				var timer = Stopwatch.StartNew();
				bool converged;
				do
				{
					g_main_context_iteration(IntPtr.Zero, false);
					var expectedWidth = horizontal ? Math.Max(1200, viewport.GetAllocatedWidth()) : viewport.GetAllocatedWidth();
					var expectedHeight = vertical ? Math.Max(900, viewport.GetAllocatedHeight()) : viewport.GetAllocatedHeight();
					converged = window.GetAllocatedWidth() == width && window.GetAllocatedHeight() == height &&
						viewport.GetAllocatedWidth() > 1 && viewport.GetAllocatedHeight() > 1 &&
						child.Width == expectedWidth && child.Height == expectedHeight &&
						nativeChild.GetAllocatedWidth() == expectedWidth && nativeChild.GetAllocatedHeight() == expectedHeight;
					if (!converged)
						Thread.Sleep(1);
				} while (!converged && timer.Elapsed < TimeSpan.FromSeconds(5));
				output.WriteLine($"root={root.GetType().Name}, {orientation}, window={width}x{height}, viewport={viewport.GetAllocatedWidth()}x{viewport.GetAllocatedHeight()}, child={child.Width}x{child.Height}, native={nativeChild.GetAllocatedWidth()}x{nativeChild.GetAllocatedHeight()}");
				Assert.True(converged, $"Root {root.GetType().Name} did not reflow {orientation} content after resize to {width}x{height}.");
			}
		}
		finally
		{
			var timer = Stopwatch.StartNew();
			while (g_main_context_pending(IntPtr.Zero) && timer.Elapsed < TimeSpan.FromSeconds(2))
				g_main_context_iteration(IntPtr.Zero, false);
			window.SetChild(null);
			window.Destroy();
			foreach (var view in views)
				view.Handler?.DisconnectHandler();
			foreach (var widget in widgets)
				widget.Dispose();
		}
	}

	[DllImport("libglib-2.0.so.0")]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool g_main_context_iteration(IntPtr context, [MarshalAs(UnmanagedType.Bool)] bool mayBlock);

	[DllImport("libglib-2.0.so.0")]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool g_main_context_pending(IntPtr context);
}
