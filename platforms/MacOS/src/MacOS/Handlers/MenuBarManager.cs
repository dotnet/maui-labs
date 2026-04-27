using AppKit;
using Foundation;
using Microsoft.Maui.Controls;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// Manages the native macOS menu bar (NSApp.MainMenu) from MAUI Page.MenuBarItems.
/// macOS has a global menu bar, so this builds NSMenu/NSMenuItem hierarchy from
/// MenuBarItem, MenuFlyoutItem, and MenuFlyoutSeparator definitions.
/// </summary>
public static class MenuBarManager
{
    static MacOSMenuBarOptions _options = new();

    /// <summary>
    /// Sets up the default macOS menu bar with standard App, Edit, and Window menus.
    /// Called automatically from MacOSMauiApplication.DidFinishLaunching().
    /// </summary>
    public static void SetupDefaultMenuBar(MacOSMenuBarOptions? options = null)
    {
        _options = options ?? new MacOSMenuBarOptions();

        var mainMenu = new NSMenu();
        NSApplication.SharedApplication.MainMenu = mainMenu;

        AddDefaultAppMenu(mainMenu);

        if (_options.IncludeDefaultMenus && _options.IncludeDefaultEditMenu)
            AddDefaultEditMenu(mainMenu);

        if (_options.IncludeDefaultMenus && _options.IncludeDefaultWindowMenu)
            AddDefaultWindowMenu(mainMenu);
    }

    public static void UpdateMenuBar(IList<MenuBarItem>? menuBarItems)
    {
        var mainMenu = NSApplication.SharedApplication.MainMenu;
        if (mainMenu == null)
        {
            mainMenu = new NSMenu();
            NSApplication.SharedApplication.MainMenu = mainMenu;
        }

        // Keep the application menu (index 0) if it exists
        var appMenuItem = mainMenu.Count > 0 ? mainMenu.ItemAt(0) : null;
        mainMenu.RemoveAllItems();

        if (appMenuItem != null)
            mainMenu.AddItem(appMenuItem);
        else
            AddDefaultAppMenu(mainMenu);

        // Add custom menus from the page
        var customTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (menuBarItems != null)
        {
            foreach (var menuBarItem in menuBarItems)
            {
                var nsMenuItem = new NSMenuItem(menuBarItem.Text ?? string.Empty);
                var submenu = new NSMenu(menuBarItem.Text ?? string.Empty);

                foreach (var element in menuBarItem)
                {
                    switch (element)
                    {
                        case MenuFlyoutSeparator:
                            submenu.AddItem(NSMenuItem.SeparatorItem);
                            break;
                        case MenuFlyoutSubItem subItem:
                            submenu.AddItem(CreateSubMenuItem(subItem));
                            break;
                        case MenuFlyoutItem flyoutItem:
                            submenu.AddItem(CreateMenuItem(flyoutItem));
                            break;
                    }
                }

                nsMenuItem.Submenu = submenu;
                mainMenu.AddItem(nsMenuItem);
                if (menuBarItem.Text != null)
                    customTitles.Add(menuBarItem.Text);
            }
        }

        // Re-append default Edit/Window menus unless overridden by custom items
        if (_options.IncludeDefaultMenus)
        {
            if (_options.IncludeDefaultEditMenu && !customTitles.Contains("Edit"))
                AddDefaultEditMenu(mainMenu);

            if (_options.IncludeDefaultWindowMenu && !customTitles.Contains("Window"))
                AddDefaultWindowMenu(mainMenu);
        }
    }

    static NSMenuItem CreateMenuItem(MenuFlyoutItem flyoutItem)
    {
        var keyEquivalent = flyoutItem.KeyboardAccelerators?.FirstOrDefault();
        var wrapper = new MenuItemCommandWrapper(flyoutItem);
        var nsItem = new NSMenuItem(
            flyoutItem.Text ?? string.Empty,
            new ObjCRuntime.Selector("menuItemClicked:"),
            keyEquivalent != null ? GetKeyEquivalent(keyEquivalent) : string.Empty);

        nsItem.Target = wrapper;
        nsItem.RepresentedObject = wrapper;

        if (keyEquivalent != null)
            nsItem.KeyEquivalentModifierMask = GetModifierMask(keyEquivalent);

        return nsItem;
    }

    static NSMenuItem CreateSubMenuItem(MenuFlyoutSubItem subItem)
    {
        var nsItem = new NSMenuItem(subItem.Text ?? string.Empty);
        var submenu = new NSMenu(subItem.Text ?? string.Empty);

        foreach (var element in subItem)
        {
            switch (element)
            {
                case MenuFlyoutSeparator:
                    submenu.AddItem(NSMenuItem.SeparatorItem);
                    break;
                case MenuFlyoutSubItem nested:
                    submenu.AddItem(CreateSubMenuItem(nested));
                    break;
                case MenuFlyoutItem flyoutItem:
                    submenu.AddItem(CreateMenuItem(flyoutItem));
                    break;
            }
        }

        nsItem.Submenu = submenu;
        return nsItem;
    }

    static string GetAppName()
    {
        var infoDict = NSBundle.MainBundle.InfoDictionary;
        if (infoDict != null)
        {
            if (infoDict.TryGetValue(new NSString("CFBundleName"), out var name) && name is NSString nsName)
                return nsName.ToString();
        }
        return NSProcessInfo.ProcessInfo.ProcessName;
    }

