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
project should use its own dedicated simulator. Two apps deployed to the same simulator
will replace each other — only the last-deployed app survives.

**Before creating or booting a simulator, check what's already in use:**
```bash
maui-devflow list                             # shows agents with platform + port
xcrun simctl list devices booted              # shows all booted simulators
```

If a booted simulator is already running another project's agent, create a new one:
```bash
xcrun simctl create "ProjectName-iPhone17Pro" "iPhone 17 Pro" "iOS 26.2"
# Use the returned UDID in your build command
```

**Naming convention:** Use `<ProjectName>-<DeviceType>` (e.g. `TodoApp-iPhone17Pro`) so
it's clear which simulator belongs to which project.

### List simulators
```bash
xcrun simctl list devices                     # all devices by runtime
xcrun simctl list devices booted              # only booted
xcrun simctl list devices available            # only available
apple simulator list                          # formatted table
apple simulator list --booted                 # booted only
```

### Create simulator
```bash
# List available device types and runtimes first
xcrun simctl list devicetypes                 # e.g. "iPhone 16 Pro"
xcrun simctl list runtimes                    # e.g. "iOS 18.2"

xcrun simctl create "My iPhone" "iPhone 16 Pro" "iOS 18.2"
apple simulator create "My iPhone" --device-type "iPhone 16 Pro" --runtime "iOS 18.2"
```

### Boot / shutdown
```bash
xcrun simctl boot <UDID>
xcrun simctl shutdown <UDID>
apple simulator boot <UDID>
apple simulator shutdown <UDID>
```

### Install and launch app
```bash
xcrun simctl install booted /path/to/App.app
xcrun simctl launch booted com.company.appid
```

### Screenshots (iOS Simulator)
```bash
xcrun simctl io booted screenshot output.png
apple simulator screenshot <UDID> --output output.png
```

### Screenshots (Mac Catalyst)

**Use `maui-devflow MAUI screenshot`** for Mac Catalyst apps — it captures the UI in-process
and does NOT require the app to be in the foreground. Never use `osascript` to bring the window
to the front or `screencapture` for Mac Catalyst screenshots; they are unnecessary and unreliable.

```bash
maui-devflow MAUI screenshot --output screen.png
```

### Delete / erase
```bash
xcrun simctl erase <UDID>                     # factory reset
xcrun simctl delete <UDID>                    # permanently remove
xcrun simctl delete unavailable               # clean up old sims
```

## Building and Deploying

### Mac Catalyst
```bash
dotnet build -f net10.0-maccatalyst                          # build only
dotnet build -f net10.0-maccatalyst -t:Run                   # build + run
open path/to/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/AppName.app  # run existing
```

### iOS Simulator
```bash
# Find UDID of booted simulator
UDID=$(xcrun simctl list devices booted -j | python3 -c "
import json,sys
d=json.load(sys.stdin)
for r in d['devices'].values():
  for dev in r:
    if dev['state']=='Booted': print(dev['udid']); break
" 2>/dev/null | head -1)

# Build and deploy
dotnet build -f net10.0-ios -t:Run -p:_DeviceName=:v2:udid=$UDID
```

The `-t:Run` target keeps the process alive while the app runs — it **blocks until the app exits**.
Always run in an async/background shell, then poll `maui-devflow MAUI status` to detect when the
app is ready. Do NOT wait for the process to finish.

### Determining the correct TFM
Check the project file for `<TargetFrameworks>`:
```bash
grep -i TargetFramework *.csproj
```
Common values: `net9.0-ios`, `net9.0-maccatalyst`, `net10.0-ios`, `net10.0-maccatalyst`.

## Apple CLI Tool

The `apple` command (from `appledev.tools` NuGet) provides higher-level wrappers.

### Simulator commands
```
apple simulator list [--booted|--available|--unavailable|--name "..."]
apple simulator create <name> --device-type "..." [--runtime "..."]
apple simulator boot <target>
apple simulator shutdown <target>
apple simulator erase <target>
apple simulator delete <target>
apple simulator screenshot <target> [--output path.png]
apple simulator app install <target> <app-path>
apple simulator app launch <target> <bundle-id>
apple simulator app uninstall <target> <bundle-id>
apple simulator open [<target>]
apple simulator open-url <target> <url>
apple simulator logs <target> [--filter "..."]
apple simulator push <target> <bundle-id> [--payload "..."]
apple simulator location set <target> --lat <lat> --lon <lon>
apple simulator privacy grant <target> <service> <bundle-id>
```

