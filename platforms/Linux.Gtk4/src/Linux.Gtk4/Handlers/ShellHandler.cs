using System.Collections.Specialized;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Handlers;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;

/// <summary>
/// ShellHandler for GTK4. Implements Shell navigation using:
/// - Gtk.Paned for flyout sidebar | content split
/// - Gtk.ListBox for flyout menu items
/// - Gtk.Notebook for section tabs
/// - A stable Gtk.Box per section for its current navigation page
/// </summary>
public partial class ShellHandler : GtkViewHandler<Shell, Gtk.Box>
{
	Gtk.Paned? _paned;
	Gtk.CssProvider? _displayCssProvider;
	Gtk.ListBox? _flyoutListBox;
	Gtk.Box? _flyoutBox;
	Gtk.Notebook? _notebook;
	Gtk.Label? _flyoutHeaderLabel;
	Gtk.Label? _flyoutFooterLabel;
	int _flyoutWidth = 250;
	bool _updatingSelection;
	readonly List<Gtk.Widget> _registeredFlyoutElements = new();
	readonly List<Gtk.Widget> _registeredTabElements = new();
	readonly List<ShellSectionNavigationObserver> _sectionObservers = new();
	readonly List<Gtk.Box> _sectionContainers = new();
	Shell? _observedShell;
	int _sectionGeneration;

	public static IPropertyMapper<Shell, ShellHandler> Mapper =
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

	public override void SetVirtualView(IView view)
	{
		base.SetVirtualView(view);
		ObserveShell(VirtualView);
	}

	void ObserveShell(Shell? shell)
	{
		if (_observedShell == shell)
			return;
		if (_observedShell != null)
			_observedShell.Navigated -= OnShellNavigated;
		_observedShell = shell;
		if (_observedShell != null)
			_observedShell.Navigated += OnShellNavigated;
	}

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
				_displayCssProvider = Gtk.CssProvider.New();
				_displayCssProvider.LoadFromString(
					"paned.maui-shell-paned > * { background: none; background-image: none; }");
				Gtk.StyleContext.AddProviderForDisplay(display, _displayCssProvider,
					Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION + 1);
			}
		}

		if (VirtualView != null)
		{
			ObserveShell(VirtualView);
			RebuildFlyoutItems();
			RebuildTabs();
		}
	}

	protected override void DisconnectHandler(Gtk.Box platformView)
	{
		ObserveShell(null);
		ClearSectionObservers();
		UnregisterNativeElements(_registeredFlyoutElements);
		UnregisterNativeElements(_registeredTabElements);
		if (_flyoutListBox != null)
			_flyoutListBox.OnRowSelected -= OnFlyoutRowSelected;
		if (_notebook != null)
			_notebook.OnSwitchPage -= OnNotebookPageSwitched;
		if (_displayCssProvider != null)
		{
			var display = Gdk.Display.GetDefault();
			if (display != null)
				Gtk.StyleContext.RemoveProviderForDisplay(display, _displayCssProvider);
			_displayCssProvider = null;
		}
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

		UnregisterNativeElements(_registeredFlyoutElements);
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
			var row = _flyoutListBox.GetRowAtIndex(idx);
			if (row != null)
				RegisterNativeElement(item, row, "ShellFlyout", _registeredFlyoutElements);

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
		if (_notebook == null || MauiContext == null)
			return;

		var wasUpdatingSelection = _updatingSelection;
		_updatingSelection = true;
		try
		{
			ClearSectionObservers();
			var generation = _sectionGeneration;
			UnregisterNativeElements(_registeredTabElements);
			while (_notebook.GetNPages() > 0)
				_notebook.RemovePage(0);

			var shellItem = VirtualView?.CurrentItem;
			if (shellItem == null)
				return;

			foreach (var section in shellItem.Items)
			{
				var container = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
				container.SetVexpand(true);
				container.SetHexpand(true);
				_sectionContainers.Add(container);
				var tabLabel = Gtk.Label.New(section.Title ?? "Tab");
				_notebook.AppendPage(container, tabLabel);
				RegisterNativeElement(section, tabLabel, "ShellTab", _registeredTabElements);
				var displayVersion = 0;
				var observer = new ShellSectionNavigationObserver(section, page =>
				{
					var version = ++displayVersion;
					bool IsCurrent() => generation == _sectionGeneration && version == displayVersion;
					DisplaySectionPage(container, page, IsCurrent);
					if (IsCurrent())
						tabLabel.SetText(section.Title ?? page?.Title ?? "Tab");
				});
				_sectionObservers.Add(observer);
				observer.Refresh();
				if (generation != _sectionGeneration)
					return;
			}

			_notebook.SetShowTabs(shellItem.Items.Count > 1);

			if (shellItem.CurrentItem != null)
			{
				int sectionIdx = shellItem.Items.IndexOf(shellItem.CurrentItem);
				if (sectionIdx >= 0 && sectionIdx < _notebook.GetNPages())
					_notebook.SetCurrentPage(sectionIdx);
			}
		}
		finally
		{
			_updatingSelection = wasUpdatingSelection;
		}
	}

	void DisplaySectionPage(Gtk.Box container, Page? page, Func<bool> isCurrent)
	{
		if (!isCurrent())
			return;
		var platformPage = page == null ? null : (Gtk.Widget)page.ToPlatform(MauiContext!);
		// Handler creation can run application code which navigates or disconnects Shell.
		if (!isCurrent())
			return;
		if (container.GetFirstChild() == platformPage)
			return;

		while (container.GetFirstChild() is Gtk.Widget child)
			container.Remove(child);

		if (platformPage != null)
		{
			platformPage.SetVexpand(true);
			platformPage.SetHexpand(true);
			container.Append(platformPage);
		}
	}

	void OnShellNavigated(object? sender, ShellNavigatedEventArgs args)
	{
		foreach (var observer in _sectionObservers.ToArray())
			observer.Refresh();
	}

	void ClearSectionObservers()
	{
		_sectionGeneration++;
		foreach (var observer in _sectionObservers)
			observer.Dispose();
		_sectionObservers.Clear();
		foreach (var container in _sectionContainers)
		{
			while (container.GetFirstChild() is Gtk.Widget child)
				container.Remove(child);
		}
		_sectionContainers.Clear();
	}

	void RegisterNativeElement(
		object owner,
		Gtk.Widget nativeElement,
		string role,
		List<Gtk.Widget> registrations)
	{
		NativeElementDiagnosticsBridge.Register(owner, nativeElement, role);
		registrations.Add(nativeElement);
	}

	static void UnregisterNativeElements(List<Gtk.Widget> registrations)
	{
		foreach (var nativeElement in registrations)
			NativeElementDiagnosticsBridge.Unregister(nativeElement);
		registrations.Clear();
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
			var css = "font-weight: bold; font-size: 16px;";
			handler.UpdateCss(handler._flyoutHeaderLabel, css);
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
		handler.UpdateCss(handler._flyoutBox, color != null ? $"background-color: {ToGtkColor(color)};" : null);
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
