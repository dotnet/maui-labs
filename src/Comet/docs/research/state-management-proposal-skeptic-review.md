# Skeptic Review: Comet State Management v2 Proposal

> **Reviewer:** Skeptic (adversarial review)  
> **Target:** `docs/state-management-proposal.md`  
> **Method:** Line-by-line analysis against current Comet source, concurrency modeling, API surface attack  
> **Verdict:** The direction is sound. The implementation has 3 critical bugs, 4 high-severity design flaws, and several medium-risk gaps that will bite during real-world adoption.

---

## Bugs Found

### Bug 1: `Signal<T>.Value` setter is NOT thread-safe for reference types or large value types

- **Location:** `state-management-proposal.md:412-423`, §4.1
- **Priority:** Critical
- **The claim (line 312-313):**
  > "The signal value itself can be set from any thread (the field write is atomic for reference types and small value types)"

- **How to trigger:** Two threads simultaneously writing to `Signal<MyStruct>` where `MyStruct` is larger than 8 bytes (i.e., not atomically writable). Or even for reference types: Thread A writes `_value = newVal` at line 417, Thread B reads `_value` at line 409 between the write and `NotifySubscribers()` at line 422 — Thread B gets the new value but no notification is ever sent to its subscribers because the notification fires only from Thread A's write.

- **Impact:** Torn reads for large value types. Missed notifications when concurrent writes race — the equality check on line 414 could see value-A, then Thread B writes value-B, then Thread A's `NotifySubscribers` fires for value-B which subscribers already partially see, then Thread B's equality check sees value-B (unchanged from its own write) and skips notification entirely. Result: **stale UI**.

- **Suggested fix:** Either:
  - (a) Use a lock around the write + notify (simple, correct, tiny contention since the critical section is microseconds), or
  - (b) Always dispatch the entire setter body to the UI thread (like MauiReactor does), eliminating the concurrent write problem entirely. The proposal claims to want "thread-safe by default" — option (b) delivers that.

---

### Bug 2: `ReactiveScheduler.Flush()` has unbounded recursion

- **Location:** `state-management-proposal.md:978-984`, §4.5
- **Priority:** Critical
- **The code:**
  ```csharp
  if (hasMore)
      Flush(); // recursive — bounded by DAG depth
  ```
- **The claim:** "bounded by DAG depth"

- **How to trigger:** Create a cycle: `Effect A` reads `Signal X`, writes `Signal Y`. `Effect B` reads `Signal Y`, writes `Signal X`. On any mutation, Flush runs Effect A → dirties Y → schedules B. Phase 2 runs. Next check: B dirtied X → schedules A. Recurse. **Stack overflow.**

  Even without explicit cycles, diamond dependency patterns with Effects that write signals can create unbounded cascading:
  ```
  Signal A → Effect 1 → writes Signal B
  Signal A → Effect 2 → writes Signal C  
  Signal B → Effect 3 → writes Signal D
  Signal C → Effect 3 → writes Signal D (duplicate, but D is written twice)
  Signal D → Effect 4 → writes Signal A  ← cycle
  ```

- **Impact:** `StackOverflowException` crashes the app. No recovery possible.

- **Suggested fix:** Add a recursion depth counter. After N iterations (e.g., 10), log a diagnostic warning and break. SolidJS does exactly this — it caps at 100 iterations and throws a readable error: "Reactive graph exceeded maximum depth."

---

### Bug 3: `SubscriberList` is a mutable struct with a `readonly` field pattern mismatch

- **Location:** `state-management-proposal.md:793-876`, §4.4
- **Priority:** Critical
- **The code:**
  ```csharp
  internal struct SubscriberList
  {
      private WeakReference<IReactiveSubscriber>[]? _items;
      private int _count;
      private readonly object _lock;
  ```

- **How to trigger:** `SubscriberList` is a `struct`. When used as a field in `Signal<T>`, `Computed<T>`, and `Effect`, every call like `_subscribers.NotifyAll()` operates on a **copy** unless `_subscribers` is explicitly passed by reference. In C#, calling a method on a struct field does mutate the field in-place — but if the struct is ever boxed, copied, or passed by value, the `_lock` is `readonly` which means it's initialized in the parameterless constructor... except **struct parameterless constructors are not guaranteed to run in all contexts** (e.g., `default(SubscriberList)` skips the constructor, leaving `_lock` as `null`).

  Even if the constructor runs, the `readonly` modifier on `_lock` means it's set once — but the struct has mutable fields `_items` and `_count`. If `Signal<T>` is ever used in a context where the struct gets copied (e.g., passed to a method by value, stored in a `readonly` field where the compiler makes defensive copies), modifications to `_items` and `_count` are lost.

