# .NET MAUI Backend Implementation Checklist

A comprehensive checklist of everything required to implement a complete .NET MAUI backend for a new target platform. Derived from the Platform.Maui.Web (Blazor) implementation.

---

## 1. Core Infrastructure

### Platform Abstractions
- [ ] **Platform View Type** — Define the native view abstraction (e.g., `WebPlatformView`, `GtkWidget`, `WinUI UIElement`)
- [ ] **Platform Context** — Container for platform services, handler factory, DI scope (e.g., `WebMauiContext`)
- [ ] **Dispatcher** — `IDispatcher` implementation for marshalling to the UI thread
- [ ] **Event System** — Platform event model for user interactions (clicks, input, gestures)
- [ ] **Handler Factory Integration** — Register all handlers via `ConfigureMauiHandlers`
- [ ] **App Host Builder Extension** — `UseMauiWebHandlers()` or equivalent to wire up all services

### Rendering Pipeline
- [ ] **View Renderer** — Component that traverses the virtual view tree and renders platform views
- [ ] **Property Change Propagation** — Re-map views when `IView` property changes fire
- [ ] **Child Synchronization** — Add/remove/reorder child views when layout children change
- [ ] **Style/Attribute Application** — Apply visual properties (colors, sizes, fonts) to platform views

### JavaScript / Native Interop (if applicable)
- [ ] **Event Delegation** — Document-level event listeners that survive DOM re-renders
- [ ] **Gesture Interop** — Pointer/touch event capture and forwarding
- [ ] **Keyboard Event Interop** — Key press forwarding for Entry/Editor
- [ ] **Wheel/Scroll Interop** — Scroll and pinch gesture translation

---

## 2. Application & Window

| Control | Notes |
|---------|-------|
| [ ] **Application** | App lifecycle, main page assignment |
| [ ] **Window** | Window management, title, resize |

---

## 3. Pages

| Page | Features to Implement |
|------|----------------------|
| [ ] **ContentPage** | Content rendering, Title, MenuBarItems (dropdown menus with separators, command execution) |
| [ ] **NavigationPage** | Push/pop stack navigation, back button, title bar, `Pushed`/`Popped` events |
| [ ] **TabbedPage** | Tab bar rendering (top/bottom placement), tab selection, content switching |
| [ ] **FlyoutPage** | Flyout panel with toggle, detail area, resizable divider, back button sync |
| [ ] **Shell** | Flyout items, tab bars, ShellItem/ShellSection/ShellContent hierarchy, URL routing |

---

## 4. Layouts

| Layout | Features to Implement |
|--------|----------------------|
| [ ] **VerticalStackLayout** | Flex column with spacing |
| [ ] **HorizontalStackLayout** | Flex row with spacing |
| [ ] **Grid** | Row/column definitions (Absolute, Star, Auto), RowSpan/ColumnSpan, row/column spacing |
| [ ] **FlexLayout** | CSS flexbox mapping (Direction, Wrap, JustifyContent, AlignItems, AlignContent) |
| [ ] **AbsoluteLayout** | Absolute and proportional positioning via layout bounds/flags. **Note:** Container must default to `width/height: 100%` when no explicit dimensions are set — CSS absolutely-positioned children are out of flow so the parent collapses to 0×0 otherwise. Strip `align-self`/`justify-self` from absolute children. |
| [ ] **ScrollView** | Scrollable container with overflow, orientation (Vertical, Horizontal, Both, Neither). **Scroll APIs:** `ScrollToAsync(x, y, animated)` — subscribe to `ScrollToRequested` event and call JS `scrollTo`/`scrollBy` on the container div; `ScrollToAsync(element, position, animated)` — find child DOM element and use JS `scrollIntoView` or calculate offset for `ScrollToPosition` (MakeVisible, Start, Center, End); `SetScrolledPosition(x, y)` — update `ScrollX`/`ScrollY` from JS scroll events; raise `Scrolled` event. **IScrollView:** `RequestScrollTo(hOffset, vOffset, instant)` — platform must scroll the container; `ScrollFinished()` — call back after scroll completes; `HorizontalOffset`/`VerticalOffset` — read/write current scroll position. **ScrollBar visibility:** `HorizontalScrollBarVisibility`/`VerticalScrollBarVisibility` → CSS `overflow-x`/`overflow-y` (`auto`, `scroll`, `hidden`). |
| [ ] **ContentView** | Simple content wrapper |
| [ ] **Border** | Stroke, StrokeThickness, StrokeShape (RoundRectangle), padding, content |
| [ ] **Frame** | Legacy border container with CornerRadius, HasShadow, padding |
| [ ] **Layout (fallback)** | The base `Layout` handler must detect `StackLayout` subclasses (custom layouts like `HorizontalWrapLayout`) and apply flex direction, gap, and `flex-wrap: wrap`. Custom layouts may not set `Orientation` explicitly — infer from type name or layout manager. |

