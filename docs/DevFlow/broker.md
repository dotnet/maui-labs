# Broker Daemon

Microsoft.Maui.DevFlow includes a **broker daemon** that coordinates port assignment and agent
discovery across multiple running apps. It eliminates port collisions when debugging
several MAUI apps (or the same app on different platforms) simultaneously.

## Overview

The broker is a lightweight background process that:

- **Assigns unique ports** to each MAUI agent from a shared pool (10223–10899)
- **Tracks running agents** so the CLI can discover them without manual `--agent-port` flags
- **Detects disconnections instantly** via persistent WebSocket connections
- **Starts and stops automatically** — you rarely need to manage it directly

```
                    ┌──────────────────────────────────┐
                    │        Broker Daemon              │
                    │     (port 19223, well-known)      │
                    │                                   │
                    │  Agent Registry (in-memory)       │
                    │    key: hash(csproj + TFM)        │
                    │    val: { project, tfm, platform, │
                    │           appName, assignedPort,  │
                    │           websocket handle }      │
                    │                                   │
                    │  WebSocket /ws/agent ← agents     │
                    │  HTTP /api/agents   ← CLI         │
                    │                                   │
                    │  Auto-exit after 5 min idle       │
                    └───┬─────────────┬────────────────┘
                        │             │
         ┌──────────────┘             └──────────────┐
         │ Agent (WebSocket client)                   │ CLI (HTTP client)
         │ 1. Connect to broker                      │ 1. GET /api/agents
         │ 2. Send: project, TFM, platform           │ 2. Pick target agent
         │ 3. Receive: assigned port                  │ 3. Connect DIRECTLY to
         │ 4. Start HTTP server on assigned port      │    agent's HTTP port
         │ 5. Stay connected (liveness signal)        │    (no proxy through broker)
         └────────────────────────────────────────────┘
```

**Key design choice**: the broker is a **thin registry**, not a command proxy. The CLI
discovers an agent's port from the broker, then connects directly to the agent's own
HTTP server. This means zero overhead on the inspection/debugging hot path, and no
changes to the existing CLI command set.

## How It Works

### Agent Startup

When a MAUI app starts with `AddMicrosoft.Maui.DevFlowAgent()`:

1. The agent reads its **project identity** from assembly metadata injected at build time
   (`Microsoft.Maui.DevFlowProject` = absolute path to `.csproj`, `Microsoft.Maui.DevFlowTfm` = e.g.
   `net10.0-maccatalyst`).

2. It attempts to connect to the broker at `ws://localhost:19223/ws/agent` and sends a
   registration message:
   ```json
   {
     "type": "register",
     "project": "/Users/dev/MyApp/MyApp.csproj",
     "tfm": "net10.0-maccatalyst",
     "platform": "MacCatalyst",
     "appName": "MyApp"
   }
   ```

3. The broker assigns a free port from the pool (10223–10899), verifying the port is
   actually available via a TCP bind test. It responds:
   ```json
   { "type": "registered", "id": "a1b2c3d4e5f6", "port": 10223 }
   ```

4. The agent starts its HTTP server on the assigned port (10223 in this example).

   **Note:** If the agent already has an HTTP server running (e.g., from a `.mauidevflow`
   config or a previous broker connection), it sends `currentPort` in the registration
   message. The broker uses that port instead of allocating a new one from the pool.

5. The WebSocket connection stays open. The broker uses it as a liveness signal —
   if the connection drops, the agent is immediately marked as disconnected and
   its port is released.

### CLI Discovery

When you run a CLI command like `maui devflow ui status`:

1. The CLI calls `EnsureBrokerRunningAsync()` to make sure the broker is alive
   (starting it if necessary).

2. It queries the broker's HTTP API to find the right agent:
   - If run from a project directory, it hashes the `.csproj` path to match by identity
   - If only one agent is connected, it auto-selects
   - If multiple agents match the same project (different TFMs), it auto-selects if
     there's only one match
   - If multiple agents are connected and can't be narrowed down, it prints the agent
     list to stderr and falls back to `.mauidevflow` / default port. This is non-interactive
     — the output is designed so an AI agent (or human) can see the available ports and
     re-run with `--agent-port <port>`.

3. Once the agent's port is known, the CLI connects directly to the agent's HTTP
   server — all existing commands (`tree`, `screenshot`, `tap`, `logs`, etc.) work
   unchanged.