- **Impact:** `NullReferenceException` on `lock (_lock)` when `_lock` is null. Silent data loss when struct copies discard mutations. These are insidious bugs that surface only under specific conditions.

- **Suggested fix:** Make `SubscriberList` a `class`, not a `struct`. The proposal's rationale for struct (performance) is undermined by the `lock` object allocation anyway — each instance already allocates a heap object. Making it a class eliminates the defensive-copy and `default(T)` trap entirely.

---

## High-Severity Design Flaws

### Flaw 1: `Computed<T>.Evaluate()` unsubscribes from ALL deps before re-evaluating

- **Location:** `state-management-proposal.md:536-574`, §4.2
- **Priority:** High
- **The code (lines 538-542):**
  ```csharp
  if (_dependencies != null)
  {
      foreach (var dep in _dependencies)
          dep.Unsubscribe(this);
  }
  ```

- **The problem:** If `Evaluate()` is called from the `Flush()` path (because the Computed was dirty), and during `_compute()` execution (line 549) a signal that this Computed *should* depend on fires a notification, the Computed has already unsubscribed from it. The notification is lost. The Computed finishes evaluation, re-subscribes, but now has a stale value for that signal.

  This is a real-world scenario: imagine `Computed<string>` that reads `Signal<bool> isLoggedIn` and `Signal<string> userName`. During evaluation, `isLoggedIn` changes (e.g., from a concurrent auth callback dispatched to UI thread). The Computed has unsubscribed from `isLoggedIn` before reading it. The change notification goes nowhere. After evaluation, it re-subscribes — but the version check won't catch this because the version was already incremented before the re-subscription.

- **Suggested fix:** Don't unsubscribe before evaluation. Instead, collect new deps during evaluation, then diff old vs new after, unsubscribing only from removed deps and subscribing to added deps. SolidJS and Preact Signals both use this approach.

---

### Flaw 2: Coalescing breaks synchronous expectations in event handlers

- **Location:** `state-management-proposal.md:296-305`, §3.C and §4.5
- **Priority:** High
- **The scenario:**
  ```csharp
  void OnButtonClick()
  {
      count.Value = 42;
      // Developer expects the UI to reflect 42 here
      // But the flush hasn't run yet — it's a Dispatcher.Dispatch() microtask
      
      // Now they read a computed that depends on count:
      var label = greeting.Peek();  // Returns stale value if greeting is dirty but not yet evaluated
  }
  ```

- **Impact:** `Computed<T>.Peek()` calls `Evaluate()` if dirty (line 528-529), so it *will* return the correct value — BUT only because `Peek()` forces evaluation. However, `Computed<T>.Value` also forces evaluation when dirty (line 515-516). So the real concern is: **can a developer observe stale native UI?**

  Yes: after `count.Value = 42`, the native `Text` control still shows the old value until `Flush()` runs. If the event handler does something that depends on the visual state being updated (e.g., measuring a control, triggering an animation relative to current position), they'll get wrong results.

  MauiReactor has the same design (dispatch-coalesced). But MauiReactor makes this explicit with `invalidateComponent: false`. The proposal doesn't provide a `FlushNow()` escape hatch for event handlers that need synchronous visual updates.

- **Suggested fix:** Provide `ReactiveScheduler.FlushSync()` as a public API (it exists at line 990 but is documented "for unit tests"). Make it a first-class citizen for event handlers that need immediate visual updates. Document the async nature of coalescing prominently.

---

### Flaw 3: `NotifyAll()` passes `null!` as the source parameter

- **Location:** `state-management-proposal.md:860`
- **Priority:** High
- **The code:**
  ```csharp
  snapshot[i].OnDependencyChanged(null!); // source is available if needed
  ```

- **The problem:** Every `IReactiveSubscriber.OnDependencyChanged(IReactiveSource source)` receives `null` as the source. This means:
  1. `Computed<T>.OnDependencyChanged` can never check *which* dependency changed — it must assume all deps are potentially dirty.
  2. `Effect.OnDependencyChanged` can never log which signal triggered the effect.
  3. The comment "source is available if needed" is a lie — it's `null`.
  4. Any future subscriber that dereferences `source` will get a `NullReferenceException`.

- **Suggested fix:** Pass the actual `IReactiveSource` that triggered the notification. `NotifyAll()` is called from `Signal<T>.NotifySubscribers()` and `Computed<T>.Evaluate()` — both know `this`. Add a parameter: `NotifyAll(IReactiveSource source)` and thread it through.

