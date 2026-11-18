# Android Reference

Prefer the unified `maui` CLI for Android device prep and DevFlow workflows.
Raw `adb` commands are kept only for
operations that the `maui` CLI does not yet wrap; those are grouped under
[Raw fallbacks not yet in `maui` CLI](#raw-fallbacks-not-yet-in-maui-cli).

## Table of Contents
- [Emulator Management](#emulator-management)
- [Building and Deploying](#building-and-deploying)
- [SDK and JDK Management](#sdk-and-jdk-management)
- [Raw fallbacks not yet in `maui` CLI](#raw-fallbacks-not-yet-in-maui-cli)
- [Troubleshooting](#troubleshooting)

## Emulator Management

### Avoiding multi-project conflicts

When multiple projects (or AI agents) may deploy to Android emulators
simultaneously, each project should use its own dedicated AVD. Two apps
with distinct application IDs can coexist on the same emulator, but `adb reverse` /
`adb forward` port forwarding is per-device and can cause confusion when
multiple emulators are running.

**Before creating or starting an emulator, check what's already in use:**
```bash
maui devflow list --json                          # agents with platform + port
maui device list --platform android --json        # connected emulators / devices
```

If an emulator is already running another project's agent, create a new AVD:
```bash
maui android sdk list --json                      # find installed system-images packages
maui android emulator create "ProjectName-Android" --package <installed-system-image> --json
maui android emulator start "ProjectName-Android" --wait --json
```

Replace `<installed-system-image>` with an exact installed package path, for
example `system-images;android-35;google_apis;x86_64` on an x64 host or
`system-images;android-35;google_apis;arm64-v8a` on Apple Silicon. Do not assume
that API level, architecture, or device profile is installed. Use
`maui android sdk list --available --json` to discover packages, then install
the chosen image. Omitting `--package` in JSON mode selects the most recent
installed image; omitting `--device` uses the CLI's `pixel_6` default.

**When multiple emulators are running**, target a specific serial with
`maui android port ... --device <serial>` or `maui devflow ... --device <serial>`.

**Naming convention:** Use `<ProjectName>-<DeviceType>` (e.g. `TodoApp-Pixel8`)
so it's clear which AVD belongs to which project.

### List, create, start, stop, delete AVDs
```bash
maui android emulator list --json
maui android emulator create <name> --package <system-image> --device <device> --json
maui android emulator start <name> --json         # add --cold-boot --wait if needed
maui android emulator stop <name> --json
maui android emulator delete <name> --json
```

All of the above accept `--json` for machine-readable output. The emulator
`create` command will prompt interactively for system image / device profile
when run without `--package` / `--device` and without `--json` / `--ci`.

### Verify emulator is running
```bash
maui device list --platform android --json
```

## Building and Deploying

```bash
# Build and deploy to running emulator
dotnet build -f net10.0-android -t:Run

# Build only (no deploy)
dotnet build -f net10.0-android
```

**Port forwarding after deploy** — the Android emulator runs in its own network,
so broker registration and CLI-to-agent traffic need opposite forwarding
directions. The `maui` CLI sets up **both** automatically:

```bash
maui devflow wait --wait-platform android --json   # broker reverse + agent forward
maui devflow list --json                          # repairs the agent forward on demand
maui devflow diagnose --json                      # reports mapping status
```

`maui devflow wait` establishes the broker `reverse` (tcp:19223) before polling
so the agent inside the emulator can register, then the agent `forward` once it
registers (port from `maui devflow list`, range 10223–10899). Add
`--device <serial>` when multiple emulators are attached. For explicit mapping
inspection or repair, use the unified port commands:

```bash
maui android port list --device <serial> --json
maui android port reverse 19223 --device <serial> --json
maui android port forward <port> --device <serial> --json
```

If the broker isn't available (fallback mode), forward the direct `.mauidevflow`
port yourself — this is the one path the CLI does **not** auto-forward (it only
forwards broker-registered agents):
```bash
maui android port forward 9223 --device <serial> --json
```

Then verify: `maui devflow ui status` and `maui devflow webview status`.

## SDK and JDK Management

### SDK
```bash
maui android sdk check --json                    # status and SDK path
maui android sdk list --json                     # installed packages
maui android sdk list --available --json         # repository packages, with is_installed
maui android sdk list --all --json               # installed and available
maui android sdk install <package-name> --json
maui android sdk uninstall <package-name> --json
```

`install` and `uninstall` accept package names as **positional** arguments
(no `--package` flag); pass multiple packages by space-separating them.

For a guided full-stack setup, run:
```bash
maui android install                             # interactive setup and package selection
```

### License prompts and unattended setup

After the user has authorized accepting Android SDK licenses, skip interactive
license prompts with:

```bash
maui android install --accept-licenses --ci --json
```

This accepts licenses; it does not bypass the license terms. It is a full
environment installer (JDK, SDK tools, and default packages), not a
licenses-only command. Use `--packages <package> ...` to replace its default
package selection. `--accept-licenses` belongs to `android install`, not
`android sdk install` or `android sdk accept-licenses`.

For manual review, run `maui android sdk accept-licenses` in an interactive
terminal **without** `--json`. With `--json`, that command only checks acceptance
and returns `status: "requires_interaction"` plus the terminal command when
licenses are pending; it does not accept them. `--ci` alone does not accept
licenses. The separate `maui android sdk install <package> --json` command
already requests license acceptance when installing packages, so obtain the
same authorization before using it.

### JDK
```bash
maui android jdk check --json                    # current path and version
maui android jdk install --version 17 --json      # choose 17 or 21 as required by the workload
maui android jdk list --json                     # available JDKs
```

### Environment variables
```bash
export ANDROID_HOME=$HOME/Library/Android/sdk
export ANDROID_SDK_ROOT=$ANDROID_HOME
export PATH=$PATH:$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator
```

## Raw fallbacks not yet in `maui` CLI

Prefer `maui` for everything it wraps. APK install/launch and logcat below are
not wrapped. Port inspection and repair use `maui android port` as shown above;
its `--device` selects an online serial (or use `ANDROID_SERIAL`). Without a
selector it requires exactly one online device. `maui android port clear`
removes **all** selected forward/reverse rules for that device, not just
DevFlow's mappings; avoid it when other apps depend on those rules.

### Install / uninstall / launch APK
```bash
adb install -r path/to/app.apk
adb uninstall <pkg>
adb shell am start -n <pkg>/<activity>
adb shell am force-stop <pkg>
adb shell pm list packages | grep <name>
```

### Logs
Once a DevFlow agent is connected, prefer in-app logs:
```bash
maui devflow logs --limit 50 --json
```
Pre-agent or for system-level traces, use `adb logcat`:
```bash
adb logcat -s "DOTNET" --format brief             # .NET runtime logs
adb logcat -s "MauiDevFlow"                       # agent logs
adb logcat --pid=$(adb shell pidof <pkg>)         # app-specific logs
adb logcat -c                                     # clear log buffer
```

### Screenshots and screen recording
For a running MAUI app, prefer:
```bash
maui devflow ui screenshot --output screen.png --json
```
For pre-launch / system-level capture:
```bash
adb shell screencap /sdcard/screen.png && adb pull /sdcard/screen.png
adb shell screenrecord /sdcard/video.mp4          # Ctrl+C to stop
```

### File operations / shell
```bash
adb push local/file /sdcard/path
adb pull /sdcard/path local/file
adb -s <serial> shell
```

## Troubleshooting

- **`maui device list --platform android` shows "unauthorized"**: Accept the
  USB debugging prompt on the device/emulator.
- **Agent not connecting on emulator**: Run
  `maui devflow wait --wait-platform android --device <serial> --json`.
  Inspect mappings with `maui android port list --device <serial> --json`.
- **Emulator won't start**: Check installed system images with
  `maui android sdk list`. Install one with
  `maui android sdk install "system-images;android-XX;..."`.
- **Build error "No Android devices found"**: Ensure emulator is booted
  (`maui device list --platform android`).
- **Slow emulator**: Use hardware acceleration. Prefer `arm64-v8a` images on
  Apple Silicon Macs.
- **JSON error envelope**: When an Android command returns the standard failure
  envelope with `--json`, parse the top-level `code` and `remediation` fields
  (no `error` wrapper; see `troubleshooting.md`). Common Android codes:
  `E2101` AndroidSdkNotFound,
  `E2106` AndroidEmulatorNotFound, `E2110` AndroidAdbNotFound, and `E2111`
  AndroidDeviceNotFound. `E2103` AndroidLicensesNotAccepted currently appears
  as a `maui doctor --json` fix `issue_id`.
