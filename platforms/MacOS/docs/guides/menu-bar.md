# Menu Bar

Configure the native macOS application menu bar.

## Default Menus

The platform automatically creates standard macOS menus:

- **App menu** — About, Hide, Hide Others, Show All, Quit (with ⌘Q)
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

Setting `IncludeDefaultMenus = false` disables the default Edit and Window menus.
The application menu (including Quit) is always retained.

### Localization

Default titles use Foundation's native `NSBundle` localization and the application's
selected language, not .NET's `CurrentUICulture`. The backend ships English, German,
French and Dutch translations in `MauiMenuBar.bundle`; English is the development-language
fallback. Declare the languages your application supports with `CFBundleLocalizations`
in `Info.plist`, or provide localized resources in `<language>.lproj` directories.
As with other native macOS strings, changes to the application's language take effect
on its next launch.
The Apple SDK extracts the localization bundle from the backend assembly into the
application bundle for both project and NuGet references, including normal `dotnet run`.
A missing localization bundle is reported as a deployment error rather than silently
displaying string keys or changing languages.

AppKit's `WindowsMenu` registration manages the window list and may update or replace
standard Window commands. The backend supplies localized native responder-chain commands
for systems that do not populate an empty menu. It does not read AppKit's private string
tables or assume that registering an empty Window menu generates those commands.
See Apple's [Window menu API](https://developer.apple.com/documentation/appkit/nsapplication/windowsmenu)
and [bundle localization guidance](https://developer.apple.com/library/archive/documentation/CoreFoundation/Conceptual/CFBundles/AccessingaBundlesContents/AccessingaBundlesContents.html).

To override a default or add another language, include a `BundleResource` named
`<language>.lproj/MauiMenuBar.strings` in your application. For example:

```text
"Edit" = "Bearbeiten";
"Copy" = "Kopieren";
"AboutApp" = "Über {0}";
```

Supported keys are `AboutApp`, `HideApp`, `HideOthers`, `ShowAll`, `QuitApp`, `Edit`,
`Undo`, `Redo`, `Cut`, `Copy`, `Paste`, `Delete`, `SelectAll`, `Window`, `Minimize`,
`Zoom` and `EnterFullScreen`. `{0}` inserts the localized application display name
(`CFBundleDisplayName`, falling back to `CFBundleName` and the process name).
Unspecified keys retain the backend's translations. AppKit can update native command
titles during validation, so an override is not a way to disable system menu behavior.

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
Custom menu text remains application-owned. An `Edit` or `Window` menu suppresses the
corresponding default when its title matches either the English name or the currently
localized default title.

## API Reference

### MacOSMenuBarOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IncludeDefaultMenus` | `bool` | `true` | Include any default menus |
| `IncludeDefaultEditMenu` | `bool` | `true` | Include Edit menu (Undo, Copy, Paste, etc.) |
| `IncludeDefaultWindowMenu` | `bool` | `true` | Include Window menu (Minimize, Zoom, etc.) |
