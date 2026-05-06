# macOS AppKit MAUI Backend — docs

This directory contains design and reference documentation for the
`Microsoft.Maui.Platforms.MacOS` backend (and its Essentials / BlazorWebView companions).

## Layout

- [`handler-audit-status.md`](handler-audit-status.md) — implementation status / audit
  carried over from the upstream `shinyorg/mauiplatforms` checklist. Tracks which MAUI
  controls have native AppKit handlers and what's still TODO.
- [`guides/`](guides/) — feature guides (MenuBar, Toolbar, Sidebar, Theming, Lifecycle,
  Window, Blazor Hybrid, etc.).

## Quick start

See the platform [README](../README.md) for a minimal `MauiProgram.CreateMauiApp()`
example using `UseMauiAppMacOS<App>()`.

## Origin

The implementation was migrated from
[shinyorg/mauiplatforms](https://github.com/shinyorg/mauiplatforms) (commit `62d4022`)
and refactored to match the conventions of the canonical `Linux.Gtk4` backend in this
repository:

| Upstream (shinyorg) | maui-labs |
| --- | --- |
| `Platform.Maui.MacOS` | `Microsoft.Maui.Platforms.MacOS` |
| `Platform.Maui.MacOS.Essentials` | `Microsoft.Maui.Platforms.MacOS.Essentials` |
| `Platform.Maui.MacOS.BlazorWebView` | `Microsoft.Maui.Platforms.MacOS.BlazorWebView` |
| `Microsoft.Maui.Platform.MacOS[.Sub]` namespace | `Microsoft.Maui.Platforms.MacOS[.Sub]` namespace |
| `MacOS*` class prefix | `MacOS*` (preserved) |
| `UseMauiAppMacOS<TApp>()` | `UseMauiAppMacOS<TApp>()` (unchanged) |

Folder-level changes (to match the canonical Gtk4 layout):

- `Dispatching/MacOSDispatcher*.cs` collapsed into `Platform/`.
- `Hosting/MacOSMauiApplication.cs`, `Hosting/MacOSMauiContext*.cs`, and
  `Handlers/MacOSFontNamedSizeService.cs` moved into `Platform/`.
- `build/*.targets` ship from `buildTransitive/`.
