# Comet vs MauiReactor: State Management Comparative Analysis

> **Purpose:** Honest, code-verified analysis of two MVU state management systems for .NET MAUI.
> **Audience:** Comet maintainers investigating UI interaction bugs.
> **Sources:** Comet `src/Comet/` source code, MauiReactor `src/MauiReactor/` source code, and both frameworks' `docs/state-management.md`.

---

## 1. Executive Summary

Comet and MauiReactor both implement Model-View-Update patterns on top of .NET MAUI, but their state management philosophies are fundamentally different.

**Comet** uses **implicit dependency tracking with fine-grained bindings**. When a view's `Body` executes, Comet records every `State<T>.Value` read via `StateManager.OnPropertyRead`. Later writes to those properties are routed through a two-tier system: targeted `ViewPropertyChanged` for bindings inside lambdas, or full `Reload()` for structural dependencies read during body evaluation. This is powerful — a single `State<string>` change can update just one label's text property without rebuilding any view tree. But the implicit nature means the system's behavior depends on *how* a value is consumed (inline vs lambda, formatted vs raw, single-state vs multi-state), creating a class of silent performance traps and correctness bugs.

**MauiReactor** uses **explicit `SetState()` with positional reconciliation**. State lives in plain POCO classes inside `Component<S>`. Mutations happen through `SetState(s => s.Prop = value)`, which always dispatches to the UI thread, optionally invalidates the component, and triggers a `Render()` → reconciliation cycle. Fine-grained updates are opt-in via reactive lambdas (`Func<T>` property values). The system is predictable — every `SetState` call has the same dispatch path regardless of how state is read — but coarser-grained by default.

**The core trade-off:** Comet optimizes for the common case (targeted binding updates are cheap) at the cost of a fragile implicit contract. MauiReactor optimizes for predictability (every mutation follows the same path) at the cost of doing more work per update cycle.

**For teams experiencing UI bugs:** The most likely culprits in Comet are: (1) formatted string bindings silently causing full rebuilds, (2) background thread state mutations bypassing main-thread dispatch, (3) binding stabilization preventing dependency updates after conditional changes, and (4) missing batching on rapid state mutation sequences. Each of these is analyzed in detail below with source code citations and specific fix recommendations.

---

## 2. Architecture Comparison Table

| Aspect | Comet | MauiReactor |
|--------|-------|-------------|
| **State declaration** | `readonly State<T> field = value;` (extends `BindingObject`) | Plain POCO class: `class MyState { public int X { get; set; } }` |
| **State container** | `State<T>` wraps a single value with read/write tracking | `Component<S>` holds a `TState` instance, lazily initialized |
| **Mutation API** | Direct assignment: `state.Value = newValue` | Explicit: `SetState(s => s.X = value)` |
| **Change detection** | `INotifyPropertyRead` + `INotifyPropertyChanged` via `BindingObject` | None — `SetState` always assumes state changed |
| **Dependency tracking** | Implicit: reads during `Body` / `Func<T>` evaluation are recorded | None — reactive lambdas re-evaluate on every `SetState` of the container component |
| **Update granularity** | Two-tier: targeted `ViewPropertyChanged` (binding) or full `Reload()` (global) | Two-tier: full `Render()` + reconciliation, or reactive lambda callbacks (opt-in) |
| **Threading** | No automatic dispatch for `State<T>.Value` sets; `Component.SetState` and `SetGlobalEnvironment` dispatch to main thread | `SetState()` always checks `Dispatcher.IsDispatchRequired` and re-dispatches |
| **Batching** | Explicit `StateManager.BeginBatch()`/`EndBatch()`; `Component.SetState` batches automatically | Automatic coalescing via `_layoutCallEnqueued` flag in `ReactorApplicationHost` |
| **Reconciliation** | Diff algorithm comparing old/new view trees with handler reuse | Positional matching: `MergeChildrenFrom()` pairs children by index |
| **Child identity** | Type-based diff with hot reload awareness | Positional — no keys; reordering children is destructive |
| **Hot reload** | `BindingState.ChangedProperties` dictionary transfer + `IComponentWithState` interface | `_newComponent` forwarding chain + `TypeLoader.CopyProperties()` for cross-assembly |
| **Cleanup** | `StateManager.Disposing(view)` removes from all tracking dictionaries; weak refs on bindings | Tree-based unmount: `OnWillUnmount()` → `OnUnmount()` recursively |
| **Source generation** | `[AutoNotify]` for fields, `CometViewSourceGenerator` for MAUI interface wrappers | `[Prop]`, `[Param]`, `[Inject]` for fluent setters, DI wiring, global parameters |
| **Cross-component state** | Shared `BindingObject` instances monitored by multiple views | `IParameter<T>` (global dictionary with weak-ref observers) |
| **DI integration** | `[Environment]` attribute + `PopulateFromEnvironment()` with DI fallback | `[Inject]` source-generated constructor with `GetRequiredService<T>()` |
| **Equality checking** | `EqualityComparer<T>.Default` on `State<T>.Value` set — skips if equal | None — `SetState` always proceeds; no equality check on state properties |

---

## 3. Comet Flaws & Risks

### A. The Implicit Conversion Trap

This is Comet's most dangerous footgun and the single most likely source of UI interaction bugs. The behavior of a state read depends entirely on *how* the value flows into a `Binding<T>`, and getting it wrong silently degrades from a targeted binding update to a full view rebuild. There is no compiler warning, no runtime log in release builds, and no visual indicator that the wrong path was taken.

The complexity arises from C#'s implicit conversion rules interacting with Comet's property tracking. When you write `new Text(someExpression)`, the C# compiler evaluates `someExpression` first, then applies implicit conversion to `Binding<string>`. During that evaluation, `StateManager` is recording every `State<T>.Value` read. The conversion operator then examines those recorded reads to decide the binding strategy. The developer has no direct control over this — the same logical value can take radically different paths depending on syntactic form.

**The three paths through `implicit operator Binding<T>(T value)`** (`Binding.cs:75-94`):

```
State<T>.Value read during Body
    ↓
StateManager.EndProperty() returns property list
    ↓
┌─ props.Count > 1  → AddGlobalProperties (FULL REBUILD on any change)
├─ props.Count == 1 && BindingObject is State<T> → return State<T> implicit (TARGETED)
└─ props.Count == 1 but type mismatch → IsValue binding (checked in BindToProperty)
```

**Path 1 — Targeted binding (correct):**
```csharp
readonly State<string> name = "Alice";
new Text(name.Value);  // EndProperty returns [(name, "Value")], count == 1, is State<string> → targeted
```

**Path 2 — Formatted string, single state (global fallback):**
```csharp
readonly State<int> count = 0;
new Text($"Count: {count.Value}");  // EndProperty returns [(count, "Value")], count == 1
// BUT: the value is "Count: 0" (string), not 0 (int)
// BindToProperty checks: EqualityComparer<string>.Default.Equals(stateValue: 0, CurrentValue: "Count: 0")
// They DON'T match → falls through to isGlobal = true → FULL REBUILD
```

