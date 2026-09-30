using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Handlers.WPF;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.WPF;
using WGrid = System.Windows.Controls.Grid;
using WTextBlock = System.Windows.Controls.TextBlock;
using Visibility = System.Windows.Visibility;

namespace HandlerTests;

public class CollectionViewObservableSourceTests
{
	[Fact]
	public void FlatSource_AddRemoveAndReset_UpdateEmptyView()
	{
		Run(view =>
		{
			var items = new ObservableCollection<string>();
			view.ItemsSource = items;
			return (handler, list, empty) =>
			{
				Assert.Equal(Visibility.Visible, empty.Visibility);
				items.Add("Hello");
				Assert.Equal("Hello", Assert.Single(list.Items.Cast<object>()));
				Assert.Equal(Visibility.Visible, list.Visibility);
				Assert.Equal(Visibility.Collapsed, empty.Visibility);
				items.RemoveAt(0);
				Assert.Equal(Visibility.Visible, empty.Visibility);
				items.Add("Again");
				items.Clear();
				Assert.Equal(Visibility.Collapsed, list.Visibility);
				Assert.Equal(Visibility.Visible, empty.Visibility);
			};
		});
	}

	[Fact]
	public void GroupedSource_OuterAndInnerChanges_RebuildItems()
	{
		Run(view =>
		{
			var first = new ObservableCollection<string>();
			var groups = new ObservableCollection<ObservableCollection<string>>();
			view.IsGrouped = true;
			view.ItemsSource = groups;
			return (handler, list, empty) =>
			{
				groups.Add(first);
				Assert.Single(list.Items.Cast<object>()); // Group header.
				first.Add("Hello");
				Assert.Equal(2, list.Items.Count);
				first[0] = "Replaced";
				Assert.Equal("Replaced", GroupData(list.Items[1]));
				first.Add("Second");
				first.Move(1, 0);
				Assert.Equal("Second", GroupData(list.Items[1]));
				first.RemoveAt(1);
				Assert.Equal(2, list.Items.Count);
				first.Clear();
				Assert.Single(list.Items.Cast<object>());
				var second = new ObservableCollection<string> { "New group" };
				groups[0] = second;
				Assert.Equal("New group", GroupData(list.Items[1]));
				first.Add("Detached");
				Assert.Equal(2, list.Items.Count);
				groups.Clear();
				Assert.Empty(list.Items);
				Assert.Equal(Visibility.Visible, empty.Visibility);
			};
		});
	}

	[Fact]
	public void SourceReplacementAndDisconnect_RemoveSubscriptions()
	{
		Run(view =>
		{
			var group = new TrackedCollection<string>();
			var groups = new TrackedCollection<TrackedCollection<string>> { group, group };
			view.IsGrouped = true;
			view.ItemsSource = groups;
			return (handler, list, empty) =>
			{
				Assert.Equal(1, groups.Subscribers);
				Assert.Equal(1, group.Subscribers);
				handler.UpdateValue(nameof(ItemsView.ItemsSource));
				Assert.Equal(1, group.Subscribers);
				groups.RemoveAt(0);
				Assert.Equal(1, group.Subscribers);
				groups.Clear();
				Assert.Equal(0, group.Subscribers);
				groups.Add(group);
				var replacement = new TrackedCollection<TrackedCollection<string>> { group };
				view.ItemsSource = replacement;
				Assert.Equal(0, groups.Subscribers);
				Assert.Equal(1, replacement.Subscribers);
				Assert.Equal(1, group.Subscribers);
				view.ItemsSource = null;
				Assert.Equal(0, replacement.Subscribers);
				Assert.Equal(0, group.Subscribers);
				Assert.Empty(list.Items);
				Assert.Equal(Visibility.Visible, empty.Visibility);
				view.ItemsSource = replacement;
				Task.Run(() => group.Add("Queued before disconnect")).GetAwaiter().GetResult();
				((IElementHandler)handler).DisconnectHandler();
				list.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
				Assert.Equal(0, replacement.Subscribers);
				Assert.Equal(0, group.Subscribers);
			};
		});
	}

	[Fact]
	public void IsGroupedChange_UpdatesSubscriptionsAndRepresentation()
	{
		Run(view =>
		{
			var group = new TrackedCollection<string> { "Hello" };
			view.ItemsSource = new[] { group };
			return (handler, list, empty) =>
			{
				Assert.Same(group, list.Items[0]);
				view.IsGrouped = true;
				Assert.Equal(1, group.Subscribers);
				Assert.Equal(2, list.Items.Count);
				view.IsGrouped = false;
				Assert.Equal(0, group.Subscribers);
				Assert.Same(group, Assert.Single(list.Items.Cast<object>()));
			};
		});
	}

	static object? GroupData(object item) => item.GetType().GetProperty("Data")!.GetValue(item);

	[Fact]
	public void QueuedGroupChange_AfterSourceReplacement_DoesNotRebuildNewSource()
	{
		Run(view =>
		{
			var group = new ObservableCollection<string>();
			view.IsGrouped = true;
			view.ItemsSource = new[] { group };
			return (handler, list, empty) =>
			{
				Task.Run(() => group.Add("Queued")).GetAwaiter().GetResult();
				view.ItemsSource = new[] { new[] { "Replacement" } };
				var snapshot = list.ItemsSource;
				list.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
				Assert.Same(snapshot, list.ItemsSource);
				Assert.Equal("Replacement", GroupData(list.Items[1]));
			};
		});
	}

	[Fact]
	public void QueuedGroupChange_DispatchesToUiThread()
	{
		Run(view =>
		{
			var group = new ObservableCollection<string>();
			view.IsGrouped = true;
			view.ItemsSource = new[] { group };
			return (handler, list, empty) =>
			{
				Task.Run(() => group.Add("Queued")).GetAwaiter().GetResult();
				list.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
				Assert.Equal(2, list.Items.Count);
				Assert.Equal("Queued", GroupData(list.Items[1]));
			};
		});
	}

	static void Run(Func<CollectionView, Action<CollectionViewHandler, MauiCollectionListBox, WTextBlock>> setup)
	{
		var completed = false;
		StaHelper.RunOnSta(() =>
		{
			using var app = MauiApp.CreateBuilder().UseMauiAppWPF<Application>().Build();
			var view = new CollectionView { EmptyView = "Waiting for items" };
			var test = setup(view);
			var handler = new CollectionViewHandler();
			handler.SetMauiContext(new WPFMauiContext(app.Services));
			try
			{
				handler.SetVirtualView(view);
				var root = Assert.IsType<WGrid>(handler.PlatformView);
				test(handler, Assert.IsType<MauiCollectionListBox>(root.Children[0]),
					Assert.IsType<WTextBlock>(root.Children[1]));
				completed = true;
			}
			finally
			{
				if (((IElementHandler)handler).VirtualView != null)
					((IElementHandler)handler).DisconnectHandler();
			}
		});
		Assert.True(completed, "The STA test did not complete within its timeout.");
	}

	sealed class TrackedCollection<T> : ObservableCollection<T>
	{
		public int Subscribers { get; private set; }

		public override event NotifyCollectionChangedEventHandler? CollectionChanged
		{
			add { base.CollectionChanged += value; Subscribers++; }
			remove { base.CollectionChanged -= value; Subscribers--; }
		}
	}
}
