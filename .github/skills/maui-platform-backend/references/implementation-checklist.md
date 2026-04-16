# Implementation Checklist

A comprehensive, platform-agnostic checklist for implementing a complete .NET MAUI backend. Track progress with ✅ Done | ⚠️ Partial | ❌ Not implemented | N/A Not applicable.

## 1. Core Infrastructure

### Platform Abstractions
- [ ] **Platform View Type** — native view base class (e.g., `Gtk.Widget`, `NSView`)
- [ ] **Platform Context** — `[Prefix]MauiContext : IMauiContext` with scoped DI, handler factory, window/app scope
- [ ] **Dispatcher** — `[Prefix]Dispatcher : IDispatcher` + `[Prefix]DispatcherProvider` + `[Prefix]DispatcherTimer`
- [ ] **Event System** — native events wired to MAUI gesture recognizers and input handlers
- [ ] **Handler Factory Integration** — all handlers registered via `ConfigureMauiHandlers`
- [ ] **App Host Builder Extension** — `UseMauiApp[Platform]<TApp>()` wires handlers, dispatcher, font manager, alert system, ticker

> **MAUI Source**: [IMauiContext](https://github.com/dotnet/maui/blob/main/src/Core/src/IMauiContext.cs), [IDispatcher](https://github.com/dotnet/maui/blob/main/src/Core/src/Dispatching/IDispatcher.cs), [MauiAppBuilder](https://github.com/dotnet/maui/blob/main/src/Core/src/Hosting/MauiAppBuilder.cs)

### Rendering Pipeline
- [ ] **Base View Handler** — `[Prefix]ViewHandler<TVirtualView, TPlatformView>` bridges MAUI virtual views to native views
- [ ] **Property Change Propagation** — `IPropertyMapper` re-maps views on property changes
- [ ] **Child Synchronization** — container view handles add/remove/reorder of child subviews
- [ ] **Style/Attribute Application** — opacity, visibility, background, transforms applied to native views
- [ ] **Layout Negotiation** — platform applies MAUI-computed frames (top-left origin coordinate system)

> **Key Concept**: MAUI's layout engine computes all frames via `CrossPlatformMeasure`/`CrossPlatformArrange`. Your handler calls `GetDesiredSize` (delegates to MAUI measurement) and `PlatformArrange` (applies computed `Rect` to native view frame/bounds).
>
> **MAUI Source**: [ViewHandler<T,T>](https://github.com/dotnet/maui/blob/main/src/Core/src/Handlers/View/ViewHandlerOfT.cs)

### Native Interop
- [ ] **Native Event Handling** — mouse/touch, keyboard, focus events
- [ ] **Gesture Controller Integration** — platform gesture recognizers → MAUI gesture types
- [ ] **Accessibility** — platform accessibility APIs → MAUI semantic properties

---

## 2. Application & Window

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] Application | | Lifecycle management — maps `IApplication` to native app |
| [ ] Window | | Title, Content, Width/Height, X/Y, Min/Max sizes |
| [ ] Multi-window | | Multiple window tracking, lifecycle events |
| [ ] App Theme / Dark Mode | | Light/Dark/System theme detection and switching |
| [ ] Window Close / Lifecycle | | Destroying event, cleanup |

---

## 3. Pages

| Page | Status | Notes |
|------|--------|-------|
| [ ] ContentPage | | Content, Background, Title |
| [ ] NavigationPage | | Push/Pop stack, back button, title propagation |
| [ ] TabbedPage | | Tab bar, BarBackgroundColor, SelectedTabColor |
| [ ] FlyoutPage | | Side panel + detail, IsPresented, FlyoutBehavior |
| [ ] Shell | | Flyout, tabs, content hierarchy, route navigation |

---

## 4. Layouts

All layouts share a single `LayoutHandler`. MAUI's cross-platform layout managers compute positioning.

| Layout | Status | Notes |
|--------|--------|-------|
| [ ] VerticalStackLayout | | Via LayoutHandler |
| [ ] HorizontalStackLayout | | Via LayoutHandler |
| [ ] Grid | | Row/column definitions, spans, spacing |
| [ ] FlexLayout | | Direction, Wrap, JustifyContent, AlignItems |
| [ ] AbsoluteLayout | | Absolute and proportional positioning |
| [ ] ScrollView | | Content, Orientation, ScrollBarVisibility, ScrollToAsync |
| [ ] ContentView | | Simple content wrapper |
| [ ] Border | | Stroke, StrokeThickness, StrokeShape, dash patterns |
| [ ] Frame | | Legacy border — typically maps to BorderHandler |
| [ ] Layout (fallback) | | Base LayoutHandler for custom subclasses |

---

## 5. Basic Controls

| Control | Status | Notes |
|---------|--------|-------|
| [ ] Label | | Text, TextColor, Font, Alignment, LineBreakMode, MaxLines, TextDecorations, Padding, FormattedText/Spans |
| [ ] Button | | Text, TextColor, Font, Background, CornerRadius, Padding, ImageSource, Clicked |
| [ ] ImageButton | | Source, Clicked, Background, CornerRadius |
| [ ] Entry | | Text, Placeholder, IsPassword, IsReadOnly, MaxLength, ReturnType, CursorPosition |
| [ ] Editor | | Text, IsReadOnly, MaxLength, Placeholder, AutoSize |
| [ ] Switch | | IsOn, OnColor, ThumbColor, TrackColor |
| [ ] CheckBox | | IsChecked, Color |
| [ ] RadioButton | | IsChecked, TextColor, Content, GroupName |
| [ ] Slider | | Value, Min, Max, MinTrackColor, MaxTrackColor, ThumbColor |
| [ ] Stepper | | Value, Min, Max, Increment |
| [ ] ProgressBar | | Progress, ProgressColor |
| [ ] ActivityIndicator | | IsRunning, Color |
| [ ] BoxView | | Via ShapeViewHandler |
| [ ] Image | | Source (file/URI/stream/font), Aspect, IsOpaque |

---

## 6. Input & Selection Controls

| Control | Status | Notes |
|---------|--------|-------|
| [ ] Picker | | Title, SelectedIndex, Items, TextColor |
| [ ] DatePicker | | Date, MinimumDate, MaximumDate, TextColor, Format |
| [ ] TimePicker | | Time, TextColor, Format |
| [ ] SearchBar | | Text, Placeholder, IsReadOnly, MaxLength |

---

## 7. Collection Controls

| Control | Status | Notes |
|---------|--------|-------|
| [ ] CollectionView | | ItemsSource, ItemTemplate, SelectionMode, GroupHeaders, EmptyView, Header/Footer |
| [ ] ListView | | ItemsSource, ItemTemplate, ViewCell, Selection, Grouping |
| [ ] CarouselView | | Horizontal/vertical paging, Position, CurrentItem, Loop |
| [ ] IndicatorView | | Page indicator dots |
| [ ] TableView | | TableRoot/TableSection, TextCell, SwitchCell, EntryCell |
| [ ] SwipeView | | Swipe-to-reveal actions |
| [ ] RefreshView | | IsRefreshing, Command |

> **Key Challenge**: Collection controls require virtualization. Does the platform have a native recycling list/table view?

---

## 8. Navigation & Routing

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] NavigationPage stack | | PushAsync, PopAsync, PopToRootAsync |
| [ ] Shell navigation | | Flyout, tabs, route-based navigation |
| [ ] Modal navigation | | Push/pop modal pages with backdrop |
| [ ] Back button | | Platform-appropriate back navigation |
| [ ] ToolbarItems | | Primary/secondary items in toolbar |

