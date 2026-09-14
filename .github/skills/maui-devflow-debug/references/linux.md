# Linux / GTK Platform Guide

Platform-specific setup and usage for .NET MAUI apps running on Linux via Maui.Gtk (GTK4).

## Overview

Maui.Gtk apps target `net10.0` (not a platform-specific TFM like `net10.0-ios`) and use
GTK4 via GirCore bindings. MAUI DevFlow provides dedicated Linux packages that work with
this architecture.

## NuGet Packages

| Package | Purpose |
|---------|---------|
| `Microsoft.Maui.DevFlow.Agent.Gtk` | In-app agent (visual tree, screenshots, tapping, logging) |
| `Microsoft.Maui.DevFlow.Blazor.Gtk` | CDP bridge for WebKitGTK BlazorWebView |

These replace `Microsoft.Maui.DevFlow.Agent` and `Microsoft.Maui.DevFlow.Blazor` which target
standard MAUI platforms (iOS, Android, macCatalyst, Windows).

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Maui.DevFlow.Agent.Gtk" Version="*" />
  <!-- Blazor Hybrid apps also need: -->
  <PackageReference Include="Microsoft.Maui.DevFlow.Blazor.Gtk" Version="*" />
</ItemGroup>
```

## Registration

### MauiProgram.cs

```csharp
using Microsoft.Maui.DevFlow.Agent.Gtk;
using Microsoft.Maui.DevFlow.Blazor.Gtk;  // Blazor Hybrid only

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

The `LinuxAppDriver` automatically detects the display server and uses the appropriate
**driver-mediated** key simulation backend. This is an implementation detail for Linux
automation support, **not** a recommendation for an AI agent to invoke these tools directly
from the shell. Prefer `maui devflow ui fill`, `maui devflow ui tap`, and `maui devflow batch`
for normal interaction.

- **X11 sessions**: Uses `xdotool` (human-readable key names, no daemon needed)
- **Wayland sessions**: Uses `ydotool` (Linux input event keycodes, requires `ydotoold` daemon)

Detection uses `XDG_SESSION_TYPE` and `WAYLAND_DISPLAY` environment variables. If the
preferred tool isn't installed, it falls back to whichever is available.

### Installing xdotool (X11)

```bash
sudo apt install xdotool       # Debian/Ubuntu
sudo dnf install xdotool       # Fedora
```

### Installing ydotool (Wayland)

```bash
sudo apt install ydotool       # Debian/Ubuntu
sudo dnf install ydotool       # Fedora
```

**Required setup for ydotool** (to run without root):

```bash
# Add your user to the input group for /dev/uinput access
sudo usermod -aG input $USER

# Create udev rule for persistent permissions
echo 'KERNEL=="uinput", GROUP="input", MODE="0660"' | sudo tee /etc/udev/rules.d/99-uinput.rules
sudo udevadm control --reload-rules && sudo udevadm trigger

# Log out and back in, then start the daemon
ydotoold &
```

Key simulation is used by the driver for alert dismissal and keyboard input.
Only the driver backend should reach for `xdotool` or `ydotool`.

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
| Key simulation | Driver-mediated backend | `xdotool` (X11) / `ydotool` (Wayland) |
| Screen recording | Platform-specific | `ffmpeg` with `x11grab` (X11) / `pipewire` (Wayland) |
| Blazor WebView | WKWebView / WebView2 / Chrome | WebKitGTK 6.0 |

## Troubleshooting

### Agent Not Starting

1. Ensure `app.StartDevFlowAgent()` is called after the app is activated
2. Check that `Application.Current` is available when `StartDevFlowAgent()` runs
3. Verify the port isn't in use: `lsof -i :<port>` or `ss -tlnp | grep <port>`

### xdotool / ydotool Not Working

- On Wayland, `xdotool` does not work — install `ydotool` and start `ydotoold`
- On X11, `xdotool` is preferred over `ydotool` (simpler, no daemon)
- Prefer DevFlow commands over direct key injection wherever possible
- If `ydotool` fails silently, check that `ydotoold` daemon is running: `pgrep -x ydotoold`
- If `ydotool` gives permission errors, ensure your user is in the `input` group and `/dev/uinput` is group-writable
- Ensure the app window has focus for key events
- Check `XDG_SESSION_TYPE` to verify which display server is active: `echo $XDG_SESSION_TYPE`

### Screen Recording on Wayland

- Wayland screen recording uses `ffmpeg -f pipewire` instead of `x11grab`
- Requires PipeWire to be installed and running (standard on modern Wayland desktops)
- Check ffmpeg PipeWire support: `ffmpeg -devices | grep pipewire`
- If PipeWire is not available, you can force X11 with `GDK_BACKEND=x11` and use `x11grab`
- The first recording on Wayland may trigger a system permission dialog for screen sharing

### WebKitGTK CDP Issues

- WebKitGTK uses `EvaluateJavascriptAsync` for JS evaluation
- The same two-eval CDP pattern (send + poll) applies as other platforms
- Check that `chobitsu.js` is properly loaded in the WebView
