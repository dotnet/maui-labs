# Appendix — Adaptive Whole-Component Composer

> **Status:** Implemented v0.4.

## 1. Contract

The model returns one `ComponentLayoutDocument`. It is a flat table rather than a recursive control
tree, which keeps the contract small, enum-constrained, and straightforward to validate.

```json
{
  "layoutId": "catalog-active",
  "revision": 3,
  "surface": "Catalog",
  "explanation": "Show visual browsing first for the user's herb-garden goal.",
  "regions": [
    { "region": "CatalogBody", "rootNodeId": "catalog-root" }
  ],
  "nodes": [
    {
      "id": "catalog-root",
      "kind": "Grid",
      "order": 0,
      "gridPreset": "PrimaryWithSidebar",
      "reason": "Keep products dominant and recommendations visible."
    },
    {
      "id": "products",
      "kind": "Component",
      "parentId": "catalog-root",
      "order": 0,
      "component": "CatalogGrid",
      "dataPath": "catalog",
      "variant": "default",
      "reason": "The user asked to browse products visually."
    },
    {
      "id": "recommendations",
      "kind": "Component",
      "parentId": "catalog-root",
      "order": 1,
      "component": "RecommendationStrip",
      "dataPath": "recommendation",
      "variant": "compact",
      "reason": "Keep the current garden goal in context."
    }
  ]
}
```

Allowed kinds are `Stack`, `Grid`, `Tabs`, `Section`, and `Component`. Stack orientation and grid
preset are enums. A component node must reference a registered alias; there is no arbitrary primitive
leaf or style payload.

## 2. Catalog

`GenerativeUiRegistry` stores descriptors for app-authored whole components. A descriptor includes:

- stable alias and full use/avoid guidance;
- typed data contract;
- required and optional binding facets;
- allowed adaptive regions;
- supported variants.

The Garden app supplies the full catalog to every model request. This makes all valid alternatives
visible without requiring a discovery or compose tool.

## 3. Data projection

`GardenAdaptiveContextFactory` gathers canonical typed values from `GardenDataStore`.
`AdaptiveStateProjector` converts that snapshot to `UiObject` for rendering. The request contains a
data manifest describing available paths and facets; it does not expose arbitrary object traversal.

Projection updates existing nodes where possible so mounted components continue observing the same
binding objects.

## 4. Generation and validation

`AdaptiveLayoutGenerator` uses source-generated JSON metadata and a strict schema. The validator
checks:

1. surface and region ownership;
2. unique node IDs and valid parent/root references;
3. cycle-free flat hierarchy;
4. allowed node-kind properties and enum values;
5. registered component aliases, variants, regions, and data paths;
6. required binding facets;
7. surface-required component groups.

Invalid model output is returned as structured correction feedback for one retry. A second invalid
result is not rendered.

## 5. Sessions, ordering, and cache

`AdaptiveSurfaceSession` owns one surface instance's projected state, standard/current layout,
mounted views, region hosts, state version, and generation counter. `BeginGeneration` and
`IsCurrentGeneration` prevent late results from replacing newer state.

The cache key includes surface context rather than sharing a mutable session. Cached plans are
revalidated before use. Suspending or disposing a session invalidates pending generation.

## 6. Reconciliation

`AdaptiveRegionRenderer` builds container nodes and resolves whole components from DI. Stable node IDs
and compatible semantics allow existing views to move or reconfigure instead of being recreated.
Removed components are detached from observable state.

The renderer only mounts into named `AdaptiveRegionView` hosts. Fixed page chrome and essential
actions are outside its authority.

## 7. Standard fallback

Every surface has a checked-in `GardenAdaptiveLayouts` standard. Catalog, Cart, and Orders also have
explicit empty-state standards. These layouts use the same validator and renderer as generated plans.

The standard renders before AI work starts and remains visible if data loading, model execution,
validation, or rendering fails. Reset restores it and clears presentation intent.

## 8. Legacy composer APIs

The reusable versioned `CompositionPlan`/scaffold composer remains available and tested as a generic
library API. The Garden-specific product scaffold, explicit composition adapter, mode picker, and
comparison metrics were retired; the destination app uses only automatic surface composition.