---

## 5. Basic Controls

| Control | Features to Implement |
|---------|----------------------|
| [ ] **Label** | Text, FormattedText/Spans, TextColor, TextAlignment (H & V), LineBreakMode, MaxLines, LineHeight, CharacterSpacing, TextDecorations (Underline, Strikethrough), FontAttributes (Bold, Italic) |
| [ ] **Button** | Text, Command/CommandParameter, Clicked event, BackgroundColor, TextColor, BorderColor, BorderWidth, CornerRadius, CharacterSpacing, font properties |
| [ ] **ImageButton** | Source, Command, Clicked, BackgroundColor, BorderColor, CornerRadius, padding |
| [ ] **Entry** | Text, Placeholder, PlaceholderColor, IsPassword, Keyboard, ReturnType, ReturnCommand, TextChanged, CharacterSpacing, ClearButtonVisibility, MaxLength |
| [ ] **Editor** | Text, Placeholder, PlaceholderColor, TextChanged, AutoSize, CharacterSpacing, MaxLength, multi-line input |
| [ ] **Switch** | IsToggled, OnColor, ThumbColor, Toggled event |
| [ ] **CheckBox** | IsChecked, Color, CheckedChanged event |
| [ ] **RadioButton** | IsChecked, GroupName, Content, CheckedChanged, mutual exclusion within group |
| [ ] **Slider** | Value, Minimum, Maximum, MinimumTrackColor, MaximumTrackColor, ThumbColor, ValueChanged |
| [ ] **Stepper** | Value, Minimum, Maximum, Increment, ValueChanged |
| [ ] **ProgressBar** | Progress (0.0-1.0), ProgressColor |
| [ ] **ActivityIndicator** | IsRunning, Color, animated spinner |
| [ ] **BoxView** | Color, CornerRadius, WidthRequest, HeightRequest |
| [ ] **Image** | Source (URL, file, stream), Aspect (AspectFit, AspectFill, Fill). **Note:** `StreamImageSource` (from `ImageSource.FromResource`) must be converted to data URIs with MIME detection. MAUI's build converts SVGs→PNGs for native platforms; web must provide SVG fallback when `.png` is requested (e.g., via a `MapGet` endpoint that checks for `.svg` when `.png` doesn't exist). |

---

## 6. Input & Selection Controls

| Control | Features to Implement |
|---------|----------------------|
| [ ] **Picker** | ItemsSource, SelectedIndex, SelectedItem, SelectedIndexChanged |
| [ ] **DatePicker** | Date, MinimumDate, MaximumDate, Format, DateSelected |
| [ ] **TimePicker** | Time, Format |
| [ ] **SearchBar** | Text, Placeholder, SearchCommand, SearchButtonPressed, TextChanged |

---

## 7. Collection Controls

