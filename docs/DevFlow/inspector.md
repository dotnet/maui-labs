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
