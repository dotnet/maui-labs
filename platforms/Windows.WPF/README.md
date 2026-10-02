# .NET MAUI Backend for WPF

[![NuGet](https://img.shields.io/nuget/v/Microsoft.Maui.Platforms.Windows.WPF.svg?label=Microsoft.Maui.Platforms.Windows.WPF)](https://www.nuget.org/packages/Microsoft.Maui.Platforms.Windows.WPF/)
[![NuGet](https://img.shields.io/nuget/v/Microsoft.Maui.Platforms.Windows.WPF.Essentials.svg?label=Microsoft.Maui.Platforms.Windows.WPF.Essentials)](https://www.nuget.org/packages/Microsoft.Maui.Platforms.Windows.WPF.Essentials/)

Custom .NET MAUI backend targeting WPF (Windows Presentation Foundation) — an alternative to the official WinUI backend.

This backend uses the platform-agnostic MAUI NuGet packages (`net10.0` fallback assemblies) and provides custom handler implementations that bridge MAUI's layout/rendering system to WPF controls. **All core MAUI controls, pages, layouts, gestures, and navigation patterns are implemented.**

> **Inspiration:** This project follows the patterns established by [mauiplatforms](https://github.com/Redth/mauiplatforms) (macOS/tvOS backends) and [Maui.Gtk](https://github.com/AathifMahir/Maui.Gtk).

## Window and page titles

The native title bar belongs to `Window.Title`. Page titles are used by navigation
and Shell headers; creating, renaming, or returning to a page does not change any
window's title bar. Set or bind `Window.Title` explicitly to change it. An empty
window title stays empty rather than falling back to a page title.

## Shell inspection with DevFlow

Shell creates only the selected page through its content controller. Template pages
remain cached and attached to the MAUI tree, so DevFlow can inspect their labels and
buttons without creating inactive pages.

To check this in the sample, open **Shell Navigation**, select **Launch Shell App**,
then use the sample agent's port:

```powershell
maui devflow --agent-port <port> ui tree --depth 20
maui devflow --agent-port <port> ui query --automationId ShellHomeTitle
maui devflow --agent-port <port> ui query --type Button
maui devflow --agent-port <port> ui tap --automationId ShellSettingsButton
```

The tree should include `ShellHomePage` beneath `ShellContent`, the title query
should find a MAUI `Label`, and tapping the button should navigate to Settings.

### Dynamic Shell items

Changes to `Shell.Items`, `ShellItem.Items`, and `ShellSection.Items` on the UI
thread refresh the native flyout, tabs, and selected page. Collection notifications
are coalesced on the dispatcher after Shell settles its current selection. Clearing
the hierarchy clears the displayed page; replacing flyout items with a `TabBar`
does not leave flyout entries behind. Inactive page templates remain lazy.
The tab strip contains only the current item's sections and preserves its selected
section when inactive items change.
Deferred collection refreshes use the `Items` property mapper, including
customizations registered with `AppendToMapping(nameof(Shell.Items), ...)`.

Run the collection and lifecycle regressions on Windows:

```powershell
dotnet test platforms\Windows.WPF\tests\HandlerTests\HandlerTests.csproj -p:UseMaui=false --filter FullyQualifiedName~ShellItemsHandlerTests
```

The runtime regression renders add, remove, replace, and reset transitions in an
offscreen, nonactivating WPF window. Set `SHELL_ITEMS_EVIDENCE_DIRECTORY` in the
test process environment to save PNGs of those transitions.

### Shell section selection

Selecting a native tab navigates through Shell's cancellable section-selection
pipeline, with or without `Shell.ItemTemplate`. Cancelled or deferred navigation
keeps the native selection on the current section until Shell accepts the change.
Rebuilding the strip does not initiate navigation or create inactive pages.
The strip uses controller-visible sections, excluding hidden sections and sections
without visible content. Visibility changes refresh it automatically. Plain section
changes retain the native tab elements and synchronize selection without rebuilding,
preserving keyboard focus.

The `ShellTabNavigationTests` handler regressions drive native UI Automation
selection in an offscreen WPF window and assert the route,
`Navigated` events, and rendered page. Set `SHELL_TAB_RESULTS` to a directory to
capture the templated and non-templated repro states as PNG and JSON.
Only the keyboard-focus cases activate their window; all other cases are
nonactivating. On a shared desktop, run the focus cases only with exclusive
foreground access. They assert actual keyboard focus, not just logical focus.

For section switching within one Shell item, select **Launch Section Switching
Repro**, or start the sample with `--shell-section-repro`. This uses two lazy
pages in one `TabBar`, without calling a handler refresh workaround. The
**GoToAsync** and **Set CurrentItem** buttons must both replace Page One with
Page Two (and back). **Refresh state** shows the route, current page, page
creation count, and `Navigated` count. Only one page should be created initially;
after visiting both pages, the creation count must remain two on repeat visits.

The focused Windows handler regression suite exercises real MAUI selection and
WPF content hosting, including selection cleanup:

```powershell
dotnet test platforms\Windows.WPF\tests\HandlerTests --filter FullyQualifiedName~ShellSectionSwitchingTests
```

## Screenshots

| Home | Controls | Layouts |
|------|----------|---------|
| ![Home](https://raw.githubusercontent.com/dotnet/maui-labs/main/platforms/Windows.WPF/docs/screenshots/home.png) | ![Controls](https://raw.githubusercontent.com/dotnet/maui-labs/main/platforms/Windows.WPF/docs/screenshots/controls.png) | ![Layouts](https://raw.githubusercontent.com/dotnet/maui-labs/main/platforms/Windows.WPF/docs/screenshots/layouts.png) |

| Shapes | Graphics | CollectionView |
|--------|----------|----------------|
| ![Shapes](https://raw.githubusercontent.com/dotnet/maui-labs/main/platforms/Windows.WPF/docs/screenshots/shapes.png) | ![Graphics](https://raw.githubusercontent.com/dotnet/maui-labs/main/platforms/Windows.WPF/docs/screenshots/graphics.png) | ![CollectionView](https://raw.githubusercontent.com/dotnet/maui-labs/main/platforms/Windows.WPF/docs/screenshots/collectionview.png) |

| FormattedText | Pickers | Transforms |
|---------------|---------|------------|
| ![FormattedText](https://raw.githubusercontent.com/dotnet/maui-labs/main/platforms/Windows.WPF/docs/screenshots/formattedtext.png) | ![Pickers](https://raw.githubusercontent.com/dotnet/maui-labs/main/platforms/Windows.WPF/docs/screenshots/pickers.png) | ![Transforms](https://raw.githubusercontent.com/dotnet/maui-labs/main/platforms/Windows.WPF/docs/screenshots/transforms.png) |

| FontAwesome | ControlTemplate |
|-------------|-----------------|
| ![FontAwesome](https://raw.githubusercontent.com/dotnet/maui-labs/main/platforms/Windows.WPF/docs/screenshots/fontawesome.png) | ![ControlTemplate](https://raw.githubusercontent.com/dotnet/maui-labs/main/platforms/Windows.WPF/docs/screenshots/controltemplate.png) |

## Quick Start — Setting Up a WPF MAUI App

### Option 1: Use the template (recommended)

```bash
# Install the template
dotnet new install Microsoft.Maui.Platforms.Windows.WPF.Templates --prerelease

# Create a new WPF MAUI app
dotnet new maui-wpf -n MyApp.WPF
cd MyApp.WPF
dotnet run
```

### Option 2: Add to an existing project manually

Follow the [getting-started guide](docs/getting-started.md#using-the-nuget-package-directly)
for package references and the code-only entry point. Use a `MauiWPFApplication`
host separate from your MAUI `Application`, with `UseMauiAppWPF<App>()` and
`UseWPFEssentials()`.

The packed template selects backend and Essentials packages from its own release
and explicitly references the backend's MAUI Controls version. Use full release
versions when configuring references manually: `0.1.0-preview` selects the oldest
matching preview, not the latest.

For template development, run `eng\smoke-tests\wpf-template-smoke-test.ps1` on Windows.
It packs, generates, restores, builds, and launches the template using an isolated
template hive. See [validation details](docs/getting-started.md#validating-template-changes).

## Packaged raw assets

Declare raw files as `MauiAsset` items with their package-relative names:

```xml
<MauiAsset Include="Resources\Raw\**\*" LogicalName="%(RecursiveDir)%(Filename)%(Extension)" />
```

For example, `Resources\Raw\Data\sample.txt` is copied to `Data\sample.txt`
under the app directory in both build and publish output. Open it with
`IFileSystem.OpenAppPackageFileAsync("Data/sample.txt")`; use
`AppPackageFileExistsAsync` with the same name. Resolve `IFileSystem` from the
services registered by `UseWPFEssentials()`.

An explicit `LogicalName` takes precedence over `Link`. Without either metadata,
the path defaults to `%(RecursiveDir)%(Filename)%(Extension)` (a single explicitly
included file uses its filename). Nested folders are preserved, so files with
the same basename in different logical folders remain distinct. Do not prefix
the runtime name with `Resources\Raw`.

Run `eng\smoke-tests\wpf-assets-smoke-test.ps1 -RuntimeIdentifier win-x64` on
Windows (choose the RID matching your selected `dotnet` host) to build and run the
existing `Windows.WPF.Sample` with `-p:WpfTestScenarios=true`, then publish it
without rebuilding and run it again. The opt-in `--test-scenario packaged-assets`
entrypoint uses the actual DI-registered WPF file system without opening a window
or starting DevFlow. Normal startup (no scenario argument) still opens the gallery.
Scenario code and fixtures live under the sample's `TestScenarios` directory;
additional runtime checks should use this shared host rather than new test apps.
Launch scenarios with `dotnet Windows.WPF.Sample.dll --test-scenario <name>` to
see console diagnostics; the normal Windows executable is a GUI application.
The same asset definitions and expected contents are reused by `HandlerTests`,
while the smoke verifies the real sample build/publish output through the shipping
backend MSBuild target. `-TargetsFile` can select a historical target file for
regression reproduction; it never changes the shipped target.
The same explicit RID is used for build and no-build publish so Razor's generated
manifests are read from the directory where the build wrote them.

## Samples

See the `samples/` directory for working examples:
- **ControlGallery** — Full control gallery exercising all implemented handlers (used by UI tests)
- **Maui.Controls.Sample.Blazor** — Blazor Hybrid WebView on WPF
- **Maui.Sample** — Multi-platform sample with WPF platform folder

## Implementation Status

### Controls

| Control | WPF Native Control | Status |
|---|---|---|
| Label | TextBlock | ✅ Full (Text, FormattedText, Font, Color, Alignment, Decorations, LineHeight, MaxLines, Padding, CharacterSpacing) |
| Button | Button | ✅ Full (Text, Font, TextColor, Background, CornerRadius, Padding, Clicked/Pressed/Released) |
| Entry | TextBox / PasswordBox | ✅ Full (Text, Font, Color, Placeholder, MaxLength, IsReadOnly, IsPassword, Keyboard, CursorPosition, SelectionLength) |
| Editor | TextBox (multiline) | ✅ Full (Text, Font, Color, Placeholder, MaxLength, IsReadOnly) |
| Image | Image | ✅ Full (FileImageSource, UriImageSource, StreamImageSource, FontImageSource, Aspect) |
| ImageButton | Button + Image | ✅ (Source, Aspect, Padding, BorderWidth, BorderColor) |
| CheckBox | CheckBox | ✅ (IsChecked, Foreground via SolidPaint) |
| Switch | Custom ControlTemplate | ✅ (IsToggled, OnColor, animated thumb toggle) |
| Slider | Slider | ✅ (Value, Min, Max, MinimumTrackColor, MaximumTrackColor) |
| ProgressBar | ProgressBar | ✅ (Progress 0-1 mapped to 0-100) |
| ActivityIndicator | Custom rotating arc | ✅ (IsRunning, Color) |
| Stepper | Custom ▲/▼ panel | ✅ (Value, Min, Max, Interval) |
| RadioButton | RadioButton | ✅ (IsChecked, Content, TextColor, GroupName) |
| Picker | ComboBox | ✅ (Items, SelectedIndex, Title, TextColor, TitleColor) |
| DatePicker | DatePicker | ✅ (Date, MinimumDate, MaximumDate, Format) |
| TimePicker | ComboBox (time items) | ✅ (Time, Format) |
| SearchBar | TextBox + Button | ✅ (Text, Placeholder, TextColor, MaxLength) |
| Border | Border | ✅ (Stroke, StrokeThickness, CornerRadius, Background, Padding, StrokeDashPattern) |
| BoxView | ShapeViewHandler | ✅ (Color, CornerRadius) |
| ScrollView | ScrollViewer | ✅ (Content, Orientation) |
| ContentView | ContentControl | ✅ (Content) |
| WebView | WebView2 | ✅ (URL, HTML, JavaScript, Navigation, UserAgent) |
| GraphicsView | Custom WpfCanvas | ✅ (ICanvas drawing via DrawingContext) |
| BlazorWebView | WebView2 | ✅ (via AspNetCore.Components.WebView.Wpf) |

### Collection Controls

| Control | Status | Notes |
|---|---|---|
| CollectionView | ✅ | WPF ListBox with DataTemplateSelector, SelectedItem, SelectionMode, EmptyView; observable flat and grouped sources update live |
| ListView | ✅ | WPF ListBox with MAUI template bridge |
| CarouselView | ✅ | Horizontal ListBox with arrow navigation buttons |
| IndicatorView | ✅ | Dot indicators as Ellipses |
| TableView | ✅ | Grouped sections with TextCell, SwitchCell, EntryCell |
| SwipeView | ✅ | Context menu approximation |
| RefreshView | ✅ | Progress bar indicator |

### Pages & Navigation

| Page | Status | Notes |
|---|---|---|
| ContentPage | ✅ | Full support via PageHandler |
| NavigationPage | ✅ | Push/pop stack, back button, title bar, toolbar items, animated transitions |
| TabbedPage | ✅ | WPF TabControl with auto-generated TabItems |
| FlyoutPage | ✅ | Grid with flyout panel, GridSplitter, IsPresented two-way sync |
| Shell | ✅ | Flyout panel, tab control, URI-based routing, shell item navigation |
| Modal pages | ✅ | Animated overlay push/pop via ModalNavigationManager |

### Layouts

All layouts work via the cross-platform MAUI layout engine + `LayoutPanel`:

`VerticalStackLayout` · `HorizontalStackLayout` · `Grid` · `FlexLayout` · `AbsoluteLayout` · `StackLayout` · `ScrollView` · `ContentView` · `Border` · `Frame`

### Gestures

| Gesture | Status | Notes |
|---|---|---|
| TapGestureRecognizer | ✅ | MouseLeftButtonUp via GestureManager |
| PointerGestureRecognizer | ✅ | MouseEnter/Leave/Move |
| PanGestureRecognizer | ✅ | Mouse capture + delta tracking |
| SwipeGestureRecognizer | ✅ | Threshold-based direction detection |
| PinchGestureRecognizer | ✅ | Ctrl+MouseWheel zoom |
| DragGestureRecognizer | ✅ | WPF DragDrop.DoDragDrop |
| DropGestureRecognizer | ✅ | AllowDrop + DragOver/Drop events |
| LongPressGestureRecognizer | ✅ | DispatcherTimer 500ms hold |

### Shapes

All MAUI shapes render via WPF `System.Windows.Shapes`:

`Rectangle` · `RoundRectangle` · `Ellipse` · `Line` · `Path` · `Polygon` · `Polyline` — with Fill, Stroke, StrokeDashPattern support.

### Infrastructure

| Component | Status | Notes |
|---|---|---|
| Application | ✅ | MauiWPFApplication base class |
| Window | ✅ | Title, Size, Position, Min/Max, MenuBar, Multi-window |
| Dispatcher | ✅ | WPF Dispatcher + DispatcherProvider |
| Dialogs | ✅ | DisplayAlert, DisplayActionSheet, DisplayPromptAsync (native WPF windows with app-provided button labels) |
| Font Management | ✅ | IFontManager, IFontRegistrar, embedded font loading, FontImageSource glyph rendering |
| Dark/Light Mode | ✅ | ThemeManager detects via registry + SystemEvents, fires ThemeChanged |
| Animations | ✅ | WPFTicker at ~60fps, TranslateTo/FadeTo/ScaleTo/RotateTo all work |
| Transforms | ✅ | TranslationX/Y, Scale, Rotation → TransformGroup with RenderTransformOrigin |
| Shadow | ✅ | DropShadowEffect with radius, offset, opacity |
| Clip | ✅ | UIElement.Clip via Geometry.Parse |
| MenuBar | ✅ | WPF Menu with MenuItem hierarchy, keyboard accelerators |
| AutomationId | ✅ | AutomationProperties.AutomationId + Semantic properties |
| ToolTip | ✅ | FrameworkElement.ToolTip |
| ContextFlyout | ✅ | WPF ContextMenu with MenuItem hierarchy |
| VisualStateManager | ✅ | PointerOver, Pressed, Focused, Disabled state hooks |

### Essentials

Call `builder.UseWPFEssentials()` before `builder.Build()` on the WPF UI thread.
When `Build()` returns, every registered Essentials facade (`FileSystem.Current`,
`Preferences.Default`, `DeviceInfo.Current`, and the other supported APIs) uses the
same instance as dependency injection, including application overrides registered
before or after `UseWPFEssentials()`. Static calls are supported after `Build()`,
not while configuring the builder. Unsupported desktop capabilities keep their
existing stub behavior.

Version tracking records a launch only when `VersionTracking.Track()` or a
tracking property is used, not merely when building the app.

The facades are process-wide: the most recently built app sets their instances.
Do not use them after disposing that app.

Run the behavioral registration regressions on Windows:
`dotnet test platforms\Windows.WPF\tests\Essentials.Tests\Windows.WPF.Essentials.Tests.csproj`.

| API | Status | Notes |
|---|---|---|
| AppInfo | ✅ | Assembly-based name, version, package; RequestedTheme |
| DeviceInfo | ✅ | OS version, DeviceIdiom.Desktop |
| Connectivity | ✅ | NetworkInterface.GetIsNetworkAvailable() |
| DeviceDisplay | ✅ | SystemParameters with DPI |
| FileSystem | ✅ | Environment.SpecialFolder paths |
| Preferences | ✅ | IsolatedStorage / registry-based |
| SecureStorage | ✅ | DPAPI (ProtectedData) |
| Clipboard | ✅ | WPF System.Windows.Clipboard |
| Browser | ✅ | Process.Start URL |
| Launcher | ✅ | Process.Start |
| Email | ✅ | mailto: protocol |
| Map | ✅ | Bing Maps URL |
| Screenshot | ✅ | RenderTargetBitmap capture |
| VersionTracking | ✅ | Preferences-backed |
| TextToSpeech | ✅ | System.Speech.Synthesis via PowerShell |
| Battery | ✅ | Returns Full/AC for desktop |
| Share | ⚠️ | Stub — no native share dialog on Windows desktop |
| FilePicker | ⚠️ | Stub — needs WPF OpenFileDialog implementation |
| Sensors | ⚠️ | Stubs (Accelerometer, Gyroscope, Compass, Barometer — N/A for desktop) |
| Geolocation | ⚠️ | Stub (limited desktop relevance) |
| Haptics / Vibration | ⚠️ | Stubs (N/A for desktop) |

## Project Structure

```
src/
  Microsoft.Maui.Platforms.Windows.WPF/              # WPF backend library (net10.0-windows)
  Microsoft.Maui.Platforms.Windows.WPF.Essentials/   # WPF Essentials library
samples/
  ControlGallery/                  # WPF Control Gallery (used by UI tests)
  Maui.Controls.Sample.Blazor/    # Blazor Hybrid sample
  Maui.Sample/                    # Multi-platform sample
tests/
  UITests/                         # 213 UI tests + WinUI comparison framework
```

## Prerequisites

### .NET 10 SDK

Install the latest .NET 10 SDK from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0).

### Workloads

```bash
dotnet workload install maui-windows
dotnet workload list   # verify
```

## Building & Running

```bash
# Build libraries only (for NuGet packaging)
dotnet build build.slnf -property:Configuration=Release

# Build and run the Control Gallery sample
dotnet build samples\ControlGallery\ControlGallery.csproj
dotnet run --project samples\ControlGallery\ControlGallery.csproj

# Run UI tests (213 tests)
dotnet build tests\UITests\UITests.csproj
dotnet test tests\UITests\UITests.csproj --no-build
```

> **Note:** Build projects individually or use `build.slnf`. The full solution includes samples that may have additional dependencies.

## Testing

The `ci-wpf.yml` compatibility matrix runs `AlertManagerSubscriptionTests` in the
existing `HandlerTests` project against MAUI 10.0.41, 10.0.60, 10.0.70 and 10.0.110
through the shared build workflow's targeted test mode. These are registration
contract tests, not native dialog interaction tests.

The project includes **213 UI tests** covering all implemented controls, plus a **WinUI comparison framework** that captures side-by-side screenshots of the WPF and WinUI ControlGallery apps for visual parity validation.

```bash
# Run all UI tests
dotnet test tests\UITests\UITests.csproj --no-build

# Run comparison tests only (requires WinUI ControlGallery running)
dotnet test tests\UITests\UITests.csproj --no-build --filter "DisplayName~Compare"
```

Comparison screenshots are saved to `tests/UITests/Comparisons/`.

## Key Technical Notes

- MAUI NuGet packages resolve to the `net10.0` (platform-agnostic) assembly. `ToPlatform()` returns `object` — `WPFViewHandler<TVirtualView, TPlatformView>` provides the typed bridge.
- The platform-agnostic `ViewHandler` has no-op `PlatformArrange` and returns `Size.Zero`. `WPFViewHandler` overrides these to bridge MAUI layout to WPF `Measure`/`Arrange`.
- WPF `System.Windows.Controls` and MAUI `Microsoft.Maui.Controls` share many type names — every handler file uses `using` aliases to disambiguate (e.g., `WButton = System.Windows.Controls.Button`).
- The `MauiWPFApplication` base class in `App.xaml` bootstraps the MAUI runtime within a WPF `Application`.
- Dialogs use `DispatchProxy` + reflection to intercept `AlertManager` requests (the API is internal in MAUI). Both the nested subscription interface in MAUI 10.0.41–10.0.60 and the top-level interface in 10.0.70+ are supported. An unrecognized contract fails during registration rather than leaving dialog tasks pending. See [dotnet/maui#34104](https://github.com/dotnet/maui/issues/34104).

## Known Limitations

- **Essentials stubs:** FilePicker, Share, and sensor APIs are stubs — desktop doesn't have equivalent hardware.
- **Span.CharacterSpacing:** WPF has no direct CharacterSpacing API on TextBlock/Run — mapper is registered but no-op.
- **SwipeView:** Approximated via context menu rather than swipe gesture.
- **BlazorWebView:** DeveloperTools property is stubbed.

See [BACKEND_IMPLEMENTATION_CHECKLIST.md](BACKEND_IMPLEMENTATION_CHECKLIST.md) for the complete implementation status with detailed notes.

## License

MIT
