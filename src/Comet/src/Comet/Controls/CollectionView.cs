using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Comet.Internal;
using Comet.Reactive;
using Comet.Backend;
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
		bool _updatingItems;

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
			// Native list nodes retain materialized rows. Evicting a template here
			// would dispose content that the backend still owns.
			CurrentViews = new Dictionary<(int section, int row, object item), View>();

			ShouldDisposeViews = true;
		}

		public override void ViewPropertyChanged(string property, object value)
		{
			if (property == nameof(Items))
			{
				if (_updatingItems)
					return;
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

		/// <summary>
		/// Re-reads the source while retaining templates for equal items at unchanged
		/// indices. Use immutable items; call ReloadData after editing an item in place.
		/// </summary>
		public void RefreshItems() => UpdateItems(preserveRows: true);

		public override void ReloadData() => UpdateItems(preserveRows: false);

		void UpdateItems(bool preserveRows)
		{
			if (_updatingItems)
				return;
			_updatingItems = true;
			using var hold = ReactiveScheduler.HoldFlushes();
			try
			{
				if (!preserveRows)
					ResetRemainingItemsThreshold();
				DisposeObservable();
				_items?.Reevaluate();
				currentItems = _items?.CurrentValue;
				SetupObservable();

				int firstChanged = preserveRows ? currentItems?.Count ?? 0 : 0;
				if (preserveRows && currentItems is not null)
				{
					foreach (var key in CurrentViews.Keys)
					{
						if (key.section != 0 || key.row >= currentItems.Count ||
							!EqualityComparer<T>.Default.Equals((T)key.item, currentItems[key.row]))
							firstChanged = Math.Min(firstChanged, key.section == 0 ? key.row : 0);
					}
				}

				var stale = CurrentViews.Where(pair => pair.Key.row >= firstChanged).ToArray();
				foreach (var pair in stale)
					CurrentViews.Remove(pair.Key);
				try
				{
					if (preserveRows && Node is { } node)
						node.ApplyProperty(PropertyIds.List_InvalidateFrom, PropertyValue.From(firstChanged));
					else
						base.ReloadData();
				}
				finally
				{
					foreach (var pair in stale)
						pair.Value?.Dispose();
				}
			}
			finally
			{
				_updatingItems = false;
			}
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

		internal override void ReleaseCachedRow(int index, View content)
		{
			foreach (var pair in CurrentViews)
			{
				if (pair.Key.section != 0 || pair.Key.row != index || !ReferenceEquals(pair.Value, content))
					continue;
				CurrentViews.Remove(pair.Key);
				content.Dispose();
				return;
			}
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

		int _retainedItemLimit;

		/// <summary>
		/// Optional Android vertical-list row-cache budget. Zero retains visited rows
		/// until reload/disposal. A positive value evicts least-recently-used rows only
		/// after Compose releases them, so native-retained rows may exceed the budget.
		/// Evicted templates are recreated; keep persistent row state in the item model.
		/// Configure before materialization. Other list backends retain their existing policy.
		/// </summary>
		public int RetainedItemLimit
		{
			get => _retainedItemLimit;
			set
			{
				if (value < 0)
					throw new ArgumentOutOfRangeException(nameof(value), "A retained item limit cannot be negative.");
				if (Node is not null && value != _retainedItemLimit)
					throw new InvalidOperationException("Configure the retained item limit before materializing the list.");
				_retainedItemLimit = value;
			}
		}

		internal virtual void ReleaseCachedRow(int index, View content) { }

		// Infinite scroll support
		public int RemainingItemsThreshold { get; set; } = 0;

		public Action RemainingItemsThresholdReached { get; set; }

		public Action<int> RemainingItemsThresholdReachedCommand { get; set; }

		int _lastNotifiedCount = -1;
		bool _wasWithinThreshold;
		bool _notifyingThreshold;

		internal void ResetRemainingItemsThreshold()
		{
			_lastNotifiedCount = -1;
			_wasWithinThreshold = false;
		}

		internal void NotifyVisibleIndex(int lastVisibleIndex)
		{
			if (IsDisposed || _notifyingThreshold)
				return;
			int count = ((IListView)this).Rows(0);
			if (RemainingItemsThreshold < 0 || count == 0 || lastVisibleIndex < 0 ||
				lastVisibleIndex >= count || count - lastVisibleIndex - 1 > RemainingItemsThreshold)
			{
				_wasWithinThreshold = false;
				return;
			}
			if (_wasWithinThreshold && count == _lastNotifiedCount)
				return;
			_wasWithinThreshold = true;
			_lastNotifiedCount = count;
			_notifyingThreshold = true;
			try
			{
				RemainingItemsThresholdReached?.Invoke();
				RemainingItemsThresholdReachedCommand?.Invoke(lastVisibleIndex);
			}
			finally
			{
				_notifyingThreshold = false;
			}
		}
	}
}