---

### Flaw 4: Hot reload state transfer doesn't actually work for Signals

- **Location:** `state-management-proposal.md:1527-1543`, §6.1
- **Priority:** High
- **The claim (line 1531):**
  > "The existing hot reload pipeline (TransferState / TransferHotReloadStateToCore) transfers the entire view's field state to the new instance."

- **Reality check against the actual source** (`View.cs:1020-1030`):
  ```csharp
  protected virtual void TransferHotReloadStateToCore(View newView)
  {
      var oldState = this.GetState();
      if (oldState == null) return;
      var changes = oldState.ChangedProperties;
      foreach (var change in changes)
          newView.SetDeepPropertyValue(change.Key, change.Value);
  }
  ```

  This transfers properties from `BindingState.changeDictionary` — which is populated by `BindingState.UpdateValue()`. **Signals don't go through `BindingState.UpdateValue()`**. They use the new reactive pipeline. So `changeDictionary` is empty for Signal-based views. `TransferHotReloadStateToCore` has nothing to transfer.

  The proposal doesn't modify `TransferHotReloadStateToCore` to handle signals. The claim "No special handling needed" (line 1542) is wrong — it needs explicit signal field transfer.

  During Phase 1 (migration), Signals bridge to `INotifyPropertyRead` and go through both systems. But in Phase 3 when `StateManager` is removed, the `BindingState.changeDictionary` mechanism is gone, and there's no replacement for hot reload state transfer.

- **Suggested fix:** Either:
  - (a) Override `TransferHotReloadStateToCore` to reflect over `Signal<T>` fields and copy their `_value` to the new view's corresponding fields, or
  - (b) Make signals register their current values in a transfer dictionary when they change, or
  - (c) The hot reload system needs to be signal-aware: when creating the replacement view, copy signal field references from old to new (not just values, but the actual `Signal<T>` objects, preserving subscribers).

  Option (c) is what the proposal *describes* ("receives the same signal objects") but doesn't *implement*. `.NET hot reload replaces the Type`, so the new view is a new object with new fields. `readonly` fields can't be reassigned. The transfer mechanism needs to handle this — likely via reflection similar to MauiReactor's `TypeLoader.Instance.CopyProperties()`.

---

## Edge Cases Not Handled

### `Computed<T>` evaluated during `Flush()` can dirty MORE views

- **What happens:** Phase 1 flushes effects. An effect forces a `Computed.Value` read. The Computed re-evaluates, produces a new value, and calls `_subscribers.NotifyAll()` + `ReactiveScheduler.EnsureFlushScheduled()`. But `_flushScheduled` is `false` now (it was reset at line 953). So a NEW dispatch is posted. Meanwhile, Phase 2 runs with potentially stale dirty-view sets.
- **Should happen:** The recursive flush check at line 978-984 should catch this, but only if the newly-dirtied items were added synchronously during the flush. If `EnsureFlushScheduled()` posts a new `Dispatcher.Dispatch()`, it creates a *separate* flush cycle, not a recursive one. Now you have two flushes racing.

### `ReactiveScope` stacking can leak if `EndTracking` is never called

- **What happens:** If `_compute()` in `Computed<T>.Evaluate()` throws an exception, `EndTracking()` is called in the `finally` block (line 553). Good. But `RestorePrevious` is also in the `finally` (line 554). If `BeginTracking()` pushes scope A, then *another* `Computed.Value` is read during evaluation (triggering a nested `BeginTracking()` pushing scope B), and scope B's compute throws — scope B's finally restores scope A. But scope A's deps now include reads that happened during scope B's partial evaluation. Those are spurious dependencies.
- **Should happen:** On exception, discard the partial reads from the current scope entirely. The deps should be whatever they were before, and the Computed should remain dirty.

### `Signal<T>(T initialValue)` implicit conversion creates ambiguity

- **Location:** `state-management-proposal.md:464`
- **The code:** `public static implicit operator Signal<T>(T value) => new Signal<T>(value);`
- **The problem:** With this implicit conversion, `Signal<int> count = 0;` works (nice), but it also means `MethodTakingSignal(42)` silently creates a new unowned Signal. If someone writes:
  ```csharp
  new Text(myString)  // is this Text(string staticValue) or Text(Signal<string>)?
  ```
  The compiler will prefer `Text(string)` over `Text(Signal<string>)` for a `string` argument (exact match wins over implicit conversion). But for `Text(mySignal)` where `mySignal` is `Signal<string>`, the compiler correctly picks `Text(Signal<string>)`. So the ambiguity is limited — but `Text(count)` where `count` is `Signal<int>` and `Text` accepts `int` could be surprising.

