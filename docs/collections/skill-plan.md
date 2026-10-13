# Plan: a scenario-driven bespoke collection skill

[Research index](README.md) | [Shared design](shared-design.md) |
[Performance gates](performance-validation.md) | [Sources](sources.md)

**Status:** phase 1 skill foundation implemented from the 2026-10-12 research.
The [entry skill](../../plugins/dotnet-maui/skills/maui-bespoke-collections/SKILL.md),
24 focused references, and [52 authored evaluation
scenarios](../../tests/dotnet-maui/maui-bespoke-collections/eval.yaml) are present.
The 2026-10-13 [interaction research](interactions.md) adds source-backed drag/
reorder, deletion/swipe, refresh/reconciliation and item-action contracts.
The related stock-control skill now has a selective interaction reference and
[8 authored scenarios](../../tests/dotnet-maui/maui-controls-deep-dive/eval.yaml).
The CLI distribution fixture exercises individual install/status/update across
supported destinations. Phase 2's nonshipping
[mobile prototype](../../samples/BespokeCollections/README.md) now provides
portable tests, compiled native handlers, Release virtual-device behavior proof
and diagnostic traces. Its
[evidence record](../../samples/BespokeCollections/EVIDENCE.md) keeps physical
frame/memory qualification pending. LLM evaluation and recipe-wide certification
are not claimed; phases 3-6 remain future work.

## 1. Product of the skill

Help a developer build **one tailored .NET MAUI control with native handlers for
the requested targets**, its data/loading boundary and the validation needed for
that scenario.

Examples: a metadata-sized photo waterfall, an anchored chat timeline, a
virtualized chip wrap, an editorial grid, horizontal product shelves, or a
perspective coverflow.

The result should be app-owned code by default. It is not:

- a new universal CollectionView replacement;
- a mandatory adapter/renderer/paging framework for every app;
- a migration of working lists solely to follow a recipe;
- a promise that every layout is already supported by every backend;
- a wrapper that silently drops native interaction or uses eager fallback layouts.

Reuse knowledge, contracts, test fixtures and narrowly useful helpers. Do not
recreate the universal-control problem through a giant layout enum and a growing
matrix of optional runtime interfaces.

## 2. Proposed routing contract

**Name:** `maui-bespoke-collections`.

