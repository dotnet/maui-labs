using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[CollectionDefinition("GTK runtime", DisableParallelization = true)]
public sealed class GtkRuntimeCollection;

[Collection("GTK runtime")]
public class GtkTransformTests
{
	[GtkRuntimeFact]
	public void PlatformArrange_OffOriginTransforms_SurviveFinalizationAndPreserveCoordinates()
	{
		Gtk.Module.Initialize();

		using var services = new ServiceCollection().BuildServiceProvider();
		using var panel = new GtkLayoutPanel();
		using var window = Gtk.Window.New();
		var view = new Button();
		var handler = new TransformHandler();
		handler.SetMauiContext(new GtkMauiContext(services));
		handler.SetVirtualView(view);
		var widget = handler.PlatformView;
		panel.AddChild(widget);
		window.SetDefaultSize(640, 480);
		window.SetChild(panel);
		window.Present();

		try
		{
			// Cover origin, off-origin layout, translation from origin, and translation
			// cancelling the layout position. Each includes identity and translation-only.
			foreach (var (x, y, tx, ty) in new[]
			{
				(0, 0, 0, 0), (80, 60, 0, 0), (0, 0, 45, 30), (80, 60, -80, -60)
			})
			{
				var frame = new Rect(x, y, 120, 80);
				for (var iteration = 0; iteration < 32; iteration++)
				{
					foreach (var (scale, rotation) in new[]
					{
						(1.0, 0.0), (1.5, 0.0), (1.0, 15.0), (1.5, -30.0)
					})
					{
						view.TranslationX = tx;
						view.TranslationY = ty;
						view.Scale = scale;
						view.Rotation = rotation;
						handler.PlatformArrange(frame);
						panel.Allocate(640, 480, -1, null);

						// Point.Free() releases native memory without invalidating the
						// owning SafeHandle, which frees it again during collection.
						GC.Collect();
						GC.WaitForPendingFinalizers();
						GC.Collect();

						AssertPosition(widget, panel, frame, tx, ty, scale, rotation, 0, 0);
						AssertPosition(widget, panel, frame, tx, ty, scale, rotation, 60, 40);
						AssertPosition(widget, panel, frame, tx, ty, scale, rotation, 120, 80);

						for (var dispatch = 0; dispatch < 20 && GLib.MainContext.Default().Pending(); dispatch++)
							GLib.MainContext.Default().Iteration(false);
					}
				}
			}
		}
		finally
		{
			panel.RemoveChild(widget);
			((IElementHandler)handler).DisconnectHandler();
			window.Destroy();
		}
	}

	static void AssertPosition(Gtk.Widget widget, Gtk.Widget parent, Rect frame,
		double tx, double ty, double scale, double rotation, float x, float y)
	{
		var point = Graphene.Point.Alloc();
		point.Init(x, y);
		Assert.True(widget.ComputePoint(parent, point, out var actual));
		var radians = rotation * Math.PI / 180;
		var dx = (x - frame.Width / 2) * scale;
		var dy = (y - frame.Height / 2) * scale;
		var expectedX = frame.X + tx + frame.Width / 2 + dx * Math.Cos(radians) - dy * Math.Sin(radians);
		var expectedY = frame.Y + ty + frame.Height / 2 + dx * Math.Sin(radians) + dy * Math.Cos(radians);
		Assert.InRange(actual.X, expectedX - 0.01, expectedX + 0.01);
		Assert.InRange(actual.Y, expectedY - 0.01, expectedY + 0.01);
	}

	sealed class TransformHandler() : GtkViewHandler<IView, Gtk.Button>(ViewMapper)
	{
		protected override Gtk.Button CreatePlatformView() => Gtk.Button.New();
	}
}

internal sealed class GtkRuntimeFactAttribute : FactAttribute
{
	public GtkRuntimeFactAttribute()
	{
		if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("RUN_GTK_RUNTIME_TESTS") != "1")
			Skip = "Set RUN_GTK_RUNTIME_TESTS=1 and run under a Linux GTK4 display (for example xvfb-run).";
	}
}
