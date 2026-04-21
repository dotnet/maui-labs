# Agent Instructions

Instructions for GitHub Copilot and other AI coding agents working with the maui-labs repository.

## Repository Overview

This repository hosts experimental .NET MAUI packages. It is a **multi-product mono-repo** — each product lives under `src/{Product}/` with its own version, solution filter, and CI workflow.

### Products

| Product | Package / Tool | Description |
|---------|---------------|-------------|
| **DevFlow** | `Microsoft.Maui.DevFlow.*` packages plus the unified `maui devflow` CLI surface | Runtime MAUI automation toolkit. In-app agent with HTTP API, visual tree inspection, CDP bridge for Blazor WebViews, MCP server for AI agents, cross-platform driver library. |

### Technology Stack

- **.NET 10** (SDK version pinned in `global.json`, `rollForward: latestMinor`)
- **C#** with `LangVersion: latest`, file-scoped namespaces
- **Microsoft.DotNet.Arcade.Sdk** for build infrastructure
- **Central Package Management** — all versions in `Directory.Packages.props`
- **xUnit** v2.9.3 for testing, **coverlet** for coverage
- **System.CommandLine** 2.0.0-beta4 for CLI tooling

## Building

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (see `global.json` for exact version)
- MAUI workload: `dotnet workload install maui`

### Build Commands

```bash
# Build everything
dotnet build MauiLabs.sln

# Build a single product (recommended for focused development)
dotnet build src/DevFlow/DevFlow.slnf

# Build via Arcade CI scripts (matches what CI runs for DevFlow)
# macOS/Linux:
./eng/common/cibuild.sh --configuration Release --prepareMachine --projects src/DevFlow/DevFlow.slnf
# Windows:
eng\common\cibuild.cmd -configuration Release -prepareMachine -projects src/DevFlow/DevFlow.slnf
```

### Build Troubleshooting

- If restore fails, check `NuGet.config` — feeds are internal dnceng proxies, not nuget.org
- If workload errors occur: `dotnet workload install maui macos maui-tizen`
- SDK version mismatch: check `global.json` vs `dotnet --version`

## Testing

```bash
# All tests
dotnet test MauiLabs.sln

# Per-product
dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Tests/
```

- Tests run in CI on **macOS and Windows** (matrix build)
- Test results: `artifacts/TestResults/**/*.xml`
- No quarantine or outerloop test attributes are used in this repo

## Code Conventions

- **ImplicitUsings**: enabled repo-wide
- **Nullable**: enabled repo-wide (`#nullable enable` is implicit)
- **File-scoped namespaces**: all files use `namespace X.Y.Z;` (not block-scoped)
- **No strong naming**: `SignAssembly: false`
- **Namespace pattern**: `Microsoft.Maui.DevFlow.{Component}.{SubComponent}`
- **No .editorconfig**: relies on Arcade SDK defaults
- **TreatWarningsAsErrors**: false (not enforced)

## Project Layout

```
maui-labs/
├── src/
│   ├── Cli/                              # Maui CLI product
│   │   ├── Microsoft.Maui.Cli/           # Unified `maui` CLI (includes DevFlow commands)
│   │   │   └── DevFlow/                  # DevFlow command implementation behind `maui devflow`
│   │   │       ├── Broker/               # Connection management
│   │   │       └── Mcp/Tools/            # MCP tool implementations
│   │   ├── Microsoft.Maui.Cli.UnitTests/ # CLI unit tests
│   │   └── Cli.slnf                      # Solution filter
│   └── DevFlow/                          # DevFlow agent product
│       ├── Microsoft.Maui.DevFlow.Agent.Core/   # Platform-agnostic agent (HTTP server, visual tree)
│       ├── Microsoft.Maui.DevFlow.Agent/         # Platform-specific overrides (iOS/Android/macOS/Windows)
│       ├── Microsoft.Maui.DevFlow.Agent.Gtk/     # GTK/Linux agent
│       ├── Microsoft.Maui.DevFlow.Blazor/        # Blazor WebView CDP bridge
│       ├── Microsoft.Maui.DevFlow.Blazor.Gtk/    # WebKitGTK CDP bridge
│       ├── Microsoft.Maui.DevFlow.Driver/        # Cross-platform driver (AgentClient)
│       ├── Microsoft.Maui.DevFlow.Logging/       # JSONL file logger
│       ├── Microsoft.Maui.DevFlow.Tests/         # xUnit tests
│       └── DevFlow.slnf                          # Solution filter
├── samples/                              # Sample MAUI apps (not shipped)
├── playground/                           # Manual test/scratch apps
├── eng/                                  # Shared build infrastructure
│   ├── pipelines/                        # Azure DevOps pipeline definitions
│   ├── Versions.props                    # Central version definitions
│   ├── Signing.props                     # Code signing configuration
│   ├── Publishing.props                  # NuGet publishing config
│   └── common/                           # Arcade SDK (DO NOT MODIFY)
├── Directory.Build.props                 # Global MSBuild properties
├── Directory.Build.targets               # Global MSBuild targets
├── Directory.Packages.props              # Central Package Management
├── global.json                           # SDK version pinning
├── NuGet.config                          # NuGet feed configuration
└── MauiLabs.sln                          # Full solution
```

