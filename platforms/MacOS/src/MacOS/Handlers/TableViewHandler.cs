using CoreGraphics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class TableViewHandler : MacOSViewHandler<TableView, NSScrollView>
{
	public static readonly IPropertyMapper<TableView, TableViewHandler> Mapper =
		new PropertyMapper<TableView, TableViewHandler>(ViewMapper)
		{
			[nameof(TableView.Root)] = MapRoot,
			[nameof(TableView.HasUnevenRows)] = MapRoot,
			[nameof(TableView.RowHeight)] = MapRoot,
		};

	FlippedDocumentView? _documentView;
	MacOSContainerView? _itemsContainer;
	readonly List<NSView> _itemViews = new();

	public TableViewHandler() : base(Mapper) { }

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
		if (VirtualView?.Root != null)
			VirtualView.ModelChanged += OnModelChanged;
	}

	protected override void DisconnectHandler(NSScrollView platformView)
	{
		if (VirtualView != null)
			VirtualView.ModelChanged += OnModelChanged;
		base.DisconnectHandler(platformView);
	}

	void OnModelChanged(object? sender, EventArgs e) => ReloadItems();

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

		foreach (var subview in _itemViews)
		{
			var height = GetViewHeight(subview, width);
			subview.Frame = new CGRect(0, (double)y, (double)width, (double)height);
			y += height;
		}

		_itemsContainer.Frame = new CGRect(0, 0, (double)width, (double)y);
		_documentView.Frame = new CGRect(0, 0, (double)width, (double)y);
	}

	nfloat GetViewHeight(NSView view, nfloat width)
	{
		// Plain NSView separators have a fixed 1px frame
		if (view is not MacOSContainerView)
			return view.Frame.Height > 0 ? view.Frame.Height : 1;

		if (VirtualView != null && !VirtualView.HasUnevenRows && VirtualView.RowHeight > 0)
			return VirtualView.RowHeight;

		if (view is MacOSContainerView container)
		{
			var size = container.SizeThatFits(new CGSize(width, nfloat.MaxValue));
			return size.Height > 0 && size.Height < 10000 ? size.Height : 44;
		}
		var intrinsic = view.IntrinsicContentSize;
		return intrinsic.Height > 0 ? intrinsic.Height : 44;
	}

	public static void MapRoot(TableViewHandler handler, TableView view) => handler.ReloadItems();

	void ReloadItems()
	{
		if (_itemsContainer == null || MauiContext == null)
			return;

		foreach (var view in _itemViews)
			view.RemoveFromSuperview();
		_itemViews.Clear();

		var root = VirtualView?.Root;
		if (root == null)
		{
			if (PlatformView.Frame.Width > 0)
				LayoutItems(new Rect(0, 0, PlatformView.Frame.Width, PlatformView.Frame.Height));
			return;
		}

		bool firstSection = true;
		foreach (var section in root)
		{
			if (!firstSection)
				AddSeparator(Colors.LightGray);
			firstSection = false;

			// Section header — use native NSTextField for reliability
			var headerView = new NSTextField
			{
				StringValue = section.Title ?? string.Empty,
				Editable = false,
				Bezeled = false,
				DrawsBackground = true,
				BackgroundColor = NSColor.FromRgba(245, 245, 245, 255),
				TextColor = NSColor.Gray,
				Font = NSFont.BoldSystemFontOfSize(12),
			};
			// Wrap in a container with padding
			var headerContainer = new NSView();
			headerContainer.WantsLayer = true;
			headerContainer.Layer!.BackgroundColor = NSColor.FromRgba(245, 245, 245, 255).CGColor;
			headerView.SizeToFit();
			var textHeight = headerView.Frame.Height;
			var containerHeight = textHeight + 14; // 10pt top + 4pt bottom padding
			headerContainer.Frame = new CGRect(0, 0, 400, containerHeight);
			headerView.Frame = new CGRect(12, 4, 400, textHeight);
			headerContainer.AddSubview(headerView);
			_itemViews.Add(headerContainer);
			_itemsContainer!.AddSubview(headerContainer);

			AddSeparator(Colors.LightGray);

			// Section cells
			bool firstCell = true;
			foreach (var cell in section)
			{
				if (!firstCell)
					AddSeparator(Color.FromArgb("#E0E0E0"));
				firstCell = false;

				var cellView = CellToView(cell);
				AddView(cellView);
			}
		}

		if (PlatformView.Frame.Width > 0)
			LayoutItems(new Rect(0, 0, PlatformView.Frame.Width, PlatformView.Frame.Height));
	}

	void AddView(IView mauiView)
	{
		if (MauiContext == null) return;
		var platformView = mauiView.ToMacOSPlatform(MauiContext);
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

	static IView CellToView(Cell cell)
	{
		if (cell is ViewCell viewCell && viewCell.View != null)
			return viewCell.View;

		if (cell is TextCell textCell)
		{
			var stack = new VerticalStackLayout { Padding = new Thickness(12, 6), Spacing = 2 };
			var text = new Label { Text = textCell.Text ?? string.Empty, FontSize = 14 };
			if (textCell.TextColor != null) text.TextColor = textCell.TextColor;
			stack.Children.Add(text);
			if (!string.IsNullOrEmpty(textCell.Detail))
			{
				var detail = new Label
				{
					Text = textCell.Detail,
					FontSize = 11,
					TextColor = textCell.DetailColor ?? Colors.Gray
				};
				stack.Children.Add(detail);
			}

			if (textCell.Command != null)
			{
				var tap = new TapGestureRecognizer();
				tap.Tapped += (s, e) => textCell.Command?.Execute(textCell.CommandParameter);
				((View)stack).GestureRecognizers.Add(tap);
			}
			return stack;
		}

		if (cell is ImageCell imageCell)
		{
			var hStack = new HorizontalStackLayout { Padding = new Thickness(12, 6), Spacing = 10 };
			if (imageCell.ImageSource != null)
			{
				hStack.Children.Add(new Microsoft.Maui.Controls.Image
				{
					Source = imageCell.ImageSource,
					WidthRequest = 28,
					HeightRequest = 28,
					VerticalOptions = LayoutOptions.Center,
				});
			}
			var textStack = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
			textStack.Children.Add(new Label { Text = imageCell.Text ?? string.Empty, FontSize = 14 });
			if (!string.IsNullOrEmpty(imageCell.Detail))
				textStack.Children.Add(new Label { Text = imageCell.Detail, FontSize = 11, TextColor = Colors.Gray });
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
			grid.Children.Add(new Label
			{
				Text = switchCell.Text ?? string.Empty,
				FontSize = 14,
				VerticalOptions = LayoutOptions.Center,
			});
			var sw = new Switch { IsToggled = switchCell.On, VerticalOptions = LayoutOptions.Center };
			Grid.SetColumn(sw, 1);
			sw.Toggled += (s, e) => switchCell.On = e.Value;
			grid.Children.Add(sw);
			return grid;
		}

		if (cell is EntryCell entryCell)
		{
			var grid = new Grid
			{
				Padding = new Thickness(12, 8),
				ColumnSpacing = 10,
			};
			if (!string.IsNullOrEmpty(entryCell.Label))
			{
				grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
				grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
				var label = new Label
				{
					Text = entryCell.Label,
					FontSize = 14,
					VerticalOptions = LayoutOptions.Center,
				};
				grid.Children.Add(label);
				var entry = new Entry
				{
					Text = entryCell.Text ?? string.Empty,
					Placeholder = entryCell.Placeholder ?? string.Empty,
					VerticalOptions = LayoutOptions.Center,
				};
				Grid.SetColumn(entry, 1);
				entry.TextChanged += (s, e) => entryCell.Text = e.NewTextValue;
				grid.Children.Add(entry);
			}
			else
			{
				grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
				var entry = new Entry
				{
					Text = entryCell.Text ?? string.Empty,
					Placeholder = entryCell.Placeholder ?? string.Empty,
					VerticalOptions = LayoutOptions.Center,
				};
				entry.TextChanged += (s, e) => entryCell.Text = e.NewTextValue;
				grid.Children.Add(entry);
			}
			return grid;
		}

		return new Label
		{
			Text = cell.ToString() ?? string.Empty,
			Padding = new Thickness(12, 8),
		};
	}
}
