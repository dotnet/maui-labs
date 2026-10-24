# Bespoke, high-performance collections for .NET MAUI

Research and a skill-development plan for **scenario-specific collection controls
backed by native platform views**, rather than an increasingly configurable
replacement for `CollectionView`.

**Research dates:** 2026-10-12 (rendering/layout/data), 2026-10-13 (interactions).
**Repository baseline:**
`56064071bfeaae0dd9aa962b6079d39a5281b875`.

This includes research and the initial [marketplace skill
foundation](../../plugins/dotnet-maui/skills/maui-bespoke-collections/SKILL.md),
plus a nonshipping [mobile photo-masonry prototype](../../samples/BespokeCollections/README.md).
Native documentation, selected sample implementations, and this repository's
handlers were inspected. The prototype now compiles and exercises real UIKit and
RecyclerView handlers on virtual devices; its
[evidence record](../../samples/BespokeCollections/EVIDENCE.md) distinguishes
behavior proof, diagnostic traces and unperformed physical qualification.
Recommendations are distinguished from observed API behavior
and source-level findings; a source's performance claim is not our measurement.
See the [source ledger](sources.md) for provenance and version caveats.

## Principal conclusions

1. **Specialize the control, not the entire application architecture.** A
   `PhotoMasonryView`, `ChatTimelineView`, or `ProductShelfView` can have a small,
   explicit contract. Share stable identity, loading, reuse rules, and tests;
   do not make each control implement every layout, gesture, and grouping mode.
2. **Keep the native collection engine where it fits.** UIKit and AppKit have
   reusable collections and compositional layouts; Android has `RecyclerView`
   and layout managers; WinUI has virtualizing items controls and layouts; GTK4
   has model/factory-driven lists and grids. Use a custom layout inside that
   engine before writing a new scroller and virtualizer.
3. **Use an adapter-oriented boundary, without banning MVVM.** Native adapters
   should consume cheap, indexed presentation data with stable keys and explicit
   updates. MVVM, immutable snapshots, and compiled MAUI item templates can feed
   that boundary. Binding and adapters solve different problems.
4. **Virtualization has several independent dimensions.** Recycling native
   containers does not prove that MAUI templates are reused; bounded view counts
   do not prove bounded data/image memory; neither proves that geometry lookup
   avoids an O(N) scan on every scroll.
5. **Geometry is often as important as cell construction.** Known image aspect
   ratios, predictable text constraints, stable size caches, and precise
   invalidation are essential for masonry, wrap, and variable-height timelines.
6. **Loading must be a repository concern, not a cell callback.** HTTP cursors,
   local indexed queries, and offline-first database caches require different
   policies. Native prefetch is a hint to a deduplicating, cancellable scheduler,
   not permission to make unbounded requests.
7. **Desktop quality is part of correctness.** Keyboard focus, wheel/trackpad
   input, accessibility, resize, RTL, and selection must survive recycling.
   A control that scrolls quickly but loses those behaviors is not complete.

