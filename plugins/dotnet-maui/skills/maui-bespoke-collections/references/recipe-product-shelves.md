# Recipe: product shelves

Use for a virtualized vertical feed of independently scrolling horizontal sections.
Read [geometry](geometry-and-anchoring.md), [reuse](reuse-and-handler-lifecycle.md)
and requested platform references.

## Contract

Define ProductShelfView with stable section/item keys, bounded shelf cross-axis
extent, card geometry, snap behavior, requested group/global chrome and data source.
Persist each shelf's anchor/offset by section key, not outer-row index.

One scroll owner per axis is a useful default, not a ban on all nesting.
An unbounded same-axis inner collection can defeat virtualization; a finite-height
horizontal shelf can retain both levels.

## Native mapping

| Target | Path |
|---|---|
| UIKit/AppKit | Prefer native compositional orthogonal sections when they fit |
| Android | Bounded nested recyclers; compatible shared RecycledViewPool |
| WinUI | Bounded inner repeaters/items hosts with viewport-aware factories |
| GTK4 | Bounded outer/inner native list views with tested axis measurement |
| WPF | Bounded outer/inner virtualizing items controls |

Do not create all inner controls or load all inner records before outer realization.
Native orthogonal sections avoid unnecessary app-owned nested scrollers.

## Reuse and demand

1. Create compatible outer row/inner native host once.
2. Save the departing section's keyed inner position before rebind.
3. Cancel/release section demand without cancelling other shared consumers.
4. Reset old subscriptions/source/selection and bind the new section.
5. Restore the new section's saved position after its accepted layout.
6. Prefetch near the inner/outer viewport within one aggregate view/data budget.

Android pool sharing needs compatible holder types and consistent type namespace,
including ConcatAdapter isolation. Set InitialPrefetchItemCount near initially
visible items, not entire shelves. UI prefetch is not background inflation.
[Sources B2, B4, U4](sources.md)

Collapsed/empty shelves need explicit height/chrome/loading behavior. Source
replacement/reorder must not apply another section's offset or stale assets.

## Proof

Count outer and inner realized/native/MAUI trees together. Test outer recycle with
inner offset restoration, section reorder/removal, rare renderer types, diagonal
gestures, wheel/trackpad routing, keyboard traversal and accessible section names.
Measure combined scroll/creation/prefetch latency and total asset memory, not
only one isolated horizontal row.
