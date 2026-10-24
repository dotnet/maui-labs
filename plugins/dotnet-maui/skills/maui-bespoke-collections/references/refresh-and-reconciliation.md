# Refresh and loading reconciliation

Refreshing data is distinct from the gesture/indicator that starts it.
Read [interaction contracts](interaction-contracts.md) and
[paging](paging-http-database.md).
[Sources I3, I10, I13, I17, I22, I26](sources.md#i3)

## Define refresh ownership

Separate initial, refresh, append/prepend, retry and query replacement states.
Coalesce equivalent refresh intents or explicitly cancel/replace them.
Button, pull, menu and keyboard entry points use the same operation.

Choose data/anchor policy: same-query refresh can retain useful content while
fetching; query change may deliberately clear/reset. Preserve or reset keyed
selection, expanded groups, anchor and follow-tail according to that policy.
Do not turn every refresh into whole-source empty/reload unless required.
For same-key transient payload loading, keep compatible known height metadata
instead of generic estimates. [Anchor restoration](geometry-and-anchoring.md)
requires settled layout and an unchanged user-scroll intent revision, not just
the continued existence of the same record.

```text
acquire refresh owner -> cancel incompatible paging/cursors -> fetch/reconcile
-> publish accepted generation -> finish native request -> clear owned load state
```

Fetch/project off-thread; publish native-bound state on the owner dispatcher.
A superseded result cannot publish old data. An old operation's unconditional
finally must not stop a newer operation's shared spinner.

Finish each native deferral/request on success, error, cancellation and teardown,
but update the current app-owned refresh state only for its owner ID/generation.
Complete native request ownership and current app state are separate concerns.
Expose actual errors/retry; cancellation is neither successful empty data nor end.

Reconcile accepted pending deletes/reorders/tombstones with fetched content.
Order-dependent cursors need invalidation/version handling. A single query epoch
does not protect same-epoch local edits from an older refresh/page.

Coordinate active drag/swipe/edit/menu: cancel, defer or reconcile explicitly.
Do not let automatic pagination issue overlapping incompatible requests just
because the viewport reaches the end during refresh.

## Native integration

| Target | Mechanism |
|---|---|
| UIKit iOS/Catalyst | UIRefreshControl on the actual UICollectionView/UIScrollView; ValueChanged and EndRefreshing |
| Android | SwipeRefreshLayout around one real scrolling child; correct child-up/nested-scroll detection |
| WinUI | RefreshContainer/RefreshVisualizer; GetDeferral/Complete; RequestRefresh for non-touch entry |
| AppKit | Native command/button/menu plus progress; custom gesture only if requested |
| GTK4 | Native command plus GtkSpinner; custom pull state machine if requested |
| WPF | Native command plus progress; custom touch behavior if requested |

Keep a finite native viewport. A refresh container can wrap the real scroll owner;
adding a same-axis MAUI ScrollView around an already scrolling collection loses
the intended virtualization boundary.

UIKit needs appropriate bounce/insets, including short/empty content, and finish
on the owning dispatcher. UIRefreshControl's introduction and UIScrollView's
refreshControl property availability are separate. Catalyst requires pointer/
keyboard affordances; it is not AppKit.

For an Android custom manager, CanScrollVertically(-1) or the child-scroll-up
callback must reflect actual native offset. Preserve nested-scroll ownership;
do not disable it everywhere to force a pull gesture. Documented alpha Gradle
how-to dependencies are not the MAUI NuGet version to prescribe.

WinUI pull is touch-oriented; provide RequestRefresh/button/shortcut alternatives.
Acquire native deferral before async work and complete it on every exit.
Sample timers are not proof that repository data refresh succeeded.

GTK edge-overshot/reached is a boundary notification, not threshold/release/
cancel/refresh ownership. Desktop wheel-to-top must not repeatedly trigger refresh.
No UIKit/WinUI refresh API should be invented for AppKit or WPF.

## Stock MAUI protocol

On inspected MAUI 10.0.41, setting IsRefreshing=true invokes Refreshing and Command.
Do not then execute Command again or use a two-way binding to create feedback
reentry. Prefer one invocation route plus single-flight generation ownership.
The public indicator is already true when a pull's command starts: do not use
`if (IsRefreshing) return` as the work-start guard. Use a separate operation owner.
Set IsRefreshing=false when the current operation ends, including failure.

IsRefreshEnabled exists in that source and gates refresh while keeping content
interactive. IsEnabled=false disables the whole subtree. Check the resolved
package and custom-backend mapping; do not assume every backend implements the
new property or that Command.CanExecute alone completes a running request.

The inspected AppKit/WPF handlers are indicators without native pull. GTK offers
a refresh button/spinner; its click path may double-dispatch through core
IsRefreshing. These are source-level limitations, not native runtime certification.

## Proof

Exercise empty/short/top/scrolled lists, nested shelves, concurrent append/refresh,
query A/B cancellation and out-of-order completion, accepted local edits during
refresh, failures/retry, duplicate entry points, disconnect and native deferral/
indicator cleanup. Verify no stale spinner completion, resurrected rows,
unbounded page demand, lost anchors/focus or recreated full template trees.
