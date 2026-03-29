# Platform.Maui.Linux.Gtk4 — Backend Implementation Checklist

Implementation status of the .NET MAUI backend for Linux using GTK4. Adapted from the generic MAUI backend checklist.

> ✅ = Implemented  |  ⚠️ = Partial  |  ❌ = Not implemented  |  N/A = Not applicable to Linux/GTK4

---

## 1. Core Infrastructure

### Platform Abstractions
- [x] **Platform View Type** — `Gtk.Widget` as the native view abstraction via `GtkViewHandler<TVirtualView, TPlatformView>`
- [x] **Platform Context** — `GtkMauiContext` with DI scope, handler factory, window scope management
- [x] **Dispatcher** — `GtkDispatcherProvider` using `GLib.Functions.IdleAdd` for UI thread marshalling
- [x] **Event System** — GTK4 signal/event model for user interactions (clicks, input, gestures)
- [x] **Handler Factory Integration** — All handlers registered via `ConfigureMauiHandlers` in `AppHostBuilderExtensions`
- [x] **App Host Builder Extension** — `UseLinuxGtkHandlers()` wires up all handlers and services

### Rendering Pipeline
- [x] **View Renderer** — `GtkViewHandler` base traverses virtual view tree, creates/connects GTK widgets
- [x] **Property Change Propagation** — `IPropertyMapper` re-maps views when `IView` property changes fire
- [x] **Child Synchronization** — `GtkLayoutPanel` handles add/remove/reorder of child views via `CustomLayout` (P/Invoke `gtk_custom_layout_new`, replacing `Gtk.Fixed`'s `FixedLayout`)
- [x] **Style/Attribute Application** — CSS-based styling via `ApplyCss()` helper for colors, fonts, sizes
- [x] **Layout Negotiation** — `GtkLayoutPanel` uses `SetArrangedSize` + `ApplyArrangedSizes` (IdleAdd priority 121) to override GTK's minimum-based allocation with MAUI-arranged sizes

### Native Interop
- [x] **GTK4 Signal Handling** — Native GTK signal connections for widget events
- [x] **Gesture Controllers** — `Gtk.GestureClick`, `Gtk.GestureDrag`, `Gtk.EventControllerMotion`
- [x] **Keyboard Event Handling** — GTK `Gtk.EventControllerKey` for Entry/Editor key events
- [x] **Scroll Handling** — `Gtk.ScrolledWindow` for scroll containers, P/Invoke for scroll policy (`gtk_scrollable_set_vscroll_policy`)
- [x] **P/Invoke Interop** — Direct native calls for fontconfig (`libfontconfig.so.1`), Pango (`libpangocairo-1.0.so.0`, `libpangoft2-1.0.so.0`), Cairo (`libcairo.so.2`), and GTK4 custom layout/scrollable APIs

### Build & Packaging
- [x] **MSBuild Targets** — `buildTransitive/` props and targets for MauiAsset, MauiImage, MauiIcon, MauiFont, MauiSplashScreen resource deployment
- [x] **Hicolor Icon Theme** — Linux icon theme integration for app icons via build targets
- [x] **Project Templates** — `dotnet new` template (`maui-linux-gtk4-app`) with App.cs, MainPage.cs, MauiProgram.cs, Program.cs scaffolding
- [x] **XAML Support** — Runtime XAML page loading works (demonstrated by XamlRuntimePage.xaml with code-behind)

---

## 2. Application & Window

| Control | Status | Notes |
|---------|--------|-------|
| ✅ **Application** | Done | App lifecycle via `GtkMauiApplication`, `Gtk.Application` integration |
| ✅ **Window** | Done | Title, Width/Height mapping, default size 1024×768, min 800×600, Activated/Deactivated events, native modal dialog windows via `PushModalAsync` with sizing options (`GtkPage` attached properties). X/Y positioning disabled (Wayland/X11 compositor constraints) |
| ✅ **Multi-Window** | Done | `Application.OpenWindow`/`CloseWindow`, `Application.Windows` collection tracking, window lifecycle events (Creating, Destroying, Activated, Deactivated) |

---

## 3. Pages

| Page | Status | Notes |
|------|--------|-------|
| ✅ **ContentPage** | Done | Content rendering, Title, MenuBarItems via `GtkMenuBarManager` |
| ✅ **NavigationPage** | Done | Push/pop stack, back button, title bar, ToolbarItems propagation |
| ✅ **TabbedPage** | Done | `Gtk.Notebook` tab bar, tab selection, content switching |
| ✅ **FlyoutPage** | Done | `Gtk.Paned` flyout panel with toggle, resizable divider |
| ✅ **Shell** | Done | Flyout via `Gtk.ListBox`, tab bars via `Gtk.Notebook`, ShellItem/Section/Content hierarchy |

---

## 4. Layouts

| Layout | Status | Notes |
|--------|--------|-------|
| ✅ **VerticalStackLayout** | Done | `GtkLayoutPanel` with vertical `CrossPlatformMeasure`/`CrossPlatformArrange` |
| ✅ **HorizontalStackLayout** | Done | `GtkLayoutPanel` with horizontal layout |
| ✅ **Grid** | Done | Row/column definitions (Absolute, Star, Auto), spans, spacing |
| ✅ **FlexLayout** | Done | Handled by generic `LayoutHandler` — MAUI cross-platform layout engine does measure/arrange |
| ✅ **AbsoluteLayout** | Done | Handled by generic `LayoutHandler` — absolute and proportional positioning |
| ✅ **ScrollView** | Done | `Gtk.ScrolledWindow` with orientation support, `ScrollBarVisibility` mapping, `ScrollToAsync` via vadjustment, `Scrolled` event via value-changed signal, `RequestScrollTo` command mapper. P/Invoke for NATURAL scroll policy to enable proper scrolling in Viewport |
| ✅ **ContentView** | Done | Simple content wrapper |
| ✅ **Border** | Done | Stroke, StrokeThickness, StrokeShape, padding, content via Cairo/CSS |
| ✅ **Frame** | Done | Legacy border with CornerRadius, HasShadow, padding |

---

## 5. Basic Controls

| Control | Status | Notes |
|---------|--------|-------|
| ✅ **Label** | Done | Text, TextColor, TextAlignment (H & V), LineBreakMode, MaxLines, LineHeight, CharacterSpacing, TextDecorations, FontAttributes, Pango markup for FormattedText |
| ✅ **Button** | Done | Text, Command, Clicked, BackgroundColor, TextColor, BorderColor, BorderWidth, CornerRadius, CharacterSpacing, font properties, ImageSource (partial — mapping wired but incomplete) |
| ✅ **ImageButton** | Done | Source, Command, Clicked, BackgroundColor, BorderColor, CornerRadius, padding, Aspect, StrokeColor, StrokeThickness |
| ✅ **Entry** | Done | Text, Placeholder, PlaceholderColor, IsPassword, Keyboard, ReturnType, ReturnCommand, TextChanged, CharacterSpacing, ClearButtonVisibility, MaxLength, IsReadOnly, CursorPosition, SelectionLength, HorizontalTextAlignment, VerticalTextAlignment. ⚠️ IsSpellCheckEnabled/IsTextPredictionEnabled are no-ops (GTK limitation) |
| ✅ **Editor** | Done | Text, Placeholder, PlaceholderColor, TextChanged, AutoSize, CharacterSpacing, MaxLength (enforced in handler), IsReadOnly, CursorPosition, SelectionLength, HorizontalTextAlignment, VerticalTextAlignment, Keyboard. ⚠️ Placeholder not natively supported; IsSpellCheckEnabled/IsTextPredictionEnabled are no-ops |
| ✅ **Switch** | Done | `Gtk.Switch` — IsToggled, OnColor, ThumbColor, Toggled event, TrackColor |
| ✅ **CheckBox** | Done | `Gtk.CheckButton` — IsChecked, Color, CheckedChanged |
| ✅ **RadioButton** | Done | `Gtk.CheckButton` with group — IsChecked, GroupName, Content, mutual exclusion. Group management via `WeakReference<Gtk.CheckButton>` dictionary. CornerRadius, StrokeColor, StrokeThickness, CharacterSpacing, TextColor, font properties |
| ✅ **Slider** | Done | `Gtk.Scale` — Value, Min, Max, MinimumTrackColor, MaximumTrackColor, ThumbColor. ⚠️ ThumbImageSource mapped but not yet rendered |
| ✅ **Stepper** | Done | `Gtk.SpinButton` — Value, Min, Max, Increment, ValueChanged |
| ✅ **ProgressBar** | Done | `Gtk.ProgressBar` — Progress, ProgressColor |
| ✅ **ActivityIndicator** | Done | `Gtk.Spinner` — IsRunning, Color |
| ✅ **BoxView** | Done | Color, CornerRadius, WidthRequest, HeightRequest |
| ✅ **Image** | Done | Source (URL, file, stream), Aspect (AspectFit, AspectFill, Fill) |

---

## 6. Input & Selection Controls

| Control | Status | Notes |
|---------|--------|-------|
| ✅ **Picker** | Done | `Gtk.DropDown` — ItemsSource, SelectedIndex, SelectedItem, SelectedIndexChanged, CharacterSpacing, HorizontalTextAlignment, TextColor, TitleColor, VerticalTextAlignment, font properties. ⚠️ Title not directly exposed by DropDown |
| ✅ **DatePicker** | Done | Date, MinimumDate, MaximumDate, Format, DateSelected, CharacterSpacing, TextColor, font properties. Button launches modal `Gtk.Calendar` dialog |
| ✅ **TimePicker** | Done | Time, Format, CharacterSpacing, TextColor, font properties. Button launches modal dialog with hour/minute `Gtk.SpinButton` |
| ✅ **SearchBar** | Done | `Gtk.SearchEntry` — Text, Placeholder, SearchCommand, SearchButtonPressed, TextChanged, MaxLength (enforced in handler), IsReadOnly, HorizontalTextAlignment, VerticalTextAlignment, PlaceholderColor, CancelButtonColor, CharacterSpacing. ⚠️ IsSpellCheckEnabled/IsTextPredictionEnabled are no-ops |

---

## 7. Collection Controls

| Control | Status | Notes |
|---------|--------|-------|
| ✅ **CollectionView** | Done | `Gtk.ListView` — ItemsSource, SelectionMode (None/Single/Multiple), SelectedItem/SelectedItems, ItemTemplate/DataTemplateSelector (native GTK rendering via MAUI handler pipeline), IsGrouped + GroupHeaderTemplate, virtualization via GTK4 widget recycling, ScrollTo, EmptyView fallback, Header/Footer, ScrollBarVisibility, ItemSizingStrategy, ItemsUpdatingScrollMode, `INotifyCollectionChanged` live updates, `IsExternallyManaged` flag for nested layouts. ⚠️ Missing: `RemainingItemsThreshold`, `Scrolled` event, `SnapPoints`, `CanReorderItems` (mapped but not wired) |
| ✅ **ListView** | Done | Handler with ItemsSource, ItemTemplate (ViewCell/TextCell/SwitchCell/EntryCell), selection, Header/Footer, grouping with bold headers & separators, SeparatorVisibility, SeparatorColor, HasUnevenRows, RowHeight |
| ✅ **CarouselView** | Done | `Gtk.Stack` with animated transitions. Horizontal/vertical scrolling (via `LinearItemsLayout` direction detection), animated snap-to-item, swipe gesture navigation with direction-aware transitions (SlideLeft/Right for horizontal, SlideUp/Down for vertical), Loop (with modulo wrapping), PeekAreaInsets, Position ↔ CurrentItem bidirectional sync |
| ✅ **IndicatorView** | Done | Cairo-drawn dots with IndicatorColor, SelectedIndicatorColor, IndicatorSize, IndicatorsShape (circle/square), MaximumVisible, HideSingle, click-to-navigate on dots |
| ✅ **TableView** | Done | Handler with TableRoot/TableSection, TextCell, SwitchCell, EntryCell, ViewCell rendering |
| ✅ **SwipeView** | Done | Swipe-to-reveal action buttons via Gtk.Fixed + GestureDrag, LeftItems/RightItems with styled buttons, auto-close on action invoke |
| ✅ **RefreshView** | Done | Desktop-friendly: spinner + refresh button, IsRefreshing, RefreshColor, Command |

---

## 8. Navigation & Routing

| Feature | Status | Notes |
|---------|--------|-------|
| ✅ **NavigationPage stack** | Done | PushAsync, PopAsync, PopToRootAsync |
| ✅ **Shell navigation** | Done | Flyout, tabs, content hierarchy, GoToAsync route navigation, query parameters via `[QueryProperty]` |
| N/A **URL/route-based routing** | N/A | Not applicable to desktop Linux apps |
| N/A **Route ↔ Shell sync** | N/A | Browser-specific concept |
| N/A **Non-Shell routing** | N/A | Browser-specific concept |
| ✅ **Back button** | Done | Nav bar back button with stack awareness |
| ✅ **ToolbarItems** | Done | Primary toolbar items rendered as flat buttons in NavigationPage nav bar |
| ✅ **Hamburger button** | Done | Automatically shown in NavigationPage when nested inside FlyoutPage; toggles `IsPresented` |

---

## 9. Alerts & Dialogs

| Dialog | Status | Notes |
|--------|--------|-------|
| ✅ **DisplayAlert** | Done | Custom GTK Window dialog with Title, message, accept/cancel buttons |
| ✅ **DisplayActionSheet** | Done | Dialog with action buttons, destructive button styling, cancel |
| ✅ **DisplayPromptAsync** | Done | Text entry dialog with placeholder, initial value, validation |
| ✅ **Modal Pages** | Done | `PushModalAsync` uses native GTK4 modal `Gtk.Window` (default). `GtkPage` attached properties: `ModalPresentationStyle` (Dialog/Inline), `ModalSizesToContent`, `ModalWidth`/`ModalHeight`, `ModalMinWidth`/`ModalMinHeight` |

---

## 10. Gesture Recognizers

| Gesture | Status | Notes |
|---------|--------|-------|
| ✅ **TapGestureRecognizer** | Done | `Gtk.GestureClick` — single/double tap, Command, Tapped event |
| ✅ **PanGestureRecognizer** | Done | `Gtk.GestureDrag` — PanUpdated with StatusType, TotalX/TotalY |
| ✅ **SwipeGestureRecognizer** | Done | `Gtk.GestureSwipe` — velocity-based direction detection (Left/Right/Up/Down) |
| ✅ **PinchGestureRecognizer** | Done | `Gtk.GestureZoom` — scale delta tracking via `IPinchGestureController` |
| ✅ **PointerGestureRecognizer** | Done | `Gtk.EventControllerMotion` + `Gtk.GestureClick` — Enter/Move/Exit/Press/Release + Commands |

---

## 11. Graphics & Shapes

### Microsoft.Maui.Graphics
| Feature | Status | Notes |
|---------|--------|-------|
| ✅ **GraphicsView** | Done | Cairo-backed IDrawable rendering via `Gtk.DrawingArea`, `CairoCanvas` ICanvas implementation |
| ✅ **Canvas Operations** | Done | DrawLine, DrawRect, DrawEllipse, DrawArc, DrawPath, DrawString, FillRect, FillEllipse, FillPath |
| ✅ **Canvas State** | Done | SaveState, RestoreState, Translate, Rotate, Scale, Clipping (path & rectangular) |
| ✅ **Brushes** | Done | SolidColorBrush, LinearGradientBrush (CSS linear-gradient), RadialGradientBrush (CSS radial-gradient) |

### Shapes
| Shape | Status | Notes |
|-------|--------|-------|
| ✅ **Rectangle** | Done | Rounded corners via RadiusX/RadiusY |
| ✅ **Ellipse** | Done | Full ellipse and circle rendering |
| ✅ **Line** | Done | X1, Y1, X2, Y2 |
| ✅ **Path** | Done | PathGeometry with Line, Bezier, QuadraticBezier, Arc, PolyLine, PolyBezier, PolyQuadraticBezier segments. Geometry types: LineGeometry, RectangleGeometry, EllipseGeometry, PathGeometry, GeometryGroup. Fallback via `IShape.PathForBounds` |
| ✅ **Polygon** | Done | Points collection, closed shape |
| ✅ **Polyline** | Done | Points collection, open shape |
| ✅ **Fill & Stroke** | Done | SolidColorBrush fill/stroke, StrokeThickness, dash pattern, line cap/join, miter limit. ⚠️ CSS borders don't support custom dash patterns or line cap/join (Cairo shapes do) |

---

## 12. Common View Properties (Base Handler)

Every handler must support these properties mapped from the base `IView`:

### Visibility & State
- [x] Opacity — `MapOpacity` applies via CSS
- [x] IsVisible — `MapVisibility` toggles `Gtk.Widget.SetVisible()`
- [x] IsEnabled — `MapIsEnabled` toggles `Gtk.Widget.SetSensitive()`
- [x] InputTransparent — `MapInputTransparent` sets `Gtk.Widget.SetCanTarget()`

### Sizing
- [x] WidthRequest / HeightRequest — Used in `GetDesiredSize()` measurement
- [x] MinimumWidthRequest / MinimumHeightRequest — Used in measurement
- [x] MaximumWidthRequest / MaximumHeightRequest — Constrained in `GetDesiredSize()`

### Layout
- [x] HorizontalOptions / VerticalOptions — Handled by MAUI cross-platform layout
- [x] Margin — Handled by MAUI layout engine (cross-platform)
- [x] Padding — Mapped on individual handlers (Entry, Editor, Button, etc.)
- [x] FlowDirection (LTR, RTL) — `MapFlowDirection` sets `Gtk.TextDirection`
- [x] ZIndex — `MapZIndex` reorders GTK siblings via `InsertBefore`/`InsertAfter`

### Appearance
- [x] BackgroundColor / Background — `MapBackground` applies via CSS `background-color`

### Transforms
- [x] TranslationX / TranslationY — `Gsk.Transform.Translate()` via `SetChildTransform`
- [x] Rotation / RotationX / RotationY — `Gsk.Transform.Rotate()` via `SetChildTransform`
- [x] Scale / ScaleX / ScaleY — `Gsk.Transform.Scale()` via `SetChildTransform`
- [x] AnchorX / AnchorY — Translate-to-anchor pattern in transform chain

### Effects
- [x] Shadow — `MapShadow` applies via CSS `box-shadow`
- [x] Clip — `MapClip` applies CSS `border-radius` + `overflow:hidden` for RoundRectangleGeometry/EllipseGeometry

### Automation
- [x] AutomationId — `MapAutomationId` sets accessible name
- [x] Semantic properties — `MapSemantics` sets accessibility description

### Interactivity Attachments
- [x] **ToolTip** — `ToolTipProperties.Text` mapped to `Gtk.Widget.SetTooltipText()`
- [x] **ContextFlyout** — `FlyoutBase.GetContextFlyout()` → popup menu on right-click via `Gtk.PopoverMenu` + `GestureClick(button=3)`

> Note: All handler Mapper fields must chain from `GtkViewHandler.ViewMapper` (not `ViewHandler.ViewMapper`) to inherit transform, opacity, shadow, clip, tooltip, and context flyout property mappings.

---

## 13. VisualStateManager & Triggers

| Feature | Status | Notes |
|---------|--------|-------|
| ✅ **VisualStateManager** | Done | `GoToState` triggered by GTK EventControllerMotion/GestureClick/EventControllerFocus — Normal, PointerOver, Pressed, Disabled, Focused |
| ✅ **PropertyTrigger** | Done | Cross-platform — works via MAUI property change notifications, no platform handler needed |
| ✅ **DataTrigger** | Done | Cross-platform — works via MAUI data binding and property change notifications |
| ✅ **MultiTrigger** | Done | Cross-platform — works via MAUI property change notifications |
| ✅ **EventTrigger** | Done | Cross-platform — works via MAUI event system and TriggerAction |
| ✅ **Behaviors** | Done | Cross-platform — Behavior<T> OnAttachedTo/OnDetachingFrom works via MAUI Controls layer |

> Note: Many VSM scenarios "just work" because GTK4 natively handles hover, focus, active, and disabled states via its CSS engine.

---

## 14. Font Management

| Feature | Status | Notes |
|---------|--------|-------|
| ✅ **IFontManager** | Done | `GtkFontManager` resolves font family names, applies via Pango/CSS |
| ✅ **IFontRegistrar** | Done | `GtkFontRegistrar` registers embedded fonts with aliases |
| ✅ **IEmbeddedFontLoader** | Done | `FileSystemEmbeddedFontLoader` extracts fonts from assembly resources |
| ✅ **Font loading** | Done | Fonts installed to `~/.local/share/fonts/` + `pango_fc_font_map_config_changed` called to propagate to GTK4's Pango context. `EagerlyRegisterAllFonts()` runs on app activation before any widget rendering |
| ✅ **Font properties** | Done | FontSize, FontFamily, FontAttributes applied to Label, Button, Entry, Editor, etc. |
| ✅ **IFontNamedSizeService** | Done | `GtkFontNamedSizeService` registered via `DependencyService`. Maps `NamedSize` enum (Default=14, Micro=10, Small=12, Medium=14, Large=18, Body=14, Header=24, Title=20, Subtitle=16, Caption=12) |
| ✅ **FontImageSource** | Done | Font glyphs rendered via Cairo ImageSurface + Pango Layout → `cairo_surface_write_to_png` (P/Invoke `libcairo.so.2`) → `Gdk.Texture.NewFromFilename`. Supports Glyph, Color, Size, FontFamily. Works in Image, ImageButton, Button.ImageSource, ToolbarItems, etc. |

---

## 15. Essentials / Platform Services

| Service | Interface | Status | Notes |
|---------|-----------|--------|-------|
| ✅ **App Info** | `IAppInfo` | Done | App name, version, build, package name, theme (Tier 1 — Pure .NET) |
| ✅ **App Actions** | `IAppActions` | Done | Desktop quick actions (Tier 4 — Best-effort) |
| ✅ **Battery** | `IBattery` | Done | `/sys/class/power_supply` charge level, state, power source (Tier 3 — DBus) |
| ✅ **Browser** | `IBrowser` | Done | `xdg-open` for URLs (Tier 1 — Pure .NET) |
| ✅ **Clipboard** | `IClipboard` | Done | `Gdk.Clipboard` copy/paste, text MIME type detection (Tier 2 — GTK4) |
| ✅ **Connectivity** | `IConnectivity` | Done | `/proc/net/route` + `/sys/class/net` for WiFi/Ethernet detection (Tier 3 — DBus) |
| ✅ **Device Display** | `IDeviceDisplay` | Done | Monitor width, height, scale/density, orientation, refresh rate via `Gdk.Display` (Tier 2 — GTK4) |
| ✅ **Device Info** | `IDeviceInfo` | Done | Platform (Linux), Idiom (Desktop), model/manufacturer from DMI, OS version (Tier 1 — Pure .NET) |
| ✅ **Email** | `IEmail` | Done | `mailto:` URI composition via `xdg-open` (Tier 1 — Pure .NET) |
| ✅ **File Picker** | `IFilePicker` | Done | `Gtk.FileDialog` single/multiple file selection with MIME type filters (Tier 2 — GTK4) |
| ✅ **File System** | `IFileSystem` | Done | XDG data/cache directories, app package file access (Tier 1 — Pure .NET) |
| ⚠️ **Geolocation** | `IGeolocation` | Partial | GeoClue integration via `where-am-i` CLI — basic location only, no continuous tracking (Tier 3 — DBus) |
| ✅ **Launcher** | `ILauncher` | Done | `xdg-open` URI launching with scheme support checks (Tier 1 — Pure .NET) |
| ✅ **Map** | `IMap` | Done | Opens OpenStreetMap by coordinates/placemark (Tier 1 — Pure .NET) |
| ✅ **Media Picker** | `IMediaPicker` | Done | `Gtk.FileDialog` for photo/video selection (no capture support) (Tier 2 — GTK4) |
| ✅ **Preferences** | `IPreferences` | Done | JSON file-based key-value storage in `XDG_CONFIG_HOME` (thread-safe) (Tier 1 — Pure .NET) |
| ✅ **Secure Storage** | `ISecureStorage` | Done | AES-256 encrypted file storage in `XDG_DATA_HOME` (Tier 3 — DBus) |
| ✅ **Screenshot** | `IScreenshot` | Done | `Gtk.WidgetPaintable` window capture → PNG (Tier 2 — GTK4) |
| ✅ **Semantic Screen Reader** | `ISemanticScreenReader` | Done | `spd-say` (speech-dispatcher) for announcements (Tier 5 — Best-effort) |
| ✅ **Share** | `IShare` | Done | `xdg-open` for text/file sharing (Tier 4 — Best-effort) |
| ✅ **Text-to-Speech** | `ITextToSpeech` | Done | `spd-say` / `espeak-ng` / `espeak` with pitch/volume control (Tier 3 — DBus) |
| ✅ **Version Tracking** | `IVersionTracking` | Done | JSON file-based version history in `XDG_CONFIG_HOME` (Tier 1 — Pure .NET) |
| ⚠️ **Web Authenticator** | `IWebAuthenticator` | Partial | OAuth flow via local HTTP listener + `xdg-open` system browser (Tier 4 — Best-effort) |
| ❌ **Vibration** | `IVibration` | Stub | Not applicable to most Linux desktops |
| ❌ **Phone Dialer** | `IPhoneDialer` | Stub | Not applicable to desktop |
| ❌ **SMS** | `ISms` | Stub | Not applicable to desktop |
| ❌ **Contacts** | `IContacts` | Stub | Not implemented |
| ❌ **Flashlight** | `IFlashlight` | Stub | Not applicable to desktop |
| ❌ **Haptic Feedback** | `IHapticFeedback` | Stub | Not applicable to desktop |
| ❌ **Geocoding** | `IGeocoding` | Stub | Not implemented |
| ❌ **Accelerometer** | `IAccelerometer` | Stub | Not applicable to desktop |
| ❌ **Barometer** | `IBarometer` | Stub | Not applicable to desktop |
| ❌ **Compass** | `ICompass` | Stub | Not applicable to desktop |
| ❌ **Gyroscope** | `IGyroscope` | Stub | Not applicable to desktop |
| ❌ **Magnetometer** | `IMagnetometer` | Stub | Not applicable to desktop |
| ❌ **Orientation Sensor** | `IOrientationSensor` | Stub | Not applicable to desktop |

> Services are registered in 5 tiers via `AddLinuxGtk4Essentials()`: Tier 1 (Pure .NET, 9 services), Tier 2 (GTK4, 5 services), Tier 3 (DBus/system, 5 services), Tier 4 (Best-effort, 3 services), Tier 5 (Stubs, 12 services).

---

## 16. Styling Infrastructure

| Feature | Status | Notes |
|---------|--------|-------|
| ✅ **Border style mapping** | Done | Stroke → CSS border, StrokeShape → border-radius via BorderHandler. ⚠️ Custom dash patterns and line cap/join not supported via CSS borders |
| ✅ **View state mapping** | Done | IsVisible → `SetVisible()`, IsEnabled → `SetSensitive()`, Opacity → CSS |
| ✅ **Automation mapping** | Done | AutomationId → accessible name |
| ✅ **GTK CSS theming** | Done | `GtkThemeManager` for dark/light mode, `ApplyCustomCss()` for global CSS |
| ✅ **Per-widget CSS** | Done | `ApplyCss()` helper for inline style application via `CssProvider` |

---

## 17. WebView

| Feature | Status | Notes |
|---------|--------|-------|
| ✅ **URL loading** | Done | WebKit2-based navigation to URLs. ⚠️ Uses `WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS` env var for sandbox bypass |
| ✅ **HTML content** | Done | Display raw HTML via HtmlWebViewSource |
| ✅ **JavaScript execution** | Done | `EvaluateJavaScriptAsync` via WebKit with async result callbacks |
| ✅ **Navigation events** | Done | Navigating, Navigated with success/failure |
| ✅ **GoBack / GoForward** | Done | Navigation history with CanGoBack/CanGoForward state |
| ✅ **UserAgent** | Done | Custom user agent string |

---

## 17a. BlazorWebView (Blazor Hybrid)

| Feature | Status | Notes |
|---------|--------|-------|
| ✅ **BlazorWebViewHandler** | Done | MAUI handler bridging `IBlazorWebView` to native GTK `Gtk.Box` with embedded WebKit WebView |
| ✅ **Root Components** | Done | `RootComponentsCollection` manages Blazor root components mounted in the DOM via CSS selectors |
| ✅ **JS ↔ .NET Interop** | Done | Bidirectional message channel for JavaScript interop; `Blazor.start()` runtime initialization |
| ✅ **Static Asset Serving** | Done | Custom `app://` URI scheme handler serves Blazor framework files and app assets |
| ✅ **Host Page** | Done | Configurable `HostPage` property with extensionless URL fallback to `index.html` |
| ✅ **StartPath** | Done | Initial navigation path for Blazor router |
| ✅ **Thread Safety** | Done | `Channel<T>` message pump for WebKit thread safety |
| ✅ **Component Parameters** | Done | Blazor component parameters support |
| ✅ **Focus Management** | Done | Focus command mapper for keyboard focus |
| ❌ **CDP Debugging** | Not Done | No Chrome DevTools Protocol support for Blazor WebView inspection |

> Registered via `AddBlazorWebView()` + `AddLinuxGtk4BlazorWebView()` in `MauiProgram.cs`. Uses WebKitGTK with sandbox bypass (`WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS`). Minimum WebView size: 400×300px.

---

## 17b. App Theme Support

| Feature | Status | Notes |
|---------|--------|-------|
| ✅ **Light/Dark/System themes** | Done | `GtkThemeManager` applies dark/light mode, detects system GTK theme preference |
| ✅ **UserAppTheme** | Done | `ApplicationHandler.MapUserAppTheme` → `GtkThemeManager.SetTheme()` for programmatic theme switching |
| ✅ **RequestedThemeChanged** | Done | Event fires when app or system theme changes |
| ✅ **PlatformAppTheme** | Done | Detects current system GTK theme (light/dark) |
| ✅ **SetAppThemeColor** | Done | Cross-platform API for theme-aware color binding (works via MAUI layer) |
| ✅ **Per-widget CSS** | Done | `ApplyCss()` helper for inline style application via `CssProvider` |
| ✅ **Global CSS** | Done | `ApplyCustomCss()` for application-wide GTK CSS theming |

---

## 18. Label — FormattedText Detail

FormattedText requires Pango markup rendering for rich text:

| Feature | Status | Notes |
|---------|--------|-------|
| ✅ **Span rendering** | Done | Pango markup via `Gtk.Label.SetMarkup()` |
| ✅ **Span.Text** | Done | Escaped via `GLib.Functions.MarkupEscapeText` |
| ✅ **Span.TextColor** | Done | Pango `foreground` attribute |
| ✅ **Span.BackgroundColor** | Done | Pango `background` attribute |
| ✅ **Span.FontSize** | Done | Pango `size` attribute (in Pango units) |
| ✅ **Span.FontFamily** | Done | Pango `font_family` attribute |
| ✅ **Span.FontAttributes** | Done | Pango `weight="bold"` / `style="italic"` |
| ✅ **Span.TextDecorations** | Done | Pango `underline="single"` / `strikethrough="true"` |
| ✅ **Span.CharacterSpacing** | Done | Pango `letter_spacing` attribute |

> Note: GTK4 `Gtk.Label` supports Pango markup natively, making Span rendering straightforward to implement.

---

## 19. MenuBar (Desktop)

| Feature | Status | Notes |
|---------|--------|-------|
| ✅ **MenuBarItem** | Done | Top-level menu items via `Gtk.PopoverMenuBar` |
| ✅ **MenuFlyoutItem** | Done | Menu items with Text, Command via `Gio.SimpleAction` |
| ⚠️ **MenuFlyoutSeparator** | Partial | Basic separator support |
| ✅ **Dropdown behavior** | Done | Native GTK popover menu behavior |

> `MenuBar` rendered via `GtkMenuBarManager` — integrates with `Gtk.Window` action groups. NavigationPage propagates `Page.MenuBarItems` to the window on navigation.

### Transitions/Animation
- [x] GTK CSS transitions — CSS `transition` on background-color, opacity, box-shadow, border-radius, color (200ms ease)

---

## 20. Lifecycle Events

| Feature | Status | Notes |
|---------|--------|-------|
| ✅ **OnWindowCreated** | Done | `ConfigureLifecycleEvents().AddGtk()` hook |
| ✅ **OnMauiApplicationCreated** | Done | Access to `Gtk.Application` instance |
| ✅ **OnApplicationActivated** | Done | GTK application activated event |
| ✅ **OnApplicationShutdown** | Done | GTK application shutdown event |
| ✅ **App icon** | Done | Hicolor icon theme integration via MauiIcon build targets (SVG support) |
| ✅ **Desktop entry** | Done | `.desktop` file generation via `GtkDesktopIntegration` |

---

## 21. Animations

| Feature | Status | Notes |
|---------|--------|-------|
| ✅ **PlatformTicker** | Done | `GtkPlatformTicker` implements `ITicker` using `GLib.Functions.TimeoutAdd` at ~60fps. Registered as singleton in DI. Must call `RemoveAll<ITicker>()` before `AddSingleton` to override MAUI's default |
| ✅ **TranslateTo** | Done | Works via PlatformTicker + MapTransformProperty re-applying Gsk.Transform |
| ✅ **FadeTo** | Done | Works via PlatformTicker + MapOpacity |
| ✅ **ScaleTo** | Done | Works via PlatformTicker + MapTransformProperty |
| ✅ **RotateTo** | Done | Works via PlatformTicker + MapTransformProperty |
| ✅ **LayoutTo** | Done | Works via PlatformTicker (cross-platform MAUI logic) |
| ✅ **Easing functions** | Done | Cross-platform MAUI Easing (CubicInOut, SpringOut, etc.) — no platform code needed |
| ✅ **Animation class** | Done | Cross-platform MAUI Animation class — no platform code needed |

> All MAUI animations flow through `IAnimationManager` → `ITicker`. The `GtkPlatformTicker` drives the animation loop, and transform property mappers (`MapTransformProperty`) re-apply `Gsk.Transform` on each frame.

---

## 22. ControlTemplate & ContentPresenter

| Feature | Status | Notes |
|---------|--------|-------|
| ✅ **ControlTemplate** | Done | Works for templates that resolve to supported views. `IContentView` interface handler mapping ensures ContentPresenter is rendered |
| ✅ **ContentPresenter** | Done | Handled via `ContentViewHandler` through `IContentView` interface mapping. Injects content from ControlTemplate |
| ✅ **RadioButton** | Done | Uses `Gtk.CheckButton` with string content. ControlTemplate-based visual content falls back to `ToString()` |
| ✅ **TemplatedView** | Done | Works via `IContentView` handler mapping — custom TemplatedView subclasses with ControlTemplate render correctly |

> Note: ControlTemplate/ContentPresenter may work via MAUI's cross-platform layer if the handler properly renders the visual tree. Needs testing.

---

## Summary Statistics

| Category | Implemented | Total | Coverage |
|----------|-------------|-------|----------|
| **Core Infrastructure** | 15 | 15 | 100% |
| **Pages** | 5 | 5 | 100% |
| **Application & Window** | 3 | 3 | 100% |
| **Layouts** | 9 | 9 | 100% |
| **Basic Controls** | 14 | 14 | 100% |
| **Input Controls** | 4 | 4 | 100% |
| **Collection Controls** | 7 | 7 | 100% |
| **Navigation** | 6 | 6 | 100% |
| **Alerts & Dialogs** | 4 | 4 | 100% |
| **Gesture Recognizers** | 5 | 5 | 100% |
| **Graphics** | 4 | 4 | 100% |
| **Shapes** | 7 | 7 | 100% |
| **Font Services** | 7 | 7 | 100% |
| **WebView** | 6 | 6 | 100% |
| **BlazorWebView** | 9 | 10 | 90% |
| **App Theme** | 7 | 7 | 100% |
| **MenuBar** | 4 | 4 | 100% |
| **Essentials** | 22 (+ 2 partial) | 36 | 67% |
| **Base View Properties** | 21 | 21 | 100% |
| **FormattedText/Spans** | 9 | 9 | 100% |
| **VisualStateManager** | 6 | 6 | 100% |
| **Animations** | 8 | 8 | 100% |
| **Lifecycle Events** | 6 | 6 | 100% |
| **ControlTemplate** | 4 | 4 | 100% |

### Key Gaps (Priority Order)
1. **CollectionView** — Missing `RemainingItemsThreshold` for incremental loading, `Scrolled` event, `SnapPoints`, `CanReorderItems`
2. **BlazorWebView** — No CDP (Chrome DevTools Protocol) debugging support
3. **Geolocation** — Basic GeoClue integration only, no continuous tracking
4. **Geocoding** — Not implemented (stub)
5. **Web Authenticator** — Basic OAuth flow via system browser only
6. **Entry/Editor/SearchBar** — Spell check and text prediction are no-ops (GTK limitation)
7. **Slider** — ThumbImageSource property not yet rendered
8. **Button** — ImageSource mapping wired but incomplete
9. **Border** — CSS borders don't support custom dash patterns or line cap/join

### Third-Party Library Compatibility
- ✅ **MauiIcons.FontAwesome** — Font icon rendering via Label/ContentView with font glyphs (not Image). Font resolution through `LabelHandler.MapFont` → CSS
- ✅ **Microsoft.AspNetCore.Components.WebView.Maui** — Blazor Hybrid fully operational via WebKitGTK
