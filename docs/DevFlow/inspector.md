# DevFlow Web Inspector

> **Scope of this doc.** The first half (Overview through Inspector Server Routes) describes what ships today in `maui devflow broker`. The [Future Work](#future-work) section at the bottom collects design ideas (toolbar UI, text-input overlays, URL routing, a standalone `inspector` subcommand) that are **not yet implemented**.

## Overview

The DevFlow Web Inspector serves a running MAUI app as a fully interactive HTML page. An external inspector tool (or any browser) connects to a local URL and sees the app rendered as a live, clickable web page — complete with DOM elements matching the native visual tree.

This enables any HTML-based inspector tool to work with a native MAUI app without custom integration. The inspector tool sees a normal website; all interaction (taps, scrolls, gestures, fill, key) is transparently proxied to the real app.

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

The inspector is served by the **DevFlow broker** running on the developer's machine. The DevFlow agent runs **inside the native app** on any platform (device, emulator, simulator, desktop). The broker handles agent discovery, ADB port forwarding, and all the connection plumbing.

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

The broker also accepts `/inspector/{agentId}` without a trailing slash and 301-redirects to `/inspector/{agentId}/` so that the page's relative asset URLs (`devflow.css`, `devflow.js`) resolve correctly.

## Generated HTML Structure

The inspector page is built from two layers wrapped in a minimal HTML shell:

### Layer 1: App Viewport with Screenshot

```html
<div id="app-viewport" style="position:relative; width:{W}px; height:{H}px;">
  <img id="screenshot" src="/screenshot.png"
       style="position:absolute; top:0; left:0; width:100%; height:100%; pointer-events:none;">
```

### Layer 2: Element Divs (Transparent, Positioned)

```html
  <div class="devflow-element"
       data-id="elem_1"
       data-type="ContentPage"
       data-fullType="Microsoft.Maui.Controls.ContentPage"
       data-automationId=""
       data-isVisible="true"
       data-isEnabled="true"
       style="position:absolute; left:0px; top:0px; width:390px; height:844px;">

    <div class="devflow-element"
         data-id="elem_5"
         data-type="VerticalStackLayout"
         data-fullType="Microsoft.Maui.Controls.VerticalStackLayout"
         data-isVisible="true"
         data-isEnabled="true"
         style="position:absolute; left:0px; top:88px; width:390px; height:600px;">

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
           style="position:absolute; left:16px; top:32px; width:358px; height:44px;">
      </div>
    </div>
  </div>
</div>
```

### Layer 3: Interaction Script + Styles

```html
<link rel="stylesheet" href="devflow.css">
<script src="devflow.js"></script>
```

Both assets are served from the broker as embedded resources alongside the page (no CDN, no external requests).

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
| `/api/v1/ui/elements?type=X&text=Y&automationId=Z` | GET | Query/filter elements | Future: in-page search |
| `/api/v1/ui/elements/{id}` | GET | Full details for one element | Used by `maui_element` |
| `/api/v1/ui/elements/{id}/properties/{name}` | GET | Read specific property | Used by `maui_get_property` |
| `/api/v1/ui/hit-test?x=N&y=N` | GET | Find element at coordinates | Map click to element |
| `/api/v1/ui/screenshot` | GET | PNG screenshot | Background image |

### Action Endpoints

| Endpoint | Method | Purpose | Inspector Use |
|----------|--------|---------|---------------|
| `/api/v1/ui/actions/tap` | POST | Tap element by ID or coordinates | Click handler |
| `/api/v1/ui/actions/scroll` | POST | Scroll by delta or to index | Wheel event handler |
| `/api/v1/ui/actions/gesture` | POST | Touch gesture (swipe, drag, pinch) | Pointer drag handler |
| `/api/v1/ui/actions/back` | POST | Navigate back | Browser-side `Esc` handler |
| `/api/v1/ui/actions/fill` | POST | Fill text into Entry/Editor | Used by `/api/fill` proxy |
| `/api/v1/ui/actions/key` | POST | Send key press | Used by `/api/key` proxy |
| `/api/v1/ui/actions/focus` | POST | Focus an element | Auto on tap |
| `/api/v1/ui/actions/resize` | POST | Resize window | Not used by inspector |
| `/api/v1/ui/actions/batch` | POST | Multiple actions at once | Future optimization |

### WebSocket

| Endpoint | Purpose | Inspector Use |
|----------|---------|---------------|
| `/ws/v1/ui/events` | Real-time UI events | Relayed to browser at `/ws/events` |

#### Event Types

| Event | When | Inspector Action |
|-------|------|-----------------|
| `treeChange` | After tap, fill, scroll, property set | Browser may refresh (consumer-defined) |
| `navigation` | Shell route changed | Browser may refresh (consumer-defined) |
| `lifecycle` | App started/stopped | Show connection status |

Inspector pages today refresh via AJAX polling against `/api/state`; the WebSocket relay is available for tools that want push-driven refresh (see [Future Work](#future-work)).

Subscriptions look like:
```json
{"type": "subscribe", "data": {"events": ["treeChange", "navigation"]}}
```

The inspector relay subscribes to `["all"]` on the browser's behalf.

## Interaction Model

The shipped inspector uses AJAX polling for refresh and direct fetch calls for interaction. Each handler proxies through the local broker; the broker forwards to the agent.

### Click → Tap

```javascript
viewport.addEventListener('click', async (e) => {
  const rect = viewport.getBoundingClientRect();
  const x = e.clientX - rect.left;
  const y = e.clientY - rect.top;
  await fetch('api/tap', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ x, y })
  });
});
```

### Wheel → Scroll

```javascript
viewport.addEventListener('wheel', async (e) => {
  e.preventDefault();
  const rect = viewport.getBoundingClientRect();
  await fetch('api/scroll', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      x: e.clientX - rect.left,
      y: e.clientY - rect.top,
      deltaX: e.deltaX,
      deltaY: e.deltaY
    })
  });
});
```

### Pointer Drag → Gesture

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

viewport.addEventListener('pointerup', async () => {
  if (gesturePoints.length > 1) {
    await fetch('api/gesture', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ points: gesturePoints })
    });
  }
  gesturePoints = [];
});
```

### AJAX State Refresh

The page polls `GET /api/state` every ~500ms for an updated screenshot URL and serialized element divs, then swaps them into the DOM without a full page reload.

## Inspector Server Routes

| Route | Method | Description |
|-------|--------|-------------|
| `/` | GET | Generated interactive HTML page |
| `/api/state` | GET | JSON poll endpoint: `{ screenshot, elements }` |
| `/screenshot.png` | GET | Proxied PNG from agent (cached ~200ms per element/page id) |
| `/devflow.js` | GET | Embedded interaction script |
| `/devflow.css` | GET | Embedded stylesheet |
| `/api/tap` | POST | Proxy → agent `/api/v1/ui/actions/tap` |
| `/api/scroll` | POST | Proxy → agent `/api/v1/ui/actions/scroll` |
| `/api/gesture` | POST | Proxy → agent `/api/v1/ui/actions/gesture` |
| `/api/back` | POST | Proxy → agent `/api/v1/ui/actions/back` |
| `/api/fill` | POST | Proxy → agent `/api/v1/ui/actions/fill` |
| `/api/key` | POST | Proxy → agent `/api/v1/ui/actions/key` |
| `/ws/events` | WS | Bidirectional relay → agent `/ws/v1/ui/events` |

State-mutating POST routes reject cross-origin requests via `LocalOriginValidator` (the broker port is part of the allowed-origin set). The WebSocket upgrade also enforces the same check, since the browser same-origin policy does not block cross-origin WebSocket opens.

## Screenshot Refresh Strategy

- Screenshots are cached for ~200ms keyed by `(rootPageId, elementId)` so concurrent pollers (state + image element) share one capture.
- After a successful tap/scroll/gesture/fill/key, the cache is invalidated so the next poll picks up the new frame.
- `/api/state` honors the cache (it no longer force-invalidates it on every call).

## Implementation Files

| File | Purpose |
|------|---------|
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/InspectorServer.cs` | HTTP server, API proxy, WebSocket relay |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/HtmlRenderer.cs` | Visual tree → interactive HTML generation |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector.html` | HTML shell template |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/devflow.js` | Client-side interaction handlers + AJAX poll |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/devflow.css` | Stylesheet |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Broker/BrokerServer.cs` | Per-agent inspector mounting + `/inspector/{id}` routing |

---

## Future Work

Everything below describes ideas that **are not implemented today**. They are kept here as a design sketch for the next iterations; do not assume the corresponding endpoints, UI, or commands exist.

### Standalone `maui devflow inspector` command

A top-level subcommand that connects directly to a single agent (bypassing the broker) is planned. Expected flags: `--port`, `--agent-port`, `--device`. Currently the inspector lives only inside the broker.

### Toolbar UI

A persistent toolbar atop the viewport with refresh, back, and connection-status indicators:

```html
<nav id="devflow-toolbar">
  <button id="btn-back" title="Navigate back">←</button>
  <button id="btn-refresh" title="Refresh">↻</button>
  <span id="connection-status">● Connected</span>
