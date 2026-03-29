using System.Collections;
using System.Collections.Specialized;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Platform.Maui.Linux.Gtk4.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

/// <summary>
/// Basic CollectionView handler using Gtk.ListView and GTK selection models.
/// Supports ItemsSource, selection mode/selection state, EmptyView, Header/Footer,
/// scrollbar visibility, ItemTemplate, and basic ItemsLayout orientation.
/// </summary>
public class CollectionViewHandler : GtkViewHandler<IView, Gtk.ScrolledWindow>
{
	Gtk.ListView? _listView;
	Gtk.StringList? _model;
	Gtk.SelectionModel? _selectionModel;
	Gtk.Label? _emptyLabel;
	readonly List<object?> _items = [];
	readonly HashSet<int> _groupHeaderIndices = [];
	bool _updatingSelection;
	INotifyCollectionChanged? _observedCollection;

	public static new IPropertyMapper<IView, CollectionViewHandler> Mapper =
		new PropertyMapper<IView, CollectionViewHandler>(ViewMapper)
		{
			["ItemsSource"] = MapItemsSource,
			["SelectionMode"] = MapSelectionMode,
			["SelectedItem"] = MapSelectedItem,
			["SelectedItems"] = MapSelectedItems,
			["EmptyView"] = MapEmptyView,
			["EmptyViewTemplate"] = MapEmptyView,
			["Header"] = MapHeader,
			["HeaderTemplate"] = MapHeader,
			["Footer"] = MapFooter,
			["FooterTemplate"] = MapFooter,
			["ItemsLayout"] = MapItemsLayout,
			["ItemTemplate"] = MapItemTemplate,
			["ItemSizingStrategy"] = MapItemSizingStrategy,
			["ItemsUpdatingScrollMode"] = MapItemsUpdatingScrollMode,
			["IsGrouped"] = MapIsGrouped,
			["CanReorderItems"] = MapCanReorderItems,
			["HorizontalScrollBarVisibility"] = MapScrollBarVisibility,
			["VerticalScrollBarVisibility"] = MapScrollBarVisibility,
			[nameof(IView.Background)] = MapBackgroundColor,
			["BackgroundColor"] = MapBackgroundColor,
			// Accessibility properties — registered as no-ops to prevent warnings
			["IsInAccessibleTree"] = MapAccessibility,
			["Description"] = MapAccessibility,
			["HeadingLevel"] = MapAccessibility,
			["Hint"] = MapAccessibility,
			["ExcludedWithChildren"] = MapAccessibility,
		};

	public CollectionViewHandler() : base(Mapper) { }

	protected override Gtk.ScrolledWindow CreatePlatformView()
	{
		var scrolled = Gtk.ScrolledWindow.New();
		scrolled.SetVexpand(true);
		scrolled.SetHexpand(true);
		scrolled.SetPolicy(Gtk.PolicyType.Automatic, Gtk.PolicyType.Automatic);
		// Prevent GTK from expanding ScrolledWindow to full content height —
		// MAUI drives sizing through PlatformArrange / SetSizeRequest.
		scrolled.SetPropagateNaturalHeight(false);

		_model = Gtk.StringList.New(null);

		RebuildListView();

		// ListView is the direct child of ScrolledWindow for proper virtualization
		scrolled.SetChild(_listView!);

		return scrolled;
	}

	void RebuildListView()
	{
		var hasTemplate = VirtualView is CollectionView cv && cv.ItemTemplate != null;
		Gtk.ListItemFactory factory;

		if (hasTemplate)
			factory = BuildTemplateFactory();
		else
			factory = BuildStringFactory();

		if (_selectionModel == null)
			_selectionModel = Gtk.NoSelection.New(_model);

		var newListView = Gtk.ListView.New(_selectionModel, factory);
		if (!hasTemplate)
			newListView.AddCssClass("navigation-sidebar");
		newListView.SetVexpand(true);

		// Replace in the ScrolledWindow directly
		if (_listView != null)
			PlatformView?.SetChild(newListView);

		_listView = newListView;
	}

