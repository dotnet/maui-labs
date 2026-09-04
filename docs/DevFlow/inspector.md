# MAUI DevFlow Inspector

> **Experimental preview**: The Inspector and its host integrations may change between releases.

The MAUI DevFlow Inspector shows a running app's screenshot, visual tree, properties, diagnostics,
data, and workflow tools. The same broker-hosted Inspector can be opened in:

- a browser;
- Visual Studio Code;
- the GitHub Copilot desktop app as a side canvas; or
- GitHub Copilot CLI as a canvas.

Set up the app once, then choose the host that fits your workflow.

## Choose a host

| Host | Open it | Best for | Availability |
|---|---|---|---|
| Browser | Start the broker and open `http://localhost:19223/inspector/` | Fastest first run and the complete shared Inspector UI | Included with `Microsoft.Maui.Cli` |
| VS Code | Run **MAUI DevFlow: Open Inspector** | Source navigation, workflow files, and Copilot context tools | Source-built preview VSIX |
| GitHub Copilot desktop | Ask Copilot to open the MAUI DevFlow Inspector canvas | A side canvas shared by the human and Copilot | Repository-scoped source preview |
| GitHub Copilot CLI | Ask Copilot to open the MAUI DevFlow Inspector canvas | Terminal-first work with the same live canvas | Repository-scoped source preview |

The Copilot extension folder is named `maui-devflow-canvas`; it registers the Canvas ID
`maui-live-canvas`.

## Set up the app once

### 1. Install the CLI

```bash
dotnet tool install --global Microsoft.Maui.Cli --prerelease
```

If it is already installed:

```bash
dotnet tool update --global Microsoft.Maui.Cli --prerelease
```

Confirm the command and version:

```bash
maui devflow version
```

On PowerShell, use `Get-Command maui`; on bash or zsh, use `command -v maui` if you need to confirm
which installation is running.

### 2. Add the in-app agent

For a standard .NET MAUI app:

```bash
dotnet add path/to/MyApp.csproj package Microsoft.Maui.DevFlow.Agent --prerelease
```

Replace `path/to/MyApp.csproj` with the app project's actual path.

For a Blazor Hybrid app, also add:

```bash
dotnet add path/to/MyApp.csproj package Microsoft.Maui.DevFlow.Blazor --prerelease
```

