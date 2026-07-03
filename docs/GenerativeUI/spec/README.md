# Generative UI — Spec

> **Status:** Draft (v0.1) — for iteration. Nothing here is final.

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
| [`appendix-extensibility.md`](./appendix-extensibility.md) | How an app **extends** the DSL — statically or **dynamically at runtime** (login/permissions): registering brand **styles**, bespoke **components** (e.g. a watermarking product image), full **views** (e.g. checkout, reports), and **renderer** policies (mandatory controls). Registration API + sources (imperative/XAML/source-gen), context-conditional resolution, discovery tools, per-app schema, security. |
| [`appendix-binding-model.md`](./appendix-binding-model.md) | The **generic observable data context** the UI binds to when there are no hand-authored view models: the `UiObject`/`UiObjectCollection` tree, why not `System.Dynamic`, path compilation, two-way form state, type coercion, and persistence across re-inflation. |
| [`appendix-openapi-processor.md`](./appendix-openapi-processor.md) | How the library fetches, reduces, and serves a server's OpenAPI doc to the model, and the generic invoker for `read_api`/`write_api`: pipeline, reduction, tool signatures, security. |
| [`sample-generative-garden.md`](./sample-generative-garden.md) | The reference sample: 3-project layout, shared models + source-gen JSON context, server endpoints, client shell/DI, system prompt, interaction scenarios, run steps. |

## Status & conventions

- Every document is **Draft (v0.1)** and carries its own **Open Questions** section.
- The **overview** is the anchor; appendices and the sample spec cross-link to it and must stay
  consistent (tool names, the library/sample boundary, DSL vocabulary, the approval model).
- These are living design docs written before implementation. We iterate on the specs first, then
  build.
