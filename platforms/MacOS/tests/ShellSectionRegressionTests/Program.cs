using AppKit;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.MacOS.Essentials;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Tests;

static class Program
{
	static int _finished;
	internal static readonly string Output = Environment.GetEnvironmentVariable("SHELL_SECTION_RESULTS")
		?? Path.Combine(Environment.CurrentDirectory, "shell-section-results");

	static void Main(string[] args)
	{
		Directory.CreateDirectory(Output);
		using var watchdog = new System.Threading.Timer(_ =>
		{
			Finish(1, "failure.txt", "AppKit regression timed out.");
		}, null, TimeSpan.FromSeconds(90), Timeout.InfiniteTimeSpan);
		NSApplication.Init();
		NSApplication.SharedApplication.Delegate = new RegressionDelegate();
		NSApplication.Main(args);
	}

	internal static void Finish(int exitCode, string file, string message)
	{
		if (Interlocked.Exchange(ref _finished, 1) != 0)
			return;
		File.WriteAllText(Path.Combine(Output, file), message);
		Environment.Exit(exitCode);
	}
}

[Register("ShellSectionRegressionDelegate")]
sealed class RegressionDelegate : MacOSMauiApplication
{
	protected override MauiApp CreateMauiApp() => MauiApp.CreateBuilder()
		.UseMauiAppMacOS<RegressionApp>()
		.AddMacOSEssentials()
		.Build();

	protected override void OnStarted() =>
		NSApplication.SharedApplication.BeginInvokeOnMainThread(Run);

	async void Run()
	{
		try
		{
			var app = (RegressionApp)Application;
			var shell = app.TestShell;
			var window = (NSWindow)app.Windows[0].Handler!.PlatformView!;
			var root = window.ContentView!;
			Require(app.CreatedPages == 1, "only initial page materialized");
			AssertDisplayed(shell, root, "One");
			var firstView = (NSView)shell.CurrentPage.Handler!.PlatformView!;
			Capture(root, "initial");
			var navigated = 0;
			shell.Navigated += (_, _) => navigated++;

			await shell.GoToAsync("//two");
			await FlushMainQueue();
			File.WriteAllText(Path.Combine(Program.Output, "route-state.txt"),
				$"route={shell.CurrentState.Location}; page={shell.CurrentPage?.Title ?? "null"}; created={app.CreatedPages}; navigated={navigated}");
			Capture(root, "route-two");
			if (shell.CurrentPage == null && app.CreatedPages == 1 &&
				shell.CurrentState.Location.ToString().EndsWith("/two", StringComparison.Ordinal))
			{
				Require(Descendants(root).Contains(firstView), "baseline still displays previous native page");
				Require(navigated == 0, "baseline has not raised Navigated");
				Program.Finish(42, "failure.txt",
					"REGRESSION: route changed to two but lazy target was not created and previous page remains displayed");
			}
			AssertDisplayed(shell, root, "Two");
			Require(navigated > 0, "route navigation raises Navigated");
			Require(app.CreatedPages == 2, "target created exactly once");

			var item = shell.CurrentItem;
			var secondSection = item.CurrentItem;
			item.CurrentItem = item.Items[0];
			await FlushMainQueue();
			AssertDisplayed(shell, root, "One");
			item.CurrentItem = secondSection;
			await FlushMainQueue();
			AssertDisplayed(shell, root, "Two");
			Require(app.CreatedPages == 2, "return visits reuse cached pages");

			secondSection.CurrentItem = secondSection.Items[1];
			await FlushMainQueue();
			AssertDisplayed(shell, root, "Three");
			Require(app.CreatedPages == 3, "content selection creates only selected page");
			Capture(root, "content-three");

			secondSection.IsVisible = false;
			await FlushMainQueue();
			AssertDisplayed(shell, root, "One");
			secondSection.IsVisible = true;
			item.CurrentItem = secondSection;
			await FlushMainQueue();
			AssertDisplayed(shell, root, "Three");
			item.Items.Remove(secondSection);
			await FlushMainQueue();
			AssertDisplayed(shell, root, "One");
			item.Items.Add(secondSection);
			Require(app.CreatedPages == 3, "hide/remove active section reuses remaining page");

			var other = new FlyoutItem
			{
				Items = { new ShellContent { Content = RegressionApp.CreateVisiblePage("Other") } },
			};
			shell.Items.Add(other);
			shell.CurrentItem = other;
			await FlushMainQueue();
			AssertDisplayed(shell, root, "Other");
			item.CurrentItem = item.Items[0];
			secondSection.CurrentItem = secondSection.Items[0];
			await FlushMainQueue();
			AssertDisplayed(shell, root, "Other");
			shell.CurrentItem = item;
			await FlushMainQueue();
			AssertDisplayed(shell, root, "One");
			Require(app.CreatedPages == 3, "inactive selection does not materialize content");

			var handler = (Handlers.ShellHandler)shell.Handler!;
			var replacementCreated = 0;
			var replacement = new Shell
			{
				FlyoutBehavior = FlyoutBehavior.Disabled,
				Items =
				{
					new TabBar
					{
						Items =
						{
							new ShellContent { Content = RegressionApp.CreateVisiblePage("Replacement") },
							new ShellContent
							{
								ContentTemplate = new DataTemplate(() =>
								{
									replacementCreated++;
									return RegressionApp.CreateVisiblePage("Replacement target");
								}),
							},
						},
					},
				},
			};
			var replacementWindow = new Window(replacement);
			handler.SetVirtualView(replacement);
			item.CurrentItem = secondSection;
			shell.FlyoutIsPresented = true;
			await FlushMainQueue();
			AssertDisplayed(replacement, root, "Replacement");
			replacement.CurrentItem.CurrentItem = replacement.CurrentItem.Items[1];
			await FlushMainQueue();
			AssertDisplayed(replacement, root, "Replacement target");
			Require(replacementCreated == 1, "rebound handler observes replacement section");
			Capture(root, "replacement-target");

			var disconnectedCreated = 0;
			var pending = new ShellContent
			{
				ContentTemplate = new DataTemplate(() =>
				{
					disconnectedCreated++;
					return new ContentPage();
				}),
			};
			replacement.CurrentItem.CurrentItem.Items.Add(pending);
			replacement.CurrentItem.CurrentItem.CurrentItem = pending;
			((IElementHandler)handler).DisconnectHandler();
			shell.CurrentItem = other;
			await FlushMainQueue();
			Require(disconnectedCreated == 0, "queued selection after disconnect does not create page");
			Program.Finish(0, "passed.txt",
				$"PASS: real AppKit route/property/content navigation, native page hosting, lazy caching, inactive selections, disconnect. OS={Environment.OSVersion}");
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine(ex);
			Program.Finish(1, "failure.txt", ex.ToString());
		}
	}