Linux/GTK apps use `Microsoft.Maui.DevFlow.Agent.Gtk` and, for Blazor Hybrid,
`Microsoft.Maui.DevFlow.Blazor.Gtk`. For plain .NET Android, iOS, Mac Catalyst, or macOS apps, see
the [DevFlow quick start](../../src/DevFlow/README.md#2b-or-in-a-plain-net-app-no-maui).

### 3. Register DevFlow

In `MauiProgram.cs`:

```csharp
using Microsoft.Maui.DevFlow.Agent;

// For Blazor Hybrid:
// using Microsoft.Maui.DevFlow.Blazor;

public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder.UseMauiApp<App>();

#if DEBUG
    builder.AddMauiDevFlowAgent();
    // Blazor Hybrid only:
    // builder.AddMauiBlazorDevFlowTools();
#endif

    return builder.Build();
}
```

Keep the registration inside `#if DEBUG` so Release builds do not start the development agent.

### 4. Start and verify

Start the broker before launching the app:

```bash
maui devflow broker start
```

Launch the app in Debug, then verify the connection:

```bash
maui devflow list
maui devflow ui status
maui devflow ui tree --depth 1
```

Platform notes:

- **Android**: On a fresh emulator/device session, run
  `maui devflow wait --wait-platform Android --device <serial>` while launching the app. The
  command prepares the broker reverse before waiting and then prepares the agent-port forward.
  `maui devflow diagnose --device <serial>` reports the current forwarding state without changing
  it.
- **Mac Catalyst**: A sandboxed Debug build needs the
  `com.apple.security.network.server` entitlement.
- **GTK**: Start the agent after app activation with `app.StartDevFlowAgent()`.

## Open in a browser

Run:

```bash
maui devflow broker start
```

Then open:

```text
http://localhost:19223/inspector/
```

The page lists every connected app. Select the app and platform you want to inspect. The per-agent
URL is `http://localhost:19223/inspector/{agent-id}/`.

## Open in Visual Studio Code

The VS Code extension is currently a source-built preview and is not published to the Marketplace.

### Build and install the VSIX

Requirements:

- Node.js 20 or later;
- VS Code 1.98 or later; and
- a trusted workspace.

From a `maui-labs` checkout:

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

### Open the Inspector

1. Start the broker and launch the Debug app.
2. Open the app workspace in VS Code.
3. Open the Command Palette.
4. Run **MAUI DevFlow: Open Inspector**.

If no app is connected, VS Code shows a warning; launch the app and run the command again. If
several apps are connected, select the intended app from the picker.

The extension also exposes the selected element and the current bounded Data snapshot to Copilot
through its two language-model tools.

See the [VS Code host README](../../src/DevFlow/js/vscode-inspector/README.md) for configuration and
host features.

## Open in GitHub Copilot

The GitHub Copilot desktop app and Copilot CLI use the same repository-scoped extension at
`.github/extensions/maui-devflow-canvas`. It is currently a source preview in `maui-labs`, not a
package for arbitrary app repositories.

### Prepare the Canvas extension

Use Node.js 20.19 or later, or Node.js 22.12 or later:

```bash
cd src/DevFlow/js
npm ci
npm run build -w @maui-devflow/client
cd ../../../.github/extensions/maui-devflow-canvas
npm ci
```

Do not run `npm start`; the Copilot host starts `extension.mjs`.

If a user extension exists at `~/.copilot/extensions/maui-live-canvas` or
`~/.copilot/extensions/maui-devflow-canvas`, temporarily move it outside
`~/.copilot/extensions` or rename its `extension.mjs` entry point. This removes ambiguity between
the user and repository copies.

### GitHub Copilot desktop app

1. Open the `maui-labs` repository folder and start a new signed-in Copilot session.
2. Start the broker and launch the Debug app.
3. Ask: **Open the MAUI DevFlow Inspector canvas.**

### GitHub Copilot CLI

1. Start Copilot CLI from the `maui-labs` repository root.
2. Start a new session, or run `/clear` after preparing or changing the extension.
3. Run `/env` and confirm `maui-devflow-canvas` resolves from this repository's
   `.github/extensions` directory.
4. Start the broker and launch the Debug app.
5. Ask: **Open the MAUI DevFlow Inspector canvas.**

When several apps are connected, ask Copilot to list the agents and select the intended app or
platform before making changes.

See the [Canvas host README](../../.github/extensions/maui-devflow-canvas/README.md) for
capabilities, safety boundaries, and contributor tests.

## Troubleshooting

| Symptom | What to do |
|---|---|
| `maui devflow list` shows no app | Confirm the package and `AddMauiDevFlowAgent()` registration, start the broker, and launch a Debug build. |
| The browser page does not open | Run `maui devflow broker status`, then `maui devflow broker start`. |
| More than one app is connected | Browser: choose from the agent list. VS Code: use the picker. Canvas: ask Copilot to list and select an agent. |
| Android app does not register or cannot be inspected | Run `maui devflow wait --wait-platform Android --device <serial>` while launching the app. Use `maui devflow diagnose --device <serial>` for a read-only forwarding report. |
| VS Code command is missing | Install the generated VSIX, confirm VS Code 1.98 or later, trust the workspace, and reload the window. |
| `npm ci` child scripts cannot find Node on Windows | Run `node --version` and `cmd /c node --version`; fix `PATH` if the second command fails. |
| Copilot Canvas is missing or uses the wrong copy | Disable same-purpose user extensions, start a new session, and use `/env` in Copilot CLI to confirm the repository extension path. |

For broker lifecycle, logs, and detailed connectivity recovery, see
[Broker daemon](broker.md#troubleshooting).

## Related documentation

- [Inspector internals](inspector-internals.md)
- [DevFlow overview and package quick start](../../src/DevFlow/README.md)
- [Broker daemon](broker.md)
- [VS Code Inspector host](../../src/DevFlow/js/vscode-inspector/README.md)
- [GitHub Copilot Canvas host](../../.github/extensions/maui-devflow-canvas/README.md)
- [DevFlow HTTP and WebSocket specification](spec/README.md)
