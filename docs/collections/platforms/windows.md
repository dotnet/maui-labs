# Windows: WinUI 3 and the WPF backend

[Index](../README.md) | [Shared contracts](../shared-design.md) |
[Performance validation](../performance-validation.md)

These are **two different native UI stacks**, not interchangeable handler
implementations. Official Windows MAUI uses WinUI; the experimental repo backend
uses WPF.

## 1. WinUI control selection

| Native host | Prefer when | Obligations / limitations |
|---|---|---|
| `ListView` / `GridView` | Standard selection, grouping, focus and list/grid behavior | Use virtualizing items panels and a finite viewport |
| `ItemsView` | Standard collection interaction plus pluggable/swappable layouts | Native `ItemContainer` item-template root; inspect installed SDK |
| `ItemsRepeater` + scroll owner | A small, bespoke collection with custom policies | Implement required selection, keyboard, accessibility and loading behavior |

`ItemsRepeater` is a lightweight rendering/virtualization primitive, not a
fully featured list. It has no built-in scrolling and no comprehensive selection/
interaction policy. A `ScrollViewer` around a repeater is its **expected host**;
that is not equivalent to wrapping a complete `ListView` in another unbounded
same-axis scroller. [W1](../sources.md#w1)

`ItemsView` is built from repeater, scroll view, item container and transition
components. It supplies more interaction policy while accepting layouts.
Do not confuse `Microsoft.UI.Xaml.Controls.ItemsView` with the MAUI control of
the same short name. [W2](../sources.md#w2)

## 2. Version traps

The MAUI 10.0.41 reference source uses a `ListViewBase` handler and Windows App SDK
`1.8.251106002`. Current MAUI documentation separately describes a .NET 11
`ItemsRepeater`-based handler. The latter is not evidence of the repo's baseline.
[M2](../sources.md#m2) [M4](../sources.md#m4)

Windows App SDK 1.4 introduced `ItemsView`, `ItemContainer` and `LinedFlowLayout`.
Those mechanisms are available in the inspected 1.8 family, but bindings and
transitive package conflicts still need a compile check in the consuming app.
[W3](../sources.md#w3)

**General `FlowLayout` is not stable 1.8 `LinedFlowLayout`.** The attempted 1.8 API
page redirected to `windows-app-sdk-2.0-experimental` and marks the type
`Experimental`. Current Gallery source uses this newer path and suppresses
experimental API warnings. Do not automatically upgrade an app's SDK because a
Gallery example compiles on main. [W7](../sources.md#w7) [W8](../sources.md#w8)

For an app pinned to stable 1.8, unrestricted virtualized wrap needs a custom
`VirtualizingLayout` or a reviewed compatible implementation. Keep
`LinedFlowLayout` for its own fixed-height/variable-width image-row contract.

## 3. Layout recipes

| Scenario | Native starting point |
|---|---|
| Vertical/horizontal linear list | ListView virtualizing panel, or repeater/items-view `StackLayout` |
| Uniform grid | GridView `ItemsWrapGrid`, or `UniformGridLayout` |
| Justified/line-based photo gallery | `LinedFlowLayout` when native crop/stretch/row policy fits |
| Variable-sized masonry | Custom `VirtualizingLayout` using metadata and a bounded realization window |
| General variable-width wrap | Custom stable layout; experimental `FlowLayout` only by explicit opt-in |
| Grouped list/grid | Native grouped ListView/GridView, or typed section projection/custom layout |
| Shelves | Virtualized outer host plus finite inner repeaters and compatible element factories |
| Coverflow | Virtualizing backing layout plus bounded Composition transforms and snapping policy |

`ItemsStackPanel` and `ItemsWrapGrid` preserve ListView/GridView virtualization.
Ordinary `StackPanel`, `WrapGrid`, and `VariableSizedWrapGrid` do not. A panel
whose name contains "Grid" is not automatically a large-data virtualizer.
[W4](../sources.md#w4)

Keep the control's viewport finite. An auto-sized same-axis parent can cause the
control to expand to content and realize far more items. Prefer correct
star/fill constraints or a deliberate viewport size over arbitrary oversized
height values that mask the layout problem.

## 4. Custom VirtualizingLayout contract

Use `VirtualizingLayoutContext` to manage realization:

1. Store per-host state in `context.LayoutState`.
2. Determine required geometry from `RealizationRect`, including its buffer.
3. Honor the recommended anchor and appropriate layout origin.
4. Acquire/measure only required candidates with `GetOrCreateElementAt`.
5. Arrange realized elements and report the full/estimated content extent.
6. Allow automatic recycling, or explicitly recycle tracked outgoing elements
   when useful; do not retain unnecessary elements forever.
7. Process source/constraint changes and invalidate geometry precisely.

`VisibleRect` and `RealizationRect` are different: the latter includes the
realization buffer. Do not schedule expensive assets for every realized object
as if it were visible. [W6](../sources.md#w6)

Do not use one layout instance's mutable rectangle lists as global state across
multiple hosts. The context is the lifetime boundary. Protect empty sources,
partial final rows, invalid constraints, source replacement and deep seeks.

### Inspected Gallery examples

**ActivityFeedLayout** demonstrates row arithmetic from the realization rectangle,
cached property values, `LayoutState`, and acquisition/automatic recycling. It
assumes groups of three items and known row height. Adapting it requires empty
source and partial-row handling; do not copy its extent arithmetic as a general
grid algorithm.

**VariedImageSizeLayout** demonstrates shortest-column placement, incremental
measurement and cached bounds. Its start lookup walks cached bounds from the
beginning, source changes clear geometry, and mutable caches are instance fields.
For a production bespoke control, add indexed lookup, precise invalidation,
per-context state, and anchor/seek policy. Virtualizing UI elements does not
eliminate that geometry cost.

**ItemsRepeaterPage** tracks prepared/clearing elements and demonstrates
Composition-driven scale based on scroll position. Such counters are useful
evidence of bounded realization; the animation sample is not a complete
coverflow/focus/cleanup implementation. [W8](../sources.md#w8)

## 5. Source contracts and data virtualization

An indexed `IList`/`IReadOnlyList` avoids the internal full copy required for an
`IEnumerable`-only repeater source. Collection notifications publish changes.
`IKeyIndexMapping` helps preserve/reassociate realized elements after a reset
when stable keys are available. Plain index identity loses meaning after prepend
and reorder. [W1](../sources.md#w1)

ListView/GridView have documented native data-loading integrations:

| Pattern | Source behavior |
|---|---|
| Incremental loading | `ISupportIncrementalLoading`, indexed source and notifications; count is locally loaded count |
| Known-count random access | Nonblocking indexer returns placeholder; async fetch/cache and replace notification |
| Range-aware cache | `IItemsRangeInfo` reports actively used ranges, including buffered/focused items |

These interfaces exist in Windows App SDK; the current vendor documentation is
not merely old UWP guidance. [W5](../sources.md#w5)

However, **ItemsRepeater explicitly ignores** `ISupportIncrementalLoading`,
`IItemsRangeInfo` and `ISelectionInfo`. Implement viewport-based demand through
the bespoke source/scheduler. Do not assume ItemsView inherits ListView's loading
semantics just because it offers data virtualization; verify its current
source/projection behavior and use an explicit loading bridge where necessary.
[W1](../sources.md#w1)

A range cache must consider focused/anchored elements as well as the visible
rectangle. Shared repositories can share data, but eviction demand is per view.
Never synchronously fetch a missing row from the indexer.

## 6. Rendering, templates and lifecycle

Choose typed native assignment or native XAML/MAUI template hosting according to
the scenario. Native `x:Bind` and MAUI compiled bindings are not interchangeable.
The native ItemsView template requires an `ItemContainer` root; the repeater does
not wrap its template with a full selection/focus container automatically.
[W2](../sources.md#w2)

Pair `ElementPrepared` with `ElementClearing` and handle `ElementIndexChanged`.
Release item subscriptions, binding generation and asset demand when clearing;
stop/remove owned Composition animations before the element is rebound or destroyed.
An element's old data/index must not drive a later callback.

Native ListView/GridView can progressively populate content and use scrolling
placeholders; use those controls' supported lifecycle APIs when they fit.
Do not transplant `x:Phase` markup into a MAUI template and assume the MAUI XAML
compiler/native host supplies the same behavior.
[W4](../sources.md#w4)

Keep native UI work on `DispatcherQueue`; pure geometry and source preparation can
run elsewhere only with immutable data. A .NET object's lack of a native UI type
does not make publication into a WinUI-bound collection thread-safe.

## 7. Grouping, pinning and desktop behavior

Native grouped ListView/GridView can supply mature container and selection policy.
A repeater-based bespoke control instead needs section projection, header/footer
renderers, selection exclusion, group-change observation and its own pinning.

A flat full-span header row is not automatically sticky, and a masonry layout must
explicitly advance all columns around that row. Per-section paging must continue
an existing group rather than insert duplicate headers.

For coverflow, keep virtualizing geometry independent of Composition transforms.
Define visual overlap/hit testing, snapping, reduced-motion fallback and focus
movement. Composition animation reduces UI-thread transform work; it does not
fix an O(N) layout pass or excessive element creation.

Desktop completeness includes arrow/Home/End/Page navigation, tab entry/exit,
Ctrl/Shift selection where requested, focus after keyed moves, wheel/trackpad,
bring-into-view, high contrast and automation peers. Do not sacrifice native
selection/accessibility just to simplify the renderer.

## 8. WPF: separate native collection strategy

WPF's useful starting points are data-bound `ListBox`/`ListView` with
`VirtualizingStackPanel`, `VirtualizingPanel`, item-container generation and
`IScrollInfo` for custom panels.

UI virtualization defers containers; container recycling reuses them; data
virtualization is still an application concern. Adding prebuilt item containers
or replacing the items panel with an eager panel can defeat virtualization.
[P1](../sources.md#p1)

Recommended native configuration for a linear control:

- finite viewport with the native scroll owner;
- `VirtualizingPanel.IsVirtualizing = true`;
- `VirtualizationMode = Recycling`;
- `ScrollViewer.CanContentScroll = true`;
- native pixel scrolling where supported instead of disabling virtualization
  to get smooth pixel movement;
- `IsVirtualizingWhenGrouping` when genuine native grouping is used.

The last flag does not fix a nonvirtualizing group template/panel or unbounded
same-axis hierarchy. Measure actual generated containers in the final visual
tree. [P1](../sources.md#p1) [P2](../sources.md#p2)

### Grid, wrap and masonry

Ordinary WPF `WrapPanel` is eager. A large wrapping collection needs a
virtualizing panel that cooperates with the item generator and scrolling host.

The inspected `sbaeumlisberger/VirtualizingWrapPanel` implementation offers
orientation, configurable cache units, recycling, grouped/hierarchical behavior
and a size provider. Its variable-size documentation recommends metadata through
`ItemSizeProvider` and acknowledges correctness limitations without it.
Review integration/dependencies/license and benchmark the real MAUI cell tree
before adoption. Its million-item README claim is not our measurement.
[P3](../sources.md#p3)

A virtualizing wrap panel is not automatically shortest-column masonry. That
scenario needs its own placement/index/extent algorithm or a library whose actual
implementation matches it. Justified rows and overlap-based coverflow similarly
need specialized geometry. Ordinary WPF `RenderTransform` is 2D, so actual
perspective coverflow needs a bounded native `Viewport3D`/`PerspectiveCamera`
strategy with interactive `Viewport2DVisual3D` cards or a verified equivalent.
A 3D viewport does not supply virtualization automatically; input/accessibility
must be proven with the custom realization layer. [P6](../sources.md#p6)

### WPF source and rendering

Use data items rather than inserting a `ListBoxItem` per record. Keep durable
selection/expansion/editing state outside the recycled container.
Native WPF templates use WPF bindings; they do not support WinUI `x:Bind` or
MAUI's compiled-binding attributes merely because all three are XAML.

A reused native item container can still recreate its entire MAUI subtree.
Retain compatible child content and handlers when the scenario permits; release
item-specific state on rebind, and disconnect permanently evicted trees.

Source/panel update work belongs on the WPF `Dispatcher`. Keep operations short,
and do not block it with task waits or expensive indexer queries.
[P4](../sources.md#p4)

For native grouping, use the appropriate collection view/group style and test
hierarchical virtualization. Alternatively, a typed flattened-row source can make
headers/footers explicit, but then it owns selection exclusion, key translation
and full-span/pinning behavior.

## 9. What the repo's WPF handler does

At the research baseline, the handler:

- selects `VirtualizingStackPanel` for its default linear path;
- selects ordinary `WrapPanel` for `GridItemsLayout`;
- flattens groups into header/item/footer markers;
- observes root and child group collections;
- clears and recreates MAUI template content during container preparation;
- resets ItemsSource for some refresh/theme work.

The useful prior art is source observation, lifecycle protection and ownership.
The grid path is **not** a virtualizing grid, and the template path is not proof
of retained MAUI child-tree reuse. A bespoke control should register its own
handler and use the public WPF base rather than globally modifying all
CollectionView instances. [R1](../sources.md#r1) [R4](../sources.md#r4)

## 10. Required proof on Windows

Compile/run the **requested UI stack** on Windows; a Mac compile does not exercise
WinUI or WPF scrolling. Check native types, element/container counts, reuse and
source/callback cleanup. Cover resize, DPI changes, keyboard-only navigation,
Narrator/high contrast, grouping and random/rapid scrollbar movement.

Use WPR/ETW + WPA for native/UI work and .NET allocation/GC profiling for the
managed/native bridge. Compare identical Release fixtures with recorded SDK,
viewport, data, image and cache conditions. [P5](../sources.md#p5)
