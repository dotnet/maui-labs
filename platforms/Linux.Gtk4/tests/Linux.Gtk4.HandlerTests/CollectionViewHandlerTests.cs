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
