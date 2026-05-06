using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// Options for configuring the default macOS menu bar items.
/// All options default to true — set to false to disable specific default menus.
/// </summary>
public class MacOSMenuBarOptions
{
    /// <summary>
    /// Master toggle for all default menus (App, Edit, Window).
    /// When false, no default menus are created — only developer-defined MenuBarItems will appear.
    /// The App menu with Quit (⌘Q) is still included for proper macOS behavior.
    /// </summary>
    public bool IncludeDefaultMenus { get; set; } = true;

    /// <summary>
    /// Include the default Edit menu (Undo, Redo, Cut, Copy, Paste, Delete, Select All).
    /// </summary>
    public bool IncludeDefaultEditMenu { get; set; } = true;

    /// <summary>
    /// Include the default Window menu (Minimize, Zoom, Toggle Full Screen).
    /// </summary>
    public bool IncludeDefaultWindowMenu { get; set; } = true;
}
