using System.Collections.Specialized;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Handlers;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

/// <summary>
/// ShellHandler for GTK4. Implements Shell navigation using:
/// - Gtk.Paned for flyout sidebar | content split
/// - Gtk.ListBox for flyout menu items
/// - Gtk.Notebook for section tabs
/// - Gtk.Stack for page navigation within sections
/// </summary>
public partial class ShellHandler : GtkViewHandler<Shell, Gtk.Box>
{
	Gtk.Paned? _paned;
	Gtk.ListBox? _flyoutListBox;
	Gtk.Box? _flyoutBox;
	Gtk.Notebook? _notebook;
	Gtk.Label? _flyoutHeaderLabel;
	Gtk.Label? _flyoutFooterLabel;
	int _flyoutWidth = 250;
	bool _updatingSelection;

	public static new IPropertyMapper<Shell, ShellHandler> Mapper =
		new PropertyMapper<Shell, ShellHandler>(ViewMapper)
		{
			[nameof(Shell.CurrentItem)] = MapCurrentItem,
			[nameof(Shell.Items)] = MapItems,
			[nameof(Shell.FlyoutItems)] = MapFlyoutItems,
			[nameof(Shell.FlyoutHeader)] = MapFlyoutHeader,
			[nameof(Shell.FlyoutHeaderTemplate)] = MapFlyoutHeader,
			[nameof(Shell.FlyoutFooter)] = MapFlyoutFooter,
			[nameof(Shell.FlyoutFooterTemplate)] = MapFlyoutFooter,
			[nameof(Shell.FlyoutBackground)] = MapFlyoutBackground,
			[nameof(Shell.FlyoutBackgroundColor)] = MapFlyoutBackground,
			[nameof(Shell.FlyoutBehavior)] = MapFlyoutBehavior,
			[nameof(Shell.FlyoutIsPresented)] = MapFlyoutIsPresented,
			[nameof(Shell.FlyoutWidth)] = MapFlyoutWidth,
			[nameof(Shell.FlyoutIcon)] = MapFlyoutIcon,
		};

	public static CommandMapper<Shell, ShellHandler> CommandMapper =
		new(ViewCommandMapper);

	public ShellHandler() : base(Mapper, CommandMapper) { }

	protected override Gtk.Box CreatePlatformView()
	{
		var root = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
		root.SetVexpand(true);
		root.SetHexpand(true);

		_paned = Gtk.Paned.New(Gtk.Orientation.Horizontal);
		_paned.SetVexpand(true);
		_paned.SetHexpand(true);
		_paned.SetPosition(_flyoutWidth);
		_paned.SetResizeStartChild(false);
		_paned.SetShrinkStartChild(false);
		_paned.SetResizeEndChild(true);
		_paned.SetShrinkEndChild(false);

		// Flyout sidebar
		_flyoutBox = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
		_flyoutBox.SetVexpand(true);
		_flyoutBox.SetSizeRequest(_flyoutWidth, -1);

		_flyoutHeaderLabel = Gtk.Label.New("");
		_flyoutHeaderLabel.SetVisible(false);
		_flyoutHeaderLabel.SetMarginTop(12);
		_flyoutHeaderLabel.SetMarginBottom(8);
		_flyoutHeaderLabel.SetMarginStart(12);
		_flyoutHeaderLabel.SetXalign(0);
		_flyoutBox.Append(_flyoutHeaderLabel);

		_flyoutListBox = Gtk.ListBox.New();
		_flyoutListBox.SetVexpand(true);
		_flyoutListBox.SetSelectionMode(Gtk.SelectionMode.Single);
		_flyoutListBox.AddCssClass("navigation-sidebar");
		_flyoutListBox.OnRowSelected += OnFlyoutRowSelected;
		_flyoutBox.Append(_flyoutListBox);

		_flyoutFooterLabel = Gtk.Label.New("");
		_flyoutFooterLabel.SetVisible(false);
		_flyoutFooterLabel.SetMarginTop(8);
		_flyoutFooterLabel.SetMarginBottom(12);
		_flyoutFooterLabel.SetMarginStart(12);
		_flyoutFooterLabel.SetXalign(0);
		_flyoutFooterLabel.AddCssClass("dim-label");
		_flyoutBox.Append(_flyoutFooterLabel);

		_paned.SetStartChild(_flyoutBox);

		// Content area - notebook for tabs
		_notebook = Gtk.Notebook.New();
		_notebook.SetVexpand(true);
		_notebook.SetHexpand(true);
		_notebook.OnSwitchPage += OnNotebookPageSwitched;
		_paned.SetEndChild(_notebook);

		root.Append(_paned);
		return root;
	}

