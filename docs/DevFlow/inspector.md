# DevFlow Web Inspector

The DevFlow Web Inspector mirrors a running .NET MAUI app as a screenshot plus an interactive
visual-tree overlay. It is served directly at `http://localhost:19223/inspector/`.

The MAUI DevFlow Inspector host integrations embed that existing page in:

- MAUI DevFlow Inspector for VS Code;
- MAUI DevFlow Inspector for GitHub Copilot Canvas.

The broker-hosted page remains the **DevFlow Web Inspector**. **MAUI DevFlow Inspector** is the
public name for the host integrations added around it; all hosts embed the same page.

## Start the inspector

Add and start the DevFlow agent in a Debug build, launch the app, then:

```bash
maui devflow broker start
```

Open the broker URL above, run **MAUI DevFlow: Open Inspector** in VS Code, or open the MAUI
DevFlow Inspector in GitHub Copilot Canvas.

## Features

- screenshot and visual-tree inspection with hover, selection, and searchable hierarchy;
- a prominent disconnected-state overlay that preserves and clearly labels the last captured frame
  while DevFlow waits for the app to reconnect;
- tap, fill, scroll, gesture, navigation, theme, and live property mutation, with explicit
  **Apply to XAML** persistence for existing direct-literal attributes and runtime-owned property
  editor metadata;
- logs, live-updating network, preferences, device, sensor, native Alerts, read-only file browsing,
  and WebView/CDP data docks;
- click-to-XAML source navigation for Debug source maps;
- one integrated Workflow panel for recording, loading project `maui-tests` files or local Markdown
  files, replaying them, and reviewing per-step results;
- an **Add to Copilot** context menu for the selected element, loaded workflow, both together, or
  the current bounded and redacted Data snapshot, including alert metadata;
- responsive light, dark, and high-contrast host theming.

## Architecture

```text
Browser / VS Code / Canvas
          |
          v
DevFlow broker-hosted inspector
          |
          v
In-app DevFlow agent
```

The broker discovers agents and serves the HTML/CSS/JavaScript bundle. Inspector mutations are
proxied to the selected in-app agent. VS Code and Canvas embed the same page and add an
authenticated host bridge for source navigation, recording persistence, and Copilot context.

## Host-adaptive layout

The shared inspector keeps one interaction model while adapting its chrome to the host viewport:

- **Wide browser/editor:** tree, screenshot, and properties are docked as three panes.
- **Compact editor:** the tree remains available while properties open as a drawer, preserving
  screenshot width.
- **Narrow Canvas/editor:** the screenshot is primary; tree and properties become coordinated
  full-height drawers with a scrim.
- **Short host:** drawers and overlay Data/timeline sheets protect the screenshot's vertical budget.

The toolbar keeps interaction mode, tree, fit, and recording visible. Secondary actions remain
inline for as long as they fit; only the non-fitting actions move into the **More** menu. More can
open over an active Data or properties surface, and Copilot choices open as a nested submenu.
Host bridges also supply their color palette, font metadata, contrast mode, and reduced-motion
preference. VS Code placement is configurable with
`mauiDevflow.openLocation` (`auto`, `beside`, or `active`).

## Coordinated frames and coordinates

Each inspector refresh creates an immutable frame containing:

- the visual tree used for the overlay;
- the exact screenshot bytes;
- screenshot dimensions;
- the screenshotted root page and its window offset;
- rendered element HTML.

The screenshot URL includes the frame ID. Expired frame screenshots return `404`, causing the
client to refresh state instead of pairing a new screenshot with stale bounds. When a modal or
sheet is screenshotted, only that page subtree is rendered, preventing underlying window chrome
from producing negative offsets.

Browser coordinates are converted from the fit-scaled viewport back to screenshot coordinates,
then translated to window coordinates with the frame's root offset before hit testing.

## Global mutation lease

All state-changing calls use one lease per running app. Browser tabs, VS Code, Canvas, MCP, and CLI
therefore cannot drive the app concurrently.

- The first mutating host claims the lease.
- Read-only inspection remains available to other hosts.
- A host can release or explicitly take over the lease.
- Lease release or takeover hands an active app-scoped recording to the next valid lease holder.
- Closing Canvas releases its lease; abandoned leases also expire automatically.

The lease coordinates writers; it is not an authentication boundary.

## Workflow recording

Recording is owned by the broker and scoped to the current app. The current valid mutation lease
holder controls it. The agent observes successful supported mutations from every host, so a
workflow can begin in the browser, continue through Canvas or MCP after lease handoff, and stop in
VS Code without separate local recorders.