---

## 9. Alerts & Dialogs

| Dialog | Status | Notes |
|--------|--------|-------|
| [ ] DisplayAlert | | Title, message, accept/cancel |
| [ ] DisplayActionSheet | | Multi-button with destructive/cancel |
| [ ] DisplayPromptAsync | | Text input with placeholder, validation |

> ⚠️ **Requires DispatchProxy workaround** — see [extensibility-gaps.md](extensibility-gaps.md)

---

## 10. Gesture Recognizers

| Gesture | Status | Notes |
|---------|--------|-------|
| [ ] TapGestureRecognizer | | NumberOfTapsRequired, Command |
| [ ] PanGestureRecognizer | | TotalX, TotalY translation tracking |
| [ ] SwipeGestureRecognizer | | Direction detection (Left/Right/Up/Down) |
| [ ] PinchGestureRecognizer | | Scale tracking (trackpad/touch) |
| [ ] PointerGestureRecognizer | | Enter/exit/move tracking |
| [ ] DragGestureRecognizer | | Drag source for drag-and-drop |
| [ ] DropGestureRecognizer | | Drop target for drag-and-drop |
| [ ] LongPressGestureRecognizer | | Long press/hold detection |

---

## 11. Graphics & Shapes

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] GraphicsView | | Platform drawing surface with `IDrawable` |
| [ ] Canvas Operations | | Draw/Fill lines, rects, ellipses, paths, strings |
| [ ] Canvas State | | SaveState/RestoreState, transforms |
| [ ] Brushes | | Solid, LinearGradient, RadialGradient |
| [ ] ShapeViewHandler | | Renders all shapes via `IShape.PathForBounds()` |
| [ ] Fill & Stroke | | Fill brush and stroke properties |

