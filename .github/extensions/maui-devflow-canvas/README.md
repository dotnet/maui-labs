# MAUI DevFlow Inspector for GitHub Copilot Canvas

> **Source-repository preview**: This Canvas extension is project-scoped to the `maui-labs`
> checkout. It is not currently packaged for installation into arbitrary MAUI app repositories.

A live, interactive view of a running .NET MAUI app inside a GitHub Copilot canvas: the app's real
screenshot with a visual-tree overlay and an editable property grid. Both the human and Copilot
inspect, select, edit, and drive the same running app through the DevFlow broker and in-app agent.

The GitHub Copilot desktop app and GitHub Copilot CLI use this same extension. The source folder is
named `maui-devflow-canvas`; it registers the Canvas ID `maui-live-canvas`.

Start with the
[MAUI DevFlow Inspector setup guide](../../../docs/DevFlow/inspector.md) for app integration and a
comparison of all Inspector hosts.

## Open the Canvas

### Prepare the source extension

Requirements:

- Node.js 20.19 or later, or Node.js 22.12 or later;
- a `maui-labs` source checkout; and
- a running MAUI app configured with the DevFlow agent.

From the repository root:

```bash
cd src/DevFlow/js
npm ci
npm run build -w @maui-devflow/client
cd ../../../.github/extensions/maui-devflow-canvas
npm ci
```

Do not run `npm start`; the Copilot host launches `extension.mjs`.

### GitHub Copilot desktop app

1. Install or update to a GitHub Copilot desktop build that supports Canvas extensions, then sign
   in.
2. Use **Open Folder** to open the `maui-labs` repository root after preparing the extension.
3. Start a new Copilot session.
4. Launch the DevFlow-enabled MAUI app.
5. Ask: **Open the MAUI DevFlow Inspector canvas.**

### GitHub Copilot CLI

1. Start an up-to-date Copilot CLI from the `maui-labs` repository root.
2. Start a new session, or run `/clear` after preparing or changing the extension.
3. Run `/env` and confirm `maui-devflow-canvas` resolves from this repository's
   `.github/extensions` directory, not `~/.copilot/extensions`.
4. Launch the DevFlow-enabled MAUI app.
5. Ask: **Open the MAUI DevFlow Inspector canvas.**

When several apps are running, ask Copilot to list the connected agents and select the intended app
or platform before making changes.

### Legacy user extension

This repository-scoped extension replaces earlier user-scoped copies at
`~/.copilot/extensions/maui-live-canvas` and
`~/.copilot/extensions/maui-devflow-canvas`. Move either directory outside
`~/.copilot/extensions` or rename its `extension.mjs` entry point before opening `maui-labs`;
otherwise a same-name user copy can be selected instead of the project copy in some host/session
combinations, while the older differently named extension can coexist and register the same Canvas
ID.

Copilot scans each immediate extension directory under `~/.copilot/extensions`, so renaming a
directory in place does not disable it. Temporarily rename the entry point for whichever path
exists:

```powershell
Rename-Item "$HOME\.copilot\extensions\maui-devflow-canvas\extension.mjs" "extension.mjs.disabled"
```

```bash
mv ~/.copilot/extensions/maui-devflow-canvas/extension.mjs \
   ~/.copilot/extensions/maui-devflow-canvas/extension.mjs.disabled
```

Use `maui-live-canvas` for the older directory name. Restore the original `extension.mjs` name to
re-enable the user extension.

## Shared Inspector architecture

The Canvas host embeds the existing DevFlow Web Inspector also used by the browser and VS Code. It
adds selected-element and redacted Data-snapshot context attachment, project-local workflow
persistence, and agent-callable actions.
Transport and discovery use `@maui-devflow/client`.

## Architecture

```
Copilot Canvas ──► extension.mjs ──► broker-hosted shared inspector
       │                  │
       │                  ├──► LiveStore / DevflowDevice ──► @maui-devflow/client
       │                  └──► authenticated localhost host bridge
       └── agent actions ───────────────────────────────► broker + in-app agent
```

- **`extension.mjs`** — `createCanvas(...)` with ~29 agent-callable capabilities + a loopback
  server that serves the panel; `joinSession(...)` at the bottom.
