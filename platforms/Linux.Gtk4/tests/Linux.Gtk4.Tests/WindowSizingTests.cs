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
		public Label Child { get; private set; } = null!;
		public ContentPage Page { get; } = new();
		public bool IsStack { get; private set; }

		protected override Window CreateWindow(IActivationState? activationState)
		{
			SetContent(0);
			return new Window(Page);
		}

		public void SetContent(int kind)
		{
			IsStack = kind == 2;
			Child = new Label { Text = "Window sizing regression" };
			var grid = new Grid();
			grid.Add(Child);
			if (kind == 1)
			{
				var outer = new Grid();
				outer.Add(new ContentView { Content = grid });
				Page.Content = outer;
			}
			else if (IsStack)
			{
				Child.HeightRequest = 40;
				Page.Content = new VerticalStackLayout { Children = { Child } };
			}
			else
			{
				Page.Content = grid;
			}
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
			var contentKind = 0;
			var minimumPhase = 0;
			var clock = Stopwatch.StartNew();
			GLib.Functions.TimeoutAdd(0, 50, () =>
			{
				try
				{
					var (width, height) = minimumPhase switch
					{
						1 => (430, 350),
						2 => (300, 200),
						_ => sizes[index]
					};
					if (window.GetAllocatedWidth() != width || window.GetAllocatedHeight() != height)
					{
						if (clock.Elapsed < TimeSpan.FromSeconds(3))
							return true;
						throw new InvalidOperationException($"Requested {width}x{height}; native allocation {window.GetAllocatedWidth()}x{window.GetAllocatedHeight()}.");
					}
					var nativeChild = (Gtk.Widget)app.Child.Handler!.PlatformView!;
					var childHeight = app.IsStack ? 40 : height;
					if ((app.Child.Width != width || app.Child.Height != childHeight ||
						nativeChild.GetAllocatedWidth() != width || nativeChild.GetAllocatedHeight() != childHeight) &&
						clock.Elapsed < TimeSpan.FromSeconds(3))
						return true;
					output.WriteLine($"Content {contentKind}, minimum phase {minimumPhase}: expected {width}x{height}; native {window.GetAllocatedWidth()}x{window.GetAllocatedHeight()}; child {app.Child.Width}x{app.Child.Height}; native child {nativeChild.GetAllocatedWidth()}x{nativeChild.GetAllocatedHeight()}");
					Assert.Equal(width, app.Child.Width);
					Assert.Equal(childHeight, app.Child.Height);
					Assert.Equal(width, nativeChild.GetAllocatedWidth());
					Assert.Equal(childHeight, nativeChild.GetAllocatedHeight());
					if (minimumPhase == 1)
					{
						minimumPhase = 2;
						app.Windows[0].MinimumWidth = 0;
						app.Windows[0].MinimumHeight = 0;
						window.SetDefaultSize(300, 200);
						clock.Restart();
						return true;
					}
					if (minimumPhase == 2)
					{
						Completed = true;
						window.Close();
						return false;
					}
					index++;
					if (index == sizes.Length && contentKind < 2)
					{
						app.SetContent(++contentKind);
						index = 0;
					}
					if (index < sizes.Length)
					{
						window.SetDefaultSize(sizes[index].Item1, sizes[index].Item2);
						clock.Restart();
						return true;
					}
					minimumPhase = 1;
					app.Windows[0].MinimumWidth = 430;
					app.Windows[0].MinimumHeight = 350;
					window.SetDefaultSize(300, 200);
					clock.Restart();
					return true;
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