| Control | Features to Implement |
|---------|----------------------|
| [ ] **CollectionView** | ItemsSource, ItemTemplate, DataTemplateSelector, IsGrouped, GroupHeaderTemplate, GroupFooterTemplate, virtualization, INotifyCollectionChanged. **Scroll APIs:** `ScrollTo(index, groupIndex, position, animate)` and `ScrollTo(item, group, position, animate)` — subscribe to `ScrollToRequested` event; find the item's DOM element by `data-cv-index` attribute and use `scrollIntoView`/offset calculation for `ScrollToPosition` (MakeVisible, Start, Center, End); `Scrolled` event — fire from JS scroll listener with `ItemIndex`, `FirstVisibleItemIndex`, `LastVisibleItemIndex`, `HorizontalOffset`, `VerticalOffset`, `HorizontalDelta`, `VerticalDelta`; `ItemsUpdatingScrollMode` (KeepItemsInView, KeepScrollOffset, KeepLastItemInView); `RemainingItemsThreshold`/`RemainingItemsThresholdReached` for incremental loading; `SnapPointsType`/`SnapPointsAlignment` via CSS scroll-snap. |
| [ ] **ListView** | ItemsSource, ItemTemplate, cell types (ViewCell, TextCell, ImageCell, SwitchCell), grouping, headers/footers, separators, row selection, row height |
| [ ] **CarouselView** | ItemsSource, ItemTemplate, CurrentItem, Position, swipe/snap navigation, Loop, PeekAreaInsets |
| [ ] **IndicatorView** | ItemsSource, Position, IndicatorColor, SelectedIndicatorColor, IndicatorSize, dot rendering |
| [ ] **TableView** | Root/TableSections, cell types (TextCell, SwitchCell, EntryCell, ImageCell, ViewCell), section headers |
| [ ] **SwipeView** | Content, LeftItems, RightItems (TopItems, BottomItems), SwipeItem with Command, swipe-to-reveal interaction |
| [ ] **RefreshView** | Content, IsRefreshing, RefreshColor, Command, pull-to-refresh trigger |

---

## 8. Navigation & Routing

| Feature | Notes |
|---------|-------|
| [ ] **NavigationPage stack** | PushAsync, PopAsync, PopToRootAsync |
| [ ] **Shell navigation** | Flyout, tabs, content hierarchy, GoToAsync |
| [ ] **URL/route-based routing** | Deep linking (fragment-based `#/route` or path-based) |
| [ ] **Route ↔ Shell sync** | Browser URL updates when Shell navigates, URL changes navigate Shell |
| [ ] **Non-Shell routing** | Callback interface for apps that don't use Shell (e.g., `IWebNavigationHandler`) |
| [ ] **Back button** | Platform back button handling, navigation stack awareness |
| [ ] **ToolbarItems** | Primary/secondary toolbar items on NavigationPage |

---

## 9. Alerts & Dialogs

| Dialog | Features to Implement |
|--------|----------------------|
| [ ] **DisplayAlert** | Title, message, accept/cancel buttons, async result |
| [ ] **DisplayActionSheet** | Title, cancel, destruction button, action buttons, returns selected text |
| [ ] **DisplayPromptAsync** | Title, message, text input, placeholder, initial value, accept/cancel, returns text |
| [ ] **Modal overlay** | Backdrop, centered dialog, z-index management, dismiss on action |

---

## 10. Gesture Recognizers

| Gesture | Features to Implement |
|---------|----------------------|
| [ ] **TapGestureRecognizer** | Single tap, NumberOfTapsRequired (double-tap), Command, Tapped event |
| [ ] **PanGestureRecognizer** | PanUpdated with StatusType (Started, Running, Completed), TotalX/TotalY tracking |
| [ ] **SwipeGestureRecognizer** | Directional swipe detection (Left, Right, Up, Down), configurable Threshold, Swiped event |
| [ ] **PinchGestureRecognizer** | PinchUpdated with Scale factor, StatusType lifecycle, IPinchGestureController (SendPinchStarted/SendPinch/SendPinchEnded) |
| [ ] **PointerGestureRecognizer** | PointerEntered, PointerExited, PointerMoved, Command execution |

---

## 11. Graphics & Shapes

