# Windows: WinUI 3

Do not use WPF APIs in this handler.
[Sources W1-W6, M2, M4, P5](sources.md)

## Host selection

| Host | Choose for |
|---|---|
| ListView/GridView | Standard native grouping/selection/focus/list or grid policy |
| ItemsView | Collection policy plus swappable/pluggable layouts |
| ItemsRepeater + scroll owner | Low-policy bespoke rendering; implement required interaction/loading |

ItemsRepeater has no scrolling owner or complete selection/input policy. Its
ScrollViewer host is expected, unlike nesting a complete ListView in an unbounded
same-axis scroller. Keep the viewport finite.

MAUI's ItemsView and Microsoft.UI.Xaml.Controls.ItemsView are different.
Native ItemsView templates require an ItemContainer root. Native x:Bind is not
MAUI compiled binding.

## Version/layout gates

MAUI 10.0.41 inspected source uses ListViewBase and App SDK 1.8.251106002.
Separate .NET 11 documentation describes a repeater handler; inspect the app.
ItemsView/ItemContainer/LinedFlowLayout arrived in App SDK 1.4.

General FlowLayout was documented as 2.0-experimental at research time, not stable
1.8 LinedFlowLayout. The Gallery suppresses experimental warnings; that is not
permission to upgrade an app silently.

| Scenario | Native mechanism |
|---|---|
| Linear | Virtualizing list panel or StackLayout on repeater/items view |
| Uniform grid | ItemsWrapGrid or UniformGridLayout |
| Common-height photo rows | LinedFlowLayout if its stretch/crop rules fit |
| General wrap/masonry | Specialized VirtualizingLayout on the stable baseline |
| Shelves | Bounded nested repeaters and scoped factories |
| Coverflow | Virtualizing geometry plus bounded Composition transforms |

ItemsStackPanel/ItemsWrapGrid virtualize. Ordinary StackPanel/WrapGrid/
VariableSizedWrapGrid do not.

## Layout contract

Store host-specific state in context.LayoutState, use RealizationRect for buffer/
viewport candidates, honor recommended anchor/LayoutOrigin, acquire only needed
elements, arrange them, report extent and recycle outgoing content appropriately.
VisibleRect is not RealizationRect: asset demand should not treat every buffered
element as visible.

Handle empty/partial rows, resize, source replacement, deep seek and invalidation.
Do not share mutable per-view caches on one layout instance across hosts.
Gallery ActivityFeed assumes three-item rows; VariedImageSize scans cached bounds
linearly and broadly invalidates. Adapt mechanisms, not those scalability limits.

## Source and lifecycle

Indexed IList/IReadOnlyList avoids repeater's IEnumerable-only internal copy.
Use notifications and IKeyIndexMapping to preserve keyed elements across reset.
Unchanged keys still require content/size invalidation.

ListView/GridView support documented ISupportIncrementalLoading and range-aware
random-access sources. **ItemsRepeater ignores ISupportIncrementalLoading,
IItemsRangeInfo and ISelectionInfo.** Bridge viewport demand explicitly; do not
assume ItemsView inherits ListView semantics without verifying its current source.

Pair ElementPrepared/ElementClearing and process ElementIndexChanged. Release
item subscriptions/demand, invalidate generation and remove owned Composition
animations before rebind/destroy. Publish native-bound state on DispatcherQueue.

Native progressive templates/scrolling placeholders have native lifecycle rules;
MAUI templates do not automatically implement x:Phase behavior.

## Desktop correctness and proof

Groups/native supplementary policy versus flat projection must be explicit.
Custom headers require full-span/pinning, selection exclusion and accessibility.
Composition transforms do not fix O(N) geometry or missing keyboard policy.

For [drag/reorder](drag-drop-and-reordering.md), ListViewBase CanReorderItems needs
AllowDrop and excludes grouped sources/VariableSizedWrapGrid. ItemsView/Repeater
do not inherit those flags: use native transfer events and explicit keyed mutation.
[SwipeControl](deletion-and-swipe-actions.md) and
[RefreshContainer](refresh-and-reconciliation.md) are touch-oriented, with
non-touch alternatives and native deferral/visual cleanup.
[Context actions](context-menus-and-actions.md) retain intended keys across
recycled elements, not a cached container. [Sources I15-I18](sources.md#i15)

Test arrows/Home/End/Page/Tab, Ctrl/Shift selection where requested, focus after
moves, virtualized bring-into-view, wheel/trackpad, high contrast and Narrator.
Prove actual hosts/layouts, reuse, source/context cleanup, resize/DPI and Release
WPR/ETW/WPA plus managed allocation evidence on Windows. Mac compilation is not
Windows native runtime proof.
