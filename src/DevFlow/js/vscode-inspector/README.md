# MAUI DevFlow Inspector for VS Code

> **Source preview**: This extension is not currently published to the VS Code Marketplace.

Live-inspect and drive a running .NET MAUI app from VS Code. The extension embeds the shared
broker-hosted Inspector, including the visual tree, screenshot overlay, property editing, workflow
recording, and click-to-XAML navigation.

Start with the
[MAUI DevFlow Inspector setup guide](../../../../docs/DevFlow/inspector.md) for app integration and
a comparison of the browser, VS Code, GitHub Copilot desktop, and Copilot CLI hosts.

## Install from source

Requirements:

- Node.js 20.19 or later, or Node.js 22.12 or later, to build the source hosts;
- VS Code 1.125 or later; and
- a trusted VS Code workspace.

From a `maui-labs` source checkout:

```bash
cd src/DevFlow/js
npm ci
npm run build -w @maui-devflow/client
npm run package:vsix
code --install-extension vscode-inspector/dist/maui-devflow-inspector.vsix --force
```

Re-run the final command after rebuilding the VSIX, then reload the VS Code window to activate the
updated extension.

On Windows, both `node --version` and `cmd /c node --version` must succeed. If only PowerShell can
find Node, npm lifecycle scripts such as the VSIX signing helper will fail; put the Node
installation directory on `PATH` for child `cmd.exe` processes and open a new terminal.

## Open the Inspector

1. Add and register the DevFlow agent by following the
   [common app setup](../../../../docs/DevFlow/inspector.md#set-up-the-app-once).
2. Open the MAUI app workspace in VS Code.
3. Launch the DevFlow-enabled app.
4. Run **MAUI DevFlow: Open Inspector** from the Command Palette.

If no app is running, the Inspector opens a lightweight reconnecting panel and retries discovery
without taking focus. When one app appears it opens automatically; if several appear, use
**Choose app** to select deliberately.

The extension and the app do not start the broker. Before opening the panel, run a broker-dependent
command such as `maui devflow list` or start it explicitly:

```bash
maui devflow broker start
```

The extension runs in the workspace extension host so local, Remote, and WSL workspaces connect to
the broker beside the app tooling.

## Configuration

- `mauiDevflow.brokerPort` — explicit DevFlow broker port; `0` auto-discovers via
  `~/.mauidevflow/broker.json`.
- `mauiDevflow.openLocation` — where the Inspector panel opens: `auto` (default, opens beside the
  active editor when one is open, otherwise in the active group), `beside`, or `active`.
- `mauiDevflow.publishDiagnostics` — off-by-default preview that publishes runtime Problems and
  explicit Layout findings into VS Code Diagnostics.
- `mauiDevflow.registerMobileCanvasMcpServer` — off-by-default registration for the separately
  installed Mobile Canvas companion MCP server. The extension does not ship the companion and
  fails closed when it is absent or fails integrity validation.

## Copilot, source, and workflow integration

The extension contributes one command — **MAUI DevFlow: Open Inspector** — and an authenticated
bridge between the embedded Inspector and VS Code. It contributes no chat participant or
language-model tools. Its only MCP definition provider is the off-by-default Mobile Canvas
companion integration described above; run `maui devflow mcp` directly for DevFlow's app-level
automation tools.

- **Copilot** opens a context menu for the selected MAUI element, the loaded workflow, both
  together, or the current Data snapshot, and sends the bounded, redacted context to Copilot Chat.
  When Chat is unavailable the context is copied to the clipboard instead.
- The Data paperclip adds a bounded, redacted Logs, Network, Preferences, Device, Sensors, file
  metadata, or native Alerts snapshot.
- **Open source** navigates to generated XAML source locations when Debug source maps are enabled.
- **Diagnostics** can publish runtime Problems and explicit Layout findings into the Problems view,
  with actions to inspect the live control or ask Copilot for an explanation.
- **Record** creates a portable Markdown workflow that can be replayed by DevFlow.
- **Workflow** loads saved tests from the project's `maui-tests` directory or an OS-selected
  Markdown file and shows replay results in the shared Inspector panel.

## Approving an agent request

When the broker runs with `DEVFLOW_PREVIEW_AGENT_AUTHORING=true`, the Inspector shows an **Agent
requests** inbox. Approving one opens a native VS Code modal describing the exact request, scope,
and grant length. Only after you confirm does the extension read the owner-only approval token from
the local broker state, ask the broker for a single-use confirmation capability bound to that exact
target and scope, and immediately redeem it. The token never reaches the embedded page, the
capability is consumed on first use, and a replay is refused.

This proves the caller could read owner-restricted local state. It is not, and must not be
described as, proof that a human rather than a local agent process made the call.

GitHub Copilot Canvas and standalone browser tabs are not trusted approval hosts. This VS Code host
provides native approval. The explicit `maui devflow approve` CLI is an operator convenience, not a
human-attestation boundary.

## Related documentation

- [Inspector setup and host selection](../../../../docs/DevFlow/inspector.md)
- [Inspector internals](../../../../docs/DevFlow/inspector-internals.md)
- [Broker daemon](../../../../docs/DevFlow/broker.md)
- [Human-authored tests](../../../../docs/DevFlow/testing.md)
- [Device layer and Mobile Canvas](../../../../docs/DevFlow/devices.md)
