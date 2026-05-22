# DevFlow Web Inspector

> **Status**: Design spec — V1 implementation in progress

## Overview

The DevFlow Web Inspector serves a running MAUI app as a fully interactive HTML page. An external inspector tool (or any browser) connects to a local URL and sees the app rendered as a live, clickable web page — complete with DOM elements matching the native visual tree.

This enables any HTML-based inspector tool to work with a native MAUI app without custom integration. The inspector tool sees a normal website; all interaction (taps, scrolls, gestures) is transparently proxied to the real app.

## Architecture

```
┌─────────────────────┐         ┌──────────────────────────┐         ┌─────────────────────┐
│  Inspector Tool /   │  HTTP   │  CLI Inspector Server     │  HTTP   │  DevFlow Agent      │
│  Browser            │ ◄─────► │  (localhost:5223)         │ ◄─────► │  (device:9223)      │
│                     │         │                          │         │                     │
│  Sees: HTML page    │         │  - Generates HTML        │         │  - Visual tree API  │
│  Does: Click/scroll │         │  - Proxies API calls     │         │  - Screenshot API   │
│                     │         │  - WebSocket relay       │         │  - Action endpoints │
└─────────────────────┘         └──────────────────────────┘         └─────────────────────┘
```

The inspector server runs on the **developer's machine** (as part of the `maui` CLI). The DevFlow agent runs **inside the native app** on any platform (device, emulator, simulator, desktop). The CLI handles agent discovery, ADB port forwarding, and all the connection plumbing.

## Usage

```bash
# Start the inspector server (opens at http://localhost:5223)
maui devflow inspector

# Custom port
maui devflow inspector --port 8080

# Target a specific agent
maui devflow inspector --agent-port 9223

# Target an Android device
maui devflow inspector --device emulator-5554
```

## Generated HTML Structure

The inspector server generates an interactive HTML page with three layers:

### Layer 1: Toolbar
```html
<nav id="devflow-toolbar">
  <button id="btn-back" title="Navigate back">←</button>
  <button id="btn-refresh" title="Refresh">↻</button>
  <span id="connection-status">● Connected</span>
</nav>
```

### Layer 2: App Viewport with Screenshot
```html
<div id="app-viewport" style="position:relative; width:{W}px; height:{H}px;">
  <img id="screenshot" src="/screenshot.png"
       style="position:absolute; top:0; left:0; width:100%; height:100%; pointer-events:none;">
```

### Layer 3: Element Divs (Transparent, Positioned)
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

### Layer 4: Interaction Script
```html
<script src="/devflow.js"></script>
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

### WebSocket Auto-Refresh (V1)

```javascript
const ws = new WebSocket(`ws://${location.host}/ws/events`);
ws.onmessage = (e) => {
  const event = JSON.parse(e.data);
  if (event.type === 'treeChange' || event.type === 'navigation') {
    refreshPage(); // re-fetch tree + screenshot, rebuild DOM
  }
};
ws.onclose = () => {
  document.getElementById('connection-status').textContent = '○ Disconnected';
  setTimeout(connectWebSocket, 2000);
};
```

### Text Input via Overlay (V1.1)

When user taps on an element with `data-type="Entry"` or `data-role="textbox"`:
1. Show a floating `<input>` element positioned over the element bounds
2. Pre-fill with current `data-text` value
3. On blur or Enter: send `POST /api/fill` with full text value
4. Remove overlay, refresh screenshot

### URL-Based Navigation (V1.2)

Shell routes map to browser URL paths:
- `http://localhost:5223/MainPage` → navigate to `//MainPage`
- `http://localhost:5223/Settings` → navigate to `//Settings`
- `http://localhost:5223/Detail?id=42` → navigate to `//Detail?id=42`

Browser back/forward maps to app navigation. `history.pushState` keeps the URL in sync.

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

## Screenshot Refresh Strategy

After any user interaction:
1. Wait ~100ms for the app to settle (animations, layout)
2. Fetch new `/screenshot.png`
3. Swap the `<img>` src (avoids full page rebuild for simple interactions)

On `treeChange`/`navigation` WebSocket events:
- Full page rebuild (re-fetch tree + screenshot, regenerate DOM)

## Versioned Roadmap

### V1 — Interactive Mirror (Current)

| Feature | Implementation |
|---------|---------------|
| Screenshot background | `<img src="/screenshot.png">` |
| Element divs with data-* | Nested positioned divs from tree |
| Click → tap | Coordinate-based POST |
| Scroll → scroll | Wheel event → delta POST |
| Drag → gesture | Pointer events → path POST |
| Back | Toolbar button → POST |
| Auto-refresh | WebSocket → page rebuild |
| Toolbar | Back, refresh, connection status |

### V1.1 — Text Input

| Feature | Implementation |
|---------|---------------|
| Text entry | Overlay `<input>` on Entry/Editor elements |
| Key press | `keydown` → POST /api/key |
| Clear | Select-all + delete |

### V1.2 — Navigation & Editing

| Feature | Implementation |
|---------|---------------|
| URL = Shell route | Browser path maps to navigate endpoint |
| Deep linking | Opening URL navigates app |
| pushState | Navigation events update browser URL |
| Property editing | PUT endpoint from inspector |

## Implementation Files

| File | Purpose |
|------|---------|
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/InspectorServer.cs` | HTTP server, API proxy, WebSocket relay |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/HtmlRenderer.cs` | Visual tree → interactive HTML generation |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/devflow.js` | Client-side interaction handlers |
| `src/Cli/Microsoft.Maui.Cli/Microsoft.Maui.Cli.csproj` | EmbeddedResource for devflow.js |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/DevFlowCommands.cs` | `inspector` subcommand registration |
