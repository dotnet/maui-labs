---
name: maui-bespoke-collections
description: >-
  Creates scenario-specific .NET MAUI collection controls with native virtualization and tailored handlers. USE FOR: bespoke masonry, wrap, galleries, timelines, shelves, coverflow, bounded paging, and native drag/drop, reordering, swipe deletion, refresh or context actions for custom collections. DO NOT USE FOR: routine CollectionView markup, generic MVVM/data binding, general profiling, or installing a platform backend.
---

# Bespoke MAUI Collections

Build one app-owned control for one explicit collection scenario. Share data
semantics and invariants, not a universal replacement for `CollectionView`.

## Scope and evidence

These are source-backed recipes, **not packaged or performance-certified controls**.
The [mobile masonry proof](references/sources.md#r4) uses native UIKit/RecyclerView
on virtual devices, not a WebView. HTTP is its data fixture, not a control
requirement. Other recipes/targets and physical performance remain unverified.
Prove APIs in the consuming SDK and requested native runtimes.

Use this skill for a custom collection/layout/handler request. If a working stock
`CollectionView` already meets the requirement, leave it alone. Route ordinary
control use to `maui-controls-deep-dive`, general profiling to `maui-performance`,
handler basics to `maui-custom-handlers`, and new backend setup to
`maui-labs-platform-targeting`. Do not install companion skills automatically.

Answer design questions without scaffolding. For implementation, deliver the
control, registrations, loading bridge, and tests.

## Read selectively

Start with [scenario selection](references/scenario-selection.md).
For implementation, follow the [one-platform fast path](references/mobile-vertical-slice.md):
baseline -> small compile -> populated item -> reuse -> paging -> next target.
Load additional references when their checkpoint is reached, not all up front.

| Need | Reference |
|---|---|
| Indexed data, keys, snapshots, binding choices | [Source/update contract](references/source-and-update-contracts.md) |
| Variable sizes, viewport lookup, anchors, chrome | [Geometry](references/geometry-and-anchoring.md) |
| Renderer and handler ownership, stale callbacks | [Reuse/lifecycle](references/reuse-and-handler-lifecycle.md) |
| Retained MAUI text, margins, units and measure/arrange | [Hosting](references/retained-content-hosting.md) |
| HTTP, SQLite, offline-first, load states | [Paging/data](references/paging-http-database.md) |
| Interaction identity, mutation/persistence, alternatives | [Interaction contracts](references/interaction-contracts.md) |
| Drag/drop, reorder and transfer | [Drag/reorder](references/drag-drop-and-reordering.md) |
| Delete, swipe, recovery and undo | [Delete/swipe](references/deletion-and-swipe-actions.md) |
| Pull/button refresh and load races | [Refresh](references/refresh-and-reconciliation.md) |
| Item/selection/empty-space actions | [Context menus](references/context-menus-and-actions.md) |
| Budgets, profiling, tests, completion evidence | [Validation](references/performance-and-validation.md) |
| Isolated populated comparison, metrics and cleanup | [Benchmark harness](references/benchmark-harness.md) |
| iOS or Mac Catalyst | [UIKit](references/platform-uikit.md) |
| Android | [RecyclerView](references/platform-android.md) |
| Windows MAUI / WinUI | [WinUI](references/platform-winui.md) |
| Native macOS backend, not Catalyst | [AppKit](references/platform-appkit.md) |
| Linux GTK4 backend | [GTK4](references/platform-gtk4.md) |
| Windows WPF backend, not WinUI | [WPF](references/platform-wpf.md) |
| Source provenance/version refresh | [Sources](references/sources.md) |

Choose one primary recipe:

| Scenario | Recipe |
|---|---|
| Unequal-height photo columns | [Photo masonry](references/recipe-photo-masonry.md) |
| Variable-height history with prepend/grouping | [Anchored timeline](references/recipe-anchored-timeline.md) |
| Vertical feed of horizontal sections | [Product shelves](references/recipe-product-shelves.md) |
| Sequential variable-width line wrapping | [Virtualized wrap](references/recipe-virtualized-wrap.md) |
| Common-height image rows sized by aspect ratio | [Justified gallery](references/recipe-justified-gallery.md) |
| Perspective/depth/overlap around a focal card | [Coverflow](references/recipe-coverflow.md) |

## Workflow

1. Inspect the scenario and write a brief.
2. Prove native capabilities.
3. Define source, renderer and mutation contracts.
4. Wire one complete vertical slice.
5. Implement remaining requested targets.
6. Measure and report actual evidence.

### 1. Inspect and write a brief

Inspect app TFMs, resolved packages, deployment minima, backend registration,
controls, repository, renderer, and tests.
Record requested/prioritized/deferred targets and verify an unchanged runnable
baseline. After failed probes, change hypothesis/use a smaller probe; classify
environment blockers instead of looping or changing pins.

Record: layout/placement/axis; requested targets; fixed/metadata/self-sized content;
source volume/count/seek; identity/updates; grouping/chrome/nesting;
selection/input/accessibility; requested interactions, ordering/persistence,
confirmation/undo and performance acceptance. Infer ordinary details from the
project; surface decisions changing the experience or target set.

Name the tailored control; avoid a runtime layout enum or unrelated features.

### 2. Establish native capabilities

Use the actual native engine before implementing a new scroller:

| Target | Starting host |
|---|---|
| UIKit iOS/Catalyst | `UICollectionView` + flow/compositional/custom layout |
| Android | `RecyclerView` + standard or specialized `LayoutManager` |
| WinUI | `ListView`/`GridView`, `ItemsView`, or `ItemsRepeater` + `VirtualizingLayout` |
| AppKit | `NSCollectionView` in `NSScrollView`; native item controllers |
| GTK4 | `GtkListView`/`GtkGridView`; specialized native virtualizer for layout gaps |
| WPF | Data-bound items control + real virtualizing panel/generator |

Track native documentation, binding compile, backend integration, runtime and
performance independently. Prove uncertain APIs with a small consuming-SDK spike.

Preserve stable versions. Check native availability and consuming C# bindings.
Gaps need a verified custom path, approved alternative or explicit blocker,
never eager/stub parity.

### 3. Define a small data/rendering boundary

Prefer an adapter-oriented native boundary: cheap indexed presentation data,
stable domain/presentation keys, content revisions, and ordered updates.
Keep HTTP/SQL/cache policy in the existing repository.

MVVM and adapters coexist. Use typed native assignment or retained MAUI templates
as appropriate; templates declare `x:DataType`. Immutable/windowed custom sources
do not require `ObservableCollection<T>`.

Specify supported mutations, count/seek semantics, grouping, loading/error/end
states, cache bounds, and anchor/selection policy. No I/O in count, indexer,
type selection, bind, or measure.

### 4. Implement a complete vertical slice

Wire the chosen native host, layout, renderer, source publication, loading demand,
invocation/selection, measurement, and teardown on one requested target first.
Add only requested interactions via keyed commands and one mutation authority.
Native gesture/animation acceptance is not durable repository success.

Use public handler APIs: property/command mappers, `CreatePlatformView`,
`ConnectHandler`, `DisconnectHandler`, and one scoped `AddHandler` registration.
Do not depend on internal stock CollectionView cells/holders as a public extension.

MAUI owns the outer placement; the native collection owns item scrolling/layout.
For MAUI cell content, bridge constrained child measure/arrange without creating
competing collection layout engines. Build and exercise zero/one/realistic items,
source replacement, delayed assets, resize, and disconnect before extending.

### 5. Implement only the remaining requested targets

Reuse source semantics and pure geometry where appropriate, not native API guesses.
Share UIKit deliberately with `IOS || MACCATALYST`; AppKit uses `MACOS`.
For GTK/WPF, inspect the actual head project's compilation and public backend base:
`GtkViewHandler`, `MacOSViewHandler`, or `WPFViewHandler`. Do not invent `LINUX`
or distinguish WinUI/WPF with an ambiguous `#if WINDOWS` alone.

Use the registered dispatcher, not assumed `MainThread` on custom backends.
Native view/measure work stays on UI; prepare only thread-safe data/assets off-thread.

### 6. Measure, fix, and verify

Instrument native containers, retained MAUI trees, source/model/image memory,
geometry candidates/measurements, subscriptions, requests, and source epochs.
First fix lost virtualization, full invalidation/reinflation, UI-thread I/O,
all-item geometry, and oversized images. Tune pools/prefetch only with evidence.

Run focused source/geometry tests, every requested target build, native behavior/
lifecycle checks, and agreed Release performance fixtures. DevFlow inspection
helps verify types/layout/input; it does not establish presented-frame rate.
Before each measured variant verify intended host, filtered count, visible loaded
keys, settled layout and isolated dataset. Empty/wrong/drifting content is invalid.

Use inspected project/target/filter values, never unset or guessed ones:

```bash
dotnet build "$APP_PROJECT" -f "$TARGET_FRAMEWORK" -c Release
dotnet test "$TEST_PROJECT" -c Release --filter "$TEST_FILTER"
```

## Non-negotiable invariants

- Finite viewport and real native virtualization; view count alone is insufficient.
- Native containers, MAUI trees, data/images, and geometry costs are separately bounded.
- Keys are not positions or unchecked hashes; unchanged identity needs content updates.
- One ordered UI update authority; immutable inputs while diffing.
- Interaction/menu intents preserve intended keys and validate source/mutation revision.
- Bind resets item-specific state, even without a reuse callback.
- Delayed work validates item key + binding generation + source epoch.
- Compatible known heights survive transient loading; newer user scrolling supersedes anchor restoration.
- Disappear/detach is not always recycle; visibility and binding ownership differ.
- Retain compatible template handlers through rebind; drain owned resources on teardown.
- Observe mutable child groups or publish immutable section snapshots.
- Header/footer rows are not selectable data or remaining-item counts.
- Page errors are not empty success; cancellation is not end-of-data.
- Refresh completion belongs to its owner; old pages cannot undo accepted edits.
- Preserve anchors, focus, accessibility, RTL, resize, and requested native behavior.
- Do not weaken required behavior or data durability merely to pass a benchmark.

## High-risk platform traps

| Trap | Correct interpretation |
|---|---|
| UIKit preparation/reuse | Registrations live outside providers; verify image API affinity and hold bounded async work through completion |
| Android size/pools/layout | HasFixedSize describes host size; pool types/IDs must be compatible; offset fields do not prove layout |
| Eager or look-alike layouts | FlowBox/WrapPanel/FlexboxLayout are not virtualizers; carousel masking is not 3D coverflow |
| Platform/version borrowing | Verify WinUI experimental layouts, GTK section versions and UIKit/AppKit differences in platform references |
| Source/loading assumptions | Repeater ignores automatic loading interfaces; SQLite async-named work is synchronous |

Refresh version-sensitive findings against the consuming SDK and linked sources.

## Completion report

Return actual changed files and registration, a per-target capability/evidence
table, tested operations, measured budgets/results, and remaining limitations.
Distinguish source research, compile proof, native runtime proof, lifecycle proof,
and performance measurement. Report unrun targets explicitly. Never claim all
platforms verified from a screenshot or portable test.
