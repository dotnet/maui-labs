# Scenario selection

Read this first, then one recipe and only the requested platform references.
[Sources](sources.md) were checked on 2026-10-12; inspect the app's actual versions.

## Route before generating

| Need | Action |
|---|---|
| Working stock list/grid plus a small property fix | Keep CollectionView; use controls/binding guidance |
| General jank without a collection-specific design requirement | Measure with performance guidance first |
| Specific new layout/interaction not served by the existing control | Select a bespoke recipe |
| New platform backend installation | Use platform-targeting/backend setup guidance |
| Native API missing from a resolved binding | Verify the gap, then use binding guidance |

Do not turn this skill into an automatic migration or library dependency.
For ordinary reorder, check actual stock CollectionView CanReorderItems and
version/backend/source/group limits first; the generic drag recognizer and built-in
reorder path are different mechanisms. Do not invent a missing property to justify
rewriting a working list.

## Distinguish the geometry

| Requirement | Recipe and key decision |
|---|---|
| Unequal column heights, no row boundary | [Photo masonry](recipe-photo-masonry.md): shortest versus stable assigned columns |
| Variable-height ordered history | [Timeline](recipe-anchored-timeline.md): prepend anchors and explicit follow-tail |
| Outer vertical feed with horizontal shelves | [Shelves](recipe-product-shelves.md): bounded inner viewport and per-section state |
| Sequential items wrap to the next line | [Wrap](recipe-virtualized-wrap.md): width metadata and line checkpoints |
| Aspect-ratio images share row height | [Justified gallery](recipe-justified-gallery.md): crop/stretch/final-row policy |
| Perspective/depth/overlap at a focal item | [Coverflow](recipe-coverflow.md): transformed candidates, hit testing and snapping |

A uniform grid, variable-row grid, masonry, flow, and justified rows are different
algorithms. A scaled/masked carousel is not automatically coverflow.

## Brief

Record only what changes architecture:

```text
Control name:
Targets/backend, SDK/package versions, deployment minimums:
Placement/scroll axis and exact-versus-equivalent native experience:
Item renderer, size metadata and text constraints:
Logical count, loaded window, source and seek capabilities:
Keys/content revisions; append/prepend/change/move/reset:
Global/section chrome, pinning, collapse and nesting:
Invocation/selection, keyboard, accessibility and resize:
Load/error/end policy and memory/concurrency limits:
Hardware/workload/performance budgets and available validation:
```

Use existing project facts instead of asking for them again. Do not assume all
platforms, swipe/reorder, infinite looping, or arbitrary templates are required.
If a missing decision changes the experience, surface it; in unattended work,
state a conservative assumption without silently removing requirements.

## Choose source and renderer once

Use an indexed adapter-oriented boundary. Feed it from existing MVVM/observable
state, immutable snapshots, or a windowed repository as appropriate.
Do not add one abstraction for each possible future input.

Choose typed native cells for fixed hot-path content, or retained MAUI templates
when app composition warrants the cost. Compare identical content/behavior before
claiming one is faster.

## Capability record

For each requested target, separately record native host/layout availability,
binding compilation, backend integration, native behavior, lifecycle and
performance evidence. A source-inspected API is not a certified implementation.
Add current priority, available host/device and minimum acceptable evidence.
Mark excluded/deferred/blocked targets explicitly and reconcile user redirections
before more handlers. Preserve deferred-target behavior instead of inventing parity.

Keep the viewport finite. Prefer a native layout inside a native collection
engine before writing scroll physics and realization. GTK general masonry/wrap
and WPF custom panels are substantive implementations, not eager-layout fallbacks.

**Output:** one brief, one selected recipe, scoped handlers/loading/registration,
and a truthful target/evidence table. No universal collection framework.
Use [mobile checkpoints](mobile-vertical-slice.md) for baseline, small compile,
populated content, reuse and bounded retry/discovery before expanding work.
