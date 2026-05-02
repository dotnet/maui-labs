# ADR: Dual Reactive State Tracking Systems

| Field | Value |
|-------|-------|
| **Status** | Accepted |
| **Date** | 2025-07-18 |
| **Author** | Holden (Lead Architect) |
| **Applies to** | `src/Comet/` — reactive state management layer |

## Context

Comet's MVU architecture requires two fundamentally different kinds of reactivity:

1. **Fine-grained property updates** — When a user drags a Slider, only the Slider's `Value` property should be re-evaluated and pushed to the native handler. Rebuilding the entire view tree for every pixel of a drag gesture would be catastrophically expensive and would cause focus loss on interactive controls like Entry and SearchBar.

2. **Coarse-grained view rebuilds** — When a navigation state variable like `selectedIndex` changes, the entire `Body()` must re-execute so that a completely different page can be returned. No amount of property-level diffing can handle "the body now returns a TextField instead of a Slider."

These two needs are served by two coexisting tracking systems that evolved at different times in the project's history.

### System 1: StateManager / Binding (Original)

The original tracking system, built around `INotifyPropertyRead` / `INotifyPropertyChanged` events and the static `StateManager` class.

**How it works:**

```
State<T>.Value (get)
  → CallPropertyRead("Value")
    → StateManager.OnPropertyRead(this, "Value")
      → records (BindingObject, PropertyName) in thread-local list

State<T>.Value (set)
  → CallPropertyChanged("Value", newValue)
    → StateManager.OnPropertyChanged(this, "Value", value)
      → looks up NotifyToViewMappings[this] → {view1, view2, ...}
        → view.BindingPropertyChanged(...)
          → BindingState.UpdateValue(...)
            → finds Binding<T> for that property
              → Binding<T>.EvaluateAndNotify()
                → re-invokes Get Func, computes new value
                  → View.ViewPropertyChanged(property, newValue)
                    → handler property mapper updates native control
```

**Key types:**
- `BindingObject` (`BindingObject.cs:22`) — Base class with `CallPropertyRead/Changed`
- `State<T>` (`State.cs:12`) — Typed state wrapper extending `BindingObject`
- `StateManager` (`StateManager.cs:16`) — Static tracking hub with `NotifyToViewMappings`, `ViewObjectMappings`, thread-local property read lists
- `Binding<T>` (`Binding.cs:52`) — Wraps a `Func<T>` or value; re-evaluates on change
- `BindingState` (`BindingObject.cs:111`) — Per-view state holding `GlobalProperties` and `ViewUpdateProperties`

**Granularity:** Per-property. A Slider value change dispatches only to the `Binding<double>` registered for that Slider's `Value` property via the handler's property mapper.

### System 2: ReactiveScope / BodyDependencySubscriber (New)

The newer system, built for the `Reactive<T>` / `Signal<T>` / `Component` pattern, using explicit subscription-based tracking.

**How it works:**

```
View.GetRenderViewReactive()
  → ReactiveScope.BeginTracking()           // push new scope
    → Body.Invoke()                          // execute body lambda
      → someReactive.Value (get)
        → ReactiveScope.Current?.TrackRead(this)  // record dependency
    → scope.EndTracking()                    // collect all reads
  → diff old vs new dependencies
    → Subscribe/Unsubscribe BodyDependencySubscriber

Reactive<T>.Value (set)
  → base.Value = value                       // fires StateManager path too
  → _subscribers.NotifyAll(this)
    → BodyDependencySubscriber.OnDependencyChanged()
      → ReactiveScheduler.MarkViewDirty(view)
        → dispatches to UI thread
          → view.Reload()                    // full body rebuild
```

**Key types:**
- `ReactiveScope` (`Reactive/ReactiveScope.cs:10`) — Thread-static tracking context with `BeginTracking()` / `EndTracking()`
- `IReactiveSource` (`Reactive/IReactiveSource.cs:3`) — Subscribe/Unsubscribe + Version
- `IReactiveSubscriber` (`Reactive/IReactiveSubscriber.cs:3`) — `OnDependencyChanged` callback
- `Reactive<T>` (`Reactive.cs:18`) — Extends `State<T>`, implements `IReactiveSource`
- `BodyDependencySubscriber` (`Controls/View.cs:444`) — Sealed inner class of View; calls `ReactiveScheduler.MarkViewDirty()`
- `ReactiveScheduler` (`Reactive/ReactiveScheduler.cs:10`) — Batches dirty views/effects, flushes on UI thread

