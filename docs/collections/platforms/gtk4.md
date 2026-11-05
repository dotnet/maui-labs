# Linux: GTK4, GirCore and native list models

[Index](../README.md) | [Shared contracts](../shared-design.md) |
[Performance validation](../performance-validation.md)

## Native architecture

GTK4 has a strong collection architecture for lists and regular grids:

```text
Gio.ListModel / GListModel
      -> selection model
      -> GtkListView / GtkGridView
      -> GtkListItemFactory
      -> limited reusable GtkListItem + child widgets
```

`GtkListView` and `GtkGridView` realize a limited set of items and recycle them.
They implement `GtkScrollable` and should be **direct children of
`GtkScrolledWindow`** for proper viewport-driven behavior. Inserting an eager
wrapper between them can change the scroll/measurement contract.
[G1](../sources.md#g1)

`GtkListView` supports horizontal **and** vertical linear display. The existing
repo comment that it is always vertical is not the native API's current contract.
`GtkGridView` has orientation-dependent reflow and native grid accessibility roles.
[G3](../sources.md#g3)

## Baseline and version gates

The repo currently references GirCore GTK bindings **0.7.0** and documents native
runtime testing with GTK **4.12+**. Current published GTK API pages identify
**4.24.1**, while inspected main source already contains upcoming 4.26 annotations.
These are not interchangeable environments. [R2](../sources.md#r2)
[R5](../sources.md#r5) [G8](../sources.md#g8)

| Feature | Evidence / gate |
|---|---|
| Native ListView/GridView realization and factories | Established GTK4 architecture |
| List section model and header factory | Since 4.12 |
| Grid section header factory | Inspected main source explicitly says **Since 4.26** |
| Scrolling/selection/newer convenience APIs | Check binding symbol and runtime minimum separately |
| Custom C# GObject/ListModel implementation | Verify GirCore registration/interface support in a compile/runtime spike |

The generic `GtkSectionModel` documentation mentions GridView, but at inspection
the published grid header-factory API page did not exist and source annotations
were 4.26. **Do not generate grid header code for 4.12/4.24 or assume GirCore 0.7.0
contains it.** Even a managed symbol compiling would not prove the deployed native
library exports it. [G4](../sources.md#g4) [G5](../sources.md#g5)

## Layout selection

| Scenario | Recommended starting point | Gap / caution |
|---|---|---|
| Large vertical/horizontal list | `GtkListView` | Correct axis-sensitive MAUI child measurement |
| Large regular grid | `GtkGridView` | Native reflow is not arbitrary per-item span/masonry |
| Grouped linear list | Section model + ListView header factory, or typed rows | Footers, pinning and empty/collapsed groups need explicit policy |
| Vertical feed of horizontal shelves | Bounded outer/inner native list views | Shared budgets, keyed shelf state and input routing |
| Small variable-width wrap | `GtkFlowBox` | Eager; cap the dataset intentionally |
| Large wrap/masonry/justified gallery | Custom virtualizing native widget or reviewed native dependency | No built-in general collection layout-manager extension identified |
| Coverflow | Custom virtualizing native viewport with transforms/input | Overlap, clipping, focus and accessibility are substantial work |
| Full-span grouped grid on baseline | Custom path or explicit alternative presentation | Do not treat a group header as an ordinary grid cell and claim parity |

GTK's layouts do not expose a RecyclerView-style pluggable manager for
`GtkGridView`. A `GtkLayoutManager` that arranges all of an eager widget's children
does not by itself provide model-based realization/recycling.

This is the largest platform-specific implementation gap in the proposed masonry/
general-wrap recipes. Call it a feasibility/prototype task, not solved by a
`GtkGridView` property or an ordinary `FlowBox`.

## Factory lifecycle: reuse the child, not just the wrapper

`GtkSignalListItemFactory` has four distinct stages:

| Signal | Correct responsibility |
|---|---|
| `setup` | Construct reusable child widgets, attach stable container-level state |
| `bind` | Bind current model object/key, reset appearance and attach item-level signals |
| `unbind` | Undo the current bind: item subscriptions, asset demand, context and generation |
| `teardown` | Undo setup: permanent child tree, stable callbacks and owned resources |

Setup/teardown generally happen once per native list-item lifetime; bind/unbind
repeat. Recreating a full child tree in every bind defeats much of the native
reuse benefit even if native list-item count stays bounded.
[G2](../sources.md#g2)

For one bespoke row kind, keep a stable child hierarchy and update fields.
For heterogeneous kinds, retain compatible trees per slot/renderer policy, or
replace incompatible content with proper cleanup. GTK's generic native item pool
does not automatically partition every MAUI `DataTemplate` for you.

Group header factories configure `GtkListHeader`, not `GtkListItem`. Treat that
native lifetime/type separately. [G4](../sources.md#g4)

## Data models, identity and notification

`GListModel` items are `GObject` instances. Model position is an unsigned index,
not durable identity. Choose a model that can return cached lightweight objects
cheaply and notify ordered range changes.

| Model approach | Fit |
|---|---|
| `GtkStringList` | Simple string/key list; not a general DTO store |
| `GListStore` | Resident GObject-backed data with inserts/removals and bulk splice operations |
| Custom indexed/windowed GListModel | Very large data or nonblocking placeholder access, if bindings support it |
| Sort/filter/tree wrappers | Native model projection; preserve original domain keys and understand source traversal costs |

A StringList can carry opaque keys backed by an indexed managed projection, but
stringifying every object and rebuilding it on every mutation is not the default
high-performance design. Keep selection, expansion and durable state by key;
restore native selection positions after structural updates.
[G1](../sources.md#g1)

Bulk `items-changed` notifications avoid per-item signaling storms. Publish
mutations on GTK's owning main context and ensure the underlying source state
matches the notified positions/counts. Updating a GObject's content also needs
appropriate property notification/binder refresh; structural notification alone
does not automatically update every displayed field.

Sorting/filtering wrappers can inspect the entire model. Native incremental
facilities, where available, can spread work, but do not turn a remote source into
indexed database access. Sort/project large paged data in the repository when its
ordering contract requires it; never perform synchronous SQL/HTTP in `get_item`.

## What the official GTK demos actually show

**Colors:** the native demo uses `GtkGridView`, property-expression binding,
selection/sort machinery and a custom model that creates color GObjects on demand.
Its dataset can represent 16,777,216 colors, but it still allocates a large pointer
table and retains created objects. The source explicitly warns that sorting the
largest datasets can stall the application.

Take the model/factory separation, not an assertion that huge logical counts
automatically produce constant-memory data access or cheap global sorting.

**Settings:** native tree flattening with `GtkTreeListModel`, selection and
sort/filter models feeds ListView/ColumnView. It shows how hierarchical data can
be projected into a virtualized list without constructing every tree widget.
Both examples are real native GTK source, not MAUI approximations.
[G7](../sources.md#g7)

## FlowBox does not virtualize because it binds a model

The inspected `gtk_flow_box_bound_model_changed` implementation creates/inserts a
widget for **every added item**. A scrolled FlowBox therefore retains an eager
widget hierarchy. It is appropriate for intentionally bounded small chip/tile
groups, not a substitute for large-data wrap virtualization.
[G6](../sources.md#g6)

For large variable-width wrap or waterfall, a custom native viewport must own:

- model-to-keyed-item mapping and a bounded child factory/pool;
- geometry metadata/checkpoints and viewport intersection lookup;
- scroll adjustments/extent/anchor correction;
- constrained realization/measurement and resize invalidation;
- lifecycle signals, hit testing, focus, native accessibility and bring-into-view.

Implementing those surfaces is a native virtualizer, not just assigning positions
to all children in `GtkFixed`. A library dependency should be evaluated against
the same contract.

## Grouping, chrome and nesting

On supported ListView versions, use `GtkSectionModel` and a separate header factory
for real section headers. Native section headers do not establish arbitrary
footers or sticky behavior. Typed nonselectable footer rows can work for a linear
list; explicit pinning/overlays need their own geometry and accessibility policy.

The baseline grid cannot give a synthetic header cell a full-row span just
because it represents a group. For a full-span grouped grid, use a verified custom
implementation, a supported newer native/binding stack by explicit decision, or
a clearly disclosed alternative. Do not claim that a bounded nested grid with its
own scroll axis is identical to a single grouped scrolling grid.

For shelves, keep inner ListView height finite and outer rows virtualized.
Save offsets by section key, reset reused rows, and test wheel/trackpad/keyboard
routing. Do not wrap the native list in an unbounded layout to prepend a header;
model rows/native section machinery or a correctly bounded outside overlay
preserve the scroll contract.

## GirCore and MAUI integration

Use the repo's `GtkViewHandler` base and registered dispatcher. GTK UI work belongs
to its owning main context; the repo dispatcher uses `GLib` idle/timeout mechanisms.
A stock MAUI `MainThread` service is not a safe universal assumption for this
custom backend. [R4](../sources.md#r4)

For retained MAUI templates:

- use the existing native conversion/layout bridge;
- let GTK assign native row width, then measure/arrange MAUI content under that
  allocation;
- remeasure only when constraint/content changes, not every frame by default;
- separate linear horizontal measurement from vertical width-constrained rows;
- retain compatible handlers through bind/unbind while managing logical ownership;
- remove allocation observers/signals and disconnect permanently evicted trees;
- preserve native widget/reference ownership; do not manually free a value still
  owned by a GirCore safe handle.

A custom `GObject` model/native widget or new GTK interface must be proven in the
actual GirCore version. Public C documentation is not enough to assume a trivial
C# subclass can implement every native vfunc or interface.

## The current repo handler's limits

At the baseline, `CollectionViewHandler`:

- directly hosts `GtkListView` in `GtkScrolledWindow`, correctly preserving native
  list virtualization;
- uses `GtkStringList` and selection models;
- flattens group headers and has no-op global header/footer mappers;
- does not select a native `GtkGridView` for a MAUI grid layout;
- recreates MAUI template content in bind and disconnects it during unbind;
- replaces the model on a root collection change;
- measures template content with a width constraint and unbounded height.

Thus the reusable native list-item count does not prove template-tree reuse,
incremental data publication, grouped-grid parity or fully validated horizontal
template behavior. A new bespoke control must implement the exact chosen path;
do not silently alter every existing CollectionView. [R1](../sources.md#r1)

## Required Linux evidence

Record native GTK version, GirCore version, display backend, renderer and scale.
Use an actual Linux runtime for native factory/model/reuse tests. The repo already
has explicit GTK/Xvfb runtime test guidance; headless allocation checks do not
prove performance on a real GPU/display. [R5](../sources.md#r5)

Count setup/bind/unbind/teardown, retained MAUI trees, signal subscriptions and
model objects. Stress rebind/source replacement/resize and verify no stale asset
or selected state leaks.

Use Sysprof for native/main-loop work, GTK frame information where available, and
.NET allocation/GC evidence for the managed bridge. Current GTK release notes
show ongoing list correctness and frame-clock changes, so compare the same
toolkit/renderer when evaluating a handler optimization.
[G8](../sources.md#g8) [G9](../sources.md#g9)