	protected override void ConnectHandler(Gtk.Box platformView)
	{
		base.ConnectHandler(platformView);

		// Add a CSS class to scope the paned background fix to Shell only
		if (_paned != null)
		{
			_paned.AddCssClass("maui-shell-paned");
			var display = Gdk.Display.GetDefault();
			if (display != null)
			{
				var provider = Gtk.CssProvider.New();
				provider.LoadFromString(
					"paned.maui-shell-paned > * { background: none; background-image: none; }");
				Gtk.StyleContext.AddProviderForDisplay(display, provider,
					Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION + 1);
			}
		}

		if (VirtualView != null)
		{
			RebuildFlyoutItems();
			RebuildTabs();
		}
	}

	protected override void DisconnectHandler(Gtk.Box platformView)
	{
		if (_flyoutListBox != null)
			_flyoutListBox.OnRowSelected -= OnFlyoutRowSelected;
		if (_notebook != null)
			_notebook.OnSwitchPage -= OnNotebookPageSwitched;
		base.DisconnectHandler(platformView);
	}

	void OnFlyoutRowSelected(Gtk.ListBox sender, Gtk.ListBox.RowSelectedSignalArgs args)
	{
		if (_updatingSelection || VirtualView == null) return;

		var row = args.Row;
		if (row == null) return;

		int index = row.GetIndex();
		var items = VirtualView.Items;
		if (index >= 0 && index < items.Count)
		{
			_updatingSelection = true;
			try
			{
				VirtualView.CurrentItem = items[index];
			}
			finally
			{
				_updatingSelection = false;
			}
		}
	}

	void OnNotebookPageSwitched(Gtk.Notebook sender, Gtk.Notebook.SwitchPageSignalArgs args)
	{
		if (_updatingSelection || VirtualView?.CurrentItem == null) return;

		var idx = (int)args.PageNum;
		var sections = VirtualView.CurrentItem.Items;
		if (idx >= 0 && idx < sections.Count)
		{
			_updatingSelection = true;
			try
			{
				VirtualView.CurrentItem.CurrentItem = sections[idx];
			}
			finally
			{
				_updatingSelection = false;
			}
		}
	}

	void RebuildFlyoutItems()
	{
		if (_flyoutListBox == null || VirtualView == null) return;

		// Clear existing rows
		while (_flyoutListBox.GetFirstChild() is Gtk.Widget child)
			_flyoutListBox.Remove(child);

		int selectedIdx = -1;
		int idx = 0;
		foreach (var item in VirtualView.Items)
		{
			var label = Gtk.Label.New(item.Title ?? $"Item {idx + 1}");
			label.SetXalign(0);
			label.SetMarginStart(12);
			label.SetMarginEnd(12);
			label.SetMarginTop(8);
			label.SetMarginBottom(8);

			_flyoutListBox.Append(label);

			if (item == VirtualView.CurrentItem)
				selectedIdx = idx;
			idx++;
		}

		// Select current item
		if (selectedIdx >= 0)
		{
			_updatingSelection = true;
			var row = _flyoutListBox.GetRowAtIndex(selectedIdx);
			if (row != null)
				_flyoutListBox.SelectRow(row);
			_updatingSelection = false;
		}

		// Hide flyout if only one item (or FlyoutBehavior says so)
		UpdateFlyoutVisibility();
	}

	void RebuildTabs()
	{
		if (_notebook == null || VirtualView?.CurrentItem == null || MauiContext == null)
			return;

		// Clear existing tabs
		while (_notebook.GetNPages() > 0)
			_notebook.RemovePage(0);

		var shellItem = VirtualView.CurrentItem;
		bool singleSection = shellItem.Items.Count <= 1;

		foreach (var section in shellItem.Items)
		{
			var page = GetCurrentPage(section);
			if (page == null) continue;

			var platformPage = (Gtk.Widget)page.ToPlatform(MauiContext);
			platformPage.SetVexpand(true);
			platformPage.SetHexpand(true);

			var tabLabel = Gtk.Label.New(section.Title ?? page.Title ?? "Tab");
			_notebook.AppendPage(platformPage, tabLabel);
		}

		// Hide tab strip if only one section
		_notebook.SetShowTabs(!singleSection);

		// Select the current section
		if (shellItem.CurrentItem != null)
		{
			int sectionIdx = shellItem.Items.IndexOf(shellItem.CurrentItem);
			if (sectionIdx >= 0 && sectionIdx < _notebook.GetNPages())
			{
				_updatingSelection = true;
				_notebook.SetCurrentPage(sectionIdx);
				_updatingSelection = false;
			}
		}
	}

