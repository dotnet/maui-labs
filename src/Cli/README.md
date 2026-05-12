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
| **Profiling** | |
| `maui profile startup` | Collect a startup trace for a .NET MAUI app (.nettrace, speedscope, or MIBC output) |
| **Go** | |
| `maui go create` | Create a new MAUI Go single-file project |
| `maui go serve` | Start the dev server with hot reload |
| `maui go upgrade` | Graduate a Go project to a full MAUI project |

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

### `--json` flag

Most commands accept `--json` for structured output. Some commands may emit multiple JSON objects to stdout (JSONL / newline-delimited JSON) — for example, progress or status records before the final result. The final JSON object is the command result, whose shape is command-specific (see per-command `--help` and examples below). When a non-DevFlow command **fails** and the exception is handled by `HandleCommandException`, it emits the canonical error envelope described in the next section. This applies to both recognized `MauiToolException` errors (which produce specific error codes) and unexpected exceptions (which become `E1001`/`InternalError` via `ErrorResult.FromException`). Note: unrecognized parse errors (e.g. invalid flags) do not use this envelope, `OperationCanceledException` produces a status message with exit code 130 rather than the error envelope, and `maui devflow ...` uses a different JSON contract and writes structured errors to stderr rather than stdout.

Use `--ci` together with `--json` for non-interactive, fail-fast runs in automation contexts. Parse the output as a stream of JSON objects rather than assuming a single top-level document.

### Error envelope <a name="error-envelope"></a>

For non-DevFlow `maui` commands, when a command throws an exception that is handled by `HandleCommandException` and `--json` is active, it writes a structured error object to stdout. The fields appear at the **top level** — there is no enclosing `"error"` wrapper. Property names are `snake_case`. Note: some commands may return non-zero exit codes without throwing (e.g. validation paths), and `OperationCanceledException` is treated as a cancellation (exit code 130) rather than an error envelope. This section does not apply to `maui devflow ...`, which uses a different JSON error shape and writes structured errors to stderr.

```json
{
  "code": "E2106",          // stable error code — see the error code table below
  "category": "platform",   // tool | platform | user | network | permission
  "severity": "error",      // always "error" today (info | warning reserved for future use)
  "message": "Android emulator not installed",

  // OPTIONAL — omitted entirely when null (never serialized as JSON null)

  "native_error": "...",    // raw error text from the underlying tool, when available
  "context": { ... },       // command-specific diagnostics bag
  "remediation": {
    "type": "autofixable",              // autofixable | useraction | terminal | unknown
    "command": "maui android sdk install emulator",  // present when type == autofixable
    "manual_steps": ["...", "..."]      // present when type == useraction
  },
  "docs_url": "https://...",          // reserved — not yet populated by any command
  "correlation_id": "..."
}
```

**Contract guarantees** (verified line-by-line against `Models/ErrorResult.cs`):

- Property names are `snake_case` — enforced by `[JsonPropertyName]` attributes on `ErrorResult`.
- `remediation.type` values are **lowercase** strings — serialized via `.ToString().ToLowerInvariant()`.
- Optional fields (`native_error`, `context`, `remediation`, `docs_url`, `correlation_id`) are **omitted entirely** when null — enforced by `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`.
- There is **no** outer `"error"` wrapper — all fields are top-level.
- `code`, `category`, `severity`, and `message` are always present.
- The CLI intends to keep this envelope shape consistent; future changes are expected to be additive (e.g. new optional fields) rather than changing existing field names or types.

### Error code categories

| Prefix | `category` value | Examples |
|--------|-----------------|---------|
| `E1xxx` | `tool` | `E1001` InternalError, `E1004` InvalidArgument, `E1006` DeviceNotFound, `E1007` PlatformNotSupported |
| `E20xx` | `platform` | `E2001` JdkNotFound, `E2002` JdkVersionUnsupported, `E2003` JdkInstallFailed |
| `E21xx` | `platform` | `E2101` AndroidSdkNotFound, `E2102` AndroidSdkManagerNotFound, `E2103` AndroidLicensesNotAccepted, `E2105` AndroidPackageInstallFailed, `E2106` AndroidEmulatorNotFound, `E2108` AndroidAvdCreateFailed, `E2110` AndroidAdbNotFound, `E2111` AndroidDeviceNotFound, `E2112` AndroidAvdDeleteFailed |
| `E22xx` | `platform` | `E2201` AppleXcodeNotFound, `E2202` AppleCltNotFound, `E2203` AppleSimctlFailed, `E2204` AppleSimulatorNotFound, `E2205` AppleXcodeLicenseNotAccepted, `E2206` AppleSetupFailed, `E2207` AppleSimulatorCreateFailed, `E2208` AppleSimulatorEraseFailed |
| `E23xx` | `platform` | `E2301` WindowsSdkNotFound |
| `E24xx` | `platform` | `E2401` DotNetNotFound, `E2402` MauiWorkloadMissing, `E2403` DiagnosticsToolNotFound |
| `E3xxx` | `user` | User action required (wrong arguments, missing inputs) |
| `E4xxx` | `network` | Download / connectivity failures |
| `E5xxx` | `permission` | Privacy / OS permission issues |

A `maui errors list` command is planned (issue [#197](https://github.com/dotnet/maui-labs/issues/197)) to expose this catalogue at runtime. The table above is the authoritative list until then.

### Consuming the error envelope

**Bash / jq:**

```bash
if ! out=$(maui android sdk install emulator --json 2>&1); then
  # Output may contain multiple JSONL lines; extract the error envelope (has a "code" field)
  err=$(echo "$out" | jq -s '[.[] | select(.code)] | last')
  rem_type=$(echo "$err" | jq -r '.remediation.type // "unknown"')
  rem_cmd=$(echo "$err" | jq -r '.remediation.command // empty')
  if [[ "$rem_type" == "autofixable" && -n "$rem_cmd" ]]; then
    # Run the remediation command directly (never pass untrusted input to eval)
    $rem_cmd
  fi
fi
```

**PowerShell:**

```powershell
$lines = maui android sdk install emulator --json
if ($LASTEXITCODE -ne 0) {
    # Output may contain multiple JSONL lines; pick the error envelope (has a "code" property)
    $err = $lines | ForEach-Object { $_ | ConvertFrom-Json } |
           Where-Object { $_.code } | Select-Object -Last 1
    if ($err.remediation.type -eq 'autofixable' -and $err.remediation.command) {
        # Split and invoke directly (avoid Invoke-Expression with untrusted input)
        $parts = $err.remediation.command -split ' '
        & $parts[0] $parts[1..($parts.Length-1)]
    }
}
```

### Worked example: `E2106` AndroidEmulatorNotFound

`maui android emulator start <name>` emits `E2106` with an `autofixable` remediation when the Android emulator binary is not installed:

```bash
maui android emulator start Pixel8 --json
# → { "code": "E2106",
#     "category": "platform",
#     "severity": "error",
#     "message": "Android emulator not installed",
#     "remediation": { "type": "autofixable",
#                      "command": "maui android sdk install emulator" } }

# Auto-fix path:
maui android sdk install emulator --json
maui android emulator start Pixel8 --json   # retry original
```

Other `E2106` throw sites (e.g., "no AVD with that name") emit the same code **without** a `remediation` block — surface `message` and stop retrying.

> For agent-facing usage examples and remediation patterns used by AI coding agents, see
> [`plugins/dotnet-maui/skills/maui-devflow-debug/references/troubleshooting.md`](../../plugins/dotnet-maui/skills/maui-devflow-debug/references/troubleshooting.md).

## Platform Support

| Platform | Status | Notes |
|----------|--------|-------|
| macOS | ✅ | Full support including Apple commands (Xcode, simulators, runtimes) |
| Windows | ✅ | Android SDK, JDK, and emulator commands |
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
