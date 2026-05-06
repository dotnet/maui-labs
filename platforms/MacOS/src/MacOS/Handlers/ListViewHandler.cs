using System.Collections;
using System.Collections.Specialized;
using CoreGraphics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class ListViewHandler : MacOSViewHandler<ListView, NSScrollView>
{
	public static readonly IPropertyMapper<ListView, ListViewHandler> Mapper =
		new PropertyMapper<ListView, ListViewHandler>(ViewMapper)
		{
			[nameof(ListView.ItemsSource)] = MapItemsSource,
			[nameof(ListView.ItemTemplate)] = MapItemTemplate,
			[nameof(ListView.SelectedItem)] = MapSelectedItem,
			[nameof(ListView.Header)] = MapHeaderFooter,
			[nameof(ListView.Footer)] = MapHeaderFooter,
			[nameof(ListView.HeaderTemplate)] = MapHeaderFooter,
			[nameof(ListView.FooterTemplate)] = MapHeaderFooter,
			[nameof(ListView.IsGroupingEnabled)] = MapItemsSource,
			[nameof(ListView.GroupHeaderTemplate)] = MapItemsSource,
			[nameof(ListView.SeparatorColor)] = MapSeparatorColor,
			[nameof(ListView.SeparatorVisibility)] = MapSeparatorColor,
			[nameof(ListView.RowHeight)] = MapItemsSource,
			[nameof(ListView.HasUnevenRows)] = MapItemsSource,
		};

	FlippedDocumentView? _documentView;
	MacOSContainerView? _itemsContainer;
	INotifyCollectionChanged? _observableSource;
	readonly List<NSView> _itemViews = new();
	NSView? _headerView;
	NSView? _footerView;
	bool _updatingSelection;

	public ListViewHandler() : base(Mapper) { }

	protected override NSScrollView CreatePlatformView()
	{
		var scrollView = new NSScrollView
		{
			HasVerticalScroller = true,
			HasHorizontalScroller = false,
			AutohidesScrollers = true,
			DrawsBackground = false,
		};

		_documentView = new FlippedDocumentView();
		_itemsContainer = new MacOSContainerView();
		_documentView.AddSubview(_itemsContainer);
		scrollView.DocumentView = _documentView;

		return scrollView;
	}

	protected override void ConnectHandler(NSScrollView platformView)
	{
		base.ConnectHandler(platformView);
		if (VirtualView != null)
			VirtualView.ItemSelected += OnItemSelected;
	}

	protected override void DisconnectHandler(NSScrollView platformView)
	{
		UnsubscribeCollection();
		if (VirtualView != null)
			VirtualView.ItemSelected -= OnItemSelected;
		base.DisconnectHandler(platformView);
	}

	void OnItemSelected(object? sender, SelectedItemChangedEventArgs e)
	{
		if (_updatingSelection)
			return;
		UpdateSelectionHighlight();
	}

	public override void PlatformArrange(Rect rect)
	{
		base.PlatformArrange(rect);
		LayoutItems(rect);
	}

	void LayoutItems(Rect rect)
	{
		if (_itemsContainer == null || _documentView == null)
			return;

		var width = (nfloat)rect.Width;
		nfloat y = 0;

		if (_headerView != null)
		{
			var headerSize = GetViewSize(_headerView, width);
			_headerView.Frame = new CGRect(0, y, (double)width, headerSize.Height);
			y += headerSize.Height;
		}

		foreach (var subview in _itemViews)
		{
			var height = GetViewHeight(subview, width);
			subview.Frame = new CGRect(0, (double)y, (double)width, (double)height);
			y += height;
		}

		if (_footerView != null)
		{
			var footerSize = GetViewSize(_footerView, width);
			_footerView.Frame = new CGRect(0, (double)y, (double)width, footerSize.Height);
			y += footerSize.Height;
		}

		_itemsContainer.Frame = new CGRect(0, 0, width, y);
		_documentView.Frame = new CGRect(0, 0, width, y);
	}

	nfloat GetViewHeight(NSView view, nfloat width)
	{
		// Plain NSView separators have a fixed 1px frame
		if (view is not MacOSContainerView)
			return view.Frame.Height > 0 ? view.Frame.Height : 1;

		if (VirtualView != null && !VirtualView.HasUnevenRows && VirtualView.RowHeight > 0)
			return VirtualView.RowHeight;

		var size = GetViewSize(view, width);
		return size.Height > 0 && size.Height < 10000 ? size.Height : 44;
	}

	CGSize GetViewSize(NSView view, nfloat width)
	{
		if (view is MacOSContainerView container)
			return container.SizeThatFits(new CGSize(width, nfloat.MaxValue));
		var intrinsic = view.IntrinsicContentSize;
		return intrinsic.Height >= 0 ? intrinsic : new CGSize(width, 44);
	}

	public static void MapItemsSource(ListViewHandler handler, ListView view) => handler.ReloadItems();
	public static void MapItemTemplate(ListViewHandler handler, ListView view) => handler.ReloadItems();
	public static void MapSelectedItem(ListViewHandler handler, ListView view) => handler.UpdateSelectionHighlight();
	public static void MapHeaderFooter(ListViewHandler handler, ListView view) => handler.UpdateHeaderFooter();
	public static void MapSeparatorColor(ListViewHandler handler, ListView view) => handler.ReloadItems();

	void UnsubscribeCollection()
	{
		if (_observableSource != null)
		{
			_observableSource.CollectionChanged -= OnCollectionChanged;
			_observableSource = null;
		}
	}

	void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ReloadItems();

	void UpdateHeaderFooter()
	{
		if (_itemsContainer == null || MauiContext == null)
			return;

		// Remove old header/footer
		_headerView?.RemoveFromSuperview();
		_footerView?.RemoveFromSuperview();
		_headerView = null;
		_footerView = null;

		// Header
		var headerView = CreateFromTemplateOrObject(VirtualView?.Header, VirtualView?.HeaderTemplate);
		if (headerView != null)
		{
			_headerView = headerView.ToMacOSPlatform(MauiContext);
			_itemsContainer.AddSubview(_headerView);
		}

		// Footer
		var footerView = CreateFromTemplateOrObject(VirtualView?.Footer, VirtualView?.FooterTemplate);
		if (footerView != null)
		{
			_footerView = footerView.ToMacOSPlatform(MauiContext);
			_itemsContainer.AddSubview(_footerView);
		}

		if (PlatformView.Frame.Width > 0)
			LayoutItems(new Rect(0, 0, PlatformView.Frame.Width, PlatformView.Frame.Height));
	}

	void ReloadItems()
	{
		if (_itemsContainer == null || MauiContext == null)
			return;

		UnsubscribeCollection();

		// Remove old item views (keep header/footer)
		foreach (var view in _itemViews)
			view.RemoveFromSuperview();
		_itemViews.Clear();

		var itemsSource = VirtualView?.ItemsSource;
		if (itemsSource == null)
		{
			if (PlatformView.Frame.Width > 0)
				LayoutItems(new Rect(0, 0, PlatformView.Frame.Width, PlatformView.Frame.Height));
			return;
		}

		if (itemsSource is INotifyCollectionChanged observable)
		{
			_observableSource = observable;
			_observableSource.CollectionChanged += OnCollectionChanged;
		}

		var separatorColor = VirtualView?.SeparatorVisibility == SeparatorVisibility.None
			? null
			: VirtualView?.SeparatorColor ?? Colors.LightGray;

		if (VirtualView?.IsGroupingEnabled == true)
			BuildGroupedItems(itemsSource, separatorColor);
		else
			BuildFlatItems(itemsSource, separatorColor);

		UpdateHeaderFooter();
		UpdateSelectionHighlight();

		if (PlatformView.Frame.Width > 0)
			LayoutItems(new Rect(0, 0, PlatformView.Frame.Width, PlatformView.Frame.Height));
	}

	void BuildFlatItems(IEnumerable itemsSource, Color? separatorColor)
	{
		var template = VirtualView?.ItemTemplate;
		bool first = true;

		foreach (var item in itemsSource)
		{
			if (!first && separatorColor != null)
				AddSeparator(separatorColor);
			first = false;

			var view = CreateCellView(item, template);
			if (view != null)
				AddItemView(view, item);
		}
	}

	void BuildGroupedItems(IEnumerable itemsSource, Color? separatorColor)
	{
		var groupHeaderTemplate = VirtualView?.GroupHeaderTemplate;
		var itemTemplate = VirtualView?.ItemTemplate;
		bool firstGroup = true;

		foreach (var group in itemsSource)
		{
			if (!firstGroup && separatorColor != null)
				AddSeparator(separatorColor);
			firstGroup = false;

			// Group header
			var headerView = CreateCellView(group, groupHeaderTemplate);
			if (headerView == null)
			{
				headerView = new Label
				{
					Text = group?.ToString() ?? string.Empty,
					FontAttributes = FontAttributes.Bold,
					FontSize = 13,
					Padding = new Thickness(12, 6),
					BackgroundColor = Color.FromArgb("#E8E8E8"),
				};
			}
			AddItemView(headerView, group);

			// Group items
			if (group is IEnumerable groupItems)
			{
				bool firstItem = true;
				foreach (var item in groupItems)
				{
					if (!firstItem && separatorColor != null)
						AddSeparator(separatorColor);
					firstItem = false;

					var itemView = CreateCellView(item, itemTemplate);
					if (itemView != null)
						AddItemView(itemView, item);
				}
			}
		}
	}

	void AddItemView(IView mauiView, object? item)
	{
		if (MauiContext == null) return;

		var platformView = mauiView.ToMacOSPlatform(MauiContext);
		AddTapForSelection(platformView, item);
		_itemViews.Add(platformView);
		_itemsContainer!.AddSubview(platformView);
	}

	void AddSeparator(Color color)
	{
		if (_itemsContainer == null) return;

		var sep = new NSView(new CGRect(0, 0, 100, 1));
		sep.WantsLayer = true;
		sep.Layer!.BackgroundColor = color.ToPlatformColor().CGColor;
		_itemViews.Add(sep);
		_itemsContainer.AddSubview(sep);
	}

	void AddTapForSelection(NSView platformView, object? item)
	{
		var gesture = new NSClickGestureRecognizer(() =>
		{
			if (VirtualView == null) return;
			_updatingSelection = true;
			try
			{
				VirtualView.SelectedItem = item;
			}
			finally
			{
				_updatingSelection = false;
			}
			UpdateSelectionHighlight();
		});
		platformView.AddGestureRecognizer(gesture);
	}

	void UpdateSelectionHighlight()
	{
		// Selection highlighting is handled through VisualStateManager on the MAUI side
		// No additional platform work needed for basic selection
	}

	IView? CreateCellView(object? item, DataTemplate? template)
	{
		if (template is DataTemplateSelector selector && item != null)
			template = selector.SelectTemplate(item, VirtualView);

		if (template != null)
		{
			var content = template.CreateContent();
			if (content is ViewCell viewCell)
			{
				viewCell.BindingContext = item;
				return viewCell.View;
			}
			if (content is Cell cell)
				return CellToView(cell, item);
			if (content is View view)
			{
				view.BindingContext = item;
				return view;
			}
		}

		// If item is a Cell (from TableView usage), render it directly
		if (item is ViewCell vc)
			return vc.View;
		if (item is Cell cellItem)
			return CellToView(cellItem, item);

		return new Label
		{
			Text = item?.ToString() ?? string.Empty,
			Padding = new Thickness(12, 8),
		};
	}

	static IView CellToView(Cell cell, object? item)
	{
		cell.BindingContext = item;

		if (cell is TextCell textCell)
		{
			var stack = new VerticalStackLayout { Padding = new Thickness(12, 6), Spacing = 2 };
			var textLabel = new Label { FontSize = 14 };
			textLabel.SetBinding(Label.TextProperty, new Binding(nameof(TextCell.Text), source: textCell));
			textLabel.SetBinding(Label.TextColorProperty, new Binding(nameof(TextCell.TextColor), source: textCell));
			stack.Children.Add(textLabel);

			if (!string.IsNullOrEmpty(textCell.Detail))
			{
				var detailLabel = new Label { FontSize = 11, TextColor = Colors.Gray };
				detailLabel.SetBinding(Label.TextProperty, new Binding(nameof(TextCell.Detail), source: textCell));
				detailLabel.SetBinding(Label.TextColorProperty, new Binding(nameof(TextCell.DetailColor), source: textCell));
				stack.Children.Add(detailLabel);
			}
			return stack;
		}

		if (cell is ImageCell imageCell)
		{
			var hStack = new HorizontalStackLayout { Padding = new Thickness(12, 6), Spacing = 10 };
			if (imageCell.ImageSource != null)
			{
				var img = new Microsoft.Maui.Controls.Image
				{
					WidthRequest = 32,
					HeightRequest = 32,
					VerticalOptions = LayoutOptions.Center,
				};
				img.SetBinding(Microsoft.Maui.Controls.Image.SourceProperty, new Binding(nameof(ImageCell.ImageSource), source: imageCell));
				hStack.Children.Add(img);
			}
			var textStack = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
			var text = new Label { FontSize = 14 };
			text.SetBinding(Label.TextProperty, new Binding(nameof(ImageCell.Text), source: imageCell));
			textStack.Children.Add(text);
			if (!string.IsNullOrEmpty(imageCell.Detail))
			{
				var detail = new Label { FontSize = 11, TextColor = Colors.Gray };
				detail.SetBinding(Label.TextProperty, new Binding(nameof(ImageCell.Detail), source: imageCell));
				textStack.Children.Add(detail);
			}
			hStack.Children.Add(textStack);
			return hStack;
		}

		if (cell is SwitchCell switchCell)
		{
			var grid = new Grid
			{
				Padding = new Thickness(12, 8),
				ColumnSpacing = 10,
				ColumnDefinitions =
				{
					new ColumnDefinition(GridLength.Star),
					new ColumnDefinition(GridLength.Auto),
				},
			};
			var label = new Label { FontSize = 14, VerticalOptions = LayoutOptions.Center };
			label.SetBinding(Label.TextProperty, new Binding(nameof(SwitchCell.Text), source: switchCell));
			grid.Children.Add(label);
			var sw = new Switch { VerticalOptions = LayoutOptions.Center };
			Grid.SetColumn(sw, 1);
			sw.SetBinding(Switch.IsToggledProperty, new Binding(nameof(SwitchCell.On), source: switchCell, mode: BindingMode.TwoWay));
			grid.Children.Add(sw);
			return grid;
		}

		if (cell is EntryCell entryCell)
		{
			var grid = new Grid { Padding = new Thickness(12, 8), ColumnSpacing = 10 };
			if (!string.IsNullOrEmpty(entryCell.Label))
			{
				grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
				grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
				var label = new Label { FontSize = 14, VerticalOptions = LayoutOptions.Center };
				label.SetBinding(Label.TextProperty, new Binding(nameof(EntryCell.Label), source: entryCell));
				grid.Children.Add(label);
				var entry = new Entry { VerticalOptions = LayoutOptions.Center };
				Grid.SetColumn(entry, 1);
				entry.SetBinding(Entry.TextProperty, new Binding(nameof(EntryCell.Text), source: entryCell, mode: BindingMode.TwoWay));
				entry.SetBinding(Entry.PlaceholderProperty, new Binding(nameof(EntryCell.Placeholder), source: entryCell));
				grid.Children.Add(entry);
			}
			else
			{
				grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
				var entry = new Entry { VerticalOptions = LayoutOptions.Center };
				entry.SetBinding(Entry.TextProperty, new Binding(nameof(EntryCell.Text), source: entryCell, mode: BindingMode.TwoWay));
				entry.SetBinding(Entry.PlaceholderProperty, new Binding(nameof(EntryCell.Placeholder), source: entryCell));
				grid.Children.Add(entry);
			}
			return grid;
		}

		return new Label { Text = cell.ToString() ?? string.Empty, Padding = new Thickness(12, 8) };
	}

	static IView? CreateFromTemplateOrObject(object? data, DataTemplate? template)
	{
		if (data == null) return null;

		if (template != null)
		{
			var content = template.CreateContent();
			if (content is View view)
			{
				view.BindingContext = data;
				return view;
			}
		}

		if (data is View dataView)
			return dataView;

		if (data is string text)
			return new Label { Text = text, Padding = new Thickness(12, 8), FontSize = 14 };

		return new Label { Text = data.ToString() ?? string.Empty, Padding = new Thickness(12, 8), FontSize = 14 };
	}
}
