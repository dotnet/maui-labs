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
   maui devflow agent status
   maui devflow ui tree --depth 1
   ```

   A successful `agent status` proves the CLI can reach the running app and
   should show the expected app name/platform. A successful `ui tree` means
   connectivity is fixed for UI automation; continue the main debug loop.

## Platform notes

### Android emulator

Android emulators run in a separate network namespace, so broker registration and CLI-to-agent traffic need opposite forwarding directions. The unified `maui` CLI sets up **both** automatically: `maui devflow wait` establishes the broker `reverse` before polling and the agent `forward` once the agent registers, while `maui devflow list` / `agent status` / `ui status` re-establish (repair) the agent forward on demand. All honor `--device <serial>`.

```bash
maui devflow wait --platform android  # sets up broker reverse + agent forward, waits for the agent
maui devflow list                 # re-establishes the agent forward + notes the assigned port
maui device list --platform android  # confirm the emulator is connected
maui devflow diagnose             # reports broker reverse + agent forward mapping status
```

Drop to raw `adb` only to inspect or manually repair the mappings the CLI created:

```bash
adb reverse --list
adb forward --list
adb reverse tcp:19223 tcp:19223   # manual repair: app in emulator -> host broker
adb forward tcp:<port> tcp:<port> # manual repair: host CLI -> app agent
```

When multiple Android devices/emulators are connected, use the same serial selection the CLI supports:

```bash
maui devflow diagnose --device <serial>
maui devflow list --device <serial>
```

If no broker is available and the app uses a direct `.mauidevflow` port, forward that port instead — this direct-port forward is the one mapping the CLI does **not** set up automatically (it only auto-forwards broker-registered agents):

```bash
adb devices
adb forward tcp:9223 tcp:9223
maui devflow agent status --agent-host localhost --agent-port 9223
```

Use this direct status check before declaring Android connectivity broken. It is
common for broker registration to be empty while a direct, forwarded v1 agent
connection works. In that case continue with explicit `--agent-host localhost`
and `--agent-port <port>` until broker registration is restored.

### iOS simulator

No forwarding is normally needed because simulators share host networking:

```bash
maui apple simulator list
```

### Mac Catalyst and macOS

Mac Catalyst needs Debug entitlements that allow the agent and CDP servers to bind ports:

```xml
<key>com.apple.security.network.server</key>
<true/>
```

macOS AppKit and Mac Catalyst otherwise use direct localhost access. Check for explicit port conflicts only after confirming broker discovery (`lsof` is not yet wrapped by `maui` — use raw):

```bash
lsof -i :9223
```

### Windows and Linux/GTK

These use direct localhost access. On Linux, verify the app process started:

```bash
pgrep -f "YourApp"
```

## Reading errors from the CLI

`maui` commands accept `--json` to emit a structured output that an AI agent
can parse. On failure, error fields are at the **top level** of stdout (no
`"error"` wrapper) with `snake_case` property names:

```json
{
  "code": "E2106",
  "category": "platform",
  "severity": "error",
  "message": "Android emulator not installed",
  "remediation": {
    "type": "autofixable",
    "command": "maui android sdk install emulator"
  }
}
```

Note: the same `E2106` code is also thrown for "no AVD with that name" — that
throw site emits the code **without** a `remediation` block. Always treat
`remediation` as optional and fall back to surfacing `message`.

Common code prefixes (see [troubleshooting.md](troubleshooting.md#reading-machine-readable-output-and-errors) for the full taxonomy):

| Prefix | Meaning |
|--------|---------|
| `E1xxx` | Tool error (likely a bug) |
| `E2xxx` | Platform / SDK (Android, Apple, .NET) |
| `E3xxx` | User action required |
| `E4xxx` | Network |
| `E5xxx` | Permission |

Branch on `code`. When `remediation.type` is `autofixable` (lowercase!), run
`remediation.command` and retry. For `useraction`, present
`remediation.manual_steps`. If `remediation` is absent, surface `message`
and stop retrying.

> **DevFlow commands differ.** The flat stdout envelope above applies to `maui
> android`, `maui apple`, and `maui device`. The `maui devflow …` commands used
> throughout this file instead emit `{ "error", "type", "retryable",
> "suggestions" }` on **stderr**, so read stderr (not stdout) when a `maui
> devflow` invocation fails.

## Extension tools registered by the app

Apps can register custom DevFlow extension tools that the CLI surfaces dynamically.
When a tool the agent expects is missing or behaving unexpectedly, list and
inspect what the running app actually advertises:

```bash
maui devflow extensions list                       # all registered extensions and tools
maui devflow extensions describe <namespace>       # detailed tool schemas
maui devflow extensions call <namespace> <tool>    # invoke a tool with JSON parameters
```

Use this before assuming a custom DevFlow Action is unimplemented — it may simply
not be registered with the connected agent.

## Common symptoms

| Symptom | Fix |
|---------|-----|
| `maui devflow list` shows no agents | Verify app is running in Debug, package references exist, and `AddMauiDevFlowAgent()` executes |
| Android agent never registers | Run `maui devflow diagnose --device <serial>` or manually set `adb reverse tcp:19223 tcp:19223` for broker registration |
| Android connection refused after registration | Run `maui devflow diagnose --device <serial>` or manually set `adb forward tcp:<port> tcp:<port>` using the port from `maui devflow list` |
| Android broker list is empty but app is running | Forward the direct `.mauidevflow` port and run `maui devflow agent status --agent-host localhost --agent-port <port>` |
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
- Do not treat an empty broker/list result as final on Android until direct forwarded `agent status` has also failed.
- Do not use `adb reverse` for the agent HTTP port when the host CLI must connect into the emulator. Use `adb forward tcp:<port> tcp:<port>` for that direction.
- Do not kill random processes by name. Identify the owning PID first.