### Device commands
```
apple device list
apple xcode list                                # installed Xcode versions
```

## xcrun simctl Reference

Key subcommands beyond the basics:

| Command | Use |
|---------|-----|
| `simctl addmedia <UDID> file.jpg` | Add photos/videos to sim |
| `simctl openurl <UDID> "url"` | Open URL / deep link |
| `simctl push <UDID> bundle payload.json` | Simulate push notification |
| `simctl privacy <UDID> grant location bundle` | Grant permissions |
| `simctl location <UDID> set 37.33,-122.03` | Set GPS location |
| `simctl pbcopy <UDID>` | Copy stdin to clipboard |
| `simctl pbpaste <UDID>` | Read clipboard |
| `simctl get_app_container <UDID> bundle` | App container path |
| `simctl listapps <UDID>` | Installed apps |

## Troubleshooting

- **Mac Catalyst blank/white screen after crash**: macOS shows a "reopen windows" dialog after
  a crash, blocking the app from rendering. All MAUI elements appear as `[hidden] [disabled]`
  with `-1x-1` sizes. Fix: clear saved state before launch:
  ```bash
  rm -rf ~/Library/Saved\ Application\ State/<bundle-id>.savedState
  ```
  Or detect and dismiss via AppleScript:
  ```bash
  osascript -e 'tell application "System Events" to tell process "AppName" to click button "Reopen" of window 1'
  ```
- **"Unable to lookup in current state: Shutdown"**: Simulator not booted. Run `xcrun simctl boot <UDID>`.
- **Build error NETSDK1005 "Assets file doesn't have a target"**: Wrong TFM. Check
  `<TargetFrameworks>` in .csproj and use matching version (e.g. `net10.0-ios` not `net9.0-ios`).
- **Agent not connecting after deploy**: The app may still be launching. Poll
  `maui-devflow MAUI status` every few seconds. If it hasn't connected after ~60-90s, read the
  async shell output from `dotnet build -t:Run` for build/launch errors.
- **Mac Catalyst app name vs binary name**: The `.app` bundle name may differ from the project
  name (e.g. `MauiTodo.app` vs `SampleMauiApp`). Check the `ApplicationTitle` in .csproj.
  Find the bundle: `find bin/Debug/net10.0-maccatalyst -name "*.app" -maxdepth 3`

## Permission & Dialog Handling

### Pre-grant permissions (prevents dialogs from appearing)
```bash
# Grant specific permission before the app requests it
xcrun simctl privacy <UDID> grant location com.company.appid
xcrun simctl privacy <UDID> grant camera com.company.appid
xcrun simctl privacy <UDID> grant photos com.company.appid
xcrun simctl privacy <UDID> grant contacts com.company.appid
xcrun simctl privacy <UDID> grant microphone com.company.appid

# Grant all permissions at once
xcrun simctl privacy <UDID> grant all com.company.appid

# Revoke (deny) a permission
xcrun simctl privacy <UDID> revoke location com.company.appid

# Reset (next request will show dialog again)
xcrun simctl privacy <UDID> reset all com.company.appid

# Via apple CLI
apple simulator privacy grant <UDID> location com.company.appid
```

Available services: `all`, `calendar`, `contacts`, `contacts-limited`, `location`, `location-always`, `photos`, `photos-add`, `media-library`, `microphone`, `motion`, `reminders`, `siri`.

### Using MauiDevFlow.Driver for permissions
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
```bash
# macOS (affects Mac Catalyst apps)
osascript -e 'tell application "System Events" to tell appearance preferences to set dark mode to true'
osascript -e 'tell application "System Events" to tell appearance preferences to set dark mode to false'

# iOS Simulator
xcrun simctl ui <UDID> appearance dark
xcrun simctl ui <UDID> appearance light
```

### Verify dark mode via inspection
Use `maui-devflow` to verify colors without relying on screenshots:
```bash
maui-devflow MAUI property <elementId> BackgroundColor   # check MAUI element colors
maui-devflow cdp Runtime evaluate "window.matchMedia('(prefers-color-scheme: dark)').matches"  # Blazor
```