**Implemented location:**
`plugins/dotnet-maui/skills/maui-bespoke-collections/`.
The current plugin manifest discovers `./skills/`; inspect its current state and
follow marketplace release/version rules when shipping.
[R3](sources.md#r3)

The implemented description follows this routing contract:

> Build scenario-specific .NET MAUI collection controls with platform-native
> virtualization, recycling and tailored layout. USE FOR: custom masonry/
> waterfall, virtualized wrap, justified galleries, grouped timelines, nested
> shelves, coverflow, custom native collection handlers, and HTTP/database-backed
> paging for those controls. DO NOT USE FOR: routine CollectionView markup,
> generic MVVM/layout issues, general profiling, or installing a new backend.

Routing matters: the existing repo already has `maui-controls-deep-dive` and
`maui-performance`. An installed `maui-collectionview` skill can also be external
to this repository. Do not assume a duplicate in-repo skill exists or rewrite
those skills wholesale.

| Request | Route |
|---|---|
| Basic stock CollectionView layout/selection/loading | Existing controls/CollectionView guidance |
| General app performance symptom | Performance skill; this skill only when a bespoke collection is the actual solution |
| Build a new GTK/AppKit backend | Backend implementation guidance |
| Generate a specific native-backed collection | This skill |
| Debug its rendered behavior | Existing DevFlow inspect/fix workflow plus this skill's invariants |
| Native Java/Apple API lacks binding | Binding guidance, after a concrete capability check |

The entry skill should remain short, approximately 150-250 lines and always below
the contributor guide's 500-line ceiling. Read only the relevant references.

## 3. Inputs and a compact scenario brief

The skill should inspect the project first, then record a concise brief. Ordinary
implementation details can use stated defaults; do not ask a long questionnaire
when the source/request already answers it.

| Required decision | Suggested default if compatible with the request |
|---|---|
| Layout and placement semantics | One explicit primary layout, not runtime support for all layouts |
| Target platforms/backend | Only targets the user asks for or the existing feature requires |
| SDK/deployment/native package versions | Preserve existing versions; no incidental experimental upgrade |
| Data volume/source | Existing repository input; lightweight indexed projection |
| Identity/update mode | Stable presentation/domain keys and explicit revisioned changes |
| Item renderer | Typed native for a fixed hot-path design; cached compiled MAUI template when reuse/composition is needed |
| Size strategy | Fixed/metadata-derived where possible; constrained estimates/cache otherwise |
| Loading/retention | Source-appropriate bounded scheduler/cache; no I/O in native bind/measure |
| Groups/chrome | Only requested headers/footers/pinning/nesting, with documented completeness |
| Interaction/accessibility | Preserve requested native behavior and desktop fundamentals |
| Performance acceptance | Record actual hardware/workload/metric budgets before optimizing |

If missing information changes the architecture materially, surface that decision.
In unattended operation, make a conservative explicit assumption and keep the
generated capability set honest. Do not infer permission to discard a target or
change the visual/data contract.

A brief might read:

```text
PhotoMasonryView
Targets: iOS + Android; preserve current app SDKs
Layout: vertical, shortest-column, responsive 2-4 columns
Card: native thumbnail/caption; aspect ratios included in metadata
Source: HTTP cursor repository; unknown total; stable item IDs
Chrome: scrolling global header + retry/loading footer, no groups
Updates: append + query refresh; preserve keyed anchor on refresh
Bounds: finite viewport; configured page/thumbnail memory limits
Validation: Release physical devices, reuse/late-image tests, agreed frame budgets
```

That example is not an instruction to impose those choices on every app.

## 4. Progressive reference structure

Implemented files inside the skill:

```text
SKILL.md
references/
  scenario-selection.md
  source-and-update-contracts.md
  geometry-and-anchoring.md
  reuse-and-handler-lifecycle.md
  interaction-contracts.md
  drag-drop-and-reordering.md
  deletion-and-swipe-actions.md
  refresh-and-reconciliation.md
  context-menus-and-actions.md
  paging-http-database.md
  performance-and-validation.md
  platform-uikit.md
  platform-android.md
  platform-winui.md
  platform-appkit.md
  platform-gtk4.md
  platform-wpf.md
  sources.md
  recipe-photo-masonry.md
  recipe-anchored-timeline.md
  recipe-product-shelves.md
  recipe-virtualized-wrap.md
  recipe-justified-gallery.md
  recipe-coverflow.md
```

Recipe files are flattened into `references/` because the static skill validator
permits only one directory level for file references from `SKILL.md`.

Derive these concise references from this research; do not copy every chapter into
the entry skill. Split WinUI and WPF to prevent mixing APIs. Combine iOS/Catalyst
principles while keeping Catalyst interaction/version checks explicit.

Only add a reusable script if repeated use demonstrates a concrete need, such as
checking a benchmark evidence manifest. Do not begin with a scaffolding CLI,
binary dependency installer, or a collection framework hidden under `scripts/`.

Each platform reference should state:

- actual native host/layout/renderer types;
- minimum API/version and binding checks;
- implemented/native/custom/gap status for each recipe;
- native callback and source publication rules;
- measurement, chrome, accessibility and input differences;
- public MAUI/backend integration surfaces;
- native tests and profiling evidence required;
- source provenance and last verification date.

Each recipe should include its supported operations and known complexity, rather
than promise generic arbitrary edits or random access.

## 5. The skill's implementation workflow

### Step 1: inspect and select

Read project TFMs, resolved packages, deployment minimums, backend registration,
data repository and existing control. Determine whether stock native/MAUI
functionality already solves the request.

Select the scenario and only relevant platform references. Write the brief and a
small capability table; do not start by copying cross-platform handlers.

### Step 2: prove uncertain native/binding capabilities

Create the smallest compile/native spike for any uncertain API. Prove availability
in the **consuming SDK**, not current upstream source alone.

Important research-derived gates:

| Target | Specific capability to prove |
|---|---|
| UIKit | Registration/diffable identifier binding, image preparation and deployed API minimum |
| Android | Resolved RecyclerView/Material APIs, holder/view-type lifecycle and optional library compatibility |
| WinUI | Stable SDK layout availability; never assume experimental general FlowLayout on 1.8 |
| AppKit | Native collection hosting/item ownership, NSObject identifiers, actual prefetch/reconfiguration APIs |
| GTK4 | GirCore native model/widget extension, runtime library exports, section/grid-header versions |
| WPF | Virtualizing generator/panel/recycling integration with retained MAUI content |

For a documented gap, select an explicit custom implementation or stop with a
specific limitation. Do not produce a stub that looks like supported code.

### Step 3: define the small shared contract

Create only the requested control properties/commands and an appropriate
presentation source. Typical fields are stable key, section/kind if needed,
content revision and size metadata.

Specify operations: append/prepend, changes/moves, query replacement, seek,
selection and anchors. Define loading/error/end states and ownership.
A small immutable resident source does not need a full paging abstraction; a
cursor source must not advertise arbitrary indexed fetch.

Keep HTTP/SQL/cache policy in the app's repository. Adapters and MVVM coexist;
preserve existing application commands/services.

### Step 4: implement one complete vertical slice

Wire one requested native host/layout, actual renderer, source publication,
loading trigger, item invoke/selection, sizing and teardown.

For MAUI handlers, cover property/command mapping, creation, connect/disconnect,
source replacement and callback cancellation. Do not inherit unsupported internal
collection classes merely to avoid implementing the lifecycle.

The slice must handle zero/one items, a realistic populated viewport, source
replacement, delayed assets and disconnect before moving on to other targets.

### Step 5: implement remaining requested targets

Reuse domain/source semantics and portable geometry where useful, not assumed
native API parity.

- Share UIKit code deliberately with `IOS || MACCATALYST`; avoid duplicate
  declarations from `.ios.cs`/`.maccatalyst.cs` inclusion.
- AppKit uses the repo's public `MacOSViewHandler`/dispatcher/conversion path.
- GTK uses `GtkViewHandler` and its owning context; do not invent a `LINUX` symbol.
- WPF and WinUI can have overlapping Windows TFMs but different native hosts;
  use the project's actual backend-specific compilation/registration, not only
  an ambiguous `#if WINDOWS`.

No unrequested platform project, package reference or handler override should be
added just to make a broad recipe look cross-platform.

### Step 6: optimize from evidence

Build native/reuse/source counters and deterministic fixtures first. Then profile
the real card layout and compare typed native versus retained templates only
where that choice matters.

Apply targeted fixes from [the optimization order](performance-validation.md).
Do not add large caches, shims, custom text engines or AOT flags without a measured
bottleneck and a compatibility check.

### Step 7: verify and hand off

Run focused portable tests, requested native target builds and native runtime
tests. Validate exact layout/chrome/interaction fidelity, delayed binding and
handler ownership. Measure the agreed Release scenario on each required platform.

Return generated files, setup/registration, the supported feature/version table
and actual evidence. Explicitly identify unbuilt/unrun/unmeasured targets.
A screenshot or successful portable test suite cannot mark every platform done.

## 6. Guardrails that belong in the entry skill

These rules are short enough to remain load-bearing in `SKILL.md`:

- Use a finite native viewport and a real virtualizing host/layout.
- Native container reuse, MAUI child reuse, data cache bounds and geometry lookup
  are separate acceptance conditions.
- Stable keys are not native positions; unchanged identity still needs content
  invalidation.
- No HTTP/SQL/full image decode in bind, count, indexer, type selection or measure.
- Bind is idempotent; async completions validate key, bind generation and epoch.
- Observe mutable child groups or use immutable section updates.
- Compatible template trees survive rebind; owned resources are drained on
  replacement/teardown.
- Don't treat `HasFixedSize` as uniform row height.
- Don't prescribe eager FlowBox/WrapPanel/FlexboxLayout for unbounded data.
- Don't equate Material carousel with coverflow, or justified rows with masonry.
- Don't assume UIKit/AppKit or WinUI/WPF API interchangeability.
- Version-gate experimental WinUI flow and future GTK grid headers.
- Preserve accessibility/input/focus and explicitly state unsupported behavior.
- Research/source evidence is not a measured performance claim.

## 7. Prototype roadmap before shipping the skill

Phase 1 is implemented. Phase 2's implementation/virtual-device behavior proof is
present, but its physical performance exit gate is not satisfied. Remaining
recipes/targets are not certified native implementations.

| Phase | State | Scope | Exit criterion |
|---|---|---|---|
| 1. Distill and validate references | Implemented | Entry routing, capabilities, source/geometry/lifecycle contracts | Short entry skill, source-backed selective references, static/distribution validation |
| 2. Mobile proof | Implemented; physical qualification pending | Photo masonry on UIKit/Android; real metadata, delayed thumbnails, cursor repository | Native Release behavior proof is present; physical device frame/memory evidence still required |
| 3. Desktop proof | Pending | WinUI/AppKit equivalent control and desktop interaction | Native custom layout/supplementaries, resize/DPI/focus/VoiceOver/Narrator, keyed anchors and traces |
| 4. Backend-gap proof | Pending | GTK model/factory slice then custom masonry feasibility; WPF native virtualizing wrap/renderer slice | Demonstrated native integration and bounded reuse; no eager fallback presented as parity |
| 5. Recipe breadth | Pending | Anchored grouped timeline, shelves, variable wrap, justified rows, coverflow | Each recipe proves its own operations/constraints and target capabilities rather than reusing one universal control |
| 6. Skill evaluation and release | Pending | Adversarial LLM evaluation and execution fixtures | Evaluation improves decisions; generated-code checks and native evidence pass |

The mobile proof is a useful first slice, not permission for the eventual skill to
ignore desktop/Linux requests. A recipe can remain explicitly unsupported on a
target until its custom path is verified; the skill must communicate that before
generating completion-shaped code.

### GTK/WPF feasibility is a real gate

Do not spend the first milestone trying to deliver every advanced GTK layout.
First prove retained renderer/factory/model updates on native ListView/GridView.
Then determine whether GirCore can express the required custom widget/interface,
or a small native binding/dependency is necessary.

For GTK masonry/wrap, compare a native virtualizing widget prototype with any
reviewed candidate; assess focus/accessibility/scroll adjustment as well as visual
geometry. The native 4.26 grid header work does not itself solve masonry or footers.

For WPF, verify compatible retained item trees and virtualizing panel/generator
behavior. The studied wrap library is a candidate to review, not an automatic
project dependency. Exact waterfall/coverflow remain different recipes.

### Avoid creating a new product by accident

Skill distribution uses existing marketplace conventions. If a later task
explicitly turns prototypes/helpers into shipped libraries, follow the repo's
product/package/build/signing/documentation checklist then. This plan does not
require new official pipeline jobs, NuGet packages or npm projects.

## 8. Evaluation scenarios

The implemented `tests/dotnet-maui/maui-bespoke-collections/eval.yaml` contains
the following decision/error matrix. These are authored evaluations, not claims
that model runs or native fixtures have passed.

| Scenario | Required decision / evidence | Wrong answer to catch |
|---|---|---|
| Stock list already meets requirements | Route to existing stock-control guidance | Gratuitous custom control and all-platform handlers |
| iOS variable-height waterfall | Metadata/custom geometry, stable keys, indexed visibility | Uniform grid or first-item height for every card |
| UIKit delayed image after rebind | Key/generation/epoch or keyed reconfiguration | Captured old cell receives another item's image |
| UIKit registration hot path | Registration lifetime outside provider | Per-cell new registration |
| Catalyst-only shelves | UIKit orthogonal sections and desktop interaction | AppKit NSCollectionView import |
| Android grouped staggered feed | Correct full-span chrome, all-span visibility, keyed identity | First-span-only pagination or stale full-span parameters |
| Android variable row list, fixed viewport | Correct HasFixedSize interpretation | Flag requires identical row heights |
| Android nested shelves | Compatible shared pool, finite viewport, keyed offset | Pool sharing across incompatible type IDs |
| Official Flowers sample adaptation | Correct stable-key/content comparisons | Copy reversed/overstrict sample comparisons |
| Android large wrap | Review archived Flexbox status or custom manager | Eager FlexboxLayout as virtualized default |
| Material versus 3D coverflow | Choose according to actual effect | Call masking a perspective coverflow |
| WinUI 1.8 general wrap | Custom stable VirtualizingLayout | Install/use experimental FlowLayout silently |
| WinUI repeater paging | Explicit viewport scheduler | ISupportIncrementalLoading automatically works |
| WinUI masonry sample adaptation | Per-context state, indexed lookup and anchor policy | Linear cached-rect scan described as scalable completion |
| AppKit collection request | Native NSCollectionView item/controllers and backend base | Existing repo handler already uses NSCollectionView |
| AppKit coverflow | Verified layer strategy | Invent Transform3D on AppKit layout attributes |
| GTK 4.12 grouped grid | Explicit native/binding gap, custom/approved alternative | Native grid header factory assumed available |
| GTK retained MAUI row | Child tree setup/rebind and proper teardown | Full template inflation on every bind |
| Huge GTK model | Bounded data/cache and sort awareness | Million-item count proves constant memory |
| WPF large wrapping list | Real virtualizing panel/generator integration | Ordinary WrapPanel with virtualization claimed |
| SQL-backed scrolling | Unique keyset order/index, bounded worker/projection | Deep OFFSET or async-named query in UI bind |
| Offline-first refresh | One DB truth path, atomic remote keys, epoch guard | Double append from HTTP and DB observer |
| Bidirectional chat | Prepend anchor, deleted-key fallback, explicit follow-tail | Always scroll to bottom on update |
| Group mutation during load | Child group observation, stable section continuation | Root ObservableCollection alone sees all children |
| Disconnect while work completes | Cancel/detach and reject old epoch | Old callback publishes into a new handler |
| "Done on every platform" without runtime | State unavailable evidence explicitly | Compilation/screenshot treated as performance proof |
| WPF true perspective | Bounded Viewport3D/PerspectiveCamera and interactive card hosting | 2D RenderTransform or invented WinUI APIs called native 3D |
| C# UIImage preparation affinity | Owning-dispatcher async initiation, bounded slot through native completion | Synchronous preparation inside Task.Run or releasing the slot too early |
| RecyclerView asynchronous layout proof | Settled frames and expected visible keys before each traversal step | Offset-field changes accepted as completed layout |
| RecyclerView whole-window pool churn | Per-type viewport/overscan pool with measured warm/repeat counts | Dataset-sized cache or strong retention of every historical holder |
| UIKit display ownership | Bounded displayed-cell ownership and image demand released/resumed with visibility | Weak diagnostics as sole resource ownership or reuse-only cleanup |
| HTTP fixture versus renderer | Native UIKit/RecyclerView with replaceable source policy and separate optional inspection transport | WebView rendering or an HTTP server imposed on database-backed apps |

The 20 added interaction scenarios cover native reorder/placeholder ownership,
in-recycler versus external transfer, edge drag realization, WinUI grouped/
variable-layout limits and unsafe sample hit testing, AppKit registration,
GTK model ownership, WPF effect negotiation, canonical/cursor ordering and
selected blocks, delete resurrection/undo, actual backend substitutions, refresh
indicator ownership and duplicate dispatch, recycled menu targets, WPF popup
context, UIKit empty/multi-item callbacks and non-drag pointer alternatives.
These are authored and structurally validated cases, not executed model results.

Use output assertions for robust routing/API/version concepts, negative assertions
for known wrong APIs/patterns, and rubrics for the actual decision. Do not make
tests hinge on exact prose phrasing. Follow repository scope and evaluation
security guidance; choose executor/judge models explicitly according to the
executing user's allowed-model policy.

**Text evaluation is necessary but insufficient.** It cannot prove generated code
compiles, a native factory recycles correctly, or frames meet a budget.

## 9. Execution validation for the recipes

Complement skill evaluations with:

| Layer | Test objective |
|---|---|
| Portable source/scheduler | Deterministic key/diff/group/paging/cancellation correctness |
| Portable geometry | Reference comparison for layout/intersection/anchor/resize, including randomized small datasets |
| Compile fixtures | Exact supported TFMs/native packages; no invented APIs or missing registrations |
| Native runtime fixtures | Assert actual hosts/layouts, realization/reuse, delayed content, focus and disposal |
| Performance fixtures | Actual Release traces, frame/memory/loading budgets and device manifest |
| Fidelity checks | Approved geometry/visual behavior, chrome and native accessibility/input |

Use xUnit/other existing app testing surfaces and the repo's native backend hosts
where applicable. Keep per-scenario process/device selection explicit. Do not
claim a benchmark passed because a test emitted a screenshot, timed out, or
returned a placeholder metric.

For template versus native binder comparisons, preserve the same content and
accessibility behavior. For HTTP/database fixtures, use deterministic delayed/
failed/duplicate responses and provider-realistic query behavior.

## 10. Completion criteria for shipping the skill

The implementation can be called comprehensive only when:

- entry description routes bespoke requests without swallowing ordinary
  CollectionView, general profiling or backend-installation work;
- references are source-backed, versioned, selective and internally consistent;
- every advertised recipe/target has a compiled/native-tested capability entry;
- native gaps are explicit, and there is no eager/stub/look-alike substitution;
- source/geometry/lifecycle and platform-native fixtures pass;
- performance claims include actual budgets, devices and traces;
- evaluation scenarios catch the documented high-risk mistakes;
- `skill-validator check` passes, and trusted-content evaluation uses permitted
  explicitly selected models in an appropriately isolated environment;
- plugin release/version documentation is updated without adding unrelated
  product/dependency infrastructure.

At that point the result is reusable **expert guidance for making small bespoke
controls**, backed by concrete examples and evidence, rather than another
oversized general-purpose collection component.
