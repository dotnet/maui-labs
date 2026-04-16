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

## Getting Started

See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions and development setup.

For the formal DevFlow HTTP and WebSocket contract, see [`docs/DevFlow/spec`](docs/DevFlow/spec/README.md).

## Support

See [SUPPORT.md](.github/SUPPORT.md) for how to file issues, get help, and the support policy for this repository.
