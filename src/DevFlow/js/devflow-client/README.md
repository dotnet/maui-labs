# @maui-devflow/client

Shared **Node/TypeScript client** for the [DevFlow](../../README.md) broker + in-app agent:
broker discovery, the agent HTTP API, and the live event stream — with typed results and a
security-gated transport seam.

It is the common foundation for the Copilot Canvas and VS Code hosts, and mirrors the C#
`AgentClient` (`Microsoft.Maui.DevFlow.Driver`). It is consumed as a workspace/bundled package,
not published independently to npm.

## Why this exists

Hosts mirror and drive a live MAUI app by talking directly to DevFlow's broker for discovery and
the in-app agent for interaction instead of spawning the CLI per action. This package provides that
transport as one reusable, typed, tested library shared by every host.

## Install / build / test

```bash
# from src/DevFlow/js
npm install

# build the library (ESM + .d.ts → dist/)
npm run build -w @maui-devflow/client

# run the unit tests (compiles to dist-test/, then `node --test`)
npm test -w @maui-devflow/client
```

The online smoke test is skipped unless a live agent is running and `MAUI_DEVFLOW_SMOKE=1`:

```bash
MAUI_DEVFLOW_SMOKE=1 MAUI_DEVFLOW_PLATFORM=windows npm test -w @maui-devflow/client
```

Requires Node ≥ 18. Zero runtime dependencies (the WebSocket client is hand-rolled).

## Quick start

```ts
import { DevFlowClient } from "@maui-devflow/client";

// Reads MAUI_DEVFLOW_PLATFORM / _DEVICE / _AGENT_PORT / _PROJECT_ROOT (+ MAUI_CLI / ADB).
const client = DevFlowClient.fromEnv({ platform: "windows" });

const tree = await client.getTree(3);
if (tree.ok) console.log(tree.value.length, "elements on", client.target?.appName);
else console.error(tree.error.kind, tree.error.message); // e.g. "agent-ambiguous"

await client.tap({ elementId: "elem_6" });        // or { x, y }
await client.fill("elem_3", "hello");

// Live push (capability-negotiated; auto-reconnects and follows restarted apps).
const stream = client.openEvents({ onEvent: (e) => console.log(e.type) });
stream.close();
client.dispose();
```

### Host transport seam (for Canvas / VS Code proxies)

```ts
import { INTERACT } from "@maui-devflow/client";

// A strict, permission-gated, validated allow-list — the ONLY surface a webview may drive.
const transport = client.createTransport({
  permissions: INTERACT,           // read + screenshot + mutate, but NOT setProperty
  propertyAllowList: ["Text"],     // if setProperty is ever enabled
});

await transport.request({ kind: "tap", x: 40, y: 120 });
await transport.request({ kind: "getState" });
const unsub = transport.subscribe((e) => render(e));
```

## Public API (summary)

- **Discovery / lifecycle:** `listAgents()`, `connect(force?)`, `retarget({agentPort|platform|device|projectRoot})`, `dispose()`, `get target`
- **Coordination:** `controlMutationLease(...)`, `controlMutationRecording(...)`
- **Reads:** `getStatus`, `getTree`, `getElement`, `query`, `queryCss`, `hitTest`, `getProperty`, `getTheme`, `screenshot`, `getLogs`
- **Mutations:** `tap`, `fill`, `clear`, `focus`, `scroll`, `gesture`, `back`, `navigate`, `key`, `resize`, `setProperty`, `setTheme`
- **Events:** `openEvents({ onEvent, onStatus?, events? })`
- **Seam:** `createTransport(opts)` → `{ request(op), subscribe(onEvent) }`

Every I/O method returns a discriminated `DevFlowResult<T>`:

```ts
type DevFlowResult<T> =
  | { ok: true; value: T; target?: AgentTarget }
  | { ok: false; error: DevFlowError };  // error.kind: "agent-ambiguous" | "broker-not-found" | "timeout" | "http" | ...
```

