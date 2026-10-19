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
full project path, framework, platform, app name, and non-empty session identity match.
Legacy registrations without session metadata require an explicit choice after the process
changes; they can still reconnect to the same registered process.

New agent IDs use 32 lowercase hex characters from SHA-256. IDs are assigned by
the running broker, not stored as app identities. An agent uses the ID returned
by its broker, including an older broker's shorter ID. Restart the broker and
reconnect the app to use the longer IDs.

VS Code keeps selection and Data context per Inspector panel. Activating another Inspector
restores its own context, including an empty selection; background panels cannot overwrite it.

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

VS Code resolves local source paths through parent symbolic links and junctions before deciding
whether an outside-workspace confirmation is required. A linked file outside the workspace can
still be opened after confirmation; its lexical path cannot bypass that prompt.

Live property editing remains runtime-only until **Apply to XAML** is selected. The
[source-editing restrictions](inspector-internals.md#apply-property-values-to-xaml) still apply:
only supported existing direct-literal attributes can be written, and stale or ambiguous source
locations are rejected.

New XAML source hashes use the first 16 bytes of SHA-256 of the UTF-8 source text,
as 32 lowercase hex characters. The agent, CLI source editor, and VS Code host
use the same format. Rebuild the app with the current DevFlow agent before
**Apply to XAML** if its metadata still has a 16-character hash. The CLI does not
write source files using that older metadata. VS Code treats it as stale.

New layout finding and suppression hashes also retain 32 hex characters per
hash component. Existing 16-character suppression keys, including disambiguated
finding IDs, still match their original prefixes. New policy entries use the
longer keys. The Inspector can remove an existing shorter project suppression
without adding a second entry. Policy reads and writes take both the old and
new named locks, in that order, so an older CLI cannot write the same policy
at the same time.
