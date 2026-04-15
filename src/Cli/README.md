# Microsoft.Maui.Cli

A command-line tool for .NET MAUI development environment setup and device management.

> ⚠️ **Experimental** — APIs may change between releases. Not covered by the Microsoft Support Policy.

## Package

| Package | Description |
|---------|-------------|
| **Microsoft.Maui.Cli** | Global CLI tool (`maui`) for environment setup, device management, and diagnostics. |

## Quick Start

### 1. Install the CLI tool

```bash
dotnet tool install -g Microsoft.Maui.Cli
```

### 2. Check your environment

```bash
# Run diagnostics
maui doctor

# List connected devices
maui device list
```

### 3. Set up Android development

```bash
# Full interactive Android setup (JDK + SDK + emulator)
maui android install

# Manage Android SDK packages
maui android sdk list
maui android sdk install "platforms;android-35"

# Manage JDK installations
maui android jdk install

# Create and manage emulators
maui android emulator create --name MyEmulator
maui android emulator start --name MyEmulator
```

### 4. Set up Apple development (macOS only)

```bash
# List installed Xcode versions
maui apple xcode list

# List simulator runtimes
maui apple runtime list
maui apple runtime list --platform iOS

# Manage simulators
maui apple simulator list
maui apple simulator start "iPhone 16 Pro"
maui apple simulator stop "iPhone 16 Pro"
maui apple simulator delete "iPhone 16 Pro"
```

## Commands

| Command | Description |
|---------|-------------|
| `maui doctor` | Run environment diagnostics and auto-fix issues |
| `maui device list` | List connected devices and emulators |
| `maui version` | Display version information |
| `maui android install` | Full interactive Android environment setup |
| `maui android sdk list` | List available and installed Android SDK packages |
| `maui android sdk install` | Install Android SDK packages |
| `maui android jdk install` | Install and manage JDK versions |
| `maui android emulator create` | Create an Android emulator |
| `maui android emulator start` | Start an Android emulator |
| `maui android emulator stop` | Stop a running emulator |
| `maui android emulator delete` | Delete an emulator |
| `maui apple xcode list` | List installed Xcode versions (macOS only) |
| `maui apple runtime list` | List installed simulator runtimes (macOS only) |
| `maui apple simulator list` | List simulator devices (macOS only) |
| `maui apple simulator start` | Boot a simulator (macOS only) |
| `maui apple simulator stop` | Shut down a simulator (macOS only) |
| `maui apple simulator delete` | Delete a simulator (macOS only) |
| `maui devflow` | MAUI app automation via Agent API and Blazor WebViews via CDP |
| `maui devflow MAUI tree` | Dump the visual tree of a running MAUI app |
| `maui devflow MAUI screenshot` | Take a screenshot of a running MAUI app |
| `maui devflow cdp` | Blazor WebView automation via Chrome DevTools Protocol |
| `maui devflow mcp` | Start MCP server for AI agent integration |
| `maui devflow broker` | Manage the DevFlow agent broker |

## Global Options

| Option | Description |
|--------|-------------|
| `--json` | Output in JSON format (for scripting and CI) |
| `-v`, `--verbose` | Enable verbose output |
| `--dry-run` | Show what would be done without making changes |
| `--ci` | CI mode — non-interactive, fail fast on errors |

## Output Formats

The CLI supports two output modes:

- **Interactive** (default) — Rich Spectre.Console output with colors, tables, and progress bars
- **JSON** (`--json`) — Machine-readable JSON for scripting and CI pipelines

```bash
# Human-friendly output
maui doctor

# JSON output for scripting
maui doctor --json | jq '.checks[] | select(.status == "failed")'
```

## Platform Support

| Platform | Status | Notes |
|----------|--------|-------|
| macOS | ✅ | Full support including Apple commands (Xcode, simulators, runtimes) |
| Windows | ✅ | Android and Windows SDK commands |
| Linux | ✅ | Android commands |

## Development

```bash
# Open just the CLI in your IDE
open src/Cli/Cli.slnf

# Build
dotnet build src/Cli/Cli.slnf

# Run tests
dotnet test src/Cli/Microsoft.Maui.Cli.UnitTests/Microsoft.Maui.Cli.UnitTests.csproj

# Run locally without installing
dotnet run --project src/Cli/Microsoft.Maui.Cli/ -- doctor
```
