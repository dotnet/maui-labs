using System.Diagnostics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.HandlerTests;

public class CollectionViewHandlerTests
{
	[Fact]
	public void NativeFactory_ParentsRealizedItems_AndCleansUpOnReloadRebuildAndDisconnect()
	{
		Gtk.Module.Initialize();
		Gtk.Functions.Init();
		using var app = MauiApp.CreateBuilder().UseMauiAppLinuxGtk4<Application>().Build();
		var created = new List<Label>();
		var collection = new CollectionView
		{
			ItemsSource = new[] { "Card title", "Card title" },
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { AutomationId = "Card" };
				label.SetBinding(Label.TextProperty, ".");
				created.Add(label);
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
			Assert.Equal(2, realized.Length);
			Assert.All(realized, view => Assert.Same(collection, view.Parent));
			Assert.Equal(2, ((IVisualTreeElement)collection).GetVisualChildren().Count);
			var tree = new VisualTreeWalker().WalkElement(collection, null, 0, 20);
			Assert.Equal(2, tree!.Children!.Count(child => child.Text == "Card title" && child.AutomationId == "Card"));

			collection.ItemsSource = new[] { "Replacement" };
			WaitUntil(() => created.Any(view => view.Text == "Replacement") && realized.All(view => view.Parent == null));
			Assert.All(realized, view => Assert.Null(view.Parent));
			Assert.Single(((IVisualTreeElement)collection).GetVisualChildren());

			var beforeRebuild = created.ToArray();
			collection.ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { Text = "New template" };
				created.Add(label);
				return label;
			});
			WaitUntil(() => created.Any(view => view.Text == "New template" && view.Handler != null));
			Assert.All(beforeRebuild, view => Assert.Null(view.Parent));
			Assert.Single(((IVisualTreeElement)collection).GetVisualChildren());

			collection.GroupHeaderTemplate = new DataTemplate(() =>
			{
				var label = new Label { Text = "Group header" };
				created.Add(label);
				return label;
			});
			collection.IsGrouped = true;
			collection.ItemsSource = new[] { new[] { "Grouped card" } };
			WaitUntil(() => created.Any(view => view.Text == "Group header" && view.Handler != null));
			Assert.Contains(((IVisualTreeElement)collection).GetVisualChildren(),
				view => view is Label { Text: "Group header", Parent: not null });
			Assert.Equal(2, ((IVisualTreeElement)collection).GetVisualChildren().Count);

			var previousHeader = Assert.Single(created.Where(view => view.Text == "Group header" && view.Parent != null));
			collection.GroupHeaderTemplate = new DataTemplate(() =>
			{
				var label = new Label { Text = "Replacement group header" };
				created.Add(label);
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
				created.Add(label);
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

			collection.GroupHeaderTemplate = new DataTemplate(() =>
			{
				var label = new Label { Text = "Disconnect header" };
				created.Add(label);
				return label;
			});
			collection.ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { Text = "Disconnect item" };
				created.Add(label);
				return label;
			});
			WaitUntil(() => created.Count(view => view.Parent == collection) == 2);
			((IElementHandler)handler).DisconnectHandler();
			Assert.All(created, view => Assert.Null(view.Parent));
			Assert.Empty(((IVisualTreeElement)collection).GetVisualChildren());
		}
		finally
		{
			if (collection.Handler != null)
				((IElementHandler)handler).DisconnectHandler();
			window.Destroy();
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

	static void WaitUntil(Func<bool> condition)
	{
		var timeout = Stopwatch.StartNew();
		while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(10))
		{
			while (GLib.MainContext.Default().Pending())
				GLib.MainContext.Default().Iteration(false);
			Thread.Sleep(10);
		}
		Assert.True(condition(), "GTK did not realize the expected item before the timeout.");
	}
}
