using System.Collections;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Platform.Maui.Linux.Gtk4.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

/// <summary>
/// TableView handler for GTK4. Renders MAUI TableView as sections with
/// header labels and cell rows (TextCell, SwitchCell, EntryCell, ViewCell).
/// </summary>
#pragma warning disable CS0618 // TableView is obsolete
public class TableViewHandler : GtkViewHandler<TableView, Gtk.ScrolledWindow>
{
	Gtk.Box? _contentBox;
	readonly List<Gtk.Widget> _sectionWidgets = [];

	public static new IPropertyMapper<TableView, TableViewHandler> Mapper =
		new PropertyMapper<TableView, TableViewHandler>(ViewMapper)
		{
			[nameof(TableView.Root)] = MapRoot,
			[nameof(TableView.Intent)] = MapIntent,
			[nameof(TableView.RowHeight)] = MapRowHeight,
			[nameof(TableView.HasUnevenRows)] = MapHasUnevenRows,
			[nameof(IView.Background)] = MapBackgroundColor,
			["BackgroundColor"] = MapBackgroundColor,
		};

	public TableViewHandler() : base(Mapper) { }

	protected override Gtk.ScrolledWindow CreatePlatformView()
	{
		var scrolled = Gtk.ScrolledWindow.New();
		scrolled.SetVexpand(true);
		scrolled.SetHexpand(true);
		scrolled.SetPolicy(Gtk.PolicyType.Automatic, Gtk.PolicyType.Automatic);

		_contentBox = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
		_contentBox.SetVexpand(true);
		scrolled.SetChild(_contentBox);

		return scrolled;
	}

