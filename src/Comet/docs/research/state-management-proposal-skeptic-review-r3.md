# Skeptic Review — State Management Proposal Rev 3 (Third Pass)

> **Reviewer:** Skeptic (third pass)  
> **Document:** `docs/state-management-proposal.md` Rev 3  
> **Cross-referenced against:** `src/Comet/Controls/View.cs`

---

## Executive Summary

Rev 3 is materially stronger than the earlier drafts. I did **not** find any new critical issues.

I did find **two high-severity** and **three medium-severity** issues that are worth fixing before treating the proposal as stable:

1. The new `Effect`-based body tracking in §4.6 is the wrong abstraction for view invalidation. It causes duplicate `Body` execution, creates an exception-handling mismatch with current Comet behavior, and briefly drops subscriptions during rebuild.
2. `ReactiveEnvironment` in §4.11 collapses all environment reads into one broad source, which regresses today's per-key environment routing into overly broad invalidation.
3. `ReactiveDiagnostics` still exposes `FlushDepthWarning` as a raw public static event even though the rest of the diagnostics surface moved to disposable subscriptions.
4. `Effect.Run()` can leave an effect stuck dirty-but-unscheduled after a transient exception.
5. `FlushSync()` is public but still runs reload work on the caller thread, which is a UI-thread-affinity trap.

---

## Findings

### High

#### 1. `Effect`-based view tracking turns a view rebuild into a side-effect loop

**Problem**

The Rev 3 proposal changed `View` integration from explicit body-dependency tracking to a general-purpose `Effect` (`docs/state-management-proposal.md` §4.6, lines 1143–1183). That simplifies ownership, but it changes the invalidation model in a way that is architecturally wrong for Comet views.

An effect eagerly re-executes its callback when dependencies change. A Comet `Body` should not be eagerly re-executed just to rediscover dependencies; it should be re-executed **once**, during the real rebuild that already happens in `Reload()`.

**Evidence**

- Proposal §4.6 re-runs `Body` inside `_bodyEffect` after every dependency change:
  - `docs/state-management-proposal.md:1166-1181`
- Proposal §4.5 flushes dirty effects **before** dirty views:
  - `docs/state-management-proposal.md:1094-1112`
- Proposal §5.1 separately states that a dirty view rebuild still performs `Reload() -> ResetView() -> Body.Invoke()`:
  - `docs/state-management-proposal.md:1718-1724`
- Current Comet implementation rebuilds via `Reload() -> ResetView() -> GetRenderView() -> Body.Invoke()`:
  - `src/Comet/Controls/View.cs:267-287`
  - `src/Comet/Controls/View.cs:346-381`
- Current Comet also has explicit debug-time exception behavior around `Body.Invoke()`:
  - `src/Comet/Controls/View.cs:384-390`

**Why it matters**

This creates three separate problems:

1. **Duplicate work per state change.** On a signal change, the proposal's effect re-runs `Body` in Phase 1 only to re-track dependencies, then Phase 2 runs `Reload()` which rebuilds the view tree again. That means two `Body` executions for one logical update.
2. **Exception semantics drift.** The proposed `Effect.Run()` catches and suppresses exceptions, while current Comet surfaces `Body` exceptions in debug and rethrows otherwise. Using `Effect` for body tracking silently changes that behavior.
3. **Subscription blind spot.** `GetRenderViewReactive()` disposes the old `_bodyEffect` before creating the new one (`docs/state-management-proposal.md:1156-1158`). During the subsequent `Body` execution, no prior subscriptions remain active, so concurrent signal writes can be missed during rebuild.

Taken together, this means the proposal solved the earlier leak/ownership problem by choosing the wrong abstraction. It is a lateral move: cleaner ownership, but at the cost of correctness and performance characteristics that matter more for MVU rendering.

**Recommended change**

Replace `_bodyEffect` with a dedicated body-dependency tracker owned by `View`.

- Track dependencies during the **actual** `Body` evaluation inside `GetRenderViewReactive()`.
- Keep the previous dependency set subscribed until the new dependency set is known, then diff old/new just like `Computed<T>` does.
- On dependency change, mark the view dirty without eagerly re-running `Body`.
- Dispose the tracker when the view is disposed or hot-reload-replaced.

This keeps `Body` execution single-source-of-truth: one real build, one dependency capture, one invalidation path.

---

#### 2. `ReactiveEnvironment` regresses per-key routing into broad invalidation

**Problem**

Rev 3 §4.11 models the entire environment system as a single `IReactiveSource`. `TrackRead(key)` ignores the key and registers `this`, and `SetValue(key, value)` notifies all environment subscribers through the same source.

