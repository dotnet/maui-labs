# Package Selection

Choose packages based on the app flavor:

| Project flavor | Packages |
| --- | --- |
| Standard MAUI | `Microsoft.Maui.DevFlow.Agent` |
| MAUI + WebView or HybridWebView | `Microsoft.Maui.DevFlow.Agent`, `Microsoft.Maui.DevFlow.WebView` |
| MAUI + Blazor WebView | `Microsoft.Maui.DevFlow.Agent`, `Microsoft.Maui.DevFlow.Blazor` |
| GTK MAUI | `Microsoft.Maui.DevFlow.Agent.Gtk` |
| GTK MAUI + Blazor WebView | `Microsoft.Maui.DevFlow.Agent.Gtk`, `Microsoft.Maui.DevFlow.Blazor.Gtk` |

MAUI indicators:

- `<UseMaui>true</UseMaui>`;
- platform TFMs such as `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, or `net10.0-windows10.0.19041.0`;
- GTK package references such as `Microsoft.Maui.Platforms.Linux.Gtk4` or `Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView`. Superseded `Platform.Maui.Linux.Gtk4*` references must be migrated, not mixed with the current backend.

Blazor WebView indicators:

- package reference to `Microsoft.AspNetCore.Components.WebView.Maui`;
- call to `AddMauiBlazorWebView()` in `MauiProgram.cs`.

For standard `WebView` or `HybridWebView` HTML/JavaScript automation, use the
generic WebView package; it does not require Blazor/Razor. Blazor registration
includes generic registration and adds its own startup/routing adapter.
GTK and WPF generic WebView adapters are not supplied by this package.

Use Central Package Management if `Directory.Packages.props` exists. Otherwise place versions directly on project package references.
