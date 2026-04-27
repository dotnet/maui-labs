# .NET MAUI macOS (AppKit) & tvOS Backend — Implementation Checklist

A comprehensive checklist for the Microsoft.Maui.Platforms.MacOS backend targeting macOS via AppKit/Cocoa and the Platform.Maui.TvOS backend targeting Apple TV via UIKit/tvOS.
Items marked `[x]` have a handler or implementation present. Items marked `[ ]` are not yet implemented.

> **Reference:** Xamarin.Forms previously had a macOS/AppKit backend. The control gallery and platform renderers at
> [Xamarin.Forms 5.0 macOS](https://github.com/xamarin/Xamarin.Forms/tree/5.0.0/Xamarin.Forms.ControlGallery.MacOS)
> (and the corresponding `Xamarin.Forms.Platform.MacOS` renderers) can serve as useful inspiration for how to map
> MAUI virtual view properties to native AppKit controls — even though this project uses the newer handler architecture
> rather than the legacy renderer pattern.

---

## 1. Core Infrastructure

### Platform Abstractions
- [x] **Platform View Type** — `NSView` (via `MacOSContainerView`, a flipped `NSView` subclass with layer backing)
- [x] **Platform Context** — `MacOSMauiContext : IMauiContext` with scoped services, handler factory, window/app scope
- [x] **Dispatcher** — `MacOSDispatcher : IDispatcher` using `DispatchQueue.MainQueue.DispatchAsync()` + `MacOSDispatcherTimer` wrapping `NSTimer` with configurable interval (default 16ms)
- [x] **Event System** — NSEvent/NSResponder chain used by gesture recognizers, mouse tracking areas, keyboard events
- [x] **Handler Factory Integration** — 60+ handlers registered via `AddMauiControlsHandlers()` in `AppHostBuilderExtensions`
- [x] **App Host Builder Extension** — `UseMauiAppMacOS<TApp>()` wires up handlers, dispatcher, alert manager, font services, ticker

### Rendering Pipeline
- [x] **View Renderer** — `MacOSViewHandler<TVirtualView, TPlatformView>` base class bridges MAUI layout → NSView frames
- [x] **Property Change Propagation** — Property mappers re-apply when `IView` property changes fire
- [x] **Child Synchronization** — `MacOSContainerView` + `LayoutHandler` add/remove/reorder subviews
- [x] **Style/Attribute Application** — Opacity, IsVisible, IsEnabled, Background, FlowDirection, AutomationId, transforms, Clip, Shadow all mapped in base `MacOSViewHandler`

### AppKit Interop
- [x] **NSResponder Chain** — Used by gesture recognizers, mouse events in handlers, first responder for Entry/Editor
- [x] **NSEvent Handling** — Mouse events (click, hover, drag), keyboard events via NSTextField/NSTextView
- [x] **NSGestureRecognizer Integration** — `GestureManager` with `NSClickGestureRecognizer`, `NSPanGestureRecognizer`, `NSTrackingArea` for pointer
- [x] **NSAccessibility** — VoiceOver via SemanticScreenReader, semantic properties mapped to AccessibilityLabel/Help/Role

---

## 2. Application & Window

| Control | Status | Notes |
|---------|--------|-------|
| [x] **Application** | ✅ | `MacOSMauiApplication : NSApplicationDelegate + IPlatformApplication`, lifecycle events, multi-window management, window creation/removal |
| [x] **Window** | ✅ | `WindowHandler` maps Title, Content, Width, Height, X, Y, MinWidth/MinHeight, MaxWidth/MaxHeight; content re-layouts on resize |
| [x] **Multi-window** | ✅ | Multiple windows tracked in `MacOSMauiApplication.Windows` collection; cascading (20px offset); key window promotion on close |
| [x] **App Theme / Dark Mode** | ✅ | `ApplicationHandler.MapAppTheme` switches between `NSAppearance` Aqua (Light) / DarkAqua (Dark) / system default; `AppThemeBinding` works |

### Window Titlebar Customization (macOS-specific)

Attached properties on `MacOSWindow` for native titlebar configuration:

| Property | Status | Notes |
|----------|--------|-------|
| [x] **TitlebarStyle** | ✅ | `Automatic` / `Expanded` / `Preference` / `Unified` (default) / `UnifiedCompact` → `NSWindowToolbarStyle` |
| [x] **TitlebarTransparent** | ✅ | Transparent titlebar blends with content (default: `true`) |
| [x] **TitleVisibility** | ✅ | `Visible` / `Hidden` (default) → `NSWindowTitleVisibility` |
| [x] **FullSizeContentView** | ✅ | Content extends behind titlebar for edge-to-edge appearance (default: `true`) |
| [x] **TitlebarSeparatorStyle** | ✅ | `Automatic` (default) / `None` / `Line` → `NSTitlebarSeparatorStyle` |

---

## 3. Pages

| Page | Status | Notes |
|------|--------|-------|
| [x] **ContentPage** | ✅ | Maps Content, Background, Title, MenuBarItems (via `MenuBarManager` → `NSApp.MainMenu`) |
| [x] **NavigationPage** | ✅ | Push/Pop via `RequestNavigation`; back button in toolbar; title updates on navigation |
| [x] **TabbedPage** | ✅ | Tab switching, BarBackgroundColor (layer), SelectedTabColor (bezel); BarTextColor/UnselectedTabColor not available in AppKit |
| [x] **FlyoutPage** | ✅ | Maps Flyout, Detail, IsPresented, FlyoutBehavior, FlyoutWidth via `NSSplitView` |
| [x] **Shell** | ✅ | `ShellHandler` — NSSplitView with sidebar flyout, content area, selection, flyout behavior |

---

## 4. Layouts

| Layout | Status | Notes |
|--------|--------|-------|
| [x] **VerticalStackLayout** | ✅ | Handled by `LayoutHandler` — MAUI's cross-platform layout manager computes frames |
| [x] **HorizontalStackLayout** | ✅ | Same as above |
| [x] **Grid** | ✅ | Row/column definitions, spans, spacing — all computed by MAUI layout manager |
| [x] **FlexLayout** | ✅ | Direction, Wrap, JustifyContent, AlignItems — MAUI layout manager handles positioning |
| [x] **AbsoluteLayout** | ✅ | Absolute and proportional positioning — MAUI layout manager computes bounds |
| [x] **ScrollView** | ✅ | Maps Content, Orientation, ScrollBarVisibility, ContentSize via `NSScrollView`; ScrollToAsync with animated scrolling via `NSAnimationContext`; `Scrolled` event fires via `SetScrolledPosition` on `BoundsChangedNotification` |
| [x] **ContentView** | ✅ | Simple content wrapper with Background support |
| [x] **Border** | ✅ | Full stroke/shape support — Stroke, StrokeThickness, StrokeShape, StrokeLineCap, StrokeLineJoin, StrokeDashPattern |
| [x] **Frame** | ✅ | Registered to `BorderHandler` — Frame extends ContentView with IBorderView, handled by Border's stroke/shape rendering |
| [x] **Layout (fallback)** | ✅ | Base `LayoutHandler` with Background; MAUI's layout manager handles custom layout subclasses |

---

## 5. Basic Controls

| Control | Status | Notes |
|---------|--------|-------|
| [x] **Label** | ✅ | Text, TextColor, Font (family/size/bold), HorizontalTextAlignment, LineBreakMode, MaxLines, TextDecorations, CharacterSpacing, Padding (via `MauiNSTextField`/`MauiNSTextFieldCell` with TextInsets), FormattedText/Spans (via `NSAttributedString`) |
| [x] **Button** | ✅ | Maps Text, TextColor, Font, CharacterSpacing, Background, CornerRadius, StrokeColor, StrokeThickness, Padding, ImageSource (File/URI/FontImageSource), Clicked event |
| [x] **ImageButton** | ✅ | `ImageButtonHandler` maps Source (file/URI), Clicked, Background, CornerRadius, StrokeColor, StrokeThickness via `NSButton` with ImageOnly position |
| [x] **Entry** | ✅ | Maps Text, TextColor, Font, CharacterSpacing, Placeholder, PlaceholderColor, IsPassword (NSSecureTextField swap), IsReadOnly, HorizontalTextAlignment, MaxLength, ReturnType, CursorPosition, SelectionLength, IsTextPredictionEnabled |
| [x] **Editor** | ✅ | Maps Text, TextColor, Font (family/size/bold), IsReadOnly, HorizontalTextAlignment, MaxLength, CharacterSpacing, Placeholder (accessibility); AutoSize is Controls-level |
| [x] **Switch** | ✅ | Maps IsOn via `NSSwitch`; TrackColor/ThumbColor not available in AppKit |
| [x] **CheckBox** | ✅ | Maps IsChecked, Foreground via `NSButton` with checkbox style |
| [x] **RadioButton** | ✅ | Maps IsChecked, TextColor, Content text; GroupName mutual exclusion handled by MAUI's cross-platform `RadioButtonGroup` |
| [x] **Slider** | ✅ | Maps Value, Minimum, Maximum via `NSSlider`; MinimumTrackColor/MaximumTrackColor/ThumbColor not available in AppKit |
| [x] **Stepper** | ✅ | Maps Value, Minimum, Maximum, Interval via `NSStepper` |
| [x] **ProgressBar** | ✅ | Maps Progress via `NSProgressIndicator`; ProgressColor via `CIColorMonochrome` content filter |
| [x] **ActivityIndicator** | ✅ | Maps IsRunning (StartAnimation/StopAnimation) via `NSProgressIndicator`; Color via `CIColorMonochrome` content filter |
| [x] **BoxView** | ✅ | Mapped via `ShapeViewHandler` |
| [x] **Image** | ✅ | Maps Source (file/URI/stream/font), Aspect, IsOpaque via `NSImageView`; smart fallback for file sources (tries exact extension, then .png/.svg/.pdf/.jpg/.jpeg, searches Resources folder); loading state callbacks via `UpdateIsLoading` |

---

## 6. Input & Selection Controls

| Control | Status | Notes |
|---------|--------|-------|
| [x] **Picker** | ✅ | Maps Title, SelectedIndex, Items, TextColor, TitleColor, Background via `NSPopUpButton` |
| [x] **DatePicker** | ✅ | Maps Date, MinimumDate, MaximumDate, TextColor, Format via `NSDatePicker` |
| [x] **TimePicker** | ✅ | Maps Time, TextColor, Format via `NSDatePicker` |
| [x] **SearchBar** | ✅ | Maps Text, TextColor, Placeholder, IsReadOnly, MaxLength via `NSSearchField` |

---

## 7. Collection Controls

| Control | Status | Notes |
|---------|--------|-------|
| [x] **CollectionView** | ✅ | `CollectionViewHandler` — NSScrollView-based with full virtualization (item pool recycling, 200pt overscan), ItemTemplate, SelectionMode (single/multiple), LinearItemsLayout, GridItemsLayout (vertical/horizontal), grouping with headers/footers, EmptyView/EmptyViewTemplate, Header/Footer (as flat items via DataTemplate), ScrollTo with Start/Center/End/MakeVisible positions, RemainingItemsThreshold for incremental loading, item spacing |
| [x] **ListView** | ✅ | NSScrollView-based with DataTemplate/DataTemplateSelector, ViewCell, TextCell, ImageCell, SwitchCell, EntryCell, selection (via NSClickGestureRecognizer), header/footer (view/template/object), grouping with headers, SeparatorColor/SeparatorVisibility, HasUnevenRows, RowHeight |
| [x] **CarouselView** | ✅ | `CarouselViewHandler` — horizontal paging with snap, position tracking, swipe |
| [x] **IndicatorView** | ✅ | Page indicator dots with configurable size, color, and shape |
| [x] **TableView** | ✅ | NSScrollView-based with TableRoot/TableSection, TextCell, SwitchCell, EntryCell, ViewCell |
| [x] **SwipeView** | ✅ | Swipe-to-reveal actions via horizontal pan gesture with left/right items; 80px action buttons with threshold detection and animated reveal (0.25s `NSAnimationContext`) |
| [x] **RefreshView** | ✅ | Content wrapper with NSProgressIndicator spinner overlay (no pull-to-refresh on macOS) |

---

## 8. Navigation & Routing

| Feature | Status | Notes |
|---------|--------|-------|
| [x] **NavigationPage stack** | ✅ | PushAsync, PopAsync via `RequestNavigation` command |
| [x] **Shell navigation** | ✅ | Shell.CurrentItem navigation, sidebar selection, flyout behavior, push/pop within ShellSection |
| [x] **Modal navigation** | ✅ | `MacOSModalManager` — push/pop modal pages as overlay views with 40% black backdrop, `NSVisualEffectView` container, 10pt corner radius, behind-window vibrancy, 20pt inset, modal stack management |
| [x] **Deep linking** | ⚠️ | Partial — requires app-level Info.plist URL scheme + NSApplicationDelegate.OpenUrls override; framework supports it via lifecycle events |
| [x] **Back button** | ✅ | Toolbar back button via `MacOSToolbarManager` for both NavigationPage and Shell navigation stacks; pops via Navigation.PopAsync or ShellSection.Navigation.PopAsync |
| [x] **ToolbarItems** | ✅ | `ToolbarHandler` manages `NSToolbar` items from `Page.ToolbarItems` — see §22 for full toolbar detail |

---

## 9. Alerts & Dialogs

| Dialog | Status | Notes |
|--------|--------|-------|
| [x] **DisplayAlert** | ✅ | Title, message, accept/cancel buttons via `NSAlert` + `RunModal()`; managed by `AlertManagerSubscription` with reflection-based AlertManager proxy |
| [x] **DisplayActionSheet** | ✅ | Multi-button action sheet via `NSAlert` with button mapping; supports destruction buttons (`HasDestructiveAction`), cancel button |
| [x] **DisplayPromptAsync** | ✅ | Text input dialog via `NSAlert` with `NSTextField` accessory view, placeholder, initial value support |

---

## 10. Gesture Recognizers

| Gesture | Status | Notes |
|---------|--------|-------|
| [x] **TapGestureRecognizer** | ✅ | `MacOSTapGestureRecognizer` wrapping `NSClickGestureRecognizer` with NumberOfTapsRequired, Command |
| [x] **PanGestureRecognizer** | ✅ | `MacOSPanGestureRecognizer` wrapping `NSPanGestureRecognizer` with translation tracking |
| [x] **SwipeGestureRecognizer** | ✅ | `MacOSSwipeGestureRecognizer` using `NSPanGestureRecognizer` with velocity threshold for swipe detection |
| [x] **PinchGestureRecognizer** | ✅ | `MacOSPinchGestureRecognizer` wrapping `NSMagnificationGestureRecognizer` for trackpad pinch-to-zoom |
| [x] **PointerGestureRecognizer** | ✅ | `MacOSPointerTrackingArea` using `NSTrackingArea` for mouseEntered/mouseExited/mouseMoved |
| [ ] **DragGestureRecognizer** | ❌ | Not implemented |
| [ ] **DropGestureRecognizer** | ❌ | Not implemented |
| [ ] **LongPressGestureRecognizer** | ❌ | Not implemented (no `NSPressGestureRecognizer` mapping) |

---

## 11. Graphics & Shapes

### Microsoft.Maui.Graphics
| Feature | Status | Notes |
|---------|--------|-------|
| [x] **GraphicsView** | ✅ | `MacOSGraphicsView : NSView` with `IDrawable` rendering via `DirectRenderer` + CoreGraphics |
| [x] **Canvas Operations** | ✅ | CoreGraphics (`CGContext`) provides DrawLine, DrawRect, DrawEllipse, DrawPath, DrawString, Fill operations |
| [x] **Canvas State** | ✅ | CGContext supports SaveState/RestoreState, affine transforms |
| [x] **Brushes** | ✅ | SolidColorBrush, LinearGradientBrush, RadialGradientBrush via `CAGradientLayer` in MapBackground |

### Shapes
| Shape | Status | Notes |
|-------|--------|-------|
| [x] **All Shapes** | ✅ | `ShapeViewHandler` renders via `IShapeView.Shape` + CoreGraphics paths |
| [x] **Fill & Stroke** | ✅ | Fill brush and Stroke mapped in `ShapeViewHandler` |

> **Note:** Shapes are rendered using CoreGraphics (`CGPath`) in the `ShapeNSView` custom `NSView` subclass. Individual shape types (Rectangle, Ellipse, Line, Path, Polygon, Polyline) are handled by the MAUI cross-platform shape geometry — the handler draws whatever `IShape` provides.

---

## 12. Common View Properties (Base Handler)

Every handler inherits these property mappings from `MacOSViewHandler`.

### Visibility & State
- [x] Opacity → `NSView.AlphaValue`
- [x] IsVisible → `NSView.Hidden`
- [x] IsEnabled → `NSControl.Enabled`
- [x] InputTransparent → returns null from HitTest in MacOSContainerView

### Sizing
- [x] WidthRequest / HeightRequest — respected during `GetDesiredSize` measurement
- [x] MinimumWidthRequest / MinimumHeightRequest — used as floor in measurement
- [x] MaximumWidthRequest / MaximumHeightRequest

### Layout
- [x] HorizontalOptions (Start, Center, End, Fill)
- [x] VerticalOptions (Start, Center, End, Fill)
- [x] Margin
- [x] Padding (for views implementing IPadding)
- [x] FlowDirection (LTR, RTL) → `NSView.UserInterfaceLayoutDirection`
- [x] ZIndex → `NSView` `layer.zPosition`

### Appearance
- [x] BackgroundColor — mapped in base `MacOSViewHandler.MapBackground` via `CALayer.BackgroundColor`
- [x] Background (LinearGradientBrush, RadialGradientBrush) → via `CAGradientLayer`

### Interactivity Attachments
- [x] **ToolTip** — `ToolTipProperties.Text` → `NSView.ToolTip`
- [x] **ContextFlyout** — `FlyoutBase.GetContextFlyout()` → `NSMenu` on right-click

### Transforms
- [x] TranslationX / TranslationY → `CATransform3D` via `MapTransform`
- [x] Rotation / RotationX / RotationY → `CATransform3D` via `MapTransform`
- [x] Scale / ScaleX / ScaleY → `CATransform3D` via `MapTransform`
- [x] AnchorX / AnchorY → `CALayer.AnchorPoint` via `MapTransform`

### Effects
- [x] Shadow → `CALayer` shadow properties (shadowColor, shadowOffset, shadowRadius, shadowOpacity)
- [x] Clip → `CAShapeLayer` mask via `MapClip` with `PathFToCGPath` conversion

### Automation
- [x] AutomationId → `NSView.AccessibilityIdentifier`
- [x] Semantic properties → `NSAccessibility` protocol (AccessibilityLabel, AccessibilityHelp, AccessibilityRole)

### Animations
- [x] Core Animation — NSAnimationContext used for scroll, swipe, carousel transitions; MacOSTicker drives MAUI animation system

---

## 13. VisualStateManager & Triggers

| Feature | Status | Notes |
|---------|--------|-------|
| [x] **VisualStateManager** | ✅ | Cross-platform MAUI feature — works without platform code via binding/property system |
| [x] **PropertyTrigger** | ✅ | Cross-platform MAUI feature — no platform handler needed |
| [x] **DataTrigger** | ✅ | Cross-platform MAUI feature — no platform handler needed |
| [x] **MultiTrigger** | ✅ | Cross-platform MAUI feature — no platform handler needed |
| [x] **EventTrigger** | ✅ | Cross-platform MAUI feature — no platform handler needed |
| [x] **Behaviors** | ✅ | Cross-platform MAUI feature — no platform handler needed |

---

## 14. Font Management

| Feature | Status | Notes |
|---------|--------|-------|
| [x] **IFontManager** | ✅ | `MacOSFontManager` resolves `Font` → `NSFont` with family, size, weight (Bold/Light), slant (Italic/Oblique). Registered as singleton via `Services.Replace()` |
| [x] **IFontRegistrar** | ✅ | Font registration handled by MAUI's built-in `FontRegistrar` — our `MacOSEmbeddedFontLoader` provides the platform loading |
| [x] **IEmbeddedFontLoader** | ✅ | `MacOSEmbeddedFontLoader` loads fonts from streams via `CGDataProvider` → `CGFont.CreateFromProvider()` → `CTFontManager.RegisterGraphicsFont()`, returns PostScript name for `NSFont.FromFontName()` |
| [x] **Native Font Loading** | ✅ | Registered via CoreText `CTFontManager.RegisterGraphicsFont()` |
| [x] **IFontNamedSizeService** | ✅ | `MacOSFontNamedSizeService` maps NamedSize enum to macOS point sizes. Registered via `[assembly: Dependency]` attribute. Prevents `XamlParseException` for `FontSize="Title"`. |
| [x] **Font properties** | ✅ | Font mapped on Label, Entry, Editor, Button with family/size/bold via `FontExtensions.ToNSFont()` |
| [x] **FontImageSource** | ✅ | `FontImageSourceHelper` renders font glyphs to `NSImage` via `NSAttributedString` + `LockFocus`/`UnlockFocus`; used by `ImageHandler`, `ButtonHandler`, toolbar items |

---

## 15. Essentials / Platform Services

### Fully Implemented

| Service | Interface | Status | Notes |
|---------|-----------|--------|-------|
| [x] **App Info** | `IAppInfo` | ✅ | `AppInfoImplementation` — includes `RequestedTheme` for dark mode detection |
| [x] **Battery** | `IBattery` | ✅ | `BatteryImplementation` |
| [x] **Browser** | `IBrowser` | ✅ | `BrowserImplementation` — opens URLs via `NSWorkspace` |
| [x] **Clipboard** | `IClipboard` | ✅ | `ClipboardImplementation` — `NSPasteboard` |
| [x] **Connectivity** | `IConnectivity` | ✅ | `ConnectivityImplementation` |
| [x] **Device Display** | `IDeviceDisplay` | ✅ | `DeviceDisplayImplementation` — `NSScreen` info |
| [x] **Device Info** | `IDeviceInfo` | ✅ | `DeviceInfoImplementation` |
| [x] **Email** | `IEmail` | ✅ | `EmailImplementation` — opens mailto: URIs via `NSWorkspace` with to/cc/bcc/subject/body |
| [x] **File Picker** | `IFilePicker` | ✅ | `FilePickerImplementation` — `NSOpenPanel` |
| [x] **File System** | `IFileSystem` | ✅ | `FileSystemImplementation` |
| [x] **Geolocation** | `IGeolocation` | ✅ | `GeolocationImplementation` — CLLocationManager with foreground listening |
| [x] **Haptic Feedback** | `IHapticFeedback` | ✅ | `HapticFeedbackImplementation` — `NSHapticFeedbackManager` with `LevelChange` pattern for LongPress |
| [x] **Launcher** | `ILauncher` | ✅ | `LauncherImplementation` — `NSWorkspace.OpenUrl` |
| [x] **Map** | `IMap` | ✅ | `MapImplementation` — Opens Apple Maps via URL scheme |
| [x] **Media Picker** | `IMediaPicker` | ✅ | `MediaPickerImplementation` |
| [x] **Preferences** | `IPreferences` | ✅ | `PreferencesImplementation` — `NSUserDefaults` |
| [x] **Screenshot** | `IScreenshot` | ✅ | `ScreenshotImplementation` — `CGWindowListCreateImage` P/Invoke; captures active window; PNG/JPEG export with quality control |
| [x] **Secure Storage** | `ISecureStorage` | ✅ | `SecureStorageImplementation` — Keychain |
| [x] **Semantic Screen Reader** | `ISemanticScreenReader` | ✅ | `SemanticScreenReaderImplementation` — VoiceOver via NSAccessibility |
| [x] **Share** | `IShare` | ✅ | `ShareImplementation` — `NSSharingServicePicker` |
| [x] **Text-to-Speech** | `ITextToSpeech` | ✅ | `TextToSpeechImplementation` — `NSSpeechSynthesizer` / `AVSpeechSynthesizer` |
| [x] **Version Tracking** | `IVersionTracking` | ✅ | Cross-platform `VersionTrackingImplementation` uses `IPreferences` + `IAppInfo` (both implemented) |

### Stubs (Not Available on macOS)

| Service | Interface | Status | Notes |
|---------|-----------|--------|-------|
| [x] **Vibration** | `IVibration` | ⚠️ | No-op stub (`IsSupported=false`) — macOS lacks vibration hardware |
| [x] **Flashlight** | `IFlashlight` | ⚠️ | No-op stub (`IsSupported=false`) — macOS has no flashlight |
| [x] **Phone Dialer** | `IPhoneDialer` | ⚠️ | No-op stub (`IsSupported=false`) — macOS cannot make phone calls |
| [x] **SMS** | `ISms` | ⚠️ | No-op stub (`IsComposeSupported=false`) — no native SMS on macOS |
| [x] **Sensors** | `IAccelerometer`, `IGyroscope`, `ICompass`, `IBarometer`, `IMagnetometer` | ⚠️ | All no-op stubs (`IsSupported=false`) — macOS desktop lacks motion/environmental sensors |

---

## 16. Styling Infrastructure

| Feature | Status | Notes |
|---------|--------|-------|
| [x] **Border style mapping** | ✅ | `BorderHandler` maps Stroke, StrokeShape, StrokeThickness, StrokeLineCap, StrokeLineJoin, StrokeDashPattern, StrokeDashOffset, StrokeMiterLimit via CoreGraphics |
| [x] **View state mapping** | ✅ | IsVisible → `Hidden`, IsEnabled → `Enabled`, Opacity → `AlphaValue` — all mapped in base `MacOSViewHandler` |
| [x] **Automation mapping** | ✅ | AutomationId → `AccessibilityIdentifier` mapped in base `MacOSViewHandler` |

---

## 17. WebView

| Feature | Status | Notes |
|---------|--------|-------|
| [x] **URL loading** | ✅ | Navigate to URLs via `WKWebView.LoadRequest` |
| [x] **HTML content** | ✅ | Display raw HTML via `WKWebView.LoadHtmlString` |
| [x] **JavaScript execution** | ✅ | `EvaluateJavaScriptAsync` via `WKWebView.EvaluateJavaScript` |
| [x] **Navigation commands** | ✅ | GoBack, GoForward, Reload commands mapped |
| [x] **User Agent** | ✅ | Custom user agent string support |

---

## 18. Label — FormattedText Detail

FormattedText requires special handling as a compound property using `NSAttributedString`:

| Feature | Status | Notes |
|---------|--------|-------|
| [x] **FormattedText rendering** | ✅ | `LabelHandler.MapFormattedText` builds `NSAttributedString` from `FormattedString.Spans` → `NSTextField.AttributedStringValue` |
| [x] **Span.Text** | ✅ | Text content per span |
| [x] **Span.TextColor** | ✅ | `NSAttributedString` foreground color attribute |
| [x] **Span.BackgroundColor** | ✅ | `NSAttributedString` background color attribute |
| [x] **Span.FontSize** | ✅ | `NSFont` size attribute per span |
| [x] **Span.FontFamily** | ✅ | `NSFont` family attribute per span via `NSFont.FromFontName` |
| [x] **Span.FontAttributes** | ✅ | Bold/Italic via `NSFontManager.ConvertFont` |
| [x] **Span.TextDecorations** | ✅ | `NSUnderlineStyle` / `NSStrikethroughStyle` attributes |
| [x] **Span.CharacterSpacing** | ✅ | `NSKern` attribute |

---

## 19. MenuBar (Desktop)

| Feature | Status | Notes |
|---------|--------|-------|
| [x] **MenuBarItem** | ✅ | Top-level menu items → `NSMenuItem` with submenu, added to `NSApp.MainMenu` via `MenuBarManager` |
| [x] **MenuFlyoutItem** | ✅ | Submenu items with Text, Command, CommandParameter, KeyboardAccelerators, enabled/disabled state |
| [x] **MenuFlyoutSubItem** | ✅ | Recursive nested submenus via `CreateSubMenuItem()` in `MenuBarManager` — supports arbitrary nesting depth |
| [x] **MenuFlyoutSeparator** | ✅ | `NSMenuItem.SeparatorItem` |
| [x] **Integration** | ✅ | `ContentPageHandler.MapMenuBarItems` wires `Page.MenuBarItems` to `MenuBarManager.UpdateMenuBar()` |
| [x] **Default Menu Configuration** | ✅ | `MacOSMenuBarOptions` — toggle default App/Edit/Window menus; Edit includes Undo, Redo, Cut, Copy, Paste, Delete, Select All; Window includes Minimize, Zoom, Toggle Full Screen; App menu with Quit (⌘Q) always included |

> **Note:** macOS has a global menu bar (not per-window). `MenuBar` does NOT implement `IView` — it must be managed by the `ContentPage` handler or `Window` handler via `NSApp.MainMenu`. Use `ConfigureMacOSMenuBar()` in `MauiProgram.cs` to configure defaults.

---

## 20. Animations

| Feature | Status | Notes |
|---------|--------|-------|
| [x] **MacOSTicker** | ✅ | Custom `ITicker` using `NSTimer` on main run loop; replaces default `System.Timers.Timer` which fires on threadpool (unsafe for AppKit) |
| [x] **TranslateTo** | ✅ | Works via MAUI's built-in animation system — `MacOSTicker` drives frames, property mapper applies `CATransform3D` |
| [x] **FadeTo** | ✅ | Works via MAUI's built-in animation system — `MacOSTicker` drives frames, `MapOpacity` applies `AlphaValue` |
| [x] **ScaleTo** | ✅ | Works via MAUI's built-in animation system — `MacOSTicker` drives frames, `MapTransform` applies scale |
| [x] **RotateTo** | ✅ | Works via MAUI's built-in animation system — `MacOSTicker` drives frames, `MapTransform` applies rotation |
| [x] **LayoutTo** | ✅ | Works via MAUI's built-in animation system |
| [x] **Easing functions** | ✅ | Handled by MAUI's `Animation` class — easing is applied during value interpolation, not at platform level |
| [x] **Animation class** | ✅ | `new Animation(...)` with child animations, `Commit()`, `AbortAnimation()` — all cross-platform in MAUI Controls |
| [x] **AnimationExtensions** | ✅ | Extension methods on `VisualElement` — cross-platform in MAUI Controls |

> **Note:** MAUI's animation system is fully cross-platform. It uses `IAnimationManager` + `ITicker` to drive frame updates
> that set virtual view properties (e.g., `Opacity`, `TranslationX`). The handler property mappers then apply changes to native views.
> The only platform-specific requirement is providing a main-thread-safe `ITicker` — our `MacOSTicker` uses `NSTimer` on the main run loop.

---

## 21. ControlTemplate & ContentPresenter

| Feature | Status | Notes |
|---------|--------|-------|
| [x] **ControlTemplate** | ✅ | Cross-platform MAUI feature — template inflation via ContentPresenter, no platform code needed |
| [x] **ContentPresenter** | ✅ | Cross-platform MAUI feature — dynamically instantiates template content |
| [x] **RadioButton ControlTemplate** | ✅ | Cross-platform MAUI feature — works via ControlTemplate system |
| [x] **TemplatedView** | ✅ | Cross-platform MAUI feature — base class for controls with ControlTemplate support |

---

## 22. Native Toolbar System (macOS-specific)

The toolbar system (`ToolbarHandler` + `MacOSToolbarItem`) provides comprehensive NSToolbar integration far beyond standard `ToolbarItem`.

### Standard Toolbar Items
| Feature | Status | Notes |
|---------|--------|-------|
| [x] **ToolbarItem** | ✅ | Text, icon, command mapped to `NSToolbarItem` with `NSButton` view |
| [x] **Placement** | ✅ | Content area (default) or Sidebar (Leading/Center/Trailing) via `MacOSToolbarItem.PlacementProperty` |
| [x] **IsBordered** | ✅ | Button bezel appearance toggle via `MacOSToolbarItem.IsBorderedProperty` |
| [x] **Badge** | ✅ | Text/count overlay on icon via `MacOSToolbarItem.BadgeProperty` |
| [x] **BackgroundTintColor** | ✅ | Button background tint via `MacOSToolbarItem.BackgroundTintColorProperty` |
| [x] **ToolTip** | ✅ | Custom tooltip via `MacOSToolbarItem.ToolTipProperty` (defaults to ToolbarItem.Text) |
| [x] **VisibilityPriority** | ✅ | Standard/Low/High/User for space-limited hiding |
| [x] **IsVisible** | ✅ | Toggle visibility without full toolbar rebuild |
| [x] **Back Button** | ✅ | Auto back button when in NavigationPage/Shell with stack > 1 |
| [x] **Title Label** | ✅ | Page title set from `ITitledElement` |

### Extended Toolbar Item Types

| Item Type | Status | Notes |
|-----------|--------|-------|
| [x] **MacOSSearchToolbarItem** | ✅ | Native `NSSearchToolbarItem` — expands on click; Placeholder, Text (bindable), PreferredWidth, ResignsFirstResponderWithCancel; events: TextChanged, SearchCommitted, SearchStarted, SearchEnded |
| [x] **MacOSMenuToolbarItem** | ✅ | `NSMenuToolbarItem` dropdown — nested submenus, checkmarks, icons (SF Symbols), keyboard shortcuts, ShowsIndicator, ShowsTitle |
| [x] **MacOSToolbarItemGroup** | ✅ | `NSToolbarItemGroup` — SelectOne/SelectAny/Momentary modes; individual buttons or collapsed segmented display; segments with Text/Icon/IsSelected/IsEnabled; SelectionChanged event |
| [x] **MacOSShareToolbarItem** | ✅ | `NSSharingServicePickerToolbarItem` — custom share items provider (`Func` returning shareable objects); ServiceChosen event |
| [x] **MacOSPopUpToolbarItem** | ✅ | `NSPopUpButton` dropdown selector — PullDown vs. Popup modes; Items, SelectedIndex, Width; SelectionChanged event |
| [x] **MacOSViewToolbarItem** | ✅ | Arbitrary MAUI `View` hosted in toolbar — MinWidth/MaxWidth constraints; optional button-style wrapping via `ShowsToolbarButtonStyle`; Clicked event |

### System Toolbar Items

| Item | Status | Notes |
|------|--------|-------|
| [x] **ToggleSidebar** | ✅ | Built-in sidebar toggle |
| [x] **ToggleInspector** | ✅ | Built-in inspector toggle |
| [x] **CloudSharing** | ✅ | Built-in cloud sharing |
| [x] **Print** | ✅ | Built-in print |
| [x] **ShowColors** | ✅ | Built-in color picker |
| [x] **ShowFonts** | ✅ | Built-in font picker |
| [x] **WritingTools** | ✅ | Built-in writing tools |
| [x] **InspectorTrackingSeparator** | ✅ | Tracking separator for split view alignment |

### Toolbar Layout System

| Feature | Status | Notes |
|---------|--------|-------|
| [x] **Sidebar Layout** | ✅ | `MacOSToolbar.SidebarLayout` — explicit item ordering for sidebar titlebar area |
| [x] **Content Layout** | ✅ | `MacOSToolbar.ContentLayout` — explicit item ordering for content titlebar area |
| [x] **Layout Items** | ✅ | `ToolbarItemLayoutRef`, `SearchLayoutRef`, `MenuLayoutRef`, `GroupLayoutRef`, `ShareLayoutRef`, `PopUpLayoutRef`, `SystemItemLayoutItem`, `SpacerLayoutItem` (Flexible/Fixed/Separator), `TitleLayoutItem` |

---

## 23. Native Sidebar (macOS-specific)

Both FlyoutPage and Shell support a native macOS sidebar via opt-in attached properties.

### FlyoutPage Native Sidebar

| Feature | Status | Notes |
|---------|--------|-------|
| [x] **Opt-in** | ✅ | `MacOSFlyoutPage.UseNativeSidebar` attached property enables `NativeSidebarFlyoutPageHandler` |
| [x] **NSOutlineView** | ✅ | Source list style with hierarchical items and group headers |
| [x] **NSVisualEffectView** | ✅ | Behind-window vibrancy with system sidebar material |
| [x] **NSSplitViewController** | ✅ | Resizable sidebar (150–400pt range) |
| [x] **SidebarItems** | ✅ | `IList<MacOSSidebarItem>` with Title, SystemImage (SF Symbols), Icon, Children, Tag |
| [x] **Selection** | ✅ | Programmatic item selection via `SelectSidebarItem()` with callback suppression |
| [x] **FlyoutBehavior** | ✅ | Disabled/Locked/Flyout mapped to sidebar visibility |
| [x] **Full-height Titlebar** | ✅ | Traffic lights integrated inside sidebar area |

### Shell Native Sidebar

| Feature | Status | Notes |
|---------|--------|-------|
| [x] **Opt-in** | ✅ | `MacOSShell.UseNativeSidebar` attached property |
| [x] **SystemImage** | ✅ | `MacOSShell.SystemImage` — SF Symbol names for Shell items (priority: ShellContent → ShellSection → FlyoutItem) |
| [x] **IsSidebarResizable** | ✅ | `MacOSShell.IsSidebarResizable` — controls NSSplitViewController divider dragging (default: `true`) |
| [x] **NSOutlineView** | ✅ | Hierarchical items with section headers, system icons |
| [x] **Custom Sidebar** | ✅ | Alternative MAUI-rendered sidebar with `SidebarItemView`/`SidebarGroupHeaderView` when native sidebar is not opted in |
| [x] **Navigation Stack** | ✅ | Supports Shell pushed pages within navigation stack |
| [x] **Toolbar Integration** | ✅ | Notifies WindowHandler to refresh toolbar on page change |

---

## 24. Blazor WebView (Hybrid)

Full Blazor Hybrid support via the `Microsoft.Maui.Platforms.MacOS.BlazorWebView` project.

| Feature | Status | Notes |
|---------|--------|-------|
| [x] **BlazorWebViewHandler** | ✅ | `MacOSViewHandler<MacOSBlazorWebView, WKWebView>` — creates WKWebView with custom script message handler |
| [x] **JavaScript Bridge** | ✅ | Injects `window.external.sendMessage` bridge; receives via `WKScriptMessageHandler`; sends via `__dispatchMessageCallback` |
| [x] **Asset File Provider** | ✅ | `MacOSMauiAssetFileProvider` serves static files from `NSBundle.MainBundle` via `app://` URL scheme handler |
| [x] **Blazor Dispatcher** | ✅ | `MacOSBlazorDispatcher` wraps MAUI's `IDispatcher` for Blazor compatibility with `CheckAccess()`, `InvokeAsync()` |
| [x] **Content Insets** | ✅ | Auto-calculates toolbar height on FullSizeContentView for proper content offset |
| [x] **Scroll Pocket Overlay** | ✅ | `HideScrollPocketOverlay` property to hide the overscroll area for clean toolbar appearance |
| [x] **Titlebar Drag Overlay** | ✅ | Enables window dragging even when WebView covers titlebar area |
| [x] **Registration** | ✅ | `AddMacOSBlazorWebView()` extension method registers handler in MAUI pipeline |
| [x] **Bindable Properties** | ✅ | `HostPage`, `StartPath`, `ContentInsets`, `HideScrollPocketOverlay` on `MacOSBlazorWebView` |
| [x] **Root Components** | ✅ | `BlazorRootComponent` list with selector, component type, and parameters |

---

## 25. MapView

| Feature | Status | Notes |
|---------|--------|-------|
| [x] **MapViewHandler** | ✅ | `MKMapView` integration with layer backing |
| [x] **Map Types** | ✅ | Standard, Satellite, Hybrid modes |
| [x] **Region** | ✅ | Latitude, Longitude, LatitudeDelta, LongitudeDelta → `MKCoordinateRegion` |
| [x] **Interactions** | ✅ | IsScrollEnabled, IsZoomEnabled, IsShowingUser |
| [x] **Pins** | ✅ | `ObservableCollection<MapPin>` — Latitude, Longitude, Label, Address as `MKPointAnnotation` |
| [x] **Circles** | ✅ | `ObservableCollection<MapCircle>` — center coords, radius, stroke/fill colors via `MKOverlayRenderer` |
| [x] **Polylines** | ✅ | `ObservableCollection<MapPolyline>` — position list, stroke color/width |
| [x] **Polygons** | ✅ | `ObservableCollection<MapPolygon>` — position list, stroke/fill colors |

---

## 26. Lifecycle Events

Six macOS-specific lifecycle events defined in `MacOSLifecycle` with fluent registration:

| Event | Status | Notes |
|-------|--------|-------|
| [x] **DidFinishLaunching** | ✅ | App launch complete — `NSNotification` parameter |
| [x] **DidBecomeActive** | ✅ | App gains focus |
| [x] **DidResignActive** | ✅ | App loses focus |
| [x] **DidHide** | ✅ | App hidden |
| [x] **DidUnhide** | ✅ | App unhidden |
| [x] **WillTerminate** | ✅ | App terminating |

> Register via `builder.ConfigureLifecycleEvents(events => events.AddMacOS(mac => mac.DidFinishLaunching(...)))`.

---

## 27. Image Source Types

All four MAUI image source types are supported across ImageHandler and ButtonHandler:

| Source Type | Status | Notes |
|-------------|--------|-------|
| [x] **FileImageSource** | ✅ | Smart fallback chain — tries exact extension, then .png/.svg/.pdf/.jpg/.jpeg, searches Resources folder, `NSImage.ImageNamed` |
| [x] **UriImageSource** | ✅ | Async HTTP loading via `NSData` conversion |
| [x] **StreamImageSource** | ✅ | Stream-based loading with memory stream buffering |
| [x] **FontImageSource** | ✅ | Glyph rendering via `FontImageSourceHelper` — `NSAttributedString` drawn to `NSImage` with LockFocus/UnlockFocus |

---

## 28. Not Yet Implemented

Features from standard MAUI that do not yet have macOS handlers:

| Feature | Notes |
|---------|-------|
| **HybridWebView handler** | No macOS-specific handler — sample uses MAUI's built-in support but no handler in `Microsoft.Maui.Platforms.MacOS/Handlers/` |
| **TitleView** | No `NavigationPage.TitleView` support — navigation chrome rendered via NSToolbar only |
| **DragGestureRecognizer** | No `NSDraggingSource` integration |
| **DropGestureRecognizer** | No `NSDraggingDestination` integration |
| **LongPressGestureRecognizer** | No `NSPressGestureRecognizer` mapping |
| **CollectionView drag-and-drop / reordering** | No native drag-reorder support |

---

## 29. Apple tvOS Backend (Platform.Maui.TvOS)

A separate backend targeting Apple TV via UIKit/tvOS with 24 handlers.

### tvOS Handlers

| Control | Status | Notes |
|---------|--------|-------|
| [x] **Application** | ✅ | `ApplicationHandler` with lifecycle and app theme |
| [x] **Window** | ✅ | `WindowHandler` — window management for tvOS |
| [x] **ContentPage** | ✅ | `ContentPageHandler` |
| [x] **NavigationPage** | ✅ | `NavigationPageHandler` |
| [x] **TabbedPage** | ✅ | `TabbedPageHandler` |
| [x] **Layout** | ✅ | `LayoutHandler` |
| [x] **ContentView** | ✅ | `ContentViewHandler` |
| [x] **ScrollView** | ✅ | `ScrollViewHandler` |
| [x] **Border** | ✅ | `BorderHandler` |
| [x] **Label** | ✅ | `LabelHandler` |
| [x] **Button** | ✅ | `ButtonHandler` |
| [x] **Entry** | ✅ | `EntryHandler` |
| [x] **Image** | ✅ | `ImageHandler` |
| [x] **Switch** | ✅ | `SwitchHandler` |
| [x] **Slider** | ✅ | `SliderHandler` |
| [x] **Picker** | ✅ | `PickerHandler` |
| [x] **ProgressBar** | ✅ | `ProgressBarHandler` |
| [x] **ActivityIndicator** | ✅ | `ActivityIndicatorHandler` |
| [x] **SearchBar** | ✅ | `SearchBarHandler` |
| [x] **Shape** | ✅ | `ShapeViewHandler` |
| [x] **CollectionView** | ✅ | `CollectionViewHandler` |
| [x] **CarouselView** | ✅ | `CarouselViewHandler` |
| [x] **MapView** | ✅ | `MapViewHandler` — display-only (no user interaction) |

### tvOS Not Implemented

Notable controls that do not have tvOS handlers:
- Editor, CheckBox, RadioButton, Stepper, DatePicker, TimePicker
- FlyoutPage, Shell, ListView, TableView, SwipeView, RefreshView, IndicatorView
- WebView, GraphicsView, ImageButton
- MenuBar, Toolbar (not applicable to tvOS)

### tvOS Essentials

| Service | Interface | Status |
|---------|-----------|--------|
| [x] **App Info** | `IAppInfo` | ✅ |
| [x] **Clipboard** | `IClipboard` | ✅ |
| [x] **Connectivity** | `IConnectivity` | ✅ |
| [x] **Device Display** | `IDeviceDisplay` | ✅ |
| [x] **Device Info** | `IDeviceInfo` | ✅ |
| [x] **File System** | `IFileSystem` | ✅ |
| [x] **Preferences** | `IPreferences` | ✅ |
| [x] **Secure Storage** | `ISecureStorage` | ✅ |
| [x] **Text-to-Speech** | `ITextToSpeech` | ✅ |

---

## 30. Sample Applications

| Sample | Description |
|--------|-------------|
| **ControlGallery** | Comprehensive demo app with 80+ pages covering all controls, layouts, features, gestures, animations, and collection views. Uses Shell navigation with AppShell. |
| **Sample** | Cross-platform MAUI sample demonstrating Blazor, CollectionView, Graphics, Essentials, Forms, Menus, Maps, Navigation, Toolbars, WebViews |
| **SampleMac** | Blazor-based macOS sample with Counter.razor component |
| **SampleTv** | tvOS sample project (minimal) |

---

## Summary Statistics

| Category | Implemented | Total | Notes |
|----------|-------------|-------|-------|
| **Core Infrastructure** | 6 of 6 | 6 | All core abstractions in place |
| **Pages** | 5 of 5 | 5 | ✅ All page types including Shell |
| **Layouts** | 10 of 10 | 10 | ✅ All layouts including Frame via BorderHandler |
| **Basic Controls** | 14 of 14 | 14 | ✅ All basic controls with full image source support |
| **Collection Controls** | 7 of 7 | 7 | ✅ All collection controls |
| **Input Controls** | 4 of 4 | 4 | ✅ All input controls |
| **Gesture Recognizers** | 5 of 8 | 8 | Missing: Drag, Drop, LongPress |
| **Shapes** | 1 handler | 6 types | Single ShapeViewHandler covers all shape types |
| **Essentials (Real)** | 22 of 22 | 22 | ✅ Includes Email, Screenshot, HapticFeedback |
| **Essentials (Stubs)** | 7 stubs | 7 | Vibration, Flashlight, PhoneDialer, SMS, 3 sensors |
| **Dialog Types** | 3 of 3 | 3 | ✅ All implemented via NSAlert |
| **Font Services** | 6 of 6 | 6 | ✅ Includes FontImageSource rendering |
| **Animations** | 9 of 9 | 9 | ✅ MacOSTicker + cross-platform animation system |
| **MenuBar** | 5 of 5 | 5 | ✅ Full including MenuFlyoutSubItem + default menu config |
| **FormattedText** | 9 of 9 | 9 | ✅ All span properties via NSAttributedString |
| **Base View Properties** | 20+ of 20+ | 20+ | ✅ Complete |
| **VSM & Triggers** | 6 of 6 | 6 | ✅ All cross-platform features |
| **ControlTemplate** | 4 of 4 | 4 | ✅ All cross-platform features |
| **Toolbar System** | 16+ items | 16+ | ✅ Comprehensive: 6 item types, 8 system items, layout system |
| **Native Sidebar** | 2 of 2 | 2 | ✅ FlyoutPage + Shell native sidebar with SF Symbols |
| **Blazor WebView** | 10 of 10 | 10 | ✅ Full hybrid support with JS bridge, assets, insets |
| **MapView** | 8 of 8 | 8 | ✅ Full: map types, pins, circles, polylines, polygons |
| **Window Customization** | 5 of 5 | 5 | ✅ TitlebarStyle, Transparent, TitleVisibility, FullSizeContentView, SeparatorStyle |
| **Lifecycle Events** | 6 of 6 | 6 | ✅ All macOS lifecycle events |
| **Modal Navigation** | ✅ | 1 | MacOSModalManager with backdrop, vibrancy, inset |
| **App Theme / Dark Mode** | ✅ | 1 | NSAppearance switching via ApplicationHandler |
| **tvOS Handlers** | 23 of 23 | 23 | ✅ All planned tvOS handlers |
| **tvOS Essentials** | 9 of 9 | 9 | ✅ Core essentials for tvOS |