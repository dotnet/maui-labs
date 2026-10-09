# Microsoft.Maui.DevFlow.WebView

**Experimental developer tooling — not for production apps.** This package enables
DevFlow's Chrome DevTools Protocol (CDP) automation and console logging for ordinary
MAUI `WebView` and `HybridWebView`. Both hosts use one embedded JavaScript engine.
There is no ASP.NET Core/Razor dependency and no script tag or hosted debug asset to add.

## Packages and requirements

| Package | Purpose |
| --- | --- |
| `Microsoft.Maui.DevFlow.WebView` | Generic WebView/HybridWebView host adapters and shared engine |
| `Microsoft.Maui.DevFlow.Blazor` | Optional BlazorWebView adapter, Blazor startup and relative routing |
| `Microsoft.Maui.DevFlow.Agent` | In-app agent exposing the HTTP automation API |

Requires .NET 10 and a configured DevFlow agent. The host APIs used are available
from MAUI 10.0.20; this repository's current package graph resolves MAUI Core and
Essentials 10.0.41. Keep the application's MAUI packages aligned with those
resolved dependencies. Update the agent, CLI, Client, WebView and Blazor packages
together and rebuild older consumers; this experimental debug-only protocol
does not retain WebView-facing compatibility shims.

```sh
dotnet add package Microsoft.Maui.DevFlow.WebView --prerelease
```

## Quick start

```csharp
using Microsoft.Maui.DevFlow.WebView;

// In MauiProgram, alongside your existing AddMauiDevFlowAgent registration:
#if DEBUG
builder.AddMauiWebViewDevFlowTools(options =>
{
    options.EnableWebViewInspection = true;
    options.EnableLogging = true;
});
#endif
```

Existing `WebView.Source` URL/HTML content and Hybrid local resources, native message
handlers and raw JSON messaging remain owned by MAUI. The debug adapter does not
replace Android's WebViewClient, WK navigation delegates, or WebView2 initialization
handlers. Give controls `AutomationId`s for convenient native correlation; owner correlation
also works without one.

List contexts with `maui devflow webview webviews`, then select the reported
canonical ID using `--context-id webview-0` (HTTP/Client/MCP/Inspector: `contextId`).
IDs are unique even for unnamed hosts or duplicate AutomationIds. Only `ready`
is emitted; numeric/AutomationId/element selectors and the old `webview` query,
`--webview`/`-w` options and `isReady` field are removed. Omission/null selects
the active host; blank/invalid values fail without dispatch. Upgrade the agent,
CLI, Client, WebView and Blazor packages together and rebuild consumers.
`hostKind` describes hosting (`webview`, `hybrid`, `blazor` or custom metadata), not the
native backend or behavior dispatch. Native capture and service overrides determine
behavior. DOM layout enrichment remains Blazor-only (`scope.includeBlazorElements`);
generic hosts do not advertise Blazor layout coverage.

For Blazor apps use `AddMauiBlazorDevFlowTools` instead; it includes the generic
registration and adds Blazor-specific readiness and client-side routing.

Registration is idempotent: services are created lazily, mapper hooks are installed
once, and repeated calls do not create discarded service instances. Generic and
Blazor options are independent. The first explicit generic configuration wins in
either registration order; a default generic registration from Blazor can still be
explicitly configured later. The first Blazor configuration wins. Explicitly
disabled registrations remain disabled, and manually registered services are
preserved rather than reconfigured.

`EnableLogging` controls routine diagnostics, not errors. Initialization, monitoring
and cleanup failures always use `Trace.TraceError` and the connected agent's error
log path. Each bridge exposes `LastError`, cleared when initialization succeeds;
failed CDP responses include the native/injection failure details.

## Supported hosts

| Platform | WebView | HybridWebView |
| --- | --- | --- |
| Android | Yes | Yes |
| iOS | Yes | Yes |
| Mac Catalyst | Yes | Yes |
| Windows (WebView2) | Yes | Yes |
| macOS AppKit backend | Yes | No — the backend has no HybridWebView handler |
| Linux GTK / Windows WPF | Not provided by this package | Not provided by this package |
| `net10.0` | Shared engine/host unit tests only | No native adapter |

Safari inspection additionally requires iOS/Mac Catalyst 16.4+ or macOS 13.3+.
Android inspection is a process-wide native setting. CDP is a JavaScript
implementation, **not the browser's complete native debugging protocol**:
unsupported Browser methods return errors rather than fake success. DOM/source,
runtime evaluation, console capture, text insertion, reload and navigation are
available through the shared engine.

Each attachment serializes CDP requests because its JavaScript response mailbox has
one slot. Unique internal wire IDs isolate late replies even when callers reuse a
CDP ID; matching responses are remapped to the caller's original ID.
Handler replacement cancels queued commands, readiness and console work,
detaches listeners and unregisters only the old attachment's exact command delegate.
Timeouts are not replayed automatically, avoiding duplicate mutations.

The host-neutral engine injects embedded assets after document readiness. It observes
document replacement without touching the app's navigation or messaging delegates.
Explicitly schemed URLs use native navigation. Browser-relative, root-relative and
scheme-relative URLs are resolved by the document rather than treated as native
file paths. Only the Blazor adapter adds Blazor client-side routing.
