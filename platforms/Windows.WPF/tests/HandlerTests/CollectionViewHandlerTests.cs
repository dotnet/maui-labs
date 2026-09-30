using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers.WPF;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;
using Microsoft.Maui.WPF;
using WGrid = System.Windows.Controls.Grid;
using WListBoxItem = System.Windows.Controls.ListBoxItem;

namespace HandlerTests;

public class CollectionViewHandlerTests
{
	[Fact]
	public void PreparedItem_IsParented_AndVisibleToDevFlow()
	{
		Run((collection, handler, list, created) =>
		{
			var container = new WListBoxItem();
			Prepare(list, container, "Card title");
			var view = Assert.Single(created);
			Assert.Same(collection, view.Parent);
			Assert.Equal("Card title", view.BindingContext);
			Assert.NotNull(view.Handler?.PlatformView);
			Assert.Equal("ParentedBeforeHandler", view.ClassId);
			Assert.Contains(view, ((IVisualTreeElement)collection).GetVisualChildren());

			var tree = new VisualTreeWalker().WalkElement(collection, null, 0, 20);
			Assert.Contains(tree!.Children!, child =>
				child.Type == nameof(Label) && child.Text == "Card title" && child.AutomationId == "Card");
		});
	}

	[Fact]
	public void ClearContainer_WithEqualItems_RemovesOnlyItsOwnLogicalChild()
	{
		Run((collection, handler, list, created) =>
		{
			var first = new WListBoxItem();
			var second = new WListBoxItem();
			Prepare(list, first, "Same item");
			Prepare(list, second, "Same item");
			Assert.Equal(2, created.Count);
			Clear(list, first, "Same item");

			Assert.Null(created[0].Parent);
			Assert.Same(collection, created[1].Parent);
			Assert.Same(created[1], Assert.Single(((IVisualTreeElement)collection).GetVisualChildren()));
			Clear(list, second, "Same item");
			Assert.Null(created[1].Parent);
			Assert.Empty(((IVisualTreeElement)collection).GetVisualChildren());
		});
	}

	[Fact]
	public void PrepareReusedContainer_DetachesPreviousView_AndDisconnectRemovesRemainingViews()
	{
		Run((collection, handler, list, created) =>
		{
			var container = new WListBoxItem();
			Prepare(list, container, "First");
			Prepare(list, container, "Second");
			Assert.Null(created[0].Parent);
			Assert.Same(collection, created[1].Parent);
			Assert.Single(((IVisualTreeElement)collection).GetVisualChildren());

			((IElementHandler)handler).DisconnectHandler();
			Assert.All(created, view => Assert.Null(view.Parent));
			Assert.Empty(((IVisualTreeElement)collection).GetVisualChildren());
			Clear(list, container, "Second");
			Assert.Empty(((IVisualTreeElement)collection).GetVisualChildren());
		});
	}

	[Fact]
	public void FailedPlatformConversion_DoesNotLeaveLogicalChild()
	{
		Run((collection, handler, list, created) =>
		{
			var unsupported = new UnsupportedView();
			collection.ItemTemplate = new DataTemplate(() => unsupported);
			Prepare(list, new WListBoxItem(), "Unsupported");
			Assert.Null(unsupported.Parent);
			Assert.Empty(((IVisualTreeElement)collection).GetVisualChildren());
		});
	}

	[Fact]
	public void GroupHeadersAndSelectorItems_AreParentedAndCleared()
	{
		Run((collection, handler, list, created) =>
		{
			collection.IsGrouped = true;
			collection.GroupHeaderTemplate = new DataTemplate(() => new Label { Text = "Group" });
			collection.ItemTemplate = new CardTemplateSelector(collection.ItemTemplate);
			collection.ItemsSource = new[] { new[] { "Grouped card" } };
			var headerContainer = new WListBoxItem();
			var itemContainer = new WListBoxItem();
			Prepare(list, headerContainer, list.Items[0]);
			Prepare(list, itemContainer, list.Items[1]);
			var children = ((IVisualTreeElement)collection).GetVisualChildren();
			Assert.Equal(2, children.Count);
			Assert.All(children, child => Assert.Same(collection, Assert.IsType<Label>(child).Parent));
			Clear(list, headerContainer, list.Items[0]);
			Clear(list, itemContainer, list.Items[1]);
			Assert.Empty(((IVisualTreeElement)collection).GetVisualChildren());
		});
	}

	sealed class CardTemplateSelector(DataTemplate template) : DataTemplateSelector
	{
		protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => template;
	}

