# DevFlow Web Inspector

> **Status**: Mixed — the broker-hosted inspector at `http://localhost:19223/inspector/` is implemented and shipping in `maui devflow broker`. Sections that describe a standalone `maui devflow inspector` command, a `<nav id="devflow-toolbar">`, deep-link routing via URL paths, or a nested element tree are **future design** and are kept here for reference. See the [Usage](#usage) section for the current concrete commands.

## Overview

The DevFlow Web Inspector serves a running MAUI app as a fully interactive HTML page. An external inspector tool (or any browser) connects to a local URL and sees the app rendered as a live, clickable web page — complete with DOM elements matching the native visual tree.

This enables any HTML-based inspector tool to work with a native MAUI app without custom integration. The inspector tool sees a normal website; all interaction (taps, scrolls, gestures) is transparently proxied to the real app.

## Architecture

```
┌─────────────────────┐         ┌──────────────────────────┐         ┌─────────────────────┐
│  Inspector Tool /   │  HTTP   │  CLI Inspector Server     │  HTTP   │  DevFlow Agent      │
│  Browser            │ ◄─────► │  (localhost:19223)       │ ◄─────► │  (device:9223+)     │
│                     │         │  (broker-hosted)         │         │                     │
│  Sees: HTML page    │         │  - Generates HTML        │         │  - Visual tree API  │
│  Does: Click/scroll │         │  - Proxies API calls     │         │  - Screenshot API   │
│                     │         │  - WebSocket relay       │         │  - Action endpoints │
└─────────────────────┘         └──────────────────────────┘         └─────────────────────┘
```

The inspector is currently served by the **DevFlow broker** running on the developer's machine. The DevFlow agent runs **inside the native app** on any platform (device, emulator, simulator, desktop). The broker handles agent discovery, ADB port forwarding, and all the connection plumbing.

## Usage

```bash
# Start the broker (the inspector is served at http://localhost:19223/inspector/)
maui devflow broker start

# Then connect any MAUI app with the DevFlow agent — it will auto-register.
# Open the agent list at:
#   http://localhost:19223/inspector/
# Or jump straight to the only connected agent:
#   http://localhost:19223/inspector/default/
# Or by agent id:
#   http://localhost:19223/inspector/{agentId}/
```

> The standalone `maui devflow inspector` command (with `--port`, `--agent-port`, `--device` flags) shown in earlier drafts is **future work**; today the inspector lives inside the broker.

`maui devflow inspect` resolves the connected agent, starts the broker if it is not already
running, and opens the authenticated per-agent Inspector URL. Pass `--agent <agent-id>` when more
than one app is connected, and `--no-launch` to print the URL instead of opening a browser.

## Browser modules

The page is one document assembled from focused ES modules. Every module is an embedded resource
under `DevFlow/Inspector/Web/` and is routed explicitly by `InspectorServer.Routes.cs`; the
`AssetRoutesAndEmbeddedBrowserResourcesMatchExactly` test asserts the route table and the embedded
set are the same set, in both directions.

| Module | Responsibility |
|---|---|
| `devflow.js` | Orchestration: live state refresh, interaction, recording/replay, Data dock, layout, theme. |
| `inspector-api.js` | Token-aware JSON POST/GET wrapper used by every feature controller. |
| `inspector-dialog.js` | Host-independent accessible confirmation dialog. |
| `inspector-tree.js` | Visual-tree rendering, expansion, selection, keyboard navigation. |
| `inspector-properties.js` | Property descriptors, typed editors, live updates, XAML persistence controls. |
| `inspector-data-context.js` / `-data-controller.js` / `-data-ui.js` | Bounded, redacted Data snapshots and their rendering. |
| `inspector-diagnostics.js` | Problems and performance presentation. |
| `inspector-evidence.js` | Evidence bundle preview, confirmation, download. |
| `inspector-video.js` | Live video surface for hosts that can stream. |
| `inspector-host-bridge.js` | The negotiated capability registry for embedding hosts. |
| `inspector-workbench.js` / `-plan` / `-steps` / `-run` / `-trace` / `-repair` / `-improve` / `-source` | Preview-gated Test Workbench surfaces. |
| `inspector-agent-requests.js` | Agent approval inbox and the native approval ceremony. |
| `inspector-study.js` | Authoring-time study instrumentation. |

## Preview flags and optional surfaces

The server publishes what it enabled as `<meta>` tags; the page hides everything it is not told
about. This is presentation only — every preview route re-checks the same flags server-side, so a
forged meta buys nothing but a 404.

| Meta | Environment variable | Reveals |
|---|---|---|
| `devflow-preview-workbench` | `DEVFLOW_PREVIEW_WORKBENCH` | The guided Goal → Record → Review → Run → Results journey. |
| `devflow-preview-agent-authoring` | `DEVFLOW_PREVIEW_AGENT_AUTHORING` | The Agent requests approval inbox. |
| `devflow-preview-repair` | `DEVFLOW_PREVIEW_REPAIR_PROPOSALS` | The reviewed selector-repair panel. |
| `devflow-preview-source` | `DEVFLOW_PREVIEW_SOURCE_PROPOSALS` | The reviewed XAML/C# source proposal panel (review-only — see below). |
| `devflow-preview-trace-import` | `DEVFLOW_PREVIEW_TRACE_IMPORT_EXPORT` | Trace import/export. |

With no flags set the product presents only the durable specialized surface: visual tree,
properties, screenshots, hit-test and interaction, Data, recorder, video, and the Blazor bridge.
With `DEVFLOW_PREVIEW_AGENT_AUTHORING` alone the Tests toggle appears but every guided stage stays
hidden, so the panel opens directly on Agent requests rather than an empty shell.

One panel ships its browser code here but stays hidden because this layer serves no route for it:
the managed device host (`/api/device/host`). Layout diagnostics (`/api/diagnostics/*`) is served by
this layer, so its panel is advertised and visible.
`InspectorServer.OptionalSurfaces` is the single place that records this, and
`EveryOptionalSurfaceIsAdvertisedExactlyWhenItsRouteIsServed` probes each route to prove the
advertisement and the routing agree.

### Layout diagnostics panel (experimental)

The **Layout** panel runs an explicit, read-only scan through `POST /api/diagnostics/layout` and
renders typed findings with per-rule coverage and explicit limitations. It never watches: a new
scan happens only when the user asks for one.

Suppressions are proposals, not writes. `/api/diagnostics/suppress` and `/api/diagnostics/unsuppress`
return a proposal bound to the exact suppression key, diagnostics revision, agent instance, policy
file digest, and an expiry. `/api/diagnostics/suppression/apply` accepts that proposal only with a
confirmation capability issued to a trusted native approval host, and the write is a
compare-and-swap against the recorded digest. VS Code is the only host that can obtain that
confirmation; the Canvas Inspector and a standalone browser copy the proposal for human review
instead.

`report.systemEvidence` is filled only when the scan came through the broker's optional composite
route and a device was paired with this agent at exact confidence. In every other case — no device
host, no pairing, an ambiguous pairing, or no hierarchy from the host — the panel shows an
app-scoped scan and never claims a keyboard, permission dialog, alert, or share sheet was ruled in
or out. When the section is shown, its `status` is shown with it: `incomplete` means the capture
could not be aligned with these findings, and its element list is deliberately empty.

## Editor hosts and the trusted native approval

Two hosts embed the same page rather than re-implementing it:

- **VS Code** — `src/DevFlow/js/vscode-inspector`. It contributes exactly one command
  (`MAUI DevFlow: Open Inspector`) and three settings (`mauiDevflow.brokerPort`,
  `mauiDevflow.openLocation`, `mauiDevflow.publishDiagnostics`). It deliberately contributes no chat
  participant, no language-model tools, and no MCP definition provider, because it registers none of
  them. With `mauiDevflow.publishDiagnostics` enabled — off by default — it publishes runtime
  Problems and explicit layout findings into VS Code Diagnostics.
- **GitHub Copilot Canvas** — `.github/extensions/maui-devflow-canvas`.

Both reach the page through a nonce'd `postMessage` bridge and advertise a capability manifest, so
the page only offers what its host can actually do.

**VS Code is the only trusted native approval host.** Approving an agent request is the one place a
host mints authority, and it is a two-step ceremony neither the page nor chat can perform:

1. The extension shows a real modal — `showWarningMessage(…, { modal: true })` in the *extension*
   process, not in the webview — describing the exact request, scope, and grant length.
2. Only after the human confirms does the extension read the owner-only native approval token from
   the local broker state and `POST /api/workbench/approval-confirmations/issue`.
3. The broker returns a single-use capability bound to this target, subject, and a digest of the
   reviewed scope. The extension immediately redeems it on
   `POST /api/workbench/agent-requests/{id}/approve` **without** the owner token.

The owner token never reaches browser JavaScript, the capability is consumed on first use, and a
replay of the same capability is refused. `humanConfirmed` travels with the approve call, but it is
not the authority: it is a caller-supplied boolean, and the broker issues the grant only against the
capability. A browser or chat message can never substitute for either step.

Canvas carries **no** approval authority in this layer. It may inspect, interact, and record, but it
advertises no `nativeApproval` capability, holds no owner token, and serves no approval route — a
`window.confirm()` in a canvas webview is a surface the embedded page can reach, so it is not
evidence that the local human agreed.

## Reviewed source proposals are inert here

`DEVFLOW_PREVIEW_SOURCE_PROPOSALS` reveals the reviewed XAML/C# AutomationId proposal panel, and in
this layer that panel is **read-only**. The broker serves `analyze`, `propose`, `status`, `preview`
and `reject`; there is no grant, approve, apply, host-apply handoff, verification, or rollback route
for either language, and no host advertises a source-apply capability. Rejecting a proposal only
discards a review object, so the browser read token is enough for it; approving or narrowing one
would need a trusted native host capability, which nothing here offers. Source *apply* — and the
build/replay verification that must accompany it — lands in its own dedicated branch.

## Generated HTML Structure

The inspector server generates an interactive HTML page with two layers:

### Layer 1: App Viewport with Screenshot
```html
<div id="app-viewport" data-width="{W}" data-height="{H}" style="width:{W}px; height:{H}px;">
  <img id="screenshot" src="screenshot.png" alt="App screenshot">
```

### Layer 2: Element Divs (Flat, Positioned)

All elements are rendered as **flat siblings** (not nested) using window-absolute bounds
adjusted by the root page offset. This ensures 1:1 alignment with the screenshot regardless
of whether the app is showing a modal, sheet, or safe-area-offset page.

```html
  <div class="devflow-element"
       data-id="elem_1"
       data-type="ContentPage"
       data-fullType="Microsoft.Maui.Controls.ContentPage"
       data-isVisible="true"
       data-isEnabled="true"
       style="position:absolute; left:0px; top:0px; width:390px; height:844px;"></div>

  <div class="devflow-element"
       data-id="elem_6"
       data-type="Button"
       data-fullType="Microsoft.Maui.Controls.Button"
       data-automationId="btnSubmit"
       data-text="Click Me"
       data-role="button"
       data-isVisible="true"
       data-isEnabled="true"
       data-isFocused="false"
       data-opacity="1"
       data-traits="interactive,focusable"
       data-gestures="tap"
       style="position:absolute; left:16px; top:120px; width:358px; height:44px;"></div>
</div>
```

### Layer 3: Interaction Script
```html
<script src="devflow.js"></script>
```

## Element Attributes

Each `<div class="devflow-element">` carries `data-*` attributes using the **exact DevFlow JSON property names** (camelCase). This gives a 1:1 mapping with the agent API — no translation needed.

| Attribute | Source (`ElementInfo`) | Description |
|-----------|----------------------|-------------|
| `data-id` | `id` | DevFlow element ID |
| `data-parentId` | `parentId` | Parent element ID |
| `data-type` | `type` | Short type name (Button, Label, Entry) |
| `data-fullType` | `fullType` | Full .NET type (Microsoft.Maui.Controls.Button) |
| `data-framework` | `framework` | Always "maui" |
| `data-automationId` | `automationId` | AutomationId for testing |
| `data-text` | `text` | Text content |
| `data-value` | `value` | Value property |
| `data-role` | `role` | Accessibility role (button, textbox, checkbox, etc.) |
| `data-isVisible` | `isVisible` | Visibility state |
| `data-isEnabled` | `isEnabled` | Enabled state |
| `data-isFocused` | `isFocused` | Focus state |
| `data-opacity` | `opacity` | Opacity (0–1) |
| `data-traits` | `traits` | Comma-separated: interactive, focusable, scrollable, header |
| `data-gestures` | `gestures` | Comma-separated: tap, swipe, etc. |
| `data-styleClass` | `styleClass` | Comma-separated CSS style classes |
| `data-nativeType` | `nativeType` | Platform native type (e.g., Android.Widget.Button) |
| `data-nativeProperties` | `nativeProperties` | JSON-encoded native property dictionary |
| `data-frameworkProperties` | `frameworkProperties` | JSON-encoded MAUI property dictionary |

> **Note**: HTML `data-*` attributes with camelCase suffixes work correctly. The DOM `dataset` API auto-converts them (e.g., `data-automationId` → `element.dataset.automationid`), but inspector tools read the raw attribute strings directly.

## Agent UI Endpoints Reference

The DevFlow agent exposes these UI endpoints. The inspector uses them as follows:

### Read Endpoints

| Endpoint | Method | Purpose | Inspector Use |
|----------|--------|---------|---------------|
| `/api/v1/ui/tree` | GET | Full visual tree (nested ElementInfo) | Generate HTML DOM structure |
| `/api/v1/ui/tree?depth=N` | GET | Tree limited to N levels | Optimize for deep trees |
| `/api/v1/ui/elements?type=X&text=Y&automationId=Z` | GET | Query/filter elements | Future: search |
| `/api/v1/ui/elements/{id}` | GET | Full details for one element | On-demand detail fetch |
| `/api/v1/ui/elements/{id}/properties/{name}` | GET | Read specific property | Property inspection |
| `/api/v1/ui/hit-test?x=N&y=N` | GET | Find element at coordinates | Map click to element |
| `/api/v1/ui/screenshot` | GET | PNG screenshot | Background image |

### Action Endpoints

| Endpoint | Method | Purpose | Inspector Use |
|----------|--------|---------|---------------|
| `/api/v1/ui/actions/tap` | POST | Tap element by ID or coordinates | Click handler |
| `/api/v1/ui/actions/scroll` | POST | Scroll by delta or to index | Wheel event handler |
| `/api/v1/ui/actions/gesture` | POST | Touch gesture (swipe, drag, pinch) | Pointer drag handler |
| `/api/v1/ui/actions/back` | POST | Navigate back | Toolbar back button |
| `/api/v1/ui/actions/fill` | POST | Fill text into Entry/Editor | Text input (V1.1) |
| `/api/v1/ui/actions/clear` | POST | Clear text from element | Text input (V1.1) |
| `/api/v1/ui/actions/key` | POST | Send key press | Key events (V1.1) |
| `/api/v1/ui/actions/focus` | POST | Focus an element | Auto on tap |
| `/api/v1/ui/actions/navigate` | POST | Shell navigation by route | URL navigation (V1.2) |
| `/api/v1/ui/actions/resize` | POST | Resize window | Not needed |
| `/api/v1/ui/actions/batch` | POST | Multiple actions at once | Optimization |

### Mutation Endpoints

| Endpoint | Method | Purpose | Inspector Use |
|----------|--------|---------|---------------|
| `/api/v1/ui/elements/{id}/properties/{name}` | PUT | Set property value | Live editing (V1.2) |

### WebSocket

| Endpoint | Purpose | Inspector Use |
|----------|---------|---------------|
| `/ws/v1/ui/events` | Real-time UI events | Auto-refresh page |

#### Event Types

| Event | When | Inspector Action |
|-------|------|-----------------|
| `treeChange` | After tap, fill, scroll, property set | Rebuild DOM + refresh screenshot |
| `navigation` | Shell route changed | Rebuild DOM + refresh screenshot |
| `lifecycle` | App started/stopped | Show connection status |

Clients can subscribe to specific events:
```json
{"type": "subscribe", "data": {"events": ["treeChange", "navigation"]}}
```

## Interaction Model

### Click → Tap (V1)

```javascript
viewport.addEventListener('click', async (e) => {
  const rect = viewport.getBoundingClientRect();
  const x = e.clientX - rect.left;
  const y = e.clientY - rect.top;
  await fetch('/api/tap', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ x, y })
  });
  await refreshScreenshot();
});
```

### Wheel → Scroll (V1)

```javascript
viewport.addEventListener('wheel', async (e) => {
  e.preventDefault();
  const rect = viewport.getBoundingClientRect();
  await fetch('/api/scroll', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      x: e.clientX - rect.left,
      y: e.clientY - rect.top,
      deltaX: e.deltaX,
      deltaY: e.deltaY
    })
  });
  await refreshScreenshot();
});
```

### Pointer Drag → Gesture (V1)

```javascript
let gesturePoints = [];

viewport.addEventListener('pointerdown', (e) => {
  gesturePoints = [{ x: e.offsetX, y: e.offsetY, t: Date.now() }];
  viewport.setPointerCapture(e.pointerId);
});

viewport.addEventListener('pointermove', (e) => {
  if (gesturePoints.length > 0) {
    gesturePoints.push({ x: e.offsetX, y: e.offsetY, t: Date.now() });
  }
});

viewport.addEventListener('pointerup', async (e) => {
  if (gesturePoints.length > 1) {
    await fetch('/api/gesture', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ points: gesturePoints })
    });
    await refreshScreenshot();
  }
  gesturePoints = [];
});
```

### AJAX Refresh (Current Implementation)

```javascript
// Poll every 3 seconds — no full page reload, no flash
setInterval(async () => {
  const resp = await fetch(`${basePath}/api/state`);
  const state = await resp.json();
  // Update screenshot src, patch element divs via DOM diff
  screenshot.src = state.screenshotUrl;
  patchElements(state.elements);
}, 3000);
```

> **Note**: A WebSocket relay at `/ws/events` (→ agent `/ws/v1/ui/events`) is available
> for external clients. The bundled `devflow.js` uses AJAX polling as the primary strategy.

## Inspector Server Routes

| Route | Method | Description |
|-------|--------|-------------|
| `/` | GET | Generated interactive HTML page |
| `/screenshot.png` | GET | Proxied PNG from agent (cached ~200ms) |
| `/devflow.js` | GET | Embedded interaction script |
| `/api/tap` | POST | Proxy → agent `/api/v1/ui/actions/tap` |
| `/api/scroll` | POST | Proxy → agent `/api/v1/ui/actions/scroll` |
| `/api/gesture` | POST | Proxy → agent `/api/v1/ui/actions/gesture` |
| `/api/back` | POST | Proxy → agent `/api/v1/ui/actions/back` |
| `/api/fill` | POST | Proxy → agent `/api/v1/ui/actions/fill` (V1.1) |
| `/api/key` | POST | Proxy → agent `/api/v1/ui/actions/key` (V1.1) |
| `/api/tree` | GET | Proxy → agent `/api/v1/ui/tree` |
| `/ws/events` | WS | Proxy → agent `/ws/v1/ui/events` |

## Refresh Strategy

The inspector uses **AJAX polling** (every 3 seconds) via `GET /api/state`:
1. Fetch JSON containing a timestamped screenshot URL + rendered element HTML
2. Update `<img>` src to the new screenshot URL (no flash)
3. Smart DOM diff: patch only changed elements, preserving hover/selection state

A WebSocket relay (`/ws/events` → agent `/ws/v1/ui/events`) is also available for
external clients that want push-based updates. The bundled `devflow.js` uses AJAX
polling as the primary strategy for simplicity and reliability.

## Versioned Roadmap

### V1 — Interactive Mirror (Current — Implemented)

| Feature | Implementation |
|---------|---------------|
| Screenshot background | `<img src="screenshot.png">` |
| Element divs with data-* | Flat positioned divs from tree |
| Click → tap | Coordinate-based POST (with root offset adjustment) |
| Scroll → scroll | Wheel event → delta POST |
| Drag → gesture | Pointer events → swipe direction POST |
| AJAX refresh | 3-second polling via `/api/state` |
| Modal support | Screenshots topmost page; overlays offset-corrected |
| Text fill | POST `/api/fill` |
| Key press | POST `/api/key` |

### V1.1 — Future: Navigation & Editing

| Feature | Implementation |
|---------|---------------|
| Toolbar | Back, refresh, connection status controls |
| URL = Shell route | Browser path maps to navigate endpoint |
| Deep linking | Opening URL navigates app |
| pushState | Navigation events update browser URL |
| Property editing | PUT endpoint from inspector |

## Implementation Files

| File | Purpose |
|------|---------|
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/InspectorServer.cs` | HTTP server, API proxy, WebSocket relay |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/HtmlRenderer.cs` | Visual tree → flat positioned HTML generation |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/LocalOriginValidator.cs` | Origin-based CORS/CSRF protection |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector.html` | HTML template (viewport + placeholders) |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/devflow.js` | Client-side: AJAX polling, click/scroll/gesture handlers |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/devflow.css` | Inspector viewport styles |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Broker/BrokerServer.cs` | Broker integration: routes `/inspector/*` to InspectorServer |
| `src/DevFlow/Microsoft.Maui.DevFlow.Inspector.Tests/` | Playwright integration tests |
