# .NET MAUI Labs

Experimental packages and tooling for .NET MAUI. This repository hosts pre-release projects that are in active development and may ship independently.

> ⚠️ **These packages are experimental.** APIs may change between releases. These packages are not covered by the [.NET MAUI Support Policy](https://dotnet.microsoft.com/platform/support/policy/maui) and are provided as-is.

## Products

### Cli

A command-line tool for .NET MAUI development environment setup, device management, and app automation.

- **Environment diagnostics** (`maui doctor`) with auto-fix capabilities
- **Android SDK and JDK management** — install, update, and configure
- **Emulator management** — create, start, stop, and delete Android emulators
- **Device listing** across all connected platforms
- **DevFlow app automation** (`maui devflow`) — visual tree inspection, screenshots, CDP, MCP server
- **JSON output** (`--json`) for CI pipelines and scripting

| Package | Description |
|---------|-------------|
| `Microsoft.Maui.Cli` | CLI global tool (`maui`) |

```bash
dotnet tool install -g Microsoft.Maui.Cli
maui doctor
```

### DevFlow

A comprehensive MAUI testing, automation, and debugging toolkit. The DevFlow CLI is integrated into the `maui` CLI as `maui devflow` — see [Cli](#cli) above.

- **In-app HTTP agent** for visual tree inspection, element interaction, and screenshots
- **Blazor CDP bridge** for Chrome DevTools Protocol on Blazor WebViews
- **MCP server** for AI agent integration (via `maui devflow mcp`)
- **Platform drivers** for iOS, Android, Mac Catalyst, Windows, and Linux/GTK
- **Network monitoring** and **performance profiling**

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

## Support

See [SUPPORT.md](.github/SUPPORT.md) for how to file issues, get help, and the support policy for this repository.
