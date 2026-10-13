# Reuse and handler lifetime

Native container reuse does not prove MAUI child reuse or bounded model/assets.
[Sources M1, M4, U3, B4, G2, P1, R1](sources.md)

## Four lifetimes

| Stage | Do |
|---|---|
| Create/setup | Construct compatible child tree; cache children; stable container listeners |
| Bind | Set current key/revision; reset appearance; attach item subscriptions/demand |
| Unbind/recycle | Invalidate bind generation; release item subscriptions/demand/transient state |
| Destroy/teardown | Remove stable listeners; disconnect/dispose owned trees, handlers and native resources |

Retain compatible children/handlers between binds. Replace incompatible renderer/
template content with proper disposal, not a per-record forever-growing pool.
Drain pools on handler teardown and bound them by renderer/viewport needs.

Reset text/image placeholder, selected/expanded/editing state, visibility,
accessories, transforms, layout parameters and accessibility fields on bind.
Reconfiguration may not call a reuse callback: bind must be idempotent.
Reset open swipe side, drag/drop highlight, translation/elevation and transient
menu context too. [Interaction ownership](interaction-contracts.md) is keyed and
source-scoped; a menu opened for A must not silently retarget to B after rebind.

Disappearing/detach is not always recycle. UIKit preparation may never display
and a cell may reappear without a new bind; Android detach can be temporary.
Keep visibility-driven playback separate from identity/subscription lifetime.

## Async completion

Associate demand with current item key, binding generation and source epoch.
On completion, recheck all three before touching an active view, or update keyed
presentation state and request native reconfiguration.

Do not capture a reusable cell and assign a delayed image blindly. A cancellation
token alone is insufficient: cancellation can race with successful completion.
One consumer releasing demand need not cancel a request used by another consumer.

## MAUI template hosting

Create one compatible template tree per retained slot, use the correct
MauiContext/conversion bridge, update BindingContext, and preserve logical child
ownership/resources/semantics. Measure/arrange under native cell constraints.

Detach old ownership without destroying a tree deliberately retained for reuse.
On replacement/eviction/disconnect, unsubscribe and disconnect its handlers.
Do not recreate the entire tree to solve every state-reset problem.

## Handler checklist

1. Scope the handler/mapper to the named control and register once.
2. Connect current source/native listeners/dispatch bridge once.
3. Publish native-bound mutations on the owning control/window dispatcher.
4. Invalidate source/handler generations before replacing or disconnecting.
5. Unsubscribe old root, group and item observations.
6. Detach native weak delegates/callbacks while retaining bridges for active use.
7. Cancel owned work and dispose owned timers/controllers/assets/pools.
   Include drag auto-scroll, imports/placeholders, native refresh deferrals,
   action groups, popovers/adorners and their exactly-once completion/cleanup.
8. Do not dispose shared repositories/caches owned by the application.
9. Call the appropriate base disconnect after custom teardown.

Use `IOS || MACCATALYST` for shared UIKit, `MACOS` for AppKit. Inspect GTK/WPF head
symbols rather than inventing them. Use public custom-backend bases/dispatchers:
stock MainThread services need not exist on custom targets.

At native callback boundaries, report failures through app-standard logging and
explicit error state; do not broadly swallow errors or substitute blank success.
Expected stale-epoch/cancellation rejection is part of the lifetime protocol.

**Test:** late result after rebind/source change, double connect/disconnect,
reconnect/new context, event count, owned versus shared disposal, pool eviction,
rare renderer types and selection/transform leakage. Count both native creations
and MAUI tree/handler creations during repeat traversals.
