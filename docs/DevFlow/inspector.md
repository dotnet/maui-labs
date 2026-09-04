# MAUI DevFlow Inspector

> **Experimental preview**: The Inspector and its host integrations may change between releases.

The MAUI DevFlow Inspector is one broker-hosted interface for inspecting and driving a running
.NET MAUI app. You can open the same Inspector in:

- a browser;
- Visual Studio Code;
- the GitHub Copilot desktop app as a side canvas; or
- GitHub Copilot CLI as a canvas.

All four surfaces use the same DevFlow broker and in-app agent. Set up the app once, then choose
the host that fits your workflow.

For implementation details, protocol routes, browser modules, and retained future design, see
[MAUI DevFlow Inspector internals](inspector-internals.md).

## Choose a host

| Host | Install | Open | Best for | Current availability |
|---|---|---|---|---|
| Browser | Included with `Microsoft.Maui.Cli` | `maui devflow inspect` | Fastest setup and the complete shared Inspector UI | Available to DevFlow package users |
| VS Code | Install the source-built preview VSIX | Run **MAUI DevFlow: Open Inspector** | Source navigation, Diagnostics publication, Copilot context, and the designated native review ceremony | Source preview; not currently published to the VS Code Marketplace |
| GitHub Copilot desktop app | Use the repository-scoped Canvas extension | Ask Copilot to open the MAUI DevFlow Inspector canvas | A side canvas shared by the human and Copilot | Source-repository preview in `maui-labs` |
| GitHub Copilot CLI | Uses the same Canvas extension as the desktop app | Ask Copilot to open the MAUI DevFlow Inspector canvas | Terminal-first work with the same live side canvas | Source-repository preview in `maui-labs` |

The Canvas is registered as `maui-live-canvas` by the source folder
`.github/extensions/maui-devflow-canvas`. Those names identify the same Inspector host.

## Set up the app once

### 1. Install the CLI

Global tool installation uses the NuGet sources visible from the current directory. If an app
repository has a restrictive `NuGet.config` and cannot find `Microsoft.Maui.Cli`, run the command
from a neutral directory or use an approved configured feed. Do not modify the `maui-labs`
`NuGet.config` just to install the global tool.

```bash
dotnet tool install --global Microsoft.Maui.Cli --prerelease
```

If it is already installed:

```bash
dotnet tool update --global Microsoft.Maui.Cli --prerelease
```

Confirm which CLI will run:

```bash
dotnet tool list --global
maui version
maui devflow version
```

On PowerShell, run `Get-Command maui`; on bash or zsh, run `command -v maui`. If the executable
comes from another source checkout rather than the global tool directory, make sure that source
build and the app's DevFlow packages use compatible preview revisions. A mismatched CLI can find an
agent while failing to understand newer Inspector responses.

Source builds often both report `0.1.0-dev`, which is not enough to prove compatibility. Run
`maui devflow list --json` to find the app's registered `project` path, then compare the checkouts:

```bash
git -C <checkout-containing-the-maui-executable> rev-parse HEAD
git -C <checkout-containing-the-app-project> rev-parse HEAD
```

### 2. Add the in-app agent

For a standard .NET MAUI app:

```bash
dotnet add path/to/MyApp.csproj package Microsoft.Maui.DevFlow.Agent --prerelease
```

Replace `path/to/MyApp.csproj` with the MAUI app project's actual path.

For a Blazor Hybrid app, also add:

```bash
dotnet add path/to/MyApp.csproj package Microsoft.Maui.DevFlow.Blazor --prerelease
```

