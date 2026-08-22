# Generative UI — Adaptive Whole-Component Architecture

> **Status:** Implemented v0.4 in `Microsoft.Maui.AI.GenerativeUI` and
> `AIExtensions.Sample.Garden`.

## 1. Purpose

The runtime lets a model adapt the information architecture of a real MAUI application without
giving it control over application code or trusted behavior. The model chooses and arranges
registered whole components from a complete catalog. The application owns all component
implementations, styles, accessibility, navigation, data access, and mutations.

The Garden reference app proves the pattern across Home, Catalog, Product, Cart, and Orders rather
than in a blank canvas or isolated demo page.

## 2. Trust boundaries

The model may:

- select registered whole components;
- arrange them with `Stack`, `Grid`, `Tabs`, and `Section`;
- choose enum-constrained layout presets and registered component variants;
- bind components to advertised derived-data paths;
- explain why a layout fits the current intent.

The model may not:

- emit C#, XAML, styles, resources, event handlers, or arbitrary control types;
- create primitive leaves such as `Label`, `Button`, or `Entry`;
- access unadvertised state paths;
- replace fixed Shell navigation or essential actions;
- mutate Garden server state through layout generation.

The destination assistant receives only typed Garden tools and fixed-shell navigation tools. It does
not receive `OpenApiExplorerTools`, `GenerativeUiTools`, or a compose tool.

## 3. Runtime architecture

```text
Typed Garden server
  └─ shared DTOs + source-generated JSON
       └─ GardenApiClient
            └─ GardenDataStore (canonical client cache)
                 ├─ typed chat tools
                 └─ GardenAdaptiveContextFactory
                      └─ AdaptiveStateProjector -> UiObject snapshot
                           └─ AdaptiveSurfaceComposer
                                ├─ catalog/data manifest
                                ├─ model layout generator
                                ├─ validator + one correction retry
                                └─ checked-in standard fallback
                                     └─ AdaptiveRegionRenderer
                                          └─ app-authored native components
```

The typed server and shared records are canonical. `UiObject` exists only as an observable projection
for component binding and reconciliation.

## 4. Fixed shell and adaptive regions

`AppShell` owns routes and the navigation model. Each page keeps its title, back behavior, fixed
essential actions, loading/error UI, and retry affordance outside the adaptive region.

`AdaptiveContentPage` creates one `AdaptiveSurfaceSession` per page instance and attaches one or more
named `AdaptiveRegionView` hosts. On appearance it:

1. loads typed state;
2. selects the appropriate populated or empty standard layout;
3. renders that standard immediately;
4. activates the coordinator for background adaptation.

The session is suspended when the page disappears and disposed when its page instance is popped.
State, mounted views, generations, and cache keys cannot leak between page instances.

## 5. Layout contract

`ComponentLayoutDocument` is a flat, non-recursive node table:

- identity: `layoutId`, `revision`, `surface`;
- region roots: a region name and root node ID;
- nodes: stable ID, enum kind, parent ID, order, optional layout preset, optional component binding,
  and a required reason.

Allowed node kinds are `Stack`, `Grid`, `Tabs`, `Section`, and `Component`. Grid layouts use named
presets instead of model-authored dimensions. Component nodes refer to catalog aliases, advertised
data paths, and registered variants.

Flat IDs make validation, stale-result rejection, semantic reconciliation, and AOT-safe
serialization deterministic.

## 6. Catalog and data manifest

The app registers every whole component with:

- alias and model-facing description;
- data contract and allowed surface regions;
- required/optional binding facets;
- valid variants.

For each composition, the model receives the full component catalog plus a manifest of available
derived data. The resolver removes candidates whose required facets are unavailable. Surface
descriptors can require one of a group of components so that adapted layouts preserve essential
content, including populated and empty-state contracts.

## 7. Automatic composition

There is no compose chat tool. `AdaptiveSurfaceCoordinator` owns composition as page infrastructure:

1. render the standard layout;
2. gather surface, viewport, typed state, and recent user intent;
3. project typed state into the session's `UiObject`;
4. generate a layout in the background;
5. validate structure, catalog aliases, regions, bindings, and required groups;
6. retry once with structured validation feedback;
7. reject stale/cancelled results;
8. reconcile stable nodes and animate changed regions.

Chat publishes normalized user intent to the coordinator before tool execution. The latest intent and
recent context flow across Shell navigation, allowing the destination page to adapt automatically.
Viewport changes are debounced. Page activation, navigation, and a newer generation cancel older work.

## 8. Failure and reset behavior

The standard layout is always a valid usable result. Network failures display fixed error/retry UI
without replacing good content. Generation or validation failure preserves the current valid layout;
on first load that is the standard.

An "Adapted for …" status identifies the active intent and explanation. Reset cancels pending work,
clears presentation intent, and restores the standard layout without changing canonical server data.

## 9. Actions and approval

Whole components receive `IGardenComponentActions`; they do not execute model-authored handlers.
Human taps invoke `GardenApiClient` through the store directly and refresh the affected adaptive
surface.

Typed chat reads and navigation execute without approval. Every typed chat mutation is decorated with
`ApprovalRequired = true`, so the assistant pauses and shows the existing approval UI before changing
cart, order, or review state. AI route requests may use the fixed navigation tools without approval.

## 10. Compatibility surfaces

The reusable library still contains its generic primitive UI DSL, observable binding model, and
OpenAPI processor. They remain tested public APIs for research and other consumers, but the Garden
application does not register them with its assistant.

The old typed product composition adapter, product scaffold harness, metrics comparison, mode picker,
and duplicate blank Garden app were removed once all five real surfaces reached parity.

## 11. Acceptance criteria

- Home, Catalog, Product, Cart, and Orders render useful checked-in standards immediately.
- Each surface can adapt using only registered whole components.
- Fixed navigation, add/remove, checkout, open, reorder, retry, and reset remain reachable.
- Chat state and intent survive navigation; surface state does not leak between page instances.
- Empty Catalog, Cart, and Orders states remain explicit and usable.
- Narrow and wide layouts preserve readable content and chat access.
- AI failure never blanks the application.
