# Architect Review — State Management Proposal Rev 4

> **Reviewer:** Architect (direction assessment)  
> **Document:** `docs/state-management-proposal.md` Rev 4 (2,324 lines)  
> **Cross-referenced against:** `src/Comet/` source code, 3 prior skeptic reviews  
> **Verdict:** **Agree with direction. Two structural concerns remain.**

---

## Direction Assessment

**Overall:** Good — with two reservations.

**Summary:** This proposal correctly identifies every major flaw in the current state
management system and replaces them with a well-understood reactive primitive set. After
four adversarial review cycles, the core primitives (`Signal<T>`, `Computed<T>`,
`ReactiveScheduler`) are solid. My concerns are not about the reactive core — they're
about **what the proposal doesn't address** and one **coupling pattern it introduces**.

---

## Where I Agree

### 1. The problem diagnosis is excellent

The seven problems identified in §2 are real and well-documented. I verified each against
the source code:

| Problem | Verified? | Notes |
|---------|-----------|-------|
| P1: Implicit conversion trap | ✅ | `Binding.cs:75-94` — the proposal's characterization is exact |
| P2: Fragile global/local classification | ✅ | `Binding.cs:157-236` — the heuristic is genuinely fragile |
| P3: Threading unsafety | ✅ | `StateManager.OnPropertyChanged` → `View.Reload()` without thread hop |
| P4: No automatic coalescing | ✅ | Three `state.Value = x` assignments = three `Reload()` calls |
| P5: Binding stabilization | ✅ | `_bindingStable` at `Binding.cs:66,273,289` — dynamic deps silently dropped |
| P6: Static global state | ✅ | Actually **7** static collections, not 6 (proposal says 6; there's also `_propertyNameCache`) |
| P7: Runtime-only readonly enforcement | ✅ | `StateManager.cs:259` — runtime throw, no compile-time check |

The proposal's §2 is the strongest section of the document. Anyone evaluating this
proposal should read it first — the current system's problems justify the migration cost.

### 2. The three-primitive model is the right abstraction level

`Signal<T>`, `Computed<T>`, `Effect` — this is the same primitive set that SolidJS, Preact
Signals, and Angular Signals converged on. It's not a coincidence. These three primitives
are the minimal complete set for a push-based reactive system:

- **Signal** = mutable source of truth
- **Computed** = derived, cached, lazy
- **Effect** = imperative side-effect bridge

The proposal doesn't over-abstract (no `Observable<T>`, no `Subject<T>`, no operator
chains). It doesn't under-abstract (no "just use events" or "just use INotifyPropertyChanged").
The abstraction level is appropriate for a UI framework.

### 3. The Rev 4 view integration is correct

The move from `Effect`-based body tracking (Rev 3) to `BodyDependencySubscriber` (Rev 4) is
the right call. The prior skeptic review correctly identified that `Effect` was a lateral
move — it solved the ownership problem but introduced double-Body-execution.

The Rev 4 `GetRenderViewReactive()` design:
- Tracks deps during the **actual** Body build (not a speculative pre-run)
- Keeps old deps subscribed until new deps are known (diff-based)
- Lets exceptions bubble naturally (preserves current Comet debug behavior)
- Disposes cleanly when the view is disposed

This is structurally identical to how `Computed<T>.Evaluate()` manages its deps, which is
good — one dependency-management pattern used consistently, not two different mechanisms.

### 4. The migration path is realistic

The three-phase approach (§7) is the right way to do this:

- **Phase 1** (additive): `Signal<T>` implements `INotifyPropertyRead` to bridge both systems
- **Phase 2** (deprecation): `[Obsolete]` on `Binding<T>` implicit conversion + `State<T>`
- **Phase 3** (removal): Clean break

The Phase 1 bridge code (§7, lines 1977-2014) is particularly well thought out — the
`ReactiveScope.Current != null` check elegantly routes reads to the correct tracking system
depending on which context the signal is read from.

