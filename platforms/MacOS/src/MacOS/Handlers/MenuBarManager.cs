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
    static readonly Lazy<NSBundle> _menuResources = new(() =>
    {
        var path = NSBundle.MainBundle.PathForResource("MauiMenuBar", "bundle")
            ?? throw new InvalidOperationException("The MauiMenuBar.bundle localization resources are missing.");
        return NSBundle.FromPath(path)
            ?? throw new InvalidOperationException($"Cannot load menu localization bundle '{path}'.");
    });

    static string Localize(string key) =>
        NSBundle.MainBundle.GetLocalizedString(key,
            _menuResources.Value.GetLocalizedString(key, key, "MenuBar"), "MauiMenuBar");

    static void ClearWindowMenu()
    {
        // AppKit accepts nil here, but the .NET property setter rejects it.
        using var key = new NSString("windowsMenu");
        using var values = NSDictionary.FromObjectAndKey(NSNull.Null, key);
        NSApplication.SharedApplication.SetValuesForKeysWithDictionary(values);
    }

    /// <summary>
    /// Sets up the default macOS menu bar with standard App, Edit, and Window menus.
    /// Called automatically from MacOSMauiApplication.DidFinishLaunching().
    /// </summary>
    public static void SetupDefaultMenuBar(MacOSMenuBarOptions? options = null)
    {
        _options = options ?? new MacOSMenuBarOptions();
        ClearWindowMenu();

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
        ClearWindowMenu();
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
            if (_options.IncludeDefaultEditMenu &&
                !customTitles.Contains("Edit") && !customTitles.Contains(Localize("Edit")))
                AddDefaultEditMenu(mainMenu);

            if (_options.IncludeDefaultWindowMenu &&
                !customTitles.Contains("Window") && !customTitles.Contains(Localize("Window")))
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
        var bundle = NSBundle.MainBundle;
        if (bundle.ObjectForInfoDictionary("CFBundleDisplayName") is NSString displayName)
            return displayName.ToString();
        if (bundle.ObjectForInfoDictionary("CFBundleName") is NSString name)
            return name.ToString();
        return NSProcessInfo.ProcessInfo.ProcessName;
    }

    static void AddDefaultAppMenu(NSMenu mainMenu)
    {
        var appName = GetAppName();
        var appMenu = new NSMenuItem();
        var appSubmenu = new NSMenu();

        appSubmenu.AddItem(new NSMenuItem(
            Localize("AboutApp").Replace("{0}", appName, StringComparison.Ordinal),
            new ObjCRuntime.Selector("orderFrontStandardAboutPanel:"),
            string.Empty));

        appSubmenu.AddItem(NSMenuItem.SeparatorItem);

        var hideItem = new NSMenuItem(
            Localize("HideApp").Replace("{0}", appName, StringComparison.Ordinal),
            new ObjCRuntime.Selector("hide:"),
            "h");
        appSubmenu.AddItem(hideItem);

        var hideOthersItem = new NSMenuItem(
            Localize("HideOthers"),
            new ObjCRuntime.Selector("hideOtherApplications:"),
            "h");
        hideOthersItem.KeyEquivalentModifierMask = NSEventModifierMask.CommandKeyMask | NSEventModifierMask.AlternateKeyMask;
        appSubmenu.AddItem(hideOthersItem);

        appSubmenu.AddItem(new NSMenuItem(
            Localize("ShowAll"),
            new ObjCRuntime.Selector("unhideAllApplications:"),
            string.Empty));

        appSubmenu.AddItem(NSMenuItem.SeparatorItem);

        appSubmenu.AddItem(new NSMenuItem(
            Localize("QuitApp").Replace("{0}", appName, StringComparison.Ordinal),
            new ObjCRuntime.Selector("terminate:"),
            "q"));

        appMenu.Submenu = appSubmenu;
        mainMenu.AddItem(appMenu);
    }

    static void AddDefaultEditMenu(NSMenu mainMenu)
    {
        var editMenuItem = new NSMenuItem(Localize("Edit"));
        var editMenu = new NSMenu(Localize("Edit"));

        editMenu.AddItem(new NSMenuItem(Localize("Undo"), new ObjCRuntime.Selector("undo:"), "z"));

        var redoItem = new NSMenuItem(Localize("Redo"), new ObjCRuntime.Selector("redo:"), "z");
        redoItem.KeyEquivalentModifierMask = NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ShiftKeyMask;
        editMenu.AddItem(redoItem);

        editMenu.AddItem(NSMenuItem.SeparatorItem);

        editMenu.AddItem(new NSMenuItem(Localize("Cut"), new ObjCRuntime.Selector("cut:"), "x"));
        editMenu.AddItem(new NSMenuItem(Localize("Copy"), new ObjCRuntime.Selector("copy:"), "c"));
        editMenu.AddItem(new NSMenuItem(Localize("Paste"), new ObjCRuntime.Selector("paste:"), "v"));
        editMenu.AddItem(new NSMenuItem(Localize("Delete"), new ObjCRuntime.Selector("delete:"), string.Empty));
        editMenu.AddItem(new NSMenuItem(Localize("SelectAll"), new ObjCRuntime.Selector("selectAll:"), "a"));

        editMenuItem.Submenu = editMenu;
        mainMenu.AddItem(editMenuItem);
    }

    static void AddDefaultWindowMenu(NSMenu mainMenu)
    {
        var windowMenuItem = new NSMenuItem(Localize("Window"));
        var windowMenu = new NSMenu(Localize("Window"));

        windowMenu.AddItem(new NSMenuItem(Localize("Minimize"), new ObjCRuntime.Selector("performMiniaturize:"), "m"));
        windowMenu.AddItem(new NSMenuItem(Localize("Zoom"), new ObjCRuntime.Selector("performZoom:"), string.Empty));
        windowMenu.AddItem(NSMenuItem.SeparatorItem);
        var fullScreenItem = new NSMenuItem(Localize("EnterFullScreen"), new ObjCRuntime.Selector("toggleFullScreen:"), "f")
        {
            KeyEquivalentModifierMask = NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ControlKeyMask
        };
        windowMenu.AddItem(fullScreenItem);

        windowMenuItem.Submenu = windowMenu;
        mainMenu.AddItem(windowMenuItem);

        // Register the native Window menu so AppKit can manage the window list and update commands.
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
