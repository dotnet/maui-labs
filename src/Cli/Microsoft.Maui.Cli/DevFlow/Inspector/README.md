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

Feature modules receive dependencies through factory options or function arguments. They must not import `devflow.js` or mutate unrelated global state. Keep cross-feature coordination in `devflow.js`; move cohesive rendering/state logic into a module when it can expose a narrow API.

New browser modules must be:

1. included by the `Web\**\*` embedded-resource glob;
2. explicitly routed by `InspectorServer` with `application/javascript`;
3. covered by `InspectorModulesAreServedAsJavaScript`;
4. syntax-checked and exercised through the live Inspector tests.
