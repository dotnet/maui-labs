# MAUI DevFlow Inspector for VS Code

Live-inspect and drive a running .NET MAUI app from VS Code. This MAUI DevFlow Inspector host
embeds the existing DevFlow Web Inspector, including the visual tree, screenshot overlay, property
editing, workflow recording, and click-to-XAML navigation.

## Requirements

1. Install the preview `Microsoft.Maui.Cli` global tool and DevFlow agent packages.
2. Add and start the DevFlow agent in a Debug build of your MAUI app.
3. Launch the app so it registers with the local DevFlow broker.
4. Run **MAUI DevFlow: Open Inspector** from the Command Palette.

If no app is running yet, the Inspector opens a lightweight reconnecting panel. It retries
discovery in the background without taking focus. When one app appears it opens automatically; if
several appear, use **Choose app** to select deliberately.

The extension requires VS Code 1.125 or later. It runs in the workspace extension host so local,
Remote, and WSL workspaces connect to the broker beside the app tooling.

## Configuration

- `mauiDevflow.brokerPort` — explicit DevFlow broker port; `0` auto-discovers via
  `~/.mauidevflow/broker.json`.
- `mauiDevflow.openLocation` — where the Inspector panel opens: `auto` (default, opens beside the
  active editor when one is open, otherwise in the active group), `beside`, or `active`.

## Copilot and source integration

The extension contributes one command — **MAUI DevFlow: Open Inspector** — and an authenticated
bridge between the embedded Inspector and VS Code. It contributes no chat participant, no
language-model tools, and no MCP server definition, so nothing here offers a command that would
silently do nothing. Run the DevFlow MCP server directly with `maui devflow mcp` when you want the
broader automation tools.

- **Copilot** opens a context menu for the selected MAUI element, the loaded workflow, both
  together, or the current Data snapshot, and sends the bounded, redacted context to Copilot Chat.
  When Chat is unavailable the context is copied to the clipboard instead.
- The Data paperclip adds a bounded, redacted Logs, Network, Preferences, Device, Sensors, file
  metadata, or native Alerts snapshot.
- **Open source** navigates to generated XAML source locations when Debug source maps are enabled.
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

See the [DevFlow Web Inspector documentation](https://github.com/dotnet/maui-labs/blob/main/docs/DevFlow/inspector.md).