---

## 12. Base View Properties (in your base handler's PropertyMapper)

### Visibility & State
- [ ] Opacity, IsVisible, IsEnabled, InputTransparent

### Sizing
- [ ] WidthRequest/HeightRequest, MinWidth/MinHeight, MaxWidth/MaxHeight

### Appearance
- [ ] BackgroundColor/Background (solid + gradient)

### Transforms
- [ ] TranslationX/Y, Rotation/RotationX/RotationY, Scale/ScaleX/ScaleY, AnchorX/AnchorY

### Effects
- [ ] Shadow (drop shadow), Clip (rounded rect, ellipse, path)

### Automation & Interactivity
- [ ] AutomationId, Semantic properties, FlowDirection, ZIndex
- [ ] ToolTip, ContextFlyout

---

## 13. Font Management

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] IFontManager | | Resolves `Font` → native font |
| [ ] IFontRegistrar | | Registers embedded fonts with aliases |
| [ ] IEmbeddedFontLoader | | Loads font files from assembly resources |
| [ ] Native Font Loading | | Platform font registration (CoreText/fontconfig/DirectWrite) |
| [ ] IFontNamedSizeService | | Maps NamedSize enum to platform point sizes |
| [ ] Font properties on controls | | Font mapped on Label, Entry, Editor, Button, Picker |
| [ ] FontImageSource | | Render font glyphs to images |

---

## 14. Essentials / Platform Services

| Service | Interface | Priority | Notes |
|---------|-----------|----------|-------|
| [ ] App Info | IAppInfo | High | Name, version, theme |
| [ ] Battery | IBattery | Medium | Level, state, source |
| [ ] Browser | IBrowser | High | Open URLs |
| [ ] Clipboard | IClipboard | High | Copy/paste |
| [ ] Connectivity | IConnectivity | Medium | Network state |
| [ ] Device Display | IDeviceDisplay | High | Size, density, orientation |
| [ ] Device Info | IDeviceInfo | High | Platform, idiom, model |
| [ ] Email | IEmail | Medium | Compose via system client |
| [ ] File Picker | IFilePicker | High | Native file dialog |
| [ ] File System | IFileSystem | High | App data/cache dirs |
| [ ] Geolocation | IGeolocation | Low | Location services |
| [ ] Haptic Feedback | IHapticFeedback | Low | Tactile feedback |
| [ ] Launcher | ILauncher | High | Open URIs |
| [ ] Map | IMap | Low | Open map app |
| [ ] Media Picker | IMediaPicker | Medium | Photo/video selection |
| [ ] Preferences | IPreferences | High | Key-value storage |
| [ ] Screenshot | IScreenshot | Medium | Window capture |
| [ ] Secure Storage | ISecureStorage | High | Encrypted storage |
| [ ] Semantic Screen Reader | ISemanticScreenReader | Medium | Accessibility |
| [ ] Share | IShare | Medium | System share dialog |
| [ ] Text-to-Speech | ITextToSpeech | Low | Speech synthesis |
| [ ] Version Tracking | IVersionTracking | High | Cross-platform (uses IPreferences) |
| [ ] Vibration | IVibration | Low | Usually N/A on desktop |

