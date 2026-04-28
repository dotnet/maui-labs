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
| **Android** | |
| `maui android install` | Full interactive Android environment setup |
| `maui android sdk list` | List available and installed Android SDK packages |
| `maui android sdk install` | Install Android SDK packages |
| `maui android sdk check` | Check Android SDK installation status |
| `maui android sdk uninstall` | Uninstall Android SDK packages |
| `maui android sdk accept-licenses` | Accept Android SDK licenses interactively |
| `maui android jdk install` | Install and manage JDK versions |
| `maui android jdk check` | Check JDK installation status |
| `maui android jdk list` | List available JDK versions |
| `maui android emulator create` | Create an Android emulator |
| `maui android emulator start` | Start an Android emulator |
| `maui android emulator stop` | Stop a running emulator |
| `maui android emulator delete` | Delete an emulator |
| `maui android emulator list` | List available emulators |
| **Apple (macOS only)** | |
| `maui apple xcode list` | List installed Xcode versions |
| `maui apple runtime list` | List installed simulator runtimes |
| `maui apple simulator list` | List simulator devices |
| `maui apple simulator start` | Boot a simulator |
| `maui apple simulator stop` | Shut down a simulator |
| `maui apple simulator delete` | Delete a simulator |
| **DevFlow** | |
| `maui devflow init` | Install project-scoped DevFlow onboarding/debugging skills |
| `maui devflow skills` | Manage bundled DevFlow skill installs and updates |
| `maui devflow ui` | Visual tree inspection, interaction, and screenshots |
| `maui devflow recording` | Manage UI recording sessions (start, stop, status) |
| `maui devflow webview` | Blazor WebView automation via Chrome DevTools Protocol |
| `maui devflow logs` | Fetch and stream application logs |
| `maui devflow network` | Monitor HTTP network requests |
| `maui devflow storage` | Access app preferences, secure storage, discover file storage roots, and manage sandboxed app files |
| `maui devflow agent` | Discover and inspect connected DevFlow agents |
| `maui devflow broker` | Manage the DevFlow agent broker (start, stop, status, log) |
| `maui devflow batch` | Execute commands from stdin for scripting |
| `maui devflow commands` | List all available commands (schema discovery) |
| `maui devflow diagnose` | Check DevFlow agent health |
| `maui devflow wait` | Wait for an agent to connect |
| `maui devflow mcp` | Start the MCP server for AI agent integration |

Run `maui <command> --help` for detailed options on any command.

DevFlow file commands can use local files directly:

```bash
# Upload local bytes into the selected app storage root
maui devflow storage files upload logs/app.log --file ./app.log

# Download to a directory, preserving the device file name
maui devflow storage files download logs/app.log --output ./downloads/

# Download to an explicit local file name
maui devflow storage files download logs/app.log --output ./downloads/app-copy.log
```

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
