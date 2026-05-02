---
name: devflow-connect
description: >-
  Diagnose and fix DevFlow agent connectivity issues between the maui CLI and
  running .NET MAUI apps. USE FOR: "maui devflow" connection failures, agent not
  found, port conflicts, adb forwarding issues on Android, broker discovery
  problems. DO NOT USE FOR: app build failures, environment setup (use
  dotnet-maui-doctor), visual tree inspection after connection is established,
  or Blazor WebView CDP debugging.
---

# DevFlow Connect

Diagnose and resolve connectivity between the `maui` CLI and running .NET MAUI apps instrumented with the DevFlow agent.

## When to Use

- `maui devflow list` shows no agents
- `maui devflow tree` fails with "Cannot connect to agent"
- Port conflicts on 9223 or the broker port
- Android emulator connectivity issues (adb port forwarding)
- App is running but DevFlow commands fail

## When Not to Use

- Build or deployment failures (use standard build diagnostics)
- Environment setup (use `dotnet-maui-doctor` from dotnet/skills)
- Visual tree queries after connection works
- CDP/Blazor WebView debugging

## Prerequisites

- The `maui` CLI tool installed (`dotnet tool install -g Microsoft.Maui.Cli`)
- A .NET MAUI app with `Microsoft.Maui.DevFlow.Agent` NuGet package added
- The app must be running on a target device or emulator

## Workflow

### 1. Verify Agent Integration

Confirm the app has the DevFlow agent NuGet package:

```bash
grep -r "Microsoft.Maui.DevFlow.Agent" *.csproj
```

The agent must be initialized in `MauiProgram.cs`:

```csharp
builder.Services.AddMauiDevFlowAgent();
```

### 2. Check Broker Status

```bash
maui devflow broker status
```

If the broker is not running, start it:

```bash
maui devflow broker start
```

### 3. Platform-Specific Connectivity

#### Android Emulator

Android emulators run in a network namespace. Port forwarding is required:

```bash
adb reverse tcp:19223 tcp:19223  # Broker port
adb reverse tcp:9223 tcp:9223    # Agent default port
```

Verify with:

```bash
adb reverse --list
```

#### iOS Simulator

No port forwarding needed — simulators share the host network. Verify the app is running:

```bash
xcrun simctl list devices booted
```

#### Mac Catalyst / macOS

Direct localhost access. Check if the port is in use:

```bash
lsof -i :9223
```

#### Windows

Direct localhost access. Check firewall rules if connection fails.

#### Linux (GTK)

Direct localhost access. Verify the app process is running:

```bash
pgrep -f "YourApp"
```

### 4. List Connected Agents

```bash
maui devflow list
```

Expected output shows agent ID, app name, platform, and port. If empty, the agent in the app is not reaching the broker.

### 5. Test Connection

```bash
maui devflow tree --depth 1
```

A successful response returns the top-level visual tree. If this fails with a timeout, the agent HTTP server inside the app may not be responding.

### 6. Common Fixes

| Symptom | Fix |
|---------|-----|
| No agents listed | Check agent NuGet package + `AddMauiDevFlowAgent()` call |
| Android: connection refused | Run `adb reverse` for both ports (19223 + 9223) |
| Port already in use | Kill stale process: `lsof -i :9223` then `kill <PID>` |
| Broker not running | `maui devflow broker start` |
| App crashes on startup | Check that DevFlow agent version matches MAUI version |
