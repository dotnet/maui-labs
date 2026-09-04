# MAUI DevFlow Inspector for VS Code

> **Source preview**: This extension is not currently published to the VS Code Marketplace.

The extension embeds the shared broker-hosted DevFlow Web Inspector in VS Code. It adds
click-to-XAML navigation, workflow file selection, and bounded Copilot context for the selected
element and current Data snapshot.

Start with the
[MAUI DevFlow Inspector guide](../../../../docs/DevFlow/inspector.md) for app setup and a comparison
of the browser, VS Code, GitHub Copilot desktop, and Copilot CLI hosts.

## Install from source

Requirements:

- Node.js 20 or later;
- VS Code 1.98 or later; and
- a trusted VS Code workspace.

From the `maui-labs` repository root:

```bash
cd src/DevFlow/js
npm ci
npm run build -w @maui-devflow/client
npm run package:vsix
code --install-extension vscode-inspector/dist/maui-devflow-inspector.vsix --force
```

Reload the VS Code window after reinstalling the VSIX.

On Windows, both `node --version` and `cmd /c node --version` must work. If only PowerShell can
find Node, npm lifecycle scripts cannot run; add the Node installation directory to `PATH` and
open a new terminal.

## Open the Inspector

1. Follow the [common app setup](../../../../docs/DevFlow/inspector.md#set-up-the-app-once).
2. Start the broker with `maui devflow broker start`.
3. Launch the app in Debug.
4. Open the app workspace in VS Code.
5. Run **MAUI DevFlow: Open Inspector** from the Command Palette.

If no app is connected, the extension shows a warning; launch the app and run the command again.
If several apps are connected, select the intended app from the picker.

The extension runs in the workspace extension host, so local, Remote, and WSL workspaces connect
to the broker beside the app tooling.

## Configuration

- `mauiDevflow.brokerPort` — explicit broker port; `0` auto-discovers through
  `~/.mauidevflow/broker.json`.
- `mauiDevflow.openLocation` — `auto`, `beside`, or `active`.

## Copilot, source, and workflow integration

- `maui-devflow_getSelectedElement` returns the MAUI element currently selected in the Inspector.
- `maui-devflow_getDataSnapshot` returns the bounded, redacted Data snapshot added by the user.
- **Open source** navigates to generated XAML source locations when Debug source maps are enabled.
- **Record** creates a portable Markdown workflow.
- **Workflow** loads project `maui-tests` files or an OS-selected Markdown file and shows replay
  results in the shared Inspector panel.

## Related documentation

- [Inspector setup and host selection](../../../../docs/DevFlow/inspector.md)
- [Inspector internals](../../../../docs/DevFlow/inspector-internals.md)
- [Broker daemon](../../../../docs/DevFlow/broker.md)
