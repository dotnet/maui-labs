# DevFlow Web Inspector internals

The broker serves the Inspector as embedded static assets from `Web/`.

## JavaScript boundaries

| File | Responsibility |
|---|---|
| `devflow.js` | Application orchestration: live state refresh, app interaction, recording/replay, host bridge, Data dock, layout, and theme coordination. |
| `inspector-api.js` | Token-aware JSON POST wrapper used by feature controllers. |
| `inspector-dialog.js` | Host-independent accessible confirmation dialog. |
| `inspector-tree.js` | Visual-tree rendering, expansion state, selection, and keyboard navigation. |
| `inspector-properties.js` | Property descriptors, typed editors, live updates, and XAML persistence controls. |
| `inspector-data-context.js` | Pure Data snapshot bounding and secret redaction. It must remain DOM-independent so its security contract can be tested directly. |
| `inspector-data-controller.js` / `inspector-data-ui.js` | Data dock state and rendering over that snapshot. |
| `inspector-diagnostics.js` | Pure Problems/Performance presentation, filtering, and the bounded text-safe payload sent through the Copilot bridge. |
| `inspector-evidence.js` | Evidence bundle preview, confirmation dialog, and download. Its plan formatting and download-name helpers are pure so they can be tested directly. |
| `inspector-video.js` | Live video surface for hosts that can stream frames. |
| `inspector-host-bridge.js` | The negotiated host capability registry and request/response plumbing. |
| `inspector-workbench.js` and its `plan`/`steps`/`run`/`trace`/`repair`/`improve`/`source` panels | Preview-gated Test Workbench surfaces. |
| `inspector-agent-requests.js` | Agent approval inbox and the native approval ceremony. |
| `inspector-study.js` | Authoring-time study instrumentation. |

Feature modules receive dependencies through factory options or function arguments. They must not import `devflow.js` or mutate unrelated global state. Keep cross-feature coordination in `devflow.js`; move cohesive rendering/state logic into a module when it can expose a narrow API.

New browser modules must be:

1. included by the `Web\**\*` embedded-resource glob;
2. explicitly routed by `InspectorServer` with `application/javascript`;
3. covered by `AssetRoutesAndEmbeddedBrowserResourcesMatchExactly`, which asserts the route table and the embedded set match in both directions;
4. syntax-checked and exercised through the live Inspector tests.

## Preview flags and optional surfaces

`HandleRootAsync` injects a `<meta>` tag for each enabled preview flag and for each optional
surface this build actually routes. The page hides what it is not told about. That is presentation
only: `IsPreviewRouteEnabled` re-checks the same flags for every preview route, so a page that
forges a meta tag still gets a 404.

`InspectorServer.OptionalSurfaces` is the single record of panels whose browser code ships here but
whose route arrives in a later layer — layout diagnostics and the managed device host. Serving one
of those routes means flipping its `Served` flag in the same edit, and
`EveryOptionalSurfaceIsAdvertisedExactlyWhenItsRouteIsServed` probes the route to prove the two
agree.

## Reviewed source proposals are review-only

`DEVFLOW_PREVIEW_SOURCE_PROPOSALS` reveals `inspector-source.js`, and in this layer that panel can
only read. The routed source actions are `analyze`, `propose`, `status`, `preview` and `reject` for
both XAML and C#; there is no grant, approve, apply, host-apply, verification, or rollback route,
`InspectorHostIdentity` and the host-capability contracts are gone, and no host advertises a
source-apply capability. That matters because the removed routes decided trust from a
caller-supplied `hostKind`/`humanConfirmed` pair, which any holder of the browser read token could
set. `InspectorSourceAuthorityAbsenceTests` drives a broker-backed Inspector with that token and a
spoofed `hostKind: "vscode"` body to prove every one of those paths is a routing-table miss.

Reject stays available to the read token because it only discards a review object. Approving or
narrowing a proposal would require a trusted native host capability, and nothing in this layer
offers one.
