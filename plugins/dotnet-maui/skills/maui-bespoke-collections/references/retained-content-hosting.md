# Retained MAUI content in native reusable rows

Use when existing MAUI cards/templates must survive native rebind. This is a
source-backed worked measurement/lifetime procedure, **not a newly certified
two-platform text control**. Start with [mobile checkpoints](mobile-vertical-slice.md).
[Sources H1-H3](sources.md#h1)

## Ownership before measurement

Keep one compatible MAUI tree and its native handler per retained slot, not a tree
per record. Bind the payload before handler creation when needed; use the owning
MauiContext, dispatcher and logical parent so resources/inherited state are correct.
Rebind compatible content without destroying handlers. Replace incompatible
templates cleanly. Keep global header/footer sizing separate from row caches.

| Quantity | Owner and units |
|---|---|
| Collection width, item frame/scroll extent | Native layout; Android pixels, UIKit points |
| Slot inset/decoration | Declared native row/layout policy, counted once |
| MAUI root Margin | Outer content spacing in logical units; not automatically a RecyclerView margin |
| MAUI padding, wrapping, child layout | MAUI content/layout implementation |
| Desired content height | The chosen constrained measurement bridge |
| Android density conversion | Owning context/display; convert at the bridge boundary |
| UIKit display scale | Pixel-sized assets only; not a multiplier for point-space layout |

Define whether the measurement result is inner content size or outer size including
root margin. MAUI's ComputeDesiredSize subtracts margin from constraints and adds
it to its result; ComputeFrame/Arrange applies it. Do not subtract/add the same
margin again around that public IView path.

A native-child measurement path can bypass that outer IView margin accounting.
For that path, reserve root margin explicitly once, measure the inner width, and
add the outer extent once. Do not assume root Margin was transferred to Android
LayoutParams or that every wrapper has identical behavior.

## Android: constrained cross axis, unconstrained scroll axis

Worked vertical-row sequence for a native-child bridge:

1. Obtain usable row width after collection padding/decorations/slot inset.
2. Convert declared MAUI root margins from logical units to pixels exactly once.
3. Subtract left/right outer margins from the inner width; reject unusable
   constraints. Set native child width to finite Exact/appropriate AtMost and
   variable height to Unspecified, not a stale loading/first-row exact height.
4. Let the native MAUI layout-backed child measure under those specs; its backing
   layout bridges to constrained MAUI measure. Read the actual measured height.
5. Include top/bottom outer margins in the row extent once. Cache against key/
   revision/template/width/font/style and invalidate when those inputs change.
6. Arrange the child in its matching inner pixel rectangle. Let the chosen bridge
   apply MAUI child layout; do not layer another collection layout on top.
7. Report corrected row geometry and preserve the keyed anchor after settled layout.

Example accounting: usable slot width 360 logical units, horizontal root margin
24, density 2.625 gives inner width 882 px, not 945 px. If the native child measures
210 px high and vertical root margin is 12 logical units, outer height is
241.5 px before final pixel rounding. The numbers illustrate units/ownership,
not fixed content sizes or a runtime benchmark.

Do **not** measure the MAUI child once with a finite width/infinite height and then
impose a second unrelated exact native height from an estimate. It can clip wrapped
text while counters still look correct. A framework-required final exact pass at
the **accepted** size can be legitimate for text alignment/bookkeeping; the rule
is no competing/stale height authority, not "all second passes are forbidden".

The alternative public IView.Measure/Arrange path is valid when implemented
consistently: it already owns margin accounting and the chosen native bridge must
not rediscover a contradictory height. Verify GetDesiredSize/cross-platform/native
measure responsibilities in the consuming package instead of mixing both recipes.
Horizontal collections constrain height and leave width variable instead.

## UIKit: point-space width, self-sizing height

1. Bind a retained compatible MAUI tree/native content into the cell's ContentView
   with the correct logical owner/context.
2. The native layout supplies finite point-space width. For a self-sizing vertical
   row, measure MAUI content with that width and unbounded height through one
   bridge; preserve metadata/fixed geometry if that is the actual card contract.
3. If the public IView path reports outer desired size, don't double-count root
   margins. Respect native slot inset separately. Never multiply width/height by
   UIScreen scale as though points were image pixels.
4. Return/copy matching self-sizing attributes or update the custom layout's
   measured extent. Don't mutate unrelated/shared layout attributes.
5. Arrange content using the accepted frame and matching constraints, after
   measurement. A rotation/layout callback without matching new measurement is
   not permission to arrange a stale height.
6. Process real measurement invalidation and restore a still-current keyed anchor,
   with bounded convergence instead of perpetual invalidation.

Do not use a first data row's cached size for variable rows or header/footer
content. Native preparation/display/recycle are separate lifetimes. Bind must
reset context/state even when PrepareForReuse is not called.

MAUI Items2 TemplatedCell2 illustrates constrained measure, cached constraints,
supplementary-view separation and deferred arrange. GeneralWrapperView and
internal measurement/layout helpers are **not public APIs to inherit/call** in
an app-owned handler. Extract the ownership algorithm, then compile the public
bridge against the exact SDK/package.

## Logical lifetime and smoke boundary

Use an appropriate public MAUI content/logical ownership surface; attaching a
UIView/ViewGroup alone does not establish the MAUI parent or resources.
Unbind invalidates item demand/subscriptions/generation but retains compatible
content. On replacement/teardown, detach the native view, logical ownership,
measure observers and owned handlers/resources. Do not dispose app-shared services.

Compile constructors, callback parameter types, aliases, mapper/registration and
cleanup in a small target first, then exercise **one** available platform.
Advance only after visible content and repeat reuse pass. C# source inspection
and the existing typed-native photo sample do not certify this retained path.

## Required visible regression fixture

Use deterministic content containing a multiline global header, short/long rows,
root margins, nested padding and a footer. Include font-scale/text/style changes,
several densities, viewport width/rotation and zero/one items. Compare actual
native frames and full displayed text; measured height fields or compile success
alone cannot prove no clipping.

Test payload eviction/loading and editor open/cancel/return with
[compatible prior heights and anchors](geometry-and-anchoring.md). Count both
native containers and MAUI trees/handlers over reverse traversal and disconnect.
Record the exact versions and unrun cases; do not advertise a generic wrapper as
a measured high-performance control.