### Microsoft.Maui.Graphics
| Feature | Notes |
|---------|-------|
| [ ] **GraphicsView** | IDrawable rendering, canvas adapter to platform graphics (SVG, Canvas2D, Skia, etc.) |
| [ ] **Canvas Operations** | DrawLine, DrawRect, DrawEllipse, DrawPath, DrawString, FillRect, FillEllipse, FillPath |
| [ ] **Canvas State** | SaveState, RestoreState, Translate, Rotate, Scale |
| [ ] **Brushes** | SolidColorBrush, LinearGradientBrush (→ CSS `linear-gradient` with angle conversion from Start/EndPoint), RadialGradientBrush (→ CSS `radial-gradient` with center/radius). MAUI uses relative coords (0-1); convert to CSS percentages/degrees. |

### Shapes
| Shape | Notes |
|-------|-------|
| [ ] **Rectangle** | Width, Height, RadiusX/Y |
| [ ] **Ellipse** | Width, Height |
| [ ] **Line** | X1, Y1, X2, Y2 |
| [ ] **Path** | PathGeometry with segments (Line, Bezier, QuadraticBezier, Arc, PolyLine, PolyBezier) |
| [ ] **Polygon** | Points collection, closed shape |
| [ ] **Polyline** | Points collection, open shape |
| [ ] **Fill & Stroke** | Fill brush, Stroke color, StrokeThickness |

---

## 12. Common View Properties (Base Handler)

Every handler must support these properties mapped from the base `IView`:

### Visibility & State
- [ ] Opacity
- [ ] IsVisible (display: none)
- [ ] IsEnabled (pointer-events, aria-disabled)
- [ ] InputTransparent (pointer-events: none)

### Sizing
- [ ] WidthRequest / HeightRequest
- [ ] MinimumWidthRequest / MinimumHeightRequest
- [ ] MaximumWidthRequest / MaximumHeightRequest

### Layout
- [ ] HorizontalOptions (Start, Center, End, Fill)
- [ ] VerticalOptions (Start, Center, End, Fill)
- [ ] Margin
- [ ] Padding (for views implementing IPadding)
- [ ] FlowDirection (LTR, RTL)
- [ ] ZIndex

### Appearance
- [ ] BackgroundColor
- [ ] Background (SolidColorBrush, LinearGradientBrush, RadialGradientBrush)

### Interactivity Attachments
- [ ] **ToolTip** — `ToolTipProperties.Text` mapped to HTML `title` attribute (or custom tooltip overlay)
- [ ] **ContextFlyout** — `FlyoutBase.GetContextFlyout()` → popup menu overlay on right-click with `MenuFlyoutItem`, `MenuFlyoutSubItem`, `MenuFlyoutSeparator` support

### Transforms
- [ ] TranslationX / TranslationY
- [ ] Rotation / RotationX / RotationY
- [ ] Scale / ScaleX / ScaleY
- [ ] AnchorX / AnchorY (transform-origin)

### Effects
- [ ] Shadow (Color, Offset, Radius, Opacity)
- [ ] Clip (RoundRectangleGeometry, EllipseGeometry, RectangleGeometry)

### Automation
- [ ] AutomationId
- [ ] Semantic properties (for accessibility)

### Transitions/Animation
- [ ] CSS Transitions or equivalent for smooth property changes (opacity, transform, background-color)

---

## 13. VisualStateManager & Triggers

| Feature | Notes |
|---------|-------|
| [ ] **VisualStateManager** | GoToState for "Normal", "Focused", "PointerOver", "Disabled", custom states |
| [ ] **PropertyTrigger** | React to property value changes (e.g., IsFocused → change BackgroundColor) |
| [ ] **DataTrigger** | React to binding value changes with conditions |
| [ ] **MultiTrigger** | Multiple conditions combined. **Note:** `BindingCondition` inside MultiTrigger may not re-evaluate on web because trigger-applied property changes don't always fire PropertyChanged through the handler subscription chain. Needs explicit re-render when trigger modifies properties. |
| [ ] **EventTrigger** | React to events (begin animations, etc.) |
| [ ] **Behaviors** | Attach custom behaviors to views (validation, character counting, etc.) |

