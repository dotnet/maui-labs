# Appendix: Native Component Composer

> **Status:** Implemented vertical slice (v0.3).
> Parent: [`overview.md`](./overview.md). Sample:
> [`sample-generative-garden.md`](./sample-generative-garden.md).

## 1. Why this path exists

The original Generative UI MVP remains useful as a research baseline: a frontier model authors a
complete constrained primitive tree, and the runtime inflates it into MAUI views. The default v0.3
path narrows the runtime model's role. The app owns tested native components; the model selects,
prioritizes, and arranges only compatible candidates.

This preserves runtime adaptation without asking a model to reproduce visual design, accessibility,
binding, and component behavior on every turn.

## 2. Runtime flow

1. The main chat agent discovers and reads the Garden REST API through the existing OpenAPI tools.
2. The typed `compose_product_detail` tool seeds the complete `Product` under the existing dotted
   `StateRoot` path `product`.
3. `ComponentCandidateResolver` filters the registered catalog by data contract and required
   bindings. Missing facets remove components before the model sees them.
4. A dedicated `IChatClient.GetResponseAsync<CompositionPlan>()` request receives only the intent,
   state snapshot, current plan, scaffold, and valid candidates.
5. `CompositionPlanValidator` rejects unknown components, invalid slots/variants/paths, duplicate or
   unstable IDs, and stale revisions.
6. One invalid response is returned to the planner as structured correction JSON. A second invalid
   response selects the deterministic ProductHero + ProductCoreInfo plan.
7. `CompositionPlanRenderer` resolves native views through DI and reconciles the persistent scaffold
   by stable section ID.

There is no primitive-generation fallback in this path.

## 3. Minimal descriptor v1

Each app-authored component declares only:

- `alias`
- full free-form `description` containing when/when-not guidance
- accepted `dataContract`
- `requiredBindings`
- `optionalBindings`
- `allowedSlots`
- one or two `variants`

The Garden catalog intentionally has no cost, density, risk, exclusivity, policy, or max-instance
metadata. The scaffold separately declares its named slots and whether each slot accepts one or
many children; this is the minimum needed for slot validation.

## 4. CompositionPlan v1

```json
{
  "schemaVersion": 1,
  "planId": "composition-...",
  "revision": 2,
  "scaffold": "ProductDetail",
  "title": "Watering Can",
  "sections": [
    {
      "id": "product-dimensions",
      "slot": "Primary",
      "component": "DimensionsPanel",
      "dataPath": "product",
      "variant": "default",
      "priority": 100,
      "reason": "The user asked how big the product is."
    }
  ]
}
```

`dataPath` is the existing dotted `UiObject` binding path. The state tools continue to use their
existing JSON Pointer paths for subtree reads and RFC 6902 patches; the composer introduces no new
binding language.

Initial plans receive a stable `planId` and revision 1. Follow-ups preserve the plan ID and unchanged
section IDs and increment the revision exactly once. Priority orders children within a slot.

## 5. Garden components and facets

The shared `Product` DTO keeps its existing fields and adds optional trailing records:

- `SeedDetails`: planting instructions, germination window, harvest window.
- `Dimensions`: width, height, depth, unit.
- `ColorOptions`: ordered named/hex colors.

The app-owned catalog contains:

| Component | Required state | Slots | Variants |
|---|---|---|---|
| `ProductHero` | `name` | Hero | default, compact |
| `ProductCoreInfo` | `name`, `description`, `price` | Primary, Supporting | default, compact |
| `DimensionsPanel` | `dimensions.*` | Primary, Supporting | default |
| `ColorGallery` | `colorOptions.options` | Primary, Supporting | swatches, gallery |
| `SeedGrowingTimeline` | `seedDetails.*` | Primary, Supporting | default |

`ProductDetailScaffold` owns Hero, Primary, Supporting, and Actions hosts. Hero and Primary are
single-section slots. Supporting and Actions are ordered multi-section slots.

## 6. Incremental rendering

The renderer keeps:

- the scaffold `View`
- the current typed plan
- a section-ID-to-View map
- the most recent render diff

For a follow-up revision, unchanged IDs reuse their existing views and binding contexts. Moving a
section removes that same view from one slot and inserts it into another. Variant changes call the
component's in-place variant hook. Only removed or component-replaced IDs are unmounted.

The render diff reports scaffold reuse and added, reused, moved, reconfigured, and removed section
IDs. These signals are displayed beside model latency/token metrics for the sample A/B comparison.

## 7. Modes and action boundary

The Garden shell exposes:

- **Component Composer** (default): `list_endpoints`, `describe_endpoint`, `describe_model`,
  `read_api`, and `compose_product_detail`.
- **Baseline Full Generation**: the original OpenAPI and primitive UI tool set, including
  `write_api`, `render_ui`, state tools, `itemsBind`, and automatic write approval.

Switching modes clears conversation, canvas, and composition state so tool histories do not mix.

Component Composer v1 is read-only. Review submission and other write actions are deferred until the
component-to-action path and approval ownership are explicitly designed and tested. The baseline
automatic `write_api` approval behavior is unchanged.

## 8. Validation

Deterministic unit tests use typed plan deserialization and structural assertions. A scripted plan
generator covers:

- generic Watering Can composition
- Dimensions promotion for "How big?"
- ColorGallery promotion and `gallery` variant for "What colors?"
- SeedGrowingTimeline with no dimensions/color components for a seed product
- invalid-then-corrected output
- invalid-twice deterministic fallback
- cancellation propagation
- persistent scaffold and component identity across follow-ups

Live model quality and visual comparison are validated through the existing DevFlow agent rather
than nondeterministic exact-output tests.

## 9. Deferred

- ReviewEditor and all composer-mode write/approval routing.
- Primitive `GeneratedPanel` fallback.
- Rich descriptor policy.
- `[GenerativeComponent]` source generation.
- Development-time Copilot CLI component scaffolding.
- Analytics-driven catalog promotion beyond the local comparison metrics.