This check happens in `Binding.cs:190-214`. The `BindToProperty` `IsValue` branch reads back the state property value, casts it to `T`, and compares it with `CurrentValue`. For formatted strings, the state value is the *raw* value (e.g., integer `0`) while `CurrentValue` is the interpolated string (`"Count: 0"`). They never match, so the binding is **silently promoted to global**. A `Debug.WriteLine` warning fires, but only in debug builds with a debugger attached.

**Path 3 — Multiple state reads (always global):**
```csharp
readonly State<string> first = "Alice";
readonly State<string> last = "Smith";
new Text($"{first.Value} {last.Value}");  // EndProperty returns count > 1 → global immediately
```

At `Binding.cs:78-79`, `props?.Count > 1` triggers `AddGlobalProperties` directly in the implicit operator, before `BindToProperty` even runs.

**Why this matters for UI bugs:** A developer writes `new Text($"Score: {score.Value}")` thinking it's a simple binding. In reality, every change to `score` triggers a full `Reload()` of the parent view — rebuilding the entire view tree, running the diff algorithm, and potentially losing transient UI state (scroll position, focus, animation progress). The fix is trivial (`new Text(() => $"Score: {score.Value}")`), but the failure mode is invisible.

**A complete taxonomy of the same logical binding expressed four ways:**

```csharp
readonly State<string> name = "Alice";

// 1. Direct value — TARGETED (via State<T> implicit → Binding<T>)
new Text(name.Value);

// 2. Lambda — TARGETED (via Func<T> implicit → Binding<T>)
new Text(() => name.Value);

// 3. Formatted string, single state — GLOBAL (type mismatch in BindToProperty)
new Text($"Hello, {name.Value}!");

// 4. Lambda with formatting — TARGETED (Func tracks reads during evaluation)
new Text(() => $"Hello, {name.Value}!");
```

Forms 1 and 2 result in targeted property updates. Form 3 silently causes full view rebuilds. Form 4 is the correct way to format a string with state. The only difference between 3 and 4 is the `() =>` prefix, yet the performance and correctness implications are enormous. This is a textbook example of an API where the pit of success and the pit of failure are separated by two characters.

**Interaction with multi-state expressions is even worse:**

```csharp
readonly State<string> first = "Alice";
readonly State<string> last = "Smith";

// This creates a GLOBAL binding — any change to first OR last
// triggers a full Reload() of the parent view
new Text($"{first.Value} {last.Value}");

// This creates a TARGETED binding — changes re-evaluate the lambda
// and update only the Text control's property
new Text(() => $"{first.Value} {last.Value}");
```

In the first form, both properties are registered as global dependencies of the parent view. A change to either one rebuilds the entire view tree. In the second form, both properties are tracked as dependencies of the binding's Func, and a change re-evaluates just the lambda. The performance difference scales with view tree complexity.

### B. Threading Model Gaps

Comet's threading story is inconsistent. Some mutation paths dispatch to the main thread; others don't.

**Dispatches to main thread:**
- `SetGlobalEnvironment` — `View.cs:552`: `ThreadHelper.RunOnMainThread(() => { ... })`
- `IHotReloadableView.Reload()` — `View.cs:1025`: `ThreadHelper.RunOnMainThread(() => Reload(true))`
- `Component<TState>.SetState` — `Component.cs:121`: `ThreadHelper.RunOnMainThread(() => Reload())`
- Animation registration — `View.cs:874,885`

**Does NOT dispatch to main thread:**
- `StateManager.OnPropertyChanged` — `StateManager.cs:384-496`: Calls `view.BindingPropertyChanged()` directly on the calling thread
- `View.BindingPropertyChanged` — `View.cs:423-443`: Calls `Reload()` or `ViewPropertyChanged()` directly
- `View.ViewPropertyChanged` — `View.cs:494-533`: Calls `ViewHandler?.UpdateValue()` directly with no thread check
- `Binding<T>.EvaluateAndNotify` — `Binding.cs:268-337`: Calls `View?.ViewPropertyChanged()` directly

**Consequence:** If you set `state.Value = x` from a background thread (e.g., after an HTTP call), the entire notification chain — `BindingObject.CallPropertyChanged` → `StateManager.OnPropertyChanged` → `view.BindingPropertyChanged` → `view.ViewPropertyChanged` → `ViewHandler?.UpdateValue` — runs on the background thread. Most MAUI platform controls require main thread access for property updates. This can produce:
- Silent data corruption (property set races with layout)
- Platform assertion crashes ("UI operations must be on the main thread")
- Race conditions between state change dispatch and user interaction
- Intermittent, hard-to-reproduce UI glitches

**The inconsistency is particularly dangerous for developers who mix patterns.** If a developer uses `Component.SetState` for some state changes (which dispatches to main thread), they may assume all state changes are thread-safe. When they later write `state.Value = result` after an `await`, the lack of thread dispatch can cause crashes that only appear under load or on specific platforms.

**Detailed trace of a background thread state mutation:**

```
Background thread:
  state.Value = "new value"
    → State<T>.Value setter (State.cs:36-47)
    → CallPropertyChanged("Value", "new value") (BindingObject.cs:86-88)
    → StateManager.OnPropertyChanged(this, "Value", "new value") (StateManager.cs:384)
    → _rwLock.EnterReadLock() (StateManager.cs:399) — acquires read lock ON BACKGROUND THREAD
    → NotifyToViewMappings lookup
    → _rwLock.ExitReadLock()
    → view.BindingPropertyChanged(...) — called ON BACKGROUND THREAD
    → State.UpdateValue(...) — accesses BindingState ON BACKGROUND THREAD
    → ViewPropertyChanged("Value", "new value") — called ON BACKGROUND THREAD
    → ViewHandler?.UpdateValue("Text") — NATIVE CONTROL UPDATED ON BACKGROUND THREAD ← CRASH
```

**Contrast with MauiReactor:** `SetState()` **always** checks `Application.Current.Dispatcher.IsDispatchRequired` and re-dispatches the entire operation to the UI thread if needed. State mutations always occur on the main thread — no exceptions, no footgun.

### C. Global vs Local Classification Is Fragile

Whether a state read becomes "global" (full rebuild) or "local" (targeted binding) depends on three factors that developers have no direct control over:

1. **When the read happens:** Reads inside `Body.Invoke()` (wrapped by `StateBuilder`) become global. Reads inside a `Func<T>` (wrapped by `StartProperty()`/`EndProperty()`) become local. But the boundary between these contexts is determined by C# expression evaluation order and implicit conversions, not by any explicit developer annotation.

2. **How the value is consumed:** `new Text(state.Value)` → targeted (via `State<T>` implicit operator, `Binding.cs:83`). `new Text($"{state.Value}")` → global (string interpolation evaluates before the implicit conversion). `new Text(() => state.Value)` → targeted (explicit `Func<T>`). The semantic difference between these three is invisible to most developers.

3. **Type compatibility:** The `IsValue` branch in `BindToProperty` (`Binding.cs:187-214`) compares the raw state value with the binding's `CurrentValue`. If types don't match (e.g., `State<int>` read, but `Binding<string>` expected), the equality check fails and the binding falls through to global.

**The stabilization trap** (`Binding.cs:273-289`, `_bindingStable` field at line 66):

After a `Binding<T>`'s `Func<T>` re-evaluates and the set of read properties hasn't changed, `_bindingStable` is set to `true`. From that point forward, `EvaluateAndNotify` skips `StartProperty()`/`EndProperty()` entirely — it just invokes the Func and compares the result. This means:

```csharp
readonly State<bool> showFirst = true;
readonly State<string> firstName = "Alice";
readonly State<string> lastName = "Smith";

new Text(() => showFirst.Value ? firstName.Value : lastName.Value);
```

**Step-by-step walkthrough of the stabilization bug:**

1. **Initial evaluation** (`ProcessGetFunc`, `Binding.cs:100-111`):
   - `StartProperty()` → clear tracking list, set `_isTrackingProperties = true`
   - Func invokes → reads `showFirst.Value` (tracked), `firstName.Value` (tracked)
   - `EndProperty()` → returns `[(showFirst, "Value"), (firstName, "Value")]`
   - `BoundProperties = [(showFirst, "Value"), (firstName, "Value")]`

2. **First state change** (e.g., `firstName.Value = "Bob"`):
   - `EvaluateAndNotify` fires (`Binding.cs:268`)
   - `_bindingStable` is `false`, so it re-tracks: `StartProperty()` → Func → `EndProperty()`
   - Same properties read: `showFirst` + `firstName`
   - `ArePropertiesDifferent` returns `false` → `_bindingStable = true` (`Binding.cs:289`)

3. **`showFirst.Value` changes to `false`:**
   - `EvaluateAndNotify` fires
   - `_bindingStable` is `true`, so it takes the **fast path** (`Binding.cs:274-277`):
     ```csharp
     CurrentValue = Get == null ? default : Get.Invoke();
     ```
   - The Func now reads `showFirst.Value` (still tracked) + `lastName.Value` (**NOT tracked**)
   - The result is correct ("Smith"), but `lastName` is not in `BoundProperties`

4. **`lastName.Value` changes to "Jones":**
   - `StateManager.OnPropertyChanged(lastName, "Value", "Jones")`
   - Looks up `NotifyToViewMappings[lastName]` — **the view is NOT in this set** because `lastName` was never registered as a dependency
   - The notification is silently dropped
   - The UI shows "Smith" when the state says "Jones" ← **BUG: stale data**

5. **Recovery:** Only happens when the parent view rebuilds (e.g., a different global property changes), which recreates the binding from scratch and re-tracks dependencies.

**Why the performance trade-off may not be worth it:** The stabilization optimization saves one `StartProperty()`/`EndProperty()` pair per binding evaluation. These methods are lightweight — they set a boolean flag and append to a thread-local list. The cost of missing a dependency update (stale UI, confused users, bug reports) almost certainly outweighs the performance gain of skipping tracking.

### D. Static Mutable State in StateManager

`StateManager` (`StateManager.cs:27-32`) uses static dictionaries for all tracking state:

```csharp
static Dictionary<string, List<INotifyPropertyRead>> ViewObjectMappings = new(...);
static Dictionary<INotifyPropertyRead, HashSet<View>> NotifyToViewMappings = new(...);
static Dictionary<INotifyPropertyChanged, Dictionary<string, string>> ChildPropertyNamesMapping = new(...);
static List<INotifyPropertyRead> MonitoredObjects = new();
```

**Memory leak risk:** If a view is created but never properly disposed (e.g., navigation pushes without pops, exception during construction, circular references preventing GC), its entries in `ViewObjectMappings` and `NotifyToViewMappings` persist forever. The `Disposing` method (`StateManager.cs:162-207`) only runs when `View.Dispose()` is called explicitly.

The `OnPropertyChanged` method does clean up disposed views it encounters during iteration (`StateManager.cs:459-462`), but this is opportunistic — it only fires when a property actually changes. Stale entries for views that depend on state that never changes accumulate indefinitely.

**How this manifests in practice:** In a navigation-heavy app, pushing and popping views rapidly creates and disposes many `State<T>` instances. Each one registers in `MonitoredObjects`, `ViewObjectMappings`, `NotifyToViewMappings`, and `ChildPropertyNamesMapping`. If a view's `Dispose()` is delayed or skipped (e.g., the view is held alive by an event handler closure), its mappings persist. Over time:
- `NotifyToViewMappings` grows, causing longer iteration in `OnPropertyChanged`
- `MonitoredObjects` grows, causing `Contains()` checks (on a `List<T>`, which is O(n)!) to slow down
- `ReaderWriterLockSlim` contention increases as more entries require longer critical sections

The use of `List<T>` for `MonitoredObjects` (`StateManager.cs:32`) is particularly concerning — `Contains()` is O(n), and it's called in `RegisterChild` (`StateManager.cs:283`) and `StartMonitoring` (`StateManager.cs:300`). This should be a `HashSet<T>` for O(1) lookups.

**Lock contention:** All four dictionaries are protected by a single `ReaderWriterLockSlim` (`StateManager.cs:18`). Under heavy UI churn (scrolling a virtualized list, animations triggering rapid state changes), the write lock for view registration/deregistration competes with read locks for property change dispatch. The single-view fast path (`StateManager.cs:409-413`) mitigates this for the common case, but multi-view scenarios (shared state classes) take the full ArrayPool path with two separate lock acquisitions (`StateManager.cs:441-451`).

**Batching state is also static** (`StateManager.cs:35-37`): `_batchDepth`, `_dirtyBindings`, and `_viewsNeedingReload` are static fields. Nested batches from different call sites share the same depth counter. This is generally fine (batching is typically single-threaded on the UI thread), but violates the principle of least surprise.

### E. State\<T\> Fields Must Be Readonly — Enforced at Runtime

`StateManager.CheckForStateAttributes` (`StateManager.cs:251-273`) checks `field.IsInitOnly` via reflection and throws `ReadonlyRequiresException` if a `State<T>` field isn't `readonly`:

```csharp
if (!field.IsInitOnly)
{
    throw new ReadonlyRequiresException(field.DeclaringType?.FullName, field.Name);
}
```

This is a runtime check, not a compile-time check. There is no Roslyn analyzer that catches this. The exception fires during view construction (`ConstructingView`), which means:
- It's caught by manual testing or at startup, not by CI
- If the view is only constructed conditionally (behind navigation, in a lazy tab), the bug can ship to production
- The error message is clear (`"MyView.count is not readonly"`), but the fix requires understanding *why* readonly matters (the original `State<T>` instance is registered in `NotifyToViewMappings`; reassigning the field breaks the dependency graph)

### F. Dual Storage in State\<T\>

`State<T>` stores its value in **two places** (`State.cs:13-22`):

```csharp
T _value;                              // Typed field — used by Value getter
dictionary[ValuePropertyName] = value; // BindingObject dictionary — for BindingObject compatibility
```

The constructor writes to both. But the `Value` setter (`State.cs:37-47`) only updates `_value`:

```csharp
set
{
    if (_hasValue && EqualityComparer<T>.Default.Equals(_value, value))
        return;
    _value = value;
    _hasValue = true;
    CallPropertyChanged(ValuePropertyName, value);
    ValueChanged?.Invoke(value);
}
```

The dictionary entry written in the constructor is never updated again. `GetValueInternal` (`State.cs:53-58`) correctly returns `_value`, bypassing the dictionary. But `BindingObject.GetProperty<T>` (`BindingObject.cs:43-50`) reads from the dictionary:

```csharp
protected T GetProperty<T>(T defaultValue = default, [CallerMemberName] string propertyName = "")
{
    CallPropertyRead(propertyName);
    if (dictionary.TryGetValue(propertyName, out var val))
        return (T)val;
    return defaultValue;
}
```

In practice, `State<T>` never calls `GetProperty<T>` for the "Value" property — it has its own getter. But if external code calls `state.GetValueInternal("Value")` (which is `internal`), it returns the correct typed value. The dictionary staleness is a latent inconsistency: harmless now, but a maintenance risk if anyone adds code that reads from the dictionary for State<T>'s "Value" key.

### G. No Built-in Coalescing for Raw State\<T\> Mutations

MauiReactor's `ReactorApplicationHost` coalesces multiple `RequireLayoutCycle()` calls into a single `Dispatcher.Dispatch(OnLayout)` via a `_layoutCallEnqueued` flag. This means rapid-fire `SetState()` calls (e.g., from drag events, text input, animations) naturally batch into one layout pass.

Comet's `Component.SetState` wraps mutations in `BeginBatch()`/`EndBatch()` (`Component.cs:110-118`), so multiple property changes within a single `SetState` call are batched. But **direct `State<T>.Value` assignment has no automatic batching**:

```csharp
// Each assignment triggers OnPropertyChanged → BindingPropertyChanged immediately
firstName.Value = "Bob";   // → full notification chain
lastName.Value = "Jones";  // → full notification chain again
age.Value = 25;            // → full notification chain a third time
```

Without an explicit `StateManager.BeginBatch()` / `StateManager.EndBatch()` wrapper, each assignment independently triggers the full dispatch path. If these are global properties, the view rebuilds three times. If they're bound properties, three separate `ViewPropertyChanged` calls fire.

**Timing example of the batching gap:**

```csharp
// User types a character in a text field backed by State<string>
// The TextChanged handler fires:
void OnTextChanged(string newText)
{
    searchQuery.Value = newText;  // → immediate OnPropertyChanged → Reload()
    // View tree rebuilt, diff runs, handlers updated...
    
    isSearching.Value = true;    // → immediate OnPropertyChanged → Reload() AGAIN
    // View tree rebuilt AGAIN, diff runs AGAIN...
    
    results.Value = new List<Item>();  // → immediate OnPropertyChanged → Reload() THIRD TIME
    // View tree rebuilt a THIRD time...
}
```

With batching:

```csharp
void OnTextChanged(string newText)
{
    StateManager.BeginBatch();
    try
    {
        searchQuery.Value = newText;    // deferred
        isSearching.Value = true;       // deferred
        results.Value = new List<Item>();  // deferred
    }
    finally
    {
        StateManager.EndBatch();  // single Reload() here
    }
}
```

The developer must know to wrap multi-mutation sequences. In MauiReactor, this batching is automatic — multiple `SetState()` calls within the same synchronous frame coalesce into a single layout pass via `_layoutCallEnqueued`.

### H. Hot Reload State Transfer Limitations

Comet's hot reload state transfer copies `BindingState.ChangedProperties` — a dictionary populated by `UpdateValue` during the view's lifetime (`BindingObject.cs:165`):

```csharp
// BindingObject.cs, BindingState.UpdateValue
changeDictionary[fullProperty] = value;
```

This only captures state changes that went through the `BindingPropertyChanged` → `UpdateValue` path. State held in other locations is NOT transferred:

1. **Private fields not backed by `State<T>`:** If a view stores computed or derived data in plain fields, those are lost.
2. **State in closures:** Lambda captures over local variables in `Body` are lost on rebuild.
3. **Transient UI state:** Scroll position, focus, selection ranges — anything managed by the platform handler and not reflected in `BindingState`.

For `Component<TState>`, the `TransferStateFrom` override (`Component.cs:140-146`) transfers the `TState` object reference directly — which is more complete but only works for the component pattern.

**MauiReactor's approach is more robust:** `Component<S,P>.MergeWith()` transfers the state object directly for same-type components. For cross-assembly hot reload (different assembly due to recompilation), it uses `TypeLoader.Instance.CopyProperties()` — reflection-based property-by-property copying. Additionally, the `_newComponent` forwarding chain ensures old `SetState` closures (captured in event handlers before the reload) forward their mutations to the current live component instance. Comet has no equivalent forwarding mechanism for captured closures.

---

## 4. MauiReactor Strengths

### 4.1 Explicit State Mutations

Every state change in MauiReactor goes through `SetState()`. There is no implicit tracking, no ambient property recording, no distinction between "the value was read inside a lambda" vs "the value was read inline." This eliminates an entire category of bugs:

- No formatted-string trap (there is nothing to trap)
- No global-vs-local misclassification
- No binding stabilization edge cases
- No dependency on C# evaluation order for correctness

### 4.2 Guaranteed UI Thread Dispatch

`SetState()` always checks `Dispatcher.IsDispatchRequired` and re-dispatches if needed. This is a hard guarantee — state mutations always occur on the main thread. Comet only provides this for `Component.SetState`, `SetGlobalEnvironment`, and `IHotReloadableView.Reload()`. Raw `State<T>.Value` assignment has no thread safety.

### 4.3 Automatic Coalescing

`ReactorApplicationHost.OnLayoutCycleRequested()` uses `_layoutCallEnqueued` to coalesce multiple invalidations into a single `Dispatcher.Dispatch(OnLayout)`. Multiple rapid `SetState()` calls → one layout pass. No explicit `BeginBatch`/`EndBatch` required.

### 4.4 Reactive Lambdas as First-Class Fine-Grained Updates

MauiReactor's reactive lambdas (`Label(() => $"Count: {State.Counter}")`) bypass the full `Render()` → reconciliation cycle entirely. When `SetState()` fires, registered state-change callbacks re-evaluate each lambda and update the native control property directly. This provides fine-grained updates when developers opt in, without any of Comet's implicit classification complexity.

The `invalidateComponent: false` parameter on `SetState()` makes this explicit: "mutate state, fire reactive lambda callbacks, but do NOT re-render the component." This is a deliberate, visible optimization rather than an implicit side-effect of how a value was consumed.

### 4.5 Clean Separation of Render vs Reactive Updates

