# Microsoft.Maui.DevFlow.Blazor

**Experimental debug-only tooling, not for production apps.** This package adds
BlazorWebView handler capture, rendered `#app` readiness and relative
`Blazor.navigateTo` routing to the shared DevFlow WebView engine.

## Packages and requirements

Requires .NET 10, MAUI 10.0.20 or newer and a configured DevFlow agent. Keep the
application's resolved MAUI dependencies aligned. Upgrade the agent, CLI, Client,
WebView and Blazor packages together and rebuild consumers: obsolete internal
bridge/base/script APIs are not retained.

| Package | Purpose |
| --- | --- |
| Microsoft.Maui.DevFlow.Agent | In-app HTTP automation agent |
| Microsoft.Maui.DevFlow.WebView | Shared embedded engine and standard/Hybrid adapters |
| Microsoft.Maui.DevFlow.Blazor | Blazor host readiness and routing |
| Microsoft.Maui.DevFlow.Blazor.Gtk | Separate WebKitGTK Blazor adapter |

## Quick start

```sh
dotnet add package Microsoft.Maui.DevFlow.Blazor --prerelease
```

```csharp
using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.DevFlow.Blazor;

#if DEBUG
builder.AddMauiDevFlowAgent();
builder.AddMauiBlazorDevFlowTools();
#endif
```

This includes generic WebView/HybridWebView registration. Generic and Blazor
options are independent; registrations are lazy and idempotent. The first Blazor
configuration wins. Explicitly disabled and manually registered services are
respected.

| Platform | BlazorWebView |
| --- | --- |
| Android, iOS, Mac Catalyst, Windows (WinUI) | Supported |
| macOS AppKit backend | Supported with the backend's BlazorWebView package |
| Linux GTK | Use Microsoft.Maui.DevFlow.Blazor.Gtk |
| Windows WPF | Not supplied by this adapter |

The bridge injects embedded Chobitsu/console scripts from C# after document and
Blazor readiness. No JS initializer, hosted static debug asset, manual download
or script tag is needed. Native navigation and messaging remain app/MAUI-owned.
The asset-free adapter uses the ordinary .NET SDK, excludes application-only
WebView build imports and does not produce a Razor static-asset manifest.

List contexts using `maui devflow webview webviews`; select an index, AutomationId
or element ID using `--webview`. Use the index for duplicate IDs.
`hostKind: blazor` is descriptive metadata, not behavior dispatch.
Contexts retain `ready` and `isReady`; DOM layout remains Blazor-only.
CDP requests are serialized, late replies use unique wire IDs, timed-out
mutations are never replayed, and handler detachment cancels pending work.
