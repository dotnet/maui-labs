# Connectivity Recovery

Use this when a project already has DevFlow package references and `builder.AddMauiDevFlowAgent()` registered, but the `maui` CLI cannot reach a running app.

## Quick workflow

1. Run the built-in diagnostic:

   ```bash
   maui devflow diagnose
   ```

   Use the result to separate broker startup, missing project integration, no running app, and target-device networking issues.

2. Verify integration before chasing ports:

   ```bash
   grep -R --include="*.csproj" "Microsoft.Maui.DevFlow.Agent" .
   grep -R "AddMauiDevFlowAgent" .
   ```

   If package references or `AddMauiDevFlowAgent()` are missing, stop and switch to `maui-devflow-onboard`.

3. Check broker health:

   ```bash
   maui devflow broker status
   maui devflow broker start
   ```

4. List and wait for agents:

   ```bash
   maui devflow list
   maui devflow wait
   maui devflow ui tree --depth 1
   ```

   A successful `ui tree` means connectivity is fixed; continue the main debug loop.

## Platform notes

### Android emulator

Android emulators run in a separate network namespace. Broker registration and CLI-to-agent traffic need opposite forwarding directions:

```bash
maui devflow list                 # note the assigned agent port
adb reverse tcp:19223 tcp:19223   # app in emulator -> host broker
adb forward tcp:<port> tcp:<port> # host CLI -> app agent
adb reverse --list
adb forward --list
```

If no broker is available and the app uses a direct `.mauidevflow` port, forward that port instead:

```bash
adb forward tcp:9223 tcp:9223
```

### iOS simulator

No forwarding is normally needed because simulators share host networking:

```bash
xcrun simctl list devices booted
```

### Mac Catalyst and macOS

Mac Catalyst needs Debug entitlements that allow the agent and CDP servers to bind ports:

```xml
<key>com.apple.security.network.server</key>
<true/>
```

macOS AppKit and Mac Catalyst otherwise use direct localhost access. Check for explicit port conflicts only after confirming broker discovery:

```bash
lsof -i :9223
```

### Windows and Linux/GTK

These use direct localhost access. On Linux, verify the app process started:

```bash
pgrep -f "YourApp"
```

## Common symptoms

| Symptom | Fix |
|---------|-----|
| `maui devflow list` shows no agents | Verify app is running in Debug, package references exist, and `AddMauiDevFlowAgent()` executes |
| Android agent never registers | Run `adb reverse tcp:19223 tcp:19223` for broker registration |
| Android connection refused after registration | Run `adb forward tcp:<port> tcp:<port>` using the port from `maui devflow list` |
| Broker unavailable | `maui devflow broker start`; retry the command |
| Port already in use | Identify the owner with `lsof -i :<port>` before killing a PID |
| Multiple agents connected | Ask which app/device/agent port to target; use `--agent-port` |

## Stop signals

- Stop and switch to `maui-devflow-onboard` if package references or `AddMauiDevFlowAgent()` are missing.
- Stop connectivity recovery once `maui devflow wait` succeeds and `maui devflow ui tree --depth 1` returns a tree.
- Stop and ask which target to use if multiple connected agents or devices match the app.
- Stop after confirming a generic build/deploy failure; the app must launch before broker connectivity can work.

## Anti-patterns

- Do not treat an empty `maui devflow list` as proof the project is not integrated. It only means no runtime agent is connected.
- Do not use `adb reverse` for the agent HTTP port when the host CLI must connect into the emulator. Use `adb forward tcp:<port> tcp:<port>` for that direction.
- Do not kill random processes by name. Identify the owning PID first.