- **`store.mjs`** (`LiveStore`) — fallback live model and agent-action state.
- **`devflow.mjs`** (`DevflowDevice`) — adapter over `@maui-devflow/client`.
- **`shell.mjs`** — embeds the shared broker-hosted inspector in an iframe (`renderShell`) and
  renders the lightweight reconnecting shell (`renderDisconnected`) shown while waiting for the
  broker or a running app. It retries automatically, offers an explicit **Retry** action, and shares
  the hybrid `--df-*` theme-token language with the VS Code host shell.
- **`recorder.mjs`** — bounded workflow persistence and safe top-level test selection. Active
  recording is owned by the broker and observes successful mutations from every DevFlow host.
- **`replay.mjs`** — legacy offline compatibility fixture only. Production Canvas replay delegates
  to the shared Inspector's canonical C# `FlowReplayer`.
- **`selftest*.mjs`** — bridge smoke test and offline proof.

### File map

| File | Responsibility |
|---|---|
| `devflow.mjs` | Thin adapter over `@maui-devflow/client` |
| `store.mjs`, `extension.mjs` | Live state and Canvas host integration |
| `recorder.mjs` | Workflow persistence and safe test-file selection |
| `replay.mjs` | Legacy offline replay fixture; not used by the production Canvas |
| `selftest*.mjs`, `test/device.test.mjs` | Live smoke checks and offline contract tests |

## Contributor tests

From `.github/extensions/maui-devflow-canvas`:

```bash
# Build the shared client first (the file: dependency packs its dist/).
cd ../../../src/DevFlow/js && npm ci && npm run build -w @maui-devflow/client

# Install and test the extension.
cd ../../../.github/extensions/maui-devflow-canvas
npm ci
npm test                 # adapter contract tests (offline, fake agent)
npm run selftest:recorder  # offline recorder/replay proof

# Online bridge smoke test (needs a running MAUI app with the DevFlow agent):
npm run selftest

# In an isolated test environment, take over any lease held by another open Inspector and release
# it when the selftest finishes:
MAUI_DEVFLOW_FORCE_LEASE=1 npm run selftest
```

## Capabilities

`get_canvas`, `refresh`, `list_agents`, `select_agent`, `get_tree`, `get_element`, `query`,
`hit_test`, `select_element`, `get_selection`, `attach_selection`, `get_property`,
`set_property`, `apply_and_verify`, `tap`, `fill`, `scroll`, `navigate`, `back`, `resize`,
`set_theme`, `screenshot`, `get_logs`, `start_recording`, `get_recording`,
`stop_and_save_test`, `save_test`, `list_tests`, `replay_test`.

## Coordination and safety

- **The Canvas is not a trusted approval host.** It advertises no `nativeApproval` capability,
  holds no broker owner token, and serves no approval route, so it cannot issue an agent grant or
  any source authority. A `window.confirm()` here runs in a webview the embedded page can reach, so
  it is not evidence that the local human agreed. VS Code, whose modal runs in the extension
  process, is the only surface that mediates a native approval.
- The Canvas advertises no source-apply capability either; reviewed source proposals are read-only
  in this layer.
- The Canvas uses the same global mutation lease as the browser, VS Code, MCP, and CLI.
- Closing the Canvas releases its lease and disposes the shared client.
- The localhost bridge requires JSON plus a per-instance nonce for all control and file writes.
- Attach to Copilot sends bounded, text-only context; it does not attach a screenshot automatically.
- The Inspector context menu can attach only the selected element, only the loaded workflow, both,
  or the current redacted Data snapshot.
- Replays are blocked while a workflow recording is active and execute through the same C#
  `FlowReplayer` used by the Inspector, CLI, and MCP.
- Test file inputs are restricted to top-level Markdown files under the resolved project
  `maui-tests` directory.

## Requirements

- Node 20.19+ or 22.12+, `@github/copilot-sdk` 1.x, and a built `@maui-devflow/client`.
- An up-to-date, signed-in GitHub Copilot desktop app or Copilot CLI with Canvas extension support.
- No same-purpose user extension shadowing this repository-scoped extension.
- A running .NET MAUI app with the DevFlow agent, discoverable via the DevFlow broker
  (`maui devflow` / `~/.mauidevflow/broker.json`). The adapter auto-starts the broker
  (`bootstrapBroker: "once"`).

## Related documentation

- [Inspector setup and host selection](../../../docs/DevFlow/inspector.md)
- [Inspector internals](../../../docs/DevFlow/inspector-internals.md)
- [VS Code Inspector host](../../../src/DevFlow/js/vscode-inspector/README.md)
- [Broker daemon](../../../docs/DevFlow/broker.md)
