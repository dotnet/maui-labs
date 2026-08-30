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
| **Microsoft.Maui.DevFlow.Testing** | Experimental public preview of framework-neutral flow contracts, Markdown parsing, validation, recording, and replay. It is independent of CLI, broker, Inspector, MCP, and test-framework adapters. |
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

    #if MAUI_DEVFLOW
    builder.AddMauiDevFlowAgent();
    #endif

    return builder.Build();
}
```

`MAUI_DEVFLOW` is defined by the package only when `MauiDevFlowEnabled=true`. Debug builds enable
it by default. For an explicit optimized, read-only diagnostics build:

```bash
dotnet build -c Release -p:MauiDevFlowProfileMode=true
```

Profile mode also defines `MAUI_DEVFLOW`, so the same registration block is compiled without
requiring `DEBUG`; accidental optimized inclusion without profile mode remains a build error.
Profile mode disables network monitoring entirely so streaming/large HTTP bodies cannot perturb or
block the diagnostic run. The same build contract and runtime defaults ship in the standard,
GTK, and WPF agent packages.

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
- **Element Interaction** — tap, fill, scroll, navigate, focus, resize, and mutate properties
- **Screenshots** — capture PNG screenshots from any platform (full window or per-element)
- **Screen Recording** — start/stop video recording of app sessions
- **Network Monitoring** — intercept and inspect HTTP requests/responses
- **Performance Profiling** — CPU, memory, GC, and jank detection with markers and spans
- **Blazor CDP Bridge** — Chrome DevTools Protocol for Blazor WebViews (DOM, JS eval, navigation, input)
- **MCP Server** — 69 structured tools for AI agent integration (Claude, etc.)
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

### DevFlow Global Options

These options apply to all `maui devflow` subcommands:

| Option | Description |
|--------|-------------|
| `--agent-port`, `-ap` | Agent HTTP port (auto-discovered via broker/.mauidevflow; falls back to 9223) |
| `--agent-host`, `-ah` | Agent HTTP host (default: localhost) |
| `--platform`, `-p` | Target platform (maccatalyst, android, ios, windows) |
| `--no-json` | Force human-readable output |

## Test Workbench platform qualification

The Testing package is a `net9.0` library and has compile-only consumers for .NET 9/.NET 10,
Android, iOS, Mac Catalyst, Windows, and experimental AppKit. Compilation and general agent
availability do **not** qualify a platform runtime. The required all-platform completion gate is
Android, iOS, Mac Catalyst, and Windows:

| Required platform | Workbench qualification status |
|---|---|
| Android | Engineering pilot only; currently **not-qualified** because the required real-device first-attempt evidence is absent. Emulator artifacts remain pilot evidence only. |
| iOS | Required gate not yet qualified. |
| Mac Catalyst | Required gate not yet qualified. |
| Windows | Required gate not yet qualified. |

| Separately reported experimental platform | Status |
|---|---|
| macOS AppKit | Experimental AppKit fixture and advisory macOS handoff; it never waives a Mac Catalyst gate. |
| WPF | Experimental; it does not waive the Windows gate. |
| GTK/Linux | Experimental; it does not waive any required MAUI gate. |

The macOS package-consumer job is an Apple **compile** smoke before a publish; it does not launch
an app, simulator, or device. A macOS QA handoff is still required for iOS and Mac Catalyst
runtime evidence. Detailed QA scripts are intentionally owned by the `platform-qa-scripts` todo.
The independent Appium lane is black-box smoke coverage, not the DevFlow flow-execution kernel or
a qualification pass.

## Documentation

- [DevFlow Web Inspector and MAUI DevFlow Inspector hosts](../../docs/DevFlow/inspector.md)
- [Broker Architecture](../../docs/DevFlow/broker.md)
- [Protocol Spec](../../docs/DevFlow/spec/README.md)
- [Human-authored Testing and platform qualification](../../docs/DevFlow/testing.md)
- [Restricted test-agent protocol](../../docs/DevFlow/test-agent.md)
- [Evidence privacy and artifact trust](../../docs/DevFlow/evidence.md)
- [Preview API and contract compatibility policy](../../docs/DevFlow/compatibility.md)
- [Android Setup](../../docs/DevFlow/setup-guides/android-setup.md)
- [Apple Platforms Setup](../../docs/DevFlow/setup-guides/apple-platforms-setup.md)
- [Windows Setup](../../docs/DevFlow/setup-guides/windows-setup.md)
- [Independent Appium black-box smoke tests](../../docs/DevFlow/appium-smoke-testing.md)

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

The simulator/emulator-driven suite is kept separate from the fast PR test pass and is intended to be run explicitly. Set `DEVFLOW_TEST_PLATFORM` to one of: `maccatalyst` (or `mac`/`catalyst`), `macos` (experimental AppKit only), `ios`, `android`, `windows`. Defaults to `maccatalyst` on macOS, `windows` on Windows.

```bash
# Mac Catalyst
DEVFLOW_TEST_PLATFORM=maccatalyst dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/

# iOS Simulator
DEVFLOW_TEST_PLATFORM=ios DEVFLOW_TEST_IOS_VERSION=18.x dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/

# Android Emulator
DEVFLOW_TEST_PLATFORM=android DEVFLOW_TEST_ANDROID_API=35 DEVFLOW_TEST_ANDROID_AVD=devflow-tests-api35 DEVFLOW_TEST_ANDROID_SERIAL=emulator-5580 dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/

# Windows (run on a Windows machine)
DEVFLOW_TEST_PLATFORM=windows dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/

# Experimental native AppKit (run only on macOS; never Mac Catalyst coverage)
DEVFLOW_TEST_PLATFORM=macos DEVFLOW_RUN_APPKIT_FLOW_QA=1 dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/ --filter Category=AppKitFlowQa
```

For local reliability, prefer running one platform suite at a time from a given repo worktree. Android fixture selection can be pinned with `DEVFLOW_TEST_ANDROID_AVD` and `DEVFLOW_TEST_ANDROID_SERIAL` when you want the harness to use a known emulator instance.

There is also a manual GitHub Actions workflow at `.github/workflows/devflow-integration.yml` for running the same suite in CI.

### Independent Appium black-box smoke tests

The opt-in `Microsoft.Maui.DevFlow.Appium.SmokeTests` project is a separate external lane for
release-like/uninstrumented apps, native compatibility, and optional Android/iOS system permission
dialogs. It does not use the in-app DevFlow agent, broker, semantic flow runner, repair services,
or qualification gates. See [Appium black-box smoke tests](../../docs/DevFlow/appium-smoke-testing.md)
for driver setup, environment configuration, platform limitations, and artifact retention.

## Version

Current version is managed in [`eng/Versions.props`](../../eng/Versions.props).
