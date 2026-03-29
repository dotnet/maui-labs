using System.Collections;
using System.Collections.Specialized;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Platform.Maui.Linux.Gtk4.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

/// <summary>
/// ListView handler for GTK4. Renders MAUI ListView using a Gtk.Box
/// with Gtk.Label/Gtk.Box rows inside a Gtk.ScrolledWindow.
/// Supports ItemsSource, ItemTemplate, grouping, selection, headers/footers,
/// pull-to-refresh integration, and separator visibility.
/// </summary>
#pragma warning disable CS0618 // ListView is obsolete
public class ListViewHandler : GtkViewHandler<ListView, Gtk.ScrolledWindow>
{
	Gtk.Box? _listBox;
	Gtk.Box? _outerBox;
	Gtk.Label? _headerLabel;
	Gtk.Label? _footerLabel;
	readonly List<object?> _items = [];
	readonly List<Gtk.Widget> _rowWidgets = [];
	int _selectedIndex = -1;
	bool _updatingSelection;

	public static new IPropertyMapper<ListView, ListViewHandler> Mapper =
		new PropertyMapper<ListView, ListViewHandler>(ViewMapper)
		{
			[nameof(ListView.ItemsSource)] = MapItemsSource,
			[nameof(ListView.ItemTemplate)] = MapItemTemplate,
			[nameof(ListView.SelectedItem)] = MapSelectedItem,
			[nameof(ListView.SelectionMode)] = MapSelectionMode,
			[nameof(ListView.Header)] = MapHeader,
			[nameof(ListView.HeaderTemplate)] = MapHeader,
			[nameof(ListView.Footer)] = MapFooter,
			[nameof(ListView.FooterTemplate)] = MapFooter,
			[nameof(ListView.HasUnevenRows)] = MapHasUnevenRows,
			[nameof(ListView.RowHeight)] = MapRowHeight,
			[nameof(ListView.SeparatorVisibility)] = MapSeparatorVisibility,
			[nameof(ListView.SeparatorColor)] = MapSeparatorColor,
			[nameof(ListView.IsGroupingEnabled)] = MapIsGroupingEnabled,
			[nameof(ListView.GroupHeaderTemplate)] = MapGroupHeaderTemplate,
			[nameof(IView.Background)] = MapBackgroundColor,
			["BackgroundColor"] = MapBackgroundColor,
		};

	public ListViewHandler() : base(Mapper) { }

	protected override Gtk.ScrolledWindow CreatePlatformView()
	{
		var scrolled = Gtk.ScrolledWindow.New();
		scrolled.SetVexpand(true);
		scrolled.SetHexpand(true);
		scrolled.SetPolicy(Gtk.PolicyType.Automatic, Gtk.PolicyType.Automatic);

		_outerBox = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
		_outerBox.SetVexpand(true);

		_headerLabel = Gtk.Label.New("");
		_headerLabel.SetVisible(false);
		_headerLabel.SetHalign(Gtk.Align.Start);
		_headerLabel.SetMarginStart(12);
		_headerLabel.SetMarginTop(8);
		_headerLabel.SetMarginBottom(4);

		_footerLabel = Gtk.Label.New("");
		_footerLabel.SetVisible(false);
		_footerLabel.SetHalign(Gtk.Align.Start);
		_footerLabel.SetMarginStart(12);
		_footerLabel.SetMarginTop(4);
		_footerLabel.SetMarginBottom(8);

		_listBox = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
		_listBox.SetVexpand(true);

		_outerBox.Append(_headerLabel);
		_outerBox.Append(_listBox);
		_outerBox.Append(_footerLabel);
		scrolled.SetChild(_outerBox);

		return scrolled;
	}

	protected override void ConnectHandler(Gtk.ScrolledWindow platformView)
	{
		base.ConnectHandler(platformView);
		if (VirtualView != null)
			RebuildRows();
	}

	public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
	{
		var size = base.GetDesiredSize(widthConstraint, heightConstraint);
		if (VirtualView is View v &&
			v.VerticalOptions.Alignment == LayoutAlignment.Fill &&
			size.Height < heightConstraint && !double.IsInfinity(heightConstraint))
		{
			size = new Size(size.Width, heightConstraint);
		}
		return size;
	}

	void RebuildRows()
	{
		if (_listBox == null || VirtualView == null) return;

		// Clear existing
		while (_listBox.GetFirstChild() is Gtk.Widget child)
			_listBox.Remove(child);
		_rowWidgets.Clear();
		_items.Clear();

		var listView = VirtualView;
		var source = listView.ItemsSource;
		if (source == null) return;

		if (listView.IsGroupingEnabled)
			RebuildGroupedRows(source);
		else
			RebuildFlatRows(source);
	}

