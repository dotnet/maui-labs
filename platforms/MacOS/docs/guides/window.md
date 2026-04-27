# Window Configuration

Customize the macOS window titlebar appearance, toolbar style, and content layout.

## Full-Size Content View

By default, window content extends behind the titlebar (edge-to-edge). This creates the modern translucent titlebar look but can cause problems when content overlaps the titlebar area — especially with `BlazorWebView` or windows without a toolbar.

To keep content **below** the titlebar instead:

```csharp
var window = new Window(new MyPage());
MacOSWindow.SetFullSizeContentView(window, false);
```

**When to use `false`:**
- Secondary/inspector windows that don't have a toolbar (`NavigationPage` or Shell)
- Windows hosting `BlazorWebView` where the WebView intercepts titlebar mouse events
- Any window where content overlaps the titlebar and the window can't be dragged

**When to keep `true` (default):**
- Main windows with Shell or `NavigationPage` (the toolbar pushes content below the titlebar automatically)
- Windows where you want the modern translucent titlebar appearance

## Titlebar Style

Control how the toolbar integrates with the titlebar:

```csharp
MacOSWindow.SetTitlebarStyle(window, MacOSTitlebarStyle.UnifiedCompact);
```

| Style | Description |
|-------|-------------|
| `Automatic` | System default |
| `Expanded` | Full-height toolbar below the titlebar |
| `Preference` | Centered icons with labels (like System Preferences) |
| `Unified` | Toolbar items inline with the titlebar |
| `UnifiedCompact` | Compact unified style with smaller toolbar |

## Transparent Titlebar

Make the titlebar transparent so content can extend behind it:

```csharp
MacOSWindow.SetTitlebarTransparent(window, true);
```

This is commonly used with `FullSizeContentView` for edge-to-edge content layouts.

## Title Visibility

Show or hide the window title text:

```csharp
MacOSWindow.SetTitleVisibility(window, MacOSTitleVisibility.Hidden);
```

| Value | Description |
|-------|-------------|
| `Visible` | Show the title text (default) |
| `Hidden` | Hide the title text |

## Titlebar Separator Style

Control the separator line between the titlebar/toolbar and the window content:

```csharp
MacOSWindow.SetTitlebarSeparatorStyle(window, MacOSTitlebarSeparatorStyle.None);
```

| Value | Description |
|-------|-------------|
| `Automatic` | System decides based on scroll position (default) |
| `None` | No separator line — seamless transition from toolbar to content |
| `Line` | Always show a thin separator line |

> **Note:** Set this property in `OnAppearing` (not the constructor) — the `Window` must be available.

```csharp
protected override void OnAppearing()
{
    base.OnAppearing();
    MacOSWindow.SetTitlebarSeparatorStyle(Window, MacOSTitlebarSeparatorStyle.None);
}
```

## Example: Modern App Appearance

Combine these properties for a modern, unified look:

```csharp
// In your App class or Window handler
MacOSWindow.SetTitlebarStyle(window, MacOSTitlebarStyle.UnifiedCompact);
MacOSWindow.SetTitlebarTransparent(window, true);
MacOSWindow.SetTitleVisibility(window, MacOSTitleVisibility.Hidden);
```

## API Reference

### MacOSWindow (Attached Properties)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FullSizeContentView` | `bool` | `true` | Content extends behind titlebar (edge-to-edge) |
| `TitlebarStyle` | `MacOSTitlebarStyle` | `Automatic` | Toolbar/titlebar integration style |
| `TitlebarTransparent` | `bool` | `false` | Transparent titlebar |
| `TitleVisibility` | `MacOSTitleVisibility` | `Visible` | Show/hide title text |
| `TitlebarSeparatorStyle` | `MacOSTitlebarSeparatorStyle` | `Automatic` | Separator between toolbar and content |

### MacOSTitlebarStyle Enum

| Value | NSToolbarStyle | Description |
|-------|----------------|-------------|
| `Automatic` | `.Automatic` | System decides |
| `Expanded` | `.Expanded` | Separate toolbar row |
| `Preference` | `.Preference` | Centered toolbar (Settings-style) |
| `Unified` | `.Unified` | Inline with titlebar |
| `UnifiedCompact` | `.UnifiedCompact` | Compact inline |

### MacOSTitleVisibility Enum

| Value | NSTitleVisibility | Description |
|-------|-------------------|-------------|
| `Visible` | `.Visible` | Title shown |
| `Hidden` | `.Hidden` | Title hidden |

### MacOSTitlebarSeparatorStyle Enum

| Value | NSTitlebarSeparatorStyle | Description |
|-------|--------------------------|-------------|
| `Automatic` | `.Automatic` | System decides |
| `None` | `.None` | No separator line |
| `Line` | `.Line` | Always show separator |

## Common Patterns

### Main Window with Sidebar and Toolbar

The standard pattern for a main app window — Shell provides the sidebar and toolbar, content is automatically inset below the toolbar:

```csharp
// FullSizeContentView=true (default) works well here because
// Shell/NavigationPage creates an NSToolbar that pushes content down
var window = new Window(new AppShell());
```

### Secondary/Inspector Window

For secondary windows without a toolbar, disable `FullSizeContentView` so content doesn't overlap the titlebar:

```csharp
var inspectorWindow = new Window(new InspectorPage());
MacOSWindow.SetFullSizeContentView(inspectorWindow, false);
MacOSWindow.SetTitlebarStyle(inspectorWindow, MacOSTitlebarStyle.Automatic);
Application.Current.OpenWindow(inspectorWindow);
```

### BlazorWebView Window

BlazorWebView in a window without a toolbar needs special attention — the WebView intercepts mouse events in the titlebar area, making the window undraggable:

```csharp
// Option 1: Disable FullSizeContentView (simplest)
var window = new Window(new BlazorPage());
MacOSWindow.SetFullSizeContentView(window, false);

// Option 2: Wrap in NavigationPage to get a native toolbar
var window = new Window(new NavigationPage(new BlazorPage()));
// The toolbar pushes BlazorWebView content below the titlebar
```

## Gotchas

| Issue | Cause | Fix |
|-------|-------|-----|
| Can't drag titlebar | `FullSizeContentView` lets WebView/content cover titlebar | Set `MacOSWindow.SetFullSizeContentView(window, false)`, or use `NavigationPage`/Shell for a native toolbar |
| Content behind titlebar | Same as above | Same fix |
| `TitlebarTransparent = true` causes overlap | Title text renders on top of content with no background | Only use with a toolbar present, or set `TitleVisibility = Hidden` |
| Wrapping BlazorWebView in Grid breaks inset | MAUI Grid doesn't propagate safe area to WebView correctly | Use native `NSView` overlays instead of MAUI layout containers for loading screens |