	[Fact]
	public void ItemTemplateChange_ReplacesLogicalChildren_WithoutChangingItemsSource()
	{
		Run((collection, handler, list, created) =>
		{
			Realize(list);
			var original = Assert.Single(created);
			var source = collection.ItemsSource;
			collection.SelectionMode = SelectionMode.Single;
			list.SelectedIndex = 0;
			var selected = collection.SelectedItem;
			var taps = 0;
			collection.ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { Text = "New template" };
				label.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => taps++) });
				return label;
			});
			Realize(list);

			Assert.Same(source, collection.ItemsSource);
			Assert.Same(selected, collection.SelectedItem);
			Assert.Same(selected, list.SelectedItem);
			Assert.Equal(0, taps);
			Assert.Null(original.Parent);
			var replacement = Assert.IsType<Label>(Assert.Single(((IVisualTreeElement)collection).GetVisualChildren()));
			Assert.Equal("New template", replacement.Text);
			Assert.Same(collection, replacement.Parent);
			var tree = new VisualTreeWalker().WalkElement(collection, null, 0, 20);
			Assert.Contains(tree!.Children!, child => child.Text == "New template");
			Assert.DoesNotContain(tree.Children!, child => child.AutomationId == "Card");

			collection.ItemTemplate = null;
			Realize(list);
			Assert.Null(replacement.Parent);
			Assert.Empty(((IVisualTreeElement)collection).GetVisualChildren());
		});
	}

	[Fact]
	public void GroupingChange_ReplacesLogicalChildren_WithoutChangingItemsSource()
	{
		Run((collection, handler, list, created) =>
		{
			collection.GroupHeaderTemplate = new DataTemplate(() => new Label { Text = "Group header" });
			collection.ItemsSource = new[] { new[] { "Grouped card" } };
			Realize(list);
			var original = Assert.Single(created);
			var source = collection.ItemsSource;

			collection.IsGrouped = true;
			Realize(list);
			Assert.Same(source, collection.ItemsSource);
			Assert.Null(original.Parent);
			var grouped = ((IVisualTreeElement)collection).GetVisualChildren().Cast<Label>().ToArray();
			Assert.Equal(2, grouped.Length);
			Assert.All(grouped, view => Assert.Same(collection, view.Parent));
			Assert.Contains(grouped, view => view.Text == "Group header");
			Assert.Contains(grouped, view => view.Text == "Grouped card");

			collection.IsGrouped = false;
			Realize(list);
			Assert.All(grouped, view => Assert.Null(view.Parent));
			Assert.Single(((IVisualTreeElement)collection).GetVisualChildren());
		});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void GroupTemplateChange_RebuildsRealizedRoots(bool footer)
	{
		Run((collection, handler, list, created) =>
		{
			collection.IsGrouped = true;
			collection.GroupHeaderTemplate = new DataTemplate(() => new Label { Text = "Original header" });
			if (footer)
				collection.GroupFooterTemplate = new DataTemplate(() => new Label { Text = "Original footer" });
			collection.ItemsSource = new[] { new[] { "Grouped card" } };
			Realize(list);
			var original = ((IVisualTreeElement)collection).GetVisualChildren().Cast<Label>().ToArray();
			Assert.Equal(footer ? 3 : 2, original.Length);
			var template = new DataTemplate(() => new Label { Text = "Replacement group template" });

			if (footer)
				collection.GroupFooterTemplate = template;
			else
				collection.GroupHeaderTemplate = template;
			Realize(list);

			Assert.All(original, view => Assert.Null(view.Parent));
			var children = ((IVisualTreeElement)collection).GetVisualChildren().Cast<Label>().ToArray();
			Assert.Equal(footer ? 3 : 2, children.Length);
			Assert.All(children, view => Assert.Same(collection, view.Parent));
			Assert.Contains(children, view => view.Text == "Replacement group template");
			Assert.DoesNotContain(children, view => view.Text == (footer ? "Original footer" : "Original header"));
			if (footer)
			{
				collection.GroupFooterTemplate = null;
				Realize(list);
				Assert.All(children, view => Assert.Null(view.Parent));
				Assert.Equal(2, ((IVisualTreeElement)collection).GetVisualChildren().Count);
			}
		});
	}

	[Theory]
	[InlineData(SelectionMode.Single)]
	[InlineData(SelectionMode.Multiple)]
	public void GroupedTemplateChanges_PreserveSelectedOccurrences(SelectionMode mode)
	{
		Run((collection, handler, list, created) =>
		{
			collection.SelectionMode = mode;
			collection.IsGrouped = true;
			collection.GroupHeaderTemplate = new DataTemplate(() => new Label { Text = "Header" });
			collection.ItemsSource = new[] { new[] { "Same item", "Same item" } };
			Realize(list);
			list.SelectedIndex = 2;
			if (mode == SelectionMode.Multiple)
				Assert.IsType<WListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(1)).IsSelected = true;
			var selected = list.SelectedItems.Cast<object>().ToArray();
			var selectedItem = collection.SelectedItem;
			var selectedItems = collection.SelectedItems;
			var taps = 0;

			collection.ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { Text = "Replacement" };
				label.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => taps++) });
				return label;
			});
			Realize(list);
			AssertSelection();

			collection.GroupFooterTemplate = new DataTemplate(() => new Label { Text = "Footer" });
			Realize(list);
			AssertSelection();
			Assert.Equal(4, list.Items.Count);

			collection.GroupFooterTemplate = null;
			Realize(list);
			AssertSelection();
			Assert.Equal(3, list.Items.Count);

			void AssertSelection()
			{
				Assert.Equal(selected, list.SelectedItems.Cast<object>().ToArray());
				Assert.Same(selectedItem, collection.SelectedItem);
				Assert.Same(selectedItems, collection.SelectedItems);
				Assert.Equal(0, taps);
				Assert.True(Assert.IsType<WListBoxItem>(
					list.ItemContainerGenerator.ContainerFromIndex(2)).IsSelected);
			}
		});
	}

	[Theory]
	[InlineData(SelectionMode.Single)]
	[InlineData(SelectionMode.None)]
	public void SelectingEqualItems_FiresGestureOnTheSelectedContainer(SelectionMode mode)
	{
		Run((collection, handler, list, created) =>
		{
			var tapped = new List<Label>();
			collection.SelectionMode = mode;
			collection.ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { Text = "Same item" };
				label.GestureRecognizers.Add(new TapGestureRecognizer
				{
					Command = new Command(() => tapped.Add(label)),
				});
				created.Add(label);
				return label;
			});
			collection.ItemsSource = new[] { "Same item", "Same item" };
			Realize(list);
			Assert.Equal(2, created.Count);
			Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(1));

			list.SelectedIndex = 1;
			Assert.Same(created[1], Assert.Single(tapped));
			list.UnselectAll();
			list.SelectedIndex = 1;
			Assert.Equal(2, tapped.Count);
			Assert.All(tapped, view => Assert.Same(created[1], view));
		});
	}

	sealed class UnsupportedView : View;

	static void Realize(MauiCollectionListBox list)
	{
		list.ApplyTemplate();
		list.Measure(new System.Windows.Size(400, 300));
		list.Arrange(new System.Windows.Rect(0, 0, 400, 300));
		list.UpdateLayout();
	}

	static void Run(Action<CollectionView, CollectionViewHandler, MauiCollectionListBox, List<Label>> action)
	{
		RunOnSta(() =>
		{
			_ = System.Windows.Threading.Dispatcher.CurrentDispatcher;
			DispatcherProvider.SetCurrent(new WPFDispatcherProvider());
			using var app = MauiApp.CreateBuilder().UseMauiAppWPF<Application>().Build();
			var created = new List<Label>();
			var collection = new CollectionView
			{
				ItemsSource = new[] { "Card title" },
				ItemTemplate = new DataTemplate(() =>
				{
					var label = new Label { AutomationId = "Card" };
					label.HandlerChanged += (_, _) =>
					{
						if (label.Handler != null)
							label.ClassId = label.Parent is CollectionView ? "ParentedBeforeHandler" : "Unparented";
					};
					label.SetBinding(Label.TextProperty, ".");
					created.Add(label);
					return label;
				}),
			};
			var handler = new CollectionViewHandler();
			handler.SetMauiContext(new WPFMauiContext(app.Services));
			try
			{
				handler.SetVirtualView(collection);
				var root = Assert.IsType<WGrid>(handler.PlatformView);
				var list = Assert.IsType<MauiCollectionListBox>(root.Children[0]);
				action(collection, handler, list, created);
			}
			finally
			{
				if (collection.Handler != null)
					((IElementHandler)handler).DisconnectHandler();
			}
		});
	}

	static void RunOnSta(Action action)
	{
		Exception? error = null;
		var thread = new Thread(() =>
		{
			try { action(); }
			catch (Exception ex) { error = ex; }
		}) { IsBackground = true };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "CollectionView test STA thread timed out.");
		if (error != null)
			ExceptionDispatchInfo.Capture(error).Throw();
	}

	static void Prepare(MauiCollectionListBox list, WListBoxItem container, object item)
		=> InvokeContainerCallback(list, "PrepareContainerForItemOverride", container, item);

	static void Clear(MauiCollectionListBox list, WListBoxItem container, object item)
		=> InvokeContainerCallback(list, "ClearContainerForItemOverride", container, item);

	// Exercise the real native lifecycle callbacks without opening a desktop window.
	static void InvokeContainerCallback(MauiCollectionListBox list, string name, WListBoxItem container, object item)
		=> typeof(MauiCollectionListBox).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
			.Invoke(list, [container, item]);
}
