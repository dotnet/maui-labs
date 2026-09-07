# MAUI DevFlow Inspector for VS Code

Live-inspect and drive a running .NET MAUI app from VS Code. This MAUI DevFlow Inspector host
embeds the existing DevFlow Web Inspector, including the visual tree, screenshot overlay, property
editing, workflow recording, and click-to-XAML navigation.

## Requirements

1. Install the preview `Microsoft.Maui.Cli` global tool and DevFlow agent packages.
2. Add and start the DevFlow agent in a Debug build of your MAUI app.
3. Launch the app so it registers with the local DevFlow broker.
4. Run **MAUI DevFlow: Open Inspector** from the Command Palette.

If the broker or app is not running yet, the Inspector still opens. It shows whether it is waiting
for the broker, waiting for an app, or waiting for an explicit choice among several apps, and
reconnects automatically. **Retry** triggers discovery immediately. After an app is selected, the
panel preserves that choice even if the app disappears before connection completes. A restarted
process with a project-relative path requires a new explicit choice because its default identity
can be shared by other worktrees. Full project paths allow unique replacement-process matching.

The extension requires VS Code 1.98 or later. It runs in the workspace extension host so local,
Remote, and WSL workspaces connect to the broker beside the app tooling.

## Configuration

- `mauiDevflow.brokerPort` — explicit DevFlow broker port; `0` auto-discovers via
  `~/.mauidevflow/broker.json`.
- `mauiDevflow.openLocation` — where the Inspector panel opens: `auto` (default, opens beside the
  active editor when one is open, otherwise in the active group), `beside`, or `active`.

## Copilot and source integration

- **Copilot** opens a context menu for the selected MAUI element, the loaded workflow, both
  together, or the current Data snapshot. Selected elements use the
  `maui-devflow_getSelectedElement` language-model tool.
- The Data paperclip adds a bounded, redacted Logs, Network, Preferences, Device, Sensors, file
  metadata, or native Alerts snapshot through `maui-devflow_getDataSnapshot`; Copilot can use the
  included DevFlow MCP tool names for fresher or deeper follow-up.
- **Open source** navigates to generated XAML source locations when Debug source maps are enabled.
- If a mutation is blocked because another Inspector is driving the app, Copilot can inspect the
  holder with `maui_control_status`, request control with `maui_take_control`, and return it with
  `maui_release_control`.
- **Record** creates a portable Markdown workflow that can be replayed by DevFlow.
- **Workflow** loads saved tests from the project's `maui-tests` directory or an OS-selected
  Markdown file and shows replay results in the shared Inspector panel.

See the [DevFlow Web Inspector documentation](https://github.com/dotnet/maui-labs/blob/main/docs/DevFlow/inspector.md).