### Port Assignment

The broker assigns ports from a pool of **10223–10899** (677 ports). This range was
chosen to avoid collisions with ports in legacy `.mauidevflow` config files (which
typically use 9223–9899). For each new agent:

1. Iterate from 10223 upward
2. Skip ports already assigned to other connected agents
3. For each candidate, perform a real TCP bind test (start a `TcpListener`, then
   immediately stop it) to verify the port is actually free
4. Assign the first port that passes both checks

This ensures no collisions even with non-Microsoft.Maui.DevFlow processes using ports in the range.

### Agent Identity

Each agent instance is identified by a **deterministic hash**:

```
ID = SHA256( absolute_csproj_path + "|" + TFM )[:12]
```

For example, `/Users/dev/MyApp/MyApp.csproj|net10.0-maccatalyst` → `7ff0e6fd13d9`.

This means:
- The **same app on different platforms** (iOS vs Mac Catalyst) gets different IDs
- **Restarting** the same app replaces the old registration (same ID, new WebSocket)
- Different **git worktrees** of the same project get different IDs (different absolute paths)

## Broker Lifecycle

### Automatic Start

The broker starts transparently — you don't need to launch it manually. Both the CLI
and the agent call `EnsureBrokerRunningAsync()` which:

1. **Read state file** (`~/.mauidevflow/broker.json`) for the broker's port hint
2. **TCP connect** to `localhost:{port}` (500ms timeout, <1ms if refused)
3. If alive → use it
4. If not → clean up stale PID, fork a new broker process, poll until ready (5s timeout)

The state file looks like:
```json
{
  "pid": 54321,
  "port": 19223,
  "startedAt": "2026-02-13T01:20:00Z"
}
```

### Idle Timeout

The broker automatically exits after **5 minutes** with:
- Zero connected agents, AND
- No CLI HTTP requests in the last 5 minutes

A timer checks every 30 seconds. The timeout resets on any agent connection or CLI query.
This means the broker stays alive as long as any app is running, and lingers briefly after
the last app exits in case you're about to rebuild and relaunch.

### Manual Commands

For troubleshooting, you can manage the broker directly:

```bash
maui devflow broker start              # Start detached (same as auto-start)
maui devflow broker start --foreground # Start in current terminal (debug mode)
maui devflow broker stop               # Graceful shutdown
maui devflow broker status             # Show PID, port, uptime, connected agents
maui devflow broker log                # Show last 50 lines of broker.log
```

### Listing Connected Agents

```bash
maui devflow list
```

Shows all agents currently registered with the broker:

```
ID             App                  Platform       TFM                      Port   Uptime
------------------------------------------------------------------------------------------
7ff0e6fd13d9   MauiTodo             MacCatalyst    net10.0-maccatalyst      10223  2m 15s
a3c9e1f20b44   MauiTodo             Android        net10.0-android          10224  1m 30s
```

### Multiple Agents — Disambiguation

When multiple agents are connected and the CLI can't determine which one to target
(no `.csproj` in the current directory, or multiple TFMs for the same project), it
prints the agent table to stderr and falls back to the config file port:

```
Multiple agents connected. Use --agent-port to specify which one:

ID             App                  Platform       TFM                      Port
----------------------------------------------------------------------------------
7ff0e6fd13d9   MauiTodo             MacCatalyst    net10.0-maccatalyst      10223
a3c9e1f20b44   MauiTodo             Android        net10.0-android          10224

Example: maui devflow ui status --agent-port <port>
```

This output is **non-interactive** by design. AI agents can parse it and re-run
the command with the correct `--agent-port` flag. Humans can read the table and
pick the right port.

**Auto-resolution priority:**

1. `--agent-port` flag → always wins (explicit)
2. Exact match by project `.csproj` + TFM → single result
3. Match by project `.csproj` only → single result (any TFM)
4. Single agent connected → auto-select
5. Multiple agents, ambiguous → print list, fall back to `.mauidevflow` / default

## Graceful Fallback

The broker is **optional**. If it can't start or isn't available, everything falls
back to the existing behavior:

### Agent Fallback Chain

```
1. Broker assigns port           → use broker-assigned port
2. Broker unavailable            → read Microsoft.Maui.DevFlowPort from assembly metadata
                                   (compiled from .mauidevflow config at build time)
3. No assembly metadata          → use default port 9223
```

### CLI Fallback Chain

