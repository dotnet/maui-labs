# Linux: GTK4 / GirCore

Native/source guidance, not a compiled bespoke prototype.
[Sources G1-G6, G9, R1](sources.md)

## Native list/grid foundation

GListModel -> selection model -> GtkListView/GtkGridView -> list item factory.
Native views realize limited list items and recycle them. Place the GtkScrollable
view directly in GtkScrolledWindow, preserving finite native viewport constraints.

ListView supports vertical and horizontal display; GridView supports regular
orientation-dependent reflow, not arbitrary item spans/masonry. A custom eager
GtkLayoutManager does not create model-based virtualization by itself.

## Version gate

Research baseline: GirCore 0.7.0, backend runtime guidance GTK 4.12+, current
published pages 4.24.1. Inspect actual bindings/native library separately.

SectionModel and ListView header factory are since 4.12. Header factory items are
GtkListHeader, not GtkListItem. Inspected main grid header factory is marked
**Since 4.26**; generic SectionModel prose mentioning GridView is not availability
on 4.12/4.24 or proof of a GirCore symbol/runtime export.

Do not silently upgrade GTK/GirCore, synthesize a full-span grid cell, or claim
grouped-grid parity on that baseline. Custom/explicit alternative is required.

## Factory and model

| Signal | Responsibility |
|---|---|
| setup | Permanent compatible child hierarchy/listeners |
| bind | Current model key, reset state, item subscriptions/demand |
| unbind | Undo current bind, invalidate generation, release demand |
| teardown | Undo setup, disconnect/dispose owned children and signals |

Native list-item recycling with template recreation on every bind is insufficient.
Retain compatible MAUI trees/handlers; replace incompatible kinds cleanly. Pools
are per native lifetime/compatibility, not one view forever cached per data item.

Items are GObjects and positions are temporary. StringList fits strings/keys;
GListStore fits resident object data; custom indexed/windowed models need a real
GirCore registration/interface spike. No SQL/HTTP in get_item.

Publish ordered bulk model changes on the owning GTK context; notify changed
object properties too. Sorting/filtering can traverse all data; native model
wrappers do not make remote ordering or huge sorts cheap.

The Colors demo's huge logical count still has a large pointer table/retained
objects and source warnings about sorting stalls. Do not infer constant memory.

## Layout gaps

| Requirement | Path |
|---|---|
| List/grid | Native ListView/GridView with correct renderer |
| Grouped linear list | Section headers or typed rows; explicit footers/pinning |
| Horizontal shelves | Bounded nested list views, keyed offset and input policy |
| Small intentionally capped wrap | FlowBox is acceptable with disclosed bound |
| Large wrap/masonry/justified/coverflow | Specialized native virtualizer or reviewed binding/dependency |

FlowBox model binding creates a widget for every added item. A scrolled FlowBox/
GtkFixed containing all cards is not virtualization. General collection-manager
plug-in support like RecyclerView was not identified in GridView.

A custom widget must own model/key mapping, bounded child factory/pools,
metadata/checkpoints/range lookup, scroll adjustments/extent/anchors, allocation,
resize, hit testing, focus/accessibility and bring-into-view. Prove GirCore native
interfaces/vfuncs before assuming a managed subclass suffices.

## Backend and proof

Use GtkViewHandler and the registered GTK dispatcher/main context, not assumed
stock MainThread. Let GTK allocate row width; measure/arrange MAUI content under
that constraint. Horizontal content needs axis-correct measurement, not a vertical
width/infinite-height shortcut. Dispose allocation observers/signals and owned
safe handles; never manually free a value still owned by a safe handle.

Baseline repo CollectionView uses ListView/StringList, creates/disconnects MAUI
trees per bind/unbind, rebuilds the model on changes and does not select GridView.
Those are source findings, not performance measurements or current guarantees.

**Proof:** actual Linux runtime/version/renderer/display, setup/bind/unbind/
teardown and retained-tree/subscription counters, changes/resize/reconnect,
keyboard/wheel/accessibility and native/managed traces. Xvfb allocation checks
do not certify real-display GPU scrolling.

## Requested interactions

[Drag/drop](drag-drop-and-reordering.md) uses GtkDragSource/DropTarget/Async and
content providers, attached once in factory setup and cleaned up at teardown.
Mutate keyed model data, not recycled widgets. Move drag-end delete_data must not
duplicate source removal already owned by an internal reorder coordinator.

[Context actions](context-menus-and-actions.md) use native menu/action models;
[swipe](deletion-and-swipe-actions.md) and [pull refresh](refresh-and-reconciliation.md)
need explicit gesture/threshold/scroll arbitration. edge-overshot is not a complete
refresh state machine.

The inspected MAUI gesture bridge lacks drag/drop cases; RefreshView provides
button/spinner and SwipeView partial horizontal reveal. Some native bridges
explicitly execute Command before framework activation that executes it again.
Do not copy that double-dispatch or claim full gesture parity.
[Sources I21-I22, I26](sources.md#i21)
