# Sidebar Navigation

Build native macOS sidebar navigation using `NSSplitViewController` with behind-window vibrancy, SF Symbol icons, and structured source-list groups.

## Setup

There are two sidebar implementations: **Shell sidebar** and **FlyoutPage sidebar**. Both use `NSSplitViewController` with `NSOutlineView` source-list styling.

### Shell Sidebar

Enable the native sidebar on a `Shell`:

```csharp
var shell = new Shell();
MacOSShell.SetUseNativeSidebar(shell, true);
```

`UseNativeSidebar` is an opt-in flag. When `true`, the `ShellHandler` creates an `NSOutlineView` with source-list styling and behind-window vibrancy. When `false` (default), the Shell uses a standard MAUI flyout layout.

### FlyoutPage Sidebar

For `FlyoutPage`, you must **register the handler** and **opt in** to the native sidebar:

```csharp
// MauiProgram.cs — register the handler (required)
builder.ConfigureMauiHandlers(handlers =>
{
    handlers.AddHandler<FlyoutPage, NativeSidebarFlyoutPageHandler>();
});
```

```csharp
// Page code — opt in to NSSplitViewController sidebar (required)
var flyoutPage = new FlyoutPage();
MacOSFlyoutPage.SetUseNativeSidebar(flyoutPage, true);
```

> **Important:** Both steps are required for `FlyoutPage`:
> - **Handler registration** replaces the default `FlyoutPageHandler` with `NativeSidebarFlyoutPageHandler`
> - **`UseNativeSidebar = true`** opts the specific `FlyoutPage` instance into `NSSplitViewController` with sidebar vibrancy and traffic lights inside the sidebar
> - Without `UseNativeSidebar`, the handler uses a plain `NSSplitView` without source-list styling

The sidebar renders `ShellItem` and `ShellSection` entries in an `NSOutlineView` source list. Sections with multiple `ShellContent` children appear as expandable groups.

### SF Symbol Icons

Assign SF Symbol names to shell items for native icons:

```csharp
var section = new ShellSection { Title = "Settings" };
MacOSShell.SetSystemImage(section, "gear");
```

Works on `ShellItem`, `ShellSection`, and `ShellContent`.

### Sidebar Resizing

By default the sidebar can be resized by dragging. To lock it:

```csharp
MacOSShell.SetIsSidebarResizable(shell, false);
```

### Sidebar Width

Set the sidebar width via `Shell.FlyoutWidth`:

```csharp
shell.FlyoutWidth = 280;
```

The default width is 300px. The minimum is 150px and maximum is 400px.

## FlyoutPage Sidebar

`FlyoutPage` can also use the native sidebar appearance:

```csharp
var flyoutPage = new FlyoutPage();
MacOSFlyoutPage.SetUseNativeSidebar(flyoutPage, true);
```

### Structured Sidebar Items

Provide structured items with groups, icons, and hierarchy:

```csharp
var items = new List<MacOSSidebarItem>
{
    new MacOSSidebarItem
    {
        Title = "Library",
        Children = new List<MacOSSidebarItem>
        {
            new MacOSSidebarItem { Title = "Music", SystemImage = "music.note" },
            new MacOSSidebarItem { Title = "Movies", SystemImage = "film" },
            new MacOSSidebarItem { Title = "Podcasts", SystemImage = "mic" },
        }
    },
    new MacOSSidebarItem
    {
        Title = "Playlists",
        Children = new List<MacOSSidebarItem>
        {
            new MacOSSidebarItem { Title = "Favorites", SystemImage = "heart" },
            new MacOSSidebarItem { Title = "Recently Added", SystemImage = "clock" },
        }
    }
};

MacOSFlyoutPage.SetSidebarItems(flyoutPage, items);
```

Items with `Children` become group headers (non-selectable, bold uppercase text). Leaf items are selectable rows.

### Selection Handling

```csharp
MacOSFlyoutPage.SetSidebarSelectionChanged(flyoutPage, item =>
{
    Console.WriteLine($"Selected: {item.Title}");
    // Navigate to the appropriate detail page
    flyoutPage.Detail = new NavigationPage(GetPageForItem(item));
});
```

### Programmatic Selection

```csharp
MacOSFlyoutPage.SetSelectedItem(flyoutPage, items[0].Children[1]);
```

> **Note:** Programmatic selection via `SetSelectedItem` or `SelectSidebarItem` does **not** trigger the `SidebarSelectionChanged` callback. This is intentional — it prevents infinite loops when you update the detail page in the selection callback, which would otherwise re-trigger the callback. If you need to respond to programmatic selections, update your UI directly after calling `SetSelectedItem`.

## MacOSSidebarItem

| Property | Type | Description |
|----------|------|-------------|
| `Title` | `string` | Display text |
| `SystemImage` | `string?` | SF Symbol name for the icon |
| `Icon` | `ImageSource?` | Custom icon image |
| `Children` | `List<MacOSSidebarItem>?` | Child items (makes this a group header) |
| `Tag` | `string?` | Identifier for programmatic lookup |
| `IsGroup` | `bool` | Read-only; `true` when `Children` is non-empty |

## API Reference

### MacOSShell (Attached Properties)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `UseNativeSidebar` | `bool` | `false` | Use `NSSplitViewController` for sidebar |
| `SystemImage` | `string?` | `null` | SF Symbol name for sidebar icon |
| `IsSidebarResizable` | `bool` | `true` | Allow user to resize sidebar |

### MacOSFlyoutPage (Attached Properties)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `UseNativeSidebar` | `bool` | `false` | Use `NSSplitViewController` for sidebar |
| `SidebarItems` | `IList<MacOSSidebarItem>?` | `null` | Structured sidebar items |
| `SidebarSelectionChanged` | `Action<MacOSSidebarItem>?` | `null` | Selection callback |
| `SelectedItem` | `MacOSSidebarItem?` | `null` | Currently selected item |
