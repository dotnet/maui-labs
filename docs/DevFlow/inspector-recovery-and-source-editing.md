# Inspector recovery, control, and source editing

For app setup and host installation, see the [Inspector guide](inspector.md). The
[Inspector internals](inspector-internals.md) describe the shared layout, frame model, mutation
lease, and workflow recording. This page covers host recovery and source-editing behavior.

## Host recovery and selected apps

VS Code and Canvas keep their Inspector panels open while the broker or app restarts. Their
reconnecting shells distinguish broker, app, selected-target, and multiple-app states, poll for
recovery, and expose an immediate **Retry** action instead of requiring the user to close and reopen
the Inspector.

The VS Code Inspector can also be opened before the broker or app is running. It shows what it is
waiting for and reconnects automatically. VS Code opens an app picker when several agents are
available; Canvas directs Copilot to its existing list/select-agent controls. After an app is
selected, the panel preserves that choice even if the app disappears before connection completes.
Neither host silently substitutes an unrelated app when the selected process exits.

Registrations with project-relative paths require an explicit app choice after a process restart:
their default session IDs can be shared by different worktrees. The same registered process
reconnects automatically; a replacement process can also reconnect automatically when a unique
full project path, framework, platform, app name, and session identity match.

The Canvas `shell.mjs` uses `renderShell` for the connected Inspector iframe and `renderDisconnected`
for the reconnecting shell. Both states share the `--df-*` theme-token language with the VS Code
host shell.

## Control handoff

Browser, VS Code, Canvas, MCP, and CLI mutations share one
[app-scoped mutation lease](inspector-internals.md#global-mutation-lease). Read-only inspection
remains available while another host is driving the app.

Copilot and other MCP clients can inspect the current holder with `maui_control_status`, request an
available lease with `maui_take_control`, and return it with `maui_release_control`. A forced
takeover requires explicit user approval because it interrupts the current driving Inspector or
automation session.

## Debug source locations and workspace recovery

The broker resolves privacy-preserving project-relative entries before returning them to a host.
The plain-browser fallback therefore copies an absolute local path plus line number that can be
pasted directly into local tooling.

For project-relative metadata, the broker captures its source-search workspace when it starts.
Launch it from the app's workspace, or set `MAUI_DEVFLOW_PROJECT_ROOT` to that workspace before
starting the broker when an editor or MCP host launches processes from elsewhere. This local
setting is not embedded in the app. Android and iOS use this workspace rather than treating a
device process ID as a host process. Multiple matches or an incomplete bounded search still
refuse to open or write a source file; an absolute registered project path is another option for
explicit local-debug targeting.

An invalid, missing, or unsupported source workspace is reported without stopping the broker.
Workspace-based source discovery stays unavailable rather than falling back to another working
directory; ordinary runtime inspection and independently resolvable local source paths remain
usable. Network, UNC, and Windows device paths are not supported for local XAML source access.

Live property editing remains runtime-only until **Apply to XAML** is selected. The
[source-editing restrictions](inspector-internals.md#apply-property-values-to-xaml) still apply:
only supported existing direct-literal attributes can be written, and stale or ambiguous source
locations are rejected.