These conclusions synthesize [Apple's performance guidance](sources.md#u3),
[Android's rendering guidance](sources.md#b2), [WinUI's collection
guidance](sources.md#w1), and [GTK's list framework](sources.md#g1).

## Reading map

| Document | Purpose |
|---|---|
| [Shared design and data](shared-design.md) | Control contracts, adapters versus binding, layout algorithms, grouping/nesting, loading, HTTP/DB decisions, reuse and handler ownership |
| [Performance and validation](performance-validation.md) | Optimization priorities, published lessons, profiling, measurable acceptance gates, correctness and stress tests |
| [Collection interactions](interactions.md) | Native drag/drop and reorder, swipe/delete, pull/button refresh, context actions, stable mutation/persistence and accessible alternatives |
| [Interaction sources](interactions-sources.md) | Dated platform APIs, native examples, release-pinned MAUI behavior and actual backend limitations |
| [iOS and Mac Catalyst](platforms/uikit.md) | `UICollectionView`, compositional/flow/custom layouts, snapshots, reuse, image preparation, desktop UIKit differences |
| [Android](platforms/android.md) | `RecyclerView`, linear/grid/staggered layouts, grouping, nested pools, custom layouts, Material carousel, Paging lessons |
| [Windows: WinUI and WPF](platforms/windows.md) | Native Windows MAUI plus the repo's WPF backend; layout/data virtualization, custom layouts, wrap and desktop interaction |
| [macOS AppKit](platforms/appkit.md) | `NSCollectionView`, compositional layouts, native item controllers, prefetch, binding differences, repo integration |
| [Linux GTK4](platforms/gtk4.md) | `GListModel`, native list/grid factories, version-gated sections, custom-layout gaps, GirCore and backend lifecycle |
| [Skill-development plan](skill-plan.md) | Routing contract, progressive references, generation workflow, prototype milestones, evaluation scenarios and completion gates |
| [Sources and evidence](sources.md) | Primary references, commit-pinned examples, inspected files, limitations and maintenance status |
| [Mobile execution evidence](../../samples/BespokeCollections/EVIDENCE.md) | Typed native photo masonry, bounded loading, native proof, actual trace results and remaining qualification gates |

## Layout vocabulary

Do not choose an implementation from a screenshot's informal name alone.

| Layout | Actual constraint |
|---|---|
| Linear list | One scrolling axis; fixed or variable item extent |
| Uniform grid | Shared cell/row geometry; predictable columns or rows |
| Variable row grid | Rows contain differently sized content but advance by the row's maximum height |
| Masonry/waterfall | Items enter columns of unequal accumulated height; no shared row boundary |
| Wrap/flow | Sequential items fill a line and then start another; widths/heights can differ |
| Justified image rows | Image aspect ratios determine widths around a common row height; not masonry |
| Shelf/orthogonal sections | Outer scrolling axis plus independently scrolling, bounded inner sections |
| Snapping carousel | Scrolling items settle at defined focal positions |
| Coverflow | Perspective/depth/overlap transforms around a focal item; not just a masked or scaled carousel |

Also specify whether placement is shortest-column, stable assigned-column,
row-major, or editorially arranged. These choices affect reorder behavior,
accessibility traversal, anchor preservation, and incremental invalidation.

## Native starting-point matrix

**Built-in** means the native toolkit provides the layout mechanism, not that a
MAUI handler exposes it or that a generated implementation is already verified.
**Custom** still requires a real virtualizing layout/viewport implementation.

| Scenario | iOS / Catalyst | Android | WinUI | AppKit | GTK4 | WPF |
|---|---|---|---|---|---|---|
| Vertical/horizontal list | Collection flow/compositional/list sections | `LinearLayoutManager` | `ListView` or stack layout on repeater/items view | Collection flow/compositional; `NSTableView` for a table-like list | `GtkListView`, either orientation | `VirtualizingStackPanel`, either orientation |
| Uniform grid | Flow/compositional | `GridLayoutManager` | `GridView`/`ItemsWrapGrid` or `UniformGridLayout` | Flow/compositional | `GtkGridView` | Custom/library virtualizing wrap panel |
| Pinterest masonry | Custom collection layout; metadata-driven custom groups when bounded | `StaggeredGridLayoutManager`, or custom manager for stricter placement | Custom `VirtualizingLayout` | Custom collection layout | Custom virtualizing native widget; no built-in masonry manager identified | Custom virtualizing panel; ordinary wrap is not masonry |
| Variable-width wrap | Flow or custom collection layout | Custom manager or reviewed Flexbox implementation | Custom `VirtualizingLayout` on stable 1.8; general `FlowLayout` is experimental | Flow/custom layout | `FlowBox` only for bounded small data; large data needs custom virtualization | Reviewed virtualizing wrap implementation |
| Justified photo rows | Custom grouping/layout geometry | Custom manager | `LinedFlowLayout` when its sizing/cropping rules fit | Custom grouping/layout geometry | Custom | Custom |
| Vertical feed of horizontal shelves | Compositional orthogonal sections | Nested, bounded recyclers and compatible shared pools | Bounded inner repeaters with viewport propagation | Compositional orthogonal sections | Bounded outer/inner native list views | Bounded nested virtualizing controls |
| 3D coverflow | Custom attributes/transforms and snapping | Custom manager/transforms; Material carousel is a different effect | Virtualizing layout plus Composition transforms | Custom layout plus layer transforms; UIKit transform API is not identical | Custom native viewport/transforms/input | Bounded native 3D/perspective host; ordinary render transforms are only 2D |
| Group headers/footers | Native sections and supplementary views | Typed rows or composed adapters; explicit spans/pinning | Grouped list/grid, or custom section projection | Native sections and supplementary views | List section headers since 4.12; footer rows/custom paths; grid headers in main marked 4.26 | Native grouped virtualization or typed rows/custom panel |

See the platform chapters before treating a cell in this table as a production
recipe. In particular, native layout support does not automatically provide sticky
headers, arbitrary spans, data virtualization, or exact cross-platform placement.

## What the repo actually provides today

The following are source observations at the baseline commit, not measured defects
and not changes made by this research.

| Backend | Observed implementation | Implication for bespoke controls |
|---|---|---|
| AppKit | `NSScrollView` plus a hand-built document/container virtualizer; template-keyed reuse pool; full flat-item scan to find visibility; collection changes reload the flattened source | Do not describe the existing handler as an `NSCollectionView` wrapper. A native collection-backed bespoke handler is a distinct integration path. |
| GTK4 | Direct `GtkListView` child of `GtkScrolledWindow`; string model and native selection models; MAUI templates are created on bind and disconnected on unbind; root changes replace the model | Native container virtualization exists, but template reuse and incremental model updates are separate work. `GridItemsLayout` is not a `GtkGridView` path here. |
| WPF | `ListBox`; linear path selects `VirtualizingStackPanel`; grid path selects ordinary `WrapPanel`; container preparation creates fresh MAUI template content; groups are flattened | The grid path is not a high-volume virtualizing grid. Native container recycling alone would not eliminate MAUI template recreation. |
| Official MAUI 10.0.41 | UIKit optimized `Items2` cells, Android RecyclerView adapters, Windows `ListViewBase` handler | Use release-pinned prior art, but do not couple new controls to internal handler classes. Current documentation separately describes a .NET 11 Windows repeater handler. |

Evidence: [repo handlers](sources.md#r1), [official MAUI
sources](sources.md#m4), and [version baseline](sources.md#r2).

## Skill foundation and remaining plan

The [skill foundation](../../plugins/dotnet-maui/skills/maui-bespoke-collections/SKILL.md)
implements the [plan's](skill-plan.md) first phase: one scenario-driven entry
point, selectively loaded references, and authored evaluation scenarios. It:

- identify the required layout, data access, target platforms, and minimum versions;
- choose the smallest native collection/renderer combination that satisfies them;
- generate only the requested control, handler implementations, loading boundary,
  and tests;
- explicitly report native/binding/backend gaps instead of silently substituting
  a nonvirtualizing layout or a look-alike;
- require build, native behavior, lifecycle, and measured performance evidence
  before calling a generated control complete.

The mobile prototype implements one narrow slice; physical performance
qualification, desktop/backend prototypes, executed LLM evaluations, and measured
recipe certification remain subsequent milestones. The plan does **not** propose shipping a new
collection framework, adding every platform as a dependency, installing the
studied third-party libraries, or replacing working `CollectionView` usage.

## Remaining empirical questions

The research establishes what to try and what to measure, not a fastest
implementation. Prototype work must determine native cells versus cached MAUI
templates, realistic variable-size geometry costs, desktop focus retention,
binding availability in the consuming SDK, and GTK custom masonry feasibility.

The requested platform scope is iOS, Mac Catalyst, Android, Windows, native macOS,
and Linux; WPF is included because it is an additional Windows backend in this
repo. Tizen, Comet's Compose/SwiftUI node backends, and web collections are not
evaluated here.
