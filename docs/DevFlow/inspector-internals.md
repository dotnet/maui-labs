# MAUI DevFlow Inspector internals

> **Contributor reference**: For installation, host selection, and first-run instructions, see the
> [MAUI DevFlow Inspector setup guide](inspector.md).

The Inspector is one broker-hosted web application shared by the browser, VS Code, and GitHub
Copilot Canvas hosts. Host integrations embed the same page and negotiate additional capabilities;
they do not reimplement the Inspector UI.

## Runtime architecture

```text
Browser / VS Code / Copilot Canvas
                  |
                  | HTTP + WebSocket
                  v
DevFlow broker-hosted Inspector
  /inspector/{agentId}/
                  |
                  | direct agent HTTP after broker discovery
                  v
DevFlow agent inside the running MAUI app
```

- The **agent** runs in the MAUI app and exposes the visual tree, screenshots, properties,
  interaction, diagnostics, and app data.
- The **broker** tracks running agents and hosts each authenticated Inspector instance.
- The **CLI** starts the broker on demand for broker-dependent commands and prepares Android ADB
  reverse/forward mappings when it can select a device.
- The **app does not start the broker**. Its agent retries registration until a broker becomes
  available.
- The **VS Code host does not start the broker**. The Copilot Canvas client uses its own
  `bootstrapBroker: "once"` policy.

The browser base path is `/inspector/{agentId}/`. Browser code must resolve API and WebSocket URLs
against that base path rather than using origin-root `/api/*` URLs.

## Page and asset model

The page is assembled from embedded HTML, CSS, and focused ES modules under
`src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/`. `InspectorServer.Routes.cs` explicitly maps
every browser asset. `AssetRoutesAndEmbeddedBrowserResourcesMatchExactly` verifies that the route
table and embedded-resource set agree in both directions.

Key modules:

| Module | Responsibility |
|---|---|
| `devflow.js` | Page orchestration, live refresh, interaction, Data dock, recording, and host bridge coordination |
| `inspector-api.js` | Token-aware, base-path-aware HTTP requests |
| `inspector-tree.js` | Visual-tree rendering, selection, expansion, and keyboard navigation |
| `inspector-properties.js` | Property descriptors, editors, live mutation, and persistence proposals |
| `inspector-diagnostics.js` | Problems and performance presentation |
| `inspector-video.js` | Optional live device video |
| `inspector-host-bridge.js` | Capability-negotiated VS Code and Canvas bridge |
| `inspector-workbench.js` and related modules | Preview-gated testing workflows |
| `inspector-agent-requests.js` | Agent request review and native-host handoff |

## Rendered element contract

`HtmlRenderer` renders bounded elements as flat, absolutely positioned sibling `<div>` elements
over the current screenshot. `data-parentId` preserves the projected hierarchy for the tree view.

The HTML includes a selected scalar subset of `ElementInfo` plus derived availability flags:

| Attribute | Meaning |
|---|---|
| `data-id`, `data-parentId` | Projected element identity and hierarchy |
| `data-type`, `data-fullType`, `data-framework` | Framework type information |
| `data-automationId` | Automation identity when present |
| `data-stableItemKey`, `data-collectionScope` | Stable collection identity when available |
| `data-text`, `data-value`, `data-role` | Bounded visible semantics |
| `data-isVisible`, `data-isEnabled`, `data-isFocused`, `data-opacity` | Current state |
| `data-interactable` | Derived visible/enabled interactive-or-scrollable flag |
| `data-traits`, `data-gestures`, `data-styleClass` | Bounded behavior and style metadata |
| `data-nativeType` | Platform-native type name when available |
| `data-hasSource` | Boolean indicating that source can be resolved on demand |

Native and framework property dictionaries and absolute source paths are deliberately not embedded
in every element. The property and source APIs fetch those details on demand.

## Refresh and interaction

The Inspector uses a WebSocket-first refresh model:

1. It checks `/api/eventSupport`.
2. When supported, it connects to `/ws/events` and refreshes immediately after relevant app events.
3. A three-second `/api/state` poll runs only while the WebSocket is not live.

The optional device video stream can replace repeated screenshot fetches while it is live. Tree
state continues to refresh independently.

Interactions use broker-relative endpoints such as `/api/tap`, `/api/scroll`, `/api/gesture`,
`/api/fill`, and `/api/setProperty`. Every mutation participates in the shared broker mutation
lease used by the browser, VS Code, Canvas, CLI, and MCP surfaces.

## Inspector routes

The following routes are relative to `/inspector/{agentId}/`.

### Read and streaming routes

| Route | Method | Purpose |
|---|---|---|
| `/` | GET | Inspector page |
| `/api/state` | GET | Current rendered frame state |
| `/api/inspect/snapshot` | GET | Canonical `activeVisual` snapshot |
| `/api/eventSupport` | GET | Event-stream capability check |
| `/api/device/host` | GET | Optional broker device-layer catalog proxy; returns unavailable when no host is paired |
| `/api/flows/replay/evidence` | GET | Download retained replay evidence |
| `/screenshot.png` | GET | Frame screenshot |
| `/ws/events` | WebSocket | Broker-proxied agent UI events |