**Granularity:** Per-view. Any tracked `Reactive<T>` change triggers a full `Body()` rebuild of the subscribing view.

## Decision

### The Suppress/Resume Boundary

The bridge between the two systems is `ReactiveScope.Suppress()` / `ReactiveScope.Resume()`, invoked at the exact boundary where fine-grained tracking must take precedence over coarse-grained tracking.

**Location:** `Binding.cs:102-117` (ProcessGetFunc) and `Binding.cs:121-141` (State<T> implicit operator)

```csharp
// Binding.cs — ProcessGetFunc (lines 102-117)
protected void ProcessGetFunc()
{
    StateManager.StartProperty();
    var scope = ReactiveScope.Suppress();   // ← hide ReactiveScope
    var result = Get == null ? default : Get.Invoke();
    ReactiveScope.Resume(scope);            // ← restore ReactiveScope
    var props = StateManager.EndProperty();
    // ... Binding now owns the dependency tracking
}
```

**Why this boundary exists:**

When `Body()` executes, a `ReactiveScope` is active (see `View.GetRenderViewReactive()`, line 407). Inside the body, the developer writes:

```csharp
new Slider(value: () => progress.Value, ...)
```

The `Func<double>` lambda is captured and later evaluated by `Binding<T>.ProcessGetFunc()`. During that evaluation, `progress.Value` is read. Without suppression, **both** systems would track the read:

1. `ReactiveScope.Current.TrackRead(progress)` — registers body-level dependency
2. `StateManager.OnPropertyRead(progress, "Value")` — registers Binding-level dependency

This **double-tracking** means that dragging the Slider would:
- Update the Binding (correct, fine-grained) ✅
- **Also** trigger a full body rebuild via ReactiveScheduler ❌

The full rebuild recreates all views, which destroys focus state on Entry controls and resets Slider drag tracking — the exact bugs that motivated this design.

By calling `Suppress()` before evaluating the Func, the `ReactiveScope.Current` is temporarily set to `null`, so `Reactive<T>.Value`'s getter sees no scope and skips reactive tracking. Only `StateManager` captures the read. When `Resume()` restores the scope, body-level tracking continues for subsequent reads outside of Binding lambdas.

### The Bridge Type: `Reactive<T>`

`Reactive<T>` (`Reactive.cs:18`) extends `State<T>` and implements `IReactiveSource`, making it visible to both systems simultaneously:

```
                    ┌─────────────────┐
                    │  BindingObject   │  ← PropertyRead / PropertyChanged events
                    └────────┬────────┘
                             │ extends
                    ┌────────┴────────┐
                    │    State<T>      │  ← CallPropertyRead → StateManager
                    └────────┬────────┘
                             │ extends
                    ┌────────┴────────┐
                    │  Reactive<T>     │  ← IReactiveSource (Subscribe/Unsubscribe)
                    └─────────────────┘
                             │
              ┌──────────────┼──────────────┐
              ▼                              ▼
    StateManager path                ReactiveScope path
    (fine-grained)                   (coarse-grained)
```

On **read**: `Reactive<T>.Value` getter calls both `ReactiveScope.Current?.TrackRead(this)` (line 36) **and** `base.Value` which calls `CallPropertyRead` (State.cs:34). When inside a Binding Func, Suppress/Resume ensures only the StateManager path fires.

On **write**: `Reactive<T>.Value` setter calls `base.Value = value` (fires StateManager/PropertyChanged path) **and** `_subscribers.NotifyAll(this)` (fires ReactiveScheduler path). Both always fire on write — the deduplication happens at the tracking level, not the notification level.

