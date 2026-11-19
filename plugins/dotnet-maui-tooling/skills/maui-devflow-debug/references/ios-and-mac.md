# iOS & Mac Catalyst Reference

## Table of Contents
- [Simulator Management](#simulator-management)
- [Building and Deploying](#building-and-deploying)
- [Apple CLI Tool](#apple-cli-tool)
- [xcrun simctl Reference](#xcrun-simctl-reference)
- [Troubleshooting](#troubleshooting)

## Simulator Management

### Avoiding multi-project conflicts

When multiple projects (or AI agents) may deploy to iOS simulators simultaneously, each
project should use its own dedicated simulator to isolate app lifetimes and
device-wide state. Apps with different bundle IDs can coexist; redeploying the
same bundle ID replaces that app.

**Before creating or booting a simulator, check what's already in use:**
```bash
maui devflow list --json
maui apple simulator list --json             # select devices whose state is Booted
```

If a booted simulator is already running another project's agent, create a new one:
```bash
maui apple simulator create <installed-device-type> --name "ProjectName-iPhone" \
  --runtime <installed-runtime> --if-not-exists --json
# Use the returned UDID in your build command; discover identifiers below.
```

**Naming convention:** Use `<ProjectName>-<DeviceType>` (e.g. `TodoApp-iPhone17Pro`) so
it's clear which simulator belongs to which project.

### List simulators
```bash
maui apple simulator list --json              # inspect each device's state
xcrun simctl list devices booted              # raw: filter to only booted
xcrun simctl list devices available           # raw: filter to only available
```

### Create simulator
```bash
# List available device types and runtimes first (identifiers, not friendly names)
xcrun simctl list devicetypes                 # e.g. com.apple.CoreSimulator.SimDeviceType.iPhone-16-Pro
maui apple runtime list --json                # installed runtime identifiers

# Preferred: unified maui CLI (device-type is positional; --name/--runtime optional).
# --if-not-exists makes it idempotent: returns the existing UDID instead of failing.
maui apple simulator create com.apple.CoreSimulator.SimDeviceType.iPhone-16-Pro \
  --name "My iPhone" --runtime com.apple.CoreSimulator.SimRuntime.iOS-18-2 --if-not-exists --json

# Raw simctl equivalent (name, device-type, runtime are all positional)
xcrun simctl create "My iPhone" "iPhone 16 Pro" "iOS 18.2"
```

The concrete identifiers above are examples, not required installed versions.
Substitute identifiers from the local inventory; there is no `maui` device-type
inventory command yet.

### Boot / shutdown
```bash
# Preferred: unified maui CLI (accepts a name or UDID; `start` also opens the Simulator UI)
maui apple simulator start <name-or-UDID> --json     # add --no-open to boot headless
maui apple simulator stop <name-or-UDID> --json      # or `stop all`

# Raw simctl fallback
xcrun simctl boot <UDID>
xcrun simctl shutdown <UDID>
```

### Install and launch app
```bash
# Preferred: unified maui CLI (JSON output; structured errors where supported)
maui apple simulator install <UDID> /path/to/App.app --json
maui apple simulator launch <UDID> com.company.appid --json

# Raw simctl fallback (`booted` targets the active simulator)
xcrun simctl install booted /path/to/App.app
xcrun simctl launch booted com.company.appid
```

### Screenshots (iOS Simulator)
```bash
# Preferred: unified maui CLI (output path is positional — no --output flag)
maui apple simulator screenshot <UDID> output.png --json

# Raw simctl fallback
xcrun simctl io booted screenshot output.png
```

### Screenshots (Mac Catalyst)

**Use `maui devflow ui screenshot`** for Mac Catalyst apps — it captures the UI in-process
and does NOT require the app to be in the foreground. Never use `osascript` to bring the window
to the front or `screencapture` for Mac Catalyst screenshots; they are unnecessary and unreliable.

```bash
maui devflow ui screenshot --output screen.png --json
```

### Delete / erase
```bash
maui apple simulator erase <UDID> --json       # factory reset
maui apple simulator delete <UDID> --json      # permanently remove
xcrun simctl delete unavailable               # bulk cleanup has no maui wrapper
```

## Building and Deploying

### Mac Catalyst

**⚠️ Entitlements required:** Mac Catalyst apps are sandboxed by default and need the
`com.apple.security.network.server` entitlement for MAUI DevFlow's in-app HTTP server.
Without it, the agent fails to bind its port and the app may crash silently.

**Quick fix (disable sandbox for Debug):** Create `Platforms/MacCatalyst/Entitlements.Debug.plist`:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>com.apple.security.app-sandbox</key>
    <false/>
    <key>com.apple.security.network.client</key>
    <true/>
  </dict>
</plist>
```

Then reference it in `.csproj` for Debug only:
```xml
<PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)'))
    == 'maccatalyst' and '$(Configuration)' == 'Debug'">
  <CodeSignEntitlements>Platforms/MacCatalyst/Entitlements.Debug.plist</CodeSignEntitlements>
</PropertyGroup>
```

If sandbox must stay enabled (e.g. testing App Store builds), add `network.server` explicitly.
See [setup.md Step 5](setup.md#5-mac-catalyst-entitlements) for the full sandbox-enabled plist.

```bash
dotnet build -f net10.0-maccatalyst                          # build only
dotnet build -f net10.0-maccatalyst -t:Run                   # build + run
open path/to/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/AppName.app  # run existing
```

### iOS Simulator
```bash
# Select the intended booted simulator's UDID from this inventory
maui apple simulator list --json

# Build and deploy to that explicit UDID
dotnet build -f net10.0-ios -t:Run -p:_DeviceName=:v2:udid=<UDID>
```

The `-t:Run` target keeps the process alive while the app runs — it **blocks until the app exits**.
Always run in an async/background shell, then poll `maui devflow ui status` to detect when the
app is ready. Do NOT wait for the process to finish.

### Determining the correct TFM
Check the project file for `<TargetFrameworks>`:
```bash
grep -i TargetFramework *.csproj
```
Common values: `net9.0-ios`, `net9.0-maccatalyst`, `net10.0-ios`, `net10.0-maccatalyst`.

## Apple CLI Tool

Use the unified CLI; no separate Apple global tool is needed:

```bash
maui apple xcode list --json
maui apple runtime list --json
maui device list --platform ios --json
maui apple simulator list --json
maui apple simulator uninstall <UDID> com.company.appid --json
maui apple simulator terminate <UDID> com.company.appid --json
```

Some failed `start`, `stop`, or `delete` attempts return a command-specific
`{ "status": "warning", "message": "…" }` result rather than the flat error
envelope. Inspect the returned status as well as the process exit code.

Do not carry over syntax from predecessor tools: `maui` uses `start`/`stop`
(not `boot`/`shutdown`), `create <device-type> --name ...`, and positional
UDID/app arguments (no `app` subgroup). `maui apple simulator list` has no
`--booted` flag; filter the JSON inventory's state instead.

## xcrun simctl Reference

Key subcommands beyond the basics. Several of these are now wrapped by the
`maui` CLI (`maui apple simulator …`) — prefer the wrapper when available for
machine-readable output and consistent command discovery:

| Command | Use | `maui` equivalent |
|---------|-----|-------------------|
| `simctl boot <UDID>` | Boot a simulator | `maui apple simulator start <name-or-UDID>` |
| `simctl shutdown <UDID>` | Shut down | `maui apple simulator stop <name-or-UDID>` (or `stop all`) |
| `simctl create "name" "type" "runtime"` | Create | `maui apple simulator create <device-type> --name "name" --runtime <runtime>` |
| `simctl erase <UDID>` | Factory reset | `maui apple simulator erase <UDID>` |
| `simctl delete <UDID>` | Remove permanently | `maui apple simulator delete <name-or-UDID>` |
| `simctl addmedia <UDID> file.jpg` | Add photos/videos to sim | `maui apple simulator add-media <UDID> file.jpg` |
| `simctl openurl <UDID> "url"` | Open URL / deep link | `maui apple simulator openurl <UDID> "url"` |
| `simctl push <UDID> bundle payload.json` | Simulate push notification | `maui apple simulator push <UDID> bundle payload.json` |
| `simctl privacy <UDID> grant location bundle` | Grant permissions | `maui apple simulator privacy grant <UDID> location --bundle-id bundle` |
| `simctl location <UDID> set 37.33,-122.03` | Set GPS location | `maui apple simulator location set <UDID> 37.33 -122.03` |
| `simctl status_bar <UDID> override --time 9:41` | Override status bar | `maui apple simulator status-bar override <UDID> --time 9:41` |
| `simctl ui <UDID> appearance dark` | Set light/dark appearance | `maui apple simulator appearance dark <UDID>` |
| `simctl io <UDID> screenshot out.png` | Capture screenshot | `maui apple simulator screenshot <UDID> out.png` |
| `simctl io <UDID> recordVideo out.mp4` | Record until interrupted | `maui apple simulator record-video <UDID> out.mp4` |
| `simctl pbcopy <UDID>` | Copy stdin to clipboard | — |
| `simctl pbpaste <UDID>` | Read clipboard | — |
| `simctl get_app_container <UDID> bundle` | App container path | `maui apple simulator get-app-container <UDID> bundle` |
| `simctl listapps <UDID>` | Installed apps | — |

Add `--json` to the `maui` equivalents when consuming their results. Raw
equivalents are for unwrapped operations or an unavailable CLI, not a reason
to bypass a wrapper's reported failure.

## Troubleshooting

- **Mac Catalyst blank/white screen after crash**: macOS shows a "reopen windows" dialog after
  a crash, blocking the app from rendering. All MAUI elements appear as `[hidden] [disabled]`
  with `-1x-1` sizes. Fix: clear saved state before launch:
  ```bash
  rm -rf ~/Library/Saved\ Application\ State/<bundle-id>.savedState
  ```
  If the "Reopen windows?" dialog is already on screen, ask the user to dismiss it manually,
  then relaunch. Do not use AppleScript here by default — it steals focus from the user's
  desktop session.
- **"Unable to lookup in current state: Shutdown"**: Simulator not booted. Run `maui apple simulator start <UDID> --json`.
- **Build error NETSDK1005 "Assets file doesn't have a target"**: Wrong TFM. Check
  `<TargetFrameworks>` in .csproj and use matching version (e.g. `net10.0-ios` not `net9.0-ios`).
- **Agent not connecting after deploy**: The app may still be launching. Poll
  `maui devflow ui status` every few seconds. If it hasn't connected after ~60-90s, read the
  async shell output from `dotnet build -t:Run` for build/launch errors.
- **Mac Catalyst app name vs binary name**: The `.app` bundle name may differ from the project
  name (e.g. `MauiTodo.app` vs `SampleMauiApp`). Check the `ApplicationTitle` in .csproj.
  Find the bundle: `find bin/Debug/net10.0-maccatalyst -name "*.app" -maxdepth 3`

## Permission & Dialog Handling

### Pre-grant permissions (prevents dialogs from appearing)

**Prefer the `maui` CLI** — `maui apple simulator privacy` now wraps `simctl privacy`
(the `bundle-id` is optional, so you can grant for all apps). For
`maui devflow ui permission`, pass both `--udid` and `--bundle-id` when they
are known. Without `--udid`, the command selects the first booted simulator,
not the simulator associated with the connected agent. Omitting `--bundle-id`
applies the permission change to all apps on that simulator; neither command
requires a reachable DevFlow agent.
```bash
maui apple simulator privacy grant <UDID> location --bundle-id com.company.appid --json
maui apple simulator privacy grant <UDID> photos --json   # all apps (no bundle id)
maui devflow ui permission grant location --udid <UDID> --bundle-id com.company.appid --json
```

> If `maui devflow ui permission` reports `'ui' was not matched`, your installed
> `maui` CLI predates that subcommand. Use the `maui apple simulator privacy`
> path above (it needs only a UDID — no running agent) or update the CLI with
> `dotnet tool update --global Microsoft.Maui.Cli` (see setup.md → "Checking for
> Updates").

Raw `xcrun simctl` fallback (only if the `maui` CLI is unavailable):
```bash
# Grant specific permission before the app requests it
xcrun simctl privacy <UDID> grant location com.company.appid
xcrun simctl privacy <UDID> grant photos com.company.appid
xcrun simctl privacy <UDID> grant contacts com.company.appid
xcrun simctl privacy <UDID> grant microphone com.company.appid

# Grant all permissions at once
xcrun simctl privacy <UDID> grant all com.company.appid

# Revoke (deny) a permission
xcrun simctl privacy <UDID> revoke location com.company.appid

# Reset (next request will show dialog again)
xcrun simctl privacy <UDID> reset all com.company.appid

```

Available services: `all`, `calendar`, `contacts`, `contacts-limited`, `location`, `location-always`, `photos`, `photos-add`, `media-library`, `microphone`, `motion`, `reminders`, `siri`.

### Using MAUI DevFlow Driver for permissions
```csharp
var driver = new iOSSimulatorAppDriver();
driver.DeviceUdid = "<UDID>";
driver.BundleId = "com.company.appid";

// Pre-grant before running the app
await driver.GrantPermissionAsync(PermissionService.Location);
await driver.GrantPermissionAsync(PermissionService.Camera);

// Reset to test the dialog flow
await driver.ResetPermissionAsync(PermissionService.Location);
```

### Detecting and dismissing alerts (accessibility tree + HID tap)
When a dialog appears unexpectedly (permission prompt, app alert, action sheet), the driver can
detect it via the iOS accessibility tree and tap a button to dismiss it:

```csharp
// Check if an alert is currently showing
var alert = await driver.DetectAlertAsync();
if (alert is not null)
{
    Console.WriteLine($"Alert: {alert.Title}");
    foreach (var btn in alert.Buttons)
        Console.WriteLine($"  Button: {btn.Label} at ({btn.CenterX},{btn.CenterY})");
}

// Dismiss by tapping the first "accept" button (Allow, OK, etc.)
await driver.DismissAlertAsync();

// Dismiss by tapping a specific button
await driver.DismissAlertAsync("Don't Allow");

// Convenience: detect + dismiss if present, no-op if not
await driver.HandleAlertIfPresentAsync();
```

### Example workflow: permission dialog handling
```
1. App requests location → system shows "Allow location?" dialog
2. Agent detects dialog via DetectAlertAsync()
3. Agent sees buttons: ["Allow While Using App", "Allow Once", "Don't Allow"]
4. Agent taps "Allow While Using App" via DismissAlertAsync("Allow While Using App")
5. App receives permission grant, continues normal flow
```

### Dialog test page in SampleMauiApp
The SampleMauiApp includes a **Dialogs** tab with buttons that trigger:
- **Permission dialogs**: Location, Camera, Photos, Contacts, Microphone, Notifications
- **App alerts**: OK-only, OK/Cancel, custom buttons (Delete/Keep)
- **Action sheets**: Multiple options with cancel/destructive
- **Prompt dialogs**: Text input with OK/Cancel

Use these to test and validate dialog detection and dismissal workflows.

## Dark Mode Testing

### Toggle dark mode

Prefer the unified `maui` CLI. Use an **app-scoped** theme so the host desktop
is unaffected; fall back to simulator-wide appearance when you need the OS to
change too:
```bash
# App-scoped theme via the DevFlow agent (does not touch the host or OS)
maui devflow theme set dark --scope app --json
maui devflow theme set light --scope app --json
maui devflow theme get --json                    # read the current app theme

# iOS Simulator appearance (affects the simulator only)
maui apple simulator appearance dark <UDID> --json
maui apple simulator appearance light <UDID> --json

# Raw simctl fallback
xcrun simctl ui <UDID> appearance dark
```

For **Mac Catalyst**, changing system appearance affects the user's entire macOS desktop. Only do
that with explicit user approval; otherwise prefer app-level theme controls and verify via
`maui devflow ui property` / WebView inspection.

### Verify dark mode via inspection
Use `maui devflow` to verify colors without relying on screenshots:
```bash
maui devflow ui property <elementId> BackgroundColor --json
maui devflow webview Runtime evaluate "window.matchMedia('(prefers-color-scheme: dark)').matches" --json
```
