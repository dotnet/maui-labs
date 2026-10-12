using System.Diagnostics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Graphics;
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

			VerifyLayoutCallbackLifetime(app.Services, window);
			VerifyGroupedHeaderRefresh(app.Services, window, singlePass: false);
			VerifyGroupedHeaderRefresh(app.Services, window, singlePass: true);
			VerifyTemplateRefreshState(app.Services, window);
		}
		finally
		{
			if (collection.Handler != null)
				((IElementHandler)handler).DisconnectHandler();
			window.Destroy();
		}
	}

	void VerifyLayoutCallbackLifetime(IServiceProvider services, Gtk.Window window)
	{
		var handler = new LayoutHandler();
		handler.SetMauiContext(new GtkMauiContext(services));
		using var paned = Gtk.Paned.New(Gtk.Orientation.Horizontal);
		using var sibling = Gtk.Label.New("Sibling pane");
		paned.SetEndChild(sibling);
		try
		{
			var disconnected = new CountingLayout();
			handler.SetVirtualView(disconnected);
			var disconnectedPanel = handler.PlatformView;
			window.SetChild(disconnectedPanel);
			Assert.Equal((0, 0), disconnected.Counts);
			((IElementHandler)handler).DisconnectHandler();
			window.SetChild(null);
			PumpThroughSentinel();
			Assert.Equal((0, 0), disconnected.Counts);
			Assert.Null(disconnectedPanel.CrossPlatformLayout);
			Assert.Null(((IElementHandler)handler).VirtualView);

			var superseded = new CountingLayout();
			handler.SetVirtualView(superseded);
			var panel = handler.PlatformView;
			var current = new CountingLayout();
			handler.SetVirtualView(current);
			Assert.Same(panel, handler.PlatformView);
			Assert.Same(current, panel.CrossPlatformLayout);
			paned.SetStartChild(panel);
			window.SetChild(paned);
			paned.SetPosition(160);
			// Swapping the window's child queues GTK's allocate pass on its frame
			// clock rather than the next idle round; a single PumpThroughSentinel()
			// can race ahead of that under CI's virtual display (observed as a real
			// CI failure: MeasureCount/ArrangeCount still 0 immediately after pump).
			// Wait for the actual condition instead, matching the pattern already
			// used below for the dirty-tick assertion.
			PumpUntil(() => current.MeasureCount > 0 && current.ArrangeCount > 0);
			Assert.Equal((0, 0), superseded.Counts);

			var sameBindingCounts = current.Counts;
			handler.SetVirtualView(current);
			Assert.Same(panel, handler.PlatformView);
			Assert.Equal(sameBindingCounts, current.Counts);
			window.SetDefaultSize(440, 320);
			// Same frame-clock-vs-idle race as above: a resize queues the allocate
			// pass asynchronously, so wait for the actual condition rather than
			// asserting immediately.
			PumpUntil(() => current.MeasureCount > sameBindingCounts.Measure && current.ArrangeCount > sameBindingCounts.Arrange);
			var beforePaned = current.Counts;
			paned.SetPosition(180);
			PumpUntil(() => current.MeasureCount > beforePaned.Measure && current.ArrangeCount > beforePaned.Arrange);
			var beforeTick = current.Counts;
			panel.LayoutDirty = true;
			PumpUntil(() => current.MeasureCount > beforeTick.Measure && current.ArrangeCount > beforeTick.Arrange);

			var retiredCounts = current.Counts;
			var replacement = new CountingLayout();
			handler.SetVirtualView(replacement);
			Assert.Same(panel, handler.PlatformView);
			Assert.Same(replacement, panel.CrossPlatformLayout);
			// SetVirtualView(replacement) queues a fresh layout pass on the frame
			// clock, same race as above: wait for it instead of a single blind
			// idle pump.
			PumpUntil(() => replacement.MeasureCount > 0 && replacement.ArrangeCount > 0);
			Assert.Equal(retiredCounts, current.Counts);
			var beforeResize = replacement.Counts;
			window.SetDefaultSize(460, 340);
			paned.SetPosition(200);
			PumpUntil(() => replacement.MeasureCount > beforeResize.Measure && replacement.ArrangeCount > beforeResize.Arrange);
			Assert.Equal(retiredCounts, current.Counts);

			var disconnectedCounts = replacement.Counts;
			((IElementHandler)handler).DisconnectHandler();
			Assert.Null(panel.CrossPlatformLayout);
			window.SetDefaultSize(480, 360);
			paned.SetPosition(220);
			panel.LayoutDirty = true;
			PumpThroughSentinel();
			Assert.Equal(disconnectedCounts, replacement.Counts);
			Assert.Equal(retiredCounts, current.Counts);
			Assert.Equal((0, 0), disconnected.Counts);
			Assert.Equal((0, 0), superseded.Counts);
			output.WriteLine("Layout callback lifetime passed: disconnect-before-pump and superseded bindings measured/arranged zero times; same/current bindings responded to resize, paned and dirty tick; retired counter deltas stayed zero.");
		}
		finally
		{
			if (((IElementHandler)handler).VirtualView != null)
				((IElementHandler)handler).DisconnectHandler();
			window.SetChild(null);
			paned.SetStartChild(null);
			paned.SetEndChild(null);
			window.SetDefaultSize(400, 300);
		}
	}

	sealed class CountingLayout : VerticalStackLayout, ICrossPlatformLayout
	{
		public int MeasureCount { get; private set; }
		public int ArrangeCount { get; private set; }
		public (int Measure, int Arrange) Counts => (MeasureCount, ArrangeCount);

		Size ICrossPlatformLayout.CrossPlatformMeasure(double widthConstraint, double heightConstraint)
		{
			MeasureCount++;
			return new Size(Math.Min(80, widthConstraint), Math.Min(40, heightConstraint));
		}

		Size ICrossPlatformLayout.CrossPlatformArrange(Rect bounds)
		{
			ArrangeCount++;
			return bounds.Size;
		}
	}

	static void PumpThroughSentinel()
	{
		var reached = false;
		var source = GLib.Functions.IdleAdd(200, () =>
		{
			reached = true;
			return false;
		});
		try
		{
			PumpUntil(() => reached);
		}
		finally
		{
			if (!reached)
				GLib.Functions.SourceRemove(source);
		}
	}

	static void PumpUntil(Func<bool> condition)
	{
		var timedOut = false;
		uint timeoutSource = 0;
		timeoutSource = GLib.Functions.TimeoutAdd(0, 10_000, () =>
		{
			timeoutSource = 0;
			timedOut = true;
			return false;
		});
		try
		{
			while (!condition() && !timedOut)
				GLib.MainContext.Default().Iteration(true);
			Assert.True(condition(), "GTK did not complete the queued lifecycle work before the deadline.");
		}
		finally
		{
			if (timeoutSource != 0)
				GLib.Functions.SourceRemove(timeoutSource);
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

	void VerifyTemplateRefreshState(IServiceProvider services, Gtk.Window window)
	{
		var created = new List<Label>();
		var connected = new List<DisconnectTrackingLabelHandler>();
		DataTemplate BoundTemplate() => new(() =>
		{
			var label = new Label();
			label.SetBinding(Label.TextProperty, ".");
			created.Add(label);
			label.HandlerChanged += (_, _) =>
			{
				if (label.Handler is DisconnectTrackingLabelHandler tracking && !connected.Contains(tracking))
					connected.Add(tracking);
			};
			return label;
		});
		var source = new System.Collections.ObjectModel.ObservableCollection<string> { "First", "Second" };
		var collection = new CollectionView
		{
			ItemsSource = source,
			ItemTemplate = BoundTemplate(),
			SelectionMode = SelectionMode.Single,
			EmptyView = "No cards",
		};
		var handler = new CollectionViewHandler();
		handler.SetMauiContext(new GtkMauiContext(services));
		try
		{
			handler.SetVirtualView(collection);
			window.SetChild(handler.PlatformView);
			WaitUntil(() => created.Count(view => view.Parent == collection && IsAllocated(view)) == 2);
			collection.SelectedItem = source[1];
			var selected = collection.SelectedItem;
			var selectedItems = collection.SelectedItems;
			var changes = 0;
			collection.SelectionChanged += (_, _) => changes++;
			foreach (var templated in new[] { false, true })
			{
				collection.ItemTemplate = templated ? BoundTemplate() : null;
				WaitUntil(() => ((IVisualTreeElement)collection).GetVisualChildren().Count == (templated ? 2 : 0) &&
					NativeLabelTexts(handler.PlatformView).Contains("Second"));
				var list = Assert.IsType<Gtk.ListView>(handler.PlatformView.GetChild());
				Assert.Equal(1u, Assert.IsType<Gtk.SingleSelection>(list.GetModel()).GetSelected());
				Assert.Same(selected, collection.SelectedItem);
				Assert.Same(selectedItems, collection.SelectedItems);
				Assert.Equal(0, changes);
			}
			var currentList = Assert.IsType<Gtk.ListView>(handler.PlatformView.GetChild());
			Assert.IsType<Gtk.SingleSelection>(currentList.GetModel()).SetSelected(0);
			Assert.Equal("First", collection.SelectedItem);

			source.Clear();
			WaitUntil(() => NativeLabelTexts(handler.PlatformView).Contains("No cards"));
			collection.ItemTemplate = BoundTemplate();
			Assert.Equal("No cards", Assert.Single(NativeLabelTexts(handler.PlatformView)));
			source.Add("Third");
			WaitUntil(() => created.Any(view => view.Parent == collection && view.Text == "Third" && IsAllocated(view)));
			Assert.Same(source, collection.ItemsSource);
			Assert.Single(((IVisualTreeElement)collection).GetVisualChildren());
			output.WriteLine("Template refresh retained selection, empty view and observable updates.");
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
