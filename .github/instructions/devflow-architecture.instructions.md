---
applyTo: "src/DevFlow/**"
---

# DevFlow Architecture

## Communication Model

DevFlow uses a three-tier architecture:

```
┌──────────────┐                ┌──────────────┐                ┌──────────────────────────┐
│  CLI / MCP   │  HTTP (direct) │    Broker     │  WebSocket     │  Agent (in-app)          │
│  (maui-devflow)│ ◄──────────► │  (port 19223) │ ◄───────────── │  (dynamic port)          │
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

1. **Agent** runs inside the MAUI app process (added via NuGet package). Exposes HTTP API on a dynamic port. Registers with the Broker over WebSocket. Has direct access to the visual tree, pages, platform views.
2. **Broker** runs on the developer machine (port 19223). Agents register with it via WebSocket. CLI discovers agent ports through the broker's HTTP API.
3. **CLI** (`maui-devflow`) discovers agents via the broker, then communicates **directly** with agents over HTTP. Also hosts the MCP server for AI agent integration.

## Package Dependency Graph

```
Microsoft.Maui.DevFlow.CLI (global tool)
├── Microsoft.Maui.DevFlow.Driver (AgentClient — public API)
├── ModelContextProtocol (MCP server)
├── System.CommandLine (CLI framework)
├── Spectre.Console (terminal UI)
└── Websocket.Client (broker transport)

Microsoft.Maui.DevFlow.Agent (NuGet package for app developers)
├── Microsoft.Maui.DevFlow.Agent.Core (HTTP server, visual tree, interactions)
│   ├── Fizzler (CSS selector parsing)
│   └── SkiaSharp (screenshot capture/resize)
└── Microsoft.Maui.DevFlow.Blazor (optional — CDP bridge for Blazor WebViews)

Microsoft.Maui.DevFlow.Agent.Gtk (NuGet package for GTK/Linux apps)
├── Microsoft.Maui.DevFlow.Agent.Core
└── Microsoft.Maui.DevFlow.Blazor.Gtk (optional — WebKitGTK CDP)

Microsoft.Maui.DevFlow.Logging (standalone — no MAUI dependency)
```

## Key Extension Points

### Adding a New HTTP Endpoint

1. Add route in `Agent.Core/DevFlowAgentService.cs` → `ConfigureRoutes()`:
   ```csharp
   _server.MapGet("/api/myfeature", HandleMyFeature);
   ```
2. Implement handler (virtual for platform override):
   ```csharp
   protected virtual async Task<HttpResponse> HandleMyFeature(HttpRequest request) { ... }
   ```
3. Add DTO class at bottom of `DevFlowAgentService.cs` if needed
4. Add client method in `Driver/AgentClient.cs`
5. Optionally expose as MCP tool and/or CLI command

### Adding a New MCP Tool

See `mcp-tools.instructions.md`.

### Adding Platform-Specific Behavior

Override virtual methods from `Agent.Core/DevFlowAgentService.cs`:

- In `Agent/DevFlowAgentService.cs` with `#if` directives for iOS/Android/macOS/Windows
- In `Agent.Gtk/GtkAgentService.cs` for Linux/GTK
- Always call `await DispatchAsync(() => ...)` to run on the UI thread

## Visual Tree and Element Resolution

- `VisualTreeWalker` recursively walks MAUI's `IVisualTreeElement` hierarchy
- Each element gets a unique ID (ephemeral, regenerated on tree changes)
- Elements are resolved by: element ID, AutomationId, type, text content, CSS selector
- `ElementInfo` captures: Id, Type, AutomationId, Text, IsVisible, IsEnabled, Bounds, WindowBounds
- CSS selectors (Fizzler) work in Blazor WebViews via CDP

## Screenshot Capture Flow

1. **Default** (no params): captures `window.Page` via `VisualDiagnostics.CaptureAsPngAsync` — page content only
2. **Element** (`--id` or `--selector`): captures specific element bounds
3. **Fullscreen** (`--fullscreen`): platform-specific composited capture including status bar and safe areas
4. **iOS CLI fallback**: `simctl io screenshot` for full simulator display
5. All screenshots auto-scale to 1x logical resolution (configurable via `--scale native`)