    static void AddDefaultAppMenu(NSMenu mainMenu)
    {
        var appName = GetAppName();
        var appMenu = new NSMenuItem();
        var appSubmenu = new NSMenu();

        appSubmenu.AddItem(new NSMenuItem(
            $"About {appName}",
            new ObjCRuntime.Selector("orderFrontStandardAboutPanel:"),
            string.Empty));

        appSubmenu.AddItem(NSMenuItem.SeparatorItem);

        var hideItem = new NSMenuItem(
            $"Hide {appName}",
            new ObjCRuntime.Selector("hide:"),
            "h");
        appSubmenu.AddItem(hideItem);

        var hideOthersItem = new NSMenuItem(
            "Hide Others",
            new ObjCRuntime.Selector("hideOtherApplications:"),
            "h");
        hideOthersItem.KeyEquivalentModifierMask = NSEventModifierMask.CommandKeyMask | NSEventModifierMask.AlternateKeyMask;
        appSubmenu.AddItem(hideOthersItem);

        appSubmenu.AddItem(new NSMenuItem(
            "Show All",
            new ObjCRuntime.Selector("unhideAllApplications:"),
            string.Empty));

        appSubmenu.AddItem(NSMenuItem.SeparatorItem);

        appSubmenu.AddItem(new NSMenuItem(
            $"Quit {appName}",
            new ObjCRuntime.Selector("terminate:"),
            "q"));

        appMenu.Submenu = appSubmenu;
        mainMenu.AddItem(appMenu);
    }

    static void AddDefaultEditMenu(NSMenu mainMenu)
    {
        var editMenuItem = new NSMenuItem("Edit");
        var editMenu = new NSMenu("Edit");

        editMenu.AddItem(new NSMenuItem("Undo", new ObjCRuntime.Selector("undo:"), "z"));

        var redoItem = new NSMenuItem("Redo", new ObjCRuntime.Selector("redo:"), "z");
        redoItem.KeyEquivalentModifierMask = NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ShiftKeyMask;
        editMenu.AddItem(redoItem);

        editMenu.AddItem(NSMenuItem.SeparatorItem);

        editMenu.AddItem(new NSMenuItem("Cut", new ObjCRuntime.Selector("cut:"), "x"));
        editMenu.AddItem(new NSMenuItem("Copy", new ObjCRuntime.Selector("copy:"), "c"));
        editMenu.AddItem(new NSMenuItem("Paste", new ObjCRuntime.Selector("paste:"), "v"));
        editMenu.AddItem(new NSMenuItem("Delete", new ObjCRuntime.Selector("delete:"), string.Empty));
        editMenu.AddItem(new NSMenuItem("Select All", new ObjCRuntime.Selector("selectAll:"), "a"));

        editMenuItem.Submenu = editMenu;
        mainMenu.AddItem(editMenuItem);
    }

    static void AddDefaultWindowMenu(NSMenu mainMenu)
    {
        var windowMenuItem = new NSMenuItem("Window");
        var windowMenu = new NSMenu("Window");

        windowMenu.AddItem(new NSMenuItem("Minimize", new ObjCRuntime.Selector("performMiniaturize:"), "m"));
        windowMenu.AddItem(new NSMenuItem("Zoom", new ObjCRuntime.Selector("performZoom:"), string.Empty));

        windowMenu.AddItem(NSMenuItem.SeparatorItem);

        var fullScreenItem = new NSMenuItem("Toggle Full Screen", new ObjCRuntime.Selector("toggleFullScreen:"), "f");
        fullScreenItem.KeyEquivalentModifierMask = NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ControlKeyMask;
        windowMenu.AddItem(fullScreenItem);

        windowMenuItem.Submenu = windowMenu;
        mainMenu.AddItem(windowMenuItem);

        // Let macOS auto-add open windows to this menu
        NSApplication.SharedApplication.WindowsMenu = windowMenu;
    }

    static string GetKeyEquivalent(KeyboardAccelerator accelerator)
    {
        return accelerator.Key?.ToLower() ?? string.Empty;
    }

    static NSEventModifierMask GetModifierMask(KeyboardAccelerator accelerator)
    {
        var mask = NSEventModifierMask.CommandKeyMask;
        if (accelerator.Modifiers.HasFlag(KeyboardAcceleratorModifiers.Shift))
            mask |= NSEventModifierMask.ShiftKeyMask;
        if (accelerator.Modifiers.HasFlag(KeyboardAcceleratorModifiers.Alt))
            mask |= NSEventModifierMask.AlternateKeyMask;
        if (accelerator.Modifiers.HasFlag(KeyboardAcceleratorModifiers.Ctrl))
            mask |= NSEventModifierMask.ControlKeyMask;
        return mask;
    }
}

/// <summary>
/// Wraps a MenuFlyoutItem's command for invocation from NSMenuItem action.
/// </summary>
internal class MenuItemCommandWrapper : Foundation.NSObject
{
    readonly MenuFlyoutItem _flyoutItem;

    public MenuItemCommandWrapper(MenuFlyoutItem flyoutItem)
    {
        _flyoutItem = flyoutItem;
    }

    [Foundation.Export("menuItemClicked:")]
    public void MenuItemClicked(Foundation.NSObject sender)
    {
        _flyoutItem.Command?.Execute(_flyoutItem.CommandParameter);
        (_flyoutItem as IMenuFlyoutItem)?.Clicked();
    }
}
