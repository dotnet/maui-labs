---
name: maui-ai-debugging
description: >-
  Legacy end-to-end workflow for building, deploying, inspecting, and debugging
  .NET MAUI and MAUI Blazor Hybrid apps with `maui devflow`. USE FOR: older
  clients that still request `maui-ai-debugging`, app launch, visual tree
  inspection, screenshots, Blazor WebView CDP debugging, simulator/emulator
  management, and build-deploy-inspect-fix loops. DO NOT USE FOR: new DevFlow
  setup when `maui-devflow-onboard` or `maui-devflow-debug` are available,
  generic desktop automation, AppleScript macros, or host-level xdotool control
  unrelated to MAUI app debugging.
---

# MAUI AI Debugging

Compatibility note: this legacy skill name is retained for older clients. The canonical
replacement is `maui-devflow-debug`, installed through `maui devflow init` /
`maui devflow skills update`.

Build, deploy, inspect, and debug .NET MAUI apps from the terminal. This skill enables a complete
feedback loop: **build → deploy → inspect → fix → rebuild**.

## Prerequisites

```bash
dotnet tool install --global Microsoft.Maui.Cli --prerelease || dotnet tool update --global Microsoft.Maui.Cli --prerelease
dotnet tool install --global androidsdk.tool    # Android only
dotnet tool install --global appledev.tools     # iOS/Mac only
```

Keep DevFlow skills up to date with `maui devflow skills update`. The hidden
`maui devflow update-skill` compatibility command runs the same bundled updater
and may remove this legacy skill after installing current replacements.

## Integrating MauiDevFlow into a MAUI App

For complete setup instructions, see [references/setup.md](references/setup.md).

**Quick summary:**
1. Add NuGet packages (`Microsoft.Maui.DevFlow.Agent`, and `Microsoft.Maui.DevFlow.Blazor` for Blazor Hybrid)
   - For **Linux/GTK apps** (detected via `grep -i 'GirCore\|Maui\.Gtk' *.csproj`), use `Agent.Gtk` and `Blazor.Gtk` instead
   - For **macOS (AppKit) apps** (detected via `grep -i 'Platform\.Maui\.MacOS' *.csproj`), the standard `Agent` and `Blazor` packages include macOS support
2. Register in `MauiProgram.cs` inside `#if DEBUG`
3. For Blazor Hybrid: chobitsu.js is auto-injected (no manual script tag needed)
4. For Mac Catalyst: ensure `network.server` entitlement
5. For Android: run `adb reverse` for broker + agent ports
6. For Linux: no special network setup needed (direct localhost)
7. For macOS (AppKit): separate app head project, uses `open App.app` to launch. See [references/macos.md](references/macos.md)

## Core Workflow

### 0. Verify DevFlow Availability

Before building or launching anything, determine if DevFlow is available for the current project.

**Check integration (project files — the source of truth):**
```bash
# Check if any csproj in the project has DevFlow packages
grep -rl "MauiDevFlow\|Maui\.DevFlow" --include="*.csproj" .
```

If grep returns results, DevFlow IS integrated — even if `maui devflow list` shows nothing.

**Check runtime connection:**
```bash
maui devflow list                          # shows connected agents
maui devflow broker status                 # shows broker health
maui devflow diagnose                      # full end-to-end health check (recommended)
```

**Decision tree — what to do based on results:**

| Project has DevFlow packages? | Agent in `list`? | Action |
|-------------------------------|------------------|--------|
| ✅ Yes | ✅ Yes | Ready — proceed to inspection/interaction |
| ✅ Yes | ❌ No | App not running in Debug, or broker issue. Launch app, then `maui devflow wait` |
| ❌ No | — | Need to integrate DevFlow (see "Integrating MauiDevFlow into a MAUI App") |

**⚠️ CRITICAL:** `maui devflow list` shows RUNTIME state (connected agents), NOT project integration.
An empty list does NOT mean "DevFlow is not installed." Always check project files first.

**After launching the app (via `dotnet build -t:Run` or through Aspire):**
```bash
maui devflow wait                          # blocks until agent connects (default 120s)
maui devflow wait --project path/to/App.csproj  # filter to specific project
```
ALWAYS run `wait` after launching. Never assume the agent is connected — verify it.

### 1. Ensure a Device/Simulator/Emulator is Running

**⚠️ Multi-project conflict avoidance:** When multiple projects may run simultaneously
(common with AI agents), each project should use its own dedicated simulator/emulator to
prevent apps from replacing each other. Check what's already in use first:

```bash
maui devflow list     # check if any agents are already connected (runtime state only — see Step 0 for integration check)
```

If another iOS or Android agent is already registered, **create a new simulator/emulator**
for your project instead of reusing the one that's already booted.

**iOS Simulator:**
```bash
xcrun simctl list devices booted                              # check booted sims

# Create a project-dedicated simulator to avoid conflicts
xcrun simctl create "MyApp-iPhone17Pro" "iPhone 17 Pro" "iOS 26.2"
xcrun simctl boot <UDID>                                      # boot the new sim
```

**Android Emulator:**
```bash
android avd list                                              # list AVDs

# Create a project-dedicated emulator to avoid conflicts
android avd create --name "MyApp-Pixel8" \
  --sdk "system-images;android-35;google_apis;arm64-v8a" --device pixel_8
android avd start --name "MyApp-Pixel8"
```

**Mac Catalyst / macOS (AppKit) / Linux/GTK:** No device setup needed — runs as desktop app.
Multiple desktop apps can run simultaneously without conflicts.

### 2. Detect the TFM

**IMPORTANT:** Before building, detect the correct Target Framework Moniker from the project.
Do NOT assume `net10.0` — many projects use `net9.0`, `net8.0`, etc.

```bash
grep -i 'TargetFrameworks' *.csproj Directory.Build.props 2>/dev/null
```

Use the detected version (e.g. `net9.0`) in all build commands. The examples use `$TFM`.

### 3. Build, Deploy, and Connect

Follow these steps for every launch and rebuild.

**Step 1: Kill any previous instance** (skip on first launch).
A stale app's agent stays registered with the broker, causing `maui devflow wait` to return
the old port instantly instead of waiting for the new build.

```bash
# Stop the async shell from the previous launch, then confirm:
maui devflow list                 # should show no agents (or only unrelated ones)
```

**Step 2: Launch in an async shell.**

```bash
# iOS Simulator
dotnet build -f $TFM-ios -t:Run -p:_DeviceName=:v2:udid=<UDID>

# Android Emulator
dotnet build -f $TFM-android -t:Run

# Mac Catalyst
dotnet build -f $TFM-maccatalyst -t:Run

# macOS AppKit — build exits after compiling; launch separately
dotnet build -f $TFM-macos <path-to-macos-project>
open path/to/bin/Debug/$TFM-macos/osx-arm64/AppName.app

# Linux/GTK
dotnet run --project <path-to-gtk-project>
```

**⚠️ Process lifecycle rules:**
- `dotnet build -t:Run` (iOS, Android, Mac Catalyst) and `dotnet run` (Linux/GTK) **block
  for the lifetime of the app**. Killing or stopping the shell **kills the app**. Use
  `mode: "async"` with `initial_wait: 120` and do NOT stop the shell until you are done.
- **macOS (AppKit)** is the exception: `dotnet build` exits after compiling, and `open`
  launches the app independently — the app survives shell termination.

**Step 3: Wait for the agent** — never use `sleep`.

```bash
maui devflow wait                                # blocks until agent registers (default 120s)
maui devflow wait --project path/to/App.csproj   # filter to specific project
```

`maui devflow wait` prints the assigned port as soon as the agent connects. Exit code 1
means timeout. If `wait` times out, run `maui devflow diagnose` to identify the issue.
Check async shell output for build errors.

**Android only** — set up port forwarding after the agent connects:
```bash
adb reverse tcp:19223 tcp:19223   # Broker (lets agent in emulator reach host broker)
adb forward tcp:<port> tcp:<port> # Agent (lets CLI reach agent in emulator)
```

**To rebuild:** repeat from Step 1. See [references/troubleshooting.md](references/troubleshooting.md)
if the build fails.

### 4. Inspect and Interact

**Typical inspection flow:**
1. `maui devflow ui tree --depth 15 --fields "id,type,text,automationId"` — tree with key fields only (depth 15 reaches most controls)
2. `maui devflow ui tree --window 1` — filter to a specific window (0-based index)
3. `maui devflow ui query --automationId "MyButton"` — find specific elements
4. `maui devflow ui query --type Entry --fields "id,text,automationId"` — all Entry fields with specific fields
5. `maui devflow ui element <id>` — get full details (type, bounds, visibility, children)
6. `maui devflow ui property <id> Text` — read any property by name
7. `maui devflow ui screenshot --output screen.png` — visual verification (auto-scaled to 1x on HiDPI)
8. `maui devflow ui screenshot --id <elementId> --output el.png` — element-only screenshot
9. `maui devflow ui screenshot --selector "Button" --output btn.png` — screenshot by CSS selector

**Property inspection** is more reliable than screenshots for verifying exact runtime values:
```bash
maui devflow ui property <id> BackgroundColor    # verify dark mode colors
maui devflow ui property <id> IsVisible          # check element visibility
```

**Live editing (no rebuild needed):**
```bash
maui devflow ui set-property <id> TextColor "Tomato"
maui devflow ui set-property <id> FontSize "24"
```
Supports: string, bool, int, double, Color (named/hex), Thickness, enums. Changes persist
until the app restarts — safe for experimentation.

**Typical interaction flow:**
1. `maui devflow ui fill --automationId "MyEntry" "text"` — type into Entry/Editor fields (no query needed)
2. `maui devflow ui tap --automationId "MyButton"` — tap buttons, checkboxes, list items
3. `maui devflow ui clear --automationId "MyEntry"` — clear text fields
4. Or use element IDs from tree/query: `maui devflow ui tap <elementId>`
5. Take screenshot to verify result, or use `--and-screenshot` on the action

**Blazor WebView (if applicable):**
1. `maui devflow webview snapshot` — DOM tree as accessible text (best for AI)
2. `maui devflow webview Input fill "css-selector" "text"` — fill inputs
3. `maui devflow webview Input dispatchClickEvent "css-selector"` — click elements
4. `maui devflow webview Runtime evaluate "js-expression"` — run JS

**Multiple BlazorWebViews:** If the app has more than one `BlazorWebView`, each is
registered independently with its `AutomationId`. Use `webview webviews` to list them,
then target a specific one with `--webview` (or `-w`):

```bash
maui devflow webview webviews                                  # list all WebViews
maui devflow webview -w BlazorLeft snapshot                    # snapshot of a specific WebView
maui devflow webview -w 1 Runtime evaluate "document.title"    # target by index
```

Without `--webview`, commands target the first (index 0) WebView.

**Live CSS/DOM editing in Blazor (no rebuild needed):**
```bash
maui devflow webview Runtime evaluate "document.querySelector('h1').style.color = 'tomato'"
maui devflow webview Runtime evaluate "document.documentElement.style.setProperty('--bg-color', '#1a1a2e')"
```

### 5. Reading Application Logs

MauiDevFlow automatically captures all `ILogger` output and WebView `console.*` calls
to rotating log files, retrievable remotely:

```bash
maui devflow ui logs                   # fetch 100 most recent log entries
maui devflow ui logs --limit 50        # fetch 50 entries
maui devflow ui logs --source webview  # only WebView/Blazor console logs
maui devflow ui logs --source native   # only native ILogger logs
maui devflow ui logs --follow          # stream logs in real-time (Ctrl+C to stop)
maui devflow ui logs -f --source native  # stream only native logs
maui devflow ui logs -f --json         # stream as JSONL (machine-readable)
```

**Debugging workflow:** Reproduce the issue → `maui devflow ui logs --limit 20` → check for
errors. Add temporary `ILogger` calls for more detail, rebuild, reproduce, and fetch logs again.

### 6. Screen Recording

Capture video of the app while performing interactions. Recording is host-side (not in-app)
using platform-native tools.

```bash
# Start recording (default 30s timeout)
maui devflow recording start --output demo.mp4

# Interact with the app
maui devflow ui tap <buttonId>
maui devflow ui navigate "//blazor"
maui devflow ui fill <entryId> "Hello World"

# Stop and save
maui devflow recording stop
```

**Platform tools used automatically by MauiDevFlow's recording backend** (implementation detail —
do not invoke these directly unless the user explicitly asked for host-level recording):
- **Android:** `adb screenrecord` (max 180s, capped with warning)
- **iOS Simulator:** `xcrun simctl io recordVideo`
- **Mac Catalyst / macOS (AppKit):** `screencapture -v` (targets app window when possible)
- **Windows/Linux:** `ffmpeg` (must be on PATH)

**Options:** `--timeout <seconds>` (default 30), `--output <path>` (default `recording_<timestamp>.mp4`).
Only one recording at a time — stop before starting a new one.

### 7. Network Request Monitoring

Monitor HTTP requests made by the app in real-time. MauiDevFlow automatically intercepts
all `IHttpClientFactory`-based HTTP traffic via a `DelegatingHandler` — no app code changes
needed beyond the standard `AddMauiDevFlowAgent()` setup.

```bash
# Live monitor — streams requests as they happen (Ctrl+C to stop)
maui devflow network

# JSONL streaming — machine-readable, one JSON object per line
maui devflow network --json

# One-shot: list recent captured requests
maui devflow network list

# Filter by method or host
maui devflow network list --method POST
maui devflow network list --host api.example.com

# Full request/response details (headers + body)
maui devflow network detail <requestId>

# Clear captured requests
maui devflow network clear
```

**How it works:**
- A `DelegatingHandler` wraps the platform's HTTP handler (AndroidMessageHandler,
  NSUrlSessionHandler, etc.), capturing request/response metadata, headers, and bodies
- Auto-injected via `ConfigureHttpClientDefaults` — works for all `IHttpClientFactory` clients
- For `new HttpClient()` outside DI, use `DevFlowHttp.CreateClient()` helper
- Bodies up to 256KB are captured (configurable via `AgentOptions.MaxNetworkBodySize`)
- A ring buffer (default 500 entries) stores recent requests in-memory

**JSONL output** is ideal for AI parsing — pipe to `jq` or process programmatically:
```bash
maui devflow network --json | jq 'select(.statusCode >= 400)'
```

**WebSocket streaming:** The live monitor uses WebSocket (`/ws/network`) for real-time push.
Connecting clients receive a replay of buffered history, then live entries as they arrive.

### 8. App Storage (Preferences & Secure Storage)

Read, write, and delete app preferences and secure storage entries remotely. Useful for
debugging state, resetting app configuration, or injecting test values.

```bash
# Preferences (typed key-value store)
maui devflow preferences list                       # list all known keys
maui devflow preferences get theme_mode             # get a string value
maui devflow preferences get counter --type int     # get a typed value
maui devflow preferences set api_url "https://dev.example.com"
maui devflow preferences set dark_mode true --type bool
maui devflow preferences delete temp_key
maui devflow preferences clear                      # clear all

# Shared preferences containers
maui devflow preferences list --sharedName settings
maui devflow preferences set key val --sharedName settings

# Secure Storage (encrypted, string values only)
maui devflow secure-storage get auth_token
maui devflow secure-storage set auth_token "eyJhbGc..."
maui devflow secure-storage delete auth_token
maui devflow secure-storage clear
```

**Note:** Preference key listing uses an internal registry (keys set via the agent are tracked).
Keys set directly in app code won't appear in `list` unless also set via the agent.

### 9. Platform Info & Device Features

Query read-only device and app state. These are one-shot snapshot reads.

```bash
maui devflow platform app-info         # app name, version, build, theme
maui devflow platform device-info      # manufacturer, model, OS, idiom
maui devflow platform display          # screen density, size, orientation
maui devflow platform battery          # charge level, state, power source
maui devflow platform connectivity     # WiFi/Cellular/Ethernet, network access
maui devflow platform version-tracking # version history, first launch detection
maui devflow platform permissions      # check all common permission statuses
maui devflow platform permissions camera  # check a specific permission
maui devflow platform geolocation      # current GPS coordinates
maui devflow platform geolocation --accuracy High --timeout 15
```

### 10. Device Sensors

Start, stop, and stream real-time sensor data. Sensors auto-start when streaming.

```bash
maui devflow sensors list                    # list sensors + status
maui devflow sensors start accelerometer     # start a sensor
maui devflow sensors stop accelerometer

# Stream readings to stdout (JSONL)
maui devflow sensors stream accelerometer          # Ctrl+C to stop
maui devflow sensors stream gyroscope --speed Game  # higher frequency
maui devflow sensors stream compass --duration 10  # stop after 10 seconds
```

Available sensors: `accelerometer`, `barometer`, `compass`, `gyroscope`, `magnetometer`, `orientation`.
Speed options: `UI` (default), `Game`, `Fastest`, `Default`.

**WebSocket streaming:** Sensor data uses WebSocket (`/ws/sensors?sensor=<name>`) for
real-time push. Each reading is a JSON object with `sensor`, `timestamp`, and `data` fields.

## Command Reference

### maui devflow ui (Native Agent)

Global options (work on any subcommand):
- `--agent-host` (default localhost), `--agent-port` (auto-discovered via broker), `--platform`
- `--json` — force JSON output. Auto-enabled when stdout is piped/redirected (TTY auto-detection).
- `--no-json` — force human-readable output even when piped.
- Env var: `MAUIDEVFLOW_OUTPUT=json` for persistent JSON mode.

**Implicit element resolution:** Commands that take an `<elementId>` (tap, fill, clear, focus)
also accept `--automationId`, `--type`, `--text`, `--index` to resolve the element in a single
call. This eliminates the query→act round-trip. The `<elementId>` argument is optional when
resolution options are provided.

**Post-action flags:** tap, fill, clear accept `--and-screenshot [path]`, `--and-tree`,
`--and-tree-depth N` to return verification data alongside the action result.

| Command | Description |
|---------|-------------|
| `ui status [--window W]` | Agent connection status, platform, app name, window count |
| `ui tree [--depth N] [--window W] [--fields F] [--format compact]` | Visual tree. `--fields "id,type,text"` projects specific fields. `--format compact` returns only id, type, text, automationId, bounds |
| `ui query [--type T] [--automationId A] [--text T] [--selector S] [--fields F] [--format compact] [--wait-until exists\|gone] [--timeout N]` | Find elements. `--wait-until` polls until condition met (default 30s timeout). `--fields` and `--format` same as tree |
| `ui hittest <x> <y> [--window W]` | Find elements at a point (deepest first). Returns IDs, types, bounds |
| `ui tap [elementId] [--automationId A] [--type T] [--text T] [--index N] [--and-screenshot [path]] [--and-tree] [--and-tree-depth N]` | Tap element by ID or implicit resolution |
| `ui fill [elementId] <text> [--automationId A] [--type T] [--text T] [--index N] [--and-screenshot [path]] [--and-tree]` | Fill text into Entry/Editor. elementId optional when using resolution options |
| `ui clear [elementId] [--automationId A] [--type T] [--text T] [--index N] [--and-screenshot [path]] [--and-tree]` | Clear text. elementId optional when using resolution options |
| `MAUI focus [elementId] [--automationId A] [--type T] [--text T] [--index N]` | Set focus. elementId optional when using resolution options |
| `MAUI assert [--id ID] [--automationId A] <property> <expected>` | Assert element property value. Exit 0 if match, 1 if mismatch. Ideal for verification without screenshots |
| `MAUI screenshot [--output path.png] [--window W] [--id ID] [--selector SEL] [--overwrite] [--max-width N] [--scale native]` | PNG screenshot. Auto-scales to 1x logical resolution on HiDPI displays (2x, 3x). Use `--scale native` for full resolution. `--max-width N` overrides auto-scaling with explicit width. `--overwrite` replaces existing file |
| `MAUI property <elementId> <prop>` | Read property (Text, IsVisible, FontSize, etc.) |
| `MAUI set-property <elementId> <prop> <value>` | Set property (live editing — colors, text, sizes, etc.) |
| `MAUI element <elementId>` | Full element JSON (type, bounds, children, etc.) |
| `MAUI navigate <route>` | Shell navigation (e.g. `//native`, `//blazor`) |
| `MAUI scroll [--element id] [--dx N] [--dy N] [--item-index N] [--group-index N] [--position P] [--window W]` | Scroll by delta, item index, or scroll element into view. `--item-index` scrolls to a specific item in CollectionView/ListView (works even for virtualized off-screen items). `--position`: MakeVisible (default), Start, Center, End. Delta scroll (`--dy -500`) uses native platform scroll for CollectionView |
| `MAUI resize <width> <height> [--window W]` | Resize app window. Window is 0-based index; default first window |
| `MAUI logs [--limit N] [--skip N] [--source S] [--follow]` | Fetch or stream application logs. `--follow` / `-f` streams in real-time (Ctrl+C to stop). Source: native, webview, or omit for all |
| `MAUI recording start [--output path] [--timeout 30]` | Start screen recording. Default timeout 30s |
| `MAUI recording stop` | Stop active recording and save the video file |
| `MAUI recording status` | Check if a recording is currently in progress |
| `MAUI network` | Live network monitor — streams HTTP requests in real-time (Ctrl+C to stop) |
| `MAUI network list [--host H] [--method M]` | One-shot: dump recent captured HTTP requests |
| `MAUI network detail <requestId>` | Full request/response details: headers, body, timing |
| `MAUI network clear` | Clear the captured request buffer |
| `MAUI preferences list [--sharedName N]` | List all known preference keys and values |
| `MAUI preferences get <key> [--type T] [--sharedName N]` | Get a preference value. Types: string, int, bool, double, float, long, datetime |
| `MAUI preferences set <key> <value> [--type T] [--sharedName N]` | Set a preference value |
| `MAUI preferences delete <key> [--sharedName N]` | Remove a preference |
| `MAUI preferences clear [--sharedName N]` | Clear all preferences |
| `MAUI secure-storage get <key>` | Get a secure storage value |
| `MAUI secure-storage set <key> <value>` | Set a secure storage value |
| `MAUI secure-storage delete <key>` | Remove a secure storage entry |
| `MAUI secure-storage clear` | Clear all secure storage entries |
| `MAUI platform app-info` | App name, version, build, package, theme |
| `MAUI platform device-info` | Device manufacturer, model, OS, idiom |
| `MAUI platform display` | Screen density, size, orientation, refresh rate |
| `MAUI platform battery` | Battery level, state, power source |
| `MAUI platform connectivity` | Network access and connection profiles |
| `MAUI platform version-tracking` | Current/previous/first version, build history, isFirstLaunch |
| `MAUI platform permissions [name]` | Check permission status. Omit name to check all common permissions |
| `MAUI platform geolocation [--accuracy A] [--timeout N]` | Get current GPS coordinates. Accuracy: Lowest, Low, Medium (default), High, Best |
| `MAUI sensors list` | List available sensors and their current state (started/stopped) |
| `MAUI sensors start <sensor> [--speed S]` | Start a sensor. Sensors: accelerometer, barometer, compass, gyroscope, magnetometer, orientation. Speed: UI (default), Game, Fastest, Default |
| `MAUI sensors stop <sensor>` | Stop a sensor |
| `MAUI sensors stream <sensor> [--speed S] [--duration N]` | Stream sensor readings via WebSocket. Duration 0 = indefinite (Ctrl+C to stop) |
| `commands [--json]` | List all available commands with descriptions. `--json` returns machine-readable schema with command names, descriptions, and whether they mutate state |

Element IDs come from `MAUI tree` or `MAUI query`. AutomationId-based elements use their
AutomationId directly. Others use generated hex IDs. When multiple elements share the same
AutomationId, suffixes are appended: `TodoCheckBox`, `TodoCheckBox_1`, `TodoCheckBox_2`, etc.

**Element ID lifecycle:** IDs are ephemeral — they're regenerated on each tree walk. After
navigation, page changes, or significant UI updates, re-query to get fresh IDs. AutomationIds
are stable across rebuilds (they come from XAML), so prefer `--automationId` for scripted flows.

### maui devflow webview (Blazor WebView CDP)

Global options: `--agent-host` (default localhost), `--agent-port` (auto-discovered via broker).
CDP commands use the same agent port — all communication goes through a single port.
Use `--webview <id>` (or `-w <id>`) on any CDP command to target a specific WebView
by index, AutomationId, or element ID. Default: first WebView.

| Command | Description |
|---------|-------------|
| `cdp status` | CDP connection status and WebView count |
| `cdp webviews [--json]` | List available CDP WebViews (index, AutomationId, ready status) |
| `cdp snapshot` | Accessible DOM text (best for AI agents) |
| `cdp source` | Get full page HTML source |
| `cdp Browser getVersion` | Browser/WebView version info |
| `cdp Runtime evaluate <expr>` | Evaluate JavaScript |
| `cdp DOM getDocument` | Full DOM document |
| `cdp DOM querySelector <sel>` | Find first matching element |
| `cdp DOM querySelectorAll <sel>` | Find all matching elements |
| `cdp DOM getOuterHTML <sel>` | Get outer HTML of element |
| `cdp Page navigate <url>` | Navigate to URL |
| `cdp Page reload` | Reload page |
| `cdp Page captureScreenshot` | Screenshot as base64 |
| `cdp Input dispatchClickEvent <sel>` | Click element by CSS selector |
| `cdp Input insertText <text>` | Insert text at focused element |
| `cdp Input fill <selector> <text>` | Focus + fill text into element |

**Multi-WebView targeting:** If the app has multiple BlazorWebViews, use `cdp webviews`
to list them, then `--webview <index-or-automationId>` on any command to target a specific one.
Example: `maui devflow webview --webview 1 snapshot` or `maui devflow webview -w MyWebView Runtime evaluate "1+1"`.

### maui devflow broker & discovery

The broker is a background daemon that manages port assignments for all running agents.
The CLI auto-starts the broker on first use — no manual setup needed.

| Command | Description |
|---------|-------------|
| `list` | Show all registered agents (ID, app, platform, TFM, port, uptime) |
| `wait [--timeout 120] [--project path] [--wait-platform P] [--json]` | Wait for an agent to connect. Outputs the port (or JSON with `--json`). Useful after `dotnet build -t:Run` to block until the app is ready |
| `broker status` | Broker daemon status and connected agent count |
| `broker start` | Start broker daemon (auto-started by CLI — rarely needed manually) |
| `broker stop` | Stop broker daemon |
| `broker log` | Show broker log file |

### maui devflow batch (Multi-Command Execution)

Execute multiple commands in one invocation via stdin. Returns JSONL responses. Use for
multi-step interactions to avoid repeated port resolution.

```bash
echo "ui fill textUsername user; ui fill textPassword pwd123; ui tap buttonLogin" | maui devflow batch
```

For full options, JSONL format, and streaming details, see [references/batch.md](references/batch.md).

## Platform Details

For detailed platform-specific setup, simulator/emulator management, and troubleshooting:

- **Setup & Installation**: See [references/setup.md](references/setup.md)
- **iOS / Mac Catalyst**: See [references/ios-and-mac.md](references/ios-and-mac.md)
- **macOS (AppKit)**: See [references/macos.md](references/macos.md)
- **Android**: See [references/android.md](references/android.md)
- **Linux / GTK**: See [references/linux.md](references/linux.md)
- **Troubleshooting**: See [references/troubleshooting.md](references/troubleshooting.md)

## ⚠️ Non-Disruptive Operation

**CRITICAL:** Never run commands that steal focus, move windows, simulate mouse/keyboard input,
or otherwise disrupt the user's desktop. The user is likely working on the same computer.

Some platform reference notes mention `osascript` or `xdotool`. Treat those as **implementation
details or explicit-consent edge cases**, not the default debugging workflow.

**Never directly use:**
- `osascript` to focus/activate windows, click UI elements, send keystrokes, or change host macOS appearance without the user's approval
- `screencapture` interactively (the MauiDevFlow screenshot command captures in-process instead)
- `xdotool` focus/activate/key commands from the shell that affect the active window
- Any command that moves the mouse cursor or simulates input at the OS level
- `open -a` to bring apps to the foreground (use `open` only to launch, not to focus)

**Instead:** All inspection and interaction goes through `maui devflow` CLI commands, which
communicate with the in-app agent over HTTP — no foreground focus required. Simulator-scoped
commands such as `xcrun simctl privacy`, `xcrun simctl ui <UDID> appearance ...`, and
`xcrun simctl io screenshot` are fine because they target the simulator rather than the user's
desktop. If recovery requires OS-level control outside the app, **ask the user** to do it
manually or get explicit approval before making a disruptive host-level change.

## Tips

- **`maui devflow list` shows runtime state, not project integration.** Empty list ≠ "not installed."
  Always check project files (`grep -rl "MauiDevFlow" --include="*.csproj" .`) before concluding DevFlow is unavailable.
- **`maui devflow diagnose`** is the fastest way to check the entire chain: CLI → broker → agents → projects.
- After launching through Aspire, always run `maui devflow wait` before attempting any interaction.
- **Use `maui devflow batch`** for multi-step interactions — resolves port once, adds delays,
  returns structured JSONL. See [references/batch.md](references/batch.md).
- **Always use `maui devflow ui screenshot`** — captures in-process, app does NOT need
  foreground focus.
- Use `AutomationId` on important MAUI controls for stable element references.
- For Blazor Hybrid, `cdp snapshot` is the most AI-friendly way to read page state.
- Port discovery, multi-project setup, and custom ports: see [references/setup.md](references/setup.md#3b-port-configuration).
- **Shell apps:** Read `AppShell.xaml` to discover routes before navigating. Routes are
  case-sensitive and often lowercase.
- **CollectionView items:** Tap the container Grid/StackLayout, not inner Labels/Images.
  Use `--item-index` to scroll to off-screen items.
- **Ambiguous `--text`:** When text appears on multiple pages, use explicit IDs from `tree`.

## AI Agent Best Practices

### Output Format
- **Always use `--json`** or rely on TTY auto-detection (JSON is auto-enabled when stdout is piped/redirected).
- Set `MAUIDEVFLOW_OUTPUT=json` in your environment for consistent machine-readable output.
- Use `--no-json` only when you specifically need human-readable output in a pipe.
- Errors go to stderr as structured JSON: `{"error": "...", "type": "RuntimeError", "retryable": false, "suggestions": [...]}`.
- Check exit codes: 0 = success, non-zero = failure.

### Reducing Token Usage
- **Use `--depth 15`** (or higher) for `MAUI tree` — MAUI visual trees are deeply nested (a simple
  control is often at depth 10-15). Start with `--depth 15`; if you see truncated children, increase.
  After your first successful tree dump, note the depth where meaningful controls appear and reuse
  that depth for subsequent calls. If the tree is still too large, combine with `--fields` to reduce width.
- Use **`--fields "id,type,text,automationId"`** to project only the fields you need.
- Use **`--format compact`** for minimal tree output (id, type, text, automationId, bounds).
- **Prefer `MAUI query --automationId`** over full tree traversal — much smaller response.
- Use **element-level screenshots** (`--id <elementId>`) when you only need to see one control.

### Adaptive Depth Learning
MAUI app trees vary in depth — a simple app might have controls at depth 8, while a complex app
with Shell + NavigationPage + nested layouts might need depth 20+. After your first `MAUI tree`
call, look at where the leaf-level controls (Button, Entry, Label) appear and remember that depth.
Use it for all subsequent tree calls in the same session. If you navigate to a new page that seems
deeper, bump the depth up. This avoids both truncating useful content and wasting tokens on
excessively deep dumps.

### Screenshot Auto-Scaling (HiDPI)
Screenshots are **automatically scaled to 1x logical resolution** by default. The agent detects
the device's display density (2x on Retina, 3x on iPhone Pro Max, 1x on desktop) and divides
the screenshot dimensions accordingly. This happens server-side before transfer.

- **No action needed** — just use `maui devflow ui screenshot --output screen.png` and the
  image will be appropriately sized for AI understanding.
- **Full resolution:** Use `--scale native` when you need pixel-perfect images (e.g., verifying
  exact colors, alignment, or anti-aliasing).
  ```bash
  maui devflow ui screenshot --output full-res.png --scale native
  ```
- **Explicit max width:** Use `--max-width N` to override auto-scaling with a specific pixel width.
  ```bash
  maui devflow ui screenshot --output screen.png --max-width 600
  ```

### Eliminating Round-Trips
- **Use implicit resolution** instead of query-then-act:
  ```bash
  # Instead of: query → get ID → tap
  maui devflow ui tap --automationId "LoginButton"
  maui devflow ui fill --automationId "Username" "admin"
  maui devflow ui tap --type Button --index 0  # first Button
  ```
- **Use `--wait-until`** instead of polling loops:
  ```bash
  maui devflow ui query --automationId "ResultsList" --wait-until exists --timeout 10
  maui devflow ui query --automationId "Spinner" --wait-until gone --timeout 30
  ```
- **Use post-action flags** to verify in one call:
  ```bash
  maui devflow ui tap abc123 --and-screenshot --and-tree --and-tree-depth 5
  ```
- **Use `MAUI assert`** for quick state checks:
  ```bash
  maui devflow ui assert --id abc123 Text "Welcome!"
  maui devflow ui assert --automationId "Counter" Text "5"
  ```

### Element IDs
- Element IDs are **ephemeral** — re-query after navigation or state changes.
- Don't cache element IDs across multiple actions — refresh with `tree` or `query`.
- Prefer `--automationId` for stable references (set in XAML).
- Use `maui devflow commands --json` to discover available commands at runtime.

### Shell Navigation
- **Routes are case-sensitive** and come from `ShellContent Route=""` in XAML, not from
  `FlyoutItem Title`. Discover routes by reading `AppShell.xaml`:
  ```bash
  grep -i 'Route=' AppShell.xaml
  ```
- **Flyout menu items** use generated IDs like `FlyoutItem_D_FAULT_FlyoutItem0`. Find them
  at the top level of the tree output. Don't try to tap Labels inside flyout items.
- **Flyout dismissal:** After tapping a flyout item, the flyout may stay open. Dismiss with:
  ```bash
  maui devflow ui set-property <shellId> FlyoutIsPresented "false"
  ```

### CollectionView / ListView
- **Tapping items:** Always tap the item's container (Grid/StackLayout), not inner elements
  (Label/Image). The item template's root element handles selection.
- **Virtualization:** CollectionView/ListView use item virtualization — only visible items
  (plus a small buffer) exist in the visual tree. Off-screen items have NO visual element.
  The tree shows `itemCount` in the CollectionView's properties so you know total items.
- **Scrolling by item index** (best for reaching off-screen items):
  ```bash
  maui devflow ui scroll --element <cvId> --item-index 20 --position Center
  ```
  This works even for items not in the tree yet — the platform scrolls to materialize them.
- **Scrolling by pixel delta** (for fine-grained scrolling):
  ```bash
  maui devflow ui scroll --element <cvId> --dy -500
  ```
  Uses native platform scroll (UIScrollView/RecyclerView) — works on CollectionView.
- **Workflow:** Get tree → note `itemCount` → scroll by index → re-query tree → interact:
  ```bash
  maui devflow ui tree --depth 15   # CollectionView shows itemCount: 25
  maui devflow ui scroll --item-index 20
  maui devflow ui tree --depth 15   # items around index 20 now visible
  ```

### Implicit Resolution Gotchas
- **`--text` searches the entire visual tree**, including hidden pages (other Shell tabs).
  If the text is ambiguous (e.g., `"+"`, `"OK"`, `"Cancel"`), it may match a wrong element
  on a different page.
- **Prefer `--automationId`** for reliable targeting. Fall back to explicit element IDs from
  `tree`/`query` for elements without AutomationIds.
- **Use `--type` + `--text` together** to narrow matches when text alone is ambiguous.

### Canonical Workflows

**Login flow:**
```bash
maui devflow ui query --automationId "LoginPage" --wait-until exists --timeout 15
maui devflow ui fill --automationId "UsernameField" "admin"
maui devflow ui fill --automationId "PasswordField" "password"
maui devflow ui tap --automationId "LoginButton" --and-screenshot
maui devflow ui query --automationId "HomePage" --wait-until exists --timeout 10
```

**Shell navigation:**
```bash
# Discover routes from XAML
grep -i 'Route=' AppShell.xaml                              # find route names
maui devflow ui navigate "//home"                         # navigate to a route
maui devflow ui tap FlyoutButton                          # open flyout
maui devflow ui tree --depth 3 --fields "id,type,text"    # find flyout items
maui devflow ui tap <flyoutItemId>                        # tap item
maui devflow ui set-property <shellId> FlyoutIsPresented "false"  # dismiss flyout
```

**Element inspection:**
```bash
maui devflow ui query --automationId "MyControl" --json --fields "id,type,text,bounds"
maui devflow ui element <id> --json
maui devflow ui property <id> Text
```

**State verification:**
```bash
maui devflow ui tap --automationId "IncrementButton"
maui devflow ui assert --automationId "CounterLabel" Text "1"
```
