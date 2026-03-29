# Handler Property Wiring Audit Tracker

Last updated: 2026-02-18 (+ MenuBar navigation fix, ToolbarItems in nav bar, rendering bug fixes)

## Purpose
Track MAUI handler property/command wiring parity work for `src/Platform.Maui.Linux.Gtk4/Handlers`, including visual controls and container/navigation handlers.

## Audit Snapshot
- Handlers reviewed: **42** (was 34 at start)
- New handlers added: **6** (ShellHandler, RefreshViewHandler, SwipeViewHandler, CarouselViewHandler, IndicatorViewHandler, GtkMenuBarManager)
- Handlers with mapper/command parity gaps vs MAUI reference handlers: **1** (CollectionView DataTemplate rendering)
- Missing mapper keys identified: **0** (all handler mapper keys wired)
- Missing command keys identified: **0**

## Status Legend
- `Not started`: no implementation work started
- `In progress`: partial implementation or active PR
- `Done`: handler parity item completed and validated

## Priority Workstreams
### P0 - Core functionality gaps
- [ ] **CollectionViewHandler** — missing mapper keys: `26`, missing command keys: `0` _(Status: In progress)_
- [x] **WebViewHandler** — missing mapper keys: `0`, missing command keys: `0` _(Status: Done)_
- [x] **ImageHandler** — missing mapper keys: `0`, missing command keys: `0` _(Status: Done)_
- [x] **ImageButtonHandler** — missing mapper keys: `0`, missing command keys: `0` _(Status: Done)_
- [x] **RefreshViewHandler** — NEW handler: `4` mapper keys (Content, IsRefreshing, RefreshColor, IsEnabled) _(Status: Done)_
- [x] **SwipeViewHandler** — NEW handler: `5` mapper keys (Content, Left/Right/Top/BottomItems no-ops) _(Status: Done)_
- [x] **CarouselViewHandler** — NEW handler: `9` mapper keys (ItemsSource, Position, CurrentItem, Loop, etc.) _(Status: Done)_
- [x] **IndicatorViewHandler** — NEW handler: `8` mapper keys (Count, Position, IndicatorColor, etc.) _(Status: Done)_
- [x] **MenuBar/ToolbarItem** — GtkMenuBarManager builds PopoverMenuBar + HeaderBar from MAUI items _(Status: Done)_
- [x] **ShellHandler** — NEW handler: `12` mapper keys (CurrentItem, Items, FlyoutItems, FlyoutHeader/Footer/Background/Behavior/IsPresented/Width/Icon) _(Status: Done)_

### Essentials Fixes
- [x] **LinuxScreenshot** — Fixed dimensions (was hardcoded 0, now uses window width/height) _(Status: Done)_
- [x] **LinuxAppActions** — Implemented as in-memory store with IsSupported=true _(Status: Done)_

### P1 - Input and text parity
- [x] **EntryHandler** — missing mapper keys: `0`, missing command keys: `0` _(Status: Done)_
- [x] **EditorHandler** — missing mapper keys: `0`, missing command keys: `0` _(Status: Done)_
- [x] **SearchBarHandler** — missing mapper keys: `3`, missing command keys: `0` _(Status: Done — 3 keys not on ISearchBar interface)_
- [x] **PickerHandler** — missing mapper keys: `0`, missing command keys: `0` _(Status: Done)_
- [x] **DatePickerHandler** — missing mapper keys: `1`, missing command keys: `0` _(Status: Done — IsOpen not applicable on desktop)_
- [x] **TimePickerHandler** — missing mapper keys: `1`, missing command keys: `0` _(Status: Done — IsOpen not applicable on desktop)_

### P2 - Styling and polish
- [x] **ButtonHandler** — missing mapper keys: `1`, missing command keys: `0` _(Status: Done — Source needs image loader)_
- [x] **RadioButtonHandler** — missing mapper keys: `0`, missing command keys: `0` _(Status: Done)_
- [x] **SliderHandler** — missing mapper keys: `1`, missing command keys: `0` _(Status: Done — ThumbImageSource needs image loader)_
- [x] **SwitchHandler** — missing mapper keys: `0`, missing command keys: `0` _(Status: Done)_
- [x] **ProgressBarHandler** — missing mapper keys: `0`, missing command keys: `0` _(Status: Done)_
- [x] **LabelHandler** — missing mapper keys: `0`, missing command keys: `0` _(Status: Done)_
- [x] **ActivityIndicatorHandler** — missing mapper keys: `0`, missing command keys: `0` _(Status: Done)_
- [x] **CheckBoxHandler** — missing mapper keys: `0`, missing command keys: `0` _(Status: Done)_
- [x] **ScrollViewHandler** — missing mapper keys: `0`, missing command keys: `0` _(Status: Done)_
- [x] **LayoutHandler** — missing mapper keys: `0`, missing command keys: `0` _(Status: Done)_