	static Page? GetCurrentPage(ShellSection section)
	{
		if (section.CurrentItem is ShellContent content)
		{
			return (content as IShellContentController)?.GetOrCreateContent();
		}
		return null;
	}

	void UpdateFlyoutVisibility()
	{
		if (_flyoutBox == null || _paned == null || VirtualView == null) return;

		bool showFlyout = VirtualView.FlyoutBehavior != FlyoutBehavior.Disabled
			&& VirtualView.Items.Count > 1;

		// On desktop GTK, always present the flyout as a sidebar when enabled.
		// FlyoutIsPresented defaults to false (mobile pattern), but desktop
		// should show the sidebar by default.
		if (showFlyout && !VirtualView.FlyoutIsPresented
			&& VirtualView.FlyoutBehavior != FlyoutBehavior.Disabled)
		{
			VirtualView.FlyoutIsPresented = true;
		}

		bool visible = showFlyout && VirtualView.FlyoutIsPresented;
		_flyoutBox.SetVisible(visible);
		_paned.SetPosition(visible ? _flyoutWidth : 0);
	}

	// === Mapper Methods ===

	public static void MapCurrentItem(ShellHandler handler, Shell shell)
	{
		handler.RebuildTabs();

		// Update flyout selection
		if (handler._flyoutListBox != null && shell.CurrentItem != null)
		{
			int idx = shell.Items.IndexOf(shell.CurrentItem);
			if (idx >= 0)
			{
				handler._updatingSelection = true;
				var row = handler._flyoutListBox.GetRowAtIndex(idx);
				if (row != null)
					handler._flyoutListBox.SelectRow(row);
				handler._updatingSelection = false;
			}
		}
	}

	public static void MapItems(ShellHandler handler, Shell shell)
	{
		handler.RebuildFlyoutItems();
		handler.RebuildTabs();
	}

	public static void MapFlyoutItems(ShellHandler handler, Shell shell)
	{
		handler.RebuildFlyoutItems();
	}

	public static void MapFlyoutHeader(ShellHandler handler, Shell shell)
	{
		if (handler._flyoutHeaderLabel == null) return;

		string text = "";
		if (shell.FlyoutHeader is View headerView)
			text = headerView.ToString() ?? "";
		else if (shell.FlyoutHeader is string s)
			text = s;
		else if (shell.FlyoutHeader != null)
			text = shell.FlyoutHeader.ToString() ?? "";

		if (!string.IsNullOrEmpty(text))
		{
			handler._flyoutHeaderLabel.SetText(text);
			handler._flyoutHeaderLabel.SetVisible(true);
			var css = "label { font-weight: bold; font-size: 16px; }";
			handler.ApplyCss(handler._flyoutHeaderLabel, css);
		}
		else
		{
			handler._flyoutHeaderLabel.SetVisible(false);
		}
	}

	public static void MapFlyoutFooter(ShellHandler handler, Shell shell)
	{
		if (handler._flyoutFooterLabel == null) return;

		string text = "";
		if (shell.FlyoutFooter is string s) text = s;
		else if (shell.FlyoutFooter != null) text = shell.FlyoutFooter.ToString() ?? "";

		if (!string.IsNullOrEmpty(text))
		{
			handler._flyoutFooterLabel.SetText(text);
			handler._flyoutFooterLabel.SetVisible(true);
		}
		else
		{
			handler._flyoutFooterLabel.SetVisible(false);
		}
	}

	public static void MapFlyoutBackground(ShellHandler handler, Shell shell)
	{
		if (handler._flyoutBox == null) return;

		var color = shell.FlyoutBackgroundColor;
		if (color != null)
			handler.ApplyCss(handler._flyoutBox, $"background-color: {ToGtkColor(color)};");
	}

	public static void MapFlyoutBehavior(ShellHandler handler, Shell shell)
	{
		handler.UpdateFlyoutVisibility();
	}

	public static void MapFlyoutIsPresented(ShellHandler handler, Shell shell)
	{
		handler.UpdateFlyoutVisibility();
	}

	public static void MapFlyoutWidth(ShellHandler handler, Shell shell)
	{
		if (shell.FlyoutWidth > 0)
		{
			handler._flyoutWidth = (int)shell.FlyoutWidth;
			handler._flyoutBox?.SetSizeRequest(handler._flyoutWidth, -1);
			handler._paned?.SetPosition(handler._flyoutWidth);
		}
	}

	public static void MapFlyoutIcon(ShellHandler handler, Shell shell)
	{
		// On GTK desktop, flyout icon is not typically shown separately (handled by sidebar toggle)
	}
}
