# Microsoft.Maui.DevFlow

A comprehensive testing, automation, and debugging toolkit for .NET MAUI applications — and, since
the agent was split away from MAUI, for plain .NET Android, iOS, Mac Catalyst and macOS apps too.

> ⚠️ **Experimental** — APIs may change between releases. Not covered by the Microsoft Support Policy.

## Packages

| Package | Description |
|---------|-------------|
| **Microsoft.Maui.DevFlow.Agent** | In-app agent for .NET MAUI apps. Exposes visual tree, element interactions, screenshots, and profiling via HTTP/JSON API. |
| **Microsoft.Maui.DevFlow.Agent.Abstractions** | The protocol itself: HTTP server, routing, element DTOs, CSS selector engine, network capture, profiling, extensions. No MAUI dependency. |
| **Microsoft.Maui.DevFlow.Agent.Core** | The MAUI UI backend: visual tree walker, `VisualElement` interactions, `BindableProperty` access, Essentials endpoints. |
| **Microsoft.Maui.DevFlow.Agent.Gtk** | GTK/Linux agent for Maui.Gtk apps. |
| **Microsoft.Maui.DevFlow.Agent.Native** | In-app agent for plain .NET apps with no MAUI reference — Android views, UIKit, and AppKit backends. |
| **Microsoft.Maui.DevFlow.Agent.Native.Essentials** | Optional add-on that lights up the device, storage and sensor endpoints for native apps using MAUI Essentials. |
| **Microsoft.Maui.DevFlow.Blazor** | Blazor WebView CDP bridge. Enables Chrome DevTools Protocol access for Blazor Hybrid content via Chobitsu. |
| **Microsoft.Maui.DevFlow.Blazor.Gtk** | Blazor CDP bridge for WebKitGTK on Linux. |
| **Microsoft.Maui.DevFlow.CLI** | DevFlow command implementation used by the unified `maui devflow` CLI surface for automation, debugging, and MCP server support. |
| **Microsoft.Maui.DevFlow.Client** | Portable protocol client: `AgentClient`, element and protocol DTOs, and their serialization. Targets `netstandard2.0`, so .NET Framework harnesses speak the same protocol as modern .NET consumers. |
| **Microsoft.Maui.DevFlow.Driver** | Platform-aware app driver for iOS, Android, Mac Catalyst, Windows, and Linux. Builds on `Microsoft.Maui.DevFlow.Client`. |
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

### 2b. Or, in a plain .NET app (no MAUI)

The same agent, CLI and MCP tools work against apps that never reference MAUI. Reference
`Microsoft.Maui.DevFlow.Agent.Native` and start it explicitly — there is no host builder to hook,
so nothing starts by itself.

```xml
<PackageReference Include="Microsoft.Maui.DevFlow.Agent.Native" />
```

```csharp
// Android — MainActivity.OnCreate
using Microsoft.Maui.DevFlow.Agent.Native;

protected override void OnCreate(Bundle? savedInstanceState)
{
    base.OnCreate(savedInstanceState);
    SetContentView(Resource.Layout.activity_main);
#if DEBUG
    this.StartDevFlowAgent();   // binds the activity the tree is walked from
#endif
}
```

```csharp
// iOS / Mac Catalyst — AppDelegate.FinishedLaunching
// macOS — AppDelegate.DidFinishLaunching
#if DEBUG
DevFlowAgent.Start();
#endif
```

Everything that does not need a UI framework behaves identically: visual tree, CSS queries,
hit-testing, tap/fill/clear/focus/key/gesture/scroll/batch, property get/set, screenshots, logs,
network capture, the profiler, actions and extensions.

Endpoints that have no meaning outside MAUI answer `501` with
`{ "error": "not_supported", "capability": …, "reason": … }` rather than failing opaquely, and
`/api/v1/agent/capabilities` reports them as `supported: false` up front. Today that is app theme
(MAUI reads `Application.RequestedTheme`) and Shell navigation. Preferences, secure storage, device
info, display, battery, connectivity, permissions, geolocation and sensors also start unsupported —
add `Microsoft.Maui.DevFlow.Agent.Native.Essentials` and swap the bootstrap to light them up:

```xml
<PackageReference Include="Microsoft.Maui.DevFlow.Agent.Native.Essentials" />
```

