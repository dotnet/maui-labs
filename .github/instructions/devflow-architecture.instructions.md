---
applyTo: "src/DevFlow/**"
---

# DevFlow Architecture

## Communication Model

DevFlow uses a three-tier architecture:

```
┌──────────────┐                ┌──────────────┐                ┌──────────────────────────┐
│  CLI / MCP   │  HTTP (direct) │    Broker     │  WebSocket     │  Agent (in-app)          │
│  (maui devflow)│ ◄──────────► │  (port 19223) │ ◄───────────── │  (dynamic port)          │
└──────────────┘  (after port   └──────────────┘  (registration) └──────────────────────────┘
      │            discovery)          │                                │
      │ HTTP (direct, after discovery) │                                │ Platform APIs
      └────────────────────────────────┼────────────────────────────────┘
      ▲                                                               
      │ MCP (stdio)                                                   
      ▼                                                               
┌──────────────┐                                              
│  AI Agent    │                                              
│  (Copilot,   │                                              
│   Claude,    │                                              
│   etc.)      │                                              
└──────────────┘
```

1. **Agent** runs inside the app process (added via NuGet package — MAUI or plain .NET). Exposes HTTP API on a dynamic port. Registers with the Broker over WebSocket. Has direct access to the visual tree, pages, platform views.
2. **Broker** runs on the developer machine (port 19223). Agents register with it via WebSocket. CLI discovers agent ports through the broker's HTTP API.
3. **CLI** (`maui devflow`) discovers agents via the broker, then communicates **directly** with agents over HTTP. Also hosts the MCP server for AI agent integration.

## Package Dependency Graph

```
Microsoft.Maui.DevFlow.CLI (global tool)
├── Microsoft.Maui.DevFlow.Driver (platform drivers)
│   └── Microsoft.Maui.DevFlow.Client (AgentClient — public protocol API)
├── ModelContextProtocol (MCP server)
├── System.CommandLine (CLI framework)
├── Spectre.Console (terminal UI)
└── Websocket.Client (broker transport)

Microsoft.Maui.DevFlow.Client (netstandard2.0 + modern .NET)
├── AgentClient, ElementInfo, protocol DTOs, HTTP/JSON operations
└── System.Text.Json

Microsoft.Maui.DevFlow.Driver (modern .NET, platform-specific)
├── Microsoft.Maui.DevFlow.Client
├── App process management, UI Automation, Mac accessibility
└── SkiaSharp (screenshot processing)

Microsoft.Maui.DevFlow.Agent.Abstractions (no MAUI dependency)
├── AgentHttpServer, AgentOptions, routing, DevFlowAgentService
├── ElementInfo + DTOs, Css/, Network/, Profiling/, extensions
├── Fizzler (CSS selector parsing)
└── SkiaSharp (screenshot capture/resize)

Microsoft.Maui.DevFlow.Agent.Core (MAUI UI backend)
└── Microsoft.Maui.DevFlow.Agent.Abstractions

Microsoft.Maui.DevFlow.Agent (NuGet package for MAUI app developers)
├── Microsoft.Maui.DevFlow.Agent.Core
└── Microsoft.Maui.DevFlow.Blazor (optional — CDP bridge for Blazor WebViews)

Microsoft.Maui.DevFlow.Agent.Gtk (NuGet package for GTK/Linux apps)
├── Microsoft.Maui.DevFlow.Agent.Core
└── Microsoft.Maui.DevFlow.Blazor.Gtk (optional — WebKitGTK CDP)

Microsoft.Maui.DevFlow.Agent.WPF (NuGet package for WPF apps)
└── Microsoft.Maui.DevFlow.Agent.Core

Microsoft.Maui.DevFlow.Agent.Native (plain .NET android/ios/maccatalyst/macos — no MAUI)
├── Microsoft.Maui.DevFlow.Agent.Abstractions
└── NativeUi.{Android,UIKit,AppKit}, NativeDevFlowAgentService, DevFlowAgent.Start()

Microsoft.Maui.DevFlow.Agent.Native.Essentials (optional add-on)
└── Microsoft.Maui.DevFlow.Agent.Native + Microsoft.Maui.Essentials

Microsoft.Maui.DevFlow.Logging (standalone — no MAUI dependency)
```

## The Abstractions / backend split

`DevFlowAgentService` lives in **`Agent.Abstractions`** and owns routing, orchestration policy
(retries, waits, batch sequencing, error shaping, CSS filtering over `ElementInfo`) and every
handler that needs no UI framework. Handlers that do need one are `protected virtual` and return a
uniform `501` `{ error: "not_supported", capability, reason }` envelope by default. Backends
override them:

- `MauiDevFlowAgentService` (in `Agent.Core`) — MAUI Controls
- `NativeDevFlowAgentService` (in `Agent.Native`) — Android views, UIKit, AppKit
- `EssentialsNativeDevFlowAgentService` (in `Agent.Native.Essentials`) — adds Essentials endpoints

This is a virtual-method seam rather than an `IUiBackend` interface because partial classes cannot
span assemblies, and the existing `protected virtual` hooks that `Agent`, `Agent.Gtk` and
`Agent.WPF` override had to keep working verbatim.

**Rules when adding to this area:**

- Anything framework-neutral belongs in `Agent.Abstractions`. Do not add a `Microsoft.Maui.*`
  reference to it or to `Agent.Native` — `MauiFreeAssemblyTests` reads the PE metadata and fails
  the build if you do.
- Essentials-backed handlers go in `src/DevFlow/Shared.Essentials/EssentialsAgentSupport.cs`, which
  is compiled by **both** `Agent.Core` and `Agent.Native.Essentials`. Never fork them; the whole
  point is that the two agents cannot drift.
- Report support honestly. Add the capability to `HandleCapabilities` with `supported: false` and a
  reason rather than letting an endpoint fail obscurely.
- `agent.framework` (`maui` | `native`) and `agent.uiFramework`
  (`maui-controls` | `android-views` | `uikit` | `appkit` | `gtk` | `wpf`) are how clients branch.

Assembly moves out of `Agent.Core` are **binary breaking**. Namespaces were deliberately left alone
so source stays compatible. Label such PRs `breaking-change` and say so in the description.

## Key Extension Points

### Adding a New HTTP Endpoint

1. Add route in `Agent.Abstractions/DevFlowAgentService.Handlers.cs` → `RegisterRoutes()`:
   ```csharp
   _server.MapGet("/api/myfeature", HandleMyFeature);
   ```
2. Implement the handler in `Agent.Abstractions` if it is framework-neutral. If it needs a UI
   framework, declare it virtual there returning `NotSupported(...)`, then override it in
   `Agent.Core/MauiDevFlowAgentService.cs` and `Agent.Native/NativeDevFlowAgentService.cs`:
   ```csharp
   protected virtual async Task<HttpResponse> HandleMyFeature(HttpRequest request) { ... }
   ```
3. Add DTO class at bottom of the owning file if needed
4. Add client method in `Client/AgentClient.cs`
5. Optionally expose as MCP tool and/or CLI command

### Adding a New MCP Tool

See `mcp-tools.instructions.md`.

### Adding Platform-Specific Behavior

Override virtual methods from `Agent.Abstractions/DevFlowAgentService.cs`:

- In `Agent/DevFlowAgentService.cs` with `#if` directives for iOS/Android/macOS/Windows
- In `Agent.Gtk/GtkAgentService.cs` for Linux/GTK
- In `Agent.WPF/WpfAgentService.cs` for WPF
- In `Agent.Native/NativeUi.{Android,UIKit,AppKit}.cs` for plain .NET apps
- Always call `await DispatchAsync(() => ...)` to run on the UI thread

## Visual Tree and Element Resolution

- `VisualTreeWalker` (MAUI) recursively walks MAUI's `IVisualTreeElement` hierarchy;
  `NativeElementRegistry` + `NativeUi` do the same over `ViewGroup`/`UIView`/`NSView`
- Each element gets a unique ID (ephemeral, regenerated on tree changes)
- Elements are resolved by: element ID, AutomationId, type, text content, CSS selector
- Native automation ids come from `View.Tag` then the resource-entry name (Android),
  `AccessibilityIdentifier` (UIKit), `NSView.Identifier` (AppKit)
- MAUI normalises types to `Button`/`Label`/`Entry`; native reports the real platform type
  (`UIButton`, `NSButton`, …), which is why integration tests use
  `IntegrationTestBase.ButtonTypeName`
- `ElementInfo` captures: Id, Type, AutomationId, Text, IsVisible, IsEnabled, Bounds, WindowBounds
- CSS selectors (Fizzler) work in Blazor WebViews via CDP

## Screenshot Capture Flow

1. **Default** (no params): captures `window.Page` via `VisualDiagnostics.CaptureAsPngAsync` — page content only
2. **Element** (`--id` or `--selector`): captures specific element bounds
3. **Fullscreen** (`--fullscreen`): platform-specific composited capture including status bar and safe areas
4. **iOS CLI fallback**: `simctl io screenshot` for full simulator display
5. All screenshots auto-scale to 1x logical resolution (configurable via `--scale native`)
