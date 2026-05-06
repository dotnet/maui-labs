# Skeptic Review — State Management Proposal Rev 2 (Second Pass)

> **Reviewer:** Skeptic (adversarial)  
> **Document:** `docs/state-management-proposal.md` Rev 2 (2149 lines)  
> **Scope:** Defects introduced or papered-over by the Rev 1 → Rev 2 fixes  
> **Cross-referenced against:** actual Comet source in `src/Comet/`

---

## Skeptic Analysis

### Bugs Found

#### 1. Signal\<T\>.Value getter reads `_value` outside the `_writeLock` — torn reads for large value types

- **Location**: Proposal §4.1, lines 412–419 (getter) vs 420–438 (setter)
- **Priority**: Critical
- **How to trigger**: Use a `Signal<T>` where `T` is a value type larger than the platform's native word size (e.g., `Signal<decimal>`, `Signal<Matrix4x4>`, `Signal<(long, long, long)>`, or any user-defined struct > 8 bytes). Have thread A write via the setter (which acquires `_writeLock`) while thread B reads via the getter (which does NOT acquire `_writeLock`). Thread B observes a **torn read** — a partially-written value mixing bytes from the old and new values.

- **Impact**: Corrupt data silently propagated through the reactive graph. A `Signal<decimal>` storing a currency amount could produce a value that is neither the old nor the new price. The proposal's own §3.D claims this is addressed ("Lock protects equality check + write + notify as an atomic unit"), but the getter bypasses the lock entirely.

- **Evidence trace**:
  ```
  // Proposal line 414-418 — getter, NO lock
  get
  {
      ReactiveScope.Current?.TrackRead(this);
      return _value;   // ← naked read of _value
  }
  
  // Proposal line 420-438 — setter, INSIDE lock
  set
  {
      lock (_writeLock)
      {
          // ...
          _value = value;  // ← write protected by lock
          // ...
      }
  }
  ```
  A lock only provides mutual exclusion when BOTH readers and writers acquire it. The getter acquires nothing. For reference types (pointer-sized atomic writes on x64/.NET) this is safe. For `decimal` (128-bit), `Guid` (128-bit), `Matrix4x4` (16 floats = 512-bit), or any custom struct, the CLR does **not** guarantee atomic reads or writes.

- **The Rev 2 note at line 2134 states:** "Prevents torn reads for large value types and missed notifications from concurrent writes." This claim is incorrect — only the writer is locked; the reader is unprotected.

- **Suggested fix**: Either:
  - **(A)** Acquire `_writeLock` in the getter too (adds lock contention on every read — potentially hot path).
  - **(B)** Use `Volatile.Read`/`Volatile.Write` for reference types + box large value types into a reference-typed wrapper (like `StrongBox<T>`), so reads are always pointer-atomic. The trade-off is one extra allocation per write for large `T`.
  - **(C)** Document that `Signal<T>` is only thread-safe for `T` where `sizeof(T) <= IntPtr.Size`, and add a Roslyn analyzer (COMET004) that warns on `Signal<decimal>`, `Signal<Guid>`, etc. when used from multiple threads. This is the cheapest option but shifts the burden to the developer.

---

#### 2. `Computed<T>.Evaluate()` reentrant dirtying during evaluation — old deps still subscribed

- **Location**: Proposal §4.2, lines 546–603 (Evaluate method)
- **Priority**: High
- **How to trigger**:
  1. `Computed C` depends on `Signal A` and `Signal B`.
  2. During `C.Evaluate()`, the `_compute()` lambda reads `A.Value` and `B.Value`.
  3. While `_compute()` is executing (between reading A and reading B), a background thread writes to `A.Value`.
  4. Because old deps are still subscribed (the diff-based fix), `A`'s `SubscriberList.NotifyAll` calls `C.OnDependencyChanged(A)`.
  5. `OnDependencyChanged` (line 612–622) checks `if (_dirty) return;` — but `_dirty` is in an indeterminate state during evaluation. It was presumably reset at line 595 (`_dirty = false`), but that line runs AFTER `_compute()` returns. So during evaluation, `_dirty` is whatever it was before `Evaluate()` was called. If this is the first evaluation, `_dirty` was `true` (set in constructor, line 515). The `if (_dirty) return;` guard fires, and the re-dirty notification is **silently swallowed**.