	static async Task FlushMainQueue()
	{
		// Queue twice so callbacks posted by selection callbacks also run.
		for (var i = 0; i < 2; i++)
		{
			var completion = new TaskCompletionSource();
			NSApplication.SharedApplication.BeginInvokeOnMainThread(() => completion.SetResult());
			await completion.Task;
		}
	}

	static void AssertDisplayed(Shell shell, NSView root, string title)
	{
		root.LayoutSubtreeIfNeeded();
		Require(shell.CurrentPage?.Title == title, $"current page is {title}");
		Require(shell.CurrentPage?.Handler?.PlatformView is NSView page &&
			Descendants(root).Contains(page) && page.Bounds.Width > 0 && page.Bounds.Height > 0,
			$"{title} attached to rendered native tree");
		Require(shell.CurrentPage is ContentPage { Content: Label label } &&
			label.Handler?.PlatformView is NSTextField text && text.StringValue == $"Page {title}" &&
			text.Bounds.Width > 0 && text.Bounds.Height > 0 && !text.Hidden &&
			Descendants(root).Contains(text),
			$"{title} label rendered with expected native text");
	}

	static IEnumerable<NSView> Descendants(NSView view)
	{
		yield return view;
		foreach (var child in view.Subviews)
			foreach (var descendant in Descendants(child))
				yield return descendant;
	}

	static void Require(bool condition, string message)
	{
		if (!condition)
			throw new InvalidOperationException(message);
		File.AppendAllText(Path.Combine(Program.Output, "assertions.txt"), $"PASS: {message}\n");
	}

	static void Capture(NSView view, string name)
	{
		view.LayoutSubtreeIfNeeded();
		using var bitmap = new NSBitmapImageRep(IntPtr.Zero, (nint)view.Bounds.Width,
			(nint)view.Bounds.Height, 8, 4, true, false, NSColorSpace.DeviceRGB, 0, 0);
		bitmap.Size = view.Bounds.Size;
		view.CacheDisplay(view.Bounds, bitmap);
		using var png = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png)
			?? throw new InvalidOperationException("PNG encoding failed.");
		File.WriteAllBytes(Path.Combine(Program.Output, $"{name}.png"), png.ToArray());
	}
}

public sealed class RegressionApp : Application
{
	public Shell TestShell { get; } = new() { FlyoutBehavior = FlyoutBehavior.Disabled };
	public int CreatedPages { get; private set; }

	public RegressionApp()
	{
		TestShell.Items.Add(new TabBar
		{
			Items =
			{
				new ShellContent { Route = "one", ContentTemplate = new DataTemplate(() => CreatePage("One")) },
				new Tab
				{
					Route = "second",
					Items =
					{
						new ShellContent { Route = "two", ContentTemplate = new DataTemplate(() => CreatePage("Two")) },
						new ShellContent { Route = "three", ContentTemplate = new DataTemplate(() => CreatePage("Three")) },
					},
				},
			},
		});
	}

	Page CreatePage(string title)
	{
		CreatedPages++;
		return CreateVisiblePage(title);
	}

	internal static ContentPage CreateVisiblePage(string title) => new()
	{
		Title = title,
		BackgroundColor = Colors.White,
		Content = new Label { Text = $"Page {title}", FontSize = 32, TextColor = Colors.Black },
	};

	protected override Window CreateWindow(IActivationState? activationState) =>
		new(TestShell) { Width = 800, Height = 600 };
}
