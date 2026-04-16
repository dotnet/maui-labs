# .NET MAUI Backend Implementation Checklist — Template

A comprehensive, platform-agnostic checklist for implementing a complete .NET MAUI backend for any target platform. This document covers every handler, service, build target, and infrastructure component required to bring up a fully functional MAUI backend, along with known extensibility gaps and workarounds.

> **Existing Reference Implementations:**
> - **macOS (AppKit):** [shinyorg/mauiplatforms](https://github.com/shinyorg/mauiplatforms) — `src/Platform.Maui.MacOS/`
> - **Linux (GTK4):** [`platforms/Linux.Gtk4/`](../../platforms/Linux.Gtk4/) in this repo
> - **MAUI Source Code:** [dotnet/maui](https://github.com/dotnet/maui)

---

## AI-Assisted Backend Creation — Starter Prompts

Use these prompts to bootstrap a new .NET MAUI backend with AI assistance. Each prompt is designed to be self-contained and can be given to an AI coding assistant to generate implementation scaffolding.

### Prompt 1: Research & Architecture Planning

```
I want to create a new .NET MAUI backend for [PLATFORM_NAME] using [UI_TOOLKIT].

Before we start coding, help me research and plan:

1. **Platform documentation:**
   - Where are the official docs for [UI_TOOLKIT]?
   - What is the native view base class (equivalent of UIView/NSView/Gtk.Widget)?
   - How does the platform handle layout? (constraint-based, frame-based, CSS, custom?)
   - How does the platform handle main-thread dispatching?
   - What gesture/input system does the platform use?
   - What text rendering system is available (for Label, Entry, Editor)?
   - What is the platform's image loading/display system?
   - Does the platform have a native WebView component?

2. **Mapping MAUI concepts to platform APIs:**
   - Map `IView` to native view type
   - Map layout system (MAUI computes frames → how to apply them)
   - Map property change propagation (MAUI fires property changes → native setters)
   - Map gesture recognizers to native input events
   - Map font loading to platform font system
   - Map image source types to platform image loading

3. **Project structure:**
   - Reference the macOS backend at https://github.com/shinyorg/mauiplatforms
     and the GTK4 backend at `platforms/Linux.Gtk4/` in this repo for structure patterns
   - Plan the NuGet package structure with MSBuild .targets/.props files
   - Plan the Essentials project structure (separate from the UI handlers)

4. **Known MAUI extensibility gaps:**
   - Review https://github.com/dotnet/maui/issues/34099 and sub-issues
   - Plan workarounds for: Essentials static Default, AlertManager, MainThread,
     BlazorWebView registration, Resizetizer integration
```

### Prompt 2: Core Infrastructure Scaffolding

```
Create the core infrastructure for a .NET MAUI backend targeting [PLATFORM_NAME]
using [UI_TOOLKIT]. I need:

1. A base handler class `[Platform]ViewHandler<TVirtualView, TPlatformView>` that:
   - Inherits conceptually from MAUI's handler pattern
   - Maps IView properties to native view properties
   - Implements GetDesiredSize (measurement) and PlatformArrange (layout)
   - Provides a PropertyMapper with common view property mappings
   - Reference: https://github.com/dotnet/maui/blob/main/src/Core/src/Handlers/View/ViewHandlerOfT.cs

2. A container view class (equivalent to UIView/NSView) that:
   - Supports child view management (add/remove/reorder)
   - Supports layer-backed rendering for transforms, shadows, clipping
   - Uses a top-left coordinate system (MAUI assumes top-left origin)

3. A dispatcher implementation:
   - `[Platform]Dispatcher : IDispatcher` using the platform's main-thread mechanism
   - `[Platform]DispatcherProvider : IDispatcherProvider`
   - `[Platform]DispatcherTimer` for animation/timer support

4. An app host builder extension:
   - `UseMaui[Platform]<TApp>()` extension method
   - Registers all handlers via ConfigureMauiHandlers
   - Registers dispatcher, font manager, ticker, alert manager
   - Reference: macOS uses `UseMauiAppMacOS<TApp>()`, GTK uses `UseLinuxGtkHandlers()`

5. A MauiContext implementation:
   - `[Platform]MauiContext : IMauiContext` with scoped services
   - Window scope and application scope support

Use the macOS backend (https://github.com/shinyorg/mauiplatforms/tree/main/src/Platform.Maui.MacOS)
and GTK4 backend (`platforms/Linux.Gtk4/` in this repo)
as references for the implementation pattern.
```

### Prompt 3: Control Handler Implementation

```
Implement the [CONTROL_NAME] handler for the [PLATFORM_NAME] MAUI backend.

The handler should:
1. Inherit from `[Platform]ViewHandler<I[Control], [NativeControl]>`
2. Map all properties defined in the I[Control] interface
3. Wire up native events to MAUI virtual view event dispatching
4. Support measurement (GetDesiredSize) and arrangement (PlatformArrange)

Reference the MAUI interface definition at:
https://github.com/dotnet/maui/tree/main/src/Core/src/Handlers/[Control]/

And the existing implementations:
- macOS: https://github.com/shinyorg/mauiplatforms/blob/main/src/Platform.Maui.MacOS/Handlers/[Control]Handler.cs
- GTK4: `platforms/Linux.Gtk4/src/Linux.Gtk4/Handlers/[Control]Handler.cs` (in this repo)
- iOS: https://github.com/dotnet/maui/blob/main/src/Core/src/Handlers/[Control]/[Control]Handler.iOS.cs
- Android: https://github.com/dotnet/maui/blob/main/src/Core/src/Handlers/[Control]/[Control]Handler.Android.cs

Map every property from the interface. For properties that have no native equivalent
on [PLATFORM_NAME], document them as no-ops with a comment explaining why.
```

### Prompt 4: Essentials Implementation

```
Implement the [ESSENTIALS_SERVICE] for the [PLATFORM_NAME] MAUI backend.

The service should:
1. Implement the I[Service] interface from Microsoft.Maui.Essentials
2. Use platform-native APIs where available
3. Return IsSupported=false for capabilities that don't exist on [PLATFORM_NAME]
4. Register via DI in the Essentials extension method

IMPORTANT: Due to MAUI's current architecture (see https://github.com/dotnet/maui/issues/34100),
you must also wire the implementation into the static `[Service].Default` property.
The `SetDefault()` method is internal, so use reflection:


var setDefault = typeof([Service]).GetMethod("SetDefault",
    BindingFlags.Static | BindingFlags.NonPublic);
setDefault?.Invoke(null, new object[] { new [Platform][Service]() });
```

Reference implementations:
- macOS: https://github.com/shinyorg/mauiplatforms/tree/main/src/Platform.Maui.Essentials.MacOS/
- GTK4: `platforms/Linux.Gtk4/src/Linux.Gtk4.Essentials/` (in this repo)


### Prompt 5: Resizetizer & Build Targets

```
Create MSBuild targets for the [PLATFORM_NAME] MAUI backend to handle:

1. MauiIcon → platform app icon format
2. MauiImage → platform image resources (SVG→PNG at multiple resolutions)
3. MauiFont → font bundling for the platform
4. MauiAsset → raw asset deployment
5. MauiSplashScreen → platform splash screen (if applicable)

Reference:
- MAUI Resizetizer targets: https://github.com/dotnet/maui/tree/main/src/SingleProject/Resizetizer/src/nuget/buildTransitive/
- macOS targets: https://github.com/shinyorg/mauiplatforms/blob/main/src/Platform.Maui.MacOS/build/
- GTK4 targets: `platforms/Linux.Gtk4/src/Linux.Gtk4/buildTransitive/` (in this repo)

Known issue: Resizetizer is tightly coupled to MAUI SDK platforms.
See https://github.com/dotnet/maui/issues/34102 and https://github.com/dotnet/maui/issues/34222
for the extensibility gaps. You'll likely need to implement your own image processing
or hook into the Resizetizer pipeline with AfterTargets.
```

---

## Implementation Checklist

> ✅ = Implemented | ⚠️ = Partial | ❌ = Not implemented | N/A = Not applicable

---

### 1. Core Infrastructure

#### Platform Abstractions
- [ ] **Platform View Type** — Define the native view base class (e.g., `UIView`, `NSView`, `Gtk.Widget`, `HWND`)
- [ ] **Platform Context** — `[Platform]MauiContext : IMauiContext` with scoped DI services, handler factory, window/app scope
- [ ] **Dispatcher** — `[Platform]Dispatcher : IDispatcher` using platform's main-thread dispatch mechanism + `[Platform]DispatcherTimer`
- [ ] **Event System** — Platform's native event/signal system wired to MAUI gesture recognizers and input handlers
- [ ] **Handler Factory Integration** — All handlers registered via `ConfigureMauiHandlers` in the app host builder extension
- [ ] **App Host Builder Extension** — `UseMaui[Platform]<TApp>()` wires up all handlers, dispatcher, font manager, alert system, ticker

> **MAUI Source Reference:**
> - [`IMauiContext`](https://github.com/dotnet/maui/blob/main/src/Core/src/IMauiContext.cs)
> - [`IDispatcher`](https://github.com/dotnet/maui/blob/main/src/Core/src/Dispatching/IDispatcher.cs)
> - [`MauiAppBuilder`](https://github.com/dotnet/maui/blob/main/src/Core/src/Hosting/MauiAppBuilder.cs)

#### Rendering Pipeline
- [ ] **Base View Handler** — `[Platform]ViewHandler<TVirtualView, TPlatformView>` bridges MAUI's virtual view tree to native views
- [ ] **Property Change Propagation** — `IPropertyMapper` re-maps views when `IView` property changes fire
- [ ] **Child Synchronization** — Container view handles add/remove/reorder of child subviews for layouts
- [ ] **Style/Attribute Application** — Common view properties (opacity, visibility, background, transforms, etc.) applied to native views
- [ ] **Layout Negotiation** — Platform applies MAUI-computed layout frames to native views (MAUI uses top-left origin coordinate system)

> **Key Concept:** MAUI's cross-platform layout engine computes all frames via `CrossPlatformMeasure` and `CrossPlatformArrange`. Your handler just needs to call `GetDesiredSize` (which delegates to MAUI's measurement) and `PlatformArrange` (which applies the computed `Rect` to your native view's frame/bounds).
>
> **MAUI Source Reference:**
> - [`ViewHandler<TVirtualView, TPlatformView>`](https://github.com/dotnet/maui/blob/main/src/Core/src/Handlers/View/ViewHandlerOfT.cs)
> - [`ILayoutManager`](https://github.com/dotnet/maui/blob/main/src/Core/src/Layouts/ILayoutManager.cs)
>
> **Existing Implementations:**
> - macOS: `MacOSViewHandler` applies frames directly to `NSView.Frame`
> - GTK4: `GtkViewHandler` uses `GtkLayoutPanel` with `CustomLayout` P/Invoke to override GTK's minimum-based allocation

#### Native Interop
- [ ] **Native Event Handling** — Mouse/touch events, keyboard events, focus events
- [ ] **Gesture Controller Integration** — Platform gesture recognizers mapped to MAUI gesture types
- [ ] **Accessibility** — Platform accessibility APIs (VoiceOver, TalkBack, AT-SPI, etc.) mapped to MAUI semantic properties

---

### 2. Application & Window

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] **Application** | | Lifecycle management — maps `IApplication` to native app delegate/instance |
| [ ] **Window** | | Maps Title, Content, Width, Height, X, Y, MinWidth/MinHeight, MaxWidth/MaxHeight |
| [ ] **Multi-window** | | Multiple window tracking, window lifecycle events (Creating, Activated, Destroying) |
| [ ] **App Theme / Dark Mode** | | Light/Dark/System theme detection and switching |
| [ ] **Window Close / Lifecycle** | | Destroying event, window removal, cleanup |

> **MAUI Source Reference:**
> - [`IApplication`](https://github.com/dotnet/maui/blob/main/src/Core/src/IApplication.cs)
> - [`IWindow`](https://github.com/dotnet/maui/blob/main/src/Core/src/IWindow.cs)

---

### 3. Pages

| Page | Status | Notes |
|------|--------|-------|
| [ ] **ContentPage** | | Maps Content, Background, Title; hosts single content view |
| [ ] **NavigationPage** | | Push/Pop stack navigation; back button; title propagation |
| [ ] **TabbedPage** | | Tab bar with page switching; BarBackgroundColor, SelectedTabColor |
| [ ] **FlyoutPage** | | Side panel (flyout) + detail content; IsPresented, FlyoutBehavior, FlyoutWidth |
| [ ] **Shell** | | Flyout navigation, tab bars, content hierarchy, route-based navigation |

> **MAUI Source Reference:**
> - Page handlers: [`src/Controls/src/Core/Platform/`](https://github.com/dotnet/maui/tree/main/src/Controls/src/Core/Platform)
> - Shell: [`src/Controls/src/Core/Shell/`](https://github.com/dotnet/maui/tree/main/src/Controls/src/Core/Shell)
>
> **Existing Implementations:**
> - macOS: Shell uses `NSSplitView` with native sidebar (`NSOutlineView`) or custom MAUI sidebar
> - GTK4: Shell uses `Gtk.ListBox` flyout + `Gtk.Notebook` tabs

---

### 4. Layouts

| Layout | Status | Notes |
|--------|--------|-------|
| [ ] **VerticalStackLayout** | | Handled by `LayoutHandler` — MAUI's cross-platform layout manager computes frames |
| [ ] **HorizontalStackLayout** | | Same as above |
| [ ] **Grid** | | Row/column definitions, spans, spacing — all computed by MAUI layout manager |
| [ ] **FlexLayout** | | Direction, Wrap, JustifyContent, AlignItems — MAUI layout manager handles positioning |
| [ ] **AbsoluteLayout** | | Absolute and proportional positioning — MAUI layout manager computes bounds |
| [ ] **ScrollView** | | Maps Content, Orientation, ScrollBarVisibility, ContentSize; ScrollToAsync; Scrolled event |
| [ ] **ContentView** | | Simple content wrapper with Background support |
| [ ] **Border** | | Stroke, StrokeThickness, StrokeShape, StrokeLineCap, StrokeLineJoin, StrokeDashPattern |
| [ ] **Frame** | | Legacy border — typically registered to `BorderHandler` |
| [ ] **Layout (fallback)** | | Base `LayoutHandler` for custom layout subclasses |

> **Key Concept:** All stack/grid/flex/absolute layouts share a single `LayoutHandler`. MAUI's layout engine does all measurement and arrangement cross-platform. Your `LayoutHandler` just needs to:
> 1. Create a container view
> 2. Add/remove child views as the layout's Children change
> 3. Apply frames computed by `CrossPlatformArrange` to each child's native view
>
> **MAUI Source Reference:**
> - [`LayoutHandler`](https://github.com/dotnet/maui/blob/main/src/Core/src/Handlers/Layout/LayoutHandler.cs)
> - Layout managers: [`src/Core/src/Layouts/`](https://github.com/dotnet/maui/tree/main/src/Core/src/Layouts)

---

### 5. Basic Controls

| Control | Status | Notes |
|---------|--------|-------|
| [ ] **Label** | | Text, TextColor, Font, HorizontalTextAlignment, LineBreakMode, MaxLines, TextDecorations, CharacterSpacing, Padding, FormattedText/Spans |
| [ ] **Button** | | Text, TextColor, Font, CharacterSpacing, Background, CornerRadius, StrokeColor, StrokeThickness, Padding, ImageSource, Clicked event |
| [ ] **ImageButton** | | Source (file/URI), Clicked, Background, CornerRadius, StrokeColor, StrokeThickness |
| [ ] **Entry** | | Text, TextColor, Font, Placeholder, PlaceholderColor, IsPassword, IsReadOnly, HorizontalTextAlignment, MaxLength, ReturnType, CursorPosition, SelectionLength, IsTextPredictionEnabled |
| [ ] **Editor** | | Text, TextColor, Font, IsReadOnly, HorizontalTextAlignment, MaxLength, CharacterSpacing, Placeholder, AutoSize |
| [ ] **Switch** | | IsOn, OnColor, ThumbColor, TrackColor |
| [ ] **CheckBox** | | IsChecked, Color |
| [ ] **RadioButton** | | IsChecked, TextColor, Content, GroupName (mutual exclusion handled by MAUI's cross-platform RadioButtonGroup) |
| [ ] **Slider** | | Value, Minimum, Maximum, MinimumTrackColor, MaximumTrackColor, ThumbColor |
| [ ] **Stepper** | | Value, Minimum, Maximum, Increment |
| [ ] **ProgressBar** | | Progress, ProgressColor |
| [ ] **ActivityIndicator** | | IsRunning, Color |
| [ ] **BoxView** | | Mapped via `ShapeViewHandler` |
| [ ] **Image** | | Source (file/URI/stream/font), Aspect, IsOpaque |

> **MAUI Source Reference:**
> - Control interfaces: [`src/Core/src/`](https://github.com/dotnet/maui/tree/main/src/Core/src) (e.g., `ILabel.cs`, `IButton.cs`)
> - Handler implementations: [`src/Core/src/Handlers/`](https://github.com/dotnet/maui/tree/main/src/Core/src/Handlers)

---

### 6. Input & Selection Controls

| Control | Status | Notes |
|---------|--------|-------|
| [ ] **Picker** | | Title, SelectedIndex, Items, TextColor |
| [ ] **DatePicker** | | Date, MinimumDate, MaximumDate, TextColor, Format |
| [ ] **TimePicker** | | Time, TextColor, Format |
| [ ] **SearchBar** | | Text, TextColor, Placeholder, IsReadOnly, MaxLength |

---

### 7. Collection Controls

| Control | Status | Notes |
|---------|--------|-------|
| [ ] **CollectionView** | | ItemsSource, ItemTemplate, SelectionMode (single/multiple), GroupHeaders, EmptyView, Header/Footer, ScrollTo, RemainingItemsThreshold |
| [ ] **ListView** | | ItemsSource, ItemTemplate, ViewCell/TextCell/ImageCell/SwitchCell/EntryCell, Selection, Header/Footer, Grouping, SeparatorVisibility |
| [ ] **CarouselView** | | Horizontal/vertical paging with snap, Position, CurrentItem, Loop |
| [ ] **IndicatorView** | | Page indicator dots, IndicatorColor, SelectedIndicatorColor, IndicatorSize |
| [ ] **TableView** | | TableRoot/TableSection, TextCell, SwitchCell, EntryCell, ViewCell |
| [ ] **SwipeView** | | Swipe-to-reveal actions with LeftItems/RightItems |
| [ ] **RefreshView** | | IsRefreshing, Command; desktop platforms typically use a refresh button instead of pull-to-refresh |

> **Key Challenge:** Collection controls require virtualization for performance. Consider:
> - Does the platform have a native list/table view with recycling? (e.g., `UITableView`, `RecyclerView`, `Gtk.ListView`)
> - If not, you'll need to build a virtualization layer on top of a scroll view

---

### 8. Navigation & Routing

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] **NavigationPage stack** | | PushAsync, PopAsync, PopToRootAsync |
| [ ] **Shell navigation** | | Flyout, tabs, content hierarchy, route-based navigation |
| [ ] **Modal navigation** | | Push/pop modal pages with backdrop overlay |
| [ ] **Back button** | | Platform-appropriate back navigation (toolbar button, hardware back, etc.) |
| [ ] **ToolbarItems** | | Primary/secondary toolbar items rendered in platform's toolbar/action bar |

---

### 9. Alerts & Dialogs

| Dialog | Status | Notes |
|--------|--------|-------|
| [ ] **DisplayAlert** | | Title, message, accept/cancel buttons |
| [ ] **DisplayActionSheet** | | Multi-button action sheet with destructive/cancel options |
| [ ] **DisplayPromptAsync** | | Text input dialog with placeholder, initial value, validation |

> **⚠️ Known Extensibility Gap:** The alert system relies on an internal `AlertManager` class and private `IAlertManagerSubscription` interface. Custom backends must use `DispatchProxy` + reflection to intercept dialog requests.
>
> **Workaround:** See [dotnet/maui#34104](https://github.com/dotnet/maui/issues/34104) for details.
>
> **Existing Implementations:**
> - macOS: `AlertManagerSubscription` uses `DispatchProxy` to intercept → `NSAlert.RunModal()`
> - GTK4: Similar `DispatchProxy` pattern → custom GTK `Window` dialogs
>
> ```csharp
> // Workaround: Create DispatchProxy for internal IAlertManagerSubscription
> var amType = typeof(Window).Assembly
>     .GetType("Microsoft.Maui.Controls.Platform.AlertManager");
> var iamsType = amType.GetNestedType("IAlertManagerSubscription",
>     BindingFlags.Public | BindingFlags.NonPublic);
> var proxy = DispatchProxy.Create(iamsType, typeof(YourAlertProxy));
> services.AddSingleton(iamsType, proxy);
> ```

---

### 10. Gesture Recognizers

| Gesture | Status | Notes |
|---------|--------|-------|
| [ ] **TapGestureRecognizer** | | Platform click/tap recognizer with NumberOfTapsRequired, Command |
| [ ] **PanGestureRecognizer** | | Drag/pan with translation tracking (TotalX, TotalY) |
| [ ] **SwipeGestureRecognizer** | | Velocity-based swipe direction detection (Left/Right/Up/Down) |
| [ ] **PinchGestureRecognizer** | | Pinch-to-zoom scale tracking (trackpad/touch) |
| [ ] **PointerGestureRecognizer** | | Mouse/pointer enter/exit/move tracking |
| [ ] **DragGestureRecognizer** | | Drag source for drag-and-drop |
| [ ] **DropGestureRecognizer** | | Drop target for drag-and-drop |
| [ ] **LongPressGestureRecognizer** | | Long press/hold detection |

> **MAUI Source Reference:**
> - Gesture platform managers: [`src/Controls/src/Core/Platform/GestureManager/`](https://github.com/dotnet/maui/tree/main/src/Controls/src/Core/Platform/GestureManager)
>
> **Existing Implementations:**
> - macOS: Wraps `NSClickGestureRecognizer`, `NSPanGestureRecognizer`, `NSMagnificationGestureRecognizer`, `NSTrackingArea`
> - GTK4: Wraps `Gtk.GestureClick`, `Gtk.GestureDrag`, `Gtk.GestureSwipe`, `Gtk.GestureZoom`, `Gtk.EventControllerMotion`

---

### 11. Graphics & Shapes

#### Microsoft.Maui.Graphics
| Feature | Status | Notes |
|---------|--------|-------|
| [ ] **GraphicsView** | | Platform drawing surface with `IDrawable` rendering |
| [ ] **Canvas Operations** | | DrawLine, DrawRect, DrawEllipse, DrawPath, DrawString, Fill operations |
| [ ] **Canvas State** | | SaveState/RestoreState, transforms |
| [ ] **Brushes** | | SolidColorBrush, LinearGradientBrush, RadialGradientBrush |

#### Shapes
| Shape | Status | Notes |
|-------|--------|-------|
| [ ] **ShapeViewHandler** | | Single handler renders all shape types via `IShape` + platform graphics paths |
| [ ] **Fill & Stroke** | | Fill brush and Stroke properties |

> **Key Concept:** Individual shape types (Rectangle, Ellipse, Line, Path, Polygon, Polyline) are handled by MAUI's cross-platform shape geometry. Your `ShapeViewHandler` just draws whatever `IShape.PathForBounds()` provides using the platform's 2D graphics API.
>
> **MAUI Source Reference:**
> - [`GraphicsView`](https://github.com/dotnet/maui/blob/main/src/Core/src/Handlers/GraphicsView/GraphicsViewHandler.cs)
>
> **Existing Implementations:**
> - macOS: CoreGraphics (`CGPath`, `CGContext`) in `ShapeNSView`
> - GTK4: Cairo (`CairoCanvas`) in `Gtk.DrawingArea`

---

### 12. Common View Properties (Base Handler)

Every handler must inherit these property mappings from your base `[Platform]ViewHandler`:

#### Visibility & State
- [ ] Opacity → native alpha/opacity
- [ ] IsVisible → native visibility toggle
- [ ] IsEnabled → native enabled/sensitive state
- [ ] InputTransparent → pass-through hit testing

#### Sizing
- [ ] WidthRequest / HeightRequest — respected during `GetDesiredSize` measurement
- [ ] MinimumWidthRequest / MinimumHeightRequest — used as floor in measurement
- [ ] MaximumWidthRequest / MaximumHeightRequest — used as ceiling

#### Layout
- [ ] HorizontalOptions / VerticalOptions — handled by MAUI cross-platform layout
- [ ] Margin — handled by MAUI layout engine
- [ ] Padding — mapped on individual handlers (Entry, Editor, Button, etc.)
- [ ] FlowDirection (LTR, RTL) → native text/layout direction
- [ ] ZIndex → native view ordering

#### Appearance
- [ ] BackgroundColor / Background → native view background (solid color, gradient)

#### Transforms
- [ ] TranslationX / TranslationY
- [ ] Rotation / RotationX / RotationY
- [ ] Scale / ScaleX / ScaleY
- [ ] AnchorX / AnchorY → transform anchor point

#### Effects
- [ ] Shadow → native shadow (drop shadow, box shadow, etc.)
- [ ] Clip → native clipping (rounded rect, ellipse, path)

#### Automation
- [ ] AutomationId → native accessibility identifier
- [ ] Semantic properties → native accessibility label/description/role

#### Interactivity Attachments
- [ ] **ToolTip** — `ToolTipProperties.Text` → native tooltip
- [ ] **ContextFlyout** — `FlyoutBase.GetContextFlyout()` → native context menu on right-click

---

### 13. VisualStateManager & Triggers

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] **VisualStateManager** | | Cross-platform MAUI feature — may need platform hooks for hover/pressed/focus states |
| [ ] **PropertyTrigger** | | Cross-platform — no platform handler needed |
| [ ] **DataTrigger** | | Cross-platform — no platform handler needed |
| [ ] **MultiTrigger** | | Cross-platform — no platform handler needed |
| [ ] **EventTrigger** | | Cross-platform — no platform handler needed |
| [ ] **Behaviors** | | Cross-platform — no platform handler needed |

> **Note:** While triggers are cross-platform, the VisualStateManager may need platform assistance for interactive states:
> - **PointerOver** — requires mouse tracking/hover detection
> - **Pressed** — requires mouse-down/touch-down detection
> - **Focused** — requires focus tracking
> - **Disabled** — typically maps to IsEnabled changes
>
> **Existing Implementations:**
> - GTK4: Uses `EventControllerMotion`/`GestureClick`/`EventControllerFocus` to trigger `GoToState`
> - macOS: Works via `NSTrackingArea` for hover, `NSResponder` for focus

---

### 14. Font Management

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] **IFontManager** | | Resolves `Font` → native font object with family, size, weight, slant |
| [ ] **IFontRegistrar** | | Registers embedded fonts with aliases |
| [ ] **IEmbeddedFontLoader** | | Loads font files from assembly resources into the platform's font system |
| [ ] **Native Font Loading** | | Platform-specific font registration (CoreText, fontconfig, DirectWrite, etc.) |
| [ ] **IFontNamedSizeService** | | Maps `NamedSize` enum (Default, Micro, Small, Medium, Large, Body, Header, Title, Subtitle, Caption) to platform point sizes |
| [ ] **Font properties** | | Font mapped on Label, Entry, Editor, Button, Picker, etc. |
| [ ] **FontImageSource** | | Render font glyphs to images for use in Image, Button.ImageSource, ToolbarItems |

> **MAUI Source Reference:**
> - [`IFontManager`](https://github.com/dotnet/maui/blob/main/src/Core/src/Fonts/IFontManager.cs)
> - [`IEmbeddedFontLoader`](https://github.com/dotnet/maui/blob/main/src/Core/src/Fonts/IEmbeddedFontLoader.cs)
> - [`FontManager` (iOS)](https://github.com/dotnet/maui/blob/main/src/Core/src/Fonts/FontManager.iOS.cs)
>
> **Existing Implementations:**
> - macOS: `CGDataProvider` → `CGFont` → `CTFontManager.RegisterGraphicsFont()` → `NSFont`
> - GTK4: Extracts fonts to `~/.local/share/fonts/` + calls `pango_fc_font_map_config_changed`

---

### 15. Essentials / Platform Services

#### Services to Implement

Every MAUI Essentials service needs either a real implementation or a no-op stub:

| Service | Interface | Priority | Notes |
|---------|-----------|----------|-------|
| [ ] **App Info** | `IAppInfo` | High | App name, version, package, RequestedTheme |
| [ ] **Battery** | `IBattery` | Medium | Charge level, state, power source |
| [ ] **Browser** | `IBrowser` | High | Open URLs in default browser |
| [ ] **Clipboard** | `IClipboard` | High | Copy/paste text |
| [ ] **Connectivity** | `IConnectivity` | Medium | Network state detection |
| [ ] **Device Display** | `IDeviceDisplay` | High | Screen size, density, orientation |
| [ ] **Device Info** | `IDeviceInfo` | High | Platform, idiom, model, OS version |
| [ ] **Email** | `IEmail` | Medium | Compose email via system client |
| [ ] **File Picker** | `IFilePicker` | High | Native file selection dialog |
| [ ] **File System** | `IFileSystem` | High | App data/cache directories |
| [ ] **Geolocation** | `IGeolocation` | Low | Location services |
| [ ] **Haptic Feedback** | `IHapticFeedback` | Low | Tactile feedback (if hardware supports it) |
| [ ] **Launcher** | `ILauncher` | High | Open URIs via system handler |
| [ ] **Map** | `IMap` | Low | Open map application |
| [ ] **Media Picker** | `IMediaPicker` | Medium | Photo/video selection |
| [ ] **Preferences** | `IPreferences` | High | Key-value persistent storage |
| [ ] **Screenshot** | `IScreenshot` | Medium | Window/screen capture |
| [ ] **Secure Storage** | `ISecureStorage` | High | Encrypted key-value storage |
| [ ] **Semantic Screen Reader** | `ISemanticScreenReader` | Medium | Screen reader announcements |
| [ ] **Share** | `IShare` | Medium | System share sheet/dialog |
| [ ] **Text-to-Speech** | `ITextToSpeech` | Low | Speech synthesis |
| [ ] **Version Tracking** | `IVersionTracking` | High | Cross-platform — uses IPreferences + IAppInfo |
| [ ] **Vibration** | `IVibration` | Low | Usually N/A for desktop platforms |
| [ ] **Sensors** | `IAccelerometer`, `IGyroscope`, etc. | Low | Usually N/A for desktop platforms |

> **⚠️ Known Extensibility Gap:** Essentials use internal `SetDefault()` methods. Custom backends must use reflection to wire implementations into the static `XXX.Default` properties.
>
> **Workaround:** See [dotnet/maui#34100](https://github.com/dotnet/maui/issues/34100) for details.
>
> ```csharp
> // Register in DI AND set the static default via reflection
> services.AddSingleton<IPreferences>(new MyPreferences());
> var setDefault = typeof(Preferences).GetMethod("SetDefault",
>     BindingFlags.Static | BindingFlags.NonPublic);
> setDefault?.Invoke(null, new object[] { new MyPreferences() });
> ```
>
> **MAUI Source Reference:**
> - Essentials: [`src/Essentials/src/`](https://github.com/dotnet/maui/tree/main/src/Essentials/src)

---

### 16. Styling Infrastructure

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] **Border style mapping** | | Stroke, StrokeShape, StrokeThickness, StrokeLineCap, StrokeLineJoin, StrokeDashPattern via platform 2D graphics |
| [ ] **View state mapping** | | IsVisible, IsEnabled, Opacity mapped in base handler |
| [ ] **Automation mapping** | | AutomationId → native accessibility identifier |

---

### 17. WebView

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] **URL loading** | | Navigate to URLs via platform web engine |
| [ ] **HTML content** | | Display raw HTML |
| [ ] **JavaScript execution** | | `EvaluateJavaScriptAsync` |
| [ ] **Navigation commands** | | GoBack, GoForward, Reload |
| [ ] **Navigation events** | | Navigating, Navigated |
| [ ] **User Agent** | | Custom user agent string |

> **Key Question:** What web engine is available on your target platform?
> - macOS/iOS: `WKWebView` (WebKit)
> - Windows: `WebView2` (Chromium)
> - Linux/GTK: `WebKitGTK` (WebKit2)
> - Android: `android.webkit.WebView` (Chromium)

---

### 18. BlazorWebView (Blazor Hybrid)

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] **BlazorWebViewHandler** | | Handler bridging `IBlazorWebView` to native WebView with JS interop |
| [ ] **JavaScript Bridge** | | `window.external.sendMessage` / `__dispatchMessageCallback` bidirectional messaging |
| [ ] **Static Asset Serving** | | `app://` URI scheme handler serving Blazor framework files from app bundle |
| [ ] **Blazor Dispatcher** | | Dispatcher bridge wrapping MAUI's `IDispatcher` for Blazor compatibility |
| [ ] **Host Page** | | Configurable `HostPage` property |
| [ ] **StartPath** | | Initial navigation path for Blazor router |
| [ ] **Root Components** | | Component selector, type, and parameters registration |

> **⚠️ Known Extensibility Gap:** `AddMauiBlazorWebView()` only registers handlers for built-in platforms. Custom backends must bypass it entirely and provide their own registration.
>
> **Workaround:** See [dotnet/maui#34103](https://github.com/dotnet/maui/issues/34103) for details.
>
> ```csharp
> #if MY_PLATFORM
>     builder.Services.AddMyPlatformBlazorWebView();
> #else
>     builder.Services.AddMauiBlazorWebView();
> #endif
> ```
>
> **Existing Implementations:**
> - macOS: `WKWebView` with `WKScriptMessageHandler` + custom URL scheme handler for `app://`
> - GTK4: WebKitGTK with custom `app://` URI scheme handler + `Channel<T>` message pump for thread safety

---

### 19. Label — FormattedText Detail

FormattedText requires rich text rendering using platform-specific attributed/markup strings:

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] **FormattedText rendering** | | Build attributed/markup string from `FormattedString.Spans` |
| [ ] **Span.Text** | | Text content per span |
| [ ] **Span.TextColor** | | Foreground color attribute |
| [ ] **Span.BackgroundColor** | | Background color attribute |
| [ ] **Span.FontSize** | | Font size attribute per span |
| [ ] **Span.FontFamily** | | Font family attribute per span |
| [ ] **Span.FontAttributes** | | Bold/Italic |
| [ ] **Span.TextDecorations** | | Underline / Strikethrough |
| [ ] **Span.CharacterSpacing** | | Kerning / letter spacing attribute |

> **Existing Implementations:**
> - macOS: `NSAttributedString` with `NSFont`, `NSColor`, `NSKern`, `NSUnderlineStyle` attributes
> - GTK4: Pango markup via `Gtk.Label.SetMarkup()` with `<span>` attributes

---

### 20. MenuBar (Desktop)

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] **MenuBarItem** | | Top-level menu items |
| [ ] **MenuFlyoutItem** | | Submenu items with Text, Command, KeyboardAccelerators |
| [ ] **MenuFlyoutSubItem** | | Nested submenus (recursive) |
| [ ] **MenuFlyoutSeparator** | | Separator items |
| [ ] **Default Menus** | | App/Edit/Window menus with platform standard items |

> **Note:** Menu bar is primarily a desktop concern. Mobile platforms typically don't have a traditional menu bar.
>
> **Existing Implementations:**
> - macOS: `NSApp.MainMenu` via `MenuBarManager`; default menus include Edit (Undo/Redo/Cut/Copy/Paste) and Window (Minimize/Zoom/Fullscreen)
> - GTK4: `Gtk.PopoverMenuBar` via `GtkMenuBarManager` with `Gio.SimpleAction`

---

### 21. Animations

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] **Platform Ticker** | | Custom `ITicker` using platform's timer mechanism for ~60fps frame updates |
| [ ] **TranslateTo** | | Via MAUI animation system + property mapper applying transforms |
| [ ] **FadeTo** | | Via MAUI animation system + MapOpacity |
| [ ] **ScaleTo** | | Via MAUI animation system + MapTransform |
| [ ] **RotateTo** | | Via MAUI animation system + MapTransform |
| [ ] **LayoutTo** | | Via MAUI animation system |
| [ ] **Easing functions** | | Cross-platform MAUI — no platform code needed |
| [ ] **Animation class** | | Cross-platform MAUI — no platform code needed |

> **Key Concept:** MAUI's animation system is fully cross-platform. It uses `IAnimationManager` + `ITicker` to drive frame updates that set virtual view properties. Your only platform requirement is providing a **main-thread-safe `ITicker`**.
>
> **⚠️ Critical:** The default `System.Timers.Timer`-based ticker fires on the threadpool, which is unsafe for most UI frameworks. You MUST provide a platform ticker that fires on the UI thread.
>
> **MAUI Source Reference:**
> - [`ITicker`](https://github.com/dotnet/maui/blob/main/src/Core/src/Animations/ITicker.cs)
> - [`AnimationManager`](https://github.com/dotnet/maui/blob/main/src/Core/src/Animations/AnimationManager.cs)
>
> **Existing Implementations:**
> - macOS: `MacOSTicker` uses `NSTimer` on main run loop
> - GTK4: `GtkPlatformTicker` uses `GLib.Functions.TimeoutAdd` at ~60fps

---

### 22. ControlTemplate & ContentPresenter

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] **ControlTemplate** | | Cross-platform MAUI feature — template inflation via ContentPresenter |
| [ ] **ContentPresenter** | | Cross-platform — dynamically instantiates template content |
| [ ] **TemplatedView** | | Cross-platform — base class for controls with ControlTemplate support |

> These are fully cross-platform MAUI features that require no platform-specific code, as long as the handler properly renders the visual tree via `IContentView`.

---

### 23. Image Source Types

All four MAUI image source types should be supported:

| Source Type | Status | Notes |
|-------------|--------|-------|
| [ ] **FileImageSource** | | Load images from app bundle/resources by filename |
| [ ] **UriImageSource** | | Async HTTP image loading |
| [ ] **StreamImageSource** | | Load images from streams |
| [ ] **FontImageSource** | | Render font glyphs to images |

> **Existing Implementations:**
> - macOS: Smart fallback chain for FileImageSource (tries exact extension, then .png/.svg/.pdf/.jpg, searches Resources folder, `NSImage.ImageNamed`)
> - GTK4: FontImageSource rendered via Cairo + Pango to PNG temp file → `Gdk.Texture`

---

### 24. Lifecycle Events

| Event | Status | Notes |
|-------|--------|-------|
| [ ] **App Launched** | | Application startup complete |
| [ ] **App Activated** | | Application gained focus |
| [ ] **App Deactivated** | | Application lost focus |
| [ ] **App Terminating** | | Application shutting down |
| [ ] **Platform-specific events** | | Any additional platform lifecycle events |

> **Existing Implementations:**
> - macOS: 6 events (DidFinishLaunching, DidBecomeActive, DidResignActive, DidHide, DidUnhide, WillTerminate)
> - GTK4: 4 events (OnWindowCreated, OnMauiApplicationCreated, OnApplicationActivated, OnApplicationShutdown)

---

### 25. App Theme / Dark Mode

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] **System theme detection** | | Detect platform's current light/dark preference |
| [ ] **UserAppTheme** | | Programmatic theme switching (Light/Dark/Unspecified) |
| [ ] **RequestedThemeChanged** | | Event when system or app theme changes |
| [ ] **AppThemeBinding** | | Cross-platform MAUI feature — works via property binding system |

> **Existing Implementations:**
> - macOS: `NSAppearance` (Aqua / DarkAqua) switching via `ApplicationHandler.MapAppTheme`
> - GTK4: `GtkThemeManager` detects system GTK theme + applies via `Gtk.Settings.GtkApplicationPreferDarkTheme`

---

## 26. Build System & Resizetizer Integration

### Resource Item Types

MAUI uses these MSBuild item types for resources. Your platform needs build targets to process each:

| Item Type | Purpose | Platform Action Needed |
|-----------|---------|----------------------|
| **MauiIcon** | App icon (SVG/PNG source) | Convert to platform icon format (`.icns`, `.ico`, hicolor theme, etc.) |
| **MauiImage** | Image resources (SVG/PNG) | Resize to multiple DPI scales; convert SVG→PNG |
| **MauiFont** | Embedded fonts (TTF/OTF) | Copy to platform bundle location |
| **MauiAsset** | Raw assets (JSON, HTML, etc.) | Copy to platform bundle with LogicalName |
| **MauiSplashScreen** | Splash screen (SVG/PNG) | Convert to platform splash format |

### Resizetizer Architecture

MAUI's Resizetizer processes `MauiImage` and `MauiIcon` items:

1. **Image Processing** (platform-agnostic): SVG→PNG conversion, resizing, tinting
2. **Output Injection** (platform-specific): Adding processed images to platform item groups (`BundleResource`, `AndroidResource`, `Content`, etc.)

> **⚠️ Known Extensibility Gaps:**
>
> **Issue 1: Resizetizer coupled to MAUI SDK** ([dotnet/maui#34102](https://github.com/dotnet/maui/issues/34102) — **CLOSED/FIXED**)
> - Projects not using `Microsoft.Maui.Sdk` couldn't access Resizetizer
> - **Resolution:** Resizetizer can now be referenced independently
>
> **Issue 2: No extension points in Resizetizer pipeline** ([dotnet/maui#34222](https://github.com/dotnet/maui/issues/34222) — **OPEN**)
> - `ResizetizeImages` target couples processing and platform-specific output injection in one step
> - No `DependsOnTargets` properties or hook targets for custom backends
> - Internal item groups (`_ResizetizerCollectedImages`) not documented as public contracts
> - **Workaround:** Use `AfterTargets="ResizetizeImages"` to hook into the pipeline and consume `_ResizetizerCollectedImages`
>
> ```xml
> <!-- Custom platform target to consume processed images -->
> <Target Name="_MyPlatformBundleResizetizedImages"
>         AfterTargets="ResizetizeImages"
>         Condition="'$(UsingMyPlatform)' == 'true'">
>   <ItemGroup>
>     <MyPlatformResource Include="@(_ResizetizerCollectedImages)">
>       <LogicalName>%(Filename)%(Extension)</LogicalName>
>     </MyPlatformResource>
>   </ItemGroup>
> </Target>
> ```

### Existing Platform Build Target Approaches

#### macOS Approach
- **MauiIcon:** Custom target using `sips` (macOS CLI) to generate 10 icon sizes → `iconutil` to create `.icns` file → `BundleResource` + `PartialAppManifest`
- **MauiImage:** Relies on MAUI SDK's Resizetizer (project uses `net10.0-macos` TFM)
- **MauiFont:** Standard MAUI font processing
- **BlazorWebView assets:** Custom target converts `StaticWebAsset` → `BundleResource` with `wwwroot` prefix

#### GTK4 Approach
- **MauiIcon:** Custom `buildTransitive/` targets generate hicolor icon theme (SVG/PNG at multiple sizes for Linux desktop integration)
- **MauiImage:** Custom image processing via build targets (since `net10.0` TFM doesn't import MAUI's Resizetizer)
- **MauiFont:** Extracts to `~/.local/share/fonts/` at runtime
- **MauiAsset:** Custom deployment to output directory

### Recommended Approach for New Backends

1. **If using a MAUI SDK TFM** (e.g., `net10.0-ios`, `net10.0-macos`): Resizetizer runs automatically; add `AfterTargets` hooks for platform-specific output
2. **If using plain `net10.0` TFM**: Reference Resizetizer NuGet package directly ([#34102 fix](https://github.com/dotnet/maui/issues/34102)), then hook `AfterTargets` for output injection
3. **For app icons**: Implement a custom build target since icon formats are highly platform-specific

---

## 27. Known MAUI Extensibility Gaps & Workarounds

These are known issues where MAUI's architecture makes it difficult for third-party backends. Track [dotnet/maui#34099](https://github.com/dotnet/maui/issues/34099) for the umbrella issue.

### Gap 1: Essentials Static `Default` Properties ([#34100](https://github.com/dotnet/maui/issues/34100))

**Problem:** `Preferences.Default`, `FilePicker.Default`, etc. use internal `SetDefault()` methods. DI registration alone is not sufficient — static properties still resolve to reference assembly stubs that throw `NotImplementedInReferenceAssemblyException`.

**Impact:** Any app code using `XXX.Default` (which is common and documented) crashes on custom backends.

**Workaround:**
```csharp
// For EACH Essentials service, use reflection to set the static default:
var setDefault = typeof(Preferences).GetMethod("SetDefault",
    BindingFlags.Static | BindingFlags.NonPublic);
setDefault?.Invoke(null, new object[] { new MyPreferencesImpl() });
```

**Recommendation:** Wrap all reflection calls in try/catch — internal API could change at any time.

---

### Gap 2: `MainThread.BeginInvokeOnMainThread` ([#34101](https://github.com/dotnet/maui/issues/34101))

**Problem:** `MainThread.BeginInvokeOnMainThread()` throws on custom backends even though `Application.Current.Dispatcher` works fine.

**Workaround:**
```csharp
// Replace MainThread usage with Dispatcher throughout your codebase:
// ❌ MainThread.BeginInvokeOnMainThread(() => { ... });
// ✅ Application.Current?.Dispatcher.Dispatch(() => { ... });
```

**Recommendation:** Document for app developers that `Dispatcher` should be used instead of `MainThread` for cross-platform compatibility.

---

### Gap 3: Resizetizer Extensibility ([#34102](https://github.com/dotnet/maui/issues/34102), [#34222](https://github.com/dotnet/maui/issues/34222))

**Problem:** Resizetizer was coupled to MAUI SDK and lacked extension points for custom platforms.

**Status:**
- **#34102 (Standalone access):** ✅ **CLOSED/FIXED** — Resizetizer can now be referenced independently
- **#34222 (Extension targets):** 🔴 **OPEN** — No official hook targets or public item group contracts yet

**Workaround for #34222:**
```xml
<!-- Hook into Resizetizer output using AfterTargets -->
<Target Name="_MyPlatformInjectImages"
        AfterTargets="ResizetizeImages">
  <ItemGroup>
    <!-- Consume the internal item group (fragile — could change) -->
    <MyPlatformResource Include="@(_ResizetizerCollectedImages)" />
  </ItemGroup>
</Target>
```

---

### Gap 4: BlazorWebView Registration ([#34103](https://github.com/dotnet/maui/issues/34103))

**Problem:** `AddMauiBlazorWebView()` only registers handlers for built-in platforms. Custom backends must bypass it and replicate all internal service registrations.

**Workaround:**
```csharp
// In MauiProgram.cs:
#if MY_PLATFORM
    builder.Services.AddMyPlatformBlazorWebView();
#else
    builder.Services.AddMauiBlazorWebView();
#endif

// In your platform's extension method, replicate the shared service registrations
// from AddMauiBlazorWebView() and add your platform handler
```

---

### Gap 5: Alert/Dialog System ([#34104](https://github.com/dotnet/maui/issues/34104))

**Problem:** `AlertManager` and `IAlertManagerSubscription` are internal. Custom backends must use `DispatchProxy` + heavy reflection to intercept dialog requests.

**Workaround:** Create a `DispatchProxy` implementation that intercepts `OnAlertRequested`, `OnActionSheetRequested`, and `OnPromptRequested` method calls, extracts parameters via reflection, shows native dialogs, and completes the `TaskCompletionSource` result via reflection. See the macOS implementation at `Platform/AlertManagerSubscription.cs` for a reference.

---

### Gap 6: Reference Assembly Exceptions ([#34222](https://github.com/dotnet/maui/issues/34222) — Resizetizer specific)

**Problem:** MAUI's reference assemblies throw `NotImplementedInReferenceAssemblyException` when methods are called on unrecognized platforms.

**Workaround:** Ensure all service implementations are wired in via both DI and static `SetDefault()` reflection. Guard app code that might run before DI is configured.

---

## 28. Project Structure Reference

Recommended project structure for a new MAUI backend:

```
src/
├── Platform.Maui.[Platform]/           # Main handler library
│   ├── Handlers/                        # One file per control handler
│   │   ├── [Platform]ViewHandler.cs     # Base handler class
│   │   ├── ApplicationHandler.cs
│   │   ├── WindowHandler.cs
│   │   ├── ContentPageHandler.cs
│   │   ├── LayoutHandler.cs
│   │   ├── LabelHandler.cs
│   │   ├── ButtonHandler.cs
│   │   ├── EntryHandler.cs
│   │   ├── ... (all control handlers)
│   │   └── GestureManager.cs
│   ├── Hosting/                         # DI & app builder
│   │   ├── AppHostBuilderExtensions.cs  # UseMaui[Platform]<TApp>()
│   │   ├── [Platform]MauiApplication.cs # IPlatformApplication + native app delegate
│   │   ├── [Platform]MauiContext.cs     # IMauiContext implementation
│   │   └── [Platform]MauiContextExtensions.cs
│   ├── Dispatching/                     # IDispatcher implementation
│   │   ├── [Platform]Dispatcher.cs
│   │   └── [Platform]DispatcherTimer.cs
│   ├── Platform/                        # Platform-specific utilities
│   │   ├── [Platform]ContainerView.cs   # Base native container view
│   │   ├── [Platform]FontManager.cs     # IFontManager implementation
│   │   ├── [Platform]EmbeddedFontLoader.cs
│   │   ├── [Platform]Ticker.cs          # ITicker for animations
│   │   ├── AlertManagerSubscription.cs  # Dialog system workaround
│   │   └── FontImageSourceHelper.cs     # Font glyph → image rendering
│   ├── LifecycleEvents/                 # Platform lifecycle events
│   ├── Controls/                        # Custom native controls
│   ├── build/                           # MSBuild targets shipped with NuGet
│   │   └── Platform.Maui.[Platform].targets
│   └── Platform.Maui.[Platform].csproj
│
├── Platform.Maui.Essentials.[Platform]/ # Essentials implementations
│   ├── AppInfoImplementation.cs
│   ├── ClipboardImplementation.cs
│   ├── PreferencesImplementation.cs
│   ├── ... (all essentials)
│   ├── EssentialsExtensions.cs          # AddEssentials() registration
│   └── Platform.Maui.Essentials.[Platform].csproj
│
├── Platform.Maui.[Platform].BlazorWebView/  # Blazor Hybrid (optional)
│   ├── BlazorWebViewHandler.cs
│   ├── [Platform]WebViewManager.cs
│   ├── [Platform]BlazorDispatcher.cs
│   ├── [Platform]MauiAssetFileProvider.cs
│   ├── BlazorWebViewExtensions.cs
│   ├── build/
│   │   └── Platform.Maui.[Platform].BlazorWebView.targets
│   └── Platform.Maui.[Platform].BlazorWebView.csproj
│
samples/
├── ControlGallery/                      # Comprehensive demo app
├── Sample/                              # Basic sample app
└── SampleBlazor/                        # Blazor hybrid sample
```

---

## 29. Implementation Priority Order

Recommended order for bringing up a new backend:

### Phase 1: Foundation (Get a window with "Hello World")
1. Core infrastructure (base handler, dispatcher, context, host builder)
2. Application + Window handlers
3. ContentPage handler
4. LayoutHandler (VerticalStackLayout, HorizontalStackLayout)
5. Label handler
6. Basic essentials (AppInfo, DeviceInfo, FileSystem, Preferences)

### Phase 2: Basic Controls (Interactive app)
7. Button, Entry, Editor handlers
8. Image handler (FileImageSource first)
9. Switch, CheckBox, Slider, ProgressBar, ActivityIndicator
10. ScrollView handler
11. Border handler (Frame)
12. Font management (IFontManager, IEmbeddedFontLoader)
13. Gesture recognizers (Tap, Pan)

### Phase 3: Navigation (Multi-page app)
14. NavigationPage handler (push/pop)
15. TabbedPage handler
16. FlyoutPage handler
17. Alert/Dialog system (DisplayAlert, DisplayActionSheet, DisplayPromptAsync)
18. Animations (ITicker)

### Phase 4: Advanced Controls
19. CollectionView / ListView handlers
20. Picker, DatePicker, TimePicker handlers
21. SearchBar handler
22. RadioButton, Stepper handlers
23. CarouselView, IndicatorView
24. TableView, SwipeView, RefreshView
25. GraphicsView + ShapeViewHandler

### Phase 5: Rich Features
26. Shell handler
27. WebView handler
28. BlazorWebView handler
29. MenuBar (desktop platforms)
30. FormattedText (Label spans)
31. All image source types (URI, Stream, FontImage)
32. Remaining gesture recognizers (Swipe, Pinch, Pointer)
33. Remaining essentials (full suite)
34. App Theme / Dark Mode
35. Lifecycle events
36. Build targets / Resizetizer integration

---

## Summary Statistics Template

| Category | Implemented | Total | Coverage |
|----------|-------------|-------|----------|
| **Core Infrastructure** | _ of 6 | 6 | _% |
| **Application & Window** | _ of 5 | 5 | _% |
| **Pages** | _ of 5 | 5 | _% |
| **Layouts** | _ of 10 | 10 | _% |
| **Basic Controls** | _ of 14 | 14 | _% |
| **Input Controls** | _ of 4 | 4 | _% |
| **Collection Controls** | _ of 7 | 7 | _% |
| **Navigation** | _ of 5 | 5 | _% |
| **Alerts & Dialogs** | _ of 3 | 3 | _% |
| **Gesture Recognizers** | _ of 8 | 8 | _% |
| **Graphics & Shapes** | _ of 6 | 6 | _% |
| **Base View Properties** | _ of 20+ | 20+ | _% |
| **Font Services** | _ of 7 | 7 | _% |
| **Essentials** | _ of 25+ | 25+ | _% |
| **WebView** | _ of 6 | 6 | _% |
| **BlazorWebView** | _ of 7 | 7 | _% |
| **FormattedText/Spans** | _ of 9 | 9 | _% |
| **MenuBar** | _ of 5 | 5 | _% |
| **Animations** | _ of 8 | 8 | _% |
| **VSM & Triggers** | _ of 6 | 6 | _% |
| **ControlTemplate** | _ of 3 | 3 | _% |
| **Lifecycle Events** | _ of 4+ | 4+ | _% |
| **Image Source Types** | _ of 4 | 4 | _% |
| **App Theme** | _ of 4 | 4 | _% |
| **Build/Resizetizer** | _ of 5 | 5 | _% |

---

## 30. MAUI DevFlow — Agentic Development Workflow

### What is MAUI DevFlow?

[MAUI DevFlow](https://github.com/dotnet/maui-labs/) is a unified development toolkit for automating and debugging .NET MAUI apps — both native MAUI and Blazor Hybrid. It's designed for **agentic development workflows**, giving AI coding agents (and humans) full autonomy over the MAUI development loop: **build → deploy → inspect → interact → diagnose → fix → repeat** — entirely from the terminal.

MAUI DevFlow is `#if DEBUG` only and consists of:
- **Agent** — An in-app HTTP server that exposes the visual tree, screenshots, element interaction, and property manipulation
- **CLI** (`maui devflow`) — The DevFlow subcommand of the `maui` CLI for controlling the agent
- **Broker Daemon** — A lightweight background process that assigns ports and tracks running agents across multiple apps
- **Blazor CDP Bridge** — Chrome DevTools Protocol support for inspecting Blazor WebView content

### Why MAUI DevFlow Matters for Backend Development

When building a new MAUI backend, you're operating blind — you can't use Visual Studio's XAML Hot Reload, Live Visual Tree, or device previews. MAUI DevFlow fills this gap by letting your AI assistant (or you) **see what the app actually renders**, **inspect the visual tree**, **take screenshots**, and **interact with controls** — all from the terminal.

This creates a tight feedback loop:
1. Make a handler change
2. Build and run the app
3. Inspect the visual tree to verify the control rendered correctly
4. Take a screenshot to visually confirm
5. Tap/fill/scroll to test interactivity
6. Find the issue, fix it, repeat

**Without MAUI DevFlow**, you're `Console.WriteLine`-debugging your handler property mappings. **With it**, you have a complete diagnostic view of every element's bounds, properties, native type, and visual state.

---

### Co-Evolution Workflow

When building a new MAUI backend, you're simultaneously building **four things**:

| Component | Repository | What You're Building |
|-----------|-----------|---------------------|
| **1. Backend Handlers** | `Platform.Maui.[Platform]` | The actual MAUI handlers, essentials, build targets |
| **2. Backend Sample App** | `samples/ControlGallery` | A comprehensive demo app exercising all handlers |
| **3. MAUI DevFlow Agent** | `Microsoft.Maui.DevFlow.Agent` / `Microsoft.Maui.DevFlow.Agent.Gtk` | Platform-specific agent for visual tree walking, screenshots, element interaction |
| **4. MAUI DevFlow Sample** | `samples/DevFlow.Sample.[Platform]` | A sample app head that tests the agent on the new platform |

These four components evolve together — the backend handlers enable the sample app, the sample app exercises the handlers, and MAUI DevFlow lets you inspect both. Meanwhile, adding MAUI DevFlow support for your platform requires the backend to be partially working first.

#### Phase-by-Phase Co-Evolution

```
Phase 1: Foundation (Project References Everywhere)
┌─────────────────────────────────────────────────────────────┐
│  Backend Handlers ←──ProjectRef──→ Backend Sample App       │
│       ↑                                    ↑                │
│  ProjectRef                           ProjectRef            │
│       ↑                                    ↑                │
│  Microsoft.Maui.DevFlow.Agent.Core ←─ProjectRef──→ maui CLI   │
│       ↑                                                     │
│  Microsoft.Maui.DevFlow.Agent.[Platform] (new project)       │
└─────────────────────────────────────────────────────────────┘

Phase 2: Backend Stabilizes (Backend → NuGet, DevFlow still ProjectRef)
┌─────────────────────────────────────────────────────────────┐
│  Backend NuGet Package ←──PackageRef──→ Backend Sample App  │
│       ↑                                     ↑               │
│  PackageRef                            ProjectRef           │
│       ↑                                     ↑               │
│  Microsoft.Maui.DevFlow.Agent.[Platform] ←─ProjectRef─→ CLI  │
└─────────────────────────────────────────────────────────────┘

Phase 3: Everything Published (All NuGet)
┌─────────────────────────────────────────────────────────────┐
│  Backend NuGet ←── PackageRef ──→ Any App                   │
│  Microsoft.Maui.DevFlow.Agent.[Platform] ←── PackageRef ──→ Apps │
│  Microsoft.Maui.Cli ←── dotnet tool install ──→ Terminal     │
└─────────────────────────────────────────────────────────────┘
```

---

### Setting Up MAUI DevFlow for a New Backend

#### Step 1: Create the Platform Agent

Your platform needs a `Microsoft.Maui.DevFlow.Agent.[Platform]` project that teaches MAUI DevFlow how to inspect and interact with your native views.

**What to implement:**

| Component | Base Class | What to Override |
|-----------|-----------|-----------------|
| **Agent Service** | `DevFlowAgentService` | `CreateTreeWalker()`, `TryNativeTap()`, `CaptureScreenshotAsync()`, `GetNativeWindowSize()` |
| **Tree Walker** | `VisualTreeWalker` | `PopulateNativeInfo(ElementInfo, VisualElement)` — extracts native widget properties |
| **Registration** | Extension method | `AddMauiDevFlowAgent()` |

**Core abstractions to implement:**

```csharp
// 1. Platform-specific agent service (see PlatformAgentService in Microsoft.Maui.DevFlow.Agent)
public class MyPlatformAgentService : DevFlowAgentService
{
    protected override VisualTreeWalker CreateTreeWalker()
        => new MyPlatformVisualTreeWalker();

    protected override async Task<bool> TryNativeTap(VisualElement element)
    {
        // Use platform API to tap/click the native view
        // e.g., NSButton.PerformClick(), Gtk.Button.Activate(), etc.
    }

    protected override async Task<byte[]?> CaptureScreenshotAsync()
    {
        // Use platform screenshot API
        // e.g., CGWindowListCreateImage, Gtk.WidgetPaintable, etc.
    }

    protected override Size? GetNativeWindowSize()
    {
        // Return the current window size from the native window
    }
}

// 2. Platform-specific tree walker (see PlatformVisualTreeWalker in Microsoft.Maui.DevFlow.Agent)
public class MyPlatformVisualTreeWalker : VisualTreeWalker
{
    protected override void PopulateNativeInfo(ElementInfo info, VisualElement element)
    {
        var nativeView = element.Handler?.PlatformView;
        if (nativeView == null) return;

        // Extract native widget properties
        info.NativeType = nativeView.GetType().Name;
        info.NativeProperties["isEnabled"] = /* native enabled state */;
        info.NativeProperties["isFocused"] = /* native focus state */;
        // ... add any platform-specific properties useful for debugging
    }
}

// 3. Registration extension (actual method is AddMauiDevFlowAgent())
public static class DevFlowExtensions
{
    public static MauiAppBuilder AddMauiDevFlowAgent(this MauiAppBuilder builder)
    {
        builder.Services.AddSingleton<DevFlowAgentService, MyPlatformAgentService>();
        return builder;
    }
}
```

#### Step 2: Wire Up in Your Sample App

During initial development, use **project references** to iterate quickly:

```xml
<!-- In your sample app .csproj -->
<ItemGroup>
  <!-- Backend handlers (your project) -->
  <ProjectReference Include="..\..\src\Platform.Maui.[Platform]\Platform.Maui.[Platform].csproj" />

  <!-- MAUI DevFlow agent (project reference during development) -->
  <ProjectReference Include="..\..\..\maui-labs\src\DevFlow\Microsoft.Maui.DevFlow.Agent.Core\Microsoft.Maui.DevFlow.Agent.Core.csproj" />
  <ProjectReference Include="..\..\..\maui-labs\src\DevFlow\Microsoft.Maui.DevFlow.Agent\Microsoft.Maui.DevFlow.Agent.csproj" />

  <!-- Optional: Blazor support -->
  <ProjectReference Include="..\..\..\maui-labs\src\DevFlow\Microsoft.Maui.DevFlow.Blazor\Microsoft.Maui.DevFlow.Blazor.csproj" />
</ItemGroup>
```

```csharp
// MauiProgram.cs
public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder.UseMaui[Platform]App<App>();

    #if DEBUG
    builder.AddMauiDevFlowAgent();
    // builder.AddMauiBlazorDevFlowTools();  // If supporting Blazor
    #endif

    return builder.Build();
}
```

#### Step 3: Use the CLI for Inspection

```bash
# Install the CLI (or use project reference during development)
dotnet tool install -g Microsoft.Maui.Cli

# Start the broker daemon (auto-discovers agents)
maui devflow broker start

# Build and run your app (in another terminal)
dotnet build && dotnet run

# Inspect the visual tree
maui devflow tree

# Take a screenshot to see what rendered
maui devflow screenshot --output render-check.png

# Interact with elements
maui devflow tap MyButton
maui devflow fill MyEntry "Hello from the terminal"

# Check element properties
maui devflow query --type Label
maui devflow element <elementId>
```

---

### AI-Assisted MAUI DevFlow Prompts

#### Prompt 6: Adding MAUI DevFlow Support for Your Platform

```
I'm building a .NET MAUI backend for [PLATFORM_NAME] using [UI_TOOLKIT], and I want
to add MAUI DevFlow support so I can inspect and interact with my running app from
the terminal.

I need to create a `Microsoft.Maui.DevFlow.Agent.[Platform]` project that:

1. **Subclasses `DevFlowAgentService`** with:
   - `TryNativeTap(VisualElement)` — how to programmatically click/tap a native
     [UI_TOOLKIT] widget
   - `CaptureScreenshotAsync()` — how to capture the window as a PNG using
     [UI_TOOLKIT]'s rendering API
   - `GetNativeWindowSize()` — how to get the window dimensions

2. **Subclasses `VisualTreeWalker`** with:
   - `PopulateNativeInfo(ElementInfo, VisualElement)` — extract native widget
     properties (type name, enabled state, focus state, text content, etc.)

3. **Creates a registration extension** `AddMauiDevFlowAgent()`

Reference implementations:
- macOS (AppKit): Uses NSButton.PerformClick(), CGWindowListCreateImage,
  accessibilityIdentifier/Label extraction
  (in src/DevFlow/Microsoft.Maui.DevFlow.Agent/ with #if MACOS)
- GTK4: Uses Gtk.Button.Activate(), Gtk.WidgetPaintable → Gdk.Texture.SaveToPng(),
  widget name/tooltip/sensitive extraction
  (in src/DevFlow/Microsoft.Maui.DevFlow.Agent.Gtk/)

For [UI_TOOLKIT], research:
- How to programmatically click/activate a widget
- How to capture the window/screen as an image
- How to introspect widget properties (name, type, enabled, text content)
```

#### Prompt 7: Using MAUI DevFlow to Debug a Handler

```
I'm working on the [CONTROL_NAME] handler for my [PLATFORM_NAME] MAUI backend.
The control isn't rendering correctly.

Help me debug using MAUI DevFlow:

1. Run `maui devflow tree` to see the visual tree — check that the
   [CONTROL_NAME] element appears with the correct type and properties
2. Run `maui devflow screenshot` to see what it actually looks like
3. Run `maui devflow element <id>` to inspect the specific element's
   bounds, native type, and native properties
4. Compare the expected vs actual bounds, visibility, and property values
5. If the element is missing from the tree, check that the handler is
   registered and creating the native view correctly
6. If the element is in the tree but invisible, check bounds (zero width/height?)
   and visibility properties
7. If bounds are wrong, check the GetDesiredSize and PlatformArrange methods

After each fix, rebuild, re-run, and re-inspect with MAUI DevFlow to verify.
```

#### Prompt 8: Building the ControlGallery Sample with MAUI DevFlow

```
I need to build a comprehensive ControlGallery sample app for my [PLATFORM_NAME]
MAUI backend. The sample should:

1. Use Shell navigation with pages for each control category
2. Have pages for every implemented handler (Label, Button, Entry, etc.)
3. Include MAUI DevFlow agent for debugging
4. Set AutomationId on all interactive elements (for MAUI DevFlow to target)

For each control page:
- Show the control with various property combinations
- Include interactive elements (buttons to toggle properties, sliders to change values)
- Set meaningful AutomationId values (e.g., "MainLabel", "ColorToggle")

Structure:
- Pages/Controls/ — one page per control type
- Pages/Layouts/ — layout demos
- Pages/Features/ — gestures, animations, themes, etc.

Reference: https://github.com/shinyorg/mauiplatforms/tree/main/samples/ControlGallery/
```

---

### Success Criteria: Transitioning from Project References to NuGet Packages

The co-evolution workflow uses project references during development for rapid iteration. Here's when and how to transition each component to NuGet packages:

#### Backend Handlers → NuGet

**When to transition:**
- [ ] All Phase 1-3 handlers are implemented and tested (core, basic controls, navigation)
- [ ] The ControlGallery sample runs and demonstrates all implemented controls
- [ ] Build targets (.targets/.props) are tested and produce correct platform output
- [ ] The NuGet package structure is defined (package ID, dependencies, build targets inclusion)
- [ ] Version scheme is established (e.g., `0.1.0-alpha.1`)

**How to transition:**
```xml
<!-- Before (project reference) -->
<ProjectReference Include="..\..\src\Platform.Maui.[Platform]\Platform.Maui.[Platform].csproj" />

<!-- After (NuGet package) -->
<PackageReference Include="Platform.Maui.[Platform]" Version="0.1.0-alpha.1" />
```

**Checklist:**
- [ ] `.csproj` has correct `<PackageId>`, `<Version>`, `<Description>`, `<Authors>`
- [ ] `build/` and `buildTransitive/` targets are included via `<None Include="build/**" Pack="true" />`
- [ ] Package builds cleanly with `dotnet pack`
- [ ] Package installs and works in a fresh project (not just the monorepo)
- [ ] CI/CD publishes to NuGet (or GitHub Packages for pre-release)

#### MAUI DevFlow Agent → NuGet

**When to transition:**
- [ ] The platform agent correctly walks the visual tree for all implemented handlers
- [ ] Screenshots work reliably on the target platform
- [ ] Native tap/fill/scroll interactions work for interactive controls
- [ ] The agent has been tested with the broker daemon for port assignment
- [ ] The agent works when referenced as a NuGet package (not just project reference)

**How to transition:**
```xml
<!-- Before (project reference during co-development) -->
<ProjectReference Include="..\..\..\maui-labs\src\DevFlow\Microsoft.Maui.DevFlow.Agent.Core\Microsoft.Maui.DevFlow.Agent.Core.csproj" />
<ProjectReference Include="..\..\..\maui-labs\src\DevFlow\Microsoft.Maui.DevFlow.Agent\Microsoft.Maui.DevFlow.Agent.csproj" />

<!-- After (NuGet packages) -->
<PackageReference Include="Microsoft.Maui.DevFlow.Agent.Core" Version="*" />
<PackageReference Include="Microsoft.Maui.DevFlow.Agent" Version="*" />
```

**Checklist:**
- [ ] Agent project has correct NuGet metadata
- [ ] Agent targets the correct TFM (e.g., `net10.0` for plain .NET platforms, `net10.0-[platform]` for SDK platforms)
- [ ] Package includes any necessary build targets for port generation (`Microsoft.Maui.DevFlowPort.g.cs`)
- [ ] Package works in isolation (no lingering file-link dependencies to the maui-labs repo)

#### Removing Inter-Project File Link Dependencies

During co-development, you may create file links between repos for shared code. These must be eliminated before NuGet transition:

**Common file-link anti-patterns to watch for:**
```xml
<!-- ❌ Anti-pattern: File links between repos -->
<Compile Include="..\..\..\maui-labs\src\DevFlow\Shared\SomeHelper.cs" Link="Shared\SomeHelper.cs" />

<!-- ❌ Anti-pattern: Shared props/targets via relative path -->
<Import Project="..\..\..\maui-labs\src\DevFlow\Common.props" />

<!-- ❌ Anti-pattern: Content items from another repo -->
<Content Include="..\..\..\maui-labs\src\DevFlow\Assets\*" Link="Assets\%(Filename)%(Extension)" />
```

**Resolution strategies:**
1. **Move shared code to the Core package** — If both repos need it, it belongs in `Microsoft.Maui.DevFlow.Agent.Core`
2. **Duplicate and diverge** — If the code is small and platform-specific, copy it and let it evolve independently
3. **Create a shared NuGet package** — If it's truly shared infrastructure, publish it separately
4. **Use interfaces** — Define contracts in Core, implement in platform packages

**Validation:**
```bash
# Search for any remaining file links to other repos
grep -r "\.\.\/\.\.\/\.\.\/" *.csproj  # Triple parent traversal = cross-repo link
grep -r "Link=" *.csproj | grep -v "node_modules"  # Any file links
grep -r "Import Project=.*\.\.\/" *.csproj  # Cross-repo imports
```

---

### MAUI DevFlow Platform Support Checklist

Track your MAUI DevFlow integration progress:

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] **Agent Service** | | `[Platform]AgentService : DevFlowAgentService` |
| [ ] **Visual Tree Walker** | | `[Platform]VisualTreeWalker` with `PopulateNativeInfo()` |
| [ ] **Native Tap** | | `TryNativeTap()` invokes platform click/activate |
| [ ] **Native Fill** | | Text input for Entry/Editor/SearchBar |
| [ ] **Screenshots** | | `CaptureScreenshotAsync()` captures window as PNG |
| [ ] **Window Size** | | `GetNativeWindowSize()` returns current dimensions |
| [ ] **Registration Extension** | | `AddMauiDevFlowAgent()` for DI |
| [ ] **Broker Integration** | | Agent connects to broker for port assignment |
| [ ] **CLI Compatibility** | | `maui devflow tree/screenshot/tap` all work |
| [ ] **Blazor CDP** | | (Optional) Chrome DevTools Protocol for Blazor WebView |
| [ ] **Sample App Head** | | `DevFlow.Sample.[Platform]` project in maui-labs repo |

---

## References

### MAUI Architecture
- [dotnet/maui](https://github.com/dotnet/maui) — MAUI source code
- [Handler Architecture](https://github.com/dotnet/maui/tree/main/src/Core/src/Handlers) — All handler interfaces and implementations
- [Layout System](https://github.com/dotnet/maui/tree/main/src/Core/src/Layouts) — Cross-platform layout managers
- [Animation System](https://github.com/dotnet/maui/tree/main/src/Core/src/Animations) — ITicker, AnimationManager
- [Font System](https://github.com/dotnet/maui/tree/main/src/Core/src/Fonts) — IFontManager, FontRegistrar
- [Essentials](https://github.com/dotnet/maui/tree/main/src/Essentials/src) — Platform service interfaces
- [Resizetizer](https://github.com/dotnet/maui/tree/main/src/SingleProject/Resizetizer) — Image/font/icon processing
- [BlazorWebView](https://github.com/dotnet/maui/tree/main/src/BlazorWebView) — Blazor Hybrid architecture

### Existing Backends
- [macOS (AppKit)](https://github.com/shinyorg/mauiplatforms) — Full macOS backend with 48+ handlers
- [Linux (GTK4)](../../platforms/Linux.Gtk4/) — Full Linux/GTK4 backend with comprehensive handlers (in this repo)

### Development Tools
- [MAUI DevFlow](https://github.com/dotnet/maui-labs/tree/main/src/DevFlow) — Agentic development toolkit for MAUI app inspection and debugging

### Extensibility Issues
- [#34099](https://github.com/dotnet/maui/issues/34099) — Umbrella: Improve Extensibility for 3rd Party Platform Backends (see this issue's child / sub issues too!)
- [#34100](https://github.com/dotnet/maui/issues/34100) — Essentials `SetDefault()` needs public API
- [#34101](https://github.com/dotnet/maui/issues/34101) — `MainThread` fallback to `Dispatcher`
- [#34102](https://github.com/dotnet/maui/issues/34102) — Resizetizer standalone usage (**FIXED**)
- [#34103](https://github.com/dotnet/maui/issues/34103) — BlazorWebView composable registration
- [#34104](https://github.com/dotnet/maui/issues/34104) — Alert/Dialog system extensibility
- [#34222](https://github.com/dotnet/maui/issues/34222) — Resizetizer extension targets
