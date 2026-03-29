# Copilot Agent Instructions

## Project Overview

Platform.Maui.Linux.Gtk4 is a community .NET MAUI backend for Linux using GTK4.
The solution targets `net10.0` and uses MAUI `10.0.31`.
GTK4 bindings come from **GirCore** (`GirCore.Gtk-4.0`, `GirCore.WebKit-6.0`).

## Repository Layout

- `src/Platform.Maui.Linux.Gtk4/` — Core backend: handlers, hosting, and platform plumbing.
- `src/Platform.Maui.Linux.Gtk4.BlazorWebView/` — Blazor Hybrid support via WebKitGTK.
- `samples/Platform.Maui.Linux.Gtk4.Sample/` — Standard MAUI sample app.
- `samples/Platform.Maui.Linux.Gtk4.BlazorSample/` — Blazor Hybrid sample app.
- `docs/` — Documentation (currently empty).

## Build & Run

```bash
# Restore and build
dotnet restore
dotnet build

# Run the standard sample
dotnet run --project samples/Platform.Maui.Linux.Gtk4.Sample

# Run the Blazor Hybrid sample
dotnet run --project samples/Platform.Maui.Linux.Gtk4.BlazorSample
```

## Architecture

### Handler Pattern

Every MAUI control is mapped to a GTK4 widget via a **handler** in `src/Platform.Maui.Linux.Gtk4/Handlers/`.
Handlers follow the standard MAUI handler pattern:
- Inherit from `GtkViewHandler<TVirtualView, TPlatformView>` or implement the MAUI handler interface directly.
- Map virtual-view property changes to GTK widget updates.
- Are registered in `AppHostBuilderExtensions.AddMauiControlsHandlers()`.

When adding a new handler:
1. Create `<ControlName>Handler.cs` in `src/Platform.Maui.Linux.Gtk4/Handlers/`.
2. Register it in the `AddMauiControlsHandlers` method in `src/Platform.Maui.Linux.Gtk4/Hosting/AppHostBuilderExtensions.cs`.

### Platform Layer

Key classes in `src/Platform.Maui.Linux.Gtk4/Platform/`:
- `GtkMauiApplication` — Abstract base class for the Linux entry point. Subclasses override `CreateMauiApp()`.
- `GtkMauiContext` / `GtkMauiContextExtensions` — Scoped DI context carrying the GTK window and services.
- `GtkLayoutPanel` — Custom GTK widget that runs MAUI layout logic.
- `GtkThemeManager` — Detects system light/dark theme.
- `GtkAlertManager` — Displays native GTK alert dialogs.

### Blazor Hybrid

`src/Platform.Maui.Linux.Gtk4.BlazorWebView/` wraps a `WebKit.WebView` in a MAUI handler.
Key classes:
- `GtkBlazorWebView` — GTK widget hosting the web view.
- `GtkWebViewManager` — Bridges ASP.NET Core static file serving to WebKit.
- `BlazorWebViewHandler` — MAUI handler for `BlazorWebView`.

Blazor apps must call `GtkBlazorWebView.InitializeWebKit()` before any WebKit usage and register the handler manually.

## Coding Conventions

- **Language version:** `preview` (C# latest).
- **Nullable reference types:** Enabled globally.
- **Implicit usings:** Enabled.
- **File-scoped namespaces** are used throughout (`namespace Foo;`).
- Handler files are named `<ControlName>Handler.cs`.
- Platform helpers are prefixed with `Gtk` (e.g., `GtkLayoutPanel`, `GtkThemeManager`).

## Dependencies

System-level GTK4 and (optionally) WebKitGTK 6 libraries must be installed on the development machine. See the README for distro-specific install commands.