	Gtk.SignalListItemFactory BuildStringFactory()
	{
		var factory = Gtk.SignalListItemFactory.New();
		factory.OnSetup += (_, args) =>
		{
			var listItem = (Gtk.ListItem)args.Object;
			var label = Gtk.Label.New(string.Empty);
			label.SetHalign(Gtk.Align.Start);
			label.SetXalign(0f);
			label.SetMarginStart(12);
			label.SetMarginEnd(12);
			label.SetMarginTop(8);
			label.SetMarginBottom(8);
			listItem.SetChild(label);
		};
		factory.OnBind += (_, args) =>
		{
			var listItem = (Gtk.ListItem)args.Object;
			var label = (Gtk.Label)listItem.GetChild()!;
			var item = (Gtk.StringObject)listItem.GetItem()!;
			var idx = (int)listItem.GetPosition();

			label.SetText(item.GetString());

			// Style group headers differently
			if (_groupHeaderIndices.Contains(idx))
			{
				var cssProvider = Gtk.CssProvider.New();
				cssProvider.LoadFromString("label { font-weight: bold; font-size: 13px; }");
				label.GetStyleContext().AddProvider(cssProvider, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
			}
		};
		return factory;
	}

	Gtk.SignalListItemFactory BuildTemplateFactory()
	{
		var factory = Gtk.SignalListItemFactory.New();
		factory.OnSetup += (_, args) =>
		{
			// Child is set in OnBind — can't pre-create a fixed container
			// because each template may produce different widget types.
		};
		factory.OnBind += (_, args) =>
		{
			var listItem = (Gtk.ListItem)args.Object;

			var idx = (int)listItem.GetPosition();
			if (idx < 0 || idx >= _items.Count) return;
			var dataItem = _items[idx];

			if (VirtualView is not CollectionView collectionView)
				return;

			// Check if this is a group header
			if (_groupHeaderIndices.Contains(idx))
			{
				var headerWidget = BuildGroupHeader(collectionView, dataItem);
				headerWidget.SetSizeRequest(-1, 36);
				listItem.SetChild(headerWidget);
				return;
			}

			if (collectionView.ItemTemplate == null)
				return;

			try
			{
				var (nativeWidget, measuredHeight) = InflateTemplate(collectionView, dataItem);
				if (nativeWidget != null)
					listItem.SetChild(nativeWidget);
			}
			catch (Exception ex)
			{
				var fallback = Gtk.Label.New(dataItem?.ToString() ?? "(error)");
				fallback.SetHalign(Gtk.Align.Start);
				fallback.SetMarginStart(12);
				listItem.SetChild(fallback);
				Console.WriteLine($"[CollectionView] ItemTemplate error: {ex.Message}");
			}
		};
		factory.OnUnbind += (_, args) =>
		{
			var listItem = (Gtk.ListItem)args.Object;
			listItem.SetChild(null);
		};
		factory.OnTeardown += (_, args) =>
		{
		};
		return factory;
	}

	Gtk.Widget BuildGroupHeader(CollectionView collectionView, object? groupData)
	{
		// Try GroupHeaderTemplate first — inflate via handler pipeline
		if (collectionView.GroupHeaderTemplate != null && MauiContext != null)
		{
			try
			{
				var content = collectionView.GroupHeaderTemplate.CreateContent();
				if (content is View mauiView)
				{
					mauiView.BindingContext = groupData;
					return InflateView(mauiView).widget;
				}
			}
			catch { }
		}

		// Default group header: bold label with separator
		var headerBox = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
		headerBox.SetHexpand(true);

		var label = Gtk.Label.New(groupData?.ToString() ?? "Group");
		label.SetHalign(Gtk.Align.Start);
		label.SetMarginStart(12);
		label.SetMarginTop(8);
		label.SetMarginBottom(4);
		var cssProvider = Gtk.CssProvider.New();
		cssProvider.LoadFromString("label { font-weight: bold; font-size: 13px; color: @theme_selected_bg_color; }");
		label.GetStyleContext().AddProvider(cssProvider, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
		headerBox.Append(label);

		var sep = Gtk.Separator.New(Gtk.Orientation.Horizontal);
		headerBox.Append(sep);

		return headerBox;
	}

	/// <summary>
	/// Inflates a DataTemplate item using the standard MAUI handler pipeline.
	/// Each view gets its proper handler (BorderHandler, LayoutHandler, etc.)
	/// rather than being manually reconstructed as GTK widgets.
	/// </summary>
	(Gtk.Widget? widget, int height) InflateTemplate(CollectionView collectionView, object? dataItem)
	{
		var template = collectionView.ItemTemplate;
		var content = template is DataTemplateSelector selector
			? selector.SelectTemplate(dataItem, collectionView)?.CreateContent()
			: template?.CreateContent();

		if (content is not View mauiView)
			return (null, 0);

		mauiView.BindingContext = dataItem;
		return InflateView(mauiView);
	}

	/// <summary>
	/// Converts a MAUI View to a native GTK widget using ToPlatform, then measures
	/// and sizes it so GTK's layout engine gives the row correct height.
	/// </summary>
	(Gtk.Widget widget, int height) InflateView(View mauiView)
	{
		if (MauiContext == null)
			throw new InvalidOperationException("MauiContext not set.");

		var widthConstraint = _listView?.GetAllocatedWidth() ?? 400;
		if (widthConstraint <= 0) widthConstraint = 400;

		// Use the standard MAUI handler pipeline to create native widget
		var nativeWidget = (Gtk.Widget)mauiView.ToPlatform(MauiContext);

		// Mark all GtkLayoutPanels in this tree as externally managed
		// so LayoutHandler's idle/tick callbacks don't override our sizing
		MarkExternallyManaged(nativeWidget);

		// Measure through MAUI's cross-platform layout to get desired size
		var desiredSize = mauiView.Measure(widthConstraint, double.PositiveInfinity);
		var height = Math.Max((int)desiredSize.Height, 20);

		DisableVexpandRecursive(nativeWidget);
		nativeWidget.SetSizeRequest((int)widthConstraint, height);
		nativeWidget.SetHexpand(true);

		// Trigger layout so children are positioned correctly
		if (mauiView.Handler is IViewHandler viewHandler)
			viewHandler.PlatformArrange(new Rect(0, 0, widthConstraint, height));

		return (nativeWidget, height);
	}

	static void MarkExternallyManaged(Gtk.Widget widget)
	{
		if (widget is Platform.GtkLayoutPanel panel)
			panel.IsExternallyManaged = true;

		if (widget is Gtk.Fixed fixedContainer)
		{
			var child = fixedContainer.GetFirstChild();
			while (child != null)
			{
				MarkExternallyManaged(child);
				child = child.GetNextSibling();
			}
		}
	}

	static void DisableVexpandRecursive(Gtk.Widget widget)
	{
		widget.SetVexpand(false);
		if (widget is Gtk.Fixed fixedContainer)
		{
			var child = fixedContainer.GetFirstChild();
			while (child != null)
			{
				DisableVexpandRecursive(child);
				child = child.GetNextSibling();
			}
		}
	}

	protected override void ConnectHandler(Gtk.ScrolledWindow platformView)
	{
		base.ConnectHandler(platformView);
		HookSelectionChanged();

		if (VirtualView is CollectionView cv)
			cv.ScrollToRequested += OnScrollToRequested;
	}

	protected override void DisconnectHandler(Gtk.ScrolledWindow platformView)
	{
		if (VirtualView is CollectionView cv)
			cv.ScrollToRequested -= OnScrollToRequested;

		UnhookCollectionChanged();

		base.DisconnectHandler(platformView);
	}

	void OnScrollToRequested(object? sender, ScrollToRequestEventArgs e)
	{
		if (_listView == null)
			return;

		int index = -1;

		if (e.Mode == ScrollToMode.Position)
		{
			index = e.Index;
		}
		else if (e.Item != null)
		{
			index = _items.IndexOf(e.Item);
		}

		if (index < 0 || index >= _items.Count)
			return;

		_listView.ScrollTo((uint)index, Gtk.ListScrollFlags.None, null);
	}

	public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
	{
		var ve = VirtualView as Microsoft.Maui.Controls.View;

		// Honour explicit HeightRequest
		if (ve != null && ve.HeightRequest >= 0)
		{
			double w = PlatformView.GetAllocatedWidth();
			if (w < 1) w = widthConstraint;
			return new Size(Math.Min(w, widthConstraint), Math.Min(ve.HeightRequest, heightConstraint));
		}

		// Scrollable views report a small desired height so they don't
		// inflate their parent. The parent layout (Grid star row, Fill
		// alignment, etc.) drives the actual size via PlatformArrange.
		double width = PlatformView.GetAllocatedWidth();
		if (width < 1) width = widthConstraint;

		return new Size(Math.Min(width, widthConstraint), Math.Max(1, Math.Min(50, heightConstraint)));
	}

	public override void PlatformArrange(Rect rect)
	{
		var platformView = PlatformView;
		if (platformView == null) return;

		if (platformView.GetParent() is Platform.GtkLayoutPanel layoutPanel)
		{
			layoutPanel.SetChildBounds(platformView, rect.X, rect.Y, (int)rect.Width, (int)rect.Height);
		}
	}

	void HookSelectionChanged()
	{
		if (_selectionModel == null)
			return;

		_selectionModel.OnSelectionChanged += OnSelectionChanged;
	}

	void UnhookSelectionChanged()
	{
		if (_selectionModel == null)
			return;

		_selectionModel.OnSelectionChanged -= OnSelectionChanged;
	}

	void OnSelectionChanged(Gtk.SelectionModel sender, Gtk.SelectionModel.SelectionChangedSignalArgs args)
	{
		if (_updatingSelection || VirtualView is not CollectionView collectionView)
			return;

		try
		{
			_updatingSelection = true;
			switch (collectionView.SelectionMode)
			{
				case SelectionMode.None:
					collectionView.SelectedItem = null;
					collectionView.SelectedItems = [];
					break;
				case SelectionMode.Single:
					if (_selectionModel is Gtk.SingleSelection single)
					{
						var idx = (int)single.GetSelected();
						collectionView.SelectedItem = idx >= 0 && idx < _items.Count ? _items[idx] : null;
						collectionView.SelectedItems = collectionView.SelectedItem != null
							? [collectionView.SelectedItem]
							: [];
					}
					break;
				case SelectionMode.Multiple:
					if (_selectionModel is Gtk.MultiSelection multi)
					{
						var selected = new List<object>();
						var set = multi.GetSelection();
						var count = (uint)set.GetSize();
						for (uint i = 0; i < count; i++)
						{
							var idx = (int)set.GetNth(i);
							if (idx >= 0 && idx < _items.Count && _items[idx] != null)
								selected.Add(_items[idx]!);
						}

						collectionView.SelectedItems = selected;
						collectionView.SelectedItem = selected.Count > 0 ? selected[0] : null;
					}
					break;
			}
		}
		finally
		{
			_updatingSelection = false;
		}
	}

	public static void MapItemsSource(CollectionViewHandler handler, IView view)
	{
		if (view is not CollectionView collectionView)
			return;

		handler._items.Clear();
		handler._groupHeaderIndices.Clear();

		// Collect all string representations first, then create StringList in one shot
		var strings = new List<string>();

		if (collectionView.ItemsSource != null)
		{
			if (collectionView.IsGrouped)
			{
				foreach (var group in collectionView.ItemsSource)
				{
					int headerIdx = handler._items.Count;
					handler._groupHeaderIndices.Add(headerIdx);
					handler._items.Add(group);
					strings.Add(group?.ToString() ?? "Group");

					if (group is IEnumerable groupItems)
					{
						foreach (var item in groupItems)
						{
							handler._items.Add(item);
							strings.Add(item?.ToString() ?? string.Empty);
						}
					}
				}
			}
			else
			{
				foreach (var item in collectionView.ItemsSource)
				{
					handler._items.Add(item);
					strings.Add(item?.ToString() ?? string.Empty);
				}
			}
		}

		// Replace model in one shot — avoids O(n) individual Append/Remove signals
		handler.UnhookSelectionChanged();
		handler._model = Gtk.StringList.New(strings.ToArray());

		handler._selectionModel = collectionView.SelectionMode switch
		{
			SelectionMode.Single => Gtk.SingleSelection.New(handler._model),
			SelectionMode.Multiple => Gtk.MultiSelection.New(handler._model),
			_ => Gtk.NoSelection.New(handler._model),
		};

		handler._listView?.SetModel(handler._selectionModel);
		handler.HookSelectionChanged();

		handler.UpdateDisplayedChild(collectionView);
		MapSelectedItem(handler, view);

		// Subscribe to INotifyCollectionChanged for live updates
		handler.UnhookCollectionChanged();
		if (collectionView.ItemsSource is INotifyCollectionChanged incc)
		{
			handler._observedCollection = incc;
			incc.CollectionChanged += handler.OnCollectionChanged;
		}
	}

	void UnhookCollectionChanged()
	{
		if (_observedCollection != null)
		{
			_observedCollection.CollectionChanged -= OnCollectionChanged;
			_observedCollection = null;
		}
	}

	void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (VirtualView is not CollectionView collectionView)
			return;

		// For simplicity and correctness with grouped/template scenarios,
		// do a full reload on any change. The batch StringList.New() makes this fast.
		MapItemsSource(this, collectionView);
	}

	public static void MapSelectionMode(CollectionViewHandler handler, IView view)
	{
		if (view is not CollectionView collectionView || handler._model == null || handler._listView == null)
			return;

		handler.UnhookSelectionChanged();

		handler._selectionModel = collectionView.SelectionMode switch
		{
			SelectionMode.None => Gtk.NoSelection.New(handler._model),
			SelectionMode.Single => Gtk.SingleSelection.New(handler._model),
			SelectionMode.Multiple => Gtk.MultiSelection.New(handler._model),
			_ => Gtk.SingleSelection.New(handler._model),
		};

		handler._listView.SetModel(handler._selectionModel);
		handler.HookSelectionChanged();
		MapSelectedItem(handler, view);
	}

	public static void MapSelectedItem(CollectionViewHandler handler, IView view)
	{
		if (view is not CollectionView collectionView || handler._selectionModel == null)
			return;

		try
		{
			handler._updatingSelection = true;
			if (collectionView.SelectionMode == SelectionMode.None)
			{
				handler._selectionModel.UnselectAll();
				return;
			}

			if (collectionView.SelectionMode == SelectionMode.Single && handler._selectionModel is Gtk.SingleSelection single)
			{
				var selectedIndex = collectionView.SelectedItem != null
					? handler._items.IndexOf(collectionView.SelectedItem)
					: -1;
				single.SetSelected(selectedIndex >= 0 ? (uint)selectedIndex : uint.MaxValue);
			}

			if (collectionView.SelectionMode == SelectionMode.Multiple && handler._selectionModel is Gtk.MultiSelection multi)
			{
				multi.UnselectAll();
				var selectedItems = collectionView.SelectedItems;
				if (selectedItems == null)
					return;

				foreach (var selected in selectedItems)
				{
					var idx = handler._items.IndexOf(selected);
					if (idx >= 0)
						multi.SelectItem((uint)idx, true);
				}
			}
		}
		finally
		{
			handler._updatingSelection = false;
		}
	}

	public static void MapSelectedItems(CollectionViewHandler handler, IView view)
	{
		MapSelectedItem(handler, view);
	}

	public static void MapEmptyView(CollectionViewHandler handler, IView view)
	{
		if (view is CollectionView collectionView)
			handler.UpdateDisplayedChild(collectionView);
	}

	public static void MapScrollBarVisibility(CollectionViewHandler handler, IView view)
	{
		if (view is not CollectionView collectionView || handler.PlatformView == null)
			return;

		var h = collectionView.HorizontalScrollBarVisibility switch
		{
			ScrollBarVisibility.Always => Gtk.PolicyType.Always,
			ScrollBarVisibility.Never => Gtk.PolicyType.Never,
			_ => Gtk.PolicyType.Automatic,
		};
		var v = collectionView.VerticalScrollBarVisibility switch
		{
			ScrollBarVisibility.Always => Gtk.PolicyType.Always,
			ScrollBarVisibility.Never => Gtk.PolicyType.Never,
			_ => Gtk.PolicyType.Automatic,
		};
		handler.PlatformView.SetPolicy(h, v);
	}

	public static void MapHeader(CollectionViewHandler handler, IView view)
	{
		// Header/Footer are not rendered as separate widgets to preserve
		// GTK ListView virtualization (ListView must be direct ScrolledWindow child).
	}

	public static void MapFooter(CollectionViewHandler handler, IView view)
	{
	}

	public static void MapItemsLayout(CollectionViewHandler handler, IView view)
	{
		if (view is not CollectionView collectionView || handler._listView == null)
			return;

		// GTK ListView is always vertical; orientation awareness is a best-effort hint.
		if (collectionView.ItemsLayout is LinearItemsLayout linear &&
			linear.Orientation == ItemsLayoutOrientation.Horizontal)
		{
			handler._listView.SetOrientation(Gtk.Orientation.Horizontal);
		}
		else
		{
			handler._listView.SetOrientation(Gtk.Orientation.Vertical);
		}
	}

	public static void MapBackgroundColor(CollectionViewHandler handler, IView view)
	{
		if (view is CollectionView cv && cv.BackgroundColor != null)
			handler.ApplyCss(handler.PlatformView, $"background-color: {ToGtkColor(cv.BackgroundColor)};");
	}

	public static void MapItemTemplate(CollectionViewHandler handler, IView view)
	{
		// Rebuild the ListView with the appropriate factory (string vs template)
		handler.RebuildListView();
		handler.HookSelectionChanged();
		MapItemsSource(handler, view);
	}

	public static void MapItemSizingStrategy(CollectionViewHandler handler, IView view) { }
	public static void MapItemsUpdatingScrollMode(CollectionViewHandler handler, IView view) { }
	public static void MapIsGrouped(CollectionViewHandler handler, IView view)
	{
		// Grouping affects how ItemsSource is flattened — re-map items
		MapItemsSource(handler, view);
	}
	public static void MapCanReorderItems(CollectionViewHandler handler, IView view) { }
	public static void MapAccessibility(CollectionViewHandler handler, IView view) { }

	void UpdateDisplayedChild(CollectionView collectionView)
	{
		if (PlatformView == null || _listView == null)
			return;

		var hasItems = _items.Count > 0;
		if (hasItems || collectionView.EmptyView == null)
		{
			if (PlatformView.GetChild() != _listView)
				PlatformView.SetChild(_listView);
			return;
		}

		// Show empty view
		if (_emptyLabel == null)
		{
			_emptyLabel = Gtk.Label.New(string.Empty);
			_emptyLabel.SetHalign(Gtk.Align.Center);
			_emptyLabel.SetValign(Gtk.Align.Center);
			_emptyLabel.SetWrap(true);
			_emptyLabel.SetJustify(Gtk.Justification.Center);
			_emptyLabel.SetVexpand(true);
		}

		_emptyLabel.SetText(collectionView.EmptyView?.ToString() ?? string.Empty);
		PlatformView.SetChild(_emptyLabel);
	}
}