A `null`/`false` API was deliberately rejected — hosts need to tell users *why* something
failed (no broker, ambiguous target, adb missing, capability absent, ...). Use `unwrap()` at
a boundary if you prefer exceptions.

## How it works

**Resolution** (`resolve.ts`) is broker-first, then a fast fallback:

1. Read `~/.mauidevflow/broker.json` → `GET /api/agents` (with the required `Host: localhost`).
2. Select a registration: pinned `agentPort` → `projectRoot` → `platform`/TFM. If still >1
   match, **fail with `agent-ambiguous` + candidates** (never silently pick — mutating the
   wrong app is dangerous) unless `allowAmbiguousMostRecent` is set.
3. For a broker-registered Android target, lazily ensure `adb forward` before verifying the port
  via `GET /agent/status`, even when the caller did not know the platform up front.
4. If the broker is absent/stale, a parallel scan of `{pinned, 9223, 10223..10242}`.

Android forwarding defaults to auto. Broker metadata can trigger an idempotent JS-side forward;
blind fallback ADB discovery runs only with an Android/device hint or `adb: true`, so an ordinary
desktop miss does not start ADB. Set `adb: false` to disable all JS-side ADB commands. The JS
forwarder complements the CLI's broker reverse/forward repair; neither owns app lifecycle.

Resolution is memoized behind a **mutex** so concurrent first-calls share one resolution. A
genuine socket error invalidates the cache and re-resolves.

**Retry** (`index.ts` `run()`): reads auto-retry once on a dropped socket; **mutations do
not** (a lost response can mean the change already applied) unless `retryMutations: true`.

**Events** (`events.ts`): the client checks for the `ui.events` capability before opening a
WebSocket. Older agents report `transport: "polling-only"` without repeated failed upgrades. The
capability is rechecked every 60 seconds so replacing an agent in place recovers automatically;
transient discovery failures retain bounded reconnect backoff.

**Broker bootstrap** is opt-in (`bootstrapBroker: "never" | "once" | "always"`, default
`"never"`) — a library shouldn't spawn `maui devflow list` behind the caller's back.

## Environment variables

| Var | Maps to | Meaning |
|-----|---------|---------|
| `MAUI_DEVFLOW_PLATFORM` | `platform` | Prefer agents whose platform/TFM contains this |
| `MAUI_DEVFLOW_DEVICE` | `device` | Android serial (enables ADB forwarding) |
| `MAUI_DEVFLOW_AGENT_PORT` | `agentPort` | Pin a specific agent port |
| `MAUI_DEVFLOW_PROJECT_ROOT` | `projectRoot` | Prefer agents under this folder |
| `MAUI_CLI` | `mauiCliPath` | Override the `maui` CLI path |
| `ADB` | `adbPath` | Override the `adb` path |

## Module map

```
types.ts       domain types + DevFlowResult/DevFlowError + options
env.ts         MAUI_DEVFLOW_* → options
http.ts        never-throw loopback HTTP + isConnError + parseJsonSafe
probe.ts       GET /agent/status liveness (keeps resolve independent of agent.ts)
adb.ts         AdbForwarder for Android agent ports
broker.ts      broker.json + /api/agents + bootstrap policy
ws-frame.ts    RFC6455 encode/decode + FrameReader (fragmentation/ping/pong/close)
resolve.ts     selection (unique|none|ambiguous) + Resolver (mutex/cache/retarget/dispose)
events.ts      openEventStream (subscribe-all, reconnect, self-heal, dispose-safe)
agent.ts       pure AgentRequest<T> builders + parsers
transport.ts   SeamOp allow-list + permission modes + payload validation
index.ts       DevFlowClient facade (run() = resolve → call → retry-reads → typed result)
```

The C# `AgentClient` (`src/DevFlow/Microsoft.Maui.DevFlow.Driver/AgentClient.cs`) remains the
behavioral reference; keep this client's wire shapes in sync with it and `docs/DevFlow/inspector.md`.