---

## 14. Font Management

| Feature | Notes |
|---------|-------|
| [ ] **IFontManager** | Resolve font family names, apply font properties to views |
| [ ] **IFontRegistrar** | Register embedded fonts with aliases |
| [ ] **IEmbeddedFontLoader** | Extract fonts from assembly resources, cache locally |
| [ ] **@font-face / Font loading** | Platform-specific font loading (CSS @font-face, native font registration, etc.) |
| [ ] **IFontNamedSizeService** | `DependencyService` registration mapping `NamedSize` enum (Default, Micro, Small, Medium, Large, Body, Header, Title, Subtitle, Caption) to platform pixel values. Required for XAML `FontSize="Title"` etc. — without it, `FontSizeConverter` throws `XamlParseException` during `DataTemplate.CreateContent()`. Register via `DependencyService.Register<IFontNamedSizeService, ...>()` (legacy pattern, not DI). |
| [ ] **Font properties** | FontSize, FontFamily, FontAttributes (Bold, Italic), apply to Label, Button, Entry, Editor, etc. |

---

## 15. Essentials / Platform Services

| Service | Interface | Notes |
|---------|-----------|-------|
| [ ] **App Info** | `IAppInfo` | App name, version, build, package name, theme |
| [ ] **Battery** | `IBattery` | Charge level, state, power source |
| [ ] **Browser** | `IBrowser` | Open URLs in system browser |
| [ ] **Clipboard** | `IClipboard` | Copy/paste text |
| [ ] **Connectivity** | `IConnectivity` | Network access type, profiles, change events |
| [ ] **Device Display** | `IDeviceDisplay` | Screen dimensions, density, orientation, keep screen on |
| [ ] **Device Info** | `IDeviceInfo` | Platform, model, manufacturer, OS version, device type |
| [ ] **File Picker** | `IFilePicker` | Pick files from storage |
| [ ] **File System** | `IFileSystem` | App data directory, cache directory, open bundle files |
| [ ] **Geolocation** | `IGeolocation` | Get current location, listen for changes |
| [ ] **Launcher** | `ILauncher` | Launch URIs, check URI support |
| [ ] **Map** | `IMap` | Open platform maps app |
| [ ] **Media Picker** | `IMediaPicker` | Capture/pick photos and videos |
| [ ] **Preferences** | `IPreferences` | Key-value persistent storage |
| [ ] **Secure Storage** | `ISecureStorage` | Encrypted key-value storage |
| [ ] **Semantic Screen Reader** | `ISemanticScreenReader` | Announce text for accessibility |
| [ ] **Share** | `IShare` | Share text/URIs via system share sheet |
| [ ] **Text-to-Speech** | `ITextToSpeech` | Speak text, get available locales |
| [ ] **Version Tracking** | `IVersionTracking` | Track app version history across launches |
| [ ] **Vibration** | `IVibration` | Haptic feedback |

---

## 16. Styling Infrastructure

| Feature | Notes |
|---------|-------|
| [ ] **Border style mapping** | Stroke → border CSS, StrokeShape → border-radius, StrokeThickness → border-width |
| [ ] **View state mapping** | IsVisible → display, IsEnabled → pointer-events/opacity, Opacity → opacity |
| [ ] **Automation mapping** | AutomationId → accessible name/id attributes |
| [ ] **CSS Variables** | Custom property system for theming (optional) |
| [ ] **Inline styles** | Direct property-to-style mapping |

---

## 17. WebView

| Feature | Notes |
|---------|-------|
| [ ] **URL loading** | Navigate to URLs via UrlWebViewSource |
| [ ] **HTML content** | Display raw HTML via HtmlWebViewSource |
| [ ] **JavaScript execution** | EvaluateJavaScriptAsync |
| [ ] **Navigation events** | Navigating, Navigated |

---

## 18. Label — FormattedText Detail

FormattedText requires special handling as it's a compound property:

