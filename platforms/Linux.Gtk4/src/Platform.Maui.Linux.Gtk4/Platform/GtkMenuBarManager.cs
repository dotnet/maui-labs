using Microsoft.Maui.Controls;

namespace Platform.Maui.Linux.Gtk4.Platform;

/// <summary>
/// Builds a GTK4 PopoverMenuBar from MAUI MenuBarItem/MenuFlyoutItem collections.
/// Wires Gio.SimpleAction objects on the window's action group so menu items are clickable.
/// </summary>
public static class GtkMenuBarManager
{
	private static readonly List<string> _registeredActions = new();
	private const string ActionPrefix = "menu";

	/// <summary>
	/// Applies menu bar and toolbar items from a page to a GTK window.
	/// Menu bar is added to the WindowRootViewContainer; toolbar buttons go into the nav bar.
	/// </summary>
	public static void ApplyToWindow(Gtk.Window window, Page? page)
	{
		if (page == null)
			return;

		var rootContainer = window.GetChild() as WindowRootViewContainer;

		// Apply MenuBar
		if (page.MenuBarItems.Count > 0 && rootContainer != null)
		{
			var menuBar = BuildMenuBar(window, page.MenuBarItems);
			if (menuBar != null)
				rootContainer.SetMenuBar(menuBar);
		}
	}

	/// <summary>
	/// Creates a Gtk.PopoverMenuBar from MAUI MenuBarItems with working actions.
	/// </summary>
	static Gtk.PopoverMenuBar? BuildMenuBar(Gtk.Window window, IList<MenuBarItem> menuBarItems)
	{
		if (menuBarItems.Count == 0)
			return null;

		// Clean up previously registered actions
		var actionGroup = Gio.SimpleActionGroup.New();

		var menuModel = Gio.Menu.New();
		int actionIndex = 0;

		foreach (var menuBarItem in menuBarItems)
		{
			var submenu = Gio.Menu.New();
			Gio.Menu? currentSection = null;

			foreach (var element in menuBarItem)
			{
				if (element is MenuFlyoutSeparator)
				{
					if (currentSection != null && currentSection.GetNItems() > 0)
					{
						submenu.AppendSection(null, currentSection);
					}
					currentSection = Gio.Menu.New();
					continue;
				}

				if (element is MenuFlyoutItem flyoutItem)
				{
					var actionName = $"item{actionIndex++}";
					var action = Gio.SimpleAction.New(actionName, null);

					var captured = flyoutItem;
					action.OnActivate += (_, _) =>
					{
						if (captured.Command?.CanExecute(captured.CommandParameter) == true)
							captured.Command.Execute(captured.CommandParameter);
						((IMenuItemController)captured).Activate();
					};

					actionGroup.AddAction(action);

					var target = currentSection ?? submenu;
					target.Append(flyoutItem.Text, $"{ActionPrefix}.{actionName}");
				}
			}

			// Flush last section
			if (currentSection != null && currentSection.GetNItems() > 0)
				submenu.AppendSection(null, currentSection);

			menuModel.AppendSubmenu(menuBarItem.Text, submenu);
		}

		window.InsertActionGroup(ActionPrefix, actionGroup);
		return Gtk.PopoverMenuBar.NewFromModel(menuModel);
	}
}
