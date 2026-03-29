using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using Platform.Maui.Linux.Gtk4.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class NavigationPageHandler : GtkViewHandler<IStackNavigationView, Gtk.Box>
{
	Gtk.Box? _navBar;
	Gtk.Button? _backButton;
	Gtk.Button? _flyoutToggle;
	Gtk.Label? _titleLabel;
	Gtk.Box? _toolbarBox; // Right-aligned toolbar buttons
	Gtk.Stack? _stack;
	IReadOnlyList<IView>? _currentStack;

	public static new IPropertyMapper<IStackNavigationView, NavigationPageHandler> Mapper =
		new PropertyMapper<IStackNavigationView, NavigationPageHandler>(ViewMapper)
		{
		};

	public static CommandMapper<IStackNavigationView, NavigationPageHandler> CommandMapper = new(ViewCommandMapper)
	{
		[nameof(IStackNavigation.RequestNavigation)] = MapRequestNavigation,
	};

	public NavigationPageHandler() : base(Mapper, CommandMapper)
	{
	}

	protected override Gtk.Box CreatePlatformView()
	{
		var container = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
		container.SetVexpand(true);
		container.SetHexpand(true);

		// Navigation bar
		_navBar = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
		_navBar.SetName("maui-nav-bar");

		// Flyout toggle — hidden by default, shown when inside a FlyoutPage
		_flyoutToggle = Gtk.Button.New();
		_flyoutToggle.SetLabel("☰");
		_flyoutToggle.AddCssClass("flat");
		_flyoutToggle.SetTooltipText("Toggle sidebar");
		_flyoutToggle.SetVisible(false);
		_flyoutToggle.OnClicked += OnFlyoutToggleClicked;
		_navBar.Append(_flyoutToggle);

		_backButton = Gtk.Button.New();
		_backButton.SetLabel("◀ Back");
		_backButton.SetVisible(false);
		_backButton.OnClicked += OnBackClicked;
		_backButton.AddCssClass("flat");
		_navBar.Append(_backButton);

		_titleLabel = Gtk.Label.New("");
		_titleLabel.SetHexpand(true);
		_titleLabel.SetXalign(0);
		_navBar.Append(_titleLabel);

		// Toolbar buttons container (right-aligned)
		_toolbarBox = Gtk.Box.New(Gtk.Orientation.Horizontal, 4);
		_navBar.Append(_toolbarBox);

		// Style the nav bar
		ApplyNavBarStyle();

		container.Append(_navBar);

		// Separator below nav bar
		var sep = Gtk.Separator.New(Gtk.Orientation.Horizontal);
		container.Append(sep);

		// Page content stack
		_stack = Gtk.Stack.New();
		_stack.SetTransitionType(Gtk.StackTransitionType.SlideLeftRight);
		_stack.SetTransitionDuration(250);
		_stack.SetVexpand(true);
		_stack.SetHexpand(true);
		container.Append(_stack);

		return container;
	}

	void ApplyNavBarStyle()
	{
		if (_navBar == null) return;

		var css = "padding: 8px 12px;";

		if (VirtualView is NavigationPage navPage)
		{
			if (navPage.BarBackgroundColor != null)
				css += $" background-color: {ToGtkColor(navPage.BarBackgroundColor)};";

			if (navPage.BarTextColor != null && _titleLabel != null)
			{
				var titleCss = $"color: {ToGtkColor(navPage.BarTextColor)};";
				ApplyCss(_titleLabel, titleCss);
			}

			if (navPage.BarTextColor != null && _backButton != null)
			{
				var btnCss = $"color: {ToGtkColor(navPage.BarTextColor)};";
				ApplyCss(_backButton, btnCss);
			}
		}

		ApplyCss(_navBar, css);
	}

	void OnBackClicked(Gtk.Button sender, EventArgs args)
	{
		if (VirtualView is NavigationPage navPage && navPage.Navigation.NavigationStack.Count > 1)
		{
			_ = navPage.PopAsync();
		}
	}

	void OnFlyoutToggleClicked(Gtk.Button sender, EventArgs args)
	{
		// Walk up the MAUI visual tree to find the FlyoutPage and toggle IsPresented
		if (VirtualView is NavigationPage navPage)
		{
			var parent = (navPage as Element)?.Parent;
			while (parent != null)
			{
				if (parent is FlyoutPage flyoutPage)
				{
					flyoutPage.IsPresented = !flyoutPage.IsPresented;
					return;
				}
				parent = parent.Parent;
			}
		}
	}

	protected override void ConnectHandler(Gtk.Box platformView)
	{
		base.ConnectHandler(platformView);

		// Detect if inside a FlyoutPage and show the hamburger toggle
		if (VirtualView is NavigationPage navPage)
		{
			var parent = (navPage as Element)?.Parent;
			while (parent != null)
			{
				if (parent is FlyoutPage)
				{
					_flyoutToggle?.SetVisible(true);
					break;
				}
				parent = parent.Parent;
			}
		}
	}

	protected override void DisconnectHandler(Gtk.Box platformView)
	{
		if (_backButton != null)
			_backButton.OnClicked -= OnBackClicked;
		if (_flyoutToggle != null)
			_flyoutToggle.OnClicked -= OnFlyoutToggleClicked;

		base.DisconnectHandler(platformView);
	}

	public static void MapRequestNavigation(NavigationPageHandler handler, IStackNavigationView view, object? arg)
	{
		if (arg is NavigationRequest request)
		{
			handler.HandleNavigationRequest(request);
		}
	}

	void HandleNavigationRequest(NavigationRequest request)
	{
		_ = MauiContext ?? throw new InvalidOperationException("MauiContext not set.");

		if (_stack == null) return;

		var newNames = new HashSet<string>();

		// Add new pages to the stack
		foreach (var page in request.NavigationStack)
		{
			var name = page.GetHashCode().ToString();
			newNames.Add(name);
			if (_stack.GetChildByName(name) == null)
			{
				var platformPage = (Gtk.Widget)page.ToPlatform(MauiContext);
				_stack.AddNamed(platformPage, name);
			}
		}

		// Show the top page
		if (request.NavigationStack.Count > 0)
		{
			var topPage = request.NavigationStack[^1];
			var name = topPage.GetHashCode().ToString();
			_stack.SetVisibleChildName(name);

			UpdateNavBar(topPage, request.NavigationStack.Count);
		}

		// Remove pages no longer in the stack (after pop)
		if (_currentStack != null)
		{
			foreach (var oldPage in _currentStack)
			{
				var oldName = oldPage.GetHashCode().ToString();
				if (!newNames.Contains(oldName))
				{
					var child = _stack.GetChildByName(oldName);
					if (child != null)
						_stack.Remove(child);
				}
			}
		}

		_currentStack = request.NavigationStack;

		// Notify MAUI that navigation is complete
		((IStackNavigation)VirtualView).NavigationFinished(request.NavigationStack);
	}

	void UpdateNavBar(IView topPage, int stackDepth)
	{
		// Update back button visibility
		if (_backButton != null)
		{
			var showBack = stackDepth > 1;

			// Check HasBackButton attached property
			if (topPage is Page mauiPage)
				showBack = showBack && NavigationPage.GetHasBackButton(mauiPage);

			_backButton.SetVisible(showBack);
		}

		// Update title
		if (_titleLabel != null)
		{
			var title = (topPage as Page)?.Title ?? "";
			_titleLabel.SetLabel(title);

			// Bold title via Pango markup
			var escaped = GLib.Functions.MarkupEscapeText(title, -1);
			_titleLabel.SetMarkup($"<b>{escaped}</b>");
		}

		// Check HasNavigationBar attached property
		if (_navBar != null && topPage is Page page)
		{
			_navBar.SetVisible(NavigationPage.GetHasNavigationBar(page));
			// Also hide the separator
			var sep = _navBar.GetNextSibling();
			if (sep is Gtk.Separator separator)
				separator.SetVisible(NavigationPage.GetHasNavigationBar(page));
		}

		// Update ToolbarItems in the nav bar
		UpdateToolbarItems(topPage as Page);

		// Update MenuBar on the window
		UpdateMenuBar(topPage as Page);
	}

	void UpdateToolbarItems(Page? page)
	{
		if (_toolbarBox == null) return;

		// Clear existing toolbar buttons
		while (_toolbarBox.GetFirstChild() is Gtk.Widget child)
			_toolbarBox.Remove(child);

		if (page?.ToolbarItems == null || page.ToolbarItems.Count == 0)
			return;

		foreach (var item in page.ToolbarItems)
		{
			var button = Gtk.Button.NewWithLabel(item.Text ?? string.Empty);
			button.AddCssClass("flat");

			var capturedItem = item;
			button.OnClicked += (_, _) =>
			{
				if (capturedItem.Command?.CanExecute(capturedItem.CommandParameter) == true)
					capturedItem.Command.Execute(capturedItem.CommandParameter);
				((IMenuItemController)capturedItem).Activate();
			};

			button.SetSensitive(item.IsEnabled);
			_toolbarBox.Append(button);
		}
	}

	void UpdateMenuBar(Page? page)
	{
		// Get the active window from the Gtk.Application
		var app = Gtk.Application.GetDefault();
		if (app == null) return;

		var gtkApp = (Gtk.Application)app;
		var window = gtkApp.GetActiveWindow();
		if (window == null) return;

		if (page?.MenuBarItems != null && page.MenuBarItems.Count > 0)
		{
			GtkMenuBarManager.ApplyToWindow(window, page);
		}
		else
		{
			// Clear menu bar when navigating to a page without menu items
			var rootContainer = window.GetChild() as WindowRootViewContainer;
			rootContainer?.ClearMenuBar();
			window.InsertActionGroup("menu", null);
		}
	}
}
