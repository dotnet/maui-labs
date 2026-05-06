# Toolbar Item Types

Native macOS toolbar item types beyond standard `ToolbarItem` buttons.

## Search (NSSearchToolbarItem)

A collapsible search field — starts as a magnifying glass icon and expands to a text field when clicked (like Apple Notes, Finder, etc.).

```csharp
var search = new MacOSSearchToolbarItem
{
    Placeholder = "Search notes...",
    PreferredWidth = 200,
    ResignsFirstResponderWithCancel = true,
};

search.TextChanged += (s, text) =>
{
    // Filter your content as the user types
    FilterItems(text);
};

search.SearchCommitted += (s, text) =>
{
    // User pressed Enter
    ExecuteSearch(text);
};

MacOSToolbar.SetSearchItem(page, search);
```

### Placement

In the **content area**, the search field starts collapsed as a magnifying glass icon and expands on click. In the **sidebar area**, it auto-expands to fill the available width with the placeholder visible.

```csharp
search.Placement = MacOSToolbarItemPlacement.SidebarLeading; // auto-expanded in sidebar
```

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Placeholder` | `string` | `"Search"` | Placeholder text |
| `Text` | `string` | `""` | Current search text |
| `PreferredWidth` | `double` | `0` | Preferred width (0 = system default) |
| `ResignsFirstResponderWithCancel` | `bool` | `true` | Escape key collapses the field |
| `Placement` | `MacOSToolbarItemPlacement` | `Content` | Toolbar area placement |

### Events

| Event | Args | Description |
|-------|------|-------------|
| `TextChanged` | `string` | Fired as the user types |
| `SearchCommitted` | `string` | Fired when Enter is pressed |
| `SearchStarted` | `EventArgs` | Fired when the search field expands |
| `SearchEnded` | `EventArgs` | Fired when the search field collapses |

### Explicit Layout

```csharp
MacOSToolbar.SetSidebarLayout(page, new List<MacOSToolbarLayoutItem>
{
    MacOSToolbarLayoutItem.Search(search),
    MacOSToolbarLayoutItem.FlexibleSpace,
    MacOSToolbarLayoutItem.Item(filterButton),
});
```

---

## Menu (NSMenuToolbarItem)

A toolbar button with a dropdown menu — like sort/filter menus in Finder.

```csharp
var menu = new MacOSMenuToolbarItem
{
    Text = "Sort",
    Icon = "arrow.up.arrow.down",
    ShowsIndicator = true,
    Items = new List<MacOSMenuItem>
    {
        new MacOSMenuItem
        {
            Text = "Name",
            Icon = "textformat",
            KeyEquivalent = "n",
        },
        new MacOSMenuItem
        {
            Text = "Date Modified",
            Icon = "calendar",
            IsChecked = true,
        },
        new MacOSMenuItem { IsSeparator = true },
        new MacOSMenuItem
        {
            Text = "Ascending",
            IsChecked = true,
        },
        new MacOSMenuItem { Text = "Descending" },
    }
};

// Handle clicks on individual menu items
menu.Items[0].Clicked += (s, e) => SortBy("name");
menu.Items[1].Clicked += (s, e) => SortBy("date");

MacOSToolbar.SetMenuItems(page, new List<MacOSMenuToolbarItem> { menu });
```

### Submenus

```csharp
new MacOSMenuItem
{
    Text = "View As",
    SubItems = new List<MacOSMenuItem>
    {
        new MacOSMenuItem { Text = "Icons" },
        new MacOSMenuItem { Text = "List" },
        new MacOSMenuItem { Text = "Columns" },
    }
}
```

### MacOSMenuToolbarItem Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Text` | `string` | `""` | Button label |
| `Icon` | `string?` | `null` | SF Symbol name |
| `ShowsIndicator` | `bool` | `true` | Show dropdown chevron |
| `ShowsTitle` | `bool` | `false` | Display icon and text side-by-side in the toolbar button |
| `Items` | `List<MacOSMenuItem>` | `[]` | Menu items |
| `Placement` | `MacOSToolbarItemPlacement` | `Content` | Toolbar area |

### MacOSMenuItem Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Text` | `string` | `""` | Menu item text |
| `Icon` | `string?` | `null` | SF Symbol name |
| `IsEnabled` | `bool` | `true` | Enabled state |
| `IsChecked` | `bool` | `false` | Show checkmark |
| `KeyEquivalent` | `string?` | `null` | Keyboard shortcut (e.g., `"n"`) |
| `Command` | `ICommand?` | `null` | Command binding |
| `CommandParameter` | `object?` | `null` | Command parameter |
| `SubItems` | `List<MacOSMenuItem>?` | `null` | Nested submenu items |
| `IsSeparator` | `bool` | `false` | Render as a separator line |

### MacOSMenuItem Events

| Event | Description |
|-------|-------------|
| `Clicked` | Fired when the menu item is selected |