MauiReactor has two distinct update paths, and the developer chooses between them explicitly:
- **Structural updates:** `SetState()` with `invalidateComponent: true` (default) → `Render()` → reconciliation
- **Property updates:** Reactive lambda callbacks → direct native control property update

Comet conflates these two paths through implicit classification, making it hard to predict which path a given state change will take.

### 4.6 Better Hot Reload Story

MauiReactor's `_newComponent` forwarding chain means old `SetState` closures continue to work after hot reload — they chase the linked list to the current live component. Consider this scenario:

```csharp
// Before hot reload
Button("Click", () => SetState(s => s.Count++))  // closure captures 'this' (old component)

// Hot reload replaces the component type
// Old component has _newComponent → new component
// When button is clicked, old component's SetState() traverses the chain:
//   TryForwardStateToNewComponent() → finds _newComponent → forwards mutation
```

Comet's closure-captured `State<T>` references survive reload (since the `State<T>` instance is `readonly` and transferred), but closures capturing anything else (local variables, `this` references to the old view instance) go stale. There is no forwarding mechanism.

MauiReactor's `TypeLoader.CopyProperties()` for cross-assembly hot reload is also more thorough than Comet's `ChangedProperties` dictionary approach, which only captures values that went through the `UpdateValue` path.

### 4.7 Source-Generated Boilerplate

`[Prop]`, `[Param]`, `[Inject]` generate fluent setters, parameter wiring, and DI resolution at compile time. Comet's `[AutoNotify]` handles property generation, but the `[Body]` attribute is resolved at runtime via reflection (`View.CheckForBody()` → `GetDeepMethodInfo`), and there's no equivalent to `[Inject]` or `[Param]`.

---

## 5. MauiReactor Weaknesses

### 5.1 Positional Reconciliation (No Keys)

`MergeChildrenFrom()` pairs old and new children by **index position**, not by identity. This means:

```csharp
// Before: [ItemA, ItemB, ItemC]
// After:  [ItemB, ItemC]  (ItemA removed from front)
```

MauiReactor pairs `ItemA ↔ ItemB` (index 0), `ItemB ↔ ItemC` (index 1), and unmounts `ItemC` (excess old child). The native controls for positions 0 and 1 receive entirely new property values — they aren't "moved," they're overwritten. If `ItemA` and `ItemB` have different types, the old control is unmounted and a new one is created.

For lists that can reorder, insert, or remove from the middle, this causes:
- Unnecessary native control recreation
- Loss of control-local state (focus, scroll offset, text selection)
- Animation discontinuities (animations bound to a position, not an item)
- Performance degradation for large lists

**Concrete example of the damage:**

```csharp
// Render() returns a VStack of todo items
VStack(
    State.Todos.Select(todo => new TodoRow().Text(todo.Title).IsCompleted(todo.Done))
)

// User completes the first todo, which moves it to the bottom
// SetState reorders: [Todo2, Todo3, Todo1(done)]
// Reconciliation: position 0 (was Todo1) merges with new position 0 (Todo2)
//   → All properties on the native control at position 0 are overwritten
//   → If the control had focus or mid-edit text, that's lost
//   → The "completion" animation that was playing gets detached
```

React solves this with `key` props that enable the diff algorithm to identify matching nodes even when their position changes. Flutter has `Key` widgets. MauiReactor has no equivalent — the only workaround is to design UIs that avoid reordering, or to use `CollectionView` which handles its own item recycling.

**Comet has the same limitation** in its diff algorithm, but it's less visible because Comet's fine-grained bindings can update individual properties without rebuilding the view tree. A property-level change in Comet skips the diff entirely.

### 5.2 No Fine-Grained Dependency Tracking

MauiReactor does not track which state properties a `Render()` method reads. Every `SetState()` call (with default `invalidateComponent: true`) re-executes the entire `Render()` and reconciles the entire subtree, even if the mutation only affects a single property that feeds a single label.

The reactive lambda mechanism provides an escape hatch, but it's opt-in and property-level only. There's no equivalent to Comet's automatic "this binding only depends on `firstName.Value`, so only update the Text control" without the developer explicitly using a `Func<T>` overload.

**The performance implications are real for complex forms:**

```csharp
public class UserProfileState
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string Address { get; set; }
    public string City { get; set; }
    public string State { get; set; }
    public string Zip { get; set; }
    // ... 20 more fields
}

public class UserProfilePage : Component<UserProfileState>
{
    public override VisualNode Render()
    {
        // This entire method re-executes on EVERY SetState call,
        // even if only one field changed
        return ScrollView(
            VStack(
                Entry(State.FirstName).OnTextChanged(t => SetState(s => s.FirstName = t)),
                Entry(State.LastName).OnTextChanged(t => SetState(s => s.LastName = t)),
                Entry(State.Email).OnTextChanged(t => SetState(s => s.Email = t)),
                // ... 20 more entries
            )
        );
        // For each keystroke: full Render() + reconciliation of 20+ controls
    }
}
```

In Comet, each `Entry`/`TextField` would have its own `Binding<string>` that tracks only its specific `State<T>` property. A keystroke in the "FirstName" field would update only that control's binding — the other 19 entries are untouched. The Comet approach is O(1) per keystroke; the MauiReactor approach is O(n) where n is the number of controls in `Render()`.

MauiReactor developers work around this by splitting large forms into smaller sub-components, each with their own state. But this adds complexity and can be awkward when the form data needs to be submitted as a single unit.

### 5.3 Global Parameters Keyed by Type Name

`ParameterContext` stores shared parameters in a `Dictionary<string, IParameter>` keyed by `typeof(T).FullName`. Two different parameter types with the same `FullName` (possible in multi-assembly scenarios or with nested types in different namespaces) would collide. The `AsyncLocal` isolation for tests mitigates this in practice, but it's an architectural fragility.

### 5.4 SetState Always Dispatches, Even When Unnecessary

`SetState()` always checks `Dispatcher.IsDispatchRequired`, even when the call is already on the main thread. For high-frequency updates (60fps animations, touch tracking), this introduces a small but non-zero overhead per call. The `_layoutCallEnqueued` coalescing mitigates the downstream cost, but the dispatcher check itself happens unconditionally.

Additionally, `SetState` with `invalidateComponent: false` still iterates all registered state-change callbacks and fires them, even if the mutation didn't affect any reactive lambda's dependencies. There's no selectivity — all callbacks fire on every `SetState`, regardless of which property changed.

### 5.5 No Equality-Based Short-Circuiting

MauiReactor has no equivalent to Comet's `EqualityComparer<T>.Default.Equals(_value, value)` check in `State<T>.Value` setter. Calling `SetState(s => s.Counter = s.Counter)` (no-op) still triggers the full invalidation + layout cycle. The component's `ShouldUpdate` equivalent doesn't exist in the base framework.

This means high-frequency event handlers (touch move, scroll, text input) that call `SetState` even when the value hasn't actually changed will trigger unnecessary `Render()` calls. In Comet, the `State<T>.Value` setter's equality check prevents the notification from even firing.