- **Impact**: The Computed misses the update from Signal A. Its cached value is stale until some other dependency triggers a re-evaluation. The user sees an outdated derived value.

- **The sequence in detail**:
  ```
  C._dirty = true  (initial state or from a previous notification)
  
  → C.Value is read → calls Evaluate()
    → _compute() begins executing
      → reads A.Value (A version = 5)
      → background thread: A.Value = newVal  (A version = 6)
        → A.NotifySubscribers()
          → C.OnDependencyChanged(A)
            → if (_dirty) return;   // _dirty is STILL TRUE (not yet set to false)
            → returns without re-marking dirty!
      → reads B.Value
    → _compute() returns
    → _dirty = false  (line 595)
    → _depVersions[A] = 5  (stale! A is now at version 6)
  ```

  Now C thinks it's clean, but its cached value was computed with A at version 5 while A is at version 6. No one will re-dirty C until A or B changes again.

- **Suggested fix**: Set `_dirty = false` BEFORE calling `_compute()`, so that `OnDependencyChanged` during evaluation can properly re-mark it dirty:
  ```csharp
  _dirty = false; // Clear before eval so re-notifications during eval can re-dirty
  var scope = ReactiveScope.BeginTracking();
  try { newValue = _compute(); }
  catch { _dirty = true; /* restore dirty on failure */ ... }
  ```
  Alternatively, use an `_evaluating` flag: during evaluation, buffer incoming dirty notifications and apply them after evaluation completes.

---

#### 3. `GetRenderViewReactive()` — `deps` variable is scoped inside `finally`, then used outside it

