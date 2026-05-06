# Comet State Management v2 — Engineering Design Proposal

> **Status:** Draft (Rev 4 — incorporates third skeptic review)  
> **Author:** Comet Team  
> **Target:** .NET 10 / MAUI 10 / C# 13  
> **Scope:** Replace `State<T>`, `Binding<T>`, and `StateManager` with a signal-based reactive system  
> **Reviews:** [Skeptic R1](../research/state-management-proposal-skeptic-review.md) | [Skeptic R2](../research/state-management-proposal-skeptic-review-r2.md) | [Skeptic R3](../research/state-management-proposal-skeptic-review-r3.md)

---

## Table of Contents

1. [Design Goals](#1-design-goals)
2. [Problems Solved](#2-problems-solved)
3. [Core Design](#3-core-design)
4. [Detailed API Design](#4-detailed-api-design)
5. [Update Pipeline](#5-update-pipeline)
6. [Hot Reload Integration](#6-hot-reload-integration)
7. [Migration Path](#7-migration-path)
8. [Performance Comparison](#8-performance-comparison)
9. [Open Questions](#9-open-questions)

---

## 1. Design Goals

In priority order:

1. **Correctness** — No silent state classification errors. No threading bugs. Every reactive
   read/write has deterministic, documented behavior. The system is impossible to put into a
   state where a UI update is silently dropped.

2. **Performance** — Minimal rebuilds. Fine-grained targeted updates by default. A signal
   change that feeds only a `Text` label should update only that label, never rebuild the
   parent view tree.

3. **Developer experience** — Hard to misuse. The compiler catches errors that the current
   system discovers at runtime (or not at all). The API makes the "right thing" the easy thing
   and the "wrong thing" a compilation error.

4. **Hot reload** — State preservation across code changes. Signals are identity-stable and
   transferable. The new system must be at least as good as the current system's hot reload
   support.

5. **Debuggability** — Clear state flow. A developer can inspect which signals a view depends
   on, see the dependency graph, and understand why a rebuild happened. Diagnostic events are
   built-in, not bolted on.

---

## 2. Problems Solved

### P1: The Implicit Conversion Trap

**Current behavior:** `Binding<T>` has an `implicit operator Binding<T>(T value)` that
silently decides at runtime whether a raw value becomes a targeted binding or a global
property. The decision depends on how many properties were read during implicit conversion
and whether the resulting value equals the state's current value:

```csharp
// Binding.cs lines 76-93
public static implicit operator Binding<T>(T value)
{
    var props = StateManager.EndProperty();
    if (props?.Count > 1)
    {
        // Multiple reads → global property → full Reload on any change
        StateManager.CurrentView.GetState().AddGlobalProperties(props);
    }
    else if (props?.Count == 1 && props[0].BindingObject is State<T> state)
    {
        // Single State<T> read that matches type → targeted binding
        return state;
    }
    // Fall through: IsValue = true, may be reclassified later in BindToProperty
    return new Binding<T>() { IsValue = true, CurrentValue = value, ... };
}
```

This means `Text(state.Value)` becomes targeted, but `Text($"{state.Value}")` becomes a
global property triggering full rebuilds — and the developer has no way to know this without
reading the framework source code.

**New design:** Signals eliminate implicit conversion entirely. Controls accept either
`Signal<T>` (for direct two-way binding) or `Func<T>` (for computed values). There is no
implicit `T → Binding<T>` conversion. The compiler tells you which path you're on.

### P2: Fragile Global vs Local Classification

**Current behavior:** `BindToProperty` in `Binding.cs` (lines 157-236) runs a complex
heuristic: if `IsValue` and `BoundProperties.Count == 1`, it checks whether the tracked
value equals `CurrentValue` via `EqualityComparer`. If they match → targeted binding. If
they don't match (e.g., string interpolation happened between the read and the assignment) →
global property with a Debug.WriteLine warning. Multiple bound properties → always global.

This means the same developer code can behave differently depending on:
- The runtime value of the state at build time
- Whether C# string interpolation ran between the property read and the implicit conversion
- Whether the Func<T> lambda captures one or many state properties

**New design:** Classification is structural, not runtime-heuristic. A `Func<T>` always
creates a `Computed<T>` with explicit dependency tracking. A raw `Signal<T>` reference is
always a direct binding. There is no "maybe targeted, maybe global" path.

### P3: Threading Unsafety

**Current behavior:** `StateManager.OnPropertyChanged` (line 384) dispatches to
`View.BindingPropertyChanged` which calls either `Reload()` or `ViewPropertyChanged` —
neither of which guarantees UI thread execution. The only thread safety comes from
`ReaderWriterLockSlim` around the mapping dictionaries, not around the actual UI updates.
`View.Reload()` calls `ResetView()` which manipulates the view tree directly.

The `Component<TState>.SetState()` does dispatch to the main thread, but raw `State<T>.Value`
assignments from background threads go through `BindingObject.CallPropertyChanged` →
`StateManager.OnPropertyChanged` without any thread hop.

**New design:** `Signal<T>.Value` setter always dispatches the notification pipeline to the
UI thread. The signal value can be set from any thread (the `StrongBox<T>` swap inside a
lock guarantees no torn reads for any type), and the notification is always coalesced and
dispatched on the main thread.

### P4: No Automatic Coalescing

**Current behavior:** Multiple rapid `State<T>.Value` assignments trigger multiple
`OnPropertyChanged` → `Reload()` calls unless manually wrapped in
`StateManager.BeginBatch()`/`EndBatch()`. The `Component<TState>.SetState()` method wraps in
a batch, but direct `state.Value = x` does not.

```csharp
// This triggers THREE separate Reload() calls:
count.Value = 1;
name.Value = "Alice";
age.Value = 30;

// This triggers ONE Reload() — but requires manual wrapping:
StateManager.BeginBatch();
count.Value = 1;
name.Value = "Alice";
age.Value = 30;
StateManager.EndBatch();
```

**New design:** Every signal mutation marks the owning view(s) dirty. Dirty views are
flushed in a single microtask posted to `Dispatcher.Dispatch()`. Multiple mutations within
the same synchronous execution frame are automatically coalesced. No manual batching API
needed (though an explicit `batch()` scope is provided for advanced scenarios like
cross-await coalescing).

### P5: Binding Stabilization Limitation

**Current behavior:** `Binding<T>.EvaluateAndNotify` (lines 272-290) tracks whether the
set of read properties changes between evaluations. Once the set stabilizes,
`_bindingStable = true` and subsequent evaluations skip `StartProperty()`/`EndProperty()`.
This is a performance optimization, but it means **dynamic dependencies are silently
dropped**:

```csharp
readonly State<bool> showDetails = false;
readonly State<string> summary = "Brief";
readonly State<string> details = "Full details here";

// This binding's dependency set CHANGES based on showDetails.Value:
new Text(() => showDetails.Value ? details.Value : summary.Value)
```

After the first few evaluations, if `showDetails` was `false`, the binding stabilizes with
dependencies `[showDetails, summary]`. When `showDetails` later becomes `true`, the binding
correctly re-evaluates (because `showDetails` is tracked), but `details` is now read without
tracking. If only `details.Value` changes afterward, the binding won't update.

**New design:** `Computed<T>` always tracks dependencies on every evaluation. The
performance cost is mitigated by using a lightweight bitset-based tracking system instead of
`StartProperty()`/`EndProperty()` with list allocations. There is no stabilization shortcut
that drops dependencies.

### P6: Static Global State

**Current behavior:** `StateManager` is entirely static with six static dictionaries:

```csharp
static Dictionary<string, List<INotifyPropertyRead>> ViewObjectMappings;
static Dictionary<INotifyPropertyRead, HashSet<View>> NotifyToViewMappings;
static Dictionary<INotifyPropertyChanged, Dictionary<string, string>> ChildPropertyNamesMapping;
static List<INotifyPropertyRead> MonitoredObjects;
static readonly List<Binding> _dirtyBindings;
static readonly HashSet<View> _viewsNeedingReload;
```

This creates: (a) memory leak risk if views aren't properly disposed, (b) lock contention
under the single `ReaderWriterLockSlim`, and (c) implicit coupling between all views in the
application.

**New design:** Each `View` owns a `ReactiveScope` that tracks its signal subscriptions.
There is no global mapping dictionary. Signals maintain a list of weak subscribers. When a
view is disposed, its scope unsubscribes from all signals. No global lock needed.

### P7: Runtime-Only Readonly Enforcement

**Current behavior:** `CheckForStateAttributes` uses reflection to find `State<T>` fields
and throws `ReadonlyRequiresException` if `!field.IsInitOnly`. This check runs at view
construction time — a runtime error that could surface late in development or only on
specific code paths.

```csharp
// StateManager.cs line 259
if (!field.IsInitOnly)
{
    throw new ReadonlyRequiresException(field.DeclaringType?.FullName, field.Name);
}
```

**New design:** A Roslyn analyzer ships with the NuGet package and reports a **compile-time
error** for non-readonly `Signal<T>`, `Computed<T>`, or `Effect` fields. The runtime check
remains as defense-in-depth but should never fire.

---

## 3. Core Design

The new system is built on three primitives borrowed from the signals model (SolidJS, Preact
Signals, Angular Signals) adapted for .NET and Comet's architecture:

| Primitive | Purpose | Replaces |
|-----------|---------|----------|
| `Signal<T>` | Mutable reactive value | `State<T>` |
| `Computed<T>` | Derived reactive value (lazy, cached) | `Func<T>` bindings with implicit tracking |
| `Effect` | Side-effect that runs when dependencies change | Global property tracking + `Reload()` |

### 3.A Explicit Reactive Signals

```csharp
public class Counter : View
{
    readonly Signal<int> count = new(0);
    readonly Signal<string> name = new("World");

    // Computed: derived state, re-evaluated only when dependencies change
    readonly Computed<string> greeting;

    public Counter()
    {
        greeting = new(() => $"Hello {name.Value}, count is {count.Value}");
    }

    [Body]
    View body() => new VStack
    {
        // Lambda binding — Computed<T> is created implicitly by the generated control
        new Text(() => $"Count: {count.Value}"),

        // Direct signal binding — two-way, targeted
        new TextField(name),

        // Computed binding — read-only, targeted
        new Text(greeting),

        new Button("Increment", () => count.Value++),
    };
}
```

### 3.B Always-Lambda Binding

Controls accept `Func<T>` or `Signal<T>`, never raw `T` for reactive properties:

```csharp
// ✅ Lambda — always tracked, creates Computed<T> internally
new Text(() => $"Count: {count.Value}")

// ✅ Direct Signal — always two-way bound, targeted updates
new TextField(name)

// ✅ Explicit Computed — pre-computed derived value
new Text(greeting)

// ✅ Static value — explicitly non-reactive
new Text("Hello, static world")

// ❌ REMOVED: No implicit T → Binding<T> conversion
// new Text(count.Value)  // This was the trap. Now it's a compile error
//                         // because Text(string) is non-reactive and
//                         // Text(Func<string>) requires a lambda.
```

The source generator for controls emits overloads that make this natural:

```csharp
// Generated control constructor overloads:
public Text(string staticValue);                    // Non-reactive
public Text(Func<string> computed);                 // Reactive computed
public Text(Signal<string> signal);                 // Reactive direct
public Text(Computed<string> computed);              // Reactive pre-computed
```

### 3.C Automatic Coalescing via Microtask Scheduling

```csharp
count.Value = 1;   // marks owner views dirty, posts microtask if not already posted
count.Value = 2;   // already dirty, no additional post
name.Value = "X";  // marks owner views dirty (may be same or different views)
// → at end of current dispatch: single flush processes all dirty computeds and views
```

Implementation uses `Dispatcher.Dispatch()` to post a single flush callback. A boolean flag
(`_flushScheduled`) prevents multiple posts. This mirrors MauiReactor's
`_layoutCallEnqueued` pattern.

### 3.D Thread-Safe by Default

```csharp
// Safe from any thread, for ANY value type (including decimal, Matrix4x4, etc.):
Task.Run(() =>
{
    count.Value = 42;  // Lock protects equality check + box swap + notify as an atomic unit
                       // Getter reads volatile StrongBox ref — always consistent, never torn
                       // Flush is dispatched to UI thread automatically
});
```

The `Signal<T>` stores its value inside a `volatile StrongBox<T>`. The setter acquires a
per-signal lock to ensure the equality check, box swap, and subscriber notification are
atomic. The getter reads the `volatile` `StrongBox<T>` reference — this is a pointer-sized
atomic read on all platforms, guaranteeing no torn reads even for large value types like
`decimal` or `Matrix4x4`. Cost: one `StrongBox<T>` allocation per value change — negligible
for UI state mutations (typically single-digit per user interaction).

### 3.E Scoped Tracking Context

```
┌─────────────────────────────────────┐
│ View (has ReactiveScope)            │
│                                     │
│  Signal<int> count ──────┐          │
│  Signal<string> name ────┤          │
│                          ▼          │
│  Computed<string> greeting          │
│       ↕ subscribes to count, name   │
│                                     │
│  Body lambda                        │
│       ↕ Effect that rebuilds view   │
│         when dependencies change    │
└─────────────────────────────────────┘
```

No global `StateManager` dictionaries. Each `ReactiveScope` is a lightweight container that
owns the subscriptions between signals and their dependents. Disposal is automatic when the
view is disposed.

### 3.F Roslyn Analyzer for Signal Fields

Ships as part of the Comet NuGet package (alongside the existing source generator):

```csharp
// COMET001: Signal field must be readonly
readonly Signal<int> count = new(0);    // ✅ OK
Signal<int> count = new(0);             // ❌ Error COMET001

// COMET002: Computed field must be readonly
readonly Computed<string> label;        // ✅ OK
Computed<string> label;                 // ❌ Error COMET002

// COMET003: Avoid reading Signal.Value outside reactive context
void SomeMethod()
{
    var x = count.Value;  // ⚠️ Warning COMET003: Reading Signal.Value outside
                          //    a Computed, Effect, or Body lambda. This read
                          //    is not tracked. Use count.Peek() for intentional
                          //    untracked reads.
}
```

---

## 4. Detailed API Design

### 4.1 Signal\<T\>

The fundamental mutable reactive primitive. Replaces `State<T>`.

```csharp
namespace Comet.Reactive;

/// <summary>
/// A mutable reactive value. Reading <see cref="Value"/> inside a tracking context
/// (Computed, Effect, or Body build) registers a dependency. Writing <see cref="Value"/>
/// notifies all dependents and schedules a UI flush.
/// </summary>
public sealed class Signal<T> : IReactiveSource, IDisposable
{
    // Value is stored in a StrongBox to ensure atomicity for ALL types.
    // For reference types and small value types, pointer-sized writes are already
    // atomic. For large value types (decimal, Matrix4x4, custom structs > 8 bytes),
    // a naked field read/write can tear. By boxing into a reference-typed container,
    // both reads and writes are pointer-atomic (the reference swap is atomic).
    // Cost: one extra allocation per value change — negligible for UI state mutations.
    private volatile StrongBox<T> _box;
    private readonly EqualityComparer<T> _comparer;
    private readonly SubscriberList _subscribers = new();
    private readonly object _writeLock = new();
    private bool _disposed;

    // Monotonically increasing version number — incremented on every write.
    // Used by Computed<T> to know if a dependency has changed without re-reading the value.
    private uint _version;

    public Signal(T initialValue, EqualityComparer<T>? comparer = null)
    {
        _box = new StrongBox<T>(initialValue);
        _comparer = comparer ?? EqualityComparer<T>.Default;
        _version = 0;
    }

    /// <summary>
    /// Get or set the reactive value. Reading inside a tracking context registers a
    /// dependency. Setting notifies dependents if the value changed.
    /// Thread-safe for ALL types including large value types: reads are atomic
    /// (volatile reference read of StrongBox), writes acquire a lock for the
    /// equality check + box swap + notification.
    /// </summary>
    public T Value
    {
        get
        {
            // Register dependency if we're inside a tracking context
            ReactiveScope.Current?.TrackRead(this);
            // Volatile read of _box gives us a consistent snapshot — no torn reads
            // even for Signal<decimal>, Signal<Matrix4x4>, etc.
            return _box.Value;
        }
        set
        {
            lock (_writeLock)
            {
                if (_comparer.Equals(_box.Value, value))
                    return;

                // Swap the box atomically (reference assignment is atomic).
                // Any concurrent reader sees either the old box or the new box,
                // never a partially-written value.
                _box = new StrongBox<T>(value);
                unchecked { _version++; }

                // Notify all subscribers — this marks Computeds dirty and
                // schedules a UI flush on the dispatcher.
                _subscribers.NotifyAll(this);
            }

            ReactiveDiagnostics.NotifySignalChanged(this, DebugName);

            // Schedule flush outside the lock — avoids holding _writeLock
            // while potentially acquiring ReactiveScheduler._lock.
            ReactiveScheduler.EnsureFlushScheduled();
        }
    }

    /// <summary>
    /// Read the value WITHOUT registering a dependency. Use when you need the current
    /// value for a one-time computation that should not create a reactive subscription.
    /// Thread-safe (same volatile StrongBox read as Value getter).
    /// </summary>
    public T Peek() => _box.Value;

    /// <summary>
    /// Current version number. Incremented on every value change.
    /// </summary>
    public uint Version => _version;

    // --- IReactiveSource ---

    public void Subscribe(IReactiveSubscriber subscriber)
    {
        if (_disposed) return;
        _subscribers.Add(subscriber);
    }

    public void Unsubscribe(IReactiveSubscriber subscriber)
    {
        _subscribers.Remove(subscriber);
    }

    public void Dispose()
    {
        _disposed = true;
        _subscribers.Clear();
    }

    // --- Implicit conversion for ergonomic field initialization ---
    // NOTE: Only intended for field initializers (readonly Signal<int> count = 0).
    // Not intended for method arguments — overload resolution naturally prefers
    // exact-match overloads (e.g., Text(string)) over implicit conversions.

    public static implicit operator Signal<T>(T value) => new Signal<T>(value);

    // --- INotifyPropertyRead bridge for backward compatibility during migration ---

    // (Omitted from this section — see §7 Migration Path)

    public override string ToString() => _box.Value?.ToString() ?? "";
}
```

### 4.2 Computed\<T\>

A lazy, cached derived value. Re-evaluates only when at least one dependency has changed.

```csharp
namespace Comet.Reactive;

/// <summary>
/// A derived reactive value computed from a function. Dependencies are automatically
/// tracked. The value is lazily re-evaluated when a dependency changes and the
/// Computed is next read.
/// </summary>
public sealed class Computed<T> : IReactiveSource, IReactiveSubscriber, IDisposable
{
    private readonly Func<T> _compute;
    private readonly EqualityComparer<T> _comparer;
    private T _cachedValue;
    private bool _dirty;
    private uint _version;

    // Dependencies discovered during the last evaluation
    private HashSet<IReactiveSource> _dependencies;
    // Snapshot of dependency versions at last evaluation
    private Dictionary<IReactiveSource, uint> _depVersions;

    private readonly SubscriberList _subscribers = new();

    public Computed(Func<T> compute, EqualityComparer<T>? comparer = null)
    {
        _compute = compute ?? throw new ArgumentNullException(nameof(compute));
        _comparer = comparer ?? EqualityComparer<T>.Default;
        _dirty = true; // needs initial evaluation
    }

    public T Value
    {
        get
        {
            // Register dependency if we're inside another tracking context
            ReactiveScope.Current?.TrackRead(this);

            if (_dirty)
                Evaluate();

            return _cachedValue;
        }
    }

    /// <summary>
    /// Read without registering a dependency.
    /// Forces evaluation if dirty.
    /// </summary>
    public T Peek()
    {
        if (_dirty)
            Evaluate();
        return _cachedValue;
    }

    public uint Version => _version;

    private void Evaluate()
    {
        // Keep old dependencies subscribed during evaluation — this prevents
        // missed notifications if a dependency fires while we're evaluating.
        // (Skeptic review R1: unsubscribe-before-eval loses notifications.)
        var oldDeps = _dependencies;

        // Clear _dirty BEFORE evaluation so that if a dependency fires
        // during _compute() (concurrent write), OnDependencyChanged can
        // re-mark us dirty. If we left _dirty=true, the guard in
        // OnDependencyChanged would swallow the re-notification.
        // (Skeptic review R2: reentrant dirtying during evaluation.)
        _dirty = false;

        // Start tracking reads
        using var scope = ReactiveScope.BeginTracking();
        T newValue;
        HashSet<IReactiveSource> newDeps;
        try
        {
            newValue = _compute();
        }
        catch
        {
            // On exception, discard partial reads, restore dirty so the
            // next access re-evaluates. Don't update subscriptions.
            _dirty = true;
            newDeps = scope.EndTracking(); // discard
            return;
        }

        newDeps = scope.EndTracking();

        // Diff-based subscription update: unsubscribe from removed deps,
        // subscribe to added deps. Deps that stayed the same keep their
        // subscriptions — no gap for notifications to fall through.
        if (oldDeps != null)
        {
            foreach (var dep in oldDeps)
            {
                if (!newDeps.Contains(dep))
                    dep.Unsubscribe(this);
            }
        }
        foreach (var dep in newDeps)
        {
            if (oldDeps == null || !oldDeps.Contains(dep))
                dep.Subscribe(this);
        }

        // Update stored deps and version snapshots
        _dependencies = newDeps;
        _depVersions = new Dictionary<IReactiveSource, uint>(newDeps.Count);
        foreach (var dep in newDeps)
            _depVersions[dep] = dep.Version;

        // If we were re-dirtied during evaluation (concurrent notification),
        // don't update the cached value — it's already stale. Leave _dirty=true
        // so the next read re-evaluates.
        if (_dirty)
            return;

        if (!_comparer.Equals(_cachedValue, newValue))
        {
            _cachedValue = newValue;
            unchecked { _version++; }
            _subscribers.NotifyAll(this);
            ReactiveScheduler.EnsureFlushScheduled();
        }
    }

    // --- IReactiveSubscriber ---

    /// <summary>
    /// Called when a dependency notifies that it changed.
    /// Marks this Computed dirty — actual re-evaluation is deferred until Value is read.
    /// </summary>
    public void OnDependencyChanged(IReactiveSource source)
    {
        if (_dirty)
            return; // already dirty, nothing to do

        _dirty = true;

        // Propagate dirty to our own subscribers (other Computeds or Effects)
        _subscribers.NotifyAll(this);
        ReactiveScheduler.EnsureFlushScheduled();
    }

    // --- IReactiveSource ---

    public void Subscribe(IReactiveSubscriber subscriber) => _subscribers.Add(subscriber);
    public void Unsubscribe(IReactiveSubscriber subscriber) => _subscribers.Remove(subscriber);

    public void Dispose()
    {
        if (_dependencies != null)
        {
            foreach (var dep in _dependencies)
                dep.Unsubscribe(this);
            _dependencies = null;
        }
        _subscribers.Clear();
    }
}
```

### 4.3 Effect

A side-effect that runs when its tracked dependencies change. Used internally by the
framework for wiring Body lambdas, and available to developers for imperative side-effects.

```csharp
namespace Comet.Reactive;

/// <summary>
/// An imperative side-effect that re-executes when its dependencies change.
/// Dependencies are automatically tracked by reading Signal/Computed values
/// inside the effect function.
/// </summary>
public sealed class Effect : IReactiveSubscriber, IDisposable
{
    private readonly Action _execute;
    private HashSet<IReactiveSource> _dependencies;
    private bool _dirty;
    private bool _disposed;

    public Effect(Action execute, bool runImmediately = true)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        if (runImmediately)
            Run();
    }

    /// <summary>
    /// Execute the effect, tracking all reactive reads as dependencies.
    /// Uses diff-based subscription update (same as Computed) to avoid
    /// missed notifications during execution.
    /// </summary>
    public void Run()
    {
        if (_disposed)
            return;

        var oldDeps = _dependencies;

        // Track reads during execution — using var ensures RestorePrevious
        // is called even if _execute() throws (IDisposable pattern).
        using var scope = ReactiveScope.BeginTracking();
        HashSet<IReactiveSource> newDeps;
        try
        {
            _execute();
        }
        catch
        {
            // Discard partial reads on exception. Keep the previous dependency
            // set unchanged, and clear _dirty so the next dependency change can
            // re-queue the effect. Otherwise a single transient failure would
            // wedge the effect in a dirty-but-not-scheduled state.
            scope.EndTracking();
            _dirty = false;
            return;
        }

        newDeps = scope.EndTracking();

        // Diff-based subscription update
        if (oldDeps != null)
        {
            foreach (var dep in oldDeps)
            {
                if (!newDeps.Contains(dep))
                    dep.Unsubscribe(this);
            }
        }
        foreach (var dep in newDeps)
        {
            if (oldDeps == null || !oldDeps.Contains(dep))
                dep.Subscribe(this);
        }

        _dependencies = newDeps;
        _dirty = false;
    }

    // --- IReactiveSubscriber ---

    public void OnDependencyChanged(IReactiveSource source)
    {
        if (_dirty || _disposed)
            return;

        _dirty = true;
        ReactiveScheduler.ScheduleEffect(this);
    }

    internal void Flush()
    {
        if (!_dirty || _disposed)
            return;
        Run();
    }

    public void Dispose()
    {
        _disposed = true;
        if (_dependencies != null)
        {
            foreach (var dep in _dependencies)
                dep.Unsubscribe(this);
            _dependencies = null;
        }
    }
}
```

### 4.4 Reactive Infrastructure

```csharp
namespace Comet.Reactive;

/// <summary>
/// Contract for reactive values that can be subscribed to.
/// </summary>
public interface IReactiveSource
{
    void Subscribe(IReactiveSubscriber subscriber);
    void Unsubscribe(IReactiveSubscriber subscriber);
    uint Version { get; }
}

/// <summary>
/// Contract for objects that react to dependency changes.
/// </summary>
public interface IReactiveSubscriber
{
    void OnDependencyChanged(IReactiveSource source);
}

/// <summary>
/// Tracking context for automatic dependency discovery.
/// Tracking context for automatic dependency discovery.
/// Uses [ThreadStatic] for the current scope — no global lock needed.
///
/// IMPORTANT: Scopes are thread-local. Signal/Computed reads on background threads
/// (e.g., inside Task.Run) see Current == null and are silently untracked. This is
/// by design — reactive graphs should be built on the UI thread. Use Peek() for
/// intentional untracked reads from background threads.
/// </summary>
public sealed class ReactiveScope : IDisposable
{
    [ThreadStatic]
    private static ReactiveScope? _current;

    public static ReactiveScope? Current => _current;

    private readonly ReactiveScope? _previous;
    private readonly HashSet<IReactiveSource> _reads;

    private ReactiveScope(ReactiveScope? previous)
    {
        _previous = previous;
        _reads = new HashSet<IReactiveSource>();
    }

    /// <summary>
    /// Begin a new tracking context. All Signal/Computed reads will be recorded.
    /// </summary>
    public static ReactiveScope BeginTracking()
    {
        var scope = new ReactiveScope(_current);
        _current = scope;
        return scope;
    }

    /// <summary>
    /// Record a read from a reactive source.
    /// </summary>
    public void TrackRead(IReactiveSource source)
    {
        _reads.Add(source);
    }

    /// <summary>
    /// End tracking and return the set of sources that were read.
    /// </summary>
    public HashSet<IReactiveSource> EndTracking()
    {
        return _reads;
    }

    /// <summary>
    /// Restore the previous scope as current. Prefer the `using var` pattern
    /// (`using var scope = BeginTracking()`) which calls Dispose() automatically
    /// on all exit paths including exceptions.
    /// </summary>
    public static void RestorePrevious(ReactiveScope scope)
    {
        _current = scope._previous;
    }

    public void Dispose()
    {
        if (_current == this)
            _current = _previous;
    }
}

/// <summary>
/// Subscriber list using weak references. Subscribers that have been
/// garbage-collected are automatically pruned during notification.
/// This is a class (not struct) to avoid defensive-copy bugs and null _lock
/// issues when the parameterless constructor is skipped via default(T).
/// </summary>
internal sealed class SubscriberList
{
    // Using a simple array + count for low subscriber counts (typical: 1-4).
    // For high subscriber counts, switch to a linked list.
    private WeakReference<IReactiveSubscriber>[]? _items;
    private int _count;
    private readonly object _lock = new();

    public void Add(IReactiveSubscriber subscriber)
    {
        lock (_lock)
        {
            _items ??= new WeakReference<IReactiveSubscriber>[4];
            if (_count == _items.Length)
                Array.Resize(ref _items, _items.Length * 2);
            _items[_count++] = new WeakReference<IReactiveSubscriber>(subscriber);
        }
    }

    public void Remove(IReactiveSubscriber subscriber)
    {
        lock (_lock)
        {
            if (_items == null) return;
            for (int i = 0; i < _count; i++)
            {
                if (_items[i].TryGetTarget(out var target) && ReferenceEquals(target, subscriber))
                {
                    _items[i] = _items[--_count];
                    _items[_count] = null!;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Notify all subscribers that a dependency changed. The <paramref name="source"/>
    /// is the reactive source that triggered the notification, enabling subscribers
    /// to identify which dependency fired and produce diagnostic logs.
    /// </summary>
    public void NotifyAll(IReactiveSource source)
    {
        IReactiveSubscriber[]? snapshot;
        int snapshotCount;

        lock (_lock)
        {
            if (_items == null || _count == 0) return;
            snapshot = ArrayPool<IReactiveSubscriber>.Shared.Rent(_count);
            snapshotCount = 0;
            int writeIdx = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_items[i].TryGetTarget(out var target))
                {
                    snapshot[snapshotCount++] = target;
                    _items[writeIdx++] = _items[i]; // compact
                }
                // else: GC'd subscriber, skip
            }
            _count = writeIdx; // prune dead entries
        }

        try
        {
            for (int i = 0; i < snapshotCount; i++)
                snapshot[i].OnDependencyChanged(source);
        }
        finally
        {
            ArrayPool<IReactiveSubscriber>.Shared.Return(snapshot, clearArray: true);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items = null;
            _count = 0;
        }
    }
}
```

### 4.5 ReactiveScheduler — Microtask Coalescing

```csharp
namespace Comet.Reactive;

/// <summary>
/// Coalesces reactive notifications into a single UI flush per dispatch cycle.
/// This is the equivalent of MauiReactor's _layoutCallEnqueued pattern.
/// </summary>
public static class ReactiveScheduler
{
    // Volatile ensures the outer check in EnsureFlushScheduled is visible
    // across cores on weakly-ordered architectures (ARM = Android/iOS).
    private static volatile bool _flushScheduled;
    private static readonly HashSet<Effect> _dirtyEffects = new();
    private static readonly HashSet<View> _dirtyViews = new();
    private static readonly object _lock = new();

    /// <summary>
    /// Maximum recursive flush iterations before the scheduler breaks the cycle
    /// and logs a diagnostic. This prevents StackOverflowException when effects
    /// create write cycles (Effect A writes Signal X → Effect B writes Signal Y →
    /// Effect A ...). Modeled after SolidJS's 100-iteration cap.
    /// </summary>
    internal const int MaxFlushDepth = 100;

    /// <summary>
    /// Ensure a flush is scheduled on the UI thread dispatcher.
    /// Multiple calls before the flush executes are no-ops.
    /// </summary>
    public static void EnsureFlushScheduled()
    {
        if (_flushScheduled)
            return;

        lock (_lock)
        {
            if (_flushScheduled)
                return;
            _flushScheduled = true;
        }

        // Post to the MAUI dispatcher — this runs after the current synchronous
        // call stack unwinds, coalescing all mutations in the current frame.
        if (Application.Current?.Dispatcher is { } dispatcher)
        {
            dispatcher.Dispatch(FlushEntry);
        }
        else
        {
            // Fallback for unit tests or non-MAUI contexts
            ThreadHelper.RunOnMainThread(FlushEntry);
        }
    }

    internal static void ScheduleEffect(Effect effect)
    {
        lock (_lock)
        {
            _dirtyEffects.Add(effect);
        }
        EnsureFlushScheduled();
    }

    internal static void MarkViewDirty(View view)
    {
        lock (_lock)
        {
            _dirtyViews.Add(view);
        }
        EnsureFlushScheduled();
    }

    /// <summary>
    /// Entry point for dispatcher-posted flushes. Resets _flushScheduled first,
    /// then flushes with recursion depth tracking.
    /// </summary>
    private static void FlushEntry()
    {
        lock (_lock)
        {
            _flushScheduled = false;
        }

        Flush(depth: 0);
    }

    /// <summary>
    /// Flush all pending reactive updates. Runs on the UI thread.
    /// Order: (1) dirty Effects, (2) dirty Views.
    /// Recurses when effects/views produce cascading dirtiness, bounded by
    /// <see cref="MaxFlushDepth"/>.
    /// </summary>
    private static void Flush(int depth)
    {
        if (depth >= MaxFlushDepth)
        {
            // Fire the diagnostic event (works in both debug and release builds)
            ReactiveDiagnostics.NotifyFlushDepthWarning(depth);

            Debug.WriteLine(
                $"[Comet.Reactive] ReactiveScheduler exceeded {MaxFlushDepth} flush iterations. " +
                "This indicates a cycle in the reactive graph (effects writing signals that " +
                "trigger other effects in a loop). Breaking the cycle. UI may show stale data " +
                "until the next user interaction triggers a fresh flush.");

#if DEBUG
            throw new InvalidOperationException(
                $"Reactive graph cycle detected: exceeded {MaxFlushDepth} flush iterations. " +
                "Check for effects that write signals consumed by other effects in a loop.");
#endif

            lock (_lock)
            {
                _dirtyEffects.Clear();
                _dirtyViews.Clear();
            }
            return;
        }

        Effect[] effects;
        View[] views;

        lock (_lock)
        {
            effects = _dirtyEffects.Count > 0
                ? _dirtyEffects.ToArray()
                : Array.Empty<Effect>();
            _dirtyEffects.Clear();

            views = _dirtyViews.Count > 0
                ? _dirtyViews.ToArray()
                : Array.Empty<View>();
            _dirtyViews.Clear();
        }

        // Phase 1: Flush effects (may produce additional dirty views)
        foreach (var effect in effects)
            effect.Flush();

        // Phase 2: Reload dirty views
        foreach (var view in views)
        {
            if (!view.IsDisposed)
            {
                view.Reload();
                ReactiveDiagnostics.NotifyViewRebuilt(view, trigger: null);
            }
        }

        // If effects or view reloads dirtied more things, flush again
        bool hasMore;
        lock (_lock)
        {
            hasMore = _dirtyEffects.Count > 0 || _dirtyViews.Count > 0;
        }
        if (hasMore)
            Flush(depth + 1);
    }

    /// <summary>
    /// Synchronously flush all pending reactive updates. Use in event handlers
    /// that need native UI to reflect the latest state immediately (e.g., before
    /// measuring a control or triggering an animation). Also useful in unit tests.
    /// </summary>
    /// <remarks>
    /// In most cases you should NOT call this — the automatic microtask coalescing
    /// produces fewer rebuilds. Only call FlushSync when you have a concrete need
    /// to observe updated native views within the same synchronous call stack.
    ///
    /// UI thread only. Background-thread mutations should rely on the normal
    /// dispatcher-posted flush path (`EnsureFlushScheduled()`).
    /// </remarks>
    public static void FlushSync()
    {
        if (Application.Current?.Dispatcher is { } dispatcher && dispatcher.IsDispatchRequired)
        {
            throw new InvalidOperationException(
                "FlushSync must be called on the UI thread. Background-thread mutations " +
                "should rely on the automatic dispatcher-posted flush.");
        }

        lock (_lock)
        {
            _flushScheduled = false;
        }
        Flush(depth: 0);
    }
}
```

### 4.6 View Integration

The `View` base class owns a dedicated body-dependency tracker. This is intentionally
**not** an `Effect`: body evaluation should happen exactly once during the real rebuild,
not once to rediscover dependencies and then again during `Reload()`.

```csharp
// In View.cs — additions to the existing View class

public partial class View
{
    // The dependency set read during the last successful Body build.
    private HashSet<IReactiveSource>? _bodyDependencies;
    private BodyDependencySubscriber? _bodySubscriber;

    /// <summary>
     /// Called during GetRenderView() / Body evaluation.
     /// Wraps the Body invocation in a reactive tracking context.
     /// Captures dependencies during the real Body build, then diffs the
     /// subscriptions after the build succeeds.
     /// </summary>
    internal View GetRenderViewReactive()
    {
        var oldDeps = _bodyDependencies;
        var subscriber = _bodySubscriber ??= new BodyDependencySubscriber(this);

        // Track reads during the actual Body build. If Body throws, the exception
        // bubbles to the existing GetRenderView() caller, preserving Comet's current
        // debug-time behavior. Old deps remain subscribed until a successful rebuild.
        using var scope = ReactiveScope.BeginTracking();
        var result = Body!.Invoke();
        var newDeps = scope.EndTracking();

        // Diff-based subscription update: keep old deps subscribed until the new
        // dependency set is known, so concurrent writes during rebuild are not lost.
        if (oldDeps != null)
        {
            foreach (var dep in oldDeps)
            {
                if (!newDeps.Contains(dep))
                    dep.Unsubscribe(subscriber);
            }
        }
        foreach (var dep in newDeps)
        {
            if (oldDeps == null || !oldDeps.Contains(dep))
                dep.Subscribe(subscriber);
        }

        _bodyDependencies = newDeps;
        return result;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _bodyDependencies != null && _bodySubscriber != null)
        {
            foreach (var dep in _bodyDependencies)
                dep.Unsubscribe(_bodySubscriber);
            _bodyDependencies = null;
            _bodySubscriber = null;
        }
        base.Dispose(disposing);
    }

    private sealed class BodyDependencySubscriber : IReactiveSubscriber
    {
        private readonly View _view;

        public BodyDependencySubscriber(View view)
        {
            _view = view;
        }

        public void OnDependencyChanged(IReactiveSource source)
        {
            if (!_view.IsDisposed)
                ReactiveScheduler.MarkViewDirty(_view);
        }
    }
}
```

> **Design note:** The R2 revision moved view tracking onto a single `Effect`, which fixed
> the ownership problem but created a worse architectural issue: `Body` ran once during
> effect flush and again during `Reload()`. Rev 4 replaces that with a dedicated
> `BodyDependencySubscriber` owned by the view. That keeps `Body` evaluation as the
> single source of truth for view rebuilds while still using diff-based dependency handoff.

### 4.7 Component\<TState\> Integration

The `Component<TState>` class from the existing Comet API continues to work, but its
`SetState` method will leverage the reactive scheduler in Phase 3:

> **Note:** The code below shows the Phase 3 target. During Phase 1 migration,
> `SetState` continues to use the existing `StateManager.BeginBatch/EndBatch` +
> `ThreadHelper.RunOnMainThread(() => Reload())` pattern.

```csharp
public abstract class Component<TState> : Component
    where TState : class, new()
{
    private TState _state;

    public new TState State => _state ??= new TState();

    /// <summary>
    /// Mutate state and trigger a re-render. Safe to call from any thread.
    /// Uses the reactive scheduler for automatic coalescing — multiple SetState
    /// calls within a single synchronous frame produce a single re-render.
    /// </summary>
    protected void SetState(Action<TState> mutator)
    {
        if (mutator == null)
            throw new ArgumentNullException(nameof(mutator));

        var state = State;
        mutator(state);

        // Mark this view dirty — the scheduler coalesces and dispatches
        ReactiveScheduler.MarkViewDirty(this);
    }

    // State transfer for hot reload — unchanged from current implementation
    public override void TransferStateFrom(IComponentWithState source)
    {
        if (source is Component<TState> typed && typed._state != null)
            _state = typed._state;
    }
}
```

### 4.8 Two-Way Binding Pattern

Two-way binding works naturally with `Signal<T>`:

```csharp
public class LoginForm : View
{
    readonly Signal<string> username = new("");
    readonly Signal<string> password = new("");
    readonly Signal<bool> rememberMe = new(false);

    [Body]
    View body() => new VStack
    {
        // Two-way: TextField reads and writes the signal directly
        new TextField(username)
            .Placeholder("Username"),

        new SecureField(password)
            .Placeholder("Password"),

        // Two-way: Toggle reads and writes the signal
        new Toggle(rememberMe),

        new Button("Login", OnLogin),
    };

    void OnLogin()
    {
        // Read current values — Peek() since we're not in a reactive context
        var user = username.Peek();
        var pass = password.Peek();
        var remember = rememberMe.Peek();
        // ... perform login
    }
}
```

Generated control code for two-way binding:

```csharp
// In generated TextField.cs
public partial class TextField : View
{
    private Signal<string>? _textSignal;
    private Computed<string>? _textComputed;

    // Two-way: binds directly to signal
    public TextField(Signal<string> text)
    {
        _textSignal = text;
        // Subscribe to signal changes → update native control
        // Native control changes → update signal
    }

    // One-way computed: read-only display
    public TextField(Func<string> textFunc)
    {
        _textComputed = new Computed<string>(textFunc);
    }

    // Static: no reactivity
    public TextField(string staticText)
    {
        // Just set the value directly
    }
}
```

### 4.9 Collection / List State Patterns

For collections, use `Signal<T>` with immutable collection replacement, or use the
`SignalList<T>` helper for fine-grained list mutations:

```csharp
namespace Comet.Reactive;

/// <summary>
/// A reactive observable list. Mutations notify subscribers with the
/// specific change (add, remove, replace, move, reset) so that list
/// controls can perform targeted UI updates instead of full rebuilds.
/// </summary>
public sealed class SignalList<T> : IReactiveSource, IReadOnlyList<T>, IDisposable
{
    private readonly List<T> _items;
    private uint _version;
    private readonly SubscriberList _subscribers = new();

    public SignalList() { _items = new List<T>(); }
    public SignalList(IEnumerable<T> items) { _items = new List<T>(items); }

    /// <summary>
    /// Pending changes since the last flush. Changes are queued (not stored in a
    /// single mutable field) so that list renderers can consume them during the
    /// coalesced flush cycle. If multiple mutations happen before a flush, only
    /// the individual changes are queued — a Reset is NOT forced.
    /// </summary>
    private readonly Queue<ListChange<T>> _pendingChanges = new();

    /// <summary>
    /// Dequeue all pending changes since the last flush. Called by list renderers
    /// during the flush cycle to apply incremental updates.
    /// Returns empty if no changes are pending (or if changes were coalesced to a Reset).
    /// </summary>
    public IReadOnlyList<ListChange<T>> ConsumePendingChanges()
    {
        if (_pendingChanges.Count == 0)
            return Array.Empty<ListChange<T>>();
        var result = _pendingChanges.ToArray();
        _pendingChanges.Clear();
        return result;
    }

    public int Count
    {
        get
        {
            ReactiveScope.Current?.TrackRead(this);
            return _items.Count;
        }
    }

    public T this[int index]
    {
        get
        {
            ReactiveScope.Current?.TrackRead(this);
            return _items[index];
        }
        set
        {
            var old = _items[index];
            _items[index] = value;
            Notify(ListChange<T>.Replace(index, old, value));
        }
    }

    public void Add(T item)
    {
        _items.Add(item);
        Notify(ListChange<T>.Insert(_items.Count - 1, item));
    }

    public void Insert(int index, T item)
    {
        _items.Insert(index, item);
        Notify(ListChange<T>.Insert(index, item));
    }

    public bool Remove(T item)
    {
        int index = _items.IndexOf(item);
        if (index < 0) return false;
        _items.RemoveAt(index);
        Notify(ListChange<T>.Remove(index, item));
        return true;
    }

    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        Notify(ListChange<T>.Remove(index, item));
    }

    public void Clear()
    {
        _items.Clear();
        Notify(ListChange<T>.Reset());
    }

    /// <summary>
    /// Batch multiple mutations. Only a single Reset notification fires at the end.
    /// </summary>
    public void Batch(Action<List<T>> mutator)
    {
        mutator(_items);
        Notify(ListChange<T>.Reset());
    }

    private void Notify(ListChange<T> change)
    {
        // Cap pending changes — if the queue exceeds the threshold, collapse
        // all pending changes into a single Reset. This prevents unbounded queue
        // growth when a SignalList receives rapid mutations while offscreen.
        const int MaxPendingChanges = 100;
        if (_pendingChanges.Count >= MaxPendingChanges)
        {
            _pendingChanges.Clear();
            _pendingChanges.Enqueue(ListChange<T>.Reset());
        }
        else
        {
            _pendingChanges.Enqueue(change);
        }
        unchecked { _version++; }
        _subscribers.NotifyAll(this);
        ReactiveScheduler.EnsureFlushScheduled();
    }

    // --- IReactiveSource ---
    public uint Version => _version;
    public void Subscribe(IReactiveSubscriber subscriber) => _subscribers.Add(subscriber);
    public void Unsubscribe(IReactiveSubscriber subscriber) => _subscribers.Remove(subscriber);

    // --- IReadOnlyList<T> ---
    public IEnumerator<T> GetEnumerator()
    {
        ReactiveScope.Current?.TrackRead(this);
        return _items.GetEnumerator();
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose() => _subscribers.Clear();
}

/// <summary>
/// Describes a single change to a SignalList.
/// </summary>
public readonly record struct ListChange<T>
{
    public ListChangeKind Kind { get; init; }
    public int Index { get; init; }
    public T? Item { get; init; }
    public T? OldItem { get; init; }

    public static ListChange<T> Insert(int index, T item)
        => new() { Kind = ListChangeKind.Insert, Index = index, Item = item };
    public static ListChange<T> Remove(int index, T item)
        => new() { Kind = ListChangeKind.Remove, Index = index, OldItem = item };
    public static ListChange<T> Replace(int index, T oldItem, T newItem)
        => new() { Kind = ListChangeKind.Replace, Index = index, Item = newItem, OldItem = oldItem };
    public static ListChange<T> Reset()
        => new() { Kind = ListChangeKind.Reset };
}

public enum ListChangeKind { Insert, Remove, Replace, Reset }
```

Usage:

```csharp
public class TodoList : View
{
    readonly SignalList<string> items = new(["Buy milk", "Walk dog"]);
    readonly Signal<string> newItem = new("");

    [Body]
    View body() => new VStack
    {
        new HStack
        {
            new TextField(newItem),
            new Button("Add", () =>
            {
                items.Add(newItem.Peek());
                newItem.Value = "";
            }),
        },

        new ForEach(items, (item, index) => new HStack
        {
            new Text(item),
            new Button("×", () => items.RemoveAt(index)),
        }),
    };
}
```

### 4.10 Diagnostics

Design Goal 5 promises debuggability. Here is the concrete diagnostic infrastructure:

```csharp
namespace Comet.Reactive;

/// <summary>
/// Optional diagnostic observer for the reactive graph. Disabled by default;
/// enable by setting <see cref="ReactiveDiagnostics.IsEnabled"/> = true
/// (typically in a #if DEBUG block or from a diagnostic settings page).
/// </summary>
public static class ReactiveDiagnostics
{
    /// <summary>
    /// When true, diagnostic events are emitted. Default: false (zero overhead).
    /// </summary>
    public static bool IsEnabled { get; set; }

    /// <summary>
    /// Fired when a view rebuild is triggered. Returns an IDisposable subscription
    /// token — dispose it to unsubscribe. This prevents the classic .NET memory leak
    /// where static events root subscriber instances.
    /// </summary>
    public static IDisposable OnViewRebuilt(Action<ViewRebuildEvent> handler)
    {
        ViewRebuilt += handler;
        return new DiagnosticSubscription(() => ViewRebuilt -= handler);
    }

    /// <summary>
    /// Fired when a signal value changes. Returns an IDisposable subscription token.
    /// </summary>
    public static IDisposable OnSignalChanged(Action<SignalChangeEvent> handler)
    {
        SignalChanged += handler;
        return new DiagnosticSubscription(() => SignalChanged -= handler);
    }

    /// <summary>
    /// Fired when the flush cycle depth exceeds MaxFlushDepth.
    /// Indicates a cycle in the reactive graph. Returns an IDisposable
    /// subscription token like the other diagnostics hooks.
    /// </summary>
    public static IDisposable OnFlushDepthWarning(Action<int> handler)
    {
        FlushDepthWarning += handler;
        return new DiagnosticSubscription(() => FlushDepthWarning -= handler);
    }

    // Internal events — only accessible via the subscription methods above
    private static event Action<int>? FlushDepthWarning;
    private static event Action<ViewRebuildEvent>? ViewRebuilt;
    private static event Action<SignalChangeEvent>? SignalChanged;

    internal static void NotifyFlushDepthWarning(int depth)
    {
        if (!IsEnabled) return;
        FlushDepthWarning?.Invoke(depth);
    }

    internal static void NotifyViewRebuilt(View view, IReactiveSource? trigger)
    {
        if (!IsEnabled) return;
        ViewRebuilt?.Invoke(new ViewRebuildEvent(
            view.GetType().Name,
            trigger?.GetType().Name,
            DateTime.UtcNow));
    }

    internal static void NotifySignalChanged(IReactiveSource signal, string? name)
    {
        if (!IsEnabled) return;
        SignalChanged?.Invoke(new SignalChangeEvent(
            signal.GetType().GenericTypeArguments.FirstOrDefault()?.Name ?? "?",
            name,
            signal.Version,
            DateTime.UtcNow));
    }

    private sealed class DiagnosticSubscription : IDisposable
    {
        private Action? _unsubscribe;
        public DiagnosticSubscription(Action unsubscribe) => _unsubscribe = unsubscribe;
        public void Dispose()
        {
            _unsubscribe?.Invoke();
            _unsubscribe = null;
        }
    }
}

public readonly record struct ViewRebuildEvent(
    string ViewType, string? TriggerType, DateTime Timestamp);

public readonly record struct SignalChangeEvent(
    string ValueType, string? Name, uint Version, DateTime Timestamp);
```

Usage in debug builds:

```csharp
#if DEBUG
ReactiveDiagnostics.IsEnabled = true;
// Subscriptions return IDisposable — dispose to unsubscribe (prevents memory leaks)
var sub1 = ReactiveDiagnostics.OnViewRebuilt(e =>
    Debug.WriteLine($"[Reactive] {e.ViewType} rebuilt (trigger: {e.TriggerType})"));
var sub2 = ReactiveDiagnostics.OnSignalChanged(e =>
    Debug.WriteLine($"[Reactive] Signal<{e.ValueType}> '{e.Name}' → v{e.Version}"));
// Later: sub1.Dispose(); sub2.Dispose(); to clean up
#endif
```

To support the `Name` field in diagnostics, `Signal<T>` gets an optional debug name:

```csharp
// In Signal<T>:
public string? DebugName { get; init; }

// Usage:
readonly Signal<int> count = new(0) { DebugName = "count" };
```

> **Note:** The `DebugName` is purely diagnostic — it has no effect on reactive behavior
> and is `null` by default. The Roslyn analyzer could offer a code fix to auto-populate
> `DebugName` from the field name during development.

### 4.11 Environment Integration

The Environment system is bridged by making each environment key its own reactive source.
This preserves Comet's current per-key routing behavior (`usedEnvironmentData`) instead of
collapsing all environment reads into a single broad invalidation source.

```csharp
/// <summary>
/// Reactive wrapper around Comet's environment system. When an environment
/// value changes, views that read it are notified through the standard
/// reactive pipeline.
/// </summary>
internal sealed class ReactiveEnvironment
{
    private readonly object _lock = new();
    private readonly Dictionary<string, EnvironmentKeySource> _sources = new();

    public void SetValue(string key, object value)
    {
        GetSource(key).NotifyChanged();
        ReactiveScheduler.EnsureFlushScheduled();
    }

    public void TrackRead(string key)
    {
        ReactiveScope.Current?.TrackRead(GetSource(key));
    }

    private EnvironmentKeySource GetSource(string key)
    {
        lock (_lock)
        {
            if (_sources.TryGetValue(key, out var source))
                return source;

            source = new EnvironmentKeySource(key);
            _sources.Add(key, source);
            return source;
        }
    }

    private sealed class EnvironmentKeySource : IReactiveSource
    {
        private readonly SubscriberList _subscribers = new();
        private uint _version;

        public EnvironmentKeySource(string key)
        {
            Key = key;
        }

        public string Key { get; }
        public uint Version => _version;

        public void NotifyChanged()
        {
            unchecked { _version++; }
            _subscribers.NotifyAll(this);
        }

        public void Subscribe(IReactiveSubscriber subscriber) => _subscribers.Add(subscriber);
        public void Unsubscribe(IReactiveSubscriber subscriber) => _subscribers.Remove(subscriber);
    }
}
```

Fluent environment methods like `.Background()`, `.FontSize()`, etc. continue to work
unchanged. Internally, when the framework reads an environment value during Body evaluation,
it calls `ReactiveEnvironment.TrackRead(key)`, which registers a dependency on that
specific key source. When a parent sets an environment value, `ReactiveEnvironment
.SetValue(key, value)` notifies only subscribers to that key. Styled keys and typed keys
remain distinct sources, matching today's environment lookup behavior.

---

## 5. Update Pipeline

### 5.1 Signal Write → UI Update Flow

```
Signal<T>.Value = newValue
    │
    ├── 1. Equality check: if equal, return (no-op)
    │
    ├── 2. Swap StrongBox and increment _version
    │
    ├── 3. NotifySubscribers()
    │   │
    │   ├── For each Computed<T> subscriber:
    │   │       Mark _dirty = true
    │   │       Propagate: notify Computed's own subscribers
    │   │
    │   ├── For each Effect subscriber:
    │   │       Mark _dirty = true
    │   │       Add to ReactiveScheduler._dirtyEffects
    │   │
    │   └── For each BodyDependencySubscriber:
    │           Add view to ReactiveScheduler._dirtyViews
    │
    └── 4. ReactiveScheduler.EnsureFlushScheduled()
            │
            ├── If _flushScheduled: return (coalesce!)
            │
            └── Post Dispatcher.Dispatch(FlushEntry) ←── runs after current sync frame
                    │
                    ├── Reset _flushScheduled = false
                    │
                    ├── Flush(depth: 0)
                    │   │
                    │   ├── Phase 1: Flush dirty Effects
                    │   │       For each dirty Effect:
                    │   │           Re-execute the effect Action
                    │   │           Diff-based dep update (subscribe new, unsub removed)
                    │   │           (may produce more dirty views/effects)
                    │   │
                    │   ├── Phase 2: Flush dirty Views
                    │   │       For each dirty View:
                    │   │           Reload() → ResetView()
                    │   │               Build new view tree (Body.Invoke())
                    │   │               Diff with old view tree
                    │   │               Update handlers
                    │   │
                    │   └── Phase 3: Check for cascading dirt
                    │           If more dirty items exist → Flush(depth + 1)
                    │           If depth >= 100 → break with diagnostic warning
                    │
```

### 5.2 Targeted Property Update Flow (Computed/Signal → Control)

When a control is bound directly to a Signal or Computed, the update bypasses full
view rebuild:

```
Signal<string>.Value = "new text"
    │
    └── NotifySubscribers()
            │
            └── TextControl's internal Computed<string> subscriber
                    │
                    ├── Mark dirty
                    └── On flush:
                            Computed.Value re-evaluates
                            Compare old vs new
                            If different: ViewHandler.UpdateValue("Text")
                            (No Reload, no Body re-evaluation, no tree diff)
```

### 5.3 Coalescing Example

```csharp
// User clicks a button that triggers:
void OnComplexUpdate()
{
    count.Value = 42;       // Posts flush (first mutation)
    name.Value = "Alice";   // Flush already scheduled — no-op post
    age.Value = 30;         // Flush already scheduled — no-op post
    // → Dispatcher processes: single Flush()
    //   → count's subscribers notified
    //   → name's subscribers notified
    //   → age's subscribers notified
    //   → Dirty views reloaded once
}
```

### 5.4 Thread Safety Flow

```
Background thread:                      UI thread:
    │                                       │
    signal.Value = 42                       │
    │ lock (_writeLock) {                   │
    │   equality check                      │
    │   _box = new StrongBox(42)             │
    │   NotifySubscribers()                 │
    │     mark deps dirty                   │
    │ }                                     │
    │ EnsureFlushScheduled()                │
    │   Dispatcher.Dispatch(FlushEntry) ────┤
    │                                       │ FlushEntry()
    │                                       │   _flushScheduled = false
    │                                       │   Flush(depth: 0)
    │                                       │     effects run
    │                                       │     views reload
    │                                       │     all on UI thread ✓
```

---

## 6. Hot Reload Integration

### 6.1 Signal Identity Stability

Signals are identity-stable across hot reloads because they are `readonly` fields on view
instances. However, .NET hot reload replaces the Type, creating a new view object with new
field slots. The `readonly` modifier means fields can't be reassigned after construction.
Therefore, the hot reload transfer mechanism must use reflection to copy signal **references**
(not just values) from the old view to the new view, preserving subscribers.

```csharp
// New override in View.cs for signal-aware hot reload:
// Hot reload is a debug-time feature — NativeAOT and trimming are disabled during debug.
// This reflection pattern is intentionally trim-unsafe and AOT-unsafe.
[UnconditionalSuppressMessage("Trimming", "IL2070",
    Justification = "Hot reload is debug-only; trimming/AOT are disabled in debug builds")]
protected override void TransferHotReloadStateToCore(View newView)
{
    // Phase 1: Transfer existing BindingState properties (backward compat)
    base.TransferHotReloadStateToCore(newView);

    // Phase 2: Transfer Signal<T> field references via reflection.
    // This preserves subscriber relationships — the new view gets the SAME
    // Signal objects (with their subscriptions intact).
    var oldType = this.GetType();
    var newType = newView.GetType();
    var cometAssembly = typeof(Signal<>).Assembly;

    var fields = oldType.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
        .Where(f => f.FieldType.IsGenericType &&
                     f.FieldType.GetGenericTypeDefinition().Assembly == cometAssembly &&
                     f.FieldType.GetGenericTypeDefinition().Name == "Signal`1");

    foreach (var field in fields)
    {
        var newField = newType.GetField(field.Name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (newField != null && newField.FieldType == field.FieldType)
        {
            var signalRef = field.GetValue(this);
            if (signalRef != null)
            {
                // Use reflection to write to readonly field — this is intentional
                // and safe during hot reload (the object is being set up, not yet
                // in use by the reactive graph).
                newField.SetValue(newView, signalRef);
            }
        }
    }
}
```

> **Note:** Writing to `readonly` fields via reflection is supported by the runtime and
> is the standard approach used by serialization frameworks and hot reload systems. The
> `FieldInfo.SetValue` call bypasses the `readonly` restriction. This is safe because
> the new view object is being constructed and not yet subscribed to any reactive graph.

### 6.2 Subscription Rewiring

When a view is hot-reloaded:

1. The old view's `BodyDependencySubscriber` is unsubscribed from its current
   `_bodyDependencies` during disposal/transfer.
2. The new view receives the signal references via `TransferState`.
3. The new view's next `GetRenderViewReactive()` call invokes Body once, captures the
   new dependency set, and re-subscribes its own `BodyDependencySubscriber`.
4. Any Computed values that reference the same signals continue to work because
   the signal identity hasn't changed.

This is cleaner than the current system where hot reload requires re-running
`CheckForStateAttributes` via reflection to re-establish the `NotifyToViewMappings`.

### 6.3 Component\<TState\> Hot Reload

For `Component<TState>`, the state object transfer works identically to today:

```csharp
// Component<TState>.TransferStateFrom transfers the _state object
// The new component's Render() will re-read State properties and
// establish new reactive subscriptions via the tracking context.
```

The `_newComponent` forwarding chain pattern from MauiReactor is not needed because
signals don't capture component identity — they're standalone reactive cells that
any subscriber can observe.

### 6.4 MauiHotReloadHelper Integration

The existing `MauiHotReloadHelper.RegisterReplacedView` / `IsReplacedView` / `TriggerReload`
pipeline continues to work. The only change is that `IHotReloadableView.Reload()` now
also disposes the old reactive scope before rebuilding, ensuring clean subscription state.

---

## 7. Migration Path

### Phase 1: Add Signal\<T\> alongside State\<T\> (non-breaking)

**Timeline:** First release after adoption.

1. Add the `Comet.Reactive` namespace with `Signal<T>`, `Computed<T>`, `Effect`,
   `ReactiveScope`, and `ReactiveScheduler`.

2. `Signal<T>` implements `INotifyPropertyRead` for backward compatibility, so it can
   participate in the existing `StateManager` pipeline during transition:

```csharp
public sealed class Signal<T> : IReactiveSource, INotifyPropertyRead, IDisposable
{
    // ... existing Signal<T> implementation (with StrongBox + _writeLock) ...

    // Bridge: INotifyPropertyRead for backward compatibility
    public event PropertyChangedEventHandler? PropertyRead;
    public event PropertyChangedEventHandler? PropertyChanged;

    // When read inside an old-style StateBuilder context, fire PropertyRead
    // so StateManager picks it up. When read inside a ReactiveScope, track
    // via the new system instead.
    public T Value
    {
        get
        {
            if (ReactiveScope.Current != null)
                ReactiveScope.Current.TrackRead(this);
            else
                PropertyRead?.Invoke(this, new PropertyChangedEventArgs("Value"));
            return _box.Value;
        }
        set
        {
            lock (_writeLock)
            {
                if (_comparer.Equals(_box.Value, value)) return;
                _box = new StrongBox<T>(value);
                unchecked { _version++; }

                // Notify both systems during transition
                _subscribers.NotifyAll(this); // new system
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Value")); // old system
            }
            ReactiveScheduler.EnsureFlushScheduled();
        }
    }
}
```

3. `CheckForStateAttributes` in `StateManager` is updated to recognize `Signal<T>` fields
   in addition to `State<T>` fields:

```csharp
static IEnumerable<INotifyPropertyRead> CheckForStateAttributes(object obj, View view)
{
    var type = obj.GetType();
    var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
        .Where(x =>
            (x.FieldType.Assembly == CometAssembly && x.FieldType.Name == "State`1") ||
            (x.FieldType.Assembly == CometAssembly && x.FieldType.Name == "Signal`1") || // NEW
            Attribute.IsDefined(x, typeof(StateAttribute)))
        .ToList();
    // ... rest unchanged
}
```

4. Generated controls get new overloads accepting `Signal<T>` and `Func<T>` alongside
   existing `Binding<T>`:

```csharp
// Generated Text control — Phase 1 adds new overloads
public Text(Binding<string> value) { /* existing */ }
public Text(Signal<string> value) { /* NEW: direct signal binding */ }
public Text(Func<string> value) { /* NEW: explicit lambda binding */ }
public Text(string staticValue) { /* existing non-reactive */ }
```

5. Developers can incrementally adopt `Signal<T>` in new views while existing views
   continue to use `State<T>`.

**Example — mixing old and new in the same app:**

```csharp
// Old view — unchanged, works as before
public class OldCounter : View
{
    readonly State<int> count = 0;
    [Body]
    View body() => new Text(() => $"Count: {count.Value}");
}

// New view — uses Signal<T>
public class NewCounter : View
{
    readonly Signal<int> count = new(0);
    [Body]
    View body() => new VStack
    {
        new Text(() => $"Count: {count.Value}"),
        new Button("+", () => count.Value++),
    };
}
```

### Phase 2: Deprecate implicit T → Binding\<T\> conversion

**Timeline:** One release after Phase 1.

1. Add `[Obsolete("Use Signal<T>, Func<T>, or a static value instead. See migration guide.")]`
   to `Binding<T>.implicit operator Binding<T>(T value)`.

2. The Roslyn analyzer emits warnings (not errors) for implicit conversions to `Binding<T>`.

3. `State<T>` is marked `[Obsolete("Use Signal<T> instead.")]`.

4. Provide a `comet-migrate` dotnet tool that rewrites `State<T>` → `Signal<T>` via Roslyn:

```bash
dotnet tool install --global Comet.Migrate
comet-migrate --project ./src/MyApp/MyApp.csproj --dry-run
comet-migrate --project ./src/MyApp/MyApp.csproj
```

The tool performs these transformations:
- `readonly State<T> x = value;` → `readonly Signal<T> x = new(value);`
- `state.Value` → `signal.Value` (no change needed, same property name)
- `new Text(() => ...)` → unchanged (lambdas work with both systems)
- `new Text(state.Value)` → `new Text(() => state.Value)` (fixes the trap)

### Phase 3: Remove State\<T\> and Binding\<T\>

**Timeline:** Major version bump.

1. Remove `State<T>`, `Binding<T>`, and `StateManager`.
2. Remove `INotifyPropertyRead` bridge from `Signal<T>`.
3. Generated controls only accept `Signal<T>`, `Computed<T>`, `Func<T>`, or static values.
4. The `BindingState`, `ViewUpdateProperties`, and `GlobalProperties` machinery is replaced
   entirely by the `ReactiveScope` / `SubscriberList` system.

---

## 8. Performance Comparison

### 8.1 Theoretical Analysis

| Operation | Current (State\<T\>) | New (Signal\<T\>) | MauiReactor |
|-----------|---------------------|--------------------|-------------|
| **Single property update** | Targeted if 1:1 binding detected; may be global | Always targeted via Computed subscription | Reactive lambda (targeted) or full Render |
| **Multi-property update** | N × Reload unless manual BeginBatch/EndBatch | 1 × Flush via microtask coalescing | 1 × Layout via `_layoutCallEnqueued` |
| **Dependency tracking cost** | `StartProperty`/`EndProperty` per binding, list allocs, HashSet dedup | `HashSet<IReactiveSource>.Add()` per read, no list copies | No tracking (explicit `SetState` invalidation) |
| **Stabilized binding read** | Zero tracking overhead (fast path) | `ReactiveScope.Current?.TrackRead()` null-check only outside reactive context | N/A |
| **Dependency change detection** | Re-track + compare lists on every evaluation until stable | Version number comparison: `O(deps)` integer compares | N/A |
| **Memory per signal** | BindingObject dictionary + StateManager static maps | Signal object: volatile StrongBox\<T\> + version + lock + SubscriberList | Plain property on state class |
| **Memory per view** | BindingState (2 dictionaries + 1 HashSet) | Single Effect (owns all signal subscriptions) | VisualNode (children list + property values) |
| **GC pressure** | High: `PropertyChangedEventArgs` alloc (mitigated by cache), List allocs in EndProperty | Low-moderate: one StrongBox\<T\> alloc per write, WeakReference arrays, ArrayPool for notify | Low: state is mutated in-place |
| **Thread safety overhead** | `ReaderWriterLockSlim` on every property change | Per-signal `lock` on write (μs critical section) + single Dispatcher.Dispatch | `Dispatcher.IsDispatchRequired` check |

### 8.2 Expected Improvements

**Fewer rebuilds:** The current system's global vs local classification heuristic causes
unnecessary full rebuilds when string interpolation or multi-property reads occur. The new
system makes every signal read a targeted dependency — the only time a full `Reload()` fires
is when a structural dependency (signal read directly in Body) changes.

**Coalescing:** The automatic microtask coalescing eliminates the most common performance
anti-pattern: multiple rapid state updates causing multiple rebuilds. In a typical form
submission scenario (updating 5 fields simultaneously), the new system fires 1 flush vs
the current system's 5 reloads.

**No stabilization penalty:** The current system pays a tracking cost for the first N
evaluations of each binding until it stabilizes. The new system tracks on every evaluation
but with lower per-track cost (HashSet.Add vs list allocation + dedup).

### 8.3 Benchmark Targets

These benchmarks should be implemented as part of the Phase 1 work:

| Benchmark | Target |
|-----------|--------|
| Single Signal write → single Text update | < 50μs on desktop |
| 100 Signal writes (same frame) → single flush | < 200μs on desktop |
| View with 50 Computed bindings, 1 Signal changes | < 100μs to update all |
| SignalList with 1000 items, add 1 item | < 500μs (targeted insert, no full rebuild) |
| Hot reload of view with 10 Signals | State preserved, < 100ms total |

---

## 9. Open Questions

### Q1: Should Computed\<T\> support write-back?

Currently, `Binding<T>` supports both get and set (two-way binding). `Computed<T>` as
designed is read-only. For two-way bindings, we provide `Signal<T>` directly. But some
scenarios need a "computed with write-back" — e.g., a temperature converter where the
displayed value is computed but the user can type a new value that writes back through
a transform.

**Options:**
- (A) `WritableComputed<T>` — a Computed with an explicit setter: `new WritableComputed<T>(() => CtoF(celsius.Value), v => celsius.Value = FtoC(v))`
- (B) Use `Signal<T>` with an `Effect` that syncs it: more code but uses fewer primitives
- (C) Defer to Phase 2

**Leaning toward (A)** — it's a natural extension and avoids sync loops.

### Q2: Should Effects run synchronously or in the microtask?

The current design defers Effect execution to the microtask flush. This means an Effect
that calls `Debug.WriteLine` after a signal write won't see the output until the next
dispatch cycle.

**Options:**
- (A) Always deferred (current design) — consistent, coalesced, but slightly surprising
- (B) Option for synchronous effects: `new Effect(action, synchronous: true)`
- (C) Effects in microtask, but provide `Signal<T>.OnChanged(Action<T>)` for synchronous callbacks

**Leaning toward (C)** — keeps the reactive graph clean while providing an escape hatch.

### Q3: How to handle async state initialization?

A common pattern is fetching initial data from an API:

```csharp
readonly Signal<List<Item>> items = new(new());

protected override async void OnLoaded()
{
    var data = await api.GetItemsAsync();
    items.Value = data;
}
```

Should we provide a built-in `AsyncSignal<T>` that has loading/error/data states?

**Options:**
- (A) No special support — use `Signal<T>` with a wrapper type: `Signal<AsyncState<List<Item>>>`
- (B) Built-in `AsyncSignal<T>` with `IsLoading`, `Error`, `Data` properties
- (C) Defer to a community package

**Leaning toward (A)** — keep the core primitives minimal. A `record AsyncState<T>(bool IsLoading, T? Data, Exception? Error)` is easy to define per-app.

### Q4: Analyzer strictness level

The COMET003 analyzer warns when reading `Signal.Value` outside a reactive context. This
could be noisy for event handlers, button callbacks, etc., where you legitimately want the
current value without tracking.

**Options:**
- (A) Warning by default, suppressed by `Peek()` — developers learn to use `Peek()` in non-reactive contexts
- (B) Info-level by default, upgradeable to warning via `.editorconfig`
- (C) Only warn in `Body` methods, not in event handlers

**Leaning toward (B)** — info by default avoids noise while still educating developers.

### Q5: Should the source generator auto-wrap Body reads?

The current source generator auto-wires `[Body]` methods. Should it also automatically
wrap Body invocations in a `ReactiveScope`, or should this be done in the framework's
`GetRenderView()` method?

**Decision: framework-level.** The `GetRenderView()` / `GetRenderViewReactive()` method
handles scope creation. The source generator continues to wire `Body = () => body()` as
it does today. This keeps the source generator simpler and the reactive infrastructure
in one place.

### Q6: Observable collection interop

Should `SignalList<T>` implement `INotifyCollectionChanged` for interop with MAUI's
`CollectionView` and other controls that expect the standard .NET collection change
interface?

**Options:**
- (A) Yes, implement `INotifyCollectionChanged` — full MAUI interop
- (B) No, use a bridge adapter: `new ObservableCollectionBridge<T>(signalList)`
- (C) Implement both: `INotifyCollectionChanged` for MAUI interop, plus `IReactiveSource` for Comet-native reactivity

**Leaning toward (C)** — dual interface gives the best of both worlds.

### Q7: Memory overhead of SubscriberList

The `SubscriberList` is a `class` (not `struct` — see skeptic review for why) using
`WeakReference<T>` arrays. For views with many signals (e.g., a form with 20 fields),
each signal has a small array of weak references plus the class header overhead (~16 bytes
on 64-bit). Is this overhead acceptable, or should we use a pooled/interned approach?

**Preliminary analysis:** 20 signals × (16-byte object header + 4 subscribers × 32 bytes
per WeakReference + 8-byte lock reference) ≈ 3 KB. This is negligible compared to the
native view tree. The class overhead is justified by correctness: mutable structs with
`readonly` lock fields are a known C# footgun (defensive copies, `default(T)` skipping
constructors). **No optimization needed at this stage.** Revisit if profiling shows hot spots.

---

*This is a living document. As implementation proceeds, decisions will be recorded
in `.squad/decisions/` and this document will be updated to reflect the final design.*

---

## Revision History

### Rev 2 — Skeptic Review Fixes

Addressed all findings from the [skeptic review](../research/state-management-proposal-skeptic-review.md):

**Critical (3 fixed):**
1. **Signal setter thread safety:** Added `_writeLock` to `Signal<T>.Value` setter with `StrongBox<T>` pattern. The equality check, box swap, and subscriber notification are now atomic. Prevents torn reads for all types and missed notifications from concurrent writes. (§4.1)
2. **Flush recursion guard:** `Flush()` now takes a `depth` parameter, capped at `MaxFlushDepth = 100`. On overflow, clears dirty sets and emits a diagnostic warning instead of crashing with `StackOverflowException`. Modeled after SolidJS. (§4.5)
3. **SubscriberList → class:** Changed from `struct` to `sealed class`. Eliminates defensive-copy bugs, `default(T)` null-lock traps, and mutable-struct footguns. The lock allocation overhead was already present anyway. (§4.4)

**High (4 fixed):**
1. **Computed diff-based dep update:** `Evaluate()` no longer unsubscribes from all deps before re-evaluating. Instead, it collects new deps during evaluation, then diffs old vs new — unsubscribing only from removed deps and subscribing to added deps. Prevents missed notifications during evaluation. Same fix applied to `Effect.Run()`. (§4.2, §4.3)
2. **FlushSync as public API:** `ReactiveScheduler.FlushSync()` is now documented as a first-class API for event handlers that need synchronous visual updates, not just unit tests. (§4.5)
3. **NotifyAll passes source:** `SubscriberList.NotifyAll(IReactiveSource source)` now accepts and threads through the actual source that triggered the notification. Enables diagnostics and prevents future `NullReferenceException`. (§4.4)
4. **Hot reload signal transfer:** Replaced the incorrect "no special handling needed" claim with a concrete `TransferHotReloadStateToCore` override that uses reflection to copy `Signal<T>` field references from old to new view, preserving subscribers. (§6.1)

**Medium (5 fixed):**
1. **SignalList change queue:** Replaced `LastChange` (set-then-null timing bug) with `Queue<ListChange<T>>` + `ConsumePendingChanges()` API. Changes survive until the flush cycle. (§4.9)
2. **Exception handling in tracking:** `Computed.Evaluate()` and `Effect.Run()` now discard partial reads on exception and leave the node dirty for retry. No spurious deps from failed evaluations. (§4.2, §4.3)
3. **Effect deduplication:** Changed `_dirtyEffects` from `List<Effect>` to `HashSet<Effect>` to prevent the same effect from being flushed twice. (§4.5)
4. **Diagnostics infrastructure:** Added `ReactiveDiagnostics` static class with `ViewRebuilt` and `SignalChanged` events, `FlushDepthWarning`, and `Signal<T>.DebugName` property. Delivers on Design Goal 5. (§4.10)
5. **ThreadStatic documentation:** Added explicit note that background-thread reads are silently untracked by design. (§4.4 ReactiveScope)

### Rev 3 — Second Skeptic Review Fixes

Addressed findings from the [second skeptic review](../research/state-management-proposal-skeptic-review-r2.md):

**Critical (1 fixed):**
1. **Signal getter torn reads:** The Rev 2 `_writeLock` only protected the setter — the getter read `_value` naked, causing torn reads for large value types (`decimal`, `Matrix4x4`, custom structs > 8 bytes). Replaced raw `_value` field with `volatile StrongBox<T> _box`. The getter reads the volatile reference (pointer-atomic on all platforms), the setter swaps to a new `StrongBox<T>` inside the lock. Cost: one allocation per write — negligible for UI state mutations. (§4.1, §3.D)

**High (3 fixed):**
1. **Computed reentrant dirtying:** `_dirty` was still `true` during `_compute()` execution, so `OnDependencyChanged` silently swallowed re-notifications from deps that fired mid-evaluation. Fix: set `_dirty = false` BEFORE `_compute()`, check after. If re-dirtied during evaluation, skip cached value update and leave dirty for next read. (§4.2)
2. **GetRenderViewReactive compile error + dual mechanism:** `deps` was declared inside `finally` but used outside it (won't compile). Additionally used two subscription mechanisms (unused Effect + manual ViewDirtySubscribers). Rewrote to use a single Effect with `runImmediately: true` that tracks its own deps and cleans up via its own `Dispose()`. No zombie subscribers. (§4.6)
3. **`ReactiveScope` using pattern:** `Computed.Evaluate()` and `Effect.Run()` now use `using var scope = ReactiveScope.BeginTracking()` to ensure the previous scope is always restored, even on exception. Eliminates the risk of a permanently stuck `[ThreadStatic] _current` scope. (§4.2, §4.3, §4.4)

**Medium (4 fixed):**
1. **ARM volatile `_flushScheduled`:** Marked `_flushScheduled` as `volatile` to ensure the outer check in `EnsureFlushScheduled()` sees the latest value on weakly-ordered architectures (ARM = Android/iOS). (§4.5)
2. **Flush depth fires `FlushDepthWarning`:** The `ReactiveDiagnostics.FlushDepthWarning` event was declared but never called. Now fired from the `Flush(depth)` guard. In debug builds, also throws `InvalidOperationException` for early cycle detection. (§4.5)
3. **Hot reload trimming annotation:** Added `[UnconditionalSuppressMessage("Trimming", "IL2070")]` to the reflection-based signal transfer, with a comment noting it's debug-only. (§6.1)
4. **SignalList queue cap:** `_pendingChanges` is now capped at 100 entries — excess collapses to a single `Reset`. Prevents unbounded growth for rapid mutations on offscreen lists. (§4.9)

**Medium (1 fixed — diagnostics):**
5. **ReactiveDiagnostics memory leak:** Replaced public static events with `IDisposable` subscription API (`OnViewRebuilt()`, `OnSignalChanged()` return disposable tokens). Prevents the classic .NET static-event-roots-subscriber leak. (§4.10)

**Low (1 fixed):**
1. **Component\<TState\>.SetState divergence:** Clarified that the proposal shows the Phase 3 target. During Phase 1, `SetState` continues using `StateManager.BeginBatch/EndBatch`. (§4.7)

### Rev 4 — Third Skeptic Review Fixes

Addressed findings from the [third skeptic review](../research/state-management-proposal-skeptic-review-r3.md):

**High (2 fixed):**
1. **Effect-based body tracking replaced with dedicated invalidation tracking:** The Rev 3 `Effect` integration re-executed `Body` during effect flush and then again during `Reload()`, while also changing exception behavior and briefly dropping subscriptions during rebuild. Rev 4 replaces `_bodyEffect` with a view-owned `BodyDependencySubscriber` + `_bodyDependencies` set. Dependencies are now captured during the real Body build, old deps remain subscribed until the new set is known, and dependency changes only mark the view dirty. (§4.6, §5.1, §6.2)
2. **Environment invalidation restored to per-key precision:** Rev 3 modeled `ReactiveEnvironment` as a single `IReactiveSource`, which would have broadened every environment read into one coarse invalidation source. Rev 4 introduces a per-key `EnvironmentKeySource`, so `.Background()` reads are not invalidated by unrelated `.FontSize()` writes. This preserves current `usedEnvironmentData`-style precision. (§4.11)

**Medium (3 fixed):**
1. **Diagnostics API completed:** `FlushDepthWarning` now matches the rest of the diagnostics surface via `OnFlushDepthWarning(Action<int>)` returning `IDisposable`. The proposal also now shows diagnostics being emitted from `Signal<T>.Value` changes and view reloads, so the diagnostic surface is wired into the update pipeline instead of existing only as a passive API sketch. (§4.1, §4.5, §4.10)
2. **Effect failure recovery:** `Effect.Run()` no longer leaves an effect stuck in a dirty-but-unscheduled state after an exception. The exception path now clears `_dirty` after discarding partial reads, allowing the next dependency change to requeue the effect instead of silently wedging it. (§4.3)
3. **`FlushSync()` thread-affinity guard:** `FlushSync()` is now explicitly UI-thread-only and throws if called from a background thread. This keeps the public synchronous flush API from bypassing the dispatcher and accidentally running `view.Reload()` off-thread. (§4.5)