	protected override void ConnectHandler(Gtk.ScrolledWindow platformView)
	{
		base.ConnectHandler(platformView);
		if (VirtualView?.Root != null)
			RebuildSections();
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

	void RebuildSections()
	{
		if (_contentBox == null || VirtualView?.Root == null) return;

		// Clear
		while (_contentBox.GetFirstChild() is Gtk.Widget child)
			_contentBox.Remove(child);
		_sectionWidgets.Clear();

		foreach (var section in VirtualView.Root)
		{
			// Section header
			if (!string.IsNullOrEmpty(section.Title))
			{
				var header = Gtk.Label.New(section.Title);
				header.SetHalign(Gtk.Align.Start);
				header.SetMarginStart(12);
				header.SetMarginTop(16);
				header.SetMarginBottom(4);
				var css = Gtk.CssProvider.New();
				css.LoadFromString("label { font-weight: bold; font-size: 13px; color: @theme_selected_bg_color; }");
				header.GetStyleContext().AddProvider(css, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
				_contentBox.Append(header);
				_sectionWidgets.Add(header);

				var sep = Gtk.Separator.New(Gtk.Orientation.Horizontal);
				_contentBox.Append(sep);
			}

			// Section cells
			foreach (var cell in section)
			{
				var row = BuildCellRow(cell);
				_contentBox.Append(row);
				_sectionWidgets.Add(row);

				var rowSep = Gtk.Separator.New(Gtk.Orientation.Horizontal);
				_contentBox.Append(rowSep);
			}
		}
	}

	Gtk.Widget BuildCellRow(Cell cell)
	{
		return cell switch
		{
			SwitchCell sc => BuildSwitchCell(sc),
			EntryCell ec => BuildEntryCell(ec),
			TextCell tc => BuildTextCell(tc),
			ViewCell vc => BuildViewCell(vc),
			_ => BuildFallbackCell(cell),
		};
	}

	Gtk.Widget BuildTextCell(TextCell cell)
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
		box.SetHexpand(true);
		box.SetMarginStart(12);
		box.SetMarginEnd(12);
		box.SetMarginTop(8);
		box.SetMarginBottom(8);

		var textLabel = Gtk.Label.New(cell.Text ?? "");
		textLabel.SetHalign(Gtk.Align.Start);
		if (cell.TextColor != null)
		{
			var c = cell.TextColor;
			var css = Gtk.CssProvider.New();
			css.LoadFromString($"label {{ color: rgba({(int)(c.Red*255)},{(int)(c.Green*255)},{(int)(c.Blue*255)},{c.Alpha}); }}");
			textLabel.GetStyleContext().AddProvider(css, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
		}
		box.Append(textLabel);

		if (!string.IsNullOrEmpty(cell.Detail))
		{
			var detailLabel = Gtk.Label.New(cell.Detail);
			detailLabel.SetHalign(Gtk.Align.Start);
			var dc = cell.DetailColor ?? Colors.Gray;
			var css = Gtk.CssProvider.New();
			css.LoadFromString($"label {{ font-size: 12px; color: rgba({(int)(dc.Red*255)},{(int)(dc.Green*255)},{(int)(dc.Blue*255)},{dc.Alpha}); }}");
			detailLabel.GetStyleContext().AddProvider(css, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
			box.Append(detailLabel);
		}

		// TextCell tapped
		var click = Gtk.GestureClick.New();
		click.OnReleased += (_, _) => cell.Command?.Execute(cell.CommandParameter);
		box.AddController(click);

		return box;
	}

	Gtk.Widget BuildSwitchCell(SwitchCell cell)
	{
		var box = Gtk.Box.New(Gtk.Orientation.Horizontal, 12);
		box.SetHexpand(true);
		box.SetMarginStart(12);
		box.SetMarginEnd(12);
		box.SetMarginTop(6);
		box.SetMarginBottom(6);

		var label = Gtk.Label.New(cell.Text ?? "");
		label.SetHalign(Gtk.Align.Start);
		label.SetHexpand(true);
		box.Append(label);

		var toggle = Gtk.Switch.New();
		toggle.SetActive(cell.On);
		toggle.SetValign(Gtk.Align.Center);
		toggle.OnNotify += (sender, args) =>
		{
			if (args.Pspec.GetName() == "active")
				cell.On = toggle.GetActive();
		};
		box.Append(toggle);

		return box;
	}

	Gtk.Widget BuildEntryCell(EntryCell cell)
	{
		var box = Gtk.Box.New(Gtk.Orientation.Horizontal, 12);
		box.SetHexpand(true);
		box.SetMarginStart(12);
		box.SetMarginEnd(12);
		box.SetMarginTop(6);
		box.SetMarginBottom(6);

		if (!string.IsNullOrEmpty(cell.Label))
		{
			var label = Gtk.Label.New(cell.Label);
			label.SetHalign(Gtk.Align.Start);
			box.Append(label);
		}

		var entry = Gtk.Entry.New();
		entry.SetHexpand(true);
		if (!string.IsNullOrEmpty(cell.Text))
			entry.SetText(cell.Text);
		if (!string.IsNullOrEmpty(cell.Placeholder))
			entry.SetPlaceholderText(cell.Placeholder);

		var buffer = entry.GetBuffer();
		buffer.OnNotify += (sender, args) =>
		{
			if (args.Pspec.GetName() == "text")
				cell.Text = buffer.GetText();
		};
		box.Append(entry);

		return box;
	}

	Gtk.Widget BuildViewCell(ViewCell cell)
	{
		if (cell.View != null)
			return RenderViewAsNative(cell.View);

		return BuildFallbackCell(cell);
	}

	Gtk.Widget BuildFallbackCell(Cell cell)
	{
		var label = Gtk.Label.New(cell.ToString() ?? "Cell");
		label.SetHalign(Gtk.Align.Start);
		label.SetMarginStart(12);
		label.SetMarginTop(8);
		label.SetMarginBottom(8);
		return label;
	}

	Gtk.Widget RenderViewAsNative(View mauiView)
	{
		if (mauiView is Label label)
		{
			var gtkLabel = Gtk.Label.New(label.Text ?? "");
			gtkLabel.SetHalign(Gtk.Align.Start);
			ApplyMargin(gtkLabel, mauiView.Margin);
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

		// Fallback
		var fallback = Gtk.Label.New(mauiView.GetType().Name);
		fallback.SetHalign(Gtk.Align.Start);
		return fallback;
	}

	static void ApplyPadding(Gtk.Widget widget, Thickness padding)
	{
		if (padding.Left > 0) widget.SetMarginStart((int)padding.Left);
		if (padding.Right > 0) widget.SetMarginEnd((int)padding.Right);
		if (padding.Top > 0) widget.SetMarginTop((int)padding.Top);
		if (padding.Bottom > 0) widget.SetMarginBottom((int)padding.Bottom);
	}

	static void ApplyMargin(Gtk.Widget widget, Thickness margin)
	{
		if (margin.Left > 0) widget.SetMarginStart((int)margin.Left);
		if (margin.Right > 0) widget.SetMarginEnd((int)margin.Right);
		if (margin.Top > 0) widget.SetMarginTop((int)margin.Top);
		if (margin.Bottom > 0) widget.SetMarginBottom((int)margin.Bottom);
	}

	// === Mappers ===

	public static void MapRoot(TableViewHandler handler, TableView view) => handler.RebuildSections();
	public static void MapIntent(TableViewHandler handler, TableView view) { }
	public static void MapRowHeight(TableViewHandler handler, TableView view) { }
	public static void MapHasUnevenRows(TableViewHandler handler, TableView view) { }
	public static void MapBackgroundColor(TableViewHandler handler, TableView view)
	{
		if (view.BackgroundColor != null)
			handler.ApplyCss(handler.PlatformView, $"background-color: {ToGtkColor(view.BackgroundColor)};");
	}
}
#pragma warning restore CS0618
