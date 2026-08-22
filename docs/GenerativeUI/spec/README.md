# Generative UI — Spec

> **Status:** Implemented component-composer vertical slice (v0.3) with the v0.2 full primitive
> generation mode preserved as a runnable research baseline.

An experiment in runtime-adaptive MAUI UI. The default path selects and arranges app-authored native
components from typed data; the baseline path lets a model author a complete constrained primitive
tree. Both use the same OpenAPI and persistent state infrastructure.

Two deliverables:

- **`Microsoft.Maui.AI.GenerativeUI`** — a reusable, app-agnostic library for OpenAPI discovery,
  native component composition, and the preserved constrained UI-DSL/runtime inflator.
- **`GenerativeUI.Sample.Garden`** — a concrete sample (a garden shop) whose client and server are
  co-developed and share a typed models project.

## Documents

| Document | What it covers |
|---|---|
| [`overview.md`](./overview.md) | The main spec: motivation, goals/non-goals, architecture, the two tool families, runtime loop, library/sample boundary, state & binding, approval, config, security, MVP scope, and open questions. **Start here.** |
| [`appendix-ui-dsl.md`](./appendix-ui-dsl.md) | The JSON UI-DSL the model emits and the inflator that turns it into MAUI controls: node catalog, binding model, intents, styles, validation, versioning, worked examples, draft schema. |
| [`appendix-extensibility.md`](./appendix-extensibility.md) | How an app **extends** the DSL — at startup **or anytime afterwards** (login/permissions): registering brand **styles**, bespoke **controls** (e.g. a watermarking product image), and full **screens** (e.g. checkout, reports). A single mutable `GenerativeUiRegistry` in DI, description-driven when/when-not guidance (never clipped), native-XAML theming, "send-all" discovery, and security. |
| [`appendix-binding-model.md`](./appendix-binding-model.md) | The single persistent **observable state graph** the UI binds to when there are no hand-authored view models: `UiObject`/`UiObjectCollection`, path compilation, `itemsBind`, RFC 6902 JSON Patch mutation, snapshots/deltas, coercion, and persistence across re-inflation. |
| [`appendix-component-composer.md`](./appendix-component-composer.md) | Minimal native component descriptors, typed versioned plans, facet-based candidate filtering, validation/correction/fallback, and incremental scaffold reconciliation. |
| [`appendix-openapi-processor.md`](./appendix-openapi-processor.md) | How the library fetches, reduces, and serves a server's OpenAPI doc to the model, and the generic invoker for `read_api`/`write_api`: pipeline, reduction, tool signatures, security. |
| [`appendix-protocol-alignment.md`](./appendix-protocol-alignment.md) | How the design aligns with **AG-UI** (runtime/state protocol) and **A2UI** (generative UI language): component mapping, adoption costs, package/feed reality, and the chosen compatibility-north-star strategy. |
| [`sample-generative-garden.md`](./sample-generative-garden.md) | The reference sample: native component project, shared models + source-gen JSON context, server endpoints, client shell/mode switch, interaction scenarios, and run steps. |

## Status & conventions

- Documents carry their own versioned implementation status. Component composition and the Garden
  product-detail slice are **implemented v0.3**. UI-DSL, binding, OpenAPI, and the fully generated
  Garden baseline remain **implemented v0.2**.
- The **overview** is the anchor; appendices and the sample spec cross-link to it and must stay
  consistent (tool names, the library/sample boundary, DSL vocabulary, the approval model).
- These are living design docs. Major implementation decisions (single state graph, JSON Patch,
  `itemsBind`, AG-UI/A2UI compatibility stance) are recorded here as they land; open questions are
  distinguished from implemented behavior.
