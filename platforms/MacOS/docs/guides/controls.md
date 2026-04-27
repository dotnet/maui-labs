# Controls & Platform Notes

Platform-specific control behaviors, custom controls, and macOS-specific features.

## App Icons

The `MauiIcon` build item is automatically converted to a macOS `.icns` file at build time. No manual icon generation is needed.

```xml
<MauiIcon Include="..\MyApp\Resources\AppIcon\appicon.png" />
```

The build targets use `sips` and `iconutil` to generate all required sizes:

| Size | File |
|------|------|
| 16×16 | `icon_16x16.png` |
| 32×32 | `icon_16x16@2x.png`, `icon_32x32.png` |
| 64×64 | `icon_32x32@2x.png` |
| 128×128 | `icon_128x128.png` |
| 256×256 | `icon_128x128@2x.png`, `icon_256x256.png` |
| 512×512 | `icon_256x256@2x.png`, `icon_512x512.png` |
| 1024×1024 | `icon_512x512@2x.png` |

The `CFBundleIconFile` entry is automatically injected into `Info.plist`.

## Modal Pages

Modal pages are presented as sheet-style overlays with:

- Semi-transparent backdrop (40% black)
- `NSVisualEffectView` with `WindowBackground` material for vibrancy
- 20px inset from safe area edges
- 10pt rounded corners
- Automatic light/dark mode adaptation

```csharp
await Navigation.PushModalAsync(new MyModalPage());
await Navigation.PopModalAsync();
```

Multiple modals can be stacked — each new modal overlays the previous one.

## MapView

A custom `MapView` control wrapping `MKMapView`:

```csharp
using Microsoft.Maui.Platforms.MacOS.Controls;

var map = new MapView
{
    Latitude = 47.6062,
    Longitude = -122.3321,
    LatitudeDelta = 0.05,
    LongitudeDelta = 0.05,
    MapType = MapType.Standard,
    IsScrollEnabled = true,
    IsZoomEnabled = true,
    IsShowingUser = true,
};

// Add overlays
map.Pins.Add(new MapPin { Latitude = 47.6062, Longitude = -122.3321, Title = "Seattle" });
map.Circles.Add(new MapCircle { Center = new Location(47.6, -122.3), Radius = 500 });
map.Polylines.Add(new MapPolyline { Points = { ... } });
map.Polygons.Add(new MapPolygon { Points = { ... } });
```

### MapType Values

| Value | Description |
|-------|-------------|
| `Standard` | Street map |
| `Satellite` | Satellite imagery |
| `Hybrid` | Streets overlaid on satellite |

## Gesture Recognizers

All MAUI gesture recognizers are supported via `NSGestureRecognizer` wrappers:

| MAUI Gesture | macOS Implementation |
|-------------|---------------------|
| `TapGestureRecognizer` | `NSClickGestureRecognizer` with configurable tap count |
| `PanGestureRecognizer` | `NSPanGestureRecognizer` with velocity tracking |
| `SwipeGestureRecognizer` | Custom four-direction swipe detection |
| `PinchGestureRecognizer` | `NSMagnificationGestureRecognizer` |
| `PointerGestureRecognizer` | `NSTrackingArea` for enter/exit/move events |

## Coordinate System

macOS AppKit uses a bottom-left origin coordinate system, but the platform automatically handles the conversion:

- All `NSView` subclasses used by the platform override `IsFlipped = true` to use MAUI's top-left origin
- Layout and hit-testing work with MAUI coordinates — no manual conversion needed

## Multi-Window Support

The platform supports `Application.OpenWindow()` for creating multiple windows:

```csharp
Application.Current.OpenWindow(new Window(new SecondPage()));
```

Each window gets its own toolbar state, title, and lifecycle events.

## Fonts

- Default system font: SF Pro Display (13pt)
- Custom fonts registered via `MauiFont` are loaded with `CTFontManager`
- Font weight mapping supports Bold and Light traits

```csharp
builder.ConfigureFonts(fonts =>
{
    fonts.AddFont("MyFont-Regular.ttf", "MyFont");
    fonts.AddFont("MyFont-Bold.ttf", "MyFontBold");
});
```

## Text Input

- `Entry` maps to `NSTextField`
- `Editor` maps to `NSTextView` in an `NSScrollView`
- `SearchBar` maps to `NSSearchField`

All support standard macOS text editing behaviors (spell check, auto-correct, Services menu).

## Dispatcher & Threading

The platform provides a main-thread dispatcher using `DispatchQueue.MainQueue`.

> **⚠️ Important:** `MainThread.BeginInvokeOnMainThread()` and `MainThread.InvokeOnMainThreadAsync()` from `Microsoft.Maui.ApplicationModel` will throw `NotImplementedInReferenceAssemblyException` on macOS AppKit. Use `Dispatcher` or `IDispatcher` instead:

```csharp
// ✅ Correct — use Dispatcher (available on any BindableObject / View)
Dispatcher.Dispatch(() =>
{
    myLabel.Text = "Updated";
});

// ✅ Correct — use IDispatcher from DI
var dispatcher = serviceProvider.GetRequiredService<IDispatcher>();
dispatcher.Dispatch(() => { /* UI work */ });

// ✅ Correct — use MainThreadHelper from Essentials package
MainThreadHelper.BeginInvokeOnMainThread(() => { /* UI work */ });

// ❌ Throws NotImplementedInReferenceAssemblyException
// MainThread.BeginInvokeOnMainThread(() => { });
```

The `MainThreadHelper` class in `Microsoft.Maui.Platforms.MacOS.Essentials` provides a familiar static API as an alternative:

```csharp
using Microsoft.Maui.Platforms.MacOS.Essentials;

if (!MainThreadHelper.IsMainThread)
    MainThreadHelper.BeginInvokeOnMainThread(UpdateUI);
```

Animation tickers use `NSTimer` for smooth AppKit-thread animation timing.
