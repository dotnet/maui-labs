using System.Diagnostics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using Xunit.Abstractions;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[Collection("GTK runtime")]
public class CollectionViewHandlerTests(ITestOutputHelper output)
{
	[GtkRuntimeFact]
	public void NativeFactory_ParentsRealizedItems_AndCleansUpOnReloadRebuildAndDisconnect()
	{
		Gtk.Module.Initialize();
		Gtk.Functions.Init();
		using var app = MauiApp.CreateBuilder().UseMauiAppLinuxGtk4<Application>()
			.ConfigureMauiHandlers(handlers => handlers.AddHandler<Label, DisconnectTrackingLabelHandler>())
			.Build();
		var created = new List<Label>();
		var connected = new List<DisconnectTrackingLabelHandler>();
		void Track(Label label, bool isRoot = true)
		{
			if (isRoot)
				created.Add(label);
			label.HandlerChanged += (_, _) =>
			{
				if (label.Handler is DisconnectTrackingLabelHandler tracking && !connected.Contains(tracking))
					connected.Add(tracking);
			};
		}
		var collection = new CollectionView
		{
			ItemsSource = new[] { "Card title", "Card title" },
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { AutomationId = "Card" };
				label.SetBinding(Label.TextProperty, ".");
				Track(label);
				return label;
			}),
		};
		var handler = new CollectionViewHandler();
		handler.SetMauiContext(new GtkMauiContext(app.Services));
		using var window = Gtk.Window.New();
		try
		{
			handler.SetVirtualView(collection);
			window.SetDefaultSize(400, 300);
			window.SetChild(handler.PlatformView);
			window.Present();
			WaitUntil(() => created.Count >= 2);

			var realized = created.Where(view => view.Handler != null).ToArray();
			var originalHandlers = connected.ToArray();
			Assert.Equal(2, realized.Length);
			Assert.Equal(2, originalHandlers.Length);
			Assert.All(originalHandlers, item => Assert.Equal(0, item.DisconnectCount));
			Assert.All(realized, view => Assert.Same(collection, view.Parent));
			Assert.Equal(2, ((IVisualTreeElement)collection).GetVisualChildren().Count);
			var tree = new VisualTreeWalker().WalkElement(collection, null, 0, 20);
			Assert.Equal(2, tree!.Children!.Count(child => child.Text == "Card title" && child.AutomationId == "Card"));

			collection.ItemsSource = new[] { "Replacement" };
			WaitUntil(() => created.Any(view => view.Text == "Replacement") && realized.All(view => view.Parent == null));
			Assert.All(realized, view => Assert.Null(view.Parent));
			Assert.All(originalHandlers, item => Assert.Equal(1, item.DisconnectCount));
			Assert.Single(((IVisualTreeElement)collection).GetVisualChildren());

			var beforeRebuild = created.ToArray();
			var beforeRebuildHandlers = connected.ToArray();
			collection.ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { Text = "New template" };
				Track(label);
				return label;
			});
			WaitUntil(() => created.Any(view => view.Text == "New template" && view.Handler != null));
			Assert.All(beforeRebuild, view => Assert.Null(view.Parent));
			Assert.All(beforeRebuildHandlers, item => Assert.Equal(1, item.DisconnectCount));
			Assert.Single(((IVisualTreeElement)collection).GetVisualChildren());

			collection.GroupHeaderTemplate = new DataTemplate(() =>
			{
				var label = new Label { Text = "Group header" };
				Track(label);
				return label;
			});
			collection.IsGrouped = true;
			collection.ItemsSource = new[] { new[] { "Grouped card" } };
			WaitUntil(() => created.Any(view => view.Text == "Group header" && view.Handler != null));
			Assert.Contains(((IVisualTreeElement)collection).GetVisualChildren(),
				view => view is Label { Text: "Group header", Parent: not null });
			Assert.Equal(2, ((IVisualTreeElement)collection).GetVisualChildren().Count);

			var previousHeader = Assert.Single(created, view => view.Text == "Group header" && view.Parent != null);
			collection.GroupHeaderTemplate = new DataTemplate(() =>
			{
				var label = new Label { Text = "Replacement group header" };
				Track(label);
				return label;
			});
			WaitUntil(() => previousHeader.Parent == null &&
				created.Any(view => view.Text == "Replacement group header" && view.Parent == collection));
			var updatedTree = new VisualTreeWalker().WalkElement(collection, null, 0, 20);
			Assert.Contains(updatedTree!.Children!, child => child.Text == "Replacement group header");
			Assert.DoesNotContain(updatedTree.Children!, child => child.Text == "Group header");
			Assert.Equal(2, ((IVisualTreeElement)collection).GetVisualChildren().Count);

			collection.ItemTemplate = null;
			WaitUntil(() => ((IVisualTreeElement)collection).GetVisualChildren().Count == 1 &&
				NativeLabelTexts(handler.PlatformView).Contains("Grouped card"));
			var headerWithoutItemTemplate = Assert.IsType<Label>(
				Assert.Single(((IVisualTreeElement)collection).GetVisualChildren()));
			Assert.Equal("Replacement group header", headerWithoutItemTemplate.Text);
			collection.GroupHeaderTemplate = new DataTemplate(() =>
			{
				var label = new Label { Text = "Header with default rows" };
				Track(label);
				return label;
			});
			WaitUntil(() => headerWithoutItemTemplate.Parent == null &&
				created.Any(view => view.Text == "Header with default rows" && view.Parent == collection));
			var defaultRowsTree = new VisualTreeWalker().WalkElement(collection, null, 0, 20);
			Assert.Contains(defaultRowsTree!.Children!, child => child.Text == "Header with default rows");
			Assert.Contains("Grouped card", NativeLabelTexts(handler.PlatformView));

			collection.GroupHeaderTemplate = null;
			WaitUntil(() => created.All(view => view.Parent == null) &&
				NativeLabelTexts(handler.PlatformView).Contains("Grouped card"));
			Assert.Empty(((IVisualTreeElement)collection).GetVisualChildren());
			Assert.All(connected, item => Assert.Equal(1, item.DisconnectCount));

			collection.GroupHeaderTemplate = new DataTemplate(() =>
			{
				var label = new Label { Text = "Disconnect header" };
				Track(label);
				return label;
			});
			var nestedRoots = new List<VerticalStackLayout>();
			var nestedLeaves = new List<Label>();
			View CreateNestedItem()
			{
				var label = new Label { Text = "Disconnect item" };
				Track(label, isRoot: false);
				nestedLeaves.Add(label);
				var root = new VerticalStackLayout { new ContentView { Content = label } };
				nestedRoots.Add(root);
				return root;
			}
			collection.ItemTemplate = new DataTemplate(CreateNestedItem);
			WaitUntil(() => nestedRoots.Any(view => view.Parent == collection) &&
				nestedLeaves.Any(view => view.Handler != null));
			var firstNestedRoot = Assert.Single(nestedRoots, view => view.Parent == collection);
			var firstNestedLeaf = Assert.IsType<Label>(
				Assert.IsType<ContentView>(Assert.Single(firstNestedRoot.Children)).Content);
			var firstNestedHandler = Assert.IsType<DisconnectTrackingLabelHandler>(firstNestedLeaf.Handler);
			Assert.Equal(0, firstNestedHandler.DisconnectCount);
			collection.ItemsSource = new[] { new[] { "Replacement nested card" } };
			WaitUntil(() => firstNestedRoot.Parent == null &&
				nestedRoots.Any(view => view.Parent == collection));
			Assert.Equal(1, firstNestedHandler.DisconnectCount);

			var beforeNestedRebuild = connected.ToArray();
			var oldNestedRoots = nestedRoots.ToArray();
			collection.ItemTemplate = new DataTemplate(CreateNestedItem);
			WaitUntil(() => oldNestedRoots.All(view => view.Parent == null) &&
				nestedRoots.Any(view => view.Parent == collection));
			Assert.All(beforeNestedRebuild, item => Assert.Equal(1, item.DisconnectCount));
			Assert.Equal(2, ((IVisualTreeElement)collection).GetVisualChildren().Count);
			Assert.Contains(connected, item => item.DisconnectCount == 0);
			((IElementHandler)handler).DisconnectHandler();
			Assert.All(created, view => Assert.Null(view.Parent));
			Assert.All(nestedRoots, view => Assert.Null(view.Parent));
			Assert.All(connected, item => Assert.Equal(1, item.DisconnectCount));
			Assert.Empty(((IVisualTreeElement)collection).GetVisualChildren());
			((IElementHandler)handler).DisconnectHandler();
			Assert.All(connected, item => Assert.Equal(1, item.DisconnectCount));

			VerifyGroupedHeaderRefresh(app.Services, window, singlePass: false);
			VerifyGroupedHeaderRefresh(app.Services, window, singlePass: true);
		}
		finally
		{
			if (collection.Handler != null)
				((IElementHandler)handler).DisconnectHandler();
			window.Destroy();
		}
	}

	void VerifyGroupedHeaderRefresh(IServiceProvider services, Gtk.Window window, bool singlePass)
	{
		var created = new List<Label>();
		var connected = new List<DisconnectTrackingLabelHandler>();
		Label Track(Label label)
		{
			created.Add(label);
			label.HandlerChanged += (_, _) =>
			{
				if (label.Handler is DisconnectTrackingLabelHandler tracking && !connected.Contains(tracking))
					connected.Add(tracking);
			};
			return label;
		}
		var collection = new CollectionView();
		var handler = new CollectionViewHandler();
		handler.SetMauiContext(new GtkMauiContext(services));
		try
		{
			handler.SetVirtualView(collection);
			window.SetChild(handler.PlatformView);
			collection.IsGrouped = true;
			collection.ItemTemplate = new DataTemplate(() =>
			{
				var label = Track(new Label());
				label.SetBinding(Label.TextProperty, ".");
				return label;
			});
			collection.GroupHeaderTemplate = new DataTemplate(() => Track(new Label { Text = "Initial one-pass header" }));
			var source = new TrackingGroupedSource(singlePass);
			collection.ItemsSource = source;

			bool HasAllocatedRows(string header) =>
				created.Count(view => view.Parent == collection && IsAllocated(view)) == 2 &&
				NativeLabelTexts(handler.PlatformView).Contains(header) &&
				NativeLabelTexts(handler.PlatformView).Contains("One-pass card");
			WaitUntil(() => HasAllocatedRows("Initial one-pass header"));
			Assert.Equal(1, source.EnumerationCount);
			var original = ((IVisualTreeElement)collection).GetVisualChildren().Cast<Label>().ToArray();
			Assert.Equal(2, original.Length);
			Assert.All(original, view => Assert.Same(collection, view.Parent));
			Assert.Contains(original, view => view.Text == "Initial one-pass header");
			Assert.Contains(original, view => view.Text == "One-pass card");
			var originalHandlers = connected.ToArray();
			Assert.Equal(2, originalHandlers.Length);
			Assert.All(originalHandlers, item => Assert.Equal(0, item.DisconnectCount));
			output.WriteLine($"Grouped header initial state: singlePass={singlePass}, allocated rows=2, enumerations={source.EnumerationCount}.");

			collection.GroupHeaderTemplate = new DataTemplate(() => Track(new Label { Text = "Replacement one-pass header" }));
			WaitUntil(() => HasAllocatedRows("Replacement one-pass header"),
				() => $"After GroupHeaderTemplate-only change (singlePass={singlePass}), expected allocated header and card; logical rows={((IVisualTreeElement)collection).GetVisualChildren().Count}, source enumerations={source.EnumerationCount}.");
			Assert.Same(source, collection.ItemsSource);
			Assert.All(original, view => Assert.Null(view.Parent));
			Assert.All(originalHandlers, item => Assert.Equal(1, item.DisconnectCount));
			Assert.Equal(2, ((IVisualTreeElement)collection).GetVisualChildren().Count);
			if (singlePass)
				Assert.Equal(1, source.EnumerationCount);
			output.WriteLine($"Grouped header refresh passed: singlePass={singlePass}, allocated rows=2, enumerations={source.EnumerationCount}.");
		}
		finally
		{
			window.SetChild(null);
			if (collection.Handler != null)
				((IElementHandler)handler).DisconnectHandler();
			Assert.All(created, view => Assert.Null(view.Parent));
			Assert.All(connected, item => Assert.Equal(1, item.DisconnectCount));
		}
	}

	static bool IsAllocated(Label view) =>
		view.Handler is DisconnectTrackingLabelHandler handler &&
		handler.PlatformView.GetWidth() > 0 && handler.PlatformView.GetHeight() > 0;

	sealed class TrackingGroupedSource(bool singlePass) : System.Collections.IEnumerable
	{
		public int EnumerationCount { get; private set; }

		public System.Collections.IEnumerator GetEnumerator()
		{
			EnumerationCount++;
			return singlePass && EnumerationCount > 1
				? Array.Empty<object>().GetEnumerator()
				: Groups().GetEnumerator();
		}

		static System.Collections.IEnumerable Groups()
		{
			yield return new[] { "One-pass card" };
		}
	}

	public sealed class DisconnectTrackingLabelHandler : LabelHandler
	{
		public int DisconnectCount { get; private set; }

		protected override void DisconnectHandler(Gtk.Label platformView)
		{
			DisconnectCount++;
			base.DisconnectHandler(platformView);
		}
	}

	static IEnumerable<string> NativeLabelTexts(Gtk.Widget widget)
	{
		if (widget is Gtk.Label label)
			yield return label.GetText();
		for (var child = widget.GetFirstChild(); child != null; child = child.GetNextSibling())
			foreach (var text in NativeLabelTexts(child))
				yield return text;
	}

	static void WaitUntil(Func<bool> condition, Func<string>? failureMessage = null)
	{
		var timeout = Stopwatch.StartNew();
		while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(10))
		{
			while (GLib.MainContext.Default().Pending())
				GLib.MainContext.Default().Iteration(false);
			Thread.Sleep(10);
		}
		Assert.True(condition(), failureMessage?.Invoke() ?? "GTK did not realize the expected item before the timeout.");
	}
}
