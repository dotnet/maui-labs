using AppKit;
using System.Runtime.CompilerServices;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Handlers = Microsoft.Maui.Platforms.MacOS.Handlers;

namespace MacOS.RuntimeTests.Scenarios.ShellSections;

static class ShellSectionRegistration
{
	[ModuleInitializer]
	public static void Register() => ScenarioRegistry.Register(
		new("shell-sections", ExpectedCases: 1,
			CreateDelegate: context => new ShellSectionScenario().CreateDelegate(context),
			ExpectedAssertions: 59));
}

sealed class ShellSectionScenario : MauiRuntimeScenario
{
	RuntimeTestContext _context = null!;
	Shell TestShell { get; } = new() { FlyoutBehavior = FlyoutBehavior.Disabled };
	int CreatedPages { get; set; }

	public override async Task RunAsync(RuntimeTestContext context, Window window)
	{
		_context = context;
		var app = this;
		var shell = TestShell;
		var nativeWindow = (NSWindow)window.Handler!.PlatformView!;
		var root = nativeWindow.ContentView!;
		Require(app.CreatedPages == 1, "only initial page materialized");
		AssertDisplayed(shell, root, "One");
		var firstView = (NSView)shell.CurrentPage.Handler!.PlatformView!;
		Capture(root, "initial");
		var navigated = 0;
		shell.Navigated += (_, _) => navigated++;

		await shell.GoToAsync("//two");
		await FlushMainQueue();
		File.WriteAllText(context.EvidencePath("route-state.txt"),
			$"route={shell.CurrentState.Location}; page={shell.CurrentPage?.Title ?? "null"}; created={app.CreatedPages}; navigated={navigated}");
		Capture(root, "route-two");
		if (shell.CurrentPage == null && app.CreatedPages == 1 &&
			shell.CurrentState.Location.ToString().EndsWith("/two", StringComparison.Ordinal))
		{
			Require(Descendants(root).Contains(firstView), "baseline still displays previous native page");
			Require(navigated == 0, "baseline has not raised Navigated");
			context.BaselineFailure("shell-sections.lazy-route",
				"REGRESSION: route changed to two but lazy target was not created and previous page remains displayed");
			return;
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

		var dynamicCreated = 0;
		var dynamicSection = new Tab
		{
			Items =
			{
				new ShellContent { ContentTemplate = new DataTemplate(() =>
				{
					dynamicCreated++;
					return CreateVisiblePage("Dynamic one");
				}) },
				new ShellContent { ContentTemplate = new DataTemplate(() =>
				{
					dynamicCreated++;
					return CreateVisiblePage("Dynamic two");
				}) },
			},
		};
		item.Items.Add(dynamicSection);
		Require(dynamicCreated == 0, "inserting an inactive section keeps its templates lazy");
		item.CurrentItem = dynamicSection;
		await FlushMainQueue();
		AssertDisplayed(shell, root, "Dynamic one");
		dynamicSection.CurrentItem = dynamicSection.Items[1];
		await FlushMainQueue();
		Capture(root, "dynamic-content-two");
		AssertDisplayed(shell, root, "Dynamic two");
		Require(dynamicCreated == 2, "new section content selection creates the second page");
		item.Items.Remove(dynamicSection);
		await FlushMainQueue();
		AssertDisplayed(shell, root, "One");

		var other = new FlyoutItem
		{
			Items = { new ShellContent { Content = CreateVisiblePage("Other") } },
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
						new ShellContent { Content = CreateVisiblePage("Replacement") },
						new ShellContent
						{
							ContentTemplate = new DataTemplate(() =>
							{
								replacementCreated++;
								return CreateVisiblePage("Replacement target");
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
		context.Pass("section-selection-lifecycle");
	}

	static Task FlushMainQueue() => RuntimeTestContext.FlushMainQueueAsync();

	void AssertDisplayed(Shell shell, NSView root, string title)
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

	void Require(bool condition, string message) => _context.Assert(condition, message);

	void Capture(NSView view, string name) => _context.Capture(view, $"{name}.png");

	protected override void Prepare(RuntimeTestContext context)
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

	static ContentPage CreateVisiblePage(string title) => new()
	{
		Title = title,
		BackgroundColor = Colors.White,
		Content = new Label { Text = $"Page {title}", FontSize = 32, TextColor = Colors.Black },
	};

	public override Window CreateWindow(IActivationState? activationState) =>
		new(TestShell) { Width = 800, Height = 600 };
}
