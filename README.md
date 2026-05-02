# .NET MAUI Labs

Experimental packages and tooling for .NET MAUI. This repository hosts pre-release projects that are in active development and may ship independently.

> ⚠️ **These packages are experimental.** APIs may change between releases. These packages are not covered by the [.NET MAUI Support Policy](https://dotnet.microsoft.com/platform/support/policy/maui) and are provided as-is.

## Products

### Cli

A command-line tool for .NET MAUI development environment setup, device management, and app automation.

- **Environment diagnostics** (`maui doctor`) with auto-fix capabilities
- **Android SDK and JDK management** (`maui android`) — install, update, and configure
- **Emulator management** (`maui android emulator`) — create, start, stop, and delete Android emulators
- **Apple platform management** (`maui apple`) — Xcode, simulator, and runtime management (macOS)
- **Device listing** (`maui device list`) across all connected platforms
- **DevFlow app automation** (`maui devflow`) — visual tree inspection, element interaction, screenshots, WebView/CDP automation, network monitoring, profiling, storage access, real-time log/sensor streaming, and MCP server for AI agents
- **MAUI Go** (`maui go`) — create, serve, and upgrade single-file Comet Go projects for rapid prototyping
- **Version info** (`maui version`)
- **Global options** — `--json` for CI pipelines, `--verbose`, `--dry-run`, `--ci`

| Package | Description |
|---------|-------------|
| `Microsoft.Maui.Cli` | CLI global tool (`maui`) |

```bash
# Microsoft.Maui.Cli is currently released as a pre-release, so make sure to use the --prerelease flag
dotnet tool install -g Microsoft.Maui.Cli --prerelease
maui doctor
```

### Comet

Experimental MVU UI framework for .NET MAUI — C# fluent UI, signals/reactive state, single-file apps via Comet Go.

| Package | Description |
|---------|-------------|
| `Comet` | Core MVU framework |
| `Comet.SourceGenerator` | Roslyn source generators for Comet |
| `Comet.Layout.Yoga` | Yoga layout integration |

### Go

Single-file Comet apps server + companion app for rapid prototyping (alpha; sister to Comet).

| Package | Description |
|---------|-------------|
| `Microsoft.Maui.Go.Server` | Comet Go server for hosting single-file apps |

### DevFlow

A comprehensive MAUI testing, automation, and debugging toolkit. The DevFlow CLI is integrated into the `maui` CLI as `maui devflow` — see [Cli](#cli) above.

- **In-app HTTP agent** for visual tree inspection, element interaction, and screenshots
- **Blazor CDP bridge** for Chrome DevTools Protocol on Blazor WebViews
- **MCP server** for AI agent integration (via `maui devflow mcp`)
- **Platform drivers** for iOS, Android, Mac Catalyst, Windows, and Linux/GTK
- **Network monitoring** and **performance profiling**
- **Real-time streaming** — WebSocket channels for logs, network requests, sensor data, profiler samples, and UI events
- **Storage access** — read/write app preferences and secure storage
- **Device introspection** — battery, connectivity, geolocation, display info, and permissions

| Package | Description |
|---------|-------------|
| `Microsoft.Maui.DevFlow.Agent` | In-app agent for MAUI automation |
| `Microsoft.Maui.DevFlow.Agent.Core` | Platform-agnostic agent core |
| `Microsoft.Maui.DevFlow.Agent.Gtk` | GTK/Linux agent |
| `Microsoft.Maui.DevFlow.Blazor` | Blazor WebView CDP bridge |
| `Microsoft.Maui.DevFlow.Blazor.Gtk` | WebKitGTK CDP bridge |
| `Microsoft.Maui.DevFlow.Driver` | Platform driver library |
| `Microsoft.Maui.DevFlow.Logging` | Buffered JSONL file logger |

### Essentials.AI

On-device AI capabilities for .NET MAUI via `Microsoft.Extensions.AI` abstractions. On Apple platforms, wraps Apple Intelligence (Foundation Models) for chat completion with streaming and tool calling, and Apple NaturalLanguage APIs for on-device embeddings.

- **`IChatClient`** backed by Apple Intelligence on iOS, macOS, and Mac Catalyst
- **Streaming infrastructure** — progressive JSON deserialization of LLM responses
- **NL embeddings** — on-device semantic search via Apple's NaturalLanguage framework (`NLEmbeddingGenerator`)
- **Tool calling** — function-calling support for on-device models

| Package | Description |
|---------|-------------|
| `Microsoft.Maui.Essentials.AI` | On-device AI APIs for MAUI |

### AppProjectReference

An MSBuild package that lets test projects, packaging projects, or CI tools declare a MAUI app as a build-time dependency and consume its platform artifacts (`.apk`, `.ipa`, `.app`, `.msix`) as MSBuild items with rich metadata.

```xml
<MauiAppProjectReference Include="..\MyApp\MyApp.csproj" />
```

Built artifacts are exposed as `@(MauiAppArtifact)` items with `ArtifactType`, `ApplicationId`, `Installable`, `Launchable`, and other metadata — no manual path hunting required.

| Package | Description |
|---------|-------------|
| `Microsoft.Maui.Build.AppProjectReference` | Build-time app project reference with artifact discovery |

## Getting Started

See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions and development setup.

For the formal DevFlow HTTP and WebSocket contract, see [`docs/DevFlow/spec`](docs/DevFlow/spec/README.md).

## Agent Skills

This repository is also a marketplace for distributable agent skills for .NET MAUI development. Skills are organized as plugins compatible with Copilot CLI, Claude Code, and VS Code.

| Plugin | Description |
|--------|-------------|
| [`dotnet-maui`](plugins/dotnet-maui/) | MAUI development: DevFlow automation, profiling, accessibility, platform bindings, diagnostics |

```bash
# Install via Copilot CLI
/plugin marketplace add dotnet/maui-labs
/plugin install dotnet-maui@dotnet-maui-labs
```

See [plugins/](plugins/) for the full catalog and [plugins/CONTRIBUTING.md](plugins/CONTRIBUTING.md) for how to add skills.

## Support

See [SUPPORT.md](.github/SUPPORT.md) for how to file issues, get help, and the support policy for this repository.
