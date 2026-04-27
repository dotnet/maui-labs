# Blazor Hybrid

Host Blazor components in a native macOS WKWebView using the BlazorWebView control.

## Setup

### 1. Add the NuGet Package

```xml
<!-- MyApp.MacOS/MyApp.MacOS.csproj -->
<PackageReference Include="Microsoft.Maui.Platforms.MacOS.BlazorWebView" Version="0.2.0-beta.6" />
```

### 2. Register the Handler

```csharp
// MauiProgram.cs
using Microsoft.Maui.Platforms.MacOS.Hosting;

public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder
        .UseMauiAppMacOS<MacOSApp>()
        .AddMacOSBlazorWebView();  // Register BlazorWebView handler

    return builder.Build();
}
```

### 3. Link wwwroot Resources

In your macOS app head `.csproj`, link the Blazor static assets:

```xml
<ItemGroup>
  <BundleResource Include="..\MyApp\wwwroot\**"
                  Link="wwwroot\%(RecursiveDir)%(Filename)%(Extension)" />
</ItemGroup>
```

The `wwwroot/` folder should contain your `index.html` and any static assets (CSS, JS, images).

### 4. Use BlazorWebView in a Page

```csharp
using Microsoft.Maui.Platforms.MacOS.Controls;

var blazorView = new MacOSBlazorWebView
{
    HostPage = "wwwroot/index.html",
};

blazorView.RootComponents.Add(new RootComponent
{
    Selector = "#app",
    ComponentType = typeof(MyBlazorApp.Main),
});

Content = blazorView;
```

## How It Works

- **WKWebView**: Blazor components are rendered inside a native `WKWebView`
- **Asset loading**: The `MacOSMauiAssetFileProvider` loads files from the app bundle
- **JavaScript interop**: Full Blazor JS interop support via the WebView bridge
- **Threading**: `MacOSBlazorDispatcher` ensures UI updates run on the main AppKit thread

## Conditional Compilation

If your shared project has Blazor pages that should only be available on macOS:

```xml
<!-- In your macOS .csproj -->
<PropertyGroup>
  <DefineConstants>$(DefineConstants);MACAPP</DefineConstants>
</PropertyGroup>
```

```csharp
#if MACAPP
// Register Blazor-specific Shell routes
shell.Items.Add(new ShellContent
{
    Title = "Blazor",
    Route = "blazor",
    ContentTemplate = new DataTemplate(typeof(BlazorPage)),
});
#endif
```

## Debugging with MauiDevFlow

If you have [MauiDevFlow](https://github.com/Redth/MauiDevFlow) set up, you can inspect Blazor WebView content via CDP:

```bash
maui-devflow cdp snapshot           # View DOM as accessible text
maui-devflow cdp Runtime evaluate "document.title"  # Run JS
```

## Content Insets

The `ContentInsets` property controls how the WebView's scrollable content is positioned within its bounds. Content scrolls through the full `WKWebView` area, but the scroll indicators and initial content position are inset by the specified amounts.

This is useful when your BlazorWebView extends behind the toolbar or titlebar (e.g., with `FullSizeContentView` enabled) — you can inset the top so content isn't obscured.

### Usage

```csharp
var blazorView = new MacOSBlazorWebView
{
    HostPage = "wwwroot/index.html",
    ContentInsets = new Thickness(0, 52, 0, 0), // 52pt top inset (toolbar height)
};
```

### Dynamic Updates

Content insets can be changed at runtime and the WebView will update immediately:

```csharp
blazorView.ContentInsets = new Thickness(0, 38, 0, 0); // Adjust for compact toolbar
```

### How It Works

- Uses `_topContentInset` on WKWebView (macOS 14+) for top insets
- Uses `ObscuredContentInsets` via `NSEdgeInsets` when available (future macOS versions) for all edges
- The `Thickness` maps to `NSEdgeInsets(top, left, bottom, right)`
- Scroll indicators honor the insets — they won't appear behind obscuring UI

> **Note:** On current macOS versions, only the **top** inset is reliably supported. Left, bottom, and right insets will take effect when `ObscuredContentInsets` becomes available in a future macOS SDK.

## Titlebar Drag (FullSizeContentView)

When `FullSizeContentView` is enabled (the default), the `WKWebView` extends behind the toolbar and can intercept mouse events in the titlebar area, making the window undraggable from the content region.

The `BlazorWebViewHandler` automatically installs a transparent drag overlay that:

- Captures mouse events in the titlebar zone and initiates `Window.PerformWindowDrag()`
- Supports double-click to zoom the window
- Passes all other mouse events through to the WKWebView below

This happens automatically — no configuration needed. If you disable `FullSizeContentView`, the overlay is not installed.

## Transparent Background

To let the native window background show through the WebView, set the HTML body background to `transparent`:

```css
body {
    background-color: transparent;
}
```

The handler automatically sets `drawsBackground = false` on the underlying `WKWebView`, so transparent CSS backgrounds work out of the box.