## Data Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        BODY EXECUTION                                   │
│                                                                         │
│  View.GetRenderViewReactive()                                           │
│    │                                                                    │
│    ├─ ReactiveScope.BeginTracking()         ← open coarse scope         │
│    │                                                                    │
│    ├─ Body.Invoke() ─────────────────────────────────────────┐          │
│    │                                                         │          │
│    │   ┌──────────────────────────┐   ┌────────────────────┐ │          │
│    │   │ Direct reactive reads    │   │ Binding<T> Func    │ │          │
│    │   │ e.g. if (flag.Value)     │   │ evaluation         │ │          │
│    │   │                          │   │                    │ │          │
│    │   │ ReactiveScope tracks ✅  │   │ Suppress() ──┐    │ │          │
│    │   │ StateManager tracks ✅   │   │ Get.Invoke() │    │ │          │
│    │   │                          │   │ Resume()  ───┘    │ │          │
│    │   │ → body-level dependency  │   │                    │ │          │
│    │   └──────────────────────────┘   │ ReactiveScope: ❌  │ │          │
│    │                                  │ StateManager:  ✅  │ │          │
│    │                                  │                    │ │          │
│    │                                  │ → per-property     │ │          │
│    │                                  │   Binding only     │ │          │
│    │                                  └────────────────────┘ │          │
│    │                                                         │          │
│    ├─ scope.EndTracking()               ← collect deps      │          │
│    ├─ diff old/new dependencies                              │          │
│    └─ Subscribe/Unsubscribe BodyDependencySubscriber         │          │
│                                                              │          │
└──────────────────────────────────────────────────────────────┘──────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│                     STATE CHANGE PROPAGATION                            │
│                                                                         │
│  reactive.Value = newVal                                                │
│    │                                                                    │
│    ├─── StateManager path (System 1) ──────────────────────────────┐    │
│    │    base.Value = value                                         │    │
│    │      → CallPropertyChanged("Value", value)                    │    │
│    │        → StateManager.OnPropertyChanged(...)                  │    │
│    │          → view.BindingPropertyChanged(...)                   │    │
│    │            → BindingState.UpdateValue(...)                    │    │
│    │              ├─ ViewUpdateProperties match?                   │    │
│    │              │   YES → Binding<T>.EvaluateAndNotify()         │    │
│    │              │         → re-invoke Func, update native ctrl   │    │
│    │              │                                                │    │
│    │              └─ GlobalProperties match?                       │    │
│    │                  YES → view.Reload() (full body)              │    │
│    │                                                               │    │
│    └───────────────────────────────────────────────────────────────┘    │
│    │                                                                    │
│    ├─── ReactiveScope path (System 2) ─────────────────────────────┐   │
│    │    _subscribers.NotifyAll(this)                                │   │
│    │      → BodyDependencySubscriber.OnDependencyChanged()         │   │
│    │        → ReactiveScheduler.MarkViewDirty(view)                │   │
│    │          → dispatched to UI thread                             │   │
│    │            → view.Reload() (full body)                        │   │
│    └───────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  DEDUPLICATION: Suppress/Resume during Binding Func evaluation          │
│  ensures the reactive read is NOT tracked at body level, so only        │
│  the StateManager/Binding path fires — no redundant Reload().           │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

## Consequences

### What Works Well

1. **Interactive controls remain responsive.** Slider drag, Entry typing, and Toggle toggling all use the fine-grained Binding path. Only the specific property is updated via the handler's property mapper — no body rebuild, no focus loss.

2. **Navigation-level state changes work correctly.** Reading `selectedIndex.Value` directly in the body (not inside a Binding Func) is tracked by ReactiveScope. Changing it triggers a full rebuild, which is exactly what's needed when swapping entire pages.

3. **The boundary is small and contained.** The Suppress/Resume mechanism is ~21 lines in `ReactiveScope.cs` (lines 49–62) + 8 lines in `Binding.cs` (lines 108–110 and 124–126). The blast radius of a bug here is limited.

4. **Batching works across both systems.** `StateManager.BeginBatch/EndBatch` defers Binding re-evaluations, and `ReactiveScheduler` naturally coalesces via dispatcher posting. Multiple rapid state changes produce a single UI update.

5. **Backward compatibility.** Code using `State<T>` (the old API) continues to work unchanged. `Reactive<T>` extends `State<T>`, so it participates in both systems without requiring migration.

### What's Fragile

1. **Thread-static coupling.** Both `ReactiveScope._current` and `StateManager._currentReadProperties` are `[ThreadStatic]`. The Suppress/Resume mechanism assumes Binding Func evaluation happens on the same thread as body execution. Background-thread reads are untracked by design, but if body evaluation ever moves off the UI thread, the suppress/resume pairing could mismatch.

2. **Ordering sensitivity.** `Suppress()` **must** be called before `Get.Invoke()` and `Resume()` **must** be called after — including in exception paths. Currently `ProcessGetFunc` does not use try/finally (lines 102-117), so an exception during `Get.Invoke()` would leave the scope permanently suppressed on that thread. The `State<T>` implicit operator (lines 121-141) has the same pattern.