---

## Segmented Control / Group (NSToolbarItemGroup)

A group of buttons that can act as a segmented control (like view mode switchers).

```csharp
var viewMode = new MacOSToolbarItemGroup
{
    Label = "View",
    SelectionMode = MacOSToolbarGroupSelectionMode.SelectOne,
    Representation = MacOSToolbarGroupRepresentation.Automatic,
    Segments = new List<MacOSToolbarGroupSegment>
    {
        new() { Text = "Icons", Icon = "square.grid.2x2" },
        new() { Text = "List", Icon = "list.bullet" },
        new() { Text = "Columns", Icon = "rectangle.split.3x1" },
    },
    SelectedIndex = 0,
};

viewMode.SelectionChanged += (s, e) =>
{
    Console.WriteLine($"Selected index: {e.SelectedIndex}");
    // e.SelectedSegments contains all selected indices (for SelectAny mode)
};

MacOSToolbar.SetItemGroups(page, new List<MacOSToolbarItemGroup> { viewMode });
```

### Selection Modes

| Mode | Description |
|------|-------------|
| `SelectOne` | Radio-button behavior — exactly one segment selected |
| `SelectAny` | Checkbox behavior — multiple segments can be selected |
| `Momentary` | Push-button behavior — no persistent selection |

### Representation

| Mode | Description |
|------|-------------|
| `Automatic` | AppKit chooses expanded or collapsed based on space |
| `Expanded` | Always show all segments inline |
| `Collapsed` | Show as a single button with dropdown |

### MacOSToolbarItemGroup Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Segments` | `List<MacOSToolbarGroupSegment>` | `[]` | Segment definitions |
| `SelectionMode` | `MacOSToolbarGroupSelectionMode` | `SelectOne` | Selection behavior |
| `Representation` | `MacOSToolbarGroupRepresentation` | `Automatic` | Visual representation |
| `SelectedIndex` | `int` | `0` | Currently selected index |
| `Label` | `string` | `""` | Accessibility label |
| `Placement` | `MacOSToolbarItemPlacement` | `Content` | Toolbar area |

### MacOSToolbarGroupSegment Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Text` | `string` | `""` | Segment label |
| `Icon` | `string?` | `null` | SF Symbol name |
| `Label` | `string?` | `null` | Accessibility label |
| `IsSelected` | `bool` | `false` | Initial selection state |
| `IsEnabled` | `bool` | `true` | Enabled state |

### Events

| Event | Args | Description |
|-------|------|-------------|
| `SelectionChanged` | `MacOSToolbarGroupSelectionChangedEventArgs` | Selection changed |

---

## Share (NSSharingServicePickerToolbarItem)

A native share button that opens the macOS share sheet.

```csharp
var share = new MacOSShareToolbarItem
{
    Label = "Share",
    ShareItemsProvider = () => new object[]
    {
        "Check out this app!",
        new Uri("https://example.com"),
    },
};

share.ServiceChosen += (s, e) =>
{
    Console.WriteLine("User chose a sharing service");
};

MacOSToolbar.SetShareItem(page, share);
```

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ShareItemsProvider` | `Func<object[]>?` | `null` | Returns items to share (strings, URIs) |
| `Label` | `string` | `"Share"` | Accessibility label |
| `Placement` | `MacOSToolbarItemPlacement` | `Content` | Toolbar area |

### Events

| Event | Description |
|-------|-------------|
| `ServiceChosen` | Fired when the user selects a sharing service |

---

## Popup Button (NSPopUpButton)

A popup or pull-down menu button in the toolbar.

```csharp
var format = new MacOSPopUpToolbarItem
{
    Items = new List<string> { "Rich Text", "Plain Text", "Markdown", "HTML" },
    SelectedIndex = 0,
    PullsDown = false,
    Width = 140,
};

format.SelectionChanged += (s, e) =>
{
    Console.WriteLine($"Format: {format.Items[e.SelectedIndex]}");
};

MacOSToolbar.SetPopUpItems(page, new List<MacOSPopUpToolbarItem> { format });
```

### Pull-Down vs. Pop-Up

- **Pop-Up** (`PullsDown = false`): Shows the selected item. Clicking reveals all options.
- **Pull-Down** (`PullsDown = true`): Shows a fixed title. Clicking reveals a menu of actions.

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Items` | `List<string>` | `[]` | Menu item labels |
| `SelectedIndex` | `int` | `0` | Currently selected index |
| `PullsDown` | `bool` | `false` | Pull-down menu style |
| `Width` | `double` | `120` | Button width in points |
| `Placement` | `MacOSToolbarItemPlacement` | `Content` | Toolbar area |

### Events

| Event | Args | Description |
|-------|------|-------------|
| `SelectionChanged` | `MacOSToolbarGroupSelectionChangedEventArgs` | Selection changed |

### Showing Icon + Title