### Key Configuration Files

| File | Purpose |
|------|---------|
| `global.json` | .NET SDK version and Arcade SDK version |
| `Directory.Build.props` | Global properties: TFMs, nullable, implicit usings, platform versions |
| `Directory.Packages.props` | All NuGet package versions (Central Package Management) |
| `eng/Versions.props` | Product version (`0.1.0-preview`), dependency versions |
| `eng/Signing.props` | Code signing: Microsoft cert for first-party, 3PartySHA2 for third-party |
| `eng/Publishing.props` | Arcade publishing version |
| `src/{Product}/Version.props` | Per-product version override |

## Packaging and Signing

- Packages are built by the Arcade SDK's `Pack` target
- **PackAsTool**: The user-facing global tool is `maui`; DevFlow functionality is exposed via `maui devflow`
- **IsShipping/IsPackable**: Default `false` in `Directory.Build.props`; shipped projects override to `true`
- **Signing**: `eng/Signing.props` configures Microsoft .NET certificate for first-party DLLs, `3PartySHA2` for third-party dependencies, `NuGet` certificate for `.nupkg` files
- **Version flow**: `eng/Versions.props` defines `VersionPrefix`/`VersionSuffix`, Arcade SDK applies them

## CI/CD

### GitHub Actions (PR validation)

- Workflow: `.github/workflows/ci-devflow.yml` → calls `_build.yml`
- **Matrix**: macOS + Windows
- **Path-filtered**: only triggers for changed product paths
- Steps: restore → build → test → upload test results + packages

### Azure DevOps (official builds)

- Pipeline: `eng/pipelines/devflow-official.yml`
- Builds, signs, and publishes to internal feeds via Maestro/DARC
- **MicroBuild signing** enabled (`enableMicrobuild: true`) — this enforces CFS network isolation
- NuGet.org publishing: separate pipeline (`eng/pipelines/release-publish-nuget.yml`) with `networkIsolationPolicy: Permissive`

### NuGet Feed Configuration

NuGet.config uses **internal dnceng proxy feeds only** — no direct nuget.org reference:
- `dotnet-public`, `dotnet-tools`, `dotnet-eng`, `dotnet10`, `dotnet11`

**Do not** add `nuget.org` as a direct feed source. Package versions flow via Dependency Flow (Maestro/DARC).

## Adding a New Product

1. Create `src/{NewProduct}/` with `Version.props`, project folders, test project, `{NewProduct}.slnf`
2. Add projects to `MauiLabs.sln`
3. Add package versions to `Directory.Packages.props`
4. Add path filter in `.github/workflows/ci.yml`
5. Add signing entries in `eng/Signing.props` for any new third-party DLLs

## DevFlow MCP Tools

DevFlow exposes 49 MCP tools for AI agent integration (in `src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp/Tools/`):