### P3 - Advanced shape/border/window parity
- [x] **BorderHandler** — missing mapper keys: `0`, missing command keys: `0` _(Status: Done — dash/cap/join are CSS no-ops)_
- [x] **ShapeViewHandler** — missing mapper keys: `0`, missing command keys: `0` _(Status: Done — Cairo stroke properties wired)_
- [x] **WindowHandler** — missing mapper keys: `0`, missing command keys: `0` _(Status: Done — X/Y no-op on Wayland)_

## Mapped-But-Empty Implementations (partial wiring)
- [x] `CheckBoxHandler.MapForeground` _(Status: Done)_
- [x] `LayoutHandler.MapBackground` _(Status: Done)_
- [x] `ScrollViewHandler.MapHorizontalScrollBarVisibility` _(Status: Done)_
- [x] `ScrollViewHandler.MapVerticalScrollBarVisibility` _(Status: Done)_
- [x] `SwitchHandler.MapTrackColor` _(Status: Done)_
- [x] `SwitchHandler.MapThumbColor` _(Status: Done)_

## Detailed Gaps by Handler (vs MAUI reference handlers)
### ActivityIndicatorHandler
- Reference handler: `Microsoft.Maui.Handlers.ActivityIndicatorHandler`
- Missing mapper keys (1):
  - `Color`
- Missing command keys (0):
  - _(none)_
- Status: `Not started`

