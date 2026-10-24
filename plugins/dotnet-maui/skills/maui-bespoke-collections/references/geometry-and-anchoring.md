# Geometry, realization and anchors

These are algorithm goals, not device benchmarks. Let N be logical items, V be
viewport candidates and C columns. [Sources U6, W6, W8, G1](sources.md)

## Separate geometry from visual trees

MAUI places the outer control; native collection/layout code owns item geometry,
scrolling and realization. Constrained MAUI cell measure/arrange supplies content
sizes without running a second full collection layout.

UI virtualization does not remove O(N) rectangle arrays, metadata, source records
or scans. Record their memory/rebuild costs separately.

| Geometry | Candidate strategy |
|---|---|
| Fixed list/grid | Offset/row arithmetic approaching O(V) work |
| Immutable variable extents | Prefix/interval data plus binary search |
| Variable extent point changes | Fenwick/segment tree for O(log N) sums/lookup |
| Masonry | Ordered per-column intervals or indexed blocks |
| General wrap | Line starts/extents and checkpoints |
| Coverflow | Bounded logical candidates expanded for transformed bounds |

An array-backed Fenwick tree does not make structural insertions/moves O(log N).
A wrap or shortest-column edit can reflow a suffix; do not promise constant cost.

## Measurement cache

Key sizes by presentation key, content revision, renderer/template, cross-axis
constraint, text scale/font and relevant style revision.

Use fixed dimensions or image aspect-ratio metadata where the contract permits.
Use constrained estimates/self-sizing otherwise. Do not give every item the first
item's size when heights vary, or inflate all offscreen views for an exact extent.

On a late correction, invalidate affected geometry, preserve the keyed anchor,
and bound convergence. Width/font/template changes need different invalidation
from an image arriving inside already reserved aspect-ratio bounds.

Transient payload loading/eviction is not automatically a size invalidation.
Reserve a compatible known height for the same key/content revision, width,
font/style and template while data reloads; keep this metadata bounded. Do not
replace measured history with generic loading estimates merely on editor return.
Actual content/width/font/template changes require deliberate remeasure/reflow.
See [retained-content hosting](retained-content-hosting.md) for margin/units and
one measurement authority; root margin must not be counted by two bridges.

Validate finite nonnegative geometry, positive usable constraints, spacing/insets,
orientation/RTL, zero items, partial final rows and extreme valid aspect ratios.
Surface invalid input/layout failures; do not leak exceptions through native
callbacks or return success-shaped fallback geometry.

## Anchor protocol

Before mutation, capture a visible item key, its relative offset, and fallback
neighbor keys. After update/measurement, locate the surviving key and restore
relative placement. If deleted, use the declared next/previous fallback.
Capture a user-scroll intent revision too. Restore only after accepted source and
settled layout/measurement, and only if no newer user scrolling has superseded
that request. Do not apply a delayed saved offset over the user's new position.

Anchor restoration is not merely retaining an absolute scroll offset. Prepending
or changing an earlier height changes that offset while the visible item stays
the same. Preserve nested offsets by section key.

For chat, follow tail only when the user was already following it or explicitly
requests it. A background update must not drag a reader away from older history.

On width/font changes, item reflow is legitimate: preserve the agreed identity/
alignment policy, not an impossible absolute-pixel guarantee.

## Chrome and section boundaries

Global chrome, section headers/footers, sticky overlays, empty states and load rows
are distinct. Chrome must not inflate data counts or selection.

Full-span masonry chrome advances all columns to a common boundary. Pin a header
within its section and collide with the next boundary; define z-order, hit testing
and duplicate accessibility exposure. Native header insertion alone is not pinning.

## Lookup and seek

Honor the native realization buffer and focused/pinned items, but distinguish
realized from visible asset demand. Avoid scanning all rectangles on every scroll.

Unknown-total/cursor sources cannot support arbitrary index jumps by pretending
to know total count. Deep variable-size seeks can require prefix computation or
approximate correction when no metadata/checkpoint exists. State that cost.

**Test:** compare indexed intersections to a slow correct reference on randomized
small data; exercise prepend, removed anchor, suffix reflow, resize, metadata
changes and deep seeks. Instrument candidates and offscreen measurements at scale.
Include same-key loading/payload eviction and editor open/cancel/return, asserting
key plus relative offset in explicit logical/native-pixel units with a declared
tolerance. Unchanged identity or a raw measured height alone is not sufficient.