The Workflow panel can also load an existing test from the registered app project's top-level
`maui-tests` directory or from an OS-selected `.md` file. Project files are confined to that
directory, capped at 1 MB, and parsed and validated before Inspector makes them replayable.
Replay results stay in the same Workflow panel instead of opening a separate report surface.

Currently normalized actions include tap, fill, scroll, navigate, back, theme changes, and property
changes. The result is a Markdown file with an authoritative `json maui-test` block for replay.
Replay is blocked while recording is active.

## Click-to-XAML

The Agent.Core package supplies a build-transitive source generator. In Debug builds it maps XAML
elements to source file, line, column, and a build-time content hash. VS Code compares the current
file hash before opening the recorded line and warns when the file changed after the app was built.

Source locations are emitted only when the runtime element can be matched conservatively to its
XAML declaration. Repeated same-type siblings need sibling-unique `AutomationId` values; otherwise
their source actions are withheld rather than risk opening or editing the wrong declaration.

Source maps are disabled outside Debug by default because they embed XAML text and source paths.
They can be disabled explicitly:

```xml
<DevFlowXamlSourceMapsEnabled>false</DevFlowXamlSourceMapsEnabled>
```

### Apply property values to XAML

For source-mapped elements, each supported property row includes an **Apply to XAML** button. Live
editing remains runtime-only until this button is selected. The broker then updates only an
existing direct-literal attribute in the registered app project while preserving the rest of the
file, its encoding, and line endings.

The write is rejected when the property comes from a binding, resource, markup extension, style,
property element, template, or code-created element. It is also rejected when the source changed
outside Inspector after the app was built or after the previous Inspector write. Rebuild the app
to refresh stale source maps.

The broker validates the value against the running element before writing and restricts edits to
the agent-advertised property grid. Current agents describe editor kind, current value, writability,
enum choices, and numeric constraints from the runtime control. It binds relative project names to the build's default path-derived
DevFlow session identity; builds using a custom `MauiDevFlowSessionId` should also set
`MauiDevFlowIncludeProjectPath=true` to provide an unambiguous project root.

## Platform boundaries

The WebView data tab lists attached Blazor WebViews, displays page source, and evaluates JavaScript
through the existing CDP bridge. Every expression requires confirmation because arbitrary
JavaScript can read or change live application data. A bundled Chrome DevTools frontend is
intentionally outside the Inspector scope; use external browser platform tools when the
full DOM, console, network, and debugger experience is required.

System dialogs and MAUI alerts are outside the in-app MAUI visual tree. The **Alerts** data tab uses
the existing platform drivers to detect and dismiss them without pretending they are selectable
MAUI elements. Detection remains read-only. Dismissal requires the mutation lease and is blocked
while workflow replay is driving the app.

- Android actions target only the online device whose existing ADB forward owns the selected
  agent port. Missing or ambiguous ownership is rejected.
- Windows and Mac Catalyst actions refresh and use the exact app process ID reported by the agent.
- Linux actions connect the platform driver to the exact selected agent.
- iOS alert control remains CLI-only because Inspector registration does not carry a simulator
  UDID. Use the platform alert driver explicitly:

```bash
maui devflow ui alert detect --device <simulator-udid>
maui devflow ui alert dismiss "OK" --device <simulator-udid>
```

DevFlow Action request bodies are passed to the registered action. A generic
`include: ["screenshot", "tree"]` field does not add post-action captures; use the CLI/MCP
post-action options or request the screenshot and tree explicitly.

## Compatibility

Current clients remain read/write compatible with older agents that do not expose the lease
endpoint. Older clients cannot mutate current agents because they do not send a lease identity.
Inspector falls back to its legacy static property table when an older agent does not expose
runtime descriptors. The Node client negotiates `ui.events`; unsupported agents enter a stable
`polling-only` state and recheck the capability every 60 seconds so an in-place upgrade recovers
without reconnect churn. Upgrade the agent packages and host tooling together.

## Implementation

| Area | Location |
|---|---|
| Inspector server and proxy | `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/` |
| Shared web UI | `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/` |
| Broker routing and coordination | `src/Cli/Microsoft.Maui.Cli/DevFlow/Broker/` |
| XAML source maps | `src/DevFlow/Microsoft.Maui.DevFlow.Agent.Core/SourceMapping/` |
| Shared Node client | `src/DevFlow/js/devflow-client/` |
| VS Code host | `src/DevFlow/js/vscode-inspector/` |
| Copilot Canvas host | `.github/extensions/maui-devflow-canvas/` |
| Playwright tests | `src/DevFlow/Microsoft.Maui.DevFlow.Inspector.Tests/` |