By default, menu toolbar items show only the icon. Set `ShowsTitle = true` to display both:

```csharp
var menu = new MacOSMenuToolbarItem
{
    Text = "My Identity",
    Icon = "apple.logo",
    ShowsTitle = true,
    Items = new List<MacOSMenuItem>
    {
        new MacOSMenuItem { Text = "Profile" },
        new MacOSMenuItem { Text = "Sign Out" },
    }
};
```

---

## Custom View (MacOSViewToolbarItem)

Embed any MAUI `View` in the toolbar. The view is measured and arranged at the toolbar's standard height (28pt).

```csharp
var viewItem = new MacOSViewToolbarItem
{
    Label = "Progress",
    View = new HorizontalStackLayout
    {
        Spacing = 6,
        Padding = new Thickness(8, 0),
        VerticalOptions = LayoutOptions.Center,
        Children =
        {
            new Label { Text = "Build:", FontSize = 12, VerticalOptions = LayoutOptions.Center },
            new ProgressBar { Progress = 0.6, WidthRequest = 80, VerticalOptions = LayoutOptions.Center },
            new Label { Text = "60%", FontSize = 12, VerticalOptions = LayoutOptions.Center },
        }
    },
};

MacOSToolbar.SetViewItems(page, new List<MacOSViewToolbarItem> { viewItem });
```

### Button Style (Hover/Click States)

Set `ShowsToolbarButtonStyle = true` to wrap the view in a native toolbar button that provides hover highlighting and click states:

```csharp
var viewItem = new MacOSViewToolbarItem
{
    Label = "Status",
    ShowsToolbarButtonStyle = true,
    View = new Label
    {
        Text = "Ready",
        Padding = new Thickness(8, 0),
        VerticalTextAlignment = TextAlignment.Center,
    },
};

viewItem.Clicked += (s, e) =>
{
    Console.WriteLine("Custom view toolbar item clicked");
};
```

When `ShowsToolbarButtonStyle` is `false` (default), the MAUI view is placed directly — handle interactions via MAUI gesture recognizers or interactive controls (buttons, etc.) within the view.

### Sizing

The toolbar item auto-sizes to the MAUI view's measured width. Use `MinWidth` and `MaxWidth` to constrain:

```csharp
var viewItem = new MacOSViewToolbarItem
{
    MinWidth = 100,  // minimum width in points
    MaxWidth = 300,  // maximum width in points
    View = myView,
};
```

The MAUI view controls all internal padding — the handler adds none.

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `View` | `View?` | `null` | The MAUI view to display |
| `Label` | `string?` | `null` | Label for customization palette and overflow |
| `MinWidth` | `double` | `0` | Minimum width (0 = auto) |
| `MaxWidth` | `double` | `0` | Maximum width (0 = no limit) |
| `ShowsToolbarButtonStyle` | `bool` | `false` | Wrap in native toolbar button for hover/click |
| `Placement` | `MacOSToolbarItemPlacement` | `Content` | Toolbar area |

### Events

| Event | Description |
|-------|-------------|
| `Clicked` | Fired when clicked (requires `ShowsToolbarButtonStyle = true`) |

---

## Using in Explicit Layouts

All item types can be positioned in explicit layouts:

```csharp
MacOSToolbar.SetContentLayout(page, new List<MacOSToolbarLayoutItem>
{
    MacOSToolbarLayoutItem.Search(search),
    MacOSToolbarLayoutItem.FlexibleSpace,
    MacOSToolbarLayoutItem.Title,
    MacOSToolbarLayoutItem.FlexibleSpace,
    MacOSToolbarLayoutItem.Menu(sortMenu),
    MacOSToolbarLayoutItem.Group(viewMode),
    MacOSToolbarLayoutItem.Share(share),
    MacOSToolbarLayoutItem.PopUp(format),
});
```

## Page-Level Attached Properties

Set these on a `ContentPage` to add items to the toolbar:

| Property | Type | Description |
|----------|------|-------------|
| `MacOSToolbar.SearchItem` | `MacOSSearchToolbarItem?` | Search field |
| `MacOSToolbar.MenuItems` | `IList<MacOSMenuToolbarItem>?` | Menu buttons |
| `MacOSToolbar.ItemGroups` | `IList<MacOSToolbarItemGroup>?` | Segmented controls |
| `MacOSToolbar.ShareItem` | `MacOSShareToolbarItem?` | Share button |
| `MacOSToolbar.PopUpItems` | `IList<MacOSPopUpToolbarItem>?` | Popup buttons |
| `MacOSToolbar.ViewItems` | `IList<MacOSViewToolbarItem>?` | Custom MAUI views |
| `MacOSToolbar.SidebarLayout` | `IList<MacOSToolbarLayoutItem>?` | Explicit sidebar layout |
| `MacOSToolbar.ContentLayout` | `IList<MacOSToolbarLayoutItem>?` | Explicit content layout |
