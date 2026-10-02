using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Windows.WPF;
using WBorder = System.Windows.Controls.Border;
using WGrid = System.Windows.Controls.Grid;
using WListBox = System.Windows.Controls.ListBox;
using WScrollViewer = System.Windows.Controls.ScrollViewer;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class CollectionViewHandler : WPFViewHandler<Microsoft.Maui.Controls.CollectionView, FrameworkElement>
	{
		MauiCollectionListBox? _listBox;
		WGrid? _rootGrid;
		TextBlock? _emptyView;
		INotifyCollectionChanged? _observedSource;
		readonly HashSet<INotifyCollectionChanged> _observedGroups = new(ReferenceEqualityComparer.Instance);
		int _sourceVersion;

		public static readonly PropertyMapper<Microsoft.Maui.Controls.CollectionView, CollectionViewHandler> Mapper =
			new(ViewMapper)
			{
				[nameof(Microsoft.Maui.Controls.ItemsView.ItemsSource)] = MapItemsSource,
				[nameof(Microsoft.Maui.Controls.CollectionView.IsGrouped)] = MapItemsSource,
				[nameof(Microsoft.Maui.Controls.SelectableItemsView.SelectedItem)] = MapSelectedItem,
				[nameof(Microsoft.Maui.Controls.SelectableItemsView.SelectionMode)] = MapSelectionMode,
				[nameof(Microsoft.Maui.Controls.ItemsView.EmptyView)] = MapEmptyView,
				[nameof(Microsoft.Maui.Controls.StructuredItemsView.ItemsLayout)] = MapItemsLayout,
			};

		public static readonly CommandMapper<Microsoft.Maui.Controls.CollectionView, CollectionViewHandler> CommandMapper = new(ViewCommandMapper);

		public CollectionViewHandler() : base(Mapper, CommandMapper) { }

		protected override FrameworkElement CreatePlatformView()
		{
			_rootGrid = new WGrid();
			_listBox = new MauiCollectionListBox
			{
				HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
				BorderThickness = new System.Windows.Thickness(0),
				Background = System.Windows.Media.Brushes.Transparent,
			};
			ScrollViewer.SetVerticalScrollBarVisibility(_listBox, System.Windows.Controls.ScrollBarVisibility.Auto);
			ScrollViewer.SetHorizontalScrollBarVisibility(_listBox, System.Windows.Controls.ScrollBarVisibility.Disabled);

			_listBox.SelectionChanged += OnSelectionChanged;

			_emptyView = new TextBlock
			{
				Text = "No items",
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				VerticalAlignment = System.Windows.VerticalAlignment.Center,
				Foreground = System.Windows.Media.Brushes.Gray,
				FontSize = 16,
				Visibility = System.Windows.Visibility.Collapsed,
			};

			_rootGrid.Children.Add(_listBox);
			_rootGrid.Children.Add(_emptyView);

			return _rootGrid;
		}

		bool _processingSelection;

		void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
		{
			if (VirtualView == null || _listBox == null || _processingSelection) return;

			// Fire TapGesture for newly selected items (enables UIAutomation navigation)
			foreach (var added in e.AddedItems)
			{
				if (_listBox.ItemMauiViews.TryGetValue(added, out var mauiView))
					FireTapGestures(mauiView);
			}

			if (VirtualView.SelectionMode == Microsoft.Maui.Controls.SelectionMode.None)
			{
				_processingSelection = true;
				_listBox.UnselectAll();
				_processingSelection = false;
				return;
			}

			if (VirtualView.SelectionMode == Microsoft.Maui.Controls.SelectionMode.Single)
			{
				VirtualView.SelectedItem = _listBox.SelectedItem;
			}
			else if (VirtualView.SelectionMode == Microsoft.Maui.Controls.SelectionMode.Multiple)
			{
				var selected = new List<object>();
				foreach (var item in _listBox.SelectedItems)
					selected.Add(item);
				VirtualView.SelectedItems = selected;
			}
		}

		static void FireTapGestures(View mauiView)
		{
			foreach (var recognizer in mauiView.GestureRecognizers)
			{
				if (recognizer is not Microsoft.Maui.Controls.TapGestureRecognizer tap) continue;

				if (tap.Command != null && tap.Command.CanExecute(tap.CommandParameter))
				{
					tap.Command.Execute(tap.CommandParameter);
					return;
				}

				var sendTapped = typeof(Microsoft.Maui.Controls.TapGestureRecognizer).GetMethod(
					"SendTapped", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
				if (sendTapped != null)
				{
					var ps = sendTapped.GetParameters();
					if (ps.Length == 1) sendTapped.Invoke(tap, new object?[] { mauiView });
					else if (ps.Length == 2) sendTapped.Invoke(tap, new object?[] { mauiView, null });
				}
				return;
			}

			// Check child layout views for gestures
			if (mauiView is Microsoft.Maui.Controls.Layout layout)
			{
				foreach (var child in layout)
				{
					if (child is View childView)
					{
						FireTapGestures(childView);
						return;
					}
				}
			}
		}

		static void MapItemsSource(CollectionViewHandler handler, Microsoft.Maui.Controls.CollectionView view)
		{
			if (handler._listBox == null) return;

			handler.UnsubscribeCollections();
			handler._observedSource = view.ItemsSource as INotifyCollectionChanged;
			if (handler._observedSource != null)
				handler._observedSource.CollectionChanged += handler.OnSourceCollectionChanged;
			handler.UpdateGroupSubscriptions();
			handler.PopulateItems(view);
		}

		void UnsubscribeCollections()
		{
			_sourceVersion++;
			if (_observedSource != null)
				_observedSource.CollectionChanged -= OnSourceCollectionChanged;
			_observedSource = null;
			foreach (var group in _observedGroups)
				group.CollectionChanged -= OnGroupCollectionChanged;
			_observedGroups.Clear();
		}

		void UpdateGroupSubscriptions()
		{
			var groups = new HashSet<INotifyCollectionChanged>(ReferenceEqualityComparer.Instance);
			if (VirtualView.IsGrouped && VirtualView.ItemsSource is IEnumerable source)
			{
				foreach (var group in source)
				{
					if (group is INotifyCollectionChanged observable && !ReferenceEquals(observable, _observedSource))
						groups.Add(observable);
				}
			}

			foreach (var group in _observedGroups)
			{
				if (!groups.Contains(group))
					group.CollectionChanged -= OnGroupCollectionChanged;
			}
			foreach (var group in groups)
			{
				if (!_observedGroups.Contains(group))
					group.CollectionChanged += OnGroupCollectionChanged;
			}
			_observedGroups.Clear();
			_observedGroups.UnionWith(groups);
		}

		void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
			=> OnCollectionChanged(sender, isGroup: false);

		void OnGroupCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
			=> OnCollectionChanged(sender, isGroup: true);

		void OnCollectionChanged(object? sender, bool isGroup)
		{
			if (_listBox == null) return;
			var version = _sourceVersion;
			void Update()
			{
				// A queued notification must not revive an old source after replacement or disconnect.
				if (version != _sourceVersion || ((IElementHandler)this).VirtualView == null ||
					(isGroup ? sender is not INotifyCollectionChanged group || !_observedGroups.Contains(group)
						: !ReferenceEquals(sender, _observedSource)))
					return;

				if (VirtualView.IsGrouped)
				{
					if (!isGroup)
						UpdateGroupSubscriptions();
					PopulateItems(VirtualView);
				}
				else
				{
					// WPF tracks flat items itself; only visibility needs updating.
					UpdateEmptyView();
				}
			}

			if (_listBox.Dispatcher.CheckAccess())
				Update();
			else
				_listBox.Dispatcher.InvokeAsync(Update);
		}

		void PopulateItems(Microsoft.Maui.Controls.CollectionView view)
		{
			if (_listBox == null) return;

			_listBox.MauiTemplate = view.ItemTemplate;
			_listBox.MauiGroupHeaderTemplate = view.GroupHeaderTemplate;
			_listBox.MauiGroupFooterTemplate = view.GroupFooterTemplate;
			_listBox.IsGrouped = view.IsGrouped;
			_listBox.MauiCollectionView = view;

			if (view.IsGrouped && view.ItemsSource is IEnumerable source)
			{
				// Flatten groups into a single list with header/footer markers
				var flat = new List<GroupedItem>();
				foreach (var group in source)
				{
					flat.Add(new GroupedItem(group, GroupedItemKind.Header));
					if (group is IEnumerable children)
					{
						foreach (var child in children)
							flat.Add(new GroupedItem(child, GroupedItemKind.Item));
					}
				}
				_listBox.ClearItemViews();
				_listBox.ItemsSource = flat;
			}
			else
			{
				if (!ReferenceEquals(_listBox.ItemsSource, view.ItemsSource))
					_listBox.ClearItemViews();
				_listBox.ItemsSource = view.ItemsSource as IEnumerable;
			}

			UpdateEmptyView();
		}

		void UpdateEmptyView()
		{
			if (_listBox == null || _emptyView == null || VirtualView == null) return;

			bool hasItems = false;
			if (_listBox.ItemsSource is IEnumerable items)
			{
				var enumerator = items.GetEnumerator();
				hasItems = enumerator.MoveNext();
				(enumerator as IDisposable)?.Dispose();
			}

			_emptyView.Visibility = hasItems ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
			_listBox.Visibility = hasItems ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

			if (!hasItems && VirtualView.EmptyView is string s)
				_emptyView.Text = s;
			else if (!hasItems && VirtualView.EmptyView is View emptyMauiView)
				_emptyView.Text = emptyMauiView.ToString() ?? "No items";
		}

		static void MapSelectedItem(CollectionViewHandler handler, Microsoft.Maui.Controls.CollectionView view)
		{
			if (handler._listBox == null) return;
			if (handler._listBox.SelectedItem != view.SelectedItem)
				handler._listBox.SelectedItem = view.SelectedItem;
		}

		static void MapSelectionMode(CollectionViewHandler handler, Microsoft.Maui.Controls.CollectionView view)
		{
			if (handler._listBox == null) return;
			handler._listBox.SelectionMode = view.SelectionMode switch
			{
				Microsoft.Maui.Controls.SelectionMode.None => System.Windows.Controls.SelectionMode.Single,
				Microsoft.Maui.Controls.SelectionMode.Single => System.Windows.Controls.SelectionMode.Single,
				Microsoft.Maui.Controls.SelectionMode.Multiple => System.Windows.Controls.SelectionMode.Extended,
				_ => System.Windows.Controls.SelectionMode.Single,
			};
		}

		static void MapEmptyView(CollectionViewHandler handler, Microsoft.Maui.Controls.CollectionView view)
		{
			handler.UpdateEmptyView();
		}

		static void MapItemsLayout(CollectionViewHandler handler, Microsoft.Maui.Controls.CollectionView view)
		{
			if (handler._listBox == null) return;

			if (view.ItemsLayout is Microsoft.Maui.Controls.GridItemsLayout gridLayout)
			{
				var span = Math.Max(1, gridLayout.Span);
				var hSpacing = gridLayout.HorizontalItemSpacing;
				var vSpacing = gridLayout.VerticalItemSpacing;

				var wrapPanel = new System.Windows.Controls.WrapPanel
				{
					Orientation = System.Windows.Controls.Orientation.Horizontal,
				};

				handler._listBox.ItemsPanel = new System.Windows.Controls.ItemsPanelTemplate(
					new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.WrapPanel)));
				handler._listBox.Tag = span; // Store span for item width calculation

				// Re-apply items to pick up new template
				handler._listBox.SizeChanged -= handler.OnListBoxSizeChanged;
				handler._listBox.SizeChanged += handler.OnListBoxSizeChanged;

				// Force re-render of items
				MapItemsSource(handler, view);
			}
			else
			{
				// Default: vertical stack
				handler._listBox.ItemsPanel = new System.Windows.Controls.ItemsPanelTemplate(
					new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.VirtualizingStackPanel)));
				handler._listBox.Tag = null;
				handler._listBox.SizeChanged -= handler.OnListBoxSizeChanged;
				MapItemsSource(handler, view);
			}
		}

		void OnListBoxSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
		{
			if (_listBox?.Tag is int span && span > 0)
			{
				var totalWidth = e.NewSize.Width;
				var itemWidth = (totalWidth - 8) / span; // rough spacing
				foreach (var item in _listBox.Items)
				{
					var container = _listBox.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
					if (container != null)
						container.Width = Math.Max(50, itemWidth);
				}
			}
		}

		Action<ApplicationModel.AppTheme>? _themeChangedHandler;

		protected override void ConnectHandler(FrameworkElement platformView)
		{
			base.ConnectHandler(platformView);
			_themeChangedHandler = _ => PlatformView?.Dispatcher.InvokeAsync(RefreshItems);
			ThemeManager.ThemeChanged += _themeChangedHandler;
		}

		protected override void DisconnectHandler(FrameworkElement platformView)
		{
			UnsubscribeCollections();
			if (_themeChangedHandler != null)
				ThemeManager.ThemeChanged -= _themeChangedHandler;
			if (_listBox != null)
			{
				_listBox.SelectionChanged -= OnSelectionChanged;
				_listBox.SizeChanged -= OnListBoxSizeChanged;
				_listBox.ClearItemViews();
			}
			base.DisconnectHandler(platformView);
		}

		/// <summary>
		/// Force re-materialization of all items by resetting ItemsSource.
		/// This causes PrepareContainerForItemOverride to run again with fresh MAUI views
		/// that pick up the new AppThemeBinding values.
		/// </summary>
		void RefreshItems()
		{
			if (_listBox == null || VirtualView == null) return;
			var source = _listBox.ItemsSource;
			if (source == null) return;
			_listBox.ItemsSource = null;
			_listBox.ItemsSource = source;
		}
	}

	enum GroupedItemKind { Header, Item, Footer }

	class GroupedItem
	{
		public object Data { get; }
		public GroupedItemKind Kind { get; }
		public GroupedItem(object data, GroupedItemKind kind) { Data = data; Kind = kind; }
	}

	/// <summary>
	/// Custom ListBox that creates MAUI-templated items via PrepareContainerForItemOverride.
	/// </summary>
	public class MauiCollectionListBox : WListBox
	{
		internal Microsoft.Maui.Controls.DataTemplate? MauiTemplate { get; set; }
		internal Microsoft.Maui.Controls.DataTemplate? MauiGroupHeaderTemplate { get; set; }
		internal Microsoft.Maui.Controls.DataTemplate? MauiGroupFooterTemplate { get; set; }
		internal bool IsGrouped { get; set; }
		internal Microsoft.Maui.Controls.CollectionView? MauiCollectionView { get; set; }
		internal Dictionary<object, View> ItemMauiViews { get; } = new();

		protected override DependencyObject GetContainerForItemOverride() => new MauiCollectionListBoxItem();

		protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
			=> new MauiCollectionListBoxAutomationPeer(this);

		protected override void ClearContainerForItemOverride(DependencyObject element, object item)
		{
			if (element is MauiCollectionListBoxItem container)
				container.ClearAccessibility();
			base.ClearContainerForItemOverride(element, item);
		}

		internal void ClearItemViews()
		{
			var views = ItemMauiViews.Values.ToArray();
			ItemMauiViews.Clear();
			foreach (var view in views)
				view.DisconnectHandlers();
		}

		protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
		{
			if (element is MauiCollectionListBoxItem container)
				container.PrepareAccessibility();
			base.PrepareContainerForItemOverride(element, item);

			if (element is not ListBoxItem lbi || MauiCollectionView == null) return;

			lbi.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
			lbi.Padding = new System.Windows.Thickness(0);

			// For GridItemsLayout, set item width based on span
			if (Tag is int span && span > 0 && ActualWidth > 0)
			{
				var itemWidth = (ActualWidth - 8) / span;
				lbi.Width = Math.Max(50, itemWidth);
			}

			try
			{
				object dataItem;
				Microsoft.Maui.Controls.DataTemplate? template;

				if (item is GroupedItem gi)
				{
					dataItem = gi.Data;
					template = gi.Kind == GroupedItemKind.Header ? MauiGroupHeaderTemplate
						: gi.Kind == GroupedItemKind.Footer ? MauiGroupFooterTemplate
						: MauiTemplate;

					if (gi.Kind != GroupedItemKind.Item)
					{
						lbi.IsHitTestVisible = true;
						lbi.Focusable = false;
					}
				}
				else
				{
					dataItem = item;
					template = MauiTemplate;
				}

				if (template == null)
				{
					lbi.Content = new TextBlock
					{
						Text = dataItem?.ToString() ?? "",
						Padding = new System.Windows.Thickness(8, 4, 8, 4),
					};
					return;
				}

				// Resolve DataTemplateSelector
				var resolvedTemplate = template;
				if (template is Microsoft.Maui.Controls.DataTemplateSelector selector)
					resolvedTemplate = selector.SelectTemplate(dataItem, MauiCollectionView);

				if (resolvedTemplate == null) return;

				var content = resolvedTemplate.CreateContent() as View;
				var mauiContext = MauiCollectionView.Handler?.MauiContext;
				if (content == null) return;

				content.BindingContext = dataItem;

				if (mauiContext == null) return;

				var platformView = Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)content, mauiContext);
				lbi.Content = platformView;
				if (lbi is MauiCollectionListBoxItem mauiContainer)
					mauiContainer.MauiItemView = content;

				// Store MAUI view reference so OnSelectionChanged can fire TapGesture
				ItemMauiViews[item] = content;
			}
			catch (Exception ex)
			{
				var dataText = (item is GroupedItem g ? g.Data : item)?.ToString() ?? "";
				lbi.Content = new TextBlock
				{
					Text = dataText,
					TextWrapping = TextWrapping.Wrap,
					Padding = new System.Windows.Thickness(8, 4, 8, 4),
				};
				System.Diagnostics.Debug.WriteLine($"[CollectionView] Template error for {dataText}: {ex.Message}");
			}
		}
	}
}