> ⚠️ **Must use reflection** for `SetDefault()` — see [extensibility-gaps.md](extensibility-gaps.md)

---

## 15. WebView & BlazorWebView

### WebView
| Feature | Status | Notes |
|---------|--------|-------|
| [ ] URL loading | | Navigate to URLs |
| [ ] HTML content | | Display raw HTML |
| [ ] JavaScript execution | | EvaluateJavaScriptAsync |
| [ ] Navigation commands | | GoBack, GoForward, Reload |
| [ ] Navigation events | | Navigating, Navigated |

### BlazorWebView
| Feature | Status | Notes |
|---------|--------|-------|
| [ ] BlazorWebViewHandler | | Bridge IBlazorWebView to native WebView |
| [ ] JavaScript Bridge | | Bidirectional messaging |
| [ ] Static Asset Serving | | `app://` URI scheme handler |
| [ ] Blazor Dispatcher | | Wraps MAUI IDispatcher |
| [ ] Host Page / StartPath | | Configurable entry point |
| [ ] Root Components | | Component registration |

> ⚠️ **Requires bypass** of `AddMauiBlazorWebView()` — see [extensibility-gaps.md](extensibility-gaps.md)

---

## 16. Additional Features

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] FormattedText/Spans | | Attributed/markup string rendering |
| [ ] MenuBar (desktop) | | MenuBarItem, MenuFlyoutItem, submenus |
| [ ] Animations / ITicker | | UI-thread-safe ticker at ~60fps |
| [ ] VisualStateManager | | PointerOver, Pressed, Focused, Disabled states |
| [ ] ControlTemplate | | ContentPresenter, TemplatedView (cross-platform) |
| [ ] Image Source Types | | File, URI, Stream, FontImage |
| [ ] Lifecycle Events | | App launched/activated/deactivated/terminating |
| [ ] App Theme / Dark Mode | | System detection, programmatic switching |
| [ ] Build Targets / Resizetizer | | MauiImage, MauiIcon, MauiFont, MauiAsset processing |

---

## Summary Statistics

| Category | Implemented | Total | Coverage |
|----------|-------------|-------|----------|
| Core Infrastructure | _ of 6 | 6 | _% |
| Application & Window | _ of 5 | 5 | _% |
| Pages | _ of 5 | 5 | _% |
| Layouts | _ of 10 | 10 | _% |
| Basic Controls | _ of 14 | 14 | _% |
| Input Controls | _ of 4 | 4 | _% |
| Collection Controls | _ of 7 | 7 | _% |
| Navigation | _ of 5 | 5 | _% |
| Alerts & Dialogs | _ of 3 | 3 | _% |
| Gestures | _ of 8 | 8 | _% |
| Graphics & Shapes | _ of 6 | 6 | _% |
| Base View Properties | _ of 20+ | 20+ | _% |
| Font Services | _ of 7 | 7 | _% |
| Essentials | _ of 23+ | 23+ | _% |
| WebView | _ of 5 | 5 | _% |
| BlazorWebView | _ of 6 | 6 | _% |
| Additional Features | _ of 9 | 9 | _% |
