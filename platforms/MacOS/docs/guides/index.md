# macOS (AppKit) Platform APIs

This .NET MAUI backend provides native macOS experiences using AppKit. The following platform-specific APIs let you build apps that look and feel like first-class Mac citizens.

## Guides

| Topic | Description |
|-------|-------------|
| [Getting Started](getting-started.md) | Create a macOS app head project, link shared code & resources |
| [Sidebar Navigation](sidebar.md) | Native `NSSplitViewController` sidebar for Shell and FlyoutPage |
| [Toolbar](toolbar.md) | NSToolbar with native item types, placement, and layout |
| [Toolbar Item Types](toolbar-items.md) | Search, menu, segmented control, share, and popup toolbar items |
| [Window Configuration](window.md) | Titlebar style, transparency, and toolbar style |
| [Menu Bar](menu-bar.md) | Application menu bar and default menus |
| [Lifecycle Events](lifecycle.md) | App and window lifecycle events |
| [Theming](theming.md) | Light/dark mode and appearance |
| [Blazor Hybrid](blazor-hybrid.md) | BlazorWebView with WKWebView |
| [Controls & Platform Notes](controls.md) | MapView, gestures, modals, icons, fonts, threading |

## Quick Start

Register the macOS backend in your app:

```csharp
// MauiProgram.cs
builder.UseMauiApp<App>()
       .UseMacOS();
```

### Native Sidebar with Shell

```csharp
var shell = new Shell();
MacOSShell.SetUseNativeSidebar(shell, true);
```

### Toolbar Items

```csharp
// Add a toolbar item to the sidebar area
var item = new ToolbarItem { Text = "Refresh", IconImageSource = "arrow.clockwise" };
MacOSToolbarItem.SetPlacement(item, MacOSToolbarItemPlacement.SidebarLeading);
page.ToolbarItems.Add(item);
```

### Window Titlebar

```csharp
MacOSWindow.SetTitlebarStyle(window, MacOSTitlebarStyle.UnifiedCompact);
MacOSWindow.SetTitlebarTransparent(window, true);
```

## Platform-Specific Namespace

All macOS APIs are in the `Microsoft.Maui.Platforms.MacOS.Platform` namespace:

```csharp
using Microsoft.Maui.Platforms.MacOS.Platform;
```
