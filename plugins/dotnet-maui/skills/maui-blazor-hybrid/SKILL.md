---
name: maui-blazor-hybrid
description: >-
  Build and debug .NET MAUI Blazor Hybrid features with BlazorWebView,
  HybridWebView, Razor components, static assets, JS/.NET interop, trimming and
  NativeAOT concerns, and DevFlow CDP debugging across routes. USE FOR: MAUI
  apps hosting Razor UI, embedded HTML/JS surfaces, native-to-web messaging,
  existing plain HTML/JavaScript surfaces, non-Razor HTML/JS messaging, choosing
  BlazorWebView vs HybridWebView, SendRawMessage/RawMessageReceived, JSON DTO
  message contracts, JsonSerializerContext/System.Text.Json source generation,
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
   For `HybridWebView` raw messaging, always show `SendRawMessage`,
   `RawMessageReceived`, a JSON DTO contract, and a source-generated
   `JsonSerializerContext`/`System.Text.Json` path when trimming safety is part
   of the request.
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
exchange. For raw JS/.NET messaging answers, include both the raw message APIs
and a typed JSON DTO/source-generated serialization boundary:

```csharp
hybridWebView.SendRawMessage("refresh");
hybridWebView.RawMessageReceived += OnRawMessageReceived;
```

For non-trivial payloads, keep messages typed at the .NET boundary instead of
switching on ad-hoc strings. Use JSON DTOs with `System.Text.Json` source
generation so serialization stays trimming/NativeAOT friendly:

```csharp
public sealed record HybridMessage(string Action, string? Id);

[JsonSerializable(typeof(HybridMessage))]
internal sealed partial class HybridJsonContext : JsonSerializerContext
{
}

void OnRawMessageReceived(object? sender, HybridWebViewRawMessageReceivedEventArgs e)
{
    var message = JsonSerializer.Deserialize(
        e.Message,
        HybridJsonContext.Default.HybridMessage);

    if (message is null)
        throw new JsonException("Malformed HybridWebView message.");

    // Dispatch only known actions after validating authorization/state.
}

hybridWebView.SendRawMessage(JsonSerializer.Serialize(
    new HybridMessage("refresh", null),
    HybridJsonContext.Default.HybridMessage));
```

```javascript
window.HybridWebView.SendRawMessage(JSON.stringify({
  action: "save",
  id: "42"
}));
```

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
- HybridWebView raw messages use JSON DTOs plus `JsonSerializerContext` or
  another explicit `System.Text.Json` source-generated path when trimming-safe
  serialization matters.
- Release/trimming-sensitive code avoids unpreserved reflection.
- DevFlow CDP inspection targets the correct WebView and route.
