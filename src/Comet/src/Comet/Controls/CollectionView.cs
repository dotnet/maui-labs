using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Comet.Internal;
using Comet.Reactive;
using Microsoft.Maui;

namespace Comet
{
	public enum ItemsLayoutOrientation
	{
		Vertical,
		Horizontal
	}

	public enum ItemSizingStrategy
	{
		MeasureAllItems,
		MeasureFirstItem
	}

	public class ItemsLayout
	{
		public ItemsLayoutOrientation Orientation { get; set; }
		public double ItemSpacing { get; set; }

		public static ItemsLayout Vertical(double spacing = 0) =>
			new ItemsLayout { Orientation = ItemsLayoutOrientation.Vertical, ItemSpacing = spacing };

		public static ItemsLayout Horizontal(double spacing = 0) =>
			new ItemsLayout { Orientation = ItemsLayoutOrientation.Horizontal, ItemSpacing = spacing };
	}

	public class GridItemsLayout : ItemsLayout
	{
		public int Span { get; set; } = 1;

		public static GridItemsLayout Vertical(int span, double spacing = 0) =>
			new GridItemsLayout { Orientation = ItemsLayoutOrientation.Vertical, Span = span, ItemSpacing = spacing };

		public static GridItemsLayout Horizontal(int span, double spacing = 0) =>
			new GridItemsLayout { Orientation = ItemsLayoutOrientation.Horizontal, Span = span, ItemSpacing = spacing };
	}

	/// <summary>
	/// Generic CollectionView that inherits from CollectionView (not ListView&lt;T&gt;) so that
	/// handler resolution finds CollectionViewHandler instead of ListViewHandler.
	/// Replicates ListView&lt;T&gt; generic item machinery.
	/// </summary>
	public class CollectionView<T> : CollectionView
	{
		protected IDictionary<(int section, int row, object item), View> CurrentViews { get; }

		PropertySubscription<IReadOnlyList<T>> _items;
		PropertySubscription<IReadOnlyList<T>> Items
		{
			get => _items;
			set => this.SetPropertySubscription(ref _items, value);
		}

		IReadOnlyList<T> currentItems;

		public CollectionView(Func<IReadOnlyList<T>> items) : this()
		{
			Items = PropertySubscription<IReadOnlyList<T>>.FromFunc(items);
			this.currentItems = Items?.CurrentValue;
			SetupObservable();
		}

		public CollectionView(PropertySubscription<IReadOnlyList<T>> items) : this()
		{
			Items = items;
			this.currentItems = Items?.CurrentValue;
			SetupObservable();
		}

		public CollectionView()
		{
			if (ListView.HandlerSupportsVirtualization)
			{
				CurrentViews = new FixedSizeDictionary<(int section, int row, object item), View>(150)
				{
					OnDequeue = (pair) =>
					{
						var view = pair.Value;
						if (view?.ViewHandler?.PlatformView is null)
							view.Dispose();
						else
							CurrentViews[pair.Key] = view;
					}
				};
			}
			else
				CurrentViews = new Dictionary<(int section, int row, object item), View>();

			ShouldDisposeViews = true;
		}

		public override void ViewPropertyChanged(string property, object value)
		{
			if (property == nameof(Items))
			{
				DisposeObservable();
				currentItems = Items?.CurrentValue;
				SetupObservable();
				ReloadData();
			}
			base.ViewPropertyChanged(property, value);
		}

		void SetupObservable()
		{
			if (!(currentItems is ObservableCollection<T> observable))
				return;
			observable.CollectionChanged += Observable_CollectionChanged;
		}

		protected virtual void Observable_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
		{
			ReloadData();
		}

		void DisposeObservable()
		{
			if (!(currentItems is ObservableCollection<T> observable))
				return;
			observable.CollectionChanged -= Observable_CollectionChanged;
		}

		public Func<T, View> ViewFor { get; set; }

		public Func<int, T> ItemFor { get; set; }

		public Func<int> Count { get; set; }

		protected override int GetCount(int section) => currentItems?.Count ?? Count?.Invoke() ?? 0;

		protected override object GetItemAt(int section, int index) => currentItems.SafeGetAtIndex(index, ItemFor);

		protected override View GetViewFor(int section, int index)
		{
			var item = (T)GetItemAt(section, index);
			if (item is null)
				return null;
			var key = (section, index, item);
			if (!CurrentViews.TryGetValue(key, out var view) || (view?.IsDisposed ?? true))
			{
				view = ViewFor?.Invoke(item);
				if (view is null)
					return null;
				CurrentViews[key] = view;
				view.Parent = this;
			}
			return view;
		}

		public override void Add(View view) => throw new NotSupportedException("You cannot add a View directly to a Typed CollectionView");

		PropertySubscription<T> _selectedItem;
		public PropertySubscription<T> SelectedItem
		{
			get => _selectedItem;
			set => this.SetPropertySubscription(ref _selectedItem, value);
		}

		PropertySubscription<IReadOnlyList<T>> _selectedItems;
		public PropertySubscription<IReadOnlyList<T>> SelectedItems
		{
			get => _selectedItems;
			set => this.SetPropertySubscription(ref _selectedItems, value);
		}

		public View GroupHeaderTemplate { get; set; }
		public View GroupFooterTemplate { get; set; }
		public bool IsGrouped { get; set; }

		public Func<T, View> GroupHeaderViewFor { get; set; }
		public Func<T, View> GroupFooterViewFor { get; set; }

		public Action<int, bool> ScrollToRequested { get; set; }

		public void ScrollTo(int index, bool animate = true)
		{
			ScrollToRequested?.Invoke(index, animate);
		}

		protected override void Dispose(bool disposing)
		{
			if (!disposing)
				return;

			DisposeObservable();

			var currentViews = CurrentViews?.ToList();
			CurrentViews?.Clear();
			currentViews?.ForEach(x => x.Value?.Dispose());
			base.Dispose(disposing);
		}
	}

	public enum SelectionMode
	{
		None,
		Single,
		Multiple
	}

	/// <summary>
	/// Non-generic CollectionView for simple scenarios.
	/// </summary>
	public class CollectionView : ListView
	{
		public ItemsLayout ItemsLayout { get; set; } = ItemsLayout.Vertical();

		public View EmptyView { get; set; }

		public SelectionMode SelectionMode { get; set; } = SelectionMode.Single;

		public ItemSizingStrategy ItemSizingStrategy { get; set; } = ItemSizingStrategy.MeasureAllItems;

		// Infinite scroll support
		public int RemainingItemsThreshold { get; set; } = 0;

		public Action RemainingItemsThresholdReached { get; set; }

		public Action<int> RemainingItemsThresholdReachedCommand { get; set; }
	}
}
