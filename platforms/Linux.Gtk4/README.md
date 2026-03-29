# Platform.Maui.Linux.Gtk4

A community-driven .NET MAUI backend for Linux, powered by **GTK4**. Run your .NET MAUI applications natively on Linux desktops with GTK4 rendering via [GirCore](https://github.com/gircore/gir.core) bindings.

> **Status:** Early / experimental — contributions and feedback are welcome!

https://github.com/user-attachments/assets/70f2a910-94b3-437c-945a-6b71223c5cd3

## Screenshots

<table>
<tr>
<td><img src="docs/screenshots/home.png" alt="Home" width="400"/><br/><b>Home & Sidebar Navigation</b></td>
<td><img src="docs/screenshots/controls.png" alt="Controls" width="400"/><br/><b>Interactive Controls</b></td>
</tr>
<tr>
<td><img src="docs/screenshots/collectionview.png" alt="CollectionView" width="400"/><br/><b>CollectionView (Virtualized)</b></td>
<td><img src="docs/screenshots/fontawesome.png" alt="FontAwesome Icons" width="400"/><br/><b>FontAwesome Icons</b></td>
</tr>
<tr>
<td><img src="docs/screenshots/shapes.png" alt="Shapes" width="400"/><br/><b>Shapes & Graphics</b></td>
<td><img src="docs/screenshots/layouts.png" alt="Layouts" width="400"/><br/><b>Layouts</b></td>
</tr>
<tr>
<td><img src="docs/screenshots/controltemplate.png" alt="ControlTemplate" width="400"/><br/><b>ControlTemplate & ContentPresenter</b></td>
<td><img src="docs/screenshots/pickers.png" alt="Pickers" width="400"/><br/><b>Pickers & Search</b></td>
</tr>
<tr>
<td><img src="docs/screenshots/formattedtext.png" alt="FormattedText" width="400"/><br/><b>FormattedText & Spans</b></td>
<td><img src="docs/screenshots/transforms.png" alt="Transforms" width="400"/><br/><b>Transforms & Effects</b></td>
</tr>
<tr>
<td colspan="2"><img src="docs/screenshots/graphics.png" alt="Graphics" width="400"/><br/><b>GraphicsView (Cairo)</b></td>
</tr>
</table>

## Features

### Controls (43 handlers)

| Category | Controls |
|----------|----------|
| **Basic Controls** | Label, Button, Entry, Editor, CheckBox, Switch, Slider, Stepper, ProgressBar, ActivityIndicator, Image, ImageButton, BoxView, RadioButton |
| **Input & Selection** | Picker, DatePicker, TimePicker, SearchBar |
| **Collections** | CollectionView (virtualized `Gtk.ListView`), ListView, TableView, CarouselView, SwipeView, RefreshView, IndicatorView |
| **Layouts** | StackLayout, Grid, FlexLayout, AbsoluteLayout, ScrollView, ContentView, Border, Frame |
| **Pages & Navigation** | ContentPage, NavigationPage, TabbedPage, FlyoutPage, Shell (flyout, tabs, route navigation) |
| **Shapes** | Rectangle, Ellipse, Line, Path, Polygon, Polyline — Cairo-rendered with fill, stroke, dash patterns |
| **Other** | GraphicsView (Cairo), WebView (WebKitGTK), MenuBar (`Gtk.PopoverMenuBar`) |

### Platform Features

- **Native GTK4 rendering** — Every control maps to a real GTK4 widget, styled via GTK CSS.
- **Blazor Hybrid** — Host Blazor components inside a native GTK window via WebKitGTK.
- **Gestures** — Tap, Pan, Swipe, Pinch, and Pointer gesture recognizers via GTK4 event controllers.
- **Animations** — `TranslateTo`, `FadeTo`, `ScaleTo`, `RotateTo` via `GtkPlatformTicker` + `Gsk.Transform` at ~60fps.
- **Transforms** — TranslationX/Y, Rotation, Scale via `Gsk.Transform`; Shadow (CSS `box-shadow`), Clip, ZIndex.
- **VisualStateManager** — Normal, PointerOver, Pressed, Disabled, Focused states via GTK4 event controllers.
- **ControlTemplate** — Full ContentPresenter and TemplatedView support via `IContentView` handler mapping.
- **Font icons** — Embedded font registration with fontconfig/Pango, FontImageSource rendering via Cairo. FontAwesome and custom icon fonts work out of the box.
- **FormattedText** — Rich text via Pango markup: Span colors, fonts, sizes, bold/italic, underline/strikethrough, character spacing.
- **Alerts & Dialogs** — `DisplayAlert`, `DisplayActionSheet`, `DisplayPromptAsync` via native GTK4 modal windows.
- **Modal Pages** — `PushModalAsync` presents pages as native GTK4 dialog windows by default, with attached properties for custom sizing, content-fit sizing, and inline (legacy) presentation via `GtkPage`.
- **Brushes & Gradients** — SolidColorBrush, LinearGradientBrush, RadialGradientBrush via CSS gradients.
- **Tooltips & Context Menus** — `ToolTipProperties.Text` and `ContextFlyout` via `Gtk.PopoverMenu`.
- **Theming** — Automatic light/dark theme detection via `GtkThemeManager`.
- **Lifecycle Events** — `ConfigureLifecycleEvents().AddGtk()` hooks for `OnWindowCreated` and `OnMauiApplicationCreated`.
- **Desktop integration** — App icons via hicolor icon theme, `.desktop` file generation, `MauiImage`/`MauiFont`/`MauiAsset` resource processing.

### Essentials (20 of 26 services)

| Status | Services |
|--------|----------|
| ✅ **Done** | AppInfo, AppActions, Battery (UPower), Browser (`xdg-open`), Clipboard (`Gdk.Clipboard`), Connectivity (NetworkManager), DeviceDisplay, DeviceInfo, FilePicker (`Gtk.FileDialog`), FileSystem (XDG dirs), Launcher, Map, MediaPicker, Preferences (JSON), SecureStorage, Screenshot, SemanticScreenReader (AT-SPI), Share (XDG Portal), TextToSpeech (`espeak-ng`), VersionTracking |
| ⚠️ **Partial** | Geolocation (GeoClue — basic location only), WebAuthenticator (basic OAuth via system browser) |
| ❌ **Stub** | Vibration, PhoneDialer, SMS, Contacts — not applicable to desktop Linux |

### Implementation Parity

| Category | Coverage | Notes |
|----------|----------|-------|
| Core Infrastructure | 100% | Dispatcher, handler factory, rendering pipeline |
| Pages | 100% | ContentPage, NavigationPage, TabbedPage, FlyoutPage, Shell |
| Layouts | 100% | All layout types including FlexLayout and AbsoluteLayout |
| Basic Controls | 100% | All 14 standard controls |
| Input Controls | 100% | Picker, DatePicker, TimePicker, SearchBar |
| Collection Controls | 100% | Virtualized CollectionView, ListView, TableView, CarouselView, SwipeView |
| Navigation & Routing | 100% | Push/pop, Shell routes, query parameters |
| Alerts & Dialogs | 100% | All three dialog types + native modal dialog windows |
| Gesture Recognizers | 100% | All 5 gesture types |
| Graphics & Shapes | 100% | GraphicsView + all 6 shape types |
| Font Management | 100% | Registrar, manager, FontImageSource, named sizes |
| WebView | 100% | URL, HTML, JavaScript, navigation events |
| Animations | 100% | All animation types via GtkPlatformTicker |
| VisualStateManager | 100% | All visual states + triggers + behaviors |
| ControlTemplate | 100% | ContentPresenter, TemplatedView |
| Base View Properties | 100% | Opacity, visibility, transforms, shadow, clip, automation |
| FormattedText | 100% | All Span properties via Pango markup |
| MenuBar | 100% | MenuBarItem, MenuFlyoutItem, popover menus |
| Essentials | 81% | 20 done + 2 partial, 4 stubs (desktop N/A) |

## Prerequisites

| Requirement | Version |
|---|---|
| .NET SDK | 10.0+ |
| GTK 4 libraries | 4.x (system package) |
| WebKitGTK *(Blazor only)* | 6.x (system package) |

### Install GTK4 & WebKitGTK (Debian / Ubuntu)

```bash
sudo apt install libgtk-4-dev libwebkitgtk-6.0-dev
```

### Install GTK4 & WebKitGTK (Fedora)

```bash
sudo dnf install gtk4-devel webkitgtk6.0-devel
```

## Quick Start

### Option 1: Use the template (recommended)

```bash
# Install the template
dotnet new install Platform.Maui.Linux.Gtk4.Templates

# Create a new Linux MAUI app
dotnet new maui-linux-gtk4 -n MyApp.Linux
cd MyApp.Linux
dotnet run
```

### Option 2: Add to an existing project manually

Add the NuGet package:

```bash
dotnet add package Platform.Maui.Linux.Gtk4 --prerelease
dotnet add package Platform.Maui.Linux.Gtk4.Essentials --prerelease   # optional
```

Then set up your entry point:

**Program.cs**

```csharp
using Platform.Maui.Linux.Gtk4.Platform;
using Microsoft.Maui.Hosting;

public class Program : GtkMauiApplication
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public static void Main(string[] args)
    {
        var app = new Program();
        app.Run(args);
    }
}
```

**MauiProgram.cs**

```csharp
using Platform.Maui.Linux.Gtk4.Hosting;
using Microsoft.Maui.Hosting;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp
            .CreateBuilder()
            .UseMauiAppLinuxGtk4<App>();

        return builder.Build();
    }
}
```

## XAML Support

`Platform.Maui.Linux.Gtk4` relies on MAUI's normal transitive build assets for XAML.
In Linux head projects, `*.xaml` files are still collected as `MauiXaml` and compiled by MAUI's XAML build pipeline without extra package-specific overrides.

## Resource Item Support

Linux head projects can use MAUI resource item groups for common `Resources/*` paths:

- `MauiImage` from `Resources/Images/**`
- `MauiFont` from `Resources/Fonts/**`
- `MauiAsset` from `Resources/Raw/**` (with `LogicalName` defaulting to `%(RecursiveDir)%(Filename)%(Extension)`)
- `MauiIcon` from explicit items, or default `Resources/AppIcon/appicon.{svg|png|ico}`

These are copied into build/publish output so image/file lookups can resolve at runtime.
When `MauiIcon` is present, Linux builds emit `hicolor` icon-theme files in output and runtime sets the GTK window default icon name from that icon.

## Adding Linux to a Multi-Targeted MAUI App

Since there is no official `-linux` TFM (Target Framework Moniker) from Microsoft, MAUI projects can't conditionally include the Linux backend via `TargetFrameworks` the way they do for Android/iOS/Windows. Instead, use the **"Linux head project"** pattern:

```
MyApp/                              ← Your existing multi-targeted MAUI project
├── MyApp.csproj                       (net10.0-android;net10.0-ios;...)
├── App.cs
├── MainPage.xaml
├── ViewModels/
├── Services/
└── Platforms/
    ├── Android/
    ├── iOS/
    └── ...

MyApp.Linux/                        ← New Linux-specific project
├── MyApp.Linux.csproj                 (net10.0, references Platform.Maui.Linux.Gtk4)
├── Program.cs                         (GtkMauiApplication entry point)
└── MauiProgram.cs                     (builder.UseMauiAppLinuxGtk4<App>())
```

### Setup

1. **Create the Linux head project** next to your MAUI project:

```bash
dotnet new maui-linux-gtk4 -n MyApp.Linux
```

2. **Reference your shared code** — add a project reference from `MyApp.Linux.csproj` to your MAUI project (or a shared class library):

```xml
<!-- MyApp.Linux.csproj -->
<ItemGroup>
  <ProjectReference Include="../MyApp/MyApp.csproj" />
</ItemGroup>
```

3. **Run on Linux:**

```bash
dotnet run --project MyApp.Linux
```

### Why a separate project?

The platform-specific TFMs (`net10.0-android`, `net10.0-ios`, etc.) are powered by .NET workloads that Microsoft ships. Creating a custom `net10.0-linux` TFM would require building and distributing a full .NET workload — complex infrastructure that's unnecessary for most use cases.

The separate project approach is the same pattern used by [OpenMaui](https://github.com/open-maui/maui-linux-gtk4) and [MauiAvalonia](https://github.com/wieslawsoltes/MauiAvalonia). It works with standard `dotnet build`/`dotnet run`, is NuGet-distributable, and keeps your existing MAUI project unchanged.

## Building from Source

```bash
git clone https://github.com/Redth/Platform.Maui.Linux.Gtk4.git
cd Platform.Maui.Linux.Gtk4
dotnet restore
dotnet build
```

### Run the sample app

```bash
# Sample app (includes native controls, Blazor Hybrid, essentials, and more)
dotnet run --project samples/Platform.Maui.Linux.Gtk4.Sample
```

## Project Structure

```
Platform.Maui.Linux.Gtk4.slnx                          # Solution file
├── src/
│   ├── Platform.Maui.Linux.Gtk4/                       # Core MAUI backend
│   │   ├── Handlers/                     # GTK4 handler implementations
│   │   ├── Hosting/                      # AppHostBuilderExtensions (UseMauiAppLinuxGtk4)
│   │   └── Platform/                     # GTK application, context, layout, theming
│   ├── Platform.Maui.Linux.Gtk4.Essentials/            # MAUI Essentials for Linux (clipboard, etc.)
│   └── Platform.Maui.Linux.Gtk4.BlazorWebView/         # BlazorWebView support via WebKitGTK
├── samples/
│   └── Platform.Maui.Linux.Gtk4.Sample/                # Sample app (controls, Blazor, essentials)
├── templates/                            # dotnet new templates
└── docs/                                 # Documentation
```

## NuGet Packages

| Package | Purpose |
|---|---|
| `Platform.Maui.Linux.Gtk4` | Core GTK4 backend — handlers, hosting, platform services |
| `Platform.Maui.Linux.Gtk4.Essentials` | MAUI Essentials (clipboard, preferences, device info, etc.) |
| `Platform.Maui.Linux.Gtk4.BlazorWebView` | Blazor Hybrid support via WebKitGTK |
| `Platform.Maui.Linux.Gtk4.Templates` | `dotnet new` project templates |

## Key Dependencies

| Package | Purpose |
|---|---|
| `GirCore.Gtk-4.0` | GObject introspection bindings for GTK4 |
| `GirCore.WebKit-6.0` | WebKitGTK bindings (Blazor support) |
| `Microsoft.Maui.Controls` | .NET MAUI framework |
| `Tmds.DBus.Protocol` | D-Bus client for Linux platform services |

## License

This project is licensed under the [MIT License](LICENSE).
