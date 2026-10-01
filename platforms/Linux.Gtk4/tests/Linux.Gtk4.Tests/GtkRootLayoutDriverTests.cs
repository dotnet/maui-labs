using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[Collection("GTK runtime")]
public class GtkRootLayoutDriverTests
{
	[GtkRuntimeFact]
	public void Driver_ReleasesNestedOwnership_RearmsOnMap_AndBoundsFailureRetries()
	{
		Gtk.Module.Initialize();
		Gtk.Functions.Init();
		using var parent = new GtkLayoutPanel();
		using var child = new GtkLayoutPanel();
		var parentView = new RecordingView();
		var childView = new RecordingView();
		var logger = new RecordingLogger();
		using var parentDriver = new GtkRootLayoutDriver(parent, () => parentView, () => logger);
		using var childDriver = new GtkRootLayoutDriver(child, () => childView, () => logger);
		parent.AddChild(child);
		parent.SetChildBounds(child, 0, 0, 200, 100);
		using var window = Gtk.Window.New();
		window.SetChild(parent);
		window.SetDefaultSize(300, 200);
		window.Present();
		try
		{
			Frames(parent);
			Assert.True(parentView.Measures > 0);
			Assert.Equal(0, childView.Measures);

			parent.RemoveChild(child);
			window.SetChild(child);
			Frames(child);
			Assert.True(childView.Measures > 0);

			child.IsExternallyManaged = true;
			var previous = childView.Measures;
			Frames(child);
			Assert.Equal(previous, childView.Measures);

			child.IsExternallyManaged = false;
			child.SetVisible(false);
			child.SetVisible(true);
			Frames(child);
			Assert.True(childView.Measures > previous);

			childView.ThrowOnMeasure = true;
			child.LayoutDirty = true;
			Frames(child);
			Assert.Equal(1, logger.Errors);
			previous = childView.Measures;
			Frames(child);
			Assert.Equal(previous, childView.Measures);
			Assert.Equal(1, logger.Errors);

			child.LayoutDirty = true;
			Frames(child);
			Assert.Equal(2, logger.Errors);
			childView.ThrowOnMeasure = false;
			child.LayoutDirty = true;
			previous = childView.Arranges;
			Frames(child);
			Assert.True(childView.Arranges > previous);

			childDriver.Dispose();
			previous = childView.Measures;
			child.LayoutDirty = true;
			child.SetVisible(false);
			child.SetVisible(true);
			Frames(child);
			Assert.Equal(previous, childView.Measures);
		}
		finally
		{
			window.SetChild(null);
			window.Destroy();
		}
	}

	static void Frames(Gtk.Widget widget)
	{
		int frames = 0;
		var id = widget.AddTickCallback((sender, clock) => { frames++; return true; });
		var timer = Stopwatch.StartNew();
		try
		{
			while (frames < 3 && timer.Elapsed < TimeSpan.FromSeconds(5))
			{
				g_main_context_iteration(IntPtr.Zero, false);
				Thread.Sleep(1);
			}
			Assert.True(frames >= 3, "GTK frame clock did not advance.");
		}
		finally
		{
			widget.RemoveTickCallback(id);
		}
	}

	sealed class RecordingView : ContentView, ICrossPlatformLayout
	{
		public int Measures { get; private set; }
		public int Arranges { get; private set; }
		public bool ThrowOnMeasure { get; set; }

		Size ICrossPlatformLayout.CrossPlatformMeasure(double width, double height)
		{
			Measures++;
			if (ThrowOnMeasure)
				throw new InvalidOperationException("Expected test layout exception");
			return new Size(width, height);
		}

		Size ICrossPlatformLayout.CrossPlatformArrange(Rect bounds)
		{
			Arranges++;
			return bounds.Size;
		}
	}

	sealed class RecordingLogger : ILogger
	{
		public int Errors { get; private set; }
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => true;
		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			Assert.NotNull(exception);
			Assert.Equal(LogLevel.Error, logLevel);
			Errors++;
		}
	}

	[DllImport("libglib-2.0.so.0")]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool g_main_context_iteration(IntPtr context, [MarshalAs(UnmanagedType.Bool)] bool mayBlock);
}
