# Windows: WPF backend

Not WinUI. [Sources P1-P5, R1](sources.md)

## Real native virtualization

Use data-bound ListBox/ListView with VirtualizingStackPanel for linear content.
Use a real custom/library virtualizing panel and item generator for grid/wrap.
UI virtualization, container recycling, data virtualization and retained MAUI
child content are separate.

Keep a finite viewport and native scroll owner. Select IsVirtualizing, Recycling,
CanContentScroll=true and appropriate pixel scrolling rather than disabling
virtualization for smoothness. IsVirtualizingWhenGrouping is necessary for
native grouped virtualization but does not repair an eager/unbounded group panel.

Do not insert prebuilt ListBoxItem objects for every record. Native WPF binding
is not WinUI x:Bind or MAUI x:DataType.

## Wrap and custom panel

Ordinary WrapPanel is eager. The studied VirtualizingWrapPanel is a source-inspected
candidate with orientation, cache units, recycling, grouping and a size provider.
Its variable-size documentation recommends ItemSizeProvider and acknowledges
limitations without it. Review compatibility/license/dependencies and measure
the actual MAUI renderer; publisher million-item claims are not our benchmark.

Wrap is not shortest-column masonry. Custom panels must cooperate with generator,
IScrollInfo, extent/offset/viewport, realization/recycling, source changes, size
metadata, focus/bring-into-view, RTL and grouping. Do not instantiate all children
in MeasureOverride and call it a virtualizing panel.

## True 3D versus 2D carousel

WPF RenderTransform is a 2D transform path. Scale/translation alone can make a
carousel, not perspective coverflow. For actual perspective, investigate native
Viewport3D/PerspectiveCamera with bounded Viewport2DVisual3D-hosted interactive
cards or an explicitly verified equivalent.

A Viewport3D does not automatically virtualize items: retain a bounded candidate/
renderer set and define scroll/snap, depth hit testing, keyboard/accessibility
and reduced motion. Do not invent WinUI PlaneProjection/Composition APIs in WPF.
This requires a compile/native interaction spike, not a claimed solved recipe.

## Reuse and source ownership

Keep selection/editing/expansion by stable item key, not recycled container.
Retain compatible MAUI child trees/handlers and rebind context; remove item demand/
subscriptions on unbind and disconnect on actual eviction/replacement.

Use native grouped views or typed flat rows deliberately. Flat headers/footers
need unique keys, full-span layout, selection exclusion and separate sticky policy.
Observe mutable group children or publish immutable section state.

Publish bound changes on WPF Dispatcher; no blocking SQL indexer or synchronous
task waits. Use the public WPFViewHandler/backend registration appropriate to the
head. WINDOWS alone does not distinguish it from a WinUI head.

## Repo baseline and proof

The inspected repo path chooses VirtualizingStackPanel for linear and ordinary
WrapPanel for GridItemsLayout; container preparation recreates MAUI content and
groups are flattened. That is not a large-data virtualizing grid/reusable subtree
proof. Register a bespoke handler, not a global replacement.

**Proof:** Windows native panel/generator/viewport counts, retained tree reuse,
changes/grouping, source/handler cleanup, keyboard/focus/virtualized bring-into-view,
wheel/trackpad/high contrast/automation, resize/DPI and Release WPR/WPA + managed
memory evidence. Portable tests or a Mac build do not prove WPF native behavior.

## Requested interactions

[Drag/drop](drag-drop-and-reordering.md) uses DoDragDrop/DataObject, movement
thresholds, routed events, bounded adorners/auto-scroll and explicit keyed mutation.
DragDropEffects does not move data. [ContextMenu](context-menus-and-actions.md)
is a popup tree: bridge PlacementTarget/current context rather than row ancestors.

The inspected repo SwipeView substitutes a context menu, and RefreshView is a
progress bar without pull. Its mouse/reflection drag bridge is not complete data/
result/reorder support. Use explicit [delete](deletion-and-swipe-actions.md) and
[refresh](refresh-and-reconciliation.md) commands or implement required native
gesture behavior; never import WinUI SwipeControl/RefreshContainer as WPF APIs.
[Sources I23-I24, I26](sources.md#i23)