### 5.6 All Reactive Lambda Callbacks Fire on Every SetState

When `SetState()` completes, it iterates all registered state-change callbacks and fires every one of them, regardless of which state property actually changed. If a component has 20 reactive lambdas bound to 20 different properties, a change to one property re-evaluates all 20 lambdas.

This is an O(n) cost per `SetState` where n is the number of reactive lambdas in the component. For components with many bound properties (forms, data-dense displays), this can become a measurable performance cost. Comet's fine-grained binding system avoids this — only the bindings that actually depend on the changed property are re-evaluated.

### 5.7 Weak Reference Overhead in Parameters

`IParameter<T>` uses `WeakReference<Component>` for subscriber tracking, which adds GC pressure and indirection cost. The pruning of dead weak references happens during `Set()` iteration — if a parameter is rarely set, dead references accumulate. In a navigation-heavy app with many parameter subscribers being created and destroyed, the subscriber set can grow before being cleaned.

### 5.8 No Incremental Reconciliation

MauiReactor's reconciliation runs synchronously on the main thread during `OnLayout()`. For a large component tree, this blocks the UI thread for the entire `Render()` + `MergeChildrenFrom()` duration. There's no equivalent to React's concurrent mode or time-slicing. The `_sleeping` flag suppresses layout when the app is backgrounded, but there's no prioritization of user-interactive updates over lower-priority updates.

---

## 6. Root Cause Analysis: UI Interaction Bugs in Comet

Based on the architecture analysis, here are the most likely causes of UI bugs in the Comet framework, ordered by probability and severity.

### 6.1 Formatted String Bindings Causing Excessive Rebuilds

**Mechanism:** `new Text($"Label: {state.Value}")` silently becomes a global property. Every change to `state` triggers `Reload()` on the entire parent view.

**Symptoms:**
- UI flicker during rapid state changes (text input, sliders, drag operations)
- Loss of scroll position or focus when updating a label
- Performance degradation proportional to view tree complexity
- Animations resetting on state change

**Detection:** Search for `Debug.WriteLine($"Warning: {property} is using formated Text` — this warning fires in debug builds but is invisible in release.

**Fix:** Replace `new Text($"...")` with `new Text(() => $"...")` everywhere state is interpolated.

### 6.2 Threading Issues on Background State Mutations

**Mechanism:** `State<T>.Value = x` from a background thread (after `await httpClient.GetAsync(...)`, timer callbacks, etc.) triggers `StateManager.OnPropertyChanged` → `view.BindingPropertyChanged` → `view.ViewPropertyChanged` → `ViewHandler?.UpdateValue()` all on the background thread.

**Symptoms:**
- Platform assertion crashes ("UI operations must be on the main thread")
- Intermittent data corruption (handler properties set during layout)
- Race conditions between state change dispatch and user interaction
- Crashes that reproduce on some devices/OS versions but not others

**Fix:** Wrap all background state mutations in `ThreadHelper.RunOnMainThread(() => state.Value = x)`, or use `Component.SetState` which handles threading automatically.

### 6.3 Binding Stabilization Preventing Dynamic Dependency Updates

**Mechanism:** After `_bindingStable = true` (`Binding.cs:289`), the binding stops tracking which properties its `Func<T>` reads. If the Func contains conditional logic (ternary, if/else, switch), branches that weren't taken during stabilization have their dependencies silently dropped.

**Symptoms:**
- UI stops updating after a condition changes (e.g., toggle between two display modes)
- Stale data displayed that doesn't respond to further state changes
- Bug only manifests after the binding has been stable for at least one evaluation cycle

**Fix:** Ensure all possible state reads happen in every evaluation path of the lambda:

```csharp
// Instead of:
new Text(() => flag.Value ? a.Value : b.Value)

// Use:
new Text(() => {
    var f = flag.Value;
    var aVal = a.Value;   // Always read
    var bVal = b.Value;   // Always read
    return f ? aVal : bVal;
})
```

### 6.4 Re-entrancy in ViewPropertyChanged

The `_propertiesBeingUpdated` guard (`View.cs:510-511`) exists because re-entrancy actually happened:

```csharp
// View.cs:508-511
// Re-entrancy guard: prevent infinite recursion when SetPropertyValue
// triggers SetPropertyInContext → SetEnvironment → ContextPropertyChanged → ViewPropertyChanged
_propertiesBeingUpdated ??= new HashSet<string>();
if (!_propertiesBeingUpdated.Add(property))
    return;
```

This guard prevents infinite recursion but also means the **second update is silently dropped**. If a property change triggers an environment update that feeds back into the same property with a different value, the second value is lost. This can cause state inconsistencies where the view's displayed value doesn't match the underlying state.

### 6.5 Batching Not Applied to Raw State\<T\> Mutations

**Mechanism:** Direct `state.Value = x` triggers immediate notification without coalescing. Multiple rapid mutations (from text input callbacks, gesture handlers, animation ticks) each trigger a separate `BindingPropertyChanged` → `Reload()` cycle.

**Symptoms:**
- UI jank during rapid interaction (typing, scrolling, dragging)
- Excessive CPU usage during input sequences
- Dropped frames visible in profiler

**Fix:** Wrap rapid mutation sequences in `StateManager.BeginBatch()` / `StateManager.EndBatch()`, or use `Component<TState>.SetState()` which batches automatically.

### 6.6 Stale Dictionary Entry in State\<T\> Constructor

**Mechanism:** `State<T>` constructor writes `dictionary[ValuePropertyName] = value` (`State.cs:21`), but the `Value` setter only updates `_value`. If any code path reads from the `dictionary` for the "Value" key (via `BindingObject.GetProperty<T>` or `GetValueInternal` base implementation), it gets the initial constructor value, not the current value.

**Current risk:** Low — `State<T>` overrides `GetValueInternal` to return `_value`, and the `Value` property getter reads from `_value` directly. But this is a latent bug waiting for a code change that accidentally goes through the base class path.

### 6.7 OnPropertyChanged Value Type Filter Silently Drops Updates

At `StateManager.cs:386`, there's a filter that ignores property changes whose value is a `View`:

```csharp
if (value?.GetType() == typeof(View))
    return;
```

This check uses exact type equality (`== typeof(View)`), not `is View`. So changes to `State<View>` values would be silently dropped, but changes to `State<Text>` (a View subclass) would pass through. This inconsistency could cause hard-to-diagnose bugs if anyone stores view references in state.

### 6.8 BindingState Shared Across Parent Chain

`BindingState.UpdateValue` walks the parent chain via `UpdatePropertyChangeProperty` (`BindingObject.cs:151-157`):

```csharp
protected void UpdatePropertyChangeProperty(View view, string fullProperty, object value)
{
    if (view.Parent != null)
        UpdatePropertyChangeProperty(view.Parent, fullProperty, value);
    else
        view.GetState().changeDictionary[fullProperty] = value;
}
```