### BorderHandler
- Reference handler: `Microsoft.Maui.Handlers.BorderHandler`
- Missing mapper keys (0):
  - _(none — StrokeDashOffset, StrokeDashPattern, StrokeLineCap, StrokeLineJoin, StrokeMiterLimit all wired as no-ops; CSS borders don't support these)_
- Missing command keys (0):
  - _(none)_
- Status: `Done`

### ButtonHandler
- Reference handler: `Microsoft.Maui.Handlers.ButtonHandler`
- Missing mapper keys (1):
  - `Source` _(requires async image loading; not yet wired)_
- Missing command keys (0):
  - _(none)_
- Status: `Done` _(CharacterSpacing, CornerRadius, StrokeColor, StrokeThickness wired; Font was already mapped)_

### CollectionViewHandler
- Reference handler: `Microsoft.Maui.Controls.Handlers.Items.CollectionViewHandler`
- Missing mapper keys (26):
  - `BackgroundColor`, `BackgroundImageSource`, `CanReorderItems`, `Description`, `EmptyView`, `EmptyViewTemplate`, `ExcludedWithChildren`, `Footer`, `FooterTemplate`, `Header`, `HeaderTemplate`, `HeadingLevel`, `Hint`, `HorizontalScrollBarVisibility`, `IsGrouped`, `IsInAccessibleTree`, `IsVisible`, `ItemSizingStrategy`, `ItemsLayout`, `ItemsSource`, `ItemsUpdatingScrollMode`, `ItemTemplate`, `SelectedItem`, `SelectedItems`, `SelectionMode`, `VerticalScrollBarVisibility`
- Missing command keys (0):
  - _(none)_
- Status: `In progress` _(compile blocker fixed; build green, parity work continues)_

### DatePickerHandler
- Reference handler: `Microsoft.Maui.Handlers.DatePickerHandler`
- Missing mapper keys (1):
  - `IsOpen` _(not applicable for desktop; picker uses click-to-open dialog)_
- Missing command keys (0):
  - _(none)_
- Status: `Done` _(CharacterSpacing, TextColor, MinimumDate, MaximumDate wired; Font was already mapped)_

### EditorHandler
- Reference handler: `Microsoft.Maui.Handlers.EditorHandler`
- Missing mapper keys (0):
  - _(none — CharacterSpacing, CursorPosition, HorizontalTextAlignment, MaxLength, SelectionLength, PlaceholderColor, VerticalTextAlignment, IsSpellCheckEnabled, IsTextPredictionEnabled, Keyboard all wired)_
- Missing command keys (0):
  - _(none)_
- Status: `Done`

### EntryHandler
- Reference handler: `Microsoft.Maui.Handlers.EntryHandler`
- Missing mapper keys (0):
  - _(none — CharacterSpacing, ClearButtonVisibility, CursorPosition, SelectionLength, ReturnType, VerticalTextAlignment, IsSpellCheckEnabled, IsTextPredictionEnabled, Keyboard all wired)_
- Missing command keys (0):
  - _(none)_
- Status: `Done`

### GraphicsViewHandler
- Reference handler: `Microsoft.Maui.Handlers.GraphicsViewHandler`
- Missing mapper keys (0):
  - _(none — Drawable wired)_
- Missing command keys (0):
  - _(none)_
- Status: `Done`

### ImageButtonHandler
- Reference handler: `Microsoft.Maui.Handlers.ImageButtonHandler`
- Missing mapper keys (0):
  - _(none)_
- Missing command keys (0):
  - _(none)_
- Status: `Done`

### ImageHandler
- Reference handler: `Microsoft.Maui.Handlers.ImageHandler`
- Missing mapper keys (0):
  - _(none)_
- Missing command keys (0):
  - _(none)_
- Status: `Done`

### LabelHandler
- Reference handler: `Microsoft.Maui.Handlers.LabelHandler`
- Missing mapper keys (0):
  - _(none — CharacterSpacing, LineHeight, VerticalTextAlignment all wired)_
- Missing command keys (0):
  - _(none)_
- Status: `Done`

### PageHandler
- Reference handler: `Microsoft.Maui.Handlers.PageHandler`
- Missing mapper keys (0):
  - _(none — Title wired)_
- Missing command keys (0):
  - _(none)_
- Status: `Done`

### PickerHandler
- Reference handler: `Microsoft.Maui.Handlers.PickerHandler`
- Missing mapper keys (0):
  - _(none — CharacterSpacing, HorizontalTextAlignment, TextColor, TitleColor, VerticalTextAlignment, IsOpen all wired/no-op)_
- Missing command keys (0):
  - _(none)_
- Status: `Done`

### ProgressBarHandler
- Reference handler: `Microsoft.Maui.Handlers.ProgressBarHandler`
- Missing mapper keys (0):
  - _(none — ProgressColor wired via CSS)_
- Missing command keys (0):
  - _(none)_
- Status: `Done`

### RadioButtonHandler
- Reference handler: `Microsoft.Maui.Handlers.RadioButtonHandler`
- Missing mapper keys (0):
  - _(none — CharacterSpacing, CornerRadius, StrokeColor, StrokeThickness, TextColor all wired; Font was already mapped)_
- Missing command keys (0):
  - _(none)_
- Status: `Done`

### SearchBarHandler
- Reference handler: `Microsoft.Maui.Handlers.SearchBarHandler`
- Missing mapper keys (3):
  - `ReturnType`, `SearchIconColor`, `Keyboard` _(not available on ISearchBar / GTK SearchEntry)_
- Missing command keys (0):
  - _(none)_
- Status: `In progress` _(CharacterSpacing, HorizontalTextAlignment, IsReadOnly, MaxLength, PlaceholderColor, CancelButtonColor, VerticalTextAlignment, IsSpellCheckEnabled, IsTextPredictionEnabled wired)_

### ShapeViewHandler
- Reference handler: `Microsoft.Maui.Handlers.ShapeViewHandler`
- Missing mapper keys (0):
  - _(none — StrokeDashOffset, StrokeDashPattern, StrokeLineCap, StrokeLineJoin, StrokeMiterLimit all wired with Cairo stroke rendering)_
- Missing command keys (0):
  - _(none)_
- Status: `Done`

### SliderHandler
- Reference handler: `Microsoft.Maui.Handlers.SliderHandler`
- Missing mapper keys (1):
  - `ThumbImageSource` _(requires async image loading; not yet wired)_
- Missing command keys (0):
  - _(none)_
- Status: `Done` _(MaximumTrackColor, MinimumTrackColor, ThumbColor wired via CSS)_

### TimePickerHandler
- Reference handler: `Microsoft.Maui.Handlers.TimePickerHandler`
- Missing mapper keys (1):
  - `IsOpen` _(not applicable for desktop; picker uses click-to-open dialog)_
- Missing command keys (0):
  - _(none)_
- Status: `Done` _(CharacterSpacing, TextColor wired; Font was already mapped)_

### WebViewHandler
- Reference handler: `Microsoft.Maui.Handlers.WebViewHandler`
- Missing mapper keys (0):
  - _(none)_
- Missing command keys (0):
  - _(none)_
- Status: `Done`

### WindowHandler
- Reference handler: `Microsoft.Maui.Handlers.WindowHandler`
- Missing mapper keys (0):
  - _(none — Width/Height wired via SetDefaultSize; X/Y are no-ops on Wayland)_
- Missing command keys (0):
  - _(none)_
- Status: `Done`

## Handlers with No Mapper/Command Gaps (current parity baseline)
- `ApplicationHandler`
- `BoxViewHandler`
- `CheckBoxHandler`
- `ContentViewHandler`
- `FlyoutPageHandler`
- `FrameHandler`
- `LayoutHandler`
- `NavigationPageHandler`
- `ScrollViewHandler`
- `ShapeHandler`
- `StepperHandler`
- `SwitchHandler`
- `TabbedPageHandler`

## Notes
- Some MAUI properties are intentionally unsupported by GTK or current architecture; mark those as `Done` with rationale when triaged.
- Re-run audit script after significant handler updates and refresh this file.