**Evidence**

- Rev 3 proposal §4.11:
  - `ReactiveEnvironment.TrackRead(key)` calls `ReactiveScope.Current?.TrackRead(this)`
  - `ReactiveEnvironment.SetValue(key, value)` calls `_subscribers.NotifyAll(this)`
- Current Comet tracks environment usage per key:
  - `src/Comet/Controls/View.cs:592-603`
  - `src/Comet/Controls/View.cs:605-645`
- Current global environment updates also route by the changed key:
  - `src/Comet/Controls/View.cs:549-583`

**Why it matters**

The current framework remembers which environment keys a view actually consumed via `usedEnvironmentData`, then updates the matching property when that key changes. Rev 3 would flatten that into "this view depends on environment" as a single coarse dependency.

That means a change to one key such as font size could invalidate work that only cared about a different key such as background color. This pays down none of the current complexity but loses existing precision.

**Recommended change**

Make each environment key its own reactive source.

- `TrackRead(key)` should subscribe to a source for that specific key.
- `SetValue(key, value)` should notify only subscribers to that key.
- Styled keys and typed keys should remain distinct sources, matching current environment lookup behavior.

---

### Medium

#### 2. Diagnostics API still has one leak-prone static event

**Problem**

Rev 3 correctly moved `ViewRebuilt` and `SignalChanged` behind disposable subscription helpers, but `FlushDepthWarning` remains a public static event.

**Evidence**

- Disposable subscription API:
  - `docs/state-management-proposal.md:1539-1552`
- Raw public static event still exposed:
  - `docs/state-management-proposal.md:1554-1558`
- Scheduler invokes the event directly:
  - `docs/state-management-proposal.md:1053-1057`

**Why it matters**

The proposal explicitly calls out static-event rooting as a design problem, then leaves one diagnostics path using exactly that pattern. That is not a runtime correctness bug by itself, but it weakens the proposal's "hard to misuse" story and leaves the diagnostics surface inconsistent.

**Recommended change**

Add `OnFlushDepthWarning(Action<int>)` returning `IDisposable`, make the backing event private, and route `ReactiveScheduler` through an internal `NotifyFlushDepthWarning(depth)` helper.

---

#### 3. `Effect.Run()` can wedge an effect after a transient exception

**Problem**

Rev 3 §4.3 says the exception path "stay dirty for retry", but the effect is not actually re-queued after the failed run.

**Evidence**

- `Effect.OnDependencyChanged()` sets `_dirty = true` and queues the effect once.
- `Effect.Flush()` calls `Run()` and does not requeue on failure.
- `Effect.Run()` catches, discards partial reads, and returns without clearing `_dirty` or scheduling another flush.

**Why it matters**

After one transient failure, the effect can remain `_dirty = true` while no longer being present in `ReactiveScheduler._dirtyEffects`. Future dependency changes hit the `if (_dirty || _disposed) return;` guard and do not requeue it. The effect silently stops reacting.

**Recommended change**

On exception, discard partial reads, keep the previous dependency set unchanged, and clear `_dirty` so a later dependency change can requeue the effect. If stronger error semantics are desired, document them explicitly rather than implying automatic retry.

---

#### 4. `FlushSync()` is a caller-thread trap

**Problem**

Rev 3 exposes `ReactiveScheduler.FlushSync()` as a public API, but its implementation just calls `Flush(depth: 0)` on the caller thread.

**Evidence**

- Proposal §4.5 exposes `FlushSync()` for event handlers and tests.
- The body of `FlushSync()` resets `_flushScheduled` and immediately calls `Flush(depth: 0)` with no dispatcher/main-thread guard.
- `Flush()` performs `view.Reload()`, which is view-tree work that should stay on the UI thread.

**Why it matters**

The rest of the proposal is careful to coalesce onto the dispatcher. `FlushSync()` bypasses that safety. If a background-thread mutation calls it, the design now invites a UI-thread violation via a public API that sounds safe.

**Recommended change**

Require `FlushSync()` to be called on the UI thread and throw otherwise, or marshal it to the UI thread synchronously. The API surface should make the threading contract explicit.

---

## Final Assessment

The overall direction is still good. The proposal is not drifting into over-engineering, and the remaining issues are concentrated in the view-integration boundary rather than the core `Signal<T>` / `Computed<T>` primitives.

That said, the `Effect`-based body integration and coarse environment tracking should be corrected before this proposal is treated as the stable target architecture. View invalidation wants a dedicated invalidation tracker, not a general side-effect primitive, and environment routing should preserve the key-level precision Comet already has.