| Feature | Notes |
|---------|-------|
| [ ] **Span rendering** | Each Span as an inline element with individual styling |
| [ ] **Span.Text** | Text content |
| [ ] **Span.TextColor** | Per-span text color |
| [ ] **Span.BackgroundColor** | Per-span background |
| [ ] **Span.FontSize** | Per-span font size |
| [ ] **Span.FontFamily** | Per-span font family |
| [ ] **Span.FontAttributes** | Bold, Italic per span |
| [ ] **Span.TextDecorations** | Underline, Strikethrough per span |
| [ ] **Span.CharacterSpacing** | Letter spacing per span |

---

## 19. MenuBar (Desktop)

| Feature | Notes |
|---------|-------|
| [ ] **MenuBarItem** | Top-level menu buttons (File, Edit, View) |
| [ ] **MenuFlyoutItem** | Menu items with Text, Command, CommandParameter, enabled/disabled state |
| [ ] **MenuFlyoutSeparator** | Visual separator between menu items |
| [ ] **Dropdown behavior** | Toggle visibility on click, dismiss on action |

> **Note:** `MenuBar` does NOT implement `IView` — it must be rendered by the `ContentPage` handler via `Page.MenuBarItems`.

---

## 20. Animations

| Feature | Notes |
|---------|-------|
| [ ] **TranslateTo** | Animate TranslationX/TranslationY via CSS transitions or JS requestAnimationFrame |
| [ ] **FadeTo** | Animate Opacity |
| [ ] **ScaleTo** | Animate Scale/ScaleX/ScaleY |
| [ ] **RotateTo** | Animate Rotation |
| [ ] **LayoutTo** | Animate layout bounds (complex — may require CSS transitions on size/position) |
| [ ] **Easing functions** | Map MAUI Easing types (CubicIn, CubicOut, BounceIn, SpringIn, etc.) to CSS `cubic-bezier()` equivalents or JS-based easing |
| [ ] **Animation class** | `new Animation(...)` with child animations, `Commit()` to run, `AbortAnimation()` to cancel |
| [ ] **AnimationExtensions** | Extension methods on `VisualElement` (TranslateTo, FadeTo, etc.) that currently no-op on web |

---

## 21. ControlTemplate & ContentPresenter

| Feature | Notes |
|---------|-------|
| [ ] **ControlTemplate** | Custom control templates defined in XAML — need to render the visual tree from the template |
| [ ] **ContentPresenter** | Placeholder in ControlTemplate that inserts the actual content |
| [ ] **RadioButton ControlTemplate** | RadioButton uses ControlTemplate by default for its visual — without this, RadioButton `Content` renders as `Grid.ToString()` instead of the visual content |
| [ ] **TemplatedView** | Base class for controls with ControlTemplate support |

---

## Summary Statistics

| Category | Count |
|----------|-------|
| **Handlers** | 47 (including base, layout base, fallback) |
| **Pages** | 5 (ContentPage, NavigationPage, TabbedPage, FlyoutPage, Shell) |
| **Layouts** | 8 (VStack, HStack, Grid, Flex, Absolute, ScrollView, ContentView, Border/Frame) |
| **Basic Controls** | 14 (Label, Button, ImageButton, Entry, Editor, Switch, CheckBox, RadioButton, Slider, Stepper, ProgressBar, ActivityIndicator, BoxView, Image) |
| **Collection Controls** | 7 (CollectionView, ListView, CarouselView, IndicatorView, TableView, SwipeView, RefreshView) |
| **Input Controls** | 4 (Picker, DatePicker, TimePicker, SearchBar) |
| **Shapes** | 6 (Rectangle, Ellipse, Line, Path, Polygon, Polyline) |
| **Essentials** | 21 services |
| **Gesture Recognizers** | 5 (Tap, Pan, Swipe, Pinch, Pointer) |
| **Dialog Types** | 3 (Alert, ActionSheet, Prompt) |
| **Font Services** | 4 (FontManager, FontRegistrar, EmbeddedFontLoader, FontNamedSizeService) |