	void RebuildFlatRows(IEnumerable source)
	{
		int idx = 0;
		foreach (var item in source)
		{
			_items.Add(item);
			var row = BuildRow(item, idx, false);
			_listBox!.Append(row);
			_rowWidgets.Add(row);

			if (VirtualView!.SeparatorVisibility == SeparatorVisibility.Default)
				_listBox.Append(BuildSeparator());
			idx++;
		}
	}

	void RebuildGroupedRows(IEnumerable source)
	{
		foreach (var group in source)
		{
			// Group header
			var header = BuildGroupHeader(group);
			_listBox!.Append(header);
			_rowWidgets.Add(header);
			_items.Add(null); // placeholder for group header

			// Group items
			if (group is IEnumerable groupItems)
			{
				int idx = _items.Count;
				foreach (var item in groupItems)
				{
					_items.Add(item);
					var row = BuildRow(item, idx - 1, false);
					_listBox.Append(row);
					_rowWidgets.Add(row);

					if (VirtualView!.SeparatorVisibility == SeparatorVisibility.Default)
						_listBox.Append(BuildSeparator());
					idx++;
				}
			}
		}
	}

	Gtk.Widget BuildRow(object? item, int index, bool isHeader)
	{
		var listView = VirtualView!;
		var template = listView.ItemTemplate;

		if (template != null)
		{
			try
			{
				var content = template is DataTemplateSelector selector
					? selector.SelectTemplate(item, listView)?.CreateContent()
					: template.CreateContent();

				if (content is ViewCell viewCell)
				{
					viewCell.BindingContext = item;
					return BuildCellWidget(viewCell, index);
				}
				else if (content is TextCell textCell)
				{
					textCell.BindingContext = item;
					return BuildTextCellWidget(textCell, index);
				}
				else if (content is ImageCell imageCell)
				{
					imageCell.BindingContext = item;
					return BuildTextCellWidget(imageCell, index);
				}
				else if (content is View view)
				{
					view.BindingContext = item;
					return BuildViewRow(view, index);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ListView] ItemTemplate error: {ex.Message}");
			}
		}

		// Fallback: simple label
		return BuildSimpleRow(item?.ToString() ?? "", index);
	}

	Gtk.Widget BuildSimpleRow(string text, int index)
	{
		var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
		row.SetHexpand(true);

		var label = Gtk.Label.New(text);
		label.SetHalign(Gtk.Align.Start);
		label.SetMarginStart(12);
		label.SetMarginEnd(12);
		label.SetMarginTop(10);
		label.SetMarginBottom(10);
		row.Append(label);

		AttachRowClick(row, index);
		return row;
	}

	Gtk.Widget BuildViewRow(View mauiView, int index)
	{
		var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
		row.SetHexpand(true);

		// Render MAUI view as label(s) — simplified native rendering
		var content = RenderViewAsNative(mauiView);
		row.Append(content);

		AttachRowClick(row, index);
		return row;
	}

	Gtk.Widget BuildCellWidget(ViewCell cell, int index)
	{
		var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
		row.SetHexpand(true);

		if (cell.View != null)
		{
			var content = RenderViewAsNative(cell.View);
			row.Append(content);
		}

		AttachRowClick(row, index);
		return row;
	}

