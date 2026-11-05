# Recipe: anchored timeline

Use for variable-height ordered chat/history with prepend and optional groups.
Read [geometry](geometry-and-anchoring.md), [paging](paging-http-database.md)
and requested native platform references.

## Contract

Name a ChatTimelineView/HistoryView. Specify chronology/reverse display,
stable message/event IDs and revisions, bounded text/media sizing, prepend/append/
edit/delete operations, selection/invocation, and requested date/unread/group chrome.

Follow tail only if the user was already near/following the tail or explicitly
requests it. State the threshold/policy; do not force bottom on every update.

## Source and anchor workflow

1. Capture visible keyed anchor, relative offset and fallback neighbors.
2. Fetch bidirectional cursor/keyset pages through one bounded repository scheduler.
3. Normalize order and merge immutable/keyed presentation state.
4. Publish one serialized native update and group continuation.
5. Correct estimated extents using constrained measurements.
6. Restore the surviving anchor; use declared neighbor fallback when deleted.
7. Apply follow-tail separately, without moving a reader browsing history.

Cache heights by key/revision/width/font/template. A timestamp needs a unique key
tie-breaker. Group boundaries crossing pages retain one header and final footer
only when completeness is known.

## Same-source refresh and editor return

Before opening/returning from an editor or refreshing the same source, capture
anchor key/relative offset, fallback neighbours, constraints and user-scroll
revision. If same-key payloads temporarily clear/reload, reserve compatible prior
measured heights rather than replacing every row with a loading estimate.
Keep bounded height metadata independent of payload retention.

When accepted content arrives, invalidate only real content/revision/width/font/
template changes and restore after settled native measurement/update. A new user
scroll supersedes the saved restoration; deleted anchors use surviving neighbours.
Changing constraints can reflow legitimately, so use the declared alignment policy.

Test editor save/cancel/return, payload eviction, same-key refresh and a newer
scroll during delayed layout. Assert key and relative position within a stated
logical/pixel tolerance, not identity alone. This is a required generated-control
regression, not a feature certified by the metadata photo-masonry example.

Choose native sections or typed flat rows deliberately. Header/unread/loading/
error rows are not data counts/selection. Observe mutable group children or publish
new section snapshots. Collapse must not load all hidden messages.

## Native mapping

UIKit list/compositional sections, Android linear RecyclerView/typed or composed
adapters, WinUI native ListView or policy-equipped repeater, AppKit table/collection,
GTK ListView and WPF virtualizing stack are suitable starting points.
Use only the requested native interaction features; do not introduce a generic
all-platform chat framework.

## Lifecycle and proof

Reuse message renderers by compatible kind, clear editor/selection/media state,
pause playback on visibility but release bindings at their actual lifetime.
Late content must match key/bind generation/source epoch.

Test prepend while scrolled away from tail, removed anchor, same-key edits,
font/width changes, group moves/empty/collapse, page errors/refresh races, nested
media, keyboard/screen reader and reconnect. Assert identity/relative offset under
unchanged constraints and the declared resize policy otherwise.

Measure prepend/edit/layout latency and retained history/assets as well as fling
FPS. Unbounded retained messages are not data virtualization.