### Representative action and data routes

| Route | Method | Purpose |
|---|---|---|
| `/api/tap`, `/api/scroll`, `/api/gesture`, `/api/back` | POST | App interaction |
| `/api/fill`, `/api/key`, `/api/navigate` | POST | Input and navigation |
| `/api/hitTest`, `/api/inspect/query` | POST | Canonical element resolution |
| `/api/getProperties`, `/api/getProperty` | POST | On-demand property reads |
| `/api/setProperty`, `/api/persistProperty` | POST | Live mutation and reviewed persistence |
| `/api/problems`, `/api/diagnostics/layout` | POST | Runtime and layout diagnostics |
| `/api/logs`, `/api/network`, `/api/preferences` | POST | Bounded app data |
| `/api/flows/record/*`, `/api/flows/files/*`, `/api/flows/validate`, `/api/flows/diff`, `/api/flows/commit`, `/api/flows/replay` | POST | Recording, files, validation, commit, and replay |
| `/api/plans/*` | POST | Plan listing, loading, validation, and saving |

The in-app agent also exposes its direct versioned `/api/v1/*` API. Those agent routes are not a
substitute for the broker-relative Inspector routes above.

## Preview flags and optional surfaces

The server injects enabled preview flags as `<meta>` tags. The page hides preview UI that is not
advertised, and the server independently gates each preview route.

| Environment variable | Surface |
|---|---|
| `DEVFLOW_PREVIEW_WORKBENCH` | Guided Goal -> Record -> Review -> Run -> Results workflow |
| `DEVFLOW_PREVIEW_AGENT_AUTHORING` | Agent requests inbox |
| `DEVFLOW_PREVIEW_REPAIR_PROPOSALS` | Reviewed selector repair |
| `DEVFLOW_PREVIEW_SOURCE_PROPOSALS` | Reviewed source proposals |
| `DEVFLOW_PREVIEW_TRACE_IMPORT_EXPORT` | Trace import/export |

Layout diagnostics is routed and advertised by this layer. The `/api/device/host` GET route is also
implemented as a broker proxy, but the device panel remains unadvertised by
`devflow-surface-device-host` in this layer. Route existence and panel advertisement are therefore
separate concerns for the optional device integration.

## Embedding hosts

### VS Code

`src/DevFlow/js/vscode-inspector` embeds the shared page and contributes:

- `MAUI DevFlow: Open Inspector`;
- `mauiDevflow.brokerPort`;
- `mauiDevflow.openLocation`;
- `mauiDevflow.publishDiagnostics`; and
- `mauiDevflow.registerMobileCanvasMcpServer`.

The extension contributes no chat participant or language-model tools. Its only MCP definition
provider is the off-by-default, separately installed Mobile Canvas companion. It can publish
runtime Problems and Layout findings into VS Code Diagnostics when explicitly enabled.

VS Code is the designated native review host for approval and layout-policy mutation ceremonies.
Its modal and owner-token access are outside the embedded page, but same-user token access is not
proof that a human rather than a local process made the decision.

### GitHub Copilot Canvas

`.github/extensions/maui-devflow-canvas` embeds the shared page and adds agent-callable selection,
interaction, recording, and context-attachment actions. It does not advertise `nativeApproval`,
hold the broker owner token, or provide source-apply authority.

## Reviewed source proposals

`DEVFLOW_PREVIEW_SOURCE_PROPOSALS` exposes XAML and C# proposal analysis, preview, status, and
rejection. This layer does not route source grant, approval, apply, verification, or rollback.
Rejecting a proposal discards a review object; it does not mutate source.

## Current and retained future behavior

Current behavior includes:

- the screenshot-backed flat element overlay and visual-tree panel;
- WebSocket-first refresh with polling fallback;
- interaction, hit testing, property reads, live property editing, and reviewed persistence;
- the Inspector toolbar, Data dock, recorder, diagnostics, evidence, and preview Workbench; and
- VS Code and Copilot Canvas host bridges.

Retained future ideas include mapping browser URL paths to MAUI Shell routes and browser-history
deep linking. The current user command is `maui devflow inspect`; there is no standalone
`maui devflow inspector` server command.

## Implementation files

| File | Purpose |
|---|---|
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/InspectorServer.cs` | Inspector state, route handlers, security, and broker integration |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/InspectorServer.Routes.cs` | Browser asset and API route tables |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/InspectorSnapshotService.cs` | Canonical `activeVisual` snapshots |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/HtmlRenderer.cs` | Flat element overlay rendering |
| `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/` | Shared browser UI |
| `src/DevFlow/js/vscode-inspector/` | VS Code host |
| `.github/extensions/maui-devflow-canvas/` | GitHub Copilot Canvas host |
| `src/DevFlow/Microsoft.Maui.DevFlow.Inspector.Tests/` | Live browser integration tests |
| `src/DevFlow/Microsoft.Maui.DevFlow.Tests/` | Broker, routing, security, and contract tests |