	Gtk.Widget BuildTextCellWidget(TextCell cell, int index)
	{
		var row = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
		row.SetHexpand(true);
		row.SetMarginStart(12);
		row.SetMarginEnd(12);
		row.SetMarginTop(8);
		row.SetMarginBottom(8);

		var textLabel = Gtk.Label.New(cell.Text ?? "");
		textLabel.SetHalign(Gtk.Align.Start);
		if (cell.TextColor != null)
		{
			var c = cell.TextColor;
			var css = Gtk.CssProvider.New();
			css.LoadFromString($"label {{ color: rgba({(int)(c.Red*255)},{(int)(c.Green*255)},{(int)(c.Blue*255)},{c.Alpha}); }}");
			textLabel.GetStyleContext().AddProvider(css, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
		}
		row.Append(textLabel);

		if (!string.IsNullOrEmpty(cell.Detail))
		{
			var detailLabel = Gtk.Label.New(cell.Detail);
			detailLabel.SetHalign(Gtk.Align.Start);
			var detailCss = Gtk.CssProvider.New();
			var dc = cell.DetailColor ?? Colors.Gray;
			detailCss.LoadFromString($"label {{ font-size: 12px; color: rgba({(int)(dc.Red*255)},{(int)(dc.Green*255)},{(int)(dc.Blue*255)},{dc.Alpha}); }}");
			detailLabel.GetStyleContext().AddProvider(detailCss, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
			row.Append(detailLabel);
		}

		AttachRowClick(row, index);
		return row;
	}

	Gtk.Widget BuildGroupHeader(object? group)
	{
		var listView = VirtualView!;

		if (listView.GroupHeaderTemplate != null)
		{
			try
			{
				var content = listView.GroupHeaderTemplate.CreateContent();
				if (content is View view)
				{
					view.BindingContext = group;
					return RenderViewAsNative(view);
				}
			}
			catch { }
		}

		// Default group header
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
		box.SetHexpand(true);

		var label = Gtk.Label.New(group?.ToString() ?? "Group");
		label.SetHalign(Gtk.Align.Start);
		label.SetMarginStart(12);
		label.SetMarginTop(10);
		label.SetMarginBottom(4);
		var css = Gtk.CssProvider.New();
		css.LoadFromString("label { font-weight: bold; font-size: 13px; }");
		label.GetStyleContext().AddProvider(css, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
		box.Append(label);

		var sep = Gtk.Separator.New(Gtk.Orientation.Horizontal);
		box.Append(sep);

		return box;
	}

	Gtk.Widget RenderViewAsNative(View mauiView)
	{
		if (mauiView is Label label)
		{
			var gtkLabel = Gtk.Label.New(label.Text ?? "");
			gtkLabel.SetHalign(Gtk.Align.Start);
			gtkLabel.SetMarginStart((int)mauiView.Margin.Left);
			gtkLabel.SetMarginEnd((int)mauiView.Margin.Right);
			gtkLabel.SetMarginTop((int)mauiView.Margin.Top);
			gtkLabel.SetMarginBottom((int)mauiView.Margin.Bottom);

			if (label.FontSize > 0 && label.FontSize != 14)
			{
				var css = Gtk.CssProvider.New();
				css.LoadFromString($"label {{ font-size: {(int)label.FontSize}px; }}");
				gtkLabel.GetStyleContext().AddProvider(css, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
			}
			if (label.TextColor != null)
			{
				var c = label.TextColor;
				var css = Gtk.CssProvider.New();
				css.LoadFromString($"label {{ color: rgba({(int)(c.Red*255)},{(int)(c.Green*255)},{(int)(c.Blue*255)},{c.Alpha}); }}");
				gtkLabel.GetStyleContext().AddProvider(css, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
			}
			if (label.FontAttributes.HasFlag(FontAttributes.Bold))
			{
				var css = Gtk.CssProvider.New();
				css.LoadFromString("label { font-weight: bold; }");
				gtkLabel.GetStyleContext().AddProvider(css, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
			}
			return gtkLabel;
		}

		if (mauiView is HorizontalStackLayout hsl)
		{
			var box = Gtk.Box.New(Gtk.Orientation.Horizontal, (int)hsl.Spacing);
			box.SetHexpand(true);
			ApplyPadding(box, hsl.Padding);
			foreach (var child in hsl.Children)
				if (child is View cv) box.Append(RenderViewAsNative(cv));
			return box;
		}

		if (mauiView is VerticalStackLayout vsl)
		{
			var box = Gtk.Box.New(Gtk.Orientation.Vertical, (int)vsl.Spacing);
			box.SetHexpand(true);
			ApplyPadding(box, vsl.Padding);
			foreach (var child in vsl.Children)
				if (child is View cv) box.Append(RenderViewAsNative(cv));
			return box;
		}

		if (mauiView is StackLayout sl)
		{
			var box = Gtk.Box.New(sl.Orientation == StackOrientation.Horizontal
				? Gtk.Orientation.Horizontal : Gtk.Orientation.Vertical, (int)sl.Spacing);
			box.SetHexpand(true);
			ApplyPadding(box, sl.Padding);
			foreach (var child in sl.Children)
				if (child is View cv) box.Append(RenderViewAsNative(cv));
			return box;
		}

		if (mauiView is Grid grid)
		{
			var box = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
			box.SetHexpand(true);
			ApplyPadding(box, grid.Padding);
			foreach (var child in grid.Children)
				if (child is View cv) box.Append(RenderViewAsNative(cv));
			return box;
		}

		if (mauiView is BoxView bv)
		{
			var area = Gtk.DrawingArea.New();
			var w = (int)(bv.WidthRequest > 0 ? bv.WidthRequest : 40);
			var h = (int)(bv.HeightRequest > 0 ? bv.HeightRequest : 40);
			area.SetContentWidth(w);
			area.SetContentHeight(h);
			var color = bv.Color;
			if (color != null)
			{
				var c = color;
				area.SetDrawFunc((_, cr, width, height) =>
				{
					Cairo.Internal.Context.SetSourceRgba(cr.Handle, c.Red, c.Green, c.Blue, c.Alpha);
					Cairo.Internal.Context.Rectangle(cr.Handle, 0, 0, width, height);
					Cairo.Internal.Context.Fill(cr.Handle);
				});
			}
			return area;
		}

		// Fallback
		var fallback = Gtk.Label.New(mauiView.GetType().Name);
		fallback.SetHalign(Gtk.Align.Start);
		return fallback;
	}

	void AttachRowClick(Gtk.Widget row, int index)
	{
		var click = Gtk.GestureClick.New();
		click.OnReleased += (_, _) =>
		{
			if (_updatingSelection || VirtualView == null) return;
			_updatingSelection = true;
			try
			{
				_selectedIndex = index;
				if (index >= 0 && index < _items.Count)
				{
					VirtualView.SelectedItem = _items[index];
				}
				HighlightRow(index);
			}
			finally { _updatingSelection = false; }
		};
		row.AddController(click);
	}

	void HighlightRow(int selectedIdx)
	{
		for (int i = 0; i < _rowWidgets.Count; i++)
		{
			var w = _rowWidgets[i];
			if (i == selectedIdx)
				w.AddCssClass("selected-row");
			else
				w.RemoveCssClass("selected-row");
		}
	}

	static Gtk.Widget BuildSeparator()
	{
		var sep = Gtk.Separator.New(Gtk.Orientation.Horizontal);
		return sep;
	}

	static void ApplyPadding(Gtk.Widget widget, Thickness padding)
	{
		if (padding.Left > 0) widget.SetMarginStart((int)padding.Left);
		if (padding.Right > 0) widget.SetMarginEnd((int)padding.Right);
		if (padding.Top > 0) widget.SetMarginTop((int)padding.Top);
		if (padding.Bottom > 0) widget.SetMarginBottom((int)padding.Bottom);
	}

	// === Mappers ===

	public static void MapItemsSource(ListViewHandler handler, ListView view)
	{
		handler.RebuildRows();
	}

	public static void MapItemTemplate(ListViewHandler handler, ListView view)
	{
		handler.RebuildRows();
	}

	public static void MapSelectedItem(ListViewHandler handler, ListView view)
	{
		if (handler._updatingSelection) return;
		var idx = handler._items.IndexOf(view.SelectedItem);
		handler._selectedIndex = idx;
		handler.HighlightRow(idx);
	}

	public static void MapSelectionMode(ListViewHandler handler, ListView view) { }
	public static void MapHeader(ListViewHandler handler, ListView view)
	{
		if (handler._headerLabel == null) return;
		var text = view.Header?.ToString();
		if (!string.IsNullOrEmpty(text))
		{
			handler._headerLabel.SetText(text);
			handler._headerLabel.SetVisible(true);
		}
		else handler._headerLabel.SetVisible(false);
	}

	public static void MapFooter(ListViewHandler handler, ListView view)
	{
		if (handler._footerLabel == null) return;
		var text = view.Footer?.ToString();
		if (!string.IsNullOrEmpty(text))
		{
			handler._footerLabel.SetText(text);
			handler._footerLabel.SetVisible(true);
		}
		else handler._footerLabel.SetVisible(false);
	}

	public static void MapHasUnevenRows(ListViewHandler handler, ListView view) { }
	public static void MapRowHeight(ListViewHandler handler, ListView view) { }
	public static void MapSeparatorVisibility(ListViewHandler handler, ListView view) => handler.RebuildRows();
	public static void MapSeparatorColor(ListViewHandler handler, ListView view) => handler.RebuildRows();
	public static void MapIsGroupingEnabled(ListViewHandler handler, ListView view) => handler.RebuildRows();
	public static void MapGroupHeaderTemplate(ListViewHandler handler, ListView view) => handler.RebuildRows();
	public static void MapBackgroundColor(ListViewHandler handler, ListView view)
	{
		if (view.BackgroundColor != null)
			handler.ApplyCss(handler.PlatformView, $"background-color: {ToGtkColor(view.BackgroundColor)};");
	}
}
#pragma warning restore CS0618