```
1. Query broker for agent port   → connect directly to agent
2. Broker unavailable            → read port from .mauidevflow in current directory
3. No .mauidevflow file          → use default port 9223
4. Explicit --agent-port flag    → always overrides everything
```

No functionality is lost without the broker — you just can't run multiple apps
simultaneously without manual port management.

## Agent Reconnection

The agent automatically reconnects to the broker in two scenarios:

1. **Broker restarts or WebSocket drops** — reconnection starts immediately
2. **Initial connection fails** (broker not yet running) — reconnection starts in the background
   while the agent falls back to its config/default port

Backoff schedule:

| Attempt | Delay  |
|---------|--------|
| 1       | 2s     |
| 2       | 5s     |
| 3       | 10s    |
| 4+      | 15s    |

Retries continue **indefinitely** — the agent never gives up trying to reach the broker.
When reconnecting after the HTTP server is already running, the agent sends `currentPort`
in the registration so the broker reuses its existing port rather than assigning a new one.

The HTTP server stays up throughout reconnection attempts — only broker discovery is affected.

## Platform Connectivity

| Platform       | Agent → Broker              | CLI → Agent               |
|----------------|-----------------------------|---------------------------|
| Mac Catalyst   | `localhost:19223` direct     | `localhost:{port}` direct |
| Windows        | `localhost:19223` direct     | `localhost:{port}` direct |
| Linux/GTK      | `localhost:19223` direct     | `localhost:{port}` direct |
| iOS Simulator  | Shares host network, direct  | `localhost:{port}` direct |
| Android Emu    | `adb reverse tcp:19223 tcp:19223` | `adb reverse tcp:{port} tcp:{port}` |

For Android, you need `adb reverse` for both the broker port (always 19223) and the
agent's assigned port. The broker port only needs to be set up once per emulator session.

## File Locations

| File | Purpose |
|------|---------|
| `~/.mauidevflow/broker.json` | Broker state (PID, port, start time). Written on start, deleted on stop. |
| `~/.mauidevflow/broker.log`  | Rolling log file (auto-truncated at 1MB). |

## Broker HTTP API

The broker exposes a simple HTTP API on port 19223 for CLI and diagnostic use:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/health` | GET | Health check. Returns `{"status":"ok","agents":N}` |
| `/api/agents` | GET | List all connected agents with full metadata |
| `/api/shutdown` | POST | Request graceful shutdown |
| `/ws/agent` | WebSocket | Agent registration endpoint |

### GET /api/agents Response

```json
[
  {
    "id": "7ff0e6fd13d9",
    "project": "/Users/dev/MyApp/MyApp.csproj",
    "tfm": "net10.0-maccatalyst",
    "platform": "MacCatalyst",
    "appName": "MyApp",
    "port": 10223,
    "connectedAt": "2026-02-13T01:20:01Z"
  }
]
```

## Troubleshooting

### Broker won't start

- **Port 19223 in use?** Check with `lsof -i :19223` (macOS/Linux) or
  `netstat -ano | findstr 19223` (Windows). Kill the conflicting process or
  stop the existing broker with `maui devflow broker stop`.
- **Stale state file?** Delete `~/.mauidevflow/broker.json` and try again.
- **Permissions?** The broker binds to `localhost` only — no admin/root required.

### Agent not appearing in `maui devflow list`

- **Broker running?** Run `maui devflow broker status` to check.
- **App actually started?** The agent registers during app startup. Verify the
  app launched successfully.
- **Firewall?** On Android, ensure `adb reverse tcp:19223 tcp:19223` is set up.
- **Custom port in code?** If `AddMicrosoft.Maui.DevFlowAgent(o => o.Port = XXXX)` sets a
  non-default port, the agent skips broker registration and uses the hardcoded port.

### CLI can't connect to agent

- **Port mismatch?** The broker may have assigned a different port than expected.
  Run `maui devflow list` to see actual port assignments.
- **Agent crashed after registration?** The broker may show the agent briefly
  before detecting the disconnect. Wait a moment and check again.
- **Android?** You need `adb reverse` for **both** port 19223 (broker) and the
  agent's assigned port.

### Broker exits unexpectedly

- Check `~/.mauidevflow/broker.log` for error messages.
- The broker auto-exits after 5 minutes of idle time (no agents, no CLI requests).
  This is normal behavior — it will restart automatically on the next CLI command
  or app launch.
