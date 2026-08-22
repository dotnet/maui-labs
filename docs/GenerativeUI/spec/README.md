# Generative UI — Spec

> **Status:** Implemented adaptive whole-component runtime (v0.4).

Generative UI is an experiment in runtime-adaptive .NET MAUI experiences. The model may arrange
registered, app-authored components inside named adaptive regions, but it never authors C#, XAML,
styles, event handlers, or primitive controls.

Two deliverables are maintained:

- **`Microsoft.Maui.AI.GenerativeUI`** — the reusable layout contract, catalog, validation,
  composition, per-surface session, reconciliation, and rendering runtime. The generic primitive DSL
  and OpenAPI APIs remain available for research and other consumers.
- **`AIExtensions.Sample.Garden`** — the reference application. It combines a fixed Shell, persistent
  assistant, typed Garden server, source-generated tool bindings, checked-in standard layouts, and
  adaptive Home, Catalog, Product, Cart, and Orders surfaces.

The retired blank-canvas Garden sample, generation-mode picker, and explicit product composition
tool are not part of the current architecture.

## Documents

| Document | What it covers |
|---|---|
| [`overview.md`](./overview.md) | Current architecture, trust boundaries, runtime loop, state/session ownership, failure behavior, and acceptance criteria. |
| [`sample-generative-garden.md`](./sample-generative-garden.md) | Reference application structure, surfaces, typed tools, approval behavior, and run steps. |
| [`appendix-component-composer.md`](./appendix-component-composer.md) | Flat component-layout contract, catalog/data manifest, validation, retry, reconciliation, and fallback. |
| [`appendix-ui-dsl.md`](./appendix-ui-dsl.md) | Preserved generic primitive UI-DSL research API; not exposed to the Garden assistant. |
| [`appendix-extensibility.md`](./appendix-extensibility.md) | Generic registry extensibility for styles, controls, and screens. |
| [`appendix-binding-model.md`](./appendix-binding-model.md) | Observable `UiObject` state graph used as the derived rendering projection. |
| [`appendix-openapi-processor.md`](./appendix-openapi-processor.md) | Preserved generic OpenAPI processor and invoker; not exposed to the Garden assistant. |
| [`appendix-protocol-alignment.md`](./appendix-protocol-alignment.md) | AG-UI and A2UI alignment notes. |

## Invariants

1. Typed Garden DTOs and server state are canonical; `UiObject` is a derived rendering projection.
2. Model output is a flat enum-constrained layout using only `Stack`, `Grid`, `Tabs`, `Section`, and
   registered whole components.
3. Fixed navigation and essential actions stay app-authored and usable before, during, and after
   adaptation.
4. AI server mutations require approval. Equivalent human taps call the typed client directly.
5. Every page instance owns an isolated adaptive session.
6. A checked-in standard layout renders immediately and remains usable on AI, validation, or network
   failure.
