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

[CollectionDefinition("CollectionView native lifecycle", DisableParallelization = true)]
public sealed class CollectionViewNativeLifecycleCollection;

[Collection("CollectionView native lifecycle")]
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
				GetContainer(list, 1).IsSelected = true;
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
				Assert.True(GetContainer(list, 2).IsSelected);
			}
		});
	}

	[Theory]
	[InlineData(SelectionMode.Single, true)]
	[InlineData(SelectionMode.Multiple, true)]
	[InlineData(SelectionMode.Single, false)]
	[InlineData(SelectionMode.Multiple, false)]
	public void UngroupedItemTemplateChange_PreservesSelectedOccurrence(SelectionMode mode, bool equalItems)
	{
		Run((collection, handler, list, created) =>
		{
			collection.SelectionMode = mode;
			collection.ItemsSource = equalItems ? new[] { "Same", "Same" } : new[] { "First", "Second" };
			Realize(list);
			list.SelectedIndex = 1;
			Assert.Equal(1, list.SelectedIndex);
			Assert.Single(list.SelectedItems);
			Assert.True(GetContainer(list, 1).IsSelected);
			Assert.False(GetContainer(list, 0).IsSelected);
			var selectedItem = collection.SelectedItem;
			var selectedItems = collection.SelectedItems;
			var original = ((IVisualTreeElement)collection).GetVisualChildren().Cast<View>().ToArray();
			var taps = 0;
			var selectionChanges = 0;
			collection.SelectionChanged += (_, _) => selectionChanges++;

			collection.ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { Text = "Replacement" };
				label.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => taps++) });
				return label;
			});
			Realize(list);

			Assert.True(list.SelectedIndex == 1,
				$"After template change, selected index was {list.SelectedIndex}; expected the original index 1.");
			Assert.Single(list.SelectedItems);
			Assert.True(GetContainer(list, 1).IsSelected);
			Assert.False(GetContainer(list, 0).IsSelected);
			Assert.Same(selectedItem, collection.SelectedItem);
			Assert.Same(selectedItems, collection.SelectedItems);
			Assert.Equal(0, selectionChanges);
			Assert.Equal(0, taps);
			Assert.All(original, view => Assert.Null(view.Parent));
			Assert.All(((IVisualTreeElement)collection).GetVisualChildren(),
				view => Assert.Equal("Replacement", Assert.IsType<Label>(view).Text));
		});
	}

	[Theory]
	[InlineData(true, "item")]
	[InlineData(true, "header")]
	[InlineData(true, "footer")]
	[InlineData(false, "item")]
	[InlineData(false, "header")]
	[InlineData(false, "footer")]
	public void GroupedTemplateChange_PreservesAssignedSourceRows(bool singlePass, string templateKind)
	{
		Run((collection, handler, list, created) =>
		{
			collection.ItemsSource = null;
			collection.IsGrouped = true;
			collection.GroupHeaderTemplate = new DataTemplate(() => new Label { Text = "Initial header" });
			collection.ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label();
				label.SetBinding(Label.TextProperty, ".");
				return label;
			});
			var source = new TrackingGroupedSource(singlePass);
			collection.ItemsSource = source;
			Realize(list);

			Assert.Equal(1, source.EnumerationCount);
			Assert.Equal(2, list.Items.Count);
			var original = ((IVisualTreeElement)collection).GetVisualChildren().Cast<Label>().ToArray();
			Assert.Equal(2, original.Length);
			Assert.Contains(original, view => view.Text == "Initial header");
			Assert.Contains(original, view => view.Text == "Grouped card");
			Assert.All(original, view => Assert.Same(collection, view.Parent));
			Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(0));
			Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(1));

			switch (templateKind)
			{
				case "item": collection.ItemTemplate = new DataTemplate(() => new Label { Text = "Replacement item" }); break;
				case "header": collection.GroupHeaderTemplate = new DataTemplate(() => new Label { Text = "Replacement header" }); break;
				case "footer": collection.GroupFooterTemplate = new DataTemplate(() => new Label { Text = "Replacement footer" }); break;
				default: throw new ArgumentOutOfRangeException(nameof(templateKind));
			}
			Realize(list);

			var expectedCount = templateKind == "footer" ? 3 : 2;
			Assert.True(list.Items.Count == expectedCount,
				$"After {templateKind} template change, rows were {list.Items.Count}; expected {expectedCount}. Source enumerations: {source.EnumerationCount}.");
			Assert.Same(source, collection.ItemsSource);
			Assert.All(original, view => Assert.Null(view.Parent));
			var current = ((IVisualTreeElement)collection).GetVisualChildren().Cast<Label>().ToArray();
			Assert.Equal(expectedCount, current.Length);
			Assert.All(current, view => Assert.Same(collection, view.Parent));
			Assert.Contains(current, view => view.Text == $"Replacement {templateKind}");
			Assert.Contains(current, view => view.Text == (templateKind == "item" ? "Initial header" : "Grouped card"));
		});
	}

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
			yield return new[] { "Grouped card" };
		}
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

	[Theory]
	[InlineData(SelectionMode.Single)]
	[InlineData(SelectionMode.Multiple)]
	public void DefaultRows_BecomeTemplated_WithoutReplacingSelectedContainers(SelectionMode mode)
	{
		Run((collection, handler, list, created) =>
		{
			collection.ItemTemplate = null;
			collection.SelectionMode = mode;
			collection.ItemsSource = new[] { "Same", "Same" };
			Realize(list);
			var first = GetContainer(list, 0);
			var second = GetContainer(list, 1);
			list.SelectedIndex = 1;
			if (mode == SelectionMode.Multiple)
				first.IsSelected = true;
			var selectedItem = collection.SelectedItem;
			var selectedItems = collection.SelectedItems;
			var changes = 0;
			var taps = 0;
			collection.SelectionChanged += (_, _) => changes++;
			Assert.Empty(((IVisualTreeElement)collection).GetVisualChildren());

			collection.ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { Text = "Templated" };
				label.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => taps++) });
				return label;
			});
			Realize(list);

			Assert.Same(first, list.ItemContainerGenerator.ContainerFromIndex(0));
			Assert.Same(second, list.ItemContainerGenerator.ContainerFromIndex(1));
			Assert.True(second.IsSelected);
			Assert.Equal(mode == SelectionMode.Multiple, first.IsSelected);
			Assert.Equal(mode == SelectionMode.Multiple ? 2 : 1, list.SelectedItems.Count);
			Assert.Same(selectedItem, collection.SelectedItem);
			Assert.Same(selectedItems, collection.SelectedItems);
			Assert.Equal(0, changes);
			Assert.Equal(0, taps);
			var children = ((IVisualTreeElement)collection).GetVisualChildren();
			Assert.Equal(2, children.Count);
			Assert.All(children, child =>
			{
				var label = Assert.IsType<Label>(child);
				Assert.Equal("Templated", label.Text);
				Assert.Same(collection, label.Parent);
			});
		});
	}

	[Fact]
	public void GroupFooters_IncludeEmptyGroups_WithoutReenumeratingOrLosingSelection()
	{
		Run((collection, handler, list, created) =>
		{
			collection.ItemsSource = null;
			collection.IsGrouped = true;
			collection.SelectionMode = SelectionMode.Single;
			collection.GroupHeaderTemplate = new DataTemplate(() => new Label { Text = "Header" });
			var groups = new[] { Array.Empty<string>(), new[] { "Card" }, Array.Empty<string>() };
			var enumerations = 0;
			System.Collections.IEnumerable Groups()
			{
				Assert.Equal(1, ++enumerations);
				foreach (var group in groups)
					yield return group;
			}
			collection.ItemsSource = Groups();
			Realize(list);
			Assert.Equal(4, list.Items.Count);
			list.SelectedIndex = 2;
			var selected = list.SelectedItem;
			var changes = 0;
			collection.SelectionChanged += (_, _) => changes++;

			foreach (var includeFooters in new[] { true, false })
			{
				var previous = ((IVisualTreeElement)collection).GetVisualChildren().Cast<View>().ToArray();
				collection.GroupFooterTemplate = includeFooters
					? new DataTemplate(() => new Label { Text = "Footer" })
					: null;
				Realize(list);
				Assert.Equal(1, enumerations);
				Assert.Equal(includeFooters ? 7 : 4, list.Items.Count);
				Assert.Equal(includeFooters ? 3 : 2, list.SelectedIndex);
				Assert.Same(selected, list.SelectedItem);
				Assert.Same(selected, collection.SelectedItem);
				Assert.Equal(0, changes);
				Assert.All(previous, view => Assert.Null(view.Parent));
				var current = ((IVisualTreeElement)collection).GetVisualChildren().Cast<Label>().ToArray();
				Assert.Equal(includeFooters ? 7 : 4, current.Length);
				Assert.Equal(3, current.Count(view => view.Text == "Header"));
				Assert.Equal(includeFooters ? 3 : 0, current.Count(view => view.Text == "Footer"));
			}
		});
	}

	[Fact]
	public void TemplateRefresh_KeepsObservableSourceUpdatesLive()
	{
		Run((collection, handler, list, created) =>
		{
			var source = new System.Collections.ObjectModel.ObservableCollection<string> { "First", "Second" };
			collection.ItemsSource = source;
			Realize(list);
			collection.ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { AutomationId = "Replacement" };
				label.SetBinding(Label.TextProperty, ".");
				return label;
			});
			Realize(list);
			Assert.Same(source, list.ItemsSource);
			var old = ((IVisualTreeElement)collection).GetVisualChildren().Cast<View>().ToArray();
			source.Add("Third");
			source.RemoveAt(0);
			source[0] = "Changed";
			source.Move(1, 0);
			Realize(list);

			Assert.Equal(new[] { "Third", "Changed" }, list.Items.Cast<string>().ToArray());
			Assert.All(old, view => Assert.Null(view.Parent));
			var current = ((IVisualTreeElement)collection).GetVisualChildren().Cast<Label>().ToArray();
			Assert.Equal(2, current.Length);
			Assert.All(current, view => Assert.Equal("Replacement", view.AutomationId));
			Assert.Contains(current, view => view.Text == "Third");
			Assert.Contains(current, view => view.Text == "Changed");
		});
	}

	[Fact]
	public void TemplateRefresh_PreservesUnrealizedSelection_AndRecycledContainerLifecycle()
	{
		Run((collection, handler, list, created) =>
		{
			var window = new System.Windows.Window
			{
				Content = handler.PlatformView,
				Width = 400,
				Height = 300,
				ShowActivated = false,
				ShowInTaskbar = false,
			};
			void Layout()
			{
				window.UpdateLayout();
				window.Dispatcher.Invoke(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
				window.UpdateLayout();
			}
			try
			{
				System.Windows.Controls.VirtualizingPanel.SetIsVirtualizing(list, true);
				System.Windows.Controls.VirtualizingPanel.SetVirtualizationMode(list, System.Windows.Controls.VirtualizationMode.Recycling);
				System.Windows.Controls.ScrollViewer.SetCanContentScroll(list, true);
				var source = Enumerable.Range(0, 200).Select(index => $"Item {index}").ToArray();
				collection.SelectionMode = SelectionMode.Single;
				collection.ItemsSource = source;
				window.Show();
				Layout();
				var old = ((IVisualTreeElement)collection).GetVisualChildren().Cast<View>().ToArray();
				Assert.InRange(old.Length, 1, source.Length - 1);
				Assert.Null(list.ItemContainerGenerator.ContainerFromIndex(199));
				list.SelectedIndex = 199;
				Assert.Null(list.ItemContainerGenerator.ContainerFromIndex(199));
				var selected = collection.SelectedItem;
				var changes = 0;
				collection.SelectionChanged += (_, _) => changes++;
				collection.ItemTemplate = new DataTemplate(() =>
				{
					var label = new Label { AutomationId = "Replacement" };
					label.SetBinding(Label.TextProperty, ".");
					return label;
				});
				Layout();
				Assert.Same(source, list.ItemsSource);
				Assert.Equal(199, list.SelectedIndex);
				Assert.Same(selected, collection.SelectedItem);
				Assert.Equal(0, changes);
				Assert.Null(list.ItemContainerGenerator.ContainerFromIndex(199));
				Assert.All(old, view => Assert.Null(view.Parent));
				var beforeScroll = ((IVisualTreeElement)collection).GetVisualChildren().Cast<Label>().ToArray();
				var retiredHandlers = beforeScroll.Select(view => Assert.IsType<DisconnectTrackingLabelHandler>(view.Handler)).ToArray();

				list.ScrollIntoView(source[199]);
				Layout();
				Assert.True(GetContainer(list, 199).IsSelected);
				Assert.All(beforeScroll, view => Assert.Null(view.Parent));
				Assert.All(retiredHandlers, item => Assert.Equal(1, item.DisconnectCount));
				var current = ((IVisualTreeElement)collection).GetVisualChildren().Cast<Label>().ToArray();
				Assert.InRange(current.Length, 1, source.Length - 1);
				Assert.Contains(current, view => view.Text == "Item 199" && view.AutomationId == "Replacement");
			}
			finally
			{
				window.Content = null;
				window.Close();
			}
		});
	}

	sealed class UnsupportedView : View;

	[Theory]
	[InlineData(false, "clear")]
	[InlineData(false, "reuse")]
	[InlineData(false, "template")]
	[InlineData(false, "disconnect")]
	[InlineData(true, "clear")]
	[InlineData(true, "reuse")]
	[InlineData(true, "template")]
	[InlineData(true, "disconnect")]
	public void RetiredTemplates_DisconnectRootAndNestedHandlers(bool nested, string retirement)
	{
		Run((collection, handler, list, created) =>
		{
			var leaves = new List<Label>();
			View? root = null;
			collection.ItemTemplate = new DataTemplate(() =>
			{
				var first = new Label { Text = "First" };
				leaves.Add(first);
				if (!nested)
					return root = first;
				var second = new Label { Text = "Nested" };
				leaves.Add(second);
				return root = new VerticalStackLayout { first, new ContentView { Content = second } };
			});
			Realize(list);
			var retiredRoot = root!;
			var retired = leaves.Select(view => Assert.IsType<DisconnectTrackingLabelHandler>(view.Handler)).ToArray();
			Assert.All(retired, item => Assert.Equal(0, item.DisconnectCount));
			var container = GetContainer(list, 0);
			switch (retirement)
			{
				case "clear": Clear(list, container, list.Items[0]); break;
				case "reuse": Prepare(list, container, "Reused"); break;
				case "template":
					collection.ItemTemplate = new DataTemplate(() => new Label { Text = "Replacement" });
					Realize(list);
					break;
				case "disconnect": ((IElementHandler)handler).DisconnectHandler(); break;
				default: throw new ArgumentOutOfRangeException(nameof(retirement));
			}
			Assert.Null(retiredRoot.Parent);
			Assert.All(retired, item => Assert.Equal(1, item.DisconnectCount));
			((IElementHandler)handler).DisconnectHandler();
			Assert.All(retired, item => Assert.Equal(1, item.DisconnectCount));
		});
	}

	[Fact]
	public void FailedNestedTemplate_DisconnectsAlreadyCreatedChildHandler()
	{
		Run((collection, handler, list, created) =>
		{
			DisconnectTrackingLabelHandler? childHandler = null;
			var child = new Label { Text = "Created before failure" };
			child.HandlerChanged += (_, _) =>
			{
				if (child.Handler is DisconnectTrackingLabelHandler tracking)
					childHandler = tracking;
			};
			var root = new FailingLayout { child };
			collection.ItemTemplate = new DataTemplate(() => root);
			Realize(list);
			Assert.NotNull(childHandler);
			Assert.Null(root.Parent);
			Assert.Equal(1, childHandler.DisconnectCount);
			((IElementHandler)handler).DisconnectHandler();
			Assert.Equal(1, childHandler.DisconnectCount);
		});
	}

	[Fact]
	public void GroupedSingleTemplateChange_RestoresNativeSelectionWhenVirtualSelectionIsData()
	{
		Run((collection, handler, list, created) =>
		{
			collection.SelectionMode = SelectionMode.Single;
			collection.IsGrouped = true;
			collection.GroupHeaderTemplate = new DataTemplate(() => new Label { Text = "Header" });
			collection.ItemsSource = new[] { new[] { "Same item", "Same item" } };
			Realize(list);
			list.SelectedIndex = 2;
			var selectedWrapper = list.SelectedItem;
			collection.SelectedItem = "Same item";
			Assert.Same(selectedWrapper, list.SelectedItem);
			var selectionChanges = 0;
			collection.SelectionChanged += (_, _) => selectionChanges++;

			collection.ItemTemplate = new DataTemplate(() => new Label { Text = "Replacement" });
			Realize(list);
			Assert.Same(selectedWrapper, list.SelectedItem);
			Assert.Equal("Same item", collection.SelectedItem);
			Assert.True(GetContainer(list, 2).IsSelected);
			Assert.Equal(0, selectionChanges);
		}, () => new DataSelectionHandler());
	}

	// Simulate data-normalized selection without changing this PR's public selection mapper.
	sealed class DataSelectionHandler : CollectionViewHandler
	{
		public override void UpdateValue(string property)
		{
			if (property != nameof(CollectionView.SelectedItem))
				base.UpdateValue(property);
		}
	}

	public sealed class DisconnectTrackingLabelHandler : LabelHandler
	{
		public int DisconnectCount { get; private set; }

		protected override void DisconnectHandler(System.Windows.Controls.TextBlock platformView)
		{
			DisconnectCount++;
			base.DisconnectHandler(platformView);
		}
	}

	public sealed class FailingLayout : VerticalStackLayout;

	public sealed class FailingLayoutHandler : LayoutHandler
	{
		public override void SetVirtualView(IView view)
		{
			base.SetVirtualView(view);
			throw new InvalidOperationException("Test failure after creating native child handlers.");
		}
	}

	static void Realize(MauiCollectionListBox list)
	{
		list.ApplyTemplate();
		list.Measure(new System.Windows.Size(400, 300));
		list.Arrange(new System.Windows.Rect(0, 0, 400, 300));
		list.UpdateLayout();
	}

	static WListBoxItem GetContainer(MauiCollectionListBox list, int index)
	{
		var container = list.ItemContainerGenerator.ContainerFromIndex(index);
		Assert.NotNull(container);
		Assert.Equal(typeof(MauiCollectionListBox).Assembly.GetType(
			"Microsoft.Maui.Handlers.WPF.MauiCollectionListBoxItem", throwOnError: true), container.GetType());
		return (WListBoxItem)container;
	}

	static void Run(Action<CollectionView, CollectionViewHandler, MauiCollectionListBox, List<Label>> action,
		Func<CollectionViewHandler>? createHandler = null)
	{
		RunOnSta(() =>
		{
			_ = System.Windows.Threading.Dispatcher.CurrentDispatcher;
			DispatcherProvider.SetCurrent(new WPFDispatcherProvider());
			using var app = MauiApp.CreateBuilder().UseMauiAppWPF<Application>()
				.ConfigureMauiHandlers(handlers => handlers
					.AddHandler<Label, DisconnectTrackingLabelHandler>()
					.AddHandler<FailingLayout, FailingLayoutHandler>())
				.Build();
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
			var handler = createHandler?.Invoke() ?? new CollectionViewHandler();
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
