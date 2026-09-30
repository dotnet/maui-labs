using System.Diagnostics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using Xunit.Abstractions;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[Collection("GTK runtime")]
public class WindowSizingTests(ITestOutputHelper output)
{
	[GtkRuntimeFact]
	public void StartupWindow_ResizesBelowOldFloor_AndReflowsContent()
	{
		var host = new SizingHost(output);
		host.Run([]);
		Assert.Null(host.Failure);
		Assert.True(host.Completed);
	}

	public sealed class SizingApp : Application
	{
		public Grid Layout { get; } = new();
		public Label Child { get; } = new() { Text = "Window sizing regression" };

		protected override Window CreateWindow(IActivationState? activationState)
		{
			Layout.Add(Child);
			return new Window(new ContentPage { Content = Layout });
		}
	}

	sealed class SizingHost(ITestOutputHelper output) : GtkMauiApplication
	{
		public Exception? Failure { get; private set; }
		public bool Completed { get; private set; }
		protected override bool CreateDesktopEntry => false;
		protected override string ApplicationId => "org.maui.tests.sizing";
		protected override MauiApp CreateMauiApp() =>
			MauiApp.CreateBuilder().UseMauiAppLinuxGtk4<SizingApp>().Build();

		protected override void OnStarted()
		{
			var app = (SizingApp)Application;
			var window = (Gtk.Window)app.Windows[0].Handler!.PlatformView!;
			var sizes = new[] { (1024, 768), (500, 700), (300, 400), (1100, 700), (300, 400) };
			var index = 0;
			var clock = Stopwatch.StartNew();
			GLib.Functions.TimeoutAdd(0, 50, () =>
			{
				try
				{
					var (width, height) = sizes[index];
					if (window.GetAllocatedWidth() != width || window.GetAllocatedHeight() != height)
					{
						if (clock.Elapsed < TimeSpan.FromSeconds(3))
							return true;
						throw new InvalidOperationException($"Requested {width}x{height}; native allocation {window.GetAllocatedWidth()}x{window.GetAllocatedHeight()}.");
					}
					output.WriteLine($"Requested {width}x{height}; native {window.GetAllocatedWidth()}x{window.GetAllocatedHeight()}; child {app.Child.Width}x{app.Child.Height}");
					Assert.Equal(width, app.Child.Width);
					Assert.Equal(height, app.Child.Height);
					index++;
					if (index < sizes.Length)
					{
						window.SetDefaultSize(sizes[index].Item1, sizes[index].Item2);
						clock.Restart();
						return true;
					}
					Completed = true;
				}
				catch (Exception ex)
				{
					Failure = ex;
				}
				window.Close();
				return false;
			});
		}
	}
}
