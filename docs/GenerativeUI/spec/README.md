# Generative UI — Spec

> **Status:** Implemented MVP (v0.2) with living design docs. The OpenAPI half, UI-DSL/inflator,
> stateful canvas, JSON Patch state model, and Garden core scenarios run end to end; remaining
> extension/presentation work is identified explicitly below.

An experiment in building MAUI apps whose UI is produced **at runtime by an AI model** rather
than authored ahead of time as fixed pages. A user talks to a chat assistant; the assistant reads
and writes data over a REST API and **renders bespoke, data-bound UI** into a blank canvas.

Two deliverables:

- **`Microsoft.Maui.AI.GenerativeUI`** — a reusable, app-agnostic library giving an app two
  capabilities: discover + call a server's REST API (via its OpenAPI doc), and render UI (via a
  constrained UI-DSL + runtime inflator).
- **`GenerativeUI.Sample.Garden`** — a concrete sample (a garden shop) whose client and server are
  co-developed and share a typed models project.

## Documents

| Document | What it covers |
|---|---|
| [`overview.md`](./overview.md) | The main spec: motivation, goals/non-goals, architecture, the two tool families, runtime loop, library/sample boundary, state & binding, approval, config, security, MVP scope, and open questions. **Start here.** |
| [`appendix-ui-dsl.md`](./appendix-ui-dsl.md) | The JSON UI-DSL the model emits and the inflator that turns it into MAUI controls: node catalog, binding model, intents, styles, validation, versioning, worked examples, draft schema. |
| [`appendix-extensibility.md`](./appendix-extensibility.md) | How an app **extends** the DSL — at startup **or anytime afterwards** (login/permissions): registering brand **styles**, bespoke **controls** (e.g. a watermarking product image), and full **screens** (e.g. checkout, reports). A single mutable `GenerativeUiRegistry` in DI, description-driven when/when-not guidance (never clipped), native-XAML theming, "send-all" discovery, and security. |
| [`appendix-binding-model.md`](./appendix-binding-model.md) | The single persistent **observable state graph** the UI binds to when there are no hand-authored view models: `UiObject`/`UiObjectCollection`, path compilation, `itemsBind`, RFC 6902 JSON Patch mutation, snapshots/deltas, coercion, and persistence across re-inflation. |
| [`appendix-openapi-processor.md`](./appendix-openapi-processor.md) | How the library fetches, reduces, and serves a server's OpenAPI doc to the model, and the generic invoker for `read_api`/`write_api`: pipeline, reduction, tool signatures, security. |
| [`appendix-protocol-alignment.md`](./appendix-protocol-alignment.md) | How the design aligns with **AG-UI** (runtime/state protocol) and **A2UI** (generative UI language): component mapping, adoption costs, package/feed reality, and the chosen compatibility-north-star strategy. |
| [`sample-generative-garden.md`](./sample-generative-garden.md) | The reference sample: 3-project layout, shared models + source-gen JSON context, server endpoints, client shell/DI, system prompt, interaction scenarios, run steps. |

## Status & conventions

- Documents carry their own versioned implementation status. `overview`, UI-DSL, binding, OpenAPI,
  and the Garden core are **implemented v0.2**; extensibility's core registry is implemented while
  Garden-specific custom controls/screens remain planned; protocol alignment is an adopted
  direction.
- The **overview** is the anchor; appendices and the sample spec cross-link to it and must stay
  consistent (tool names, the library/sample boundary, DSL vocabulary, the approval model).
- These are living design docs. Major implementation decisions (single state graph, JSON Patch,
  `itemsBind`, AG-UI/A2UI compatibility stance) are recorded here as they land; open questions are
  distinguished from implemented behavior.
