using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

namespace Comet.Handlers
{
	public partial class CollectionViewHandler
	{
		public static readonly PropertyMapper<IListView, CollectionViewHandler> Mapper =
			new PropertyMapper<IListView, CollectionViewHandler>(ViewHandler.ViewMapper)
			{
				["ListView"] = MapListViewProperty,
			};

		public static readonly CommandMapper<IListView, CollectionViewHandler> ActionMapper =
			new CommandMapper<IListView, CollectionViewHandler>
			{
				[nameof(ListView.ReloadData)] = MapReloadData,
			};

		public CollectionViewHandler() : base(Mapper, ActionMapper) { }

		Microsoft.Maui.Controls.ItemsView _mauiItemsView;

		// Stable reference updated on each MapListViewProperty call so that
		// one-time callbacks (ItemTemplate, SelectionChanged) always resolve
		// to the *current* Comet view, even after body rebuilds.
		WeakReference<IListView> _currentListViewRef;

		/// <summary>
		/// Detects whether the Comet IListView is a CarouselView (generic or non-generic).
		/// </summary>
		static bool IsCarouselView(IListView listView)
		{
			var type = listView.GetType();
			while (type is not null)
			{
				if (type == typeof(Comet.CarouselView))
					return true;
				if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Comet.CarouselView<>))
					return true;
				type = type.BaseType;
			}
			return false;
		}

		/// <summary>
		/// Configuration for MAUI CarouselView: item template and carousel-specific properties.
		/// </summary>
		static void ConfigureMauiCarouselView(Microsoft.Maui.Controls.CarouselView carousel, IListView listView)
		{
			var loop = GetPropertyValue<bool>(listView, nameof(Comet.CarouselView.Loop));
			carousel.Loop = loop;

			var isBounceEnabled = GetPropertyValue<bool>(listView, nameof(Comet.CarouselView.IsBounceEnabled));
			carousel.IsBounceEnabled = isBounceEnabled;

			var isSwipeEnabled = GetPropertyValue<bool>(listView, nameof(Comet.CarouselView.IsSwipeEnabled));
			carousel.IsSwipeEnabled = isSwipeEnabled;

			var isScrollAnimated = GetPropertyValue<bool>(listView, nameof(Comet.CarouselView.IsScrollAnimated));
			carousel.IsScrollAnimated = isScrollAnimated;

			var peekAreaInsets = GetPropertyValue<double>(listView, nameof(Comet.CarouselView.PeekAreaInsets));
			if (peekAreaInsets > 0)
				carousel.PeekAreaInsets = new Microsoft.Maui.Thickness(peekAreaInsets);

			// Sync position from the Comet CarouselView to the MAUI CarouselView
			var positionProp = listView.GetType().GetProperty("Position");
			if (positionProp is not null)
			{
				var posValue = positionProp.GetValue(listView);
				if (posValue is Reactive.PropertySubscription<int> posSub)
					carousel.Position = posSub.CurrentValue;
				else if (posValue is int posInt)
					carousel.Position = posInt;
			}

			var listViewRef = new WeakReference<IListView>(listView);
			carousel.ItemTemplate = new Microsoft.Maui.Controls.DataTemplate(() =>
			{
				var container = new Microsoft.Maui.Controls.ContentView();
				container.BindingContextChanged += (s, e) =>
				{
					if (container.BindingContext is not CollectionViewItemProxy proxy)
						return;
					if (!listViewRef.TryGetTarget(out var currentListView))
						return;
					var cometView = currentListView.ViewFor(proxy.Section, proxy.Row);
					container.Content = cometView is not null ? new CometHost(cometView) : null;
				};
				return container;
			});

			carousel.PositionChanged += (s, e) =>
			{
				var callback = GetPropertyValue<Action<int>>(listView, "PositionChanged");
				callback?.Invoke(e.CurrentPosition);
			};
		}

		/// <summary>
		/// One-time setup for a new MAUI CollectionView: template + event handlers.
		/// These reference the handler's <see cref="_currentListViewRef"/> so they
		/// survive Comet body rebuilds without re-registration.
		/// </summary>
		void InitCollectionView(Microsoft.Maui.Controls.CollectionView cv)
		{
			cv.ItemTemplate = new Microsoft.Maui.Controls.DataTemplate(() =>
			{
				var container = new Microsoft.Maui.Controls.ContentView();
				container.BindingContextChanged += (s, e) =>
				{
					if (container.BindingContext is not CollectionViewItemProxy proxy)
						return;
					if (_currentListViewRef?.TryGetTarget(out var lv) != true)
						return;
					var cometView = lv.ViewFor(proxy.Section, proxy.Row);
					container.Content = cometView is not null ? new CometHost(cometView) : null;
				};
				return container;
			});

			cv.SelectionChanged += (s, e) =>
			{
				if (_currentListViewRef?.TryGetTarget(out var lv) != true)
					return;

				var current = e.CurrentSelection;
				if (current is null || current.Count == 0)
					return;

				// Find the most recently added item for accurate multi-select reporting
				var previous = e.PreviousSelection;
				object target = current[current.Count - 1];
				if (previous is not null)
				{
					foreach (var item in current)
					{
						if (!previous.Contains(item))
						{
							target = item;
							break;
						}
					}
				}

				if (target is CollectionViewItemProxy proxy)
					lv.OnSelected(proxy.Section, proxy.Row);
			};
		}

		/// <summary>
		/// Updates mutable properties on an existing MAUI CollectionView from
		/// the current Comet view (selection mode, scroll-to, items, etc.).
		/// </summary>
		static void UpdateCollectionView(Microsoft.Maui.Controls.CollectionView cv, IListView listView)
		{
			MapCometSelectionMode(cv, listView);
			MapCometEmptyView(cv, listView);
			MapCometHeaderFooter(cv, listView);
			MapCometScrollTo(cv, listView);
			RefreshItemsSource(cv, listView);
		}

		/// <summary>
		/// Rebuilds the items list from IListView sections/rows.
		/// </summary>
		static void RefreshItemsSource(Microsoft.Maui.Controls.ItemsView itemsView, IListView listView)
		{
			var items = new List<CollectionViewItemProxy>();
			var sections = listView.Sections();
			for (int s = 0; s < sections; s++)
			{
				var rows = listView.Rows(s);
				for (int r = 0; r < rows; r++)
					items.Add(new CollectionViewItemProxy(s, r));
			}
			itemsView.ItemsSource = items;
		}

		static void MapCometItemsLayout(Microsoft.Maui.Controls.CollectionView cv, IListView listView)
		{
			var cometLayout = GetPropertyValue<Comet.ItemsLayout>(listView, nameof(Comet.CollectionView.ItemsLayout));
			if (cometLayout is null)
				return;

			if (cometLayout is Comet.GridItemsLayout gridLayout)
			{
				var orientation = gridLayout.Orientation == ItemsLayoutOrientation.Vertical
					? Microsoft.Maui.Controls.ItemsLayoutOrientation.Vertical
					: Microsoft.Maui.Controls.ItemsLayoutOrientation.Horizontal;

				cv.ItemsLayout = new Microsoft.Maui.Controls.GridItemsLayout(gridLayout.Span, orientation)
				{
					HorizontalItemSpacing = gridLayout.ItemSpacing,
					VerticalItemSpacing = gridLayout.ItemSpacing
				};
			}
			else
			{
				var orientation = cometLayout.Orientation == ItemsLayoutOrientation.Vertical
					? Microsoft.Maui.Controls.ItemsLayoutOrientation.Vertical
					: Microsoft.Maui.Controls.ItemsLayoutOrientation.Horizontal;

				cv.ItemsLayout = new Microsoft.Maui.Controls.LinearItemsLayout(orientation)
				{
					ItemSpacing = cometLayout.ItemSpacing
				};
			}
		}

		static void MapCometSelectionMode(Microsoft.Maui.Controls.CollectionView cv, IListView listView)
		{
			var mode = GetPropertyValue<Comet.SelectionMode?>(listView, nameof(Comet.CollectionView.SelectionMode));

			cv.SelectionMode = mode switch
			{
				Comet.SelectionMode.None => Microsoft.Maui.Controls.SelectionMode.None,
				Comet.SelectionMode.Multiple => Microsoft.Maui.Controls.SelectionMode.Multiple,
				_ => Microsoft.Maui.Controls.SelectionMode.Single,
			};
		}

		static void MapCometEmptyView(Microsoft.Maui.Controls.CollectionView cv, IListView listView)
		{
			var emptyView = GetPropertyValue<Comet.View>(listView, nameof(Comet.CollectionView.EmptyView));
			cv.EmptyView = emptyView is not null ? new CometHost(emptyView) : null;
		}

		static void MapCometHeaderFooter(Microsoft.Maui.Controls.CollectionView cv, IListView listView)
		{
			var header = listView.HeaderView();
			cv.Header = header is not null ? new CometHost(header) : null;

			var footer = listView.FooterView();
			cv.Footer = footer is not null ? new CometHost(footer) : null;
		}

		/// <summary>
		/// Gets a property value from a view by name, supporting both generic and non-generic CollectionView types.
		/// </summary>
		static T GetPropertyValue<T>(IListView listView, string propertyName)
		{
			var prop = listView.GetType().GetProperty(propertyName);
			if (prop is not null)
			{
				var value = prop.GetValue(listView);
				if (value is T typed)
					return typed;
			}
			return default;
		}

		static void MapCometInfiniteScroll(Microsoft.Maui.Controls.CollectionView cv, IListView listView)
		{
			var threshold = GetPropertyValue<int>(listView, nameof(Comet.CollectionView.RemainingItemsThreshold));
			if (threshold <= 0)
				return;

			cv.RemainingItemsThreshold = threshold;

			var action = GetPropertyValue<Action>(listView, nameof(Comet.CollectionView.RemainingItemsThresholdReached));
			var command = GetPropertyValue<Action<int>>(listView, nameof(Comet.CollectionView.RemainingItemsThresholdReachedCommand));

			cv.RemainingItemsThresholdReachedCommand = new Microsoft.Maui.Controls.Command((param) =>
			{
				action?.Invoke();
				if (param is int index)
					command?.Invoke(index);
			});
		}

		static void MapCometScrollTo(Microsoft.Maui.Controls.CollectionView cv, IListView listView)
		{
			var scrollToProp = listView.GetType().GetProperty("ScrollToRequested");
			if (scrollToProp is not null)
			{
				scrollToProp.SetValue(listView, (Action<int, bool>)((index, animate) =>
				{
					var totalItems = cv.ItemsSource is System.Collections.ICollection c ? c.Count : 0;
					// Use End position for the last item so it scrolls fully into view
					var position = (totalItems > 0 && index >= totalItems - 1)
						? Microsoft.Maui.Controls.ScrollToPosition.End
						: Microsoft.Maui.Controls.ScrollToPosition.MakeVisible;
					cv.ScrollTo(index, position: position, animate: animate);
				}));
			}
		}
	}

	/// <summary>
	/// Lightweight proxy that holds section/row indices for DataTemplate binding.
	/// </summary>
	sealed class CollectionViewItemProxy
	{
		public int Section { get; }
		public int Row { get; }

		public CollectionViewItemProxy(int section, int row)
		{
			Section = section;
			Row = row;
		}
	}
}