```csharp
using Microsoft.Maui.DevFlow.Agent.Native.Essentials;

// iOS / Mac Catalyst / macOS
EssentialsDevFlowAgent.Start();

// Android
Microsoft.Maui.ApplicationModel.Platform.Init(this, savedInstanceState);
this.StartDevFlowAgentWithEssentials();
```

That add-on pulls in `Microsoft.Maui.Essentials`, which needs the MAUI workload installed but does
**not** make the app a MAUI app — no `Microsoft.Maui.Controls`, no `MauiApp` host. On Android it
also requires the usual Essentials wiring: `Platform.Init` as above, plus forwarding
`OnRequestPermissionsResult` to `Platform.OnRequestPermissionsResult`.

Clients can tell the two apart without guessing: `/api/v1/agent/status` reports
`framework` (`maui` | `native`) alongside `uiFramework`
(`maui-controls` | `android-views` | `uikit` | `appkit` | `gtk` | `wpf`).

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

# Open the DevFlow Web Inspector
# http://localhost:19223/inspector/
maui devflow broker start
```

### Session identity

When `Microsoft.Maui.DevFlow.Agent` is referenced, builds are tagged with a **session identity**
derived from a one-way, sanitized project-path value. This metadata-only identifier helps DevFlow
distinguish builds from different environments without modifying the app's `ApplicationId` or
bundle identifier. The full project path is not embedded by default.

The session identity is included in:
- Assembly metadata (`Microsoft.Maui.DevFlowSessionId`) — compile-time injected by the `Microsoft.Maui.DevFlow.Agent` MSBuild targets
- Project identity metadata (`Microsoft.Maui.DevFlowProject`) — the project filename by default
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

For local debugging that needs full-path project disambiguation, opt in explicitly with
`-p:MauiDevFlowIncludeProjectPath=true`. This embeds the project path in the app assembly.

## Features

- **Visual Tree Inspection** — query the full MAUI visual tree via HTTP API or CLI
- **Layout Diagnostics** — detect clipping, lost overflow, text truncation, overlap, and interaction occlusion with platform-specific evidence and confidence
- **Element Interaction** — tap, fill, scroll, navigate, focus, resize, and mutate properties
- **Screenshots** — capture PNG screenshots from any platform (full window or per-element)
- **Screen Recording** — start/stop video recording of app sessions
- **Network Monitoring** — intercept and inspect HTTP requests/responses
- **Performance Profiling** — CPU, memory, GC, and jank detection with markers and spans
- **Blazor CDP Bridge** — Chrome DevTools Protocol for Blazor WebViews (DOM, JS eval, navigation, input)
- **DevFlow Web Inspector** — the shared browser UI, embedded by MAUI DevFlow Inspector hosts for VS Code and GitHub Copilot Canvas
- **Global Mutation Lease** — prevents browser, VS Code, Canvas, MCP, and CLI callers from driving the app concurrently
- **Workflow Recording** — broker-owned recording observes successful mutations from every host and emits replayable Markdown
- **Click-to-XAML** — Debug source maps connect visual-tree elements to their XAML declarations
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

- [DevFlow Web Inspector and MAUI DevFlow Inspector hosts](../../docs/DevFlow/inspector.md)
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

#### Running the suite against a plain .NET app

`DEVFLOW_TEST_FRAMEWORK` selects which sample app the fixtures deploy: `maui` (default) drives
`samples/DevFlow.Sample`, `native` drives the matching head under `samples/DevFlow.Sample.Native`.
Both samples expose the same automation ids, so the bulk of the suite is shared.

Tests that assert on MAUI-specific behaviour (Shell routing, `AppTheme`, Essentials-backed
preferences/secure-storage/sensors/device info, WebView CDP) are tagged
`[Trait("framework", "maui")]` and must be filtered out of a native run:

```bash
# Native iOS Simulator
DEVFLOW_TEST_FRAMEWORK=native DEVFLOW_TEST_PLATFORM=ios \
  dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/ --filter "framework!=maui"

# Native Mac Catalyst
DEVFLOW_TEST_FRAMEWORK=native DEVFLOW_TEST_PLATFORM=maccatalyst \
  dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/ --filter "framework!=maui"
```

`native` is supported for `android`, `ios` and `maccatalyst`. There is no plain-.NET Windows head,
and the AppKit head under `samples/DevFlow.Sample.Native/MacOS` does not yet have a driving fixture.

There is also a manual GitHub Actions workflow at `.github/workflows/devflow-integration.yml` for running the same suite in CI.

## Version

Current version is managed in [`eng/Versions.props`](../../eng/Versions.props).