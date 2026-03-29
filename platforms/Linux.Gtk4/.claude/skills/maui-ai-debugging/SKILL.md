---
name: maui-ai-debugging
description: >
  End-to-end workflow for building, deploying, inspecting, and debugging .NET MAUI and MAUI Blazor Hybrid apps
  as an AI agent. Use when: (1) Building or running a MAUI app on iOS simulator, Android emulator, Mac Catalyst,
  macOS (AppKit), or Linux/GTK, (2) Inspecting or interacting with a running app's UI (visual tree, tapping,
  filling text, screenshots, property queries), (3) Debugging Blazor WebView content via CDP, (4) Managing
  simulators or emulators, (5) Setting up MauiDevFlow in a MAUI project, (6) Completing a build-deploy-inspect-fix
  feedback loop, (7) Handling permission dialogs and system alerts, (8) Managing multiple simultaneous apps via
  the broker daemon. Covers: maui-devflow CLI, androidsdk.tool, appledev.tools, adb, xcrun simctl, xdotool,
  and dotnet build/run for all MAUI target platforms including macOS (AppKit) and Linux/GTK.
---

# MAUI AI Debugging

Build, deploy, inspect, and debug .NET MAUI apps from the terminal. This skill enables a complete
feedback loop: **build → deploy → inspect → fix → rebuild**.

## Prerequisites

```bash
dotnet tool install --global Redth.MauiDevFlow.CLI || dotnet tool update --global Redth.MauiDevFlow.CLI
dotnet tool install --global androidsdk.tool    # Android only
dotnet tool install --global appledev.tools     # iOS/Mac only
```

