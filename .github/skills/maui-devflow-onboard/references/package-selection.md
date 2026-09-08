# Package Selection

Choose packages based on the app flavor:

| Project flavor | Packages |
| --- | --- |
| Standard MAUI | `Microsoft.Maui.DevFlow.Agent` |
| MAUI + Blazor WebView | `Microsoft.Maui.DevFlow.Agent`, `Microsoft.Maui.DevFlow.Blazor` |
| GTK MAUI | `Microsoft.Maui.DevFlow.Agent.Gtk` |
| GTK MAUI + Blazor WebView | `Microsoft.Maui.DevFlow.Agent.Gtk`, `Microsoft.Maui.DevFlow.Blazor.Gtk` |

MAUI indicators:

- `<UseMaui>true</UseMaui>`;
- platform TFMs such as `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, or `net10.0-windows10.0.19041.0`;
- GTK package references such as `Maui.Gtk`, `Platform.Maui.Linux.Gtk4`, or `GirCore.Gtk-4.0`.

Blazor WebView indicators:

- package reference to `Microsoft.AspNetCore.Components.WebView.Maui`;
- call to `AddMauiBlazorWebView()` in `MauiProgram.cs`.

Use Central Package Management if `Directory.Packages.props` exists. Otherwise place versions directly on project package references.
