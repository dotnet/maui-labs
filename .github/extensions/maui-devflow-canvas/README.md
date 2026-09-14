# MAUI DevFlow Inspector for GitHub Copilot Canvas

> **Repository-scoped source preview**: This extension is discovered from the `maui-labs`
> checkout. It is not currently packaged for installation into arbitrary app repositories.

The Canvas embeds the same broker-hosted DevFlow Web Inspector used by the browser and VS Code.
The human and Copilot see and drive the same running app, with agent-callable actions for
selection, inspection, interaction, screenshots, recording, and replay.

The source folder is named `maui-devflow-canvas`; it registers the Canvas ID
`maui-live-canvas`. The GitHub Copilot desktop app and Copilot CLI use this same extension.

Start with the
[MAUI DevFlow Inspector guide](../../../docs/DevFlow/inspector.md) for app setup and host selection.

## Prepare the extension

Requirements:

- Node.js 20.19 or later, or Node.js 22.12 or later;
- a `maui-labs` source checkout;
- a signed-in GitHub Copilot host with Canvas extension support; and
- a running app configured with the DevFlow agent.

From the repository root:

```bash
cd src/DevFlow/js
npm ci
npm run build -w @maui-devflow/client
cd ../../../.github/extensions/maui-devflow-canvas
npm ci
```

Do not run `npm start`; the Copilot host launches `extension.mjs`.

## Open in GitHub Copilot desktop

1. Open the `maui-labs` repository folder and start a new Copilot session.
2. Start the broker and launch the app in Debug.
3. Ask: **Open the MAUI DevFlow Inspector canvas.**

## Open in GitHub Copilot CLI

1. Start Copilot CLI from the `maui-labs` repository root.
2. Start a new session, or run `/clear` after preparing or changing the extension.
3. Run `/env` and confirm `maui-devflow-canvas` resolves from this repository's
   `.github/extensions` directory.
4. Start the broker and launch the app in Debug.
5. Ask: **Open the MAUI DevFlow Inspector canvas.**

When several apps are connected, ask Copilot to list the agents and select the intended app or
platform before making changes.

## Avoid duplicate user extensions

If `~/.copilot/extensions/maui-live-canvas` or
`~/.copilot/extensions/maui-devflow-canvas` exists, temporarily move it outside
`~/.copilot/extensions` or rename its `extension.mjs` entry point. This removes ambiguity between
the user and repository copies.

For example:

```powershell
Rename-Item "$HOME\.copilot\extensions\maui-devflow-canvas\extension.mjs" "extension.mjs.disabled"
```

```bash
mv ~/.copilot/extensions/maui-devflow-canvas/extension.mjs \
   ~/.copilot/extensions/maui-devflow-canvas/extension.mjs.disabled
```

Use `maui-live-canvas` in those commands for the older directory name. Restore the original
`extension.mjs` name to re-enable the user extension.

## Architecture

```text
Copilot Canvas -> extension.mjs -> broker-hosted shared Inspector
       |                |
       |                +-> LiveStore / DevflowDevice -> @maui-devflow/client
       +-> agent-callable actions ---------------------> running app
```

| File | Responsibility |
|---|---|
| `extension.mjs` | Canvas registration, loopback panel server, and agent-callable actions |
| `devflow.mjs` | Adapter over `@maui-devflow/client` |
| `store.mjs` | Live state, selection, and action coordination |
| `recorder.mjs`, `replay.mjs` | Workflow persistence and replay |
| `shell.mjs` | Shared Inspector iframe and disconnected shell |

## Contributor tests

From `.github/extensions/maui-devflow-canvas`:

```bash
cd ../../../src/DevFlow/js
npm ci
npm run build -w @maui-devflow/client

cd ../../../.github/extensions/maui-devflow-canvas
npm ci
npm test
npm run selftest:recorder

# Requires a running app with the DevFlow agent:
npm run selftest
```

## Capabilities

`get_canvas`, `refresh`, `list_agents`, `select_agent`, `get_tree`, `get_element`, `query`,
`hit_test`, `select_element`, `get_selection`, `attach_selection`, `get_property`,
`set_property`, `apply_and_verify`, `tap`, `fill`, `scroll`, `navigate`, `back`, `resize`,
`set_theme`, `screenshot`, `get_logs`, `start_recording`, `get_recording`,
`stop_and_save_test`, `save_test`, `list_tests`, `replay_test`.

## Coordination and safety

- The Canvas uses the same global mutation lease as the browser, VS Code, MCP, and CLI.
- Closing the Canvas releases its lease and disposes the shared client.
- The localhost bridge requires bounded JSON and a per-instance nonce for control and file writes.
- Recording Markdown is capped at 1 MiB, and replay paths stay inside the resolved `maui-tests`
  directory.
- Context attachment is bounded and text-only; screenshots are not attached automatically.
- Replay is blocked while a workflow recording is active.

## Related documentation

- [Inspector setup and host selection](../../../docs/DevFlow/inspector.md)
- [Inspector internals](../../../docs/DevFlow/inspector-internals.md)
- [VS Code Inspector host](../../../src/DevFlow/js/vscode-inspector/README.md)
- [Broker daemon](../../../docs/DevFlow/broker.md)