Keep the skill up to date: `maui-devflow update-skill`. Check installed version vs remote
with `maui-devflow skill-version`. For full update procedures, see
[references/setup.md](references/setup.md#checking-for-updates).

## Integrating MauiDevFlow into a MAUI App

For complete setup instructions, see [references/setup.md](references/setup.md).

**Quick summary:**
1. Add NuGet packages (`Redth.MauiDevFlow.Agent`, and `Redth.MauiDevFlow.Blazor` for Blazor Hybrid)
   - For **Linux/GTK apps** (detected via `grep -i 'GirCore\|Maui\.Gtk' *.csproj`), use `Agent.Gtk` and `Blazor.Gtk` instead
   - For **macOS (AppKit) apps** (detected via `grep -i 'Platform\.Maui\.MacOS' *.csproj`), the standard `Agent` and `Blazor` packages include macOS support
2. Register in `MauiProgram.cs` inside `#if DEBUG`
3. For Blazor Hybrid: chobitsu.js is auto-injected (no manual script tag needed)
4. For Mac Catalyst: ensure `network.server` entitlement
5. For Android: run `adb reverse` for broker + agent ports
6. For Linux: no special network setup needed (direct localhost)
7. For macOS (AppKit): separate app head project, uses `open App.app` to launch. See [references/macos.md](references/macos.md)

## Core Workflow

### 1. Ensure a Device/Simulator/Emulator is Running

**⚠️ Multi-project conflict avoidance:** When multiple projects may run simultaneously
(common with AI agents), each project should use its own dedicated simulator/emulator to
prevent apps from replacing each other. Check what's already in use first:

```bash
maui-devflow list                                             # see all registered agents
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
A stale app's agent stays registered with the broker, causing `maui-devflow wait` to return
the old port instantly instead of waiting for the new build.

```bash
# Stop the async shell from the previous launch, then confirm:
maui-devflow list                 # should show no agents (or only unrelated ones)
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
maui-devflow wait                                # blocks until agent registers (default 120s)
maui-devflow wait --project path/to/App.csproj   # filter to specific project
```

`maui-devflow wait` prints the assigned port as soon as the agent connects. Exit code 1
means timeout — check async shell output for build errors.

**Android only** — set up port forwarding after the agent connects:
```bash
adb reverse tcp:19223 tcp:19223   # Broker (lets agent in emulator reach host broker)
adb forward tcp:<port> tcp:<port> # Agent (lets CLI reach agent in emulator)
```

**To rebuild:** repeat from Step 1. See [references/troubleshooting.md](references/troubleshooting.md)
if the build fails.

### 4. Inspect and Interact

**Typical inspection flow:**
1. `maui-devflow MAUI tree` — see the full visual tree with element IDs, types, text, bounds
2. `maui-devflow MAUI query --automationId "MyButton"` — find specific elements
3. `maui-devflow MAUI element <id>` — get full details (type, bounds, visibility, children)
4. `maui-devflow MAUI property <id> Text` — read any property by name
5. `maui-devflow MAUI screenshot --output screen.png` — visual verification

**Property inspection** is more reliable than screenshots for verifying exact runtime values:
```bash
maui-devflow MAUI property <id> BackgroundColor    # verify dark mode colors
maui-devflow MAUI property <id> IsVisible          # check element visibility
```

**Live editing (no rebuild needed):**
```bash
maui-devflow MAUI set-property <id> TextColor "Tomato"
maui-devflow MAUI set-property <id> FontSize "24"
```
Supports: string, bool, int, double, Color (named/hex), Thickness, enums. Changes persist
until the app restarts — safe for experimentation.

**Typical interaction flow:**
1. `maui-devflow MAUI fill <entryId> "text"` — type into Entry/Editor fields
2. `maui-devflow MAUI tap <buttonId>` — tap buttons, checkboxes, list items
3. `maui-devflow MAUI clear <entryId>` — clear text fields
4. Take screenshot to verify result

**Blazor WebView (if applicable):**
1. `maui-devflow cdp snapshot` — DOM tree as accessible text (best for AI)
2. `maui-devflow cdp Input fill "css-selector" "text"` — fill inputs
3. `maui-devflow cdp Input dispatchClickEvent "css-selector"` — click elements
4. `maui-devflow cdp Runtime evaluate "js-expression"` — run JS

**Live CSS/DOM editing in Blazor (no rebuild needed):**
```bash
maui-devflow cdp Runtime evaluate "document.querySelector('h1').style.color = 'tomato'"
maui-devflow cdp Runtime evaluate "document.documentElement.style.setProperty('--bg-color', '#1a1a2e')"
```

### 5. Reading Application Logs

MauiDevFlow automatically captures all `ILogger` output and WebView `console.*` calls
to rotating log files, retrievable remotely:

```bash
maui-devflow MAUI logs                   # fetch 100 most recent log entries
maui-devflow MAUI logs --limit 50        # fetch 50 entries
maui-devflow MAUI logs --source webview  # only WebView/Blazor console logs
maui-devflow MAUI logs --source native   # only native ILogger logs
```

**Debugging workflow:** Reproduce the issue → `maui-devflow MAUI logs --limit 20` → check for
errors. Add temporary `ILogger` calls for more detail, rebuild, reproduce, and fetch logs again.

### 6. Screen Recording

Capture video of the app while performing interactions. Recording is host-side (not in-app)
using platform-native tools.

```bash
# Start recording (default 30s timeout)
maui-devflow MAUI recording start --output demo.mp4

# Interact with the app
maui-devflow MAUI tap <buttonId>
maui-devflow MAUI navigate "//blazor"
maui-devflow MAUI fill <entryId> "Hello World"

# Stop and save
maui-devflow MAUI recording stop
```

**Platform tools used automatically:**
- **Android:** `adb screenrecord` (max 180s, capped with warning)
- **iOS Simulator:** `xcrun simctl io recordVideo`
- **Mac Catalyst / macOS (AppKit):** `screencapture -v` (targets app window when possible)
- **Windows/Linux:** `ffmpeg` (must be on PATH)

**Options:** `--timeout <seconds>` (default 30), `--output <path>` (default `recording_<timestamp>.mp4`).
Only one recording at a time — stop before starting a new one.

## Command Reference

### maui-devflow MAUI (Native Agent)

Global options: `--agent-host` (default localhost), `--agent-port` (auto-discovered via broker), `--platform`.

These options work on any subcommand position: `maui-devflow MAUI status --agent-port 10224`
or `maui-devflow --agent-port 10224 MAUI status` — both are valid.

| Command | Description |
|---------|-------------|
| `MAUI status` | Agent connection status, platform, app name |
| `MAUI tree [--depth N]` | Visual tree (IDs, types, text, bounds). Depth 0=unlimited |
| `MAUI query --type T --automationId A --text T` | Find elements (any/all filters) |
| `MAUI tap <elementId>` | Tap an element |
| `MAUI fill <elementId> <text>` | Fill text into Entry/Editor |
| `MAUI clear <elementId>` | Clear text from element |
| `MAUI screenshot [--output path.png]` | PNG screenshot |
| `MAUI property <elementId> <prop>` | Read property (Text, IsVisible, FontSize, etc.) |
| `MAUI set-property <elementId> <prop> <value>` | Set property (live editing — colors, text, sizes, etc.) |
| `MAUI element <elementId>` | Full element JSON (type, bounds, children, etc.) |
| `MAUI navigate <route>` | Shell navigation (e.g. `//native`, `//blazor`) |
| `MAUI logs [--limit N] [--skip N] [--source S]` | Fetch application logs (newest first). Source: native, webview, or omit for all |
| `MAUI recording start [--output path] [--timeout 30]` | Start screen recording. Default timeout 30s. Uses platform-native tools (adb screenrecord, xcrun simctl, screencapture, ffmpeg) |
| `MAUI recording stop` | Stop active recording and save the video file |
| `MAUI recording status` | Check if a recording is currently in progress |

Element IDs come from `MAUI tree` or `MAUI query`. AutomationId-based elements use their
AutomationId directly. Others use generated hex IDs. When multiple elements share the same
AutomationId, suffixes are appended: `TodoCheckBox`, `TodoCheckBox_1`, `TodoCheckBox_2`, etc.

### maui-devflow cdp (Blazor WebView CDP)

Global options: `--agent-host` (default localhost), `--agent-port` (auto-discovered via broker).
CDP commands use the same agent port — all communication goes through a single port.

| Command | Description |
|---------|-------------|
| `cdp status` | CDP connection status |
| `cdp snapshot` | Accessible DOM text (best for AI agents) |
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

### maui-devflow Broker & Discovery

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

### maui-devflow batch (Multi-Command Execution)

Execute multiple commands in one invocation via stdin. Returns JSONL responses. Use for
multi-step interactions to avoid repeated port resolution.

```bash
echo "MAUI fill textUsername user; MAUI fill textPassword pwd123; MAUI tap buttonLogin" | maui-devflow batch
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

**Never use:**
- `osascript` to focus/activate windows, click UI elements, or send keystrokes
- `screencapture` interactively (the MauiDevFlow screenshot command captures in-process instead)
- `xdotool` focus/activate/key commands that affect the active window
- Any command that moves the mouse cursor or simulates input at the OS level
- `open -a` to bring apps to the foreground (use `open` only to launch, not to focus)

**Instead:** All inspection and interaction goes through `maui-devflow` CLI commands, which
communicate with the in-app agent over HTTP — no foreground focus required. If you need
something that would require OS-level control (e.g., dismissing a system dialog outside the
app), **ask the user** to do it manually rather than attempting automation that would hijack
their input.

## Tips

- **Use `maui-devflow batch`** for multi-step interactions — resolves port once, adds delays,
  returns structured JSONL. See [references/batch.md](references/batch.md).
- **Always use `maui-devflow MAUI screenshot`** — captures in-process, app does NOT need
  foreground focus.
- Use `AutomationId` on important MAUI controls for stable element references.
- For Blazor Hybrid, `cdp snapshot` is the most AI-friendly way to read page state.
- Port discovery, multi-project setup, and custom ports: see [references/setup.md](references/setup.md#3b-port-configuration).
