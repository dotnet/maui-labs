using System.Diagnostics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using Xunit.Abstractions;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[Collection("GTK runtime")]
public partial class WindowSizingTests(ITestOutputHelper output)
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
		public Grid Inner { get; private set; } = null!;
		public ContentView? Wrapper { get; private set; }
		public ScrollView TemplateScroll { get; private set; } = null!;

		protected override Window CreateWindow(IActivationState? activationState)
		{
			SetContent(0);
			return new Window(Page)
			{
				Width = 500, Height = 700,
				MinimumWidth = 300, MinimumHeight = 200
			};
		}

		public void SetContent(int kind)
		{
			Child = new Label { Text = "Window sizing regression" };
			Inner = new Grid();
			Inner.Add(Child);
			Wrapper = null;
			switch (kind)
			{
				case 1:
					Wrapper = new ContentView { Content = Inner };
					Page.Content = new Grid { Children = { Wrapper } };
					break;
				case 2:
					Child.HeightRequest = 40;
					Page.Content = new VerticalStackLayout { Children = { Child } };
					break;
				case 3:
				case 5:
					Child.HeightRequest = kind == 5 ? 1200 : 40;
					Page.Content = new ScrollView
					{
						Content = new VerticalStackLayout { Children = { Child } }
					};
					break;
				case 4:
					Child.WidthRequest = 1200;
					Child.HeightRequest = 40;
					Page.Content = new ScrollView
					{
						Orientation = ScrollOrientation.Horizontal,
						Content = new HorizontalStackLayout { Children = { Child } }
					};
					break;
				case 6:
					Page.Content = Child;
					break;
				default:
					Page.Content = Inner;
					break;
			}
		}

		public void ReplaceNestedChild()
		{
			Inner.Clear();
			Child = new Label { Text = "Changed without resizing" };
			Inner.Add(Child);
		}

		public Shell CreateTemplateShell()
		{
			Child = new Label { Text = "Hello, World!", HeightRequest = 40 };
			TemplateScroll = new ScrollView
			{
				Content = new VerticalStackLayout { Children = { Child } }
			};
			var page = new ContentPage
			{
				Title = "Home",
				Content = TemplateScroll
			};
			return new Shell
			{
				FlyoutBehavior = FlyoutBehavior.Disabled,
				Items = { new ShellContent { Title = "Home", ContentTemplate = new DataTemplate(() => page) } }
			};
		}
	}

	record Step(string Name, Action Apply, int Width, int Height, Func<(int Width, int Height)> ChildSize);

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
			var mauiWindow = app.Windows[0];
			var window = (Gtk.Window)mauiWindow.Handler!.PlatformView!;
			var container = (WindowRootViewContainer)window.GetChild()!;
			var steps = new Queue<Step>();

			void Add(string name, Action apply, int width, int height, int? childHeight = null, int? childWidth = null) =>
				steps.Enqueue(new(name, apply, width, height, () => (childWidth ?? width, childHeight ?? height)));

			Add("explicit startup dimensions and minima", () => { }, 500, 700);
			Add("startup minima clamp native shrink", () => window.SetDefaultSize(100, 100), 300, 200);
			Add("MAUI width and height mappers", () =>
			{
				mauiWindow.Width = 550;
				mauiWindow.Height = 720;
			}, 550, 720);

			foreach (var kind in new[] { 0, 1, 2, 3, 4, 5, 6 })
			{
				var contentKind = kind;
				int? childHeight = kind == 5 ? 1200 : kind is >= 2 and <= 4 ? 40 : null;
				int? childWidth = kind == 4 ? 1200 : null;
				Add($"content {kind} initial allocation", () =>
				{
					app.SetContent(contentKind);
					window.SetDefaultSize(1024, 768);
				}, 1024, 768, childHeight, childWidth);
				foreach (var (width, height) in new[] { (500, 700), (300, 400), (1100, 700), (300, 400) })
					Add($"content {kind} resize {width}x{height}", () => window.SetDefaultSize(width, height),
						width, height, childHeight, childWidth);
			}

			Add("nested mutation setup", () =>
			{
				app.SetContent(1);
				window.SetDefaultSize(500, 700);
			}, 500, 700);
			Add("nested child replacement without resize", app.ReplaceNestedChild, 500, 700);
			Add("nested panel becomes root without resize", () =>
			{
				app.Wrapper!.Content = null;
				app.Page.Content = app.Inner;
			}, 500, 700);
			Add("reparented root shrinks", () => window.SetDefaultSize(300, 400), 300, 400);

			Add("native chrome reserves content space", () =>
			{
				var chrome = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
				chrome.SetSizeRequest(-1, 50);
				container.SetMenuBar(chrome);
				window.SetDefaultSize(500, 700);
			}, 500, 700, 650);
			Add("native chrome shrink", () => window.SetDefaultSize(300, 400), 300, 400, 350);
			Add("native chrome removal without resize", container.ClearMenuBar, 300, 400);

			Add("runtime window minima", () =>
			{
				mauiWindow.MinimumWidth = 430;
				mauiWindow.MinimumHeight = 350;
				window.SetDefaultSize(300, 200);
			}, 430, 350);
			Add("cleared window minima", () =>
			{
				mauiWindow.MinimumWidth = 0;
				mauiWindow.MinimumHeight = 0;
				window.SetDefaultSize(300, 200);
			}, 300, 200);
			Add("native content minimum is preserved", () =>
			{
				((Gtk.Widget)app.Page.Handler!.PlatformView!).SetSizeRequest(360, 260);
				window.SetDefaultSize(100, 100);
			}, 360, 260);
			Add("native content minimum removal", () =>
			{
				((Gtk.Widget)app.Page.Handler!.PlatformView!).SetSizeRequest(-1, -1);
				window.SetDefaultSize(300, 200);
			}, 300, 200);

			int TemplateViewportWidth() =>
				((Gtk.ScrolledWindow)app.TemplateScroll.Handler!.PlatformView!).GetFirstChild()!.GetAllocatedWidth();
			int? shellChromeWidth = null;
			steps.Enqueue(new("template Shell/ScrollView startup", () =>
			{
				mauiWindow.Page = app.CreateTemplateShell();
				window.SetDefaultSize(1024, 768);
			}, 1024, 768, () => (TemplateViewportWidth(), 40)));
			foreach (var (width, height) in new[] { (500, 700), (300, 400), (1100, 700), (300, 400) })
				steps.Enqueue(new($"template Shell resize {width}x{height}", () =>
				{
					// Account for the native notebook border without fixing a GTK theme's thickness.
					shellChromeWidth ??= 1024 - TemplateViewportWidth();
					window.SetDefaultSize(width, height);
				}, width, height, () => (width - shellChromeWidth!.Value, 40)));

			var step = steps.Dequeue();
			step.Apply();
			var clock = Stopwatch.StartNew();
			GLib.Functions.TimeoutAdd(0, 50, () =>
			{
				try
				{
					var (childWidth, childHeight) = step.ChildSize();
					var nativeChild = app.Child.Handler?.PlatformView as Gtk.Widget;
					var converged = window.GetAllocatedWidth() == step.Width &&
						window.GetAllocatedHeight() == step.Height &&
						app.Child.Width == childWidth && app.Child.Height == childHeight &&
						nativeChild?.GetAllocatedWidth() == childWidth &&
						nativeChild?.GetAllocatedHeight() == childHeight;
					if (!converged && clock.Elapsed < TimeSpan.FromSeconds(5))
						return true;

					output.WriteLine($"{step.Name}: expected {step.Width}x{step.Height}; native {window.GetAllocatedWidth()}x{window.GetAllocatedHeight()}; child {app.Child.Width}x{app.Child.Height}; native child {nativeChild?.GetAllocatedWidth()}x{nativeChild?.GetAllocatedHeight()}");
					Assert.True(converged, $"Native window/content did not converge for '{step.Name}'.");
					if (steps.TryDequeue(out var next))
					{
						step = next;
						step.Apply();
						clock.Restart();
						return true;
					}
					new FrameChecks(app, output, error =>
					{
						Failure = error;
						Completed = error == null;
						window.Close();
					}).Start();
					return false;
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