### 5. The StrongBox<T> pattern for torn-read safety is sound

I initially questioned the allocation cost, but for UI state mutations (single-digit per
user interaction), one `StrongBox<T>` allocation per write is indeed negligible. The
alternative (locking the getter) would add contention on the hot read path, which is worse.

---

## Where I Disagree — or at least want discussion

### Concern 1: `ReactiveScheduler` is still global static state

**Priority: Medium (architectural direction)**

The proposal correctly diagnoses `StateManager`'s 7 static dictionaries as a problem (P6)
but then introduces `ReactiveScheduler` as a new static class with its own global state:

```csharp
public static class ReactiveScheduler
{
    private static volatile bool _flushScheduled;
    private static readonly HashSet<Effect> _dirtyEffects = new();
    private static readonly HashSet<View> _dirtyViews = new();
    private static readonly object _lock = new();
    // ...
}
```

This is smaller and better-designed than `StateManager`, but it's the same structural
pattern: a static class holding mutable state shared by all views in the process. The
proposal §2 states:

> *"(a) memory leak risk if views aren't properly disposed, (b) lock contention under
> the single ReaderWriterLockSlim, and (c) implicit coupling between all views"*

Points (b) and (c) still apply to `ReactiveScheduler`:
- All signal writes in the application contend on `_lock` (though the critical section is
  smaller than `StateManager`'s `ReaderWriterLockSlim`)
- All views in the process share a single flush queue, meaning one view's pathological
  behavior (e.g., a cycle that hits `MaxFlushDepth`) affects every other view

**This is not blocking.** The proposal is still a net improvement because:
1. The lock critical sections are tiny (HashSet.Add, not list-manipulation + dictionary-lookup)
2. The coupling is one-directional (views → scheduler, not scheduler ↔ views)
3. Unit tests can `FlushSync()` without needing to mock anything

But it's worth acknowledging that "no global state" is an overstatement. A future evolution
might inject `ReactiveScheduler` as a scoped service, particularly for multi-window
scenarios where independent flush cadences might be desirable.

**Recommendation:** Add a note to §9 Open Questions acknowledging this as a known
trade-off rather than claiming P6 is fully solved.

### Concern 2: The `Computed<T>` → `Computed<T>` notification chain can cascade unboundedly inside a single flush

**Priority: Medium (behavioral)**

Consider this dependency graph:
```
Signal A → Computed B → Computed C → Computed D → ... → BodyDependencySubscriber
```

When Signal A changes:
1. A's `NotifySubscribers()` calls B's `OnDependencyChanged()`
2. B sets `_dirty = true` and calls `_subscribers.NotifyAll(this)` (line 648)
3. B's `NotifyAll` calls C's `OnDependencyChanged()`
4. C sets `_dirty = true` and calls `_subscribers.NotifyAll(this)`
5. ... and so on down the chain

This all happens **synchronously inside the `Signal<T>.Value` setter's lock** (line 432-446).
For a chain of N Computeds, the setter holds `_writeLock` for O(N) notification hops.

Compare to how SolidJS handles this: signals mark computations dirty but don't propagate
through the computation graph synchronously. Instead, computations are evaluated lazily
during the flush. The proposal's `Computed<T>` does evaluate lazily (on `Value` read),
but the **dirty propagation** is eager and synchronous.

For typical UI (chains of 2-3), this is fine. For a form with 50 fields feeding a
`Computed<bool> isFormValid`, Signal A's setter would synchronously walk all 50 subscribers
plus any downstream Computeds.

**This is not blocking.** The practical impact is small because:
1. Notification is just setting a bool + HashSet.Add — very cheap per hop
2. The `if (_dirty) return` guard (line 642) means each node is visited at most once
3. Real-world Comet views rarely have deep Computed chains

But it's a scaling characteristic worth documenting. If someone builds a spreadsheet-like
dependency graph, they'll hit this.

**Recommendation:** Add a brief note in §5.1 about the synchronous dirty propagation
characteristic, or at least a comment in the `Computed.OnDependencyChanged` code.

---

## Patterns I Like

### Diff-based dependency updates

The same pattern used in `Computed.Evaluate()`, `Effect.Run()`, and
`View.GetRenderViewReactive()`: keep old deps subscribed, evaluate, diff old vs new,
subscribe/unsubscribe the delta. This is the correct approach (SolidJS, Preact Signals
both do this) and the proposal applies it consistently across all three consumer types.

### Per-signal locks instead of one global lock

`Signal<T>._writeLock` per signal vs `StateManager`'s single `ReaderWriterLockSlim`.
This eliminates false contention between unrelated state mutations. Two buttons updating
two different signals no longer serialize on the same lock.

### Weak subscriber references

`SubscriberList` using `WeakReference<IReactiveSubscriber>` with compaction during
notification. This prevents the memory leak pattern where forgotten subscriptions root
views. The current `StateManager` doesn't do this — it uses strong references in
`NotifyToViewMappings`, relying on explicit disposal.

### `IsEnabled` guard on diagnostics

`ReactiveDiagnostics.IsEnabled` check before any diagnostic work. Zero overhead in
production. This is better than the current system where `Debug.WriteLine` calls are
scattered throughout with `#if DEBUG` guards.

---

## What's Missing

### 1. No story for `INotifyPropertyChanged` interop beyond migration

The proposal focuses on migrating from `State<T>` to `Signal<T>`, but many real-world
apps have view models or services that expose `INotifyPropertyChanged`. There's no
guidance for binding a `Signal<T>` to an INPC-based model, or for wrapping an INPC
property as a `Signal<T>`.

This isn't a design flaw — it's a scope gap. But it's worth noting because Comet users
don't live in isolation from the .NET ecosystem. A one-paragraph note in §9 about
INPC bridge patterns would be sufficient.

### 2. No guidance on signal field naming conventions

The proposal shows `readonly Signal<int> count = new(0)` but doesn't address naming.
Should it be `count`, `_count`, `Count`? The existing `State<T>` fields use lowercase
(`readonly State<int> count = 0`), which matches. But since `Signal<T>` is a new type,
this is a chance to set (or explicitly reaffirm) a convention.

### 3. Computed<T> initialization timing

`Computed<T>` is initialized `_dirty = true` (line 533) and evaluates lazily on first
`.Value` read. But if a `Computed<T>` is created in a constructor and never read during
the first `Body` build, it will never subscribe to its dependencies. This is correct
behavior (lazy evaluation), but it's worth a documentation note for developers who expect
the Computed to start reacting immediately.

---

## Summary Scorecard

| Aspect | Assessment |
|--------|-----------|
| **Problem diagnosis** | Excellent — all 7 problems verified against source |
| **Primitive design** | Strong — minimal, well-understood, battle-tested in other frameworks |
| **Threading model** | Good — per-signal locks, dispatcher coalescing, StrongBox for torn reads |
| **View integration** | Good after Rev 4 — BodyDependencySubscriber is the right abstraction |
| **Environment integration** | Good after Rev 4 — per-key EnvironmentKeySource preserves precision |
| **Hot reload** | Good — reflection-based signal transfer with trim annotations |
| **Migration path** | Realistic — three-phase, backward-compatible bridge in Phase 1 |
| **Diagnostics** | Complete — IDisposable subscriptions, gated behind IsEnabled |
| **Global state** | Improved but not eliminated — ReactiveScheduler is still static |
| **Documentation gaps** | Minor — INPC interop, naming, Computed initialization timing |

**Bottom line:** This proposal is ready for implementation planning. The core architecture
is sound after four review cycles. The remaining concerns (ReactiveScheduler static state,
synchronous dirty propagation) are known trade-offs that can be addressed incrementally,
not blocking issues.

I would proceed to Phase 1 implementation with the current design, capturing the
ReactiveScheduler concern as a future-work item rather than blocking on it.
