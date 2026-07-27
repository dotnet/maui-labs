# Microsoft.Maui.DevFlow

A comprehensive testing, automation, and debugging toolkit for .NET MAUI applications.

> ⚠️ **Experimental** — APIs may change between releases. Not covered by the Microsoft Support Policy.

## Packages

| Package | Description |
|---------|-------------|
| **Microsoft.Maui.DevFlow.Agent** | In-app agent for .NET MAUI apps. Exposes visual tree, element interactions, screenshots, and profiling via HTTP/JSON API. |
| **Microsoft.Maui.DevFlow.Agent.Core** | Platform-agnostic core: HTTP server, visual tree walker, CSS selector engine, network capture, profiling. |
| **Microsoft.Maui.DevFlow.Agent.Gtk** | GTK/Linux agent for Maui.Gtk apps. |
| **Microsoft.Maui.DevFlow.Blazor** | Blazor WebView CDP bridge. Enables Chrome DevTools Protocol access for Blazor Hybrid content via Chobitsu. |
| **Microsoft.Maui.DevFlow.Blazor.Gtk** | Blazor CDP bridge for WebKitGTK on Linux. |
| **Microsoft.Maui.DevFlow.CLI** | DevFlow command implementation used by the unified `maui devflow` CLI surface for automation, debugging, and MCP server support. |
| **Microsoft.Maui.DevFlow.Driver** | Platform-aware app driver for iOS, Android, Mac Catalyst, Windows, and Linux. |
| **Microsoft.Maui.DevFlow.Logging** | Buffered rotating JSONL file logger. No MAUI dependency. |

## Quick Start

### 1. Install the NuGet packages

```xml
<PackageReference Include="Microsoft.Maui.DevFlow.Agent" />
<PackageReference Include="Microsoft.Maui.DevFlow.Blazor" />  <!-- If using Blazor Hybrid -->
```

### 2. Register in MauiProgram.cs

```csharp
using Microsoft.Maui.DevFlow.Agent;

public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder.UseMauiApp<App>();

    #if DEBUG
    builder.AddMauiDevFlowAgent();
    #endif

    return builder.Build();
}
```

Layout diagnostics is currently feature-gated while the cross-platform
acceptance matrix completes. Enable it explicitly:

```csharp
builder.AddMauiDevFlowAgent(options =>
{
    options.EnableLayoutDiagnostics = true;
});
```

### 3. Install the unified CLI tool

```bash
dotnet tool install -g Microsoft.Maui.Cli --prerelease
```

### 4. Interact with your running app

```bash
# Install DevFlow skills for AI agent integration (auto-detects target directory;
# defaults to .claude/skills/ — configurable via --target: claude, github, agent, agents, or auto)
# (configurable via --target: claude, github, agent, agents, or auto)
maui devflow init

# Visual tree
maui devflow ui tree

# Detect clipped, overflowing, truncated, or occluded UI
maui devflow ui diagnostics --profile agent

# Take a screenshot
maui devflow ui screenshot -o screenshot.png

# Tap an element
maui devflow ui tap --automationid "MyButton"

# Start MCP server for AI agent integration
maui devflow mcp
```

### Session identity

When `Microsoft.Maui.DevFlow.Agent` is referenced, builds are tagged with a **session identity**
derived from the project path. This metadata-only identifier helps DevFlow distinguish builds
from different environments (e.g. worktrees, CI agents, dev machines) without modifying
the app's `ApplicationId` or bundle identifier.

The session identity is included in:
- Assembly metadata (`Microsoft.Maui.DevFlowSessionId`) — compile-time injected by the `Microsoft.Maui.DevFlow.Agent` MSBuild targets
- Broker registration (visible via `maui devflow list`)
- Agent status endpoint (`/api/v1/agent/status`)

You can override the automatically derived identity:

```bash
# Set a specific session identity
dotnet build -p:MauiDevFlowSessionId=mysession
```

