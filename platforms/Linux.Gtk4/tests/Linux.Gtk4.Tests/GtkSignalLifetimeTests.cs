using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using static Microsoft.Maui.Platforms.Linux.Gtk4.Tests.GtkSignalAssertions;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[Collection("GTK runtime")]
public class GtkSignalLifetimeTests
{
	[GtkRuntimeFact]
	public void Signals_SurviveCollection_AndDisconnectBeforeNativeTeardown()
	{
		Gtk.Module.Initialize();
		Gtk.Functions.Init();
		using var app = MauiApp.CreateBuilder().UseMauiAppLinuxGtk4<TestApplication>().Build();
		var context = new GtkMauiContext(app.Services);
		using var window = Gtk.Window.New();
		using var box = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
		window.SetChild(box);
		window.Present();
		try
		{
			for (var batch = 0; batch < 20; batch++)
			{
				ConnectAndRetireViews(context, box);
				var collect = Task.Run(() =>
				{
					GC.Collect();
					GC.WaitForPendingFinalizers();
				});
				var timeout = Stopwatch.StartNew();
				while (!collect.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(10))
					g_main_context_iteration(IntPtr.Zero, false);
				Assert.True(collect.IsCompleted, "Signal-owner finalizers did not complete.");
				collect.GetAwaiter().GetResult();
				while (g_main_context_iteration(IntPtr.Zero, false) && timeout.Elapsed < TimeSpan.FromSeconds(10)) { }
				Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(10), "GTK teardown did not drain.");
			}
		}
		finally
		{
			window.SetChild(null);
			window.Destroy();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	static void ConnectAndRetireViews(GtkMauiContext context, Gtk.Box box)
	{
		for (var i = 0; i < 20; i++)
		{
			var button = new Button { Text = "Signal lifetime" };
			var clicked = 0;
			button.Clicked += (_, _) => clicked++;
			using var native = (Gtk.Button)button.ToPlatform(context);
			var handler = button.Handler!;
			box.Append(native);
			try
			{
				GC.Collect();
				g_signal_emit_by_name(native.Handle.DangerousGetHandle(), "clicked");
				Assert.Equal(1, clicked);
				var signal = GObject.Functions.SignalLookup("clicked", Gtk.Button.GetGType());
				Assert.True(GObject.Functions.SignalHasHandlerPending(native, signal, 0, true));

				handler.DisconnectHandler();
				Assert.False(GObject.Functions.SignalHasHandlerPending(native, signal, 0, true));
				g_signal_emit_by_name(native.Handle.DangerousGetHandle(), "clicked");
				Assert.Equal(1, clicked);
			}
			finally
			{
				box.Remove(native);
				handler.DisconnectHandler();
			}

			var layout = new VerticalStackLayout();
			using var panel = (GtkLayoutPanel)layout.ToPlatform(context);
			using var controllers = panel.ObserveControllers();
			var ownedControllers = Enumerable.Range(0, (int)controllers.GetNItems())
				.Select(index => controllers.GetObject((uint)index))
				.OfType<Gtk.EventController>()
				.Where(HasVisualStateSignals)
				.ToArray();
			Assert.Equal(3, ownedControllers.Length);
			box.Append(panel);
			var layoutHandler = layout.Handler!;
			try
			{
				GC.Collect();
				layoutHandler.DisconnectHandler();
				Assert.All(ownedControllers, controller =>
				{
					Assert.Null(controller.GetWidget());
					Assert.False(HasVisualStateSignals(controller));
				});
			}
			finally
			{
				box.Remove(panel);
				layoutHandler.DisconnectHandler();
				GC.KeepAlive(ownedControllers);
			}
		}
	}

	public sealed class TestApplication : Application;

	[DllImport("libglib-2.0.so.0")]
	static extern bool g_main_context_iteration(IntPtr context, bool mayBlock);

	[DllImport("libgobject-2.0.so.0", CharSet = CharSet.Ansi)]
	static extern void g_signal_emit_by_name(IntPtr instance, string detailedSignal);
}