GTK apps use `Microsoft.Maui.DevFlow.Agent.Gtk` and, for Blazor Hybrid,
`Microsoft.Maui.DevFlow.Blazor.Gtk`. Repositories using Central Package Management should put the
package version in `Directory.Packages.props` and keep the project `PackageReference` versionless.
If the required preview is not on the app repository's configured feeds, see
[Nightly builds](../../README.md#nightly-builds).

See the [DevFlow package quick start](../../src/DevFlow/README.md#quick-start) and
[DevFlow onboarding skill](../../.github/skills/maui-devflow-onboard/SKILL.md) for package
selection, GTK activation, Central Package Management, and platform-specific integration details.

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

### 4. Launch and verify

Build and launch the app in a DevFlow-enabled configuration, then run:

```bash
maui devflow list
maui devflow agent status
maui devflow inspect --no-launch
```

These broker-dependent CLI commands start the broker when necessary. The app retries registration
when the broker is absent, but it does not spawn the broker itself. `maui devflow list` should show
the app, platform, agent ID, and assigned port. `maui devflow inspect --no-launch` should print an
authenticated URL without opening a browser. To verify the lower-level visual-tree API too, run
`maui devflow ui tree --depth 1`.

Platform notes:

- **Android**: Run `maui devflow diagnose --device <serial>` if the app does not register or the
  host cannot reach its assigned port. This reports the forwarding state; run
  `maui devflow list --device <serial>` to repair the mappings when one device can be selected.
  See [Broker platform connectivity](broker.md#platform-connectivity).
- **Mac Catalyst**: Sandboxed Debug builds must allow the in-app HTTP server with the
  `com.apple.security.network.server` entitlement.
- **GTK**: Start the agent after app activation with `app.StartDevFlowAgent()`.

## Open in a browser

The browser is the recommended first-run path:

```bash
maui devflow inspect
```

This command starts the broker if necessary, resolves one connected app, and opens an authenticated
Inspector URL in the default browser.

When more than one app is connected:

```bash
maui devflow list
maui devflow inspect --agent <agent-id>
```

To print the URL without launching a browser:

```bash
maui devflow inspect --no-launch
```

Do not use a manually constructed per-agent URL as the normal entry point. `maui devflow inspect`
adds the current authentication information and fails safely when app selection is ambiguous.

## Open in Visual Studio Code

The VS Code extension is currently a source-built preview. It is not published to the VS Code
Marketplace.

### Build and install the VSIX

From a `maui-labs` source checkout:

```bash
cd src/DevFlow/js
npm ci
npm run build -w @maui-devflow/client
npm run package:vsix
code --install-extension vscode-inspector/dist/maui-devflow-inspector.vsix --force
```

Use Node.js 20.19 or later, or Node.js 22.12 or later, when building both source hosts. The
installed extension requires VS Code 1.125 or later and a trusted workspace.

### Open the Inspector

1. Open the MAUI app workspace in VS Code.
2. Launch the DevFlow-enabled app.
3. Open the Command Palette.
4. Run **MAUI DevFlow: Open Inspector**.

If the app is not running, the panel stays open and retries discovery. If several apps are
connected, choose the intended app from the picker.

The VS Code host and the app do not start the broker. Before opening the panel, run a
broker-dependent command such as `maui devflow list` or start it explicitly:

```bash
maui devflow broker start
```

The off-by-default `mauiDevflow.publishDiagnostics` setting publishes runtime Problems and explicit
Layout findings into VS Code Diagnostics. The separately installed Mobile Canvas companion can be
offered as an optional MCP server with `mauiDevflow.registerMobileCanvasMcpServer`; the extension
does not ship that companion. See [The device layer](devices.md) for its installation and trust
model.

See the [VS Code Inspector README](../../src/DevFlow/js/vscode-inspector/README.md) for settings,
Copilot context attachment, source navigation, and native approval behavior.

## Open in GitHub Copilot

The GitHub Copilot desktop app and GitHub Copilot CLI use the same
`.github/extensions/maui-devflow-canvas` extension. It is currently a repository-scoped source
preview in `maui-labs`; it is not yet packaged for installation into arbitrary MAUI app
repositories.

### Prepare the Canvas extension

From a `maui-labs` source checkout:

```bash
cd src/DevFlow/js
npm ci
npm run build -w @maui-devflow/client
cd ../../../.github/extensions/maui-devflow-canvas
npm ci
```

Do not run `npm start`; the Copilot host launches `extension.mjs`.

If either legacy or copied user-scoped extension exists at
`~/.copilot/extensions/maui-live-canvas` or
`~/.copilot/extensions/maui-devflow-canvas`, move it outside `~/.copilot/extensions` or rename its
`extension.mjs` entry point before continuing. A same-name user copy can be selected instead of the
project copy in some host/session combinations; the older differently named extension can coexist
and register the same Canvas ID.

Renaming the directory inside `~/.copilot/extensions` is not sufficient because Copilot scans each
immediate extension directory for `extension.mjs`. Temporarily rename the entry point instead,
using the path that exists:

```powershell
Rename-Item "$HOME\.copilot\extensions\maui-devflow-canvas\extension.mjs" "extension.mjs.disabled"
```

```bash
mv ~/.copilot/extensions/maui-devflow-canvas/extension.mjs \
   ~/.copilot/extensions/maui-devflow-canvas/extension.mjs.disabled
```

Use `maui-live-canvas` in those commands for the older directory name. Restore the original
`extension.mjs` name when you want to re-enable the user extension.

### GitHub Copilot desktop app

1. Install or update to a GitHub Copilot desktop build that supports Canvas extensions, then sign
   in.
2. Use **Open Folder** to open the `maui-labs` repository root after preparing the extension.
3. Start a new Copilot session.
4. Launch the DevFlow-enabled MAUI app.
5. Ask: **Open the MAUI DevFlow Inspector canvas.**

### GitHub Copilot CLI

1. Start an up-to-date Copilot CLI from the `maui-labs` repository root.
2. Start a new session, or run `/clear` after preparing or changing the extension so project
   extensions are reloaded.
3. Run `/env` and confirm `maui-devflow-canvas` resolves from this repository's
   `.github/extensions` directory, not `~/.copilot/extensions`.
4. Launch the DevFlow-enabled MAUI app.
5. Ask: **Open the MAUI DevFlow Inspector canvas.**

When several apps are connected, ask Copilot to list the agents and select the app or platform you
want before making changes.

The Canvas can inspect, interact, edit live properties, record workflows, and attach bounded
context to Copilot. It is **not** a trusted native approval host and does not receive the broker
owner token. Use the VS Code Inspector for the designated native review ceremony. The explicit
`maui devflow approve` CLI is an operator convenience, not a human-attestation boundary; neither
path proves that a human rather than another same-user process made the decision.

See the [Copilot Canvas README](../../.github/extensions/maui-devflow-canvas/README.md) for its
capabilities, coordination model, and contributor tests.

## Optional Test Workbench

The base Inspector does not require preview flags. To expose the experimental Test Workbench, set
`DEVFLOW_PREVIEW_WORKBENCH=true` in the broker process environment before the broker starts, then
restart the broker. Additional authoring and trace features have separate flags.

See [DevFlow human-authored tests](testing.md) for the current workflow, flags, safety boundaries,
and platform qualification status.

## Troubleshooting

| Symptom | What to do |
|---|---|
| `maui devflow list` shows no app | Confirm the agent package and `AddMauiDevFlowAgent()` registration are present, launch a DevFlow-enabled build, then run `maui devflow diagnose`. |
| More than one app is connected | Browser: use `maui devflow inspect --agent <agent-id>`. VS Code: use the app picker. Canvas: ask Copilot to list and select an agent. |
| Inspector is waiting for the broker | Run `maui devflow broker status`, then `maui devflow broker start` if needed. |
| Android app registers but cannot be inspected | Run `maui devflow diagnose --device <serial>` to explain ADB reverse/forward state, then `maui devflow list --device <serial>` to repair the mappings. |
| Browser Inspector works but `ui tree` reports no canonical Inspector tree | Check `Get-Command maui` or `command -v maui`, then compare `maui devflow version` with the versions shown by `dotnet list <app.csproj> package`. If both are source builds reporting `0.1.0-dev`, use `maui devflow list --json` and compare their Git SHAs as described above. Restart the broker and app after selecting a compatible CLI. Use `maui devflow broker log` if the revisions match. |
| VS Code command is missing | Install the generated VSIX, confirm VS Code 1.125 or later, and trust the workspace. |
| `npm ci` can run but its child scripts cannot find `node` on Windows | Run both `node --version` and `cmd /c node --version`. Ensure the Node installation directory is on `PATH` for child `cmd.exe` processes, then open a new terminal. |
| Copilot Canvas is missing or disconnected while the app is live | Prepare its dependencies, temporarily rename any same-purpose user extension's `extension.mjs` entry point, and open a new session from the `maui-labs` repository. Use `/env` in Copilot CLI to confirm the project extension is loaded. |

For broker logs, lifecycle, file locations, and detailed connectivity recovery, see
[Broker daemon](broker.md#troubleshooting).

## Related documentation

- [DevFlow overview and package quick start](../../src/DevFlow/README.md)
- [Inspector internals](inspector-internals.md)
- [Broker daemon](broker.md)
- [VS Code Inspector host](../../src/DevFlow/js/vscode-inspector/README.md)
- [GitHub Copilot Canvas host](../../.github/extensions/maui-devflow-canvas/README.md)
- [DevFlow human-authored tests](testing.md)
- [DevFlow device layer and Mobile Canvas](devices.md)
- [DevFlow HTTP and WebSocket specification](spec/README.md)