### `SignalList<T>.LastChange` is set then immediately nulled

- **Location:** `state-management-proposal.md:1296-1303`
- **The code:**
  ```csharp
  private void Notify(ListChange<T> change)
  {
      LastChange = change;
      _subscribers.NotifyAll();
      LastChange = null;  // ← cleared BEFORE flush runs
  }
  ```
- **What happens:** By the time `ReactiveScheduler.Flush()` runs (in a future dispatch), `LastChange` is already `null`. Any subscriber that wanted to use `LastChange` for incremental updates can't — it's gone. The incremental list optimization that `SignalList<T>` is supposed to provide is broken.
- **Should happen:** `LastChange` should persist until after the flush, or be queued and delivered to subscribers during notification rather than via a shared mutable field.

---

## Suspicious Patterns

### `ReactiveScope.Current` is `[ThreadStatic]` — but `Dispatcher.Dispatch(Flush)` runs on UI thread

- **Location:** `state-management-proposal.md:734`
- **Concern:** `BeginTracking()` sets `_current` on the calling thread. If a `Computed<T>.Value` getter is called from a background thread (e.g., inside a `Task.Run`), it will track reads in that thread's scope — which is likely `null` (no scope active). So the read is untracked. But the proposal says Signal reads register deps "if we're inside a tracking context" (line 409). So background-thread reads of Signal values are silently untracked. This is probably correct behavior (you shouldn't be building reactive graphs on background threads), but it's undocumented and could surprise developers who read `Computed<T>.Value` from a background task.

### The proposal keeps two separate dirty-tracking mechanisms

- **Location:** `state-management-proposal.md:891-893`
- **Concern:** `_dirtyEffects` (a List) and `_dirtyViews` (a HashSet) serve similar purposes but use different data structures. Effects can be dirtied multiple times between flushes — the List allows duplicates, meaning `Effect.Flush()` could run the same effect twice. The views HashSet deduplicates. This inconsistency suggests either effects need dedup (use a HashSet) or the proposal hasn't considered duplicate dirty-marking of effects.

### No mechanism to debug "why did my view rebuild?"

- **Location:** Design goal 5 (line 44) promises debuggability
- **Concern:** The proposal lists debuggability as a goal but provides no concrete diagnostic infrastructure. No `Signal.Name` property for debugging. No reactive graph dump. No "this view rebuilt because Signal X changed" logging. MauiReactor doesn't have this either, but the proposal specifically promises it and doesn't deliver. The closest thing is the COMET003 analyzer warning, which is a static analysis tool, not a runtime diagnostic.

---

## Could Not Break

**The core reactive graph model is sound.** The Signal → Computed → Effect DAG with version-based dirty checking is a well-proven pattern (SolidJS, Preact Signals, Angular Signals). The fundamental architecture will work.

**The migration path (Phase 1-3) is well-designed.** The `INotifyPropertyRead` bridge in Phase 1 Signal is clever — it lets old and new systems coexist without forking the codebase. The incremental deprecation through Phase 2 and removal in Phase 3 is realistic.

**The `Computed<T>` lazy evaluation model is correct for the read path.** Dirty marking + lazy re-evaluation on access is the right choice for Comet where Body evaluation pulls values.

**The API surface is genuinely better than the current system.** Eliminating `implicit operator Binding<T>(T value)` removes the #1 source of state bugs. The `Signal<T>` / `Func<T>` / static-value overload pattern is clear and hard to misuse.

**`SignalList<T>` with `ListChange<T>` is a good design** (modulo the `LastChange` timing bug above). Fine-grained list mutations are a real need that the current system completely lacks.

---

## Summary

| Priority | Count | Issues |
|----------|-------|--------|
| **Critical** | 3 | Thread-unsafe Signal setter, unbounded Flush recursion, mutable struct SubscriberList |
| **High** | 4 | Computed unsubscribes before eval, coalescing breaks sync expectations, NotifyAll passes null source, hot reload doesn't transfer Signals |
| **Medium** | 5 | SignalList.LastChange timing, scope leak on exceptions, duplicate dirty effects, missing debuggability, implicit Signal conversion ambiguity |

**Bottom line:** This proposal correctly identifies every major flaw in the current system and proposes the right architectural direction. But the implementation details have real bugs that would ship if not caught. The three critical issues (thread safety, recursion, struct semantics) would each independently cause production crashes. Fix those, address the hot reload gap, and this becomes a strong design.