3. **Implicit operator footguns.** The `State<T>` → `Binding<T>` implicit operator (line 121) and the deprecated `T` → `Binding<T>` operator (line 76) both interact with StateManager tracking. The deprecated operator is still callable and has different semantics (EndProperty vs StartProperty/EndProperty). Removing it is blocked by backward compatibility.

4. **Dual notification on Reactive<T> writes.** Every `Reactive<T>.Value` set fires **both** the StateManager PropertyChanged path and the ReactiveScope subscriber path. If a `Reactive<T>` is read both directly in the body AND inside a Binding Func (a developer mistake), the view will rebuild twice — once via each path. There's no deduplication at the notification level; it relies on the tracking-level suppression to prevent this scenario.

5. **No compile-time safety.** Whether a read goes through System 1 or System 2 depends entirely on runtime context (is a ReactiveScope active? was it suppressed?). There's no type-system distinction between "body-level reactive state" and "binding-level tracked state."

### Maintenance Burden

- **Two mental models.** Contributors must understand both tracking systems and their interaction to reason about when body rebuilds vs. property updates will fire.
- **Testing both paths.** Tests for interactive controls must verify no body rebuild occurs (Binding path only), while tests for navigation state must verify rebuild does occur (ReactiveScope path). The test suite has dedicated hot-reload state transfer tests but limited coverage of the suppress/resume boundary itself.
- **Signal<T> adds a third variant.** `Signal<T>` (pure `IReactiveSource`, not extending `State<T>`) only participates in System 2. Mixing `Signal<T>` and `State<T>` in the same view introduces yet another behavior matrix.

## Code References

| Component | File | Lines | Role |
|-----------|------|-------|------|
| ReactiveScope | `src/Comet/Reactive/ReactiveScope.cs` | 1–69 | Thread-static tracking context, Suppress/Resume |
| Binding<T> | `src/Comet/Binding.cs` | 52–387 | Fine-grained property binding with Func re-eval |
| ProcessGetFunc | `src/Comet/Binding.cs` | 102–117 | Suppress/Resume during Func evaluation |
| State<T>→Binding<T> | `src/Comet/Binding.cs` | 121–141 | Suppress/Resume during implicit conversion |
| Reactive<T> | `src/Comet/Reactive.cs` | 18–64 | Bridge type: extends State<T> + IReactiveSource |
| State<T> | `src/Comet/State.cs` | 12–69 | Original state wrapper with CallPropertyRead/Changed |
| BindingObject | `src/Comet/BindingObject.cs` | 22–108 | Base class for observable objects |
| BindingState | `src/Comet/BindingObject.cs` | 111–196 | Per-view property tracking (Global vs ViewUpdate) |
| StateManager | `src/Comet/StateManager.cs` | 16–593 | Static hub: property read/change routing, batching |
| View.GetRenderViewReactive | `src/Comet/Controls/View.cs` | 405–441 | Body execution with ReactiveScope tracking |
| BodyDependencySubscriber | `src/Comet/Controls/View.cs` | 444–458 | Inner class bridging IReactiveSource → view.Reload |
| View.BindingPropertyChanged | `src/Comet/Controls/View.cs` | 476–501 | Entry point for StateManager notifications |
| ReactiveScheduler | `src/Comet/Reactive/ReactiveScheduler.cs` | 10–212 | Batched UI-thread flush for dirty views/effects |
| IReactiveSource | `src/Comet/Reactive/IReactiveSource.cs` | 3–8 | Subscribe/Unsubscribe/Version interface |
| IReactiveSubscriber | `src/Comet/Reactive/IReactiveSubscriber.cs` | 3–6 | OnDependencyChanged callback interface |
| Signal<T> | `src/Comet/Reactive/Signal.cs` | 9+ | Pure reactive source (System 2 only) |

## Future Considerations

1. **Unify on ReactiveScope.** Long-term, the StateManager property-tracking system could be replaced by fine-grained reactive subscriptions (e.g., per-property `Computed<T>` nodes). This would eliminate the need for Suppress/Resume entirely but requires rethinking how handler property mappers consume state.

2. **Add try/finally to Suppress/Resume.** The `ProcessGetFunc` and `State<T>` implicit operator should wrap the Func evaluation in try/finally to prevent scope leaks on exception.

3. **Deprecation path for `State<T>`.** `State<T>` is already marked `[Obsolete]` (State.cs:11). As `Reactive<T>` and `Signal<T>` adoption grows, the StateManager system can be gradually retired. The Suppress/Resume bridge is the seam that will be removed last.
