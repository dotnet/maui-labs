# Android Reference

## Table of Contents
- [Emulator Management](#emulator-management)
- [Building and Deploying](#building-and-deploying)
- [Android CLI Tool](#android-cli-tool)
- [ADB Reference](#adb-reference)
- [SDK Management](#sdk-management)
- [Troubleshooting](#troubleshooting)

## Emulator Management

### Avoiding multi-project conflicts

When multiple projects (or AI agents) may deploy to Android emulators simultaneously,
each project should use its own dedicated AVD. Two apps deployed to the same emulator
will coexist (unlike iOS), but `adb reverse`/`adb forward` port forwarding is per-device
and can cause confusion when multiple emulators are running.

**Before creating or starting an emulator, check what's already in use:**
```bash
maui-devflow list                             # shows agents with platform + port
adb devices                                   # shows connected emulators
```

If an emulator is already running another project's agent, create a new AVD:
```bash
android avd create --name "ProjectName-Pixel8" \
  --sdk "system-images;android-35;google_apis;arm64-v8a" --device pixel_8
android avd start --name "ProjectName-Pixel8"
```

**When multiple emulators are running**, use `-s <serial>` to target a specific one:
```bash
adb -s emulator-5554 reverse tcp:19223 tcp:19223   # first emulator
adb -s emulator-5556 reverse tcp:19223 tcp:19223   # second emulator
```

**Naming convention:** Use `<ProjectName>-<DeviceType>` (e.g. `TodoApp-Pixel8`) so it's
clear which AVD belongs to which project.

### List and start AVDs
```bash
android avd list                              # list available AVDs
android avd start --name <avd-name>           # start emulator
```

### Create AVD
```bash
# List available targets and device profiles
android avd targets                           # system images
android avd devices                           # device profiles (pixel, etc.)

android avd create --name "Pixel8API35" \
  --sdk "system-images;android-35;google_apis;arm64-v8a" \
  --device pixel_8
```

### Delete AVD
```bash
android avd delete --name <avd-name>
```

### Verify emulator is running
```bash
adb devices                                   # should show "emulator-5554 device"
android device list                           # formatted list
```

## Building and Deploying

```bash
# Build and deploy to running emulator
dotnet build -f net10.0-android -t:Run

# Build only (no deploy)
dotnet build -f net10.0-android
```

**Critical: Port forwarding after deploy** — the Android emulator runs in its own network.
Forward the broker port and the agent port:
```bash
adb reverse tcp:19223 tcp:19223              # Broker (lets agent register)
adb forward tcp:<port> tcp:<port>            # Agent (lets CLI reach agent)
```

The broker reverse is needed so the agent inside the emulator can connect to the host's
broker daemon. The agent forward uses the port shown in `maui-devflow list` after the agent
registers (range 10223–10899).

If the broker isn't available (fallback mode), forward the port from `.mauidevflow` instead:
```bash
adb reverse tcp:9223 tcp:9223                # Fallback: direct agent port
```

Then verify: `maui-devflow MAUI status` and `maui-devflow cdp status`.

### Install APK manually
```bash
adb install -r path/to/app.apk               # install/reinstall
android device install --package path/to/app.apk
```

## Android CLI Tool

The `android` command (from `androidsdk.tool` NuGet) wraps SDK tools.

### SDK management
```
android sdk list                              # all packages
android sdk list --installed                  # installed only
android sdk list --available                  # available for install
android sdk install --package "platforms;android-35"
android sdk install --package "system-images;android-35;google_apis;arm64-v8a"
android sdk install --package "emulator"
android sdk uninstall --package <package-name>
android sdk info                              # SDK location, tools versions
android sdk accept-licenses                   # accept all SDK licenses
android sdk download                          # download cmdline-tools
```

### AVD management
```
android avd list                              # available AVDs
android avd targets                           # available system images
android avd devices                           # available device profiles
android avd create --name <name> --sdk <system-image> --device <device>
android avd delete --name <name>
android avd start --name <name>
```

### Device/emulator operations
```
android device list                           # connected devices/emulators
android device info [--device <serial>]       # device properties
android device install --package <apk>        # install APK
android device uninstall --package <pkg-id>   # uninstall by package name
```

### JDK management
```
android jdk list                              # available JDKs
android jdk info                              # current JDK info
```

## ADB Reference

### Device/emulator basics
```bash
adb devices                                   # list connected devices
adb -s <serial> shell                         # shell into specific device
adb shell pm list packages | grep <name>      # find installed packages
adb shell am start -n <pkg>/<activity>        # launch activity
adb shell am force-stop <pkg>                 # kill app
```

### Port forwarding (critical for MauiDevFlow)
```bash
adb reverse tcp:19223 tcp:19223              # Broker (agent → host)
adb forward tcp:<port> tcp:<port>            # Agent (CLI → emulator, get port from `maui-devflow list`)
adb reverse --list                            # verify forwarding
adb forward --list                            # verify forwarding
adb reverse --remove-all                      # clean up reverse
adb forward --remove-all                      # clean up forward
```

### File operations
```bash
adb push local/file /sdcard/path              # push file to device
adb pull /sdcard/path local/file              # pull file from device
```

### Logs
```bash
adb logcat -s "DOTNET" --format brief         # .NET runtime logs
adb logcat -s "MauiDevFlow"                   # agent logs
adb logcat --pid=$(adb shell pidof <pkg>)     # app-specific logs
adb logcat -c                                 # clear log buffer
```

### Screenshots and screen recording
```bash
adb shell screencap /sdcard/screen.png && adb pull /sdcard/screen.png
adb shell screenrecord /sdcard/video.mp4      # Ctrl+C to stop
```

## SDK Management

### Typical setup for MAUI Android development
```bash
android sdk accept-licenses
android sdk install --package "platforms;android-35"
android sdk install --package "build-tools;35.0.0"
android sdk install --package "system-images;android-35;google_apis;arm64-v8a"
android sdk install --package "emulator"
android sdk install --package "platform-tools"
```

### Environment variables
```bash
export ANDROID_HOME=$HOME/Library/Android/sdk
export ANDROID_SDK_ROOT=$ANDROID_HOME
export PATH=$PATH:$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator
```

## Troubleshooting

- **`adb devices` shows "unauthorized"**: Accept the USB debugging prompt on the device/emulator.
- **Agent not connecting on emulator**: Forgot `adb reverse tcp:19223 tcp:19223` for the broker. Run port forwarding, then check `maui-devflow list`.
- **Emulator won't start**: Check available system images with `android avd targets`. May need
  to install with `android sdk install --package "system-images;..."`.
- **Build error "No Android devices found"**: Ensure emulator is booted (`adb devices`).
- **Slow emulator**: Use hardware acceleration. Prefer `arm64-v8a` images on Apple Silicon Macs.
