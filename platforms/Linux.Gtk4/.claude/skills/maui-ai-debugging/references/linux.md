# Linux / GTK Platform Guide

Platform-specific setup and usage for .NET MAUI apps running on Linux via Maui.Gtk (GTK4).

## Overview

Maui.Gtk apps target `net10.0` (not a platform-specific TFM like `net10.0-ios`) and use
GTK4 via GirCore bindings. MauiDevFlow provides dedicated Linux packages that work with
this architecture.

## NuGet Packages

| Package | Purpose |
|---------|---------|
| `Redth.MauiDevFlow.Agent.Gtk` | In-app agent (visual tree, screenshots, tapping, logging) |
| `Redth.MauiDevFlow.Blazor.Gtk` | CDP bridge for WebKitGTK BlazorWebView |

These replace `Redth.MauiDevFlow.Agent` and `Redth.MauiDevFlow.Blazor` which target
standard MAUI platforms (iOS, Android, macCatalyst, Windows).

```xml
<ItemGroup>
  <PackageReference Include="Redth.MauiDevFlow.Agent.Gtk" Version="*" />
  <!-- Blazor Hybrid apps also need: -->
  <PackageReference Include="Redth.MauiDevFlow.Blazor.Gtk" Version="*" />
</ItemGroup>
```

## Registration

### MauiProgram.cs

```csharp
using MauiDevFlow.Agent.Gtk;
using MauiDevFlow.Blazor.Gtk;  // Blazor Hybrid only

var builder = MauiApp.CreateBuilder();
// ... your existing setup ...

#if DEBUG
builder.AddMauiDevFlowAgent();
builder.AddMauiBlazorDevFlowTools(); // Blazor Hybrid only
#endif
```

### Application Startup

The agent must be started after the MAUI Application is available. In your GTK app
startup (e.g., `GtkMauiApplication.OnActivate` or equivalent):

```csharp
#if DEBUG
app.StartDevFlowAgent();

// For Blazor Hybrid, wire CDP to the agent:
var blazorService = app.Handler?.MauiContext?.Services
    .GetService<GtkBlazorWebViewDebugService>();
blazorService?.WireBlazorCdpToAgent();
#endif
```

## Building and Running

Linux/GTK apps use `dotnet run` (not `dotnet build -t:Run` which is MAUI-specific):

```bash
# Build and run (in background/async shell)
dotnet run --project <path-to-gtk-project>

# Build only
dotnet build <path-to-gtk-project>
```

Build times are typically fast (~5-10s) since there's no device deployment step.

## Network Setup

**No special setup needed.** Linux apps run directly on localhost — the CLI connects
directly to `http://localhost:<port>`. No port forwarding (unlike Android) or entitlements
(unlike Mac Catalyst) required.

## Key Simulation

The `LinuxAppDriver` uses `xdotool` for key simulation. Install it if needed:

```bash
sudo apt install xdotool
```

For Wayland-only environments, `ydotool` may be needed instead. Key simulation is used
by the CLI for alert dismissal and keyboard input.

## Platform Differences

| Feature | Standard MAUI | Linux/GTK |
|---------|--------------|-----------|
| NuGet packages | `Agent`, `Blazor` | `Agent.Gtk`, `Blazor.Gtk` |
| TFM | `net10.0-<platform>` | `net10.0` |
| Build command | `dotnet build -f $TFM -t:Run` | `dotnet run --project <path>` |
| Agent startup | Automatic (lifecycle hook) | Manual (`app.StartDevFlowAgent()`) |
| Network | Varies by platform | Direct localhost |
| Screenshots | `VisualDiagnostics` | GTK `WidgetPaintable` → `Texture.SaveToPng()` |
| Native tap | Platform gesture system | `Gtk.Widget.Activate()` |
| Key simulation | Platform-specific | `xdotool` |
| Blazor WebView | WKWebView / WebView2 / Chrome | WebKitGTK 6.0 |

## Troubleshooting

### Agent Not Starting

1. Ensure `app.StartDevFlowAgent()` is called after the app is activated
2. Check that `Application.Current` is available when `StartDevFlowAgent()` runs
3. Verify the port isn't in use: `lsof -i :<port>` or `ss -tlnp | grep <port>`

### xdotool Not Working

- On Wayland, `xdotool` may not work. Try `ydotool` instead
- Ensure the app window has focus for key events

### WebKitGTK CDP Issues

- WebKitGTK uses `EvaluateJavascriptAsync` for JS evaluation
- The same two-eval CDP pattern (send + poll) applies as other platforms
- Check that `chobitsu.js` is properly loaded in the WebView