> **Note:** Session IDs are sanitized to lowercase alphanumeric characters only.
> For example, `My-Session` would become `mysession`. Auto-derived IDs (from the
> project path) are prefixed with `dw` and truncated to 26 characters. Explicit
> overrides keep the full sanitized value without prefix or truncation.

The same value can also be supplied via the `MAUI_DEVFLOW_SESSION_ID` environment variable.

## Features

- **Visual Tree Inspection** — query the full MAUI visual tree via HTTP API or CLI
- **Layout Diagnostics** — detect clipping, lost overflow, text truncation, overlap, and interaction occlusion with platform-specific evidence and confidence
- **Element Interaction** — tap, fill, scroll, navigate, focus, resize, and mutate properties
- **Screenshots** — capture PNG screenshots from any platform (full window or per-element)
- **Screen Recording** — start/stop video recording of app sessions
- **Network Monitoring** — intercept and inspect HTTP requests/responses
- **Performance Profiling** — CPU, memory, GC, and jank detection with markers and spans
- **Blazor CDP Bridge** — Chrome DevTools Protocol for Blazor WebViews (DOM, JS eval, navigation, input)
- **MCP Server** — structured tools for AI agent integration, including `maui_layout_diagnostics`
- **Logging** — buffered JSONL file logging with WebView JS console capture
- **Real-time Streaming** — WebSocket channels for logs, network, sensors, profiler, and UI events
- **Storage Access** — read/write app preferences, secure storage, discover file storage roots, and manage sandboxed app files remotely
- **Device Introspection** — battery, connectivity, geolocation, display, permissions, and sensor data
- **Dialog Handling** — detect and dismiss alerts/action sheets programmatically
- **Batch Operations** — execute command sequences from stdin for scripting
- **Agent Extensions** — expose app-specific diagnostic tools under `/api/v1/ext/{namespace}/...` with self-describing metadata for CLI and MCP discovery
- **Multi-Platform** — iOS, Android, Mac Catalyst, Windows, Linux/GTK

## CLI Commands

All DevFlow commands are available under `maui devflow`. Run `maui devflow <command> --help` for details.

| Command Group | Description |
|---------------|-------------|
| `ui` | Visual tree, element interaction, screenshots, alerts, assertions |
| `recording` | Start, stop, and manage screen recordings of app sessions |
| `webview` | Blazor WebView automation — DOM, JS eval, navigation, input, screenshots |
| `logs` | Fetch and stream application logs |
| `network` | Monitor and inspect HTTP requests |
| `storage` | Read/write app preferences, secure storage, discover file storage roots, and manage sandboxed app files |
| `agent` | Discover and inspect connected agents (status, list, wait, diagnose) |
| `extensions` | List, describe, and call app-specific DevFlow extension tools |
| `broker` | Manage the agent broker (start, stop, status, log) |
| `batch` | Execute command sequences from stdin |
| `commands` | List all available commands (schema discovery) |
| `mcp` | Start the MCP server for AI agent integration |

### Layout diagnostics

```bash
# High-signal findings for an agent repair loop
maui devflow ui diagnostics --profile agent

# Include all geometric overlap observations
maui devflow ui diagnostics --profile exhaustive --minimum-severity info

# Fail CI when serious violations are present or the scan is incomplete
maui devflow ui diagnostics --profile ci --fail-on serious --json

# Continuously re-run after UI events
maui devflow ui diagnostics --watch
```

The `ci` profile independently fails incomplete scans so unavailable or
budget-truncated evidence cannot produce a clean result. Use `--fail-on none`
only when both violation and incomplete-scan exit failures should be disabled.

The same result is available through:

- HTTP: `POST /api/v1/ui/diagnostics/layout`
- Driver: `AgentClient.AnalyzeLayoutAsync`
- MCP: `maui_layout_diagnostics`
- Web Inspector: the Layout diagnostics side panel

Results distinguish violations, observations, incomplete checks, confidence,
clip causes, visual versus interaction occlusion, and permanent platform
limitations. Text content is not returned by default.