</nav>
```

### WebSocket Push Refresh

Today the page polls `/api/state`. A future iteration could subscribe to the WebSocket relay and rebuild the DOM on `treeChange` / `navigation` events, eliminating the poll interval:

```javascript
const ws = new WebSocket(`ws://${location.host}/ws/events`);
ws.onmessage = (e) => {
  const event = JSON.parse(e.data);
  if (event.type === 'treeChange' || event.type === 'navigation') {
    refreshPage();
  }
};
ws.onclose = () => {
  document.getElementById('connection-status').textContent = '○ Disconnected';
  setTimeout(connectWebSocket, 2000);
};
```

### Text Input via Overlay

When the user taps an `Entry`/`Editor`, a floating `<input>` could be positioned over its bounds, pre-filled with `data-text`, and `POST /api/fill` on blur or Enter. The `/api/fill` and `/api/key` proxies already exist; the overlay UI does not.

### URL-Based Navigation

Map Shell routes to browser URL paths so back/forward and bookmarks work:

- `http://localhost:19223/inspector/{id}/MainPage` → `/MainPage`
- `http://localhost:19223/inspector/{id}/Detail?id=42` → `/Detail?id=42`

This would require route-aware handling in the broker plus `history.pushState` from `devflow.js`.

### Inline Property Editing

Use `PUT /api/v1/ui/elements/{id}/properties/{name}` to allow live editing of property values from the inspector — e.g. clicking a Label's `data-text` opens an editor and writes back.
