# Toolbar

Control the native `NSToolbar` with item placement, explicit layouts, and per-item customization.

## Overview

macOS toolbars have two distinct areas when a sidebar is present:

```
┌─────────────────────────────────────────────────────┐
│ ☰  [Sidebar Items]  │  [Content Items]    Title     │
│                      │                              │
│  Sidebar area        │  Content area                │
│  (before separator)  │  (after separator)           │
└─────────────────────────────────────────────────────┘
```

An `NSTrackingSeparatorToolbarItem` divides the two areas, aligning with the sidebar split divider.

## Adding Toolbar Items

Standard MAUI `ToolbarItem` objects go to the content area by default:

```csharp
page.ToolbarItems.Add(new ToolbarItem
{
    Text = "Add",
    IconImageSource = "plus",
    Command = AddCommand
});
```

### SF Symbol Icons

Use SF Symbol names directly as `IconImageSource` for native icons:

```csharp
new ToolbarItem { Text = "Share", IconImageSource = "square.and.arrow.up" }
```

> **Tip:** Any string passed as `IconImageSource` is automatically resolved as an SF Symbol name via `NSImage.GetSystemSymbol()`. You don't need a custom image file or post-processing — just use the SF Symbol name directly (e.g., `"plus.circle"`, `"gear"`, `"square.and.arrow.up"`). If no matching SF Symbol is found, it falls back to loading a file with that name. Browse available symbols in Apple's [SF Symbols app](https://developer.apple.com/sf-symbols/).

## Sidebar Placement

Place toolbar items in the sidebar area using the `MacOSToolbarItem.Placement` attached property:

```csharp
var item = new ToolbarItem { Text = "Filter", IconImageSource = "line.3.horizontal.decrease" };
MacOSToolbarItem.SetPlacement(item, MacOSToolbarItemPlacement.SidebarLeading);
page.ToolbarItems.Add(item);
```

### Placement Values

| Value | Description |
|-------|-------------|
| `Content` | Content area (right of separator) — default |
| `Sidebar` | Sidebar area, appended after other sidebar items |
| `SidebarLeading` | Sidebar area, left-aligned |
| `SidebarCenter` | Sidebar area, centered |
| `SidebarTrailing` | Sidebar area, right-aligned |

In convenience mode, sidebar items are arranged: `[Leading...] [Center...] [Trailing...]` with flexible spaces between groups.

## Explicit Layout

For full control over item ordering, spacers, and separators, use explicit layout lists.

### Sidebar Layout

```csharp
var filter = new ToolbarItem { Text = "Filter", IconImageSource = "line.3.horizontal.decrease" };
var sort = new ToolbarItem { Text = "Sort", IconImageSource = "arrow.up.arrow.down" };

MacOSToolbar.SetSidebarLayout(page, new List<MacOSToolbarLayoutItem>
{
    MacOSToolbarLayoutItem.Item(filter),
    MacOSToolbarLayoutItem.FlexibleSpace,
    MacOSToolbarLayoutItem.Item(sort),
});

page.ToolbarItems.Add(filter);
page.ToolbarItems.Add(sort);
```

### Content Layout

```csharp
var add = new ToolbarItem { Text = "Add", IconImageSource = "plus" };
var refresh = new ToolbarItem { Text = "Refresh", IconImageSource = "arrow.clockwise" };

MacOSToolbar.SetContentLayout(page, new List<MacOSToolbarLayoutItem>
{
    MacOSToolbarLayoutItem.FlexibleSpace,
    MacOSToolbarLayoutItem.Title,
    MacOSToolbarLayoutItem.FlexibleSpace,
    MacOSToolbarLayoutItem.Item(add),
    MacOSToolbarLayoutItem.Item(refresh),
});

page.ToolbarItems.Add(add);
page.ToolbarItems.Add(refresh);
```

### Layout Items

| Item | Description |
|------|-------------|
| `MacOSToolbarLayoutItem.Item(toolbarItem)` | A MAUI `ToolbarItem` |
| `MacOSToolbarLayoutItem.Title` | Centered page title label |
| `MacOSToolbarLayoutItem.FlexibleSpace` | Flexible space (pushes items apart) |
| `MacOSToolbarLayoutItem.Space` | Fixed-width space |
| `MacOSToolbarLayoutItem.Separator` | Visual separator |
| `MacOSToolbarLayoutItem.Search(searchItem)` | Search toolbar item |
| `MacOSToolbarLayoutItem.Menu(menuItem)` | Menu toolbar item |
| `MacOSToolbarLayoutItem.Group(group)` | Segmented control group |
| `MacOSToolbarLayoutItem.Share(shareItem)` | Share picker |
| `MacOSToolbarLayoutItem.PopUp(popUpItem)` | Popup button |

### System Items

Built-in macOS toolbar items:

```csharp
MacOSToolbar.SetSidebarLayout(page, new List<MacOSToolbarLayoutItem>
{
    MacOSToolbarLayoutItem.ToggleSidebar,  // Standard sidebar toggle
    MacOSToolbarLayoutItem.FlexibleSpace,
    MacOSToolbarLayoutItem.Item(myItem),
});
```

| System Item | Description |
|-------------|-------------|
| `ToggleSidebar` | Toggle sidebar visibility |
| `ToggleInspector` | Toggle inspector panel |
| `Print` | Print dialog |
| `ShowColors` | Color picker panel |
| `ShowFonts` | Font picker panel |
| `WritingTools` | macOS Writing Tools |
| `CloudSharing` | iCloud sharing |
| `InspectorTrackingSeparator` | Inspector tracking separator |

## Per-Item Properties

Customize individual toolbar items with attached properties:

```csharp
var item = new ToolbarItem { Text = "Important", IconImageSource = "star.fill" };

MacOSToolbarItem.SetIsBordered(item, true);
MacOSToolbarItem.SetBadge(item, "3");
MacOSToolbarItem.SetToolTip(item, "Mark as important");
MacOSToolbarItem.SetVisibilityPriority(item, MacOSToolbarItemVisibilityPriority.High);
```

### Attached Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Placement` | `MacOSToolbarItemPlacement` | `Content` | Where the item appears |
| `IsBordered` | `bool` | `false` | Show button border/bezel |
| `Badge` | `string?` | `null` | Badge text overlay |
| `BackgroundTintColor` | `Color?` | `null` | Background tint color |
| `ToolTip` | `string?` | `null` | Hover tooltip text |
| `VisibilityPriority` | `MacOSToolbarItemVisibilityPriority` | `Standard` | Overflow behavior |

### Visibility Priority Values

| Value | Description |
|-------|-------------|
| `Standard` | Default priority (0) |
| `Low` | Overflows first (-1000) |
| `High` | Overflows last (1000) |
| `User` | Never overflows (2000) |

## Combining Modes

You can use explicit layout for the sidebar and convenience mode for the content area (or vice versa). Items not claimed by an explicit layout are handled by the convenience mode:

```csharp
// Explicit sidebar layout
MacOSToolbar.SetSidebarLayout(page, new List<MacOSToolbarLayoutItem>
{
    MacOSToolbarLayoutItem.Item(sidebarItem),
    MacOSToolbarLayoutItem.FlexibleSpace,
    MacOSToolbarLayoutItem.Search(searchItem),
});

// Content items use default convenience layout
page.ToolbarItems.Add(sidebarItem);
page.ToolbarItems.Add(contentItem1); // goes to content area automatically
page.ToolbarItems.Add(contentItem2);
```
