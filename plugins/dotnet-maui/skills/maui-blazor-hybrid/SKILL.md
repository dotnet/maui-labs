---
name: maui-blazor-hybrid
description: >-
  Build and debug .NET MAUI Blazor Hybrid features with BlazorWebView,
  HybridWebView, Razor components, static assets, JS/.NET interop, trimming and
  NativeAOT concerns, and DevFlow CDP debugging across routes. USE FOR: MAUI
  apps hosting Razor UI, embedded HTML/JS surfaces, native-to-web messaging,
  choosing BlazorWebView vs HybridWebView, SendRawMessage/RawMessageReceived,
  wwwroot assets, JavaScript interop, hybrid auth/data handoff, stale DOM or
  Razor route debugging, and WebView CDP inspection with maui_cdp_webviews,
  maui_cdp_source, and maui_cdp_evaluate. DO NOT USE FOR: pure native XAML UI
  (use maui-ui-patterns), authentication design by itself (use
  maui-auth-secure-storage), or generic browser web apps.
---

# MAUI Blazor Hybrid

Use this skill when a MAUI app hosts Razor components or an embedded web surface
inside a native app.

## Workflow

1. Inspect `MauiProgram.cs`, `.razor` files, `wwwroot`, and XAML pages that host
   `BlazorWebView` or `HybridWebView`.
2. Choose the right host:
   - `BlazorWebView` for Razor components rendered inside MAUI.
   - `HybridWebView` for HTML/JavaScript content that exchanges messages with
     .NET without using the Blazor component model.
3. Register Blazor Hybrid services in `MauiProgram.cs`.
4. Keep native services in MAUI DI and consume them from Razor through DI.
5. Put static web assets under `wwwroot` or referenced Razor class libraries.
6. Use JS/.NET interop through documented APIs, not platform WebView internals.
7. Review trimming and NativeAOT risks for reflection, serialization, and JS
   interop payloads.
8. Use DevFlow CDP tools to inspect WebView DOM, console, screenshots, and route
   state when debugging.

## BlazorWebView Pattern

```csharp
builder.Services.AddMauiBlazorWebView();
```

```xml
<BlazorWebView HostPage="wwwroot/index.html">
    <BlazorWebView.RootComponents>
        <RootComponent Selector="#app" ComponentType="{x:Type local:Routes}" />
    </BlazorWebView.RootComponents>
</BlazorWebView>
```

Razor components can inject MAUI services registered in the same DI container.
Keep platform work behind interfaces so components remain testable. The root
component type depends on the template; newer templates often use `Routes`,
while older templates may use `Main`.

## HybridWebView Pattern

Use `HybridWebView` when the app owns an HTML/JS surface and needs message
exchange:

```csharp
hybridWebView.SendRawMessage("refresh");
hybridWebView.RawMessageReceived += OnRawMessageReceived;
```

Keep messages typed at the .NET boundary. Use JSON DTOs and source generation
when the payloads must be trimming-safe.

## Static Assets

- Put app-owned web files under `wwwroot`.
- Use `_content/{PackageId}/...` for static web assets from Razor class
  libraries.
- Avoid file-system paths that only work on one platform.
- Ensure CSP, local scripts, and asset paths work from the packaged app origin,
  not just from a development server.

## Interop and State

- Use `IJSRuntime` from Razor components for Blazor JS interop.
- Use `HybridWebView` messaging APIs for non-Blazor HTML/JS content.
- Dispose `DotNetObjectReference` instances when Razor components are disposed
  to avoid leaking component instances through JavaScript references.
- Keep auth/session/data services native-side and expose only the minimal state
  needed by Razor or JavaScript.
- Do not put long-lived secrets in browser local storage.
- Validate `RawMessageReceived` payloads against an expected schema before
  dispatching to .NET logic. Do not let raw messages trigger sensitive
  operations without authorization checks at the .NET receiver.

## Trimming and NativeAOT Guardrails

- Prefer `System.Text.Json` source-generated contexts for interop DTOs.
- Avoid reflection-based component discovery or dynamic serialization unless
  annotations/preservation are added.
- Test release builds because debug WebView behavior can hide trimming issues.
- Check third-party JS/.NET interop packages for trimming support.

## DevFlow CDP Debugging

When DevFlow is enabled, use WebView CDP tools to debug cross-route behavior:

```bash
maui devflow mcp
```

Blazor WebView developer tools and platform WebView debugging must be enabled
only in debug builds:

```csharp
#if DEBUG
builder.Services.AddBlazorWebViewDeveloperTools();
#endif
```

Relevant MCP tools include `maui_cdp_webviews`, `maui_cdp_source`,
`maui_cdp_evaluate`, `maui_cdp_screenshot`, and `maui_logs`. Inspect the active
WebView before evaluating route-specific DOM or JavaScript. Do not ship release
builds with WebView devtools or CDP access enabled.

## Validation Checklist

- `AddMauiBlazorWebView` is registered for Blazor Hybrid apps.
- Static assets resolve from packaged `wwwroot` or RCL `_content` paths.
- JS/.NET interop uses Blazor or HybridWebView APIs intentionally.
- Release/trimming-sensitive code avoids unpreserved reflection.
- DevFlow CDP inspection targets the correct WebView and route.