- **Location**: Proposal §4.6, lines 1115–1153 (GetRenderViewReactive method)
- **Priority**: High (compile error — this code won't compile as written)
- **How to trigger**: Attempt to compile the proposed code.

- **Evidence**:
  ```csharp
  // Line 1122-1132
  var scope = ReactiveScope.BeginTracking();
  View result;
  try
  {
      result = Body?.Invoke();
  }
  finally
  {
      var deps = scope.EndTracking();    // ← 'deps' declared INSIDE finally
      ReactiveScope.RestorePrevious(scope);
  }
  
  // Line 1136 — OUTSIDE the finally block
  if (deps.Count > 0)   // ← 'deps' is not in scope here!
  ```

- **Impact**: This is a straight-up compilation error. The `deps` variable is declared inside the `finally` block and referenced after it. Either this is pseudocode that was never compiled, or the actual intent was to declare `deps` outside the try/finally.

- **Suggested fix**:
  ```csharp
  HashSet<IReactiveSource> deps;
  try { result = Body?.Invoke(); }
  finally
  {
      deps = scope.EndTracking();
      ReactiveScope.RestorePrevious(scope);
  }
  if (deps.Count > 0) { ... }
  ```

---

#### 4. `GetRenderViewReactive()` creates an unused `_bodyEffect` and leaks `ViewDirtySubscriber` instances

- **Location**: Proposal §4.6, lines 1139–1151
- **Priority**: High
- **How to trigger**: Any view with signals read during Body build gets both a `_bodyEffect` (with `runImmediately: false`, never actually run) AND a set of `ViewDirtySubscriber` objects manually subscribed. On view rebuild (Reload), old `ViewDirtySubscriber` instances are never unsubscribed — only `_bodyEffect` is disposed.

- **Evidence trace**:
  ```csharp
  // Line 1139-1144 — creates an Effect that is never Run()
  _bodyEffect = new Effect(() =>
  {
      Body?.Invoke();  // This lambda is never executed (runImmediately: false)
  }, runImmediately: false);
  
  // Line 1147-1150 — manual subscription with ViewDirtySubscriber
  foreach (var dep in deps)
  {
      dep.Subscribe(new ViewDirtySubscriber(this));  // ← who unsubscribes these?
  }
  ```

  On line 1117-1118, the old `_bodyEffect` is disposed, and `_reactiveScope` is disposed. But the `ViewDirtySubscriber` instances were subscribed DIRECTLY to the signals via `dep.Subscribe(...)` — they are NOT owned by the effect or the scope. When the view rebuilds, old `ViewDirtySubscriber` instances remain subscribed as zombie listeners. They hold `WeakReference<View>` so they won't prevent GC, but they will accumulate in every signal's `SubscriberList` as dead entries, pruned only during `NotifyAll`.

  Furthermore, the `_bodyEffect` is created but never run (no `_dependencies` set), so its `Dispose()` (lines 736-745) unsubscribes from `_dependencies` which is `null` — it doesn't clean up the `ViewDirtySubscriber` subscriptions.

- **Impact**: Memory pressure from zombie subscribers accumulating in signal subscriber lists. Functionally correct (weak refs prevent leaks), but indicates a design confusion — two subscription mechanisms are set up (Effect + manual ViewDirtySubscriber) but only one cleanup path exists.

- **Suggested fix**: Choose one mechanism. Either use the Effect properly (run it immediately so it discovers its own deps and manages its own subscriptions), or use manual ViewDirtySubscribers and track them for disposal. Not both.

---

#### 5. `ReactiveScheduler.EnsureFlushScheduled()` — double-check locking without volatile `_flushScheduled`

- **Location**: Proposal §4.5, lines 959–981
- **Priority**: Medium
- **How to trigger**: Two threads call `EnsureFlushScheduled()` near-simultaneously. Thread A reads `_flushScheduled == false` (line 961, outside lock), enters the lock, sets it to `true`. Thread B's read at line 961 may see the stale `false` value due to CPU caching (no memory barrier on the non-volatile read), enters the lock, but is blocked. When B enters the lock, it re-checks (line 967) and sees `true` — this second check saves it. **However**, the initial check at line 961 is a plain `bool` read with no `volatile` or `Interlocked` semantics. On weakly-ordered architectures (ARM — which is Android and iOS!), the read at line 961 could be reordered with prior writes, causing Thread B to see stale `false` even after Thread A has set it to `true` and exited the lock.

- **Evidence**:
  ```csharp
  private static bool _flushScheduled;  // ← not volatile
  
  public static void EnsureFlushScheduled()
  {
      if (_flushScheduled)       // ← unsynchronized read
          return;
  
      lock (_lock)
      {
          if (_flushScheduled)   // ← synchronized read (lock provides barrier)
              return;
          _flushScheduled = true;
      }
      // ... Dispatch
  }
  ```

- **Impact on x86**: Likely benign (x86 has strong memory ordering). **On ARM (Android/iOS)**: Could result in a redundant `Dispatcher.Dispatch` call. This is not a correctness bug (double dispatch just means two flush attempts, the second finding nothing to do), but it's a performance waste and contradicts the "single flush per cycle" guarantee.

- **Suggested fix**: Either mark `_flushScheduled` as `volatile`, use `Volatile.Read()` for the outer check, or just remove the outer check and always take the lock (the critical section is trivially short).

---

#### 6. `Flush(depth)` silently drops pending updates at MaxFlushDepth — no user recovery path

- **Location**: Proposal §4.5, lines 1022–1036
- **Priority**: Medium
- **How to trigger**: Create a cycle: `Effect A` writes `Signal X` → `Effect B` (depends on X) writes `Signal Y` → `Effect A` (depends on Y) writes `Signal X` → ... After 100 iterations, `Flush` clears `_dirtyEffects` and `_dirtyViews` and returns.

- **Impact**: All pending dirty effects and views are silently discarded. The UI is now **silently stale** — views show old data with no indication that updates were dropped. A `Debug.WriteLine` fires, but:
  - (a) In release builds, `Debug.WriteLine` is compiled out (it's conditional on `DEBUG`).
  - (b) Even in debug builds, there's no user-facing error. The `ReactiveDiagnostics.FlushDepthWarning` event is declared (line 1522) but **never fired** in the `Flush` method. The proposal shows the event exists but the `Flush` code at lines 1024-1036 only calls `Debug.WriteLine`, not `ReactiveDiagnostics.NotifyFlushDepthWarning(depth)` or similar.
  - (c) After clearing dirty sets, the system appears to "recover" but the state is inconsistent — some effects ran with intermediate values and others were dropped.

- **Suggested fix**:
  1. Fire `ReactiveDiagnostics.FlushDepthWarning` inside the guard (it's declared but never called).
  2. Consider throwing in debug builds (`#if DEBUG throw new InvalidOperationException(...)`) — silent data loss is worse than a crash during development.
  3. Document the user-visible behavior: "If you see this diagnostic, your reactive graph has a cycle. The UI may show stale data until the next user interaction triggers a fresh flush."

---

#### 7. Hot reload `FieldInfo.SetValue` on `readonly` fields — broken on iOS NativeAOT and .NET 9+ trimming

- **Location**: Proposal §6.1, lines 1750–1789
- **Priority**: Medium
- **How to trigger**: Run the app on iOS with NativeAOT compilation (the default for .NET 9+ iOS release builds). The `FieldInfo.SetValue()` on a `readonly` field (line 1779) will throw `FieldAccessException` on CoreCLR AOT because the JIT optimizations that inline readonly field reads are baked into the native code at compile time. Even if `SetValue` succeeds at the reflection level, the JIT-compiled code may have already propagated the old value as a constant.

- **Cross-reference with actual code**: The existing `TransferHotReloadStateToCore` in `View.cs:1013-1023` only transfers dictionary-based `ChangedProperties` via `SetDeepPropertyValue` — it does NOT attempt to write readonly fields. The proposal's new reflection-based field copy (line 1779: `newField.SetValue(newView, signalRef)`) is a **new pattern** not validated against the existing codebase's approach.

- **Specific concerns**:
  1. **iOS AOT**: `FieldInfo.SetValue` on readonly fields may throw `FieldAccessException` under Mono AOT (Xamarin/MAUI iOS runtime). The .NET runtime team has explicitly warned against this.
  2. **Trimming**: `GetFields(BindingFlags.NonPublic | ...)` with `FieldType.Name == "Signal\`1"` is a trim-unsafe pattern. The trimmer may remove private fields it considers unreferenced, breaking the reflection query. No `[DynamicallyAccessedMembers]` annotation is shown.
  3. **NativeAOT on .NET 9+**: The readonly field optimization means the runtime may have already constant-folded the field value into callers. Writing the field via reflection changes the memory location, but compiled code that inlined the old value won't see the update.

- **Important context**: Hot reload is a **debug-time** feature, so NativeAOT and trimming are typically disabled. But the proposal doesn't state this constraint, and `TransferHotReloadStateToCore` is a `protected virtual` method that could be called in other scenarios.

- **Suggested fix**: Add an explicit constraint comment and a runtime guard:
  ```csharp
  // Hot reload is debug-only; NativeAOT and trimming are disabled during debug.
  // This reflection pattern is intentionally trim-unsafe.
  [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Hot reload is debug-only")]
  protected override void TransferHotReloadStateToCore(View newView) { ... }
  ```

---

#### 8. `SignalList<T>.ConsumePendingChanges()` — no ownership contract, unbounded queue growth

- **Location**: Proposal §4.9, lines 1328–1341
- **Priority**: Medium
- **How to trigger**: Use a `SignalList<T>` with rapid mutations (e.g., streaming data from a WebSocket adding items every 50ms). If no consumer calls `ConsumePendingChanges()` — because the view is offscreen, the ForEach renderer has a bug, or the list isn't bound to any UI — the `_pendingChanges` queue grows unbounded.

- **Evidence**:
  ```csharp
  private readonly Queue<ListChange<T>> _pendingChanges = new();
  
  // Only cleared when explicitly consumed:
  public IReadOnlyList<ListChange<T>> ConsumePendingChanges()
  {
      if (_pendingChanges.Count == 0)
          return Array.Empty<ListChange<T>>();
      var result = _pendingChanges.ToArray();
      _pendingChanges.Clear();
      return result;
  }
  ```

  The `Notify()` method (line 1411) enqueues changes, but nothing in the reactive pipeline (`ReactiveScheduler.Flush`, `Effect.Run`, etc.) calls `ConsumePendingChanges()`. The proposal states "Called by list renderers during the flush cycle" (line 1334), but no renderer code is shown, and the reactive pipeline has no hook for it.

- **Impact**: Memory leak proportional to mutation rate × time between flushes. For a chat app receiving 10 messages/second with a `SignalList<Message>`, after 1 minute offscreen, the queue has 600 `ListChange` structs. Not catastrophic, but it's an uncontrolled growth path with no cap.

- **Suggested fix**: Either:
  - Cap the queue (e.g., after 100 pending changes, replace with a single `Reset`).
  - Auto-drain during `NotifyAll` or when the SignalList is read.
  - Document the contract: "SignalList MUST be bound to a renderer that calls ConsumePendingChanges() during each flush cycle."

---

#### 9. `ReactiveDiagnostics` static events — classic .NET memory leak

- **Location**: Proposal §4.10, lines 1500–1548
- **Priority**: Medium
- **How to trigger**: Subscribe to `ReactiveDiagnostics.ViewRebuilt` or `SignalChanged` in a view's constructor or `OnMounted()` without unsubscribing in `Dispose()` / `OnWillUnmount()`. The static event holds a strong reference to the subscriber's delegate, which holds a strong reference to the view instance. The view is never collected.

- **Evidence**: The usage example at lines 1554–1560 shows subscribing with a lambda:
  ```csharp
  #if DEBUG
  ReactiveDiagnostics.IsEnabled = true;
  ReactiveDiagnostics.ViewRebuilt += e =>
      Debug.WriteLine($"[Reactive] {e.ViewType} rebuilt");
  #endif
  ```
  If this runs in a view-scoped context (e.g., `App.OnStart()` is fine, but a per-page diagnostic is not), each navigation to that page adds another subscriber that is never removed.

- **Cross-reference**: The existing Comet codebase already has this pattern with `AppThemeBinding.ThemeChanged` (static event at `Styles/AppThemeBinding.cs:15`) and `Theme.ThemeChanged` (at `Styles/Theme.cs:38`). So this is a **pre-existing pattern** in the codebase, not a new invention. But the proposal adds MORE static events without addressing the existing leak risk.

- **Suggested fix**: Use `WeakEventManager` or require an explicit `IDisposable` subscription token:
  ```csharp
  public static IDisposable OnViewRebuilt(Action<ViewRebuildEvent> handler)
  {
      ViewRebuilt += handler;
      return new Disposable(() => ViewRebuilt -= handler);
  }
  ```
  The `IsEnabled` guard (line 1505) prevents the event from firing when disabled, but subscribers are still rooted regardless of `IsEnabled`.

---

#### 10. Proposal's `Component<TState>.SetState` diverges from actual implementation

- **Location**: Proposal §4.7, lines 1198–1228 vs actual `src/Comet/Component.cs:102-122`
- **Priority**: Low
- **How to trigger**: N/A — this is a documentation/proposal accuracy issue, not a runtime bug.

- **Evidence**: The proposal says:
  ```csharp
  // Proposal line 1210-1219
  protected void SetState(Action<TState> mutator)
  {
      var state = State;
      mutator(state);
      ReactiveScheduler.MarkViewDirty(this);  // ← uses reactive scheduler
  }
  ```

  The actual implementation at `Component.cs:102-122`:
  ```csharp
  protected void SetState(Action<TState> mutator)
  {
      var state = State;
      StateManager.BeginBatch();
      try { mutator(state); }
      finally { StateManager.EndBatch(); }
      ThreadHelper.RunOnMainThread(() => Reload());  // ← uses old batch + reload
  }
  ```

  The proposal claims `SetState` uses `ReactiveScheduler.MarkViewDirty(this)`, but the actual code uses `StateManager.BeginBatch/EndBatch` + `ThreadHelper.RunOnMainThread(() => Reload())`. During migration Phase 1, both systems coexist, so it's important to document which path `SetState` takes — the old path or the new path. As written, the proposal implies it's already on the new path, but the actual code is on the old path.

- **Suggested fix**: Clarify that the proposal shows the Phase 3 target. During Phase 1, `SetState` continues to use the existing `StateManager.BeginBatch/EndBatch` pattern. Add a code comment noting the transition.

---

### Edge Cases Not Handled

#### E1. `Computed<T>.Evaluate()` exception handling leaves stale dep subscriptions

- **What happens**: When `_compute()` throws (line 560-567), the method discards partial reads and returns without updating `_dependencies`. But old deps remain subscribed (the diff-based approach keeps them alive). If the exception is transient and the Computed is re-evaluated successfully later, the new evaluation diffs against the old `_dependencies` — which is correct. **However**, if the exception persists permanently (e.g., a coding error), the Computed stays dirty forever, its old subscriptions are never cleaned up, and every notification from old deps triggers a re-evaluation attempt that throws again — potentially filling logs and wasting CPU.
- **Should happen**: After N consecutive failures, the Computed should unsubscribe from all deps and enter a "poisoned" state to prevent infinite retry loops.

#### E2. `ReactiveScope.BeginTracking()` / `RestorePrevious()` can desync on exception in caller

- **What happens**: `GetRenderViewReactive()` and `Computed.Evaluate()` both use the pattern `BeginTracking()` → work → `EndTracking()` → `RestorePrevious()`. If code between `BeginTracking` and `RestorePrevious` throws and the exception is caught by an outer handler that doesn't call `RestorePrevious`, the `[ThreadStatic] _current` scope is permanently stuck on the inner scope. All subsequent signal reads on that thread register dependencies in the wrong scope.
- **Should happen**: `ReactiveScope` should implement `IDisposable` with a `using` pattern that always restores the previous scope. The proposal shows `Dispose()` at line 832-836 that does this, but neither `Evaluate()` nor `GetRenderViewReactive()` uses `using var scope = ...` — they call `BeginTracking()` and `RestorePrevious()` manually, leaving a gap for exceptions.

#### E3. `Signal<T>.Dispose()` clears subscribers but doesn't prevent future subscriptions

- **What happens**: After `Signal.Dispose()` (line 464-467), `_subscribers.Clear()` is called. But nothing prevents new `Subscribe()` calls from adding to the now-cleared list. A Computed that still references the disposed Signal will re-subscribe on its next evaluation, creating a zombie subscription to a dead signal.
- **Should happen**: Add a `_disposed` flag. `Subscribe()` should be a no-op (or throw) after disposal.

---

### Suspicious Patterns

#### S1. `SubscriberList.NotifyAll` snapshot allocation on every notification

- **Location**: Proposal §4.4, lines 886-917
- **Concern**: Every signal change allocates from `ArrayPool` and iterates through weak references. For a form with 30 fields where a "Reset" button clears all 30, that's 30 separate `ArrayPool.Rent` → iterate → `Return` cycles within a single synchronous frame (before the flush). The individual notifications just mark things dirty (cheap), but the ArrayPool overhead per-signal-write may matter for bulk operations.

  The existing Comet code uses `ArrayPool` similarly in `StateManager.OnPropertyChanged` (`StateManager.cs:445`), so this is not worse than today. But the proposal claims "minimal GC pressure" — the `WeakReference<T>` array copies are not zero-cost.

#### S2. Proposal's `ReactiveScheduler` uses `Application.Current?.Dispatcher` — null in unit tests

- **Location**: Proposal §4.5, lines 973-981
- **Concern**: The fallback `ThreadHelper.RunOnMainThread(FlushEntry)` uses the Comet `ThreadHelper` which delegates to `MainThread.BeginInvokeOnMainThread`. In unit test environments without a MAUI host, `MainThread.BeginInvokeOnMainThread` throws. The existing codebase has this same pattern (`ThreadHelper` is used everywhere — `View.cs:552,565,577`, `Component.cs:121,180`), so it's not a new problem, but the proposal doesn't address test-host setup requirements.

#### S3. `EnsureFlushScheduled` called inside `_writeLock` in the setter, then again outside

- **Location**: Proposal §4.1, lines 431-437
- **Concern**: The comment says "Schedule flush outside the lock — avoids holding _writeLock while potentially acquiring ReactiveScheduler._lock." This is correct lock ordering. But `_subscribers.NotifyAll(this)` (line 431, inside `_writeLock`) calls `OnDependencyChanged` on each subscriber. For `Computed<T>`, `OnDependencyChanged` (line 620-621) calls `_subscribers.NotifyAll(this)` which acquires the Computed's `SubscriberList._lock`, and then calls `ReactiveScheduler.EnsureFlushScheduled()` which acquires `ReactiveScheduler._lock`. This means the lock ordering is: `Signal._writeLock` → `SubscriberList._lock` (on subscriber) → `ReactiveScheduler._lock`. If any code path acquires these locks in a different order, deadlock. The proposal doesn't document the required lock ordering.

---

### Could Not Break

1. **SubscriberList weak reference pruning**: The compact-during-NotifyAll pattern (lines 897-906) correctly prunes GC'd entries. Tried to construct a scenario where pruning races with Add/Remove — the `_lock` serializes all mutations. Appears robust.

2. **ReactiveScope thread-locality**: The `[ThreadStatic]` pattern for `_current` correctly isolates scopes per-thread. A background thread calling `Signal.Value` with no active scope harmlessly skips tracking (line 417: `ReactiveScope.Current?.TrackRead(this)` — null-conditional does nothing). No cross-thread scope pollution possible.

3. **Computed lazy evaluation**: The "mark dirty, evaluate on read" pattern correctly avoids redundant computation. A Computed that nobody reads stays dirty indefinitely — no wasted work. Only read triggers evaluation. This is a well-proven pattern (SolidJS, MobX).

4. **Signal equality check**: The `_comparer.Equals(_value, value)` check (line 424) correctly uses the injected `EqualityComparer<T>`, preventing notification storms when setting the same value repeatedly. The `unchecked { _version++; }` only fires after the equality check passes. Sound.

5. **Microtask coalescing pattern**: The `_flushScheduled` flag + `Dispatcher.Dispatch` correctly coalesces multiple signal writes into a single flush. Modeled after MauiReactor's `_layoutCallEnqueued` and Comet's own `ThreadHelper.RunOnMainThread` patterns. The double-check locking weakness (Finding #5 above) is a minor ARM concern, not a correctness issue.

---

## Summary

| Priority | Count | Key Issues |
|----------|-------|------------|
| Critical | 1     | Torn reads on large value types (getter outside lock) |
| High     | 3     | Reentrant dirtying during Compute eval; GetRenderViewReactive won't compile; ViewDirtySubscriber leak |
| Medium   | 4     | ARM memory ordering; silent update drop at MaxFlushDepth; iOS AOT readonly fields; unbounded SignalList queue |
| Low      | 1     | Proposal/actual code divergence for SetState |

The Rev 2 fixes addressed the first-pass findings, but the `_writeLock` fix (Critical #1 from Rev 1) was applied asymmetrically — only the writer is locked, not the reader. This creates a new class of bug (torn reads) that didn't exist before the "fix." The Computed reentrant dirtying (High #2) is a subtle interaction between the diff-based subscription fix and the `_dirty` flag lifecycle. Both warrant attention before implementation begins.
