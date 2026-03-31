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

| Platform | Status |
|----------|--------|
| macOS | ✅ |
| Windows | ✅ |
| Linux | ✅ |

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
