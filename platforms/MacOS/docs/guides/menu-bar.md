# Menu Bar

Configure the native macOS application menu bar.

## Default Menus

The platform automatically creates standard macOS menus:

- **App menu** — About, Preferences, Quit (with ⌘Q)
- **Edit menu** — Undo, Redo, Cut, Copy, Paste, Delete, Select All
- **Window menu** — Minimize, Zoom, Full Screen

### Customizing Defaults

Control which default menus are included via `ConfigureMacOSMenuBar()`:

```csharp
// MauiProgram.cs
builder.ConfigureMacOSMenuBar(options =>
{
    options.IncludeDefaultMenus = true;       // Master toggle (default: true)
    options.IncludeDefaultEditMenu = true;     // Edit menu (default: true)
    options.IncludeDefaultWindowMenu = true;   // Window menu (default: true)
});
```

Setting `IncludeDefaultMenus = false` disables all default menus. You can then build the entire menu bar from scratch using `Page.MenuBarItems`.

## Page-Level Menu Items

Add custom menus via `Page.MenuBarItems` on any `ContentPage`:

```csharp
public class MyPage : ContentPage
{
    public MyPage()
    {
        // Add a "File" menu
        var fileMenu = new MenuBarItem { Text = "File" };

        fileMenu.Add(new MenuFlyoutItem
        {
            Text = "New",
            Command = new Command(() => CreateNew()),
            KeyboardAccelerators =
            {
                new KeyboardAccelerator { Modifiers = KeyboardAcceleratorModifiers.Cmd, Key = "n" }
            }
        });

        fileMenu.Add(new MenuFlyoutItem
        {
            Text = "Open...",
            Command = new Command(() => OpenFile()),
            KeyboardAccelerators =
            {
                new KeyboardAccelerator { Modifiers = KeyboardAcceleratorModifiers.Cmd, Key = "o" }
            }
        });

        fileMenu.Add(new MenuFlyoutSeparator());

        fileMenu.Add(new MenuFlyoutItem
        {
            Text = "Save",
            Command = new Command(() => Save()),
            KeyboardAccelerators =
            {
                new KeyboardAccelerator { Modifiers = KeyboardAcceleratorModifiers.Cmd, Key = "s" }
            }
        });

        MenuBarItems.Add(fileMenu);
    }
}
```

Menu bar items are merged with the default menus. Page-level menus update automatically when the active page changes.

## API Reference

### MacOSMenuBarOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IncludeDefaultMenus` | `bool` | `true` | Include any default menus |
| `IncludeDefaultEditMenu` | `bool` | `true` | Include Edit menu (Undo, Copy, Paste, etc.) |
| `IncludeDefaultWindowMenu` | `bool` | `true` | Include Window menu (Minimize, Zoom, etc.) |