| Tool | Purpose |
|------|---------|
| `maui_list_agents` | List connected MAUI DevFlow agents (running apps) |
| `maui_select_agent` | Select a specific agent for subsequent commands |
| `maui_wait` | Wait for an agent to connect |
| `maui_status` | Agent connection status, platform, app name |
| `maui_tree` | Inspect visual tree — structured JSON hierarchy with IDs, types, bounds |
| `maui_query` | Query elements by type, AutomationId, or text |
| `maui_query_css` | Query elements by CSS selector |
| `maui_element` | Get full element details |
| `maui_hittest` | Find elements at screen coordinates |
| `maui_tap` | Tap a UI element |
| `maui_fill` | Fill text into Entry/Editor |
| `maui_clear` | Clear text from an element |
| `maui_scroll` | Scroll by delta, item index, or into view |
| `maui_focus` | Set focus to an element |
| `maui_navigate` | Shell navigation to a route |
| `maui_resize` | Resize the app window |
| `maui_screenshot` | Capture screenshot (page, element, or fullscreen) |
| `maui_assert` | Assert element property equals expected value |
| `maui_get_property` | Read any element property |
| `maui_set_property` | Live-edit element properties |
| `maui_logs` | Retrieve app logs (ILogger + WebView console) |
| `maui_network` | List captured HTTP requests |
| `maui_network_detail` | Full request/response details |
| `maui_network_clear` | Clear captured request buffer |
| `maui_cdp_evaluate` | Execute JavaScript in Blazor WebView via CDP |
| `maui_cdp_screenshot` | WebView screenshot via CDP |
| `maui_cdp_source` | Get WebView page source |
| `maui_cdp_webviews` | List available WebViews |
| `maui_recording_start` | Start screen recording |
| `maui_recording_stop` | Stop screen recording |
| `maui_recording_status` | Check recording status |
| `maui_sensors_list` | List available device sensors |
| `maui_sensors_start` | Start a sensor |
| `maui_sensors_stop` | Stop a sensor |
| `maui_app_info` | App name, version, package, theme |
| `maui_device_info` | Device manufacturer, model, OS |
| `maui_display_info` | Screen density, size, orientation |
| `maui_battery_info` | Battery level, state, power source |
| `maui_connectivity` | Network access and connection profiles |
| `maui_geolocation` | GPS coordinates |
| `maui_preferences_list` | List preference keys |
| `maui_preferences_get` | Read a preference value |
| `maui_preferences_set` | Write a preference value |
| `maui_preferences_delete` | Delete a preference |
| `maui_preferences_clear` | Clear all preferences |
| `maui_secure_storage_get` | Read secure storage value |
| `maui_secure_storage_set` | Write secure storage value |
| `maui_secure_storage_delete` | Delete secure storage entry |
| `maui_secure_storage_clear` | Clear all secure storage |

## Important Notes

- **`eng/common/` is auto-generated by Arcade SDK** — never modify files in this directory manually.
- **`AgentClient`** (in `Microsoft.Maui.DevFlow.Driver`) is the public API consumed by NuGet users. Method signature changes are **binary and source breaking** for consumers.
- The repo is at version **0.1.0-preview** — breaking changes are acceptable but should be documented.
- **Platform conditionals**: Use `#if IOS`, `#if ANDROID`, `#if MACCATALYST`, `#if MACOS`, `#if WINDOWS` for platform-specific code in multi-targeting projects.

## Skills Marketplace

This repository also distributes agent skills as a plugin under `plugins/dotnet-maui/`.

### Plugin Structure

```
plugins/<plugin-name>/
 plugin.json              # Plugin manifest (name, version, description, skills path)
 skills/
 <skill-name>/    
 SKILL.md         # Skill definition (required)        
 references/      # Supporting documentation (optional)        
```

### Skill Format

Each `SKILL.md` must have YAML frontmatter:

```yaml
---
name: skill-name
description: >-
  What this skill does. USE FOR: specific scenarios.
  DO NOT USE FOR: non-applicable contexts.
---
```

The `description` field is critical — agent runtimes read only the description to decide whether to activate the skill. Include explicit "USE FOR" and "DO NOT USE FOR" guidance.

### Adding a New Skill

See [plugins/CONTRIBUTING.md](plugins/CONTRIBUTING.md) for the full guide, including skill structure, SKILL.md format, evaluation tests, and the PR checklist.