Debug builds generate XAML source maps by default, so findings can include
`sourceFile`, `sourceLine`, and `sourceColumn`. Source-content hashes are not
emitted by the diagnostics contract.
Set `DevFlowXamlSourceMapsEnabled=false` to disable source embedding, or enable
it explicitly for another configuration. Source maps embed developer file paths
and XAML text and should normally remain disabled for Release/store builds.

The request privacy modes are:

- `none` - no text or text length in evidence (default);
- `length` - include only text length;
- `raw` - include raw text explicitly.

Interaction occlusion modes are `none`, `interactiveTargets` (default), and
`all`.

Persistent suppressions can be stored beside the project in `.mauidevflow`:

```json
{
  "port": 9225,
  "layoutDiagnostics": {
    "suppressions": [
      {
        "ruleId": "layout.element-clipped",
        "elementType": "Button",
        "automationId": "ExpectedClippedButton",
        "sourceFile": "Views/Page.xaml",
        "sourceLineStart": 20,
        "sourceLineEnd": 30,
        "relatedAutomationId": "ClipHost",
        "reason": "Intentional carousel preview"
      }
    ]
  }
}
```

User-wide suppressions use the same `suppressions` shape in
`~/.mauidevflow/layout-diagnostics.json`. CLI, MCP, and the Web Inspector merge
user and project policies before requesting a scan.

### DevFlow Global Options

These options apply to all `maui devflow` subcommands:

| Option | Description |
|--------|-------------|
| `--agent-port`, `-ap` | Agent HTTP port (auto-discovered via broker/.mauidevflow; falls back to 9223) |
| `--agent-host`, `-ah` | Agent HTTP host (default: localhost) |
| `--platform`, `-p` | Target platform (maccatalyst, android, ios, windows) |
| `--no-json` | Force human-readable output |

## Platform Support

| Platform | Status |
|----------|--------|
| Mac Catalyst | ✅ |
| iOS Simulator | ✅ |
| Linux/GTK | ✅ |
| Android | 🔄 In progress |
| Windows | 🔄 In progress |

## Documentation

- [Broker Architecture](../../docs/DevFlow/broker.md)
- [Protocol Spec](../../docs/DevFlow/spec/README.md)
- [Android Setup](../../docs/DevFlow/setup-guides/android-setup.md)
- [Apple Platforms Setup](../../docs/DevFlow/setup-guides/apple-platforms-setup.md)
- [Windows Setup](../../docs/DevFlow/setup-guides/windows-setup.md)

## Development

```bash
# Open just DevFlow in your IDE
open src/DevFlow/DevFlow.slnf

# Build
dotnet build src/DevFlow/DevFlow.slnf

# Run tests
dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Tests/
```

### Real app integration tests

The simulator/emulator-driven suite is kept separate from the fast PR test pass and is intended to be run explicitly. Set `DEVFLOW_TEST_PLATFORM` to one of: `maccatalyst` (or `mac`/`catalyst`), `ios`, `android`, `windows`. Defaults to `maccatalyst` on macOS, `windows` on Windows.

```bash
# Mac Catalyst
DEVFLOW_TEST_PLATFORM=maccatalyst dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/

# iOS Simulator
DEVFLOW_TEST_PLATFORM=ios DEVFLOW_TEST_IOS_VERSION=18.x dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/

# Android Emulator
DEVFLOW_TEST_PLATFORM=android DEVFLOW_TEST_ANDROID_API=35 DEVFLOW_TEST_ANDROID_AVD=devflow-tests-api35 DEVFLOW_TEST_ANDROID_SERIAL=emulator-5580 dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/

# Windows (run on a Windows machine)
DEVFLOW_TEST_PLATFORM=windows dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/
```

For local reliability, prefer running one platform suite at a time from a given repo worktree. Android fixture selection can be pinned with `DEVFLOW_TEST_ANDROID_AVD` and `DEVFLOW_TEST_ANDROID_SERIAL` when you want the harness to use a known emulator instance.

There is also a manual GitHub Actions workflow at `.github/workflows/devflow-integration.yml` for running the same suite in CI.

## Version

Current version is managed in [`eng/Versions.props`](../../eng/Versions.props).