This writes to the **root view's** `changeDictionary`, not the current view's. This means all changed property values accumulate at the root of the view tree, which is correct for hot reload state transfer (you want to capture everything). But it also means the root view's `changeDictionary` can grow unboundedly in a long-running app with many state changes. There's no mechanism to prune completed or outdated entries.

---

## 7. Recommendations

### For Comet Maintainers

| # | Flaw | Recommendation | Effort | Impact |
|---|------|----------------|--------|--------|
| 1 | **Implicit conversion trap** | Add a Roslyn analyzer that warns on `new Text($"...{state.Value}...")` patterns (non-lambda string interpolation with state reads). The analyzer should detect: (a) string interpolation expressions that contain `.Value` member access on `State<T>` types, and (b) multiple `.Value` reads flowing into a single `Binding<T>` parameter without a lambda wrapper. Severity: Warning. This single change would prevent the most common performance trap in Comet. | Medium | **Critical** |
| 2 | **Threading gaps** | Add `ThreadHelper.RunOnMainThread` dispatch in `StateManager.OnPropertyChanged` before calling `view.BindingPropertyChanged()`. This matches MauiReactor's guarantee and eliminates an entire category of platform crashes. Alternatively, add a debug-only thread assertion in `View.ViewPropertyChanged` that throws when called off the main thread, making the bug visible during development. The dispatch approach is safer; the assertion approach is lower-risk but less protective. | Medium | **High** |
| 3 | **Binding stabilization** | Add an option to disable stabilization per-binding (e.g., `new Binding<T>(func, stable: false)`), or detect when the set of read properties changes after stabilization and auto-destabilize. The latter can be done by always running `StartProperty`/`EndProperty` but only comparing results, not re-registering — if the new property set differs from the cached one, clear `_bindingStable` and re-register. The performance cost is one extra `StartProperty`/`EndProperty` cycle after stabilization, which is much cheaper than a missed update. | High | **High** |
| 4 | **No automatic coalescing** | Consider making `State<T>.Value` setter check `StateManager.IsBatching` and, if not batching, auto-start a micro-batch that flushes on the next main-thread dispatch (similar to React's automatic batching in v18+). Implementation: when `Value` is set and `IsBatching` is false, call `BeginBatch()`, post a `FlushBatch()` callback to `ThreadHelper.RunOnMainThread`, and set a flag to prevent double-flush. This would make all state mutations automatically coalesced within a single synchronous execution frame. | High | **Medium** |
| 5 | **Readonly enforcement** | Write a Roslyn analyzer for `State<T>` and `[State]`-attributed fields that aren't `readonly`. Emit a compile-time error instead of the runtime `ReadonlyRequiresException`. This is straightforward — match field declarations by type/attribute and check for `readonly` modifier. Ship as part of the `Comet.SourceGenerator` package so it's automatically available. | Low | **Medium** |
| 6 | **Dual storage** | Remove `dictionary[ValuePropertyName] = value` from the `State<T>` constructor, or update the dictionary in the `Value` setter. The former is safer since no code path should read State<T>'s "Value" from the dictionary. Add a comment explaining why the dictionary is not used for `State<T>.Value`. | Low | **Low** |
| 7 | **Static dictionaries** | Add periodic cleanup in `StateManager` that sweeps `NotifyToViewMappings` for entries where all views are disposed. Trigger on `FlushBatch()` or on a timer. Also consider using `ConditionalWeakTable` instead of `Dictionary<INotifyPropertyRead, HashSet<View>>` for `NotifyToViewMappings`, which would allow the GC to collect unused entries automatically. | Low | **Low** |
| 8 | **Re-entrancy guard** | Log when `_propertiesBeingUpdated` blocks a re-entrant update in debug builds, so developers can identify state feedback loops. Include the property name and the call stack in the log message. | Low | **Low** |
| 9 | **Hot reload state transfer** | Consider copying `_value` fields directly (via reflection on `State<T>` instances) in addition to `ChangedProperties`, to catch state that was set but never went through `UpdateValue`. Also consider adding a `_newComponent`-style forwarding chain (as MauiReactor does) for closures captured before hot reload. | Medium | **Medium** |
| 10 | **Debug diagnostics** | Add a `StateManager.DumpDependencyGraph()` method that returns a human-readable summary of all tracked views, their global properties, and their targeted bindings. This would be invaluable for debugging why a view is rebuilding too often or why a binding isn't updating. | Low | **High** |

### For Developers Using Comet

1. **Always use lambdas for dynamic text:** `new Text(() => $"Count: {count.Value}")`, never `new Text($"Count: {count.Value}")`. This is the single most impactful coding practice for Comet performance.

2. **Always wrap background state mutations:** `ThreadHelper.RunOnMainThread(() => state.Value = x)`. This applies to any code after an `await`, inside a `Task.Run`, or from timer/event callbacks that may fire on thread pool threads.

3. **Always read all possible dependencies in conditional lambdas:** In lambdas with conditional logic (ternary, if/else, switch), read all state values upfront before branching to avoid the binding stabilization trap:
   ```csharp
   // BAD — lastName dependency may be missed after stabilization
   new Text(() => showFirst.Value ? firstName.Value : lastName.Value)
   
   // GOOD — all dependencies read unconditionally
   new Text(() => {
       var show = showFirst.Value;
       var first = firstName.Value;
       var last = lastName.Value;
       return show ? first : last;
   })
   ```

4. **Prefer `Component<TState>.SetState()` over raw `State<T>.Value` assignment** — it handles batching, threading, and coalescing automatically. Use raw `State<T>` only when you need fine-grained targeted binding updates and understand the implications.

5. **Use `StateManager.BeginBatch()`/`EndBatch()`** when mutating multiple `State<T>` fields in sequence outside of a `Component.SetState` call:
   ```csharp
   StateManager.BeginBatch();
   try {
       firstName.Value = p.First;
       lastName.Value = p.Last;
       age.Value = p.Age;
   } finally {
       StateManager.EndBatch();
   }
   ```

6. **Mark all `State<T>` fields `readonly`** — the runtime check catches this, but a compile-time habit prevents the error entirely.

7. **Audit existing views for formatted-string bindings.** Search for patterns like `new Text($"` in your codebase and ensure each one either uses a lambda or doesn't reference state. The `Debug.WriteLine` warning in `BindToProperty` can help identify these at runtime if a debugger is attached.

8. **Profile with dependency graph awareness.** When a view seems to update too often, check whether its state reads are classified as global or local. A state change that triggers a full `Reload()` is dramatically more expensive than one that triggers a targeted `ViewPropertyChanged`.

### For Architecture Decisions

If the team is considering convergence between the two approaches:

- **Comet's `Component<TState>` already borrows from MauiReactor's model** — explicit `SetState()`, batching, main-thread dispatch. Consider making this the **recommended** pattern for new views, with raw `State<T>` reserved for performance-critical scenarios where fine-grained bindings are genuinely needed and the developer understands the tracking mechanics.

- **MauiReactor's reactive lambdas** are essentially what Comet's `Func<T>` bindings do, but without the implicit classification complexity. Consider documenting this parallel explicitly so developers can transfer mental models between frameworks.

- **Neither framework has keys for reconciliation.** This is a gap both should address for list-heavy UIs. A `Key(string)` method on Comet's `View` and MauiReactor's `VisualNode` that participates in the diff/merge algorithm would solve the reordering problem.

- **Consider a "strict mode" for Comet development** that disables the `IsValue` path entirely (all non-lambda bindings become immediate errors) and requires explicit lambdas for every dynamic binding. This would trade API convenience for safety during development, and could be controlled by a project-level setting.

- **The batching gap is the most impactful architectural difference.** MauiReactor's automatic coalescing via `_layoutCallEnqueued` is simple, effective, and always-on. Implementing something equivalent in Comet (auto-batch + flush on next dispatch) would eliminate a major class of performance issues without breaking existing code.

---

## 8. Decision Framework: When to Use Which Pattern

For teams that have access to both Comet-style `State<T>` and `Component<TState>` patterns (Comet supports both), here is a decision framework:

| Scenario | Recommended Pattern | Why |
|----------|-------------------|-----|
| Simple counter / toggle | `State<T>` with lambda bindings | Fine-grained updates are overkill but safe with lambdas; simplest code |
| Form with many fields | `Component<TState>` with `SetState` | Batching is automatic; avoids global-vs-local classification complexity |
| Data-dense dashboard (many labels updating independently) | `State<T>` with lambda bindings | Fine-grained targeted updates avoid O(n) reconciliation per change |
| List with reorderable items | Neither pattern handles this well | Both frameworks lack keyed reconciliation; use platform `CollectionView` |
| Background data fetching | `Component<TState>` with `SetState` | Guaranteed main-thread dispatch; no threading footgun |
| Animation-driven UI | `State<T>` with explicit batching | Fine-grained binding updates minimize layout work per frame |
| Shared state across views | `BindingObject` subclass with `[State]` | Comet's monitoring system handles multi-view dispatch automatically |
| Prototyping / learning | `Component<TState>` with `SetState` | Simpler mental model; fewer hidden pitfalls; closer to React/Flutter patterns |

**General rule:** Use `Component<TState>.SetState()` by default. Switch to `State<T>` only when profiling shows that full rebuilds are a bottleneck and you understand the binding classification rules.

---

## Appendix A: Source File Quick Reference

| File | Key Line Ranges | What to Look For |
|------|-----------------|------------------|
| `State.cs:17-22` | Constructor dual storage | `_value` and `dictionary` both written |
| `State.cs:29-48` | Value getter/setter | Read tracking, equality check, change notification |
| `BindingObject.cs:43-50` | `GetProperty<T>` | Dictionary-based read with `CallPropertyRead` |
| `BindingObject.cs:66-79` | `SetProperty<T>` | Dictionary write with equality check |
| `BindingObject.cs:86-88` | `CallPropertyChanged` | Routes through `StateManager.OnPropertyChanged` |
| `Binding.cs:75-94` | `implicit operator Binding<T>(T)` | The three-path classification logic |
| `Binding.cs:156-237` | `BindToProperty` | IsValue branch with formatted-string detection |
| `Binding.cs:238-266` | `BindingValueChanged` | Batching deferral for Func bindings |
| `Binding.cs:268-337` | `EvaluateAndNotify` | Stabilization logic (`_bindingStable`) |
| `StateManager.cs:18-32` | Static dictionaries | All four tracking dictionaries + lock |
| `StateManager.cs:34-98` | Batching infrastructure | `BeginBatch`/`EndBatch`/`FlushBatch` |
| `StateManager.cs:244-273` | `CheckForStateAttributes` | Readonly enforcement, field discovery |
| `StateManager.cs:384-496` | `OnPropertyChanged` | Multi-view dispatch with ArrayPool |
| `View.cs:267-268` | `Reload` | No thread dispatch |
| `View.cs:418-443` | `BindingPropertyChanged` | Three-way routing (global/binding/fallback) |
| `View.cs:494-533` | `ViewPropertyChanged` | Re-entrancy guard, no thread dispatch |
| `View.cs:549-558` | `SetGlobalEnvironment` | `RunOnMainThread` dispatch |
| `View.cs:1025` | `IHotReloadableView.Reload` | `RunOnMainThread` dispatch |
| `Component.cs:102-122` | `SetState` | Batching + `RunOnMainThread` |

---

## Appendix B: Glossary

| Term | Framework | Definition |
|------|-----------|------------|
| **Global property** | Comet | A state property read during `Body` evaluation. Changes trigger a full view `Reload()`. |
| **Bound property** | Comet | A state property read inside a `Func<T>` binding. Changes trigger targeted `ViewPropertyChanged`. |
| **Binding stabilization** | Comet | Optimization where a binding stops re-tracking dependencies after they stabilize. |
| **Monitoring view** | Comet | The view whose `Body` created a binding (`Binding.BoundFromView`). |
| **Target view** | Comet | The control that displays a binding's value (`Binding.View`). |
| **SetState** | Both | Explicit state mutation API. In Comet: `Component<TState>.SetState()`. In MauiReactor: `Component<S>.SetState()`. |
| **Reconciliation** | Both | The process of comparing old and new view trees and transferring native controls. |
| **Reactive lambda** | MauiReactor | A `Func<T>` property value that re-evaluates on every `SetState`, bypassing full `Render()`. |
| **Layout cycle** | MauiReactor | A single pass through the visual tree that applies invalidation, re-renders components, and reconciles. |
| **VisualNode** | MauiReactor | Base class for all nodes in the shadow tree (equivalent to Comet's `View`). |
| **Parameters** | MauiReactor | Global shared state via `IParameter<T>`, keyed by type name, with weak-ref observer tracking. |
| **Forwarding chain** | MauiReactor | The `_newComponent` linked list that routes old `SetState` closures to the current live component. |

---

## Appendix C: Bug Hunting Checklist

When investigating a UI interaction bug in Comet, check these in order:

- [ ] **Search for `$"` patterns in `new Text(`, `new Button(`, etc.** — formatted string bindings without lambdas cause full rebuilds
- [ ] **Check all `.Value =` assignments** — are any on background threads (after `await`, in `Task.Run`, from timer callbacks)?
- [ ] **Look for conditional lambda bindings** — does the `Func<T>` contain ternary/if logic that changes which state properties are read?
- [ ] **Check for missing batching** — are multiple `State<T>.Value` assignments happening in sequence without `BeginBatch()`/`EndBatch()`?
- [ ] **Check disposal** — is the view being properly disposed when removed from the tree? (`StateManager.Disposing` must run)
- [ ] **Check for non-readonly `State<T>` fields** — runtime exception should catch this, but check anyway
- [ ] **Enable Debug.WriteLine monitoring** — the "Warning: ... using formatted Text" and "using Multiple state Variables" messages identify silent global fallbacks
- [ ] **Use `StateManager.IsBatching` breakpoint** — verify that rapid mutations are being batched as expected

---

**End of analysis.**
