# Comet State Management — Deep Dive

> **Audience:** Contributors and advanced users of the Comet MVU framework.
> **Source of truth:** All code references are from the `src/Comet/` and `src/Comet.SourceGenerator/` directories.

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Core Abstractions](#2-core-abstractions)
3. [Property Tracking Pipeline](#3-property-tracking-pipeline)
4. [Two Types of State Changes](#4-two-types-of-state-changes)
5. [The BindingState Machine](#5-the-bindingstate-machine)
6. [Binding Evaluation & Stabilization](#6-binding-evaluation--stabilization)
7. [State Batching](#7-state-batching)
8. [Component\<TState\> Pattern](#8-componenttstate-pattern)
9. [Environment System](#9-environment-system)
10. [Hot Reload State Transfer](#10-hot-reload-state-transfer)
11. [Source Generation](#11-source-generation)
12. [Threading & Concurrency](#12-threading--concurrency)
13. [Implicit Conversions & Pitfalls](#13-implicit-conversions--pitfalls)
14. [Lifecycle & Cleanup](#14-lifecycle--cleanup)
15. [Architecture Diagrams](#15-architecture-diagrams)

---

## 1. Executive Summary

Comet's state management is built on **automatic dependency tracking** — a technique
where the framework records which state properties each view reads, then
surgically updates only what changed. There are no manual `Subscribe()` calls,
no explicit `ObservableProperty` annotations for simple cases, and no binding
syntax in markup. You just write code, and Comet figures out the rest.

The three key differentiators:

1. **Implicit read tracking.** When a view's `Body` lambda executes, every
   `State<T>.Value` access is intercepted by `StateManager`, building a
   dependency graph on-the-fly.

2. **Two-tier change routing.** Changes to state properties that feed into a
   `Binding<T>` Func trigger a targeted `ViewPropertyChanged` (handler update
   only). Changes to properties read during Body evaluation trigger a full
   `Reload` (rebuild + diff). This eliminates unnecessary work.

3. **Binding stabilization.** After a `Func<T>` binding re-evaluates and the
   set of read properties hasn't changed, the binding marks itself `_bindingStable`
   and skips property tracking on subsequent evaluations — a fast path that
   avoids `StartProperty`/`EndProperty` overhead entirely.

---

## 2. Core Abstractions

### 2.1 INotifyPropertyRead

Standard .NET has `INotifyPropertyChanged`. Comet extends this with a read-side
counterpart:

```csharp
// BindingObject.cs
public interface INotifyPropertyRead : INotifyPropertyChanged
{
    event PropertyChangedEventHandler PropertyRead;
}
```

Both events fire through `BindingObject`'s protected helpers which route through
`StateManager` before raising the CLR events:

```csharp
// BindingObject.cs
protected virtual void CallPropertyRead(string propertyName)
{
    StateManager.OnPropertyRead(this, propertyName);
    PropertyRead?.Invoke(this, GetCachedArgs(propertyName));
}

protected virtual void CallPropertyChanged<T>(string propertyName, T value)
{
    StateManager.OnPropertyChanged(this, propertyName, value);
    PropertyChanged?.Invoke(this, GetCachedArgs(propertyName));
}
```

The `PropertyChangedEventArgs` are cached per property name to avoid repeated
allocations (`_argsCache` dictionary).

### 2.2 BindingObject

The base class for all observable objects in Comet. Provides a property bag
(`dictionary`), typed get/set helpers, and fires both read and changed
notifications:

```csharp
// BindingObject.cs
public class BindingObject : INotifyPropertyRead, IAutoImplemented
{
    internal protected Dictionary<string, object> dictionary
        = new Dictionary<string, object>();

    protected T GetProperty<T>(T defaultValue = default,
        [CallerMemberName] string propertyName = "")
    {
        CallPropertyRead(propertyName);
        if (dictionary.TryGetValue(propertyName, out var val))
            return (T)val;
        return defaultValue;
    }

    protected bool SetProperty<T>(T value,
        [CallerMemberName] string propertyName = "")
    {
        if (dictionary.TryGetValue(propertyName, out object val))
        {
            if (val is T typedVal
                && EqualityComparer<T>.Default.Equals(typedVal, value))
                return false;   // no-op if equal
        }
        dictionary[propertyName] = value;
        CallPropertyChanged<T>(propertyName, value);
        return true;
    }
}
```

`IAutoImplemented` is a marker interface. When the `StateManager` detects it, it
skips subscribing to `PropertyChanged`/`PropertyRead` events — because
`BindingObject` subclasses call `StateManager.OnPropertyRead`/`OnPropertyChanged`
directly. External `INotifyPropertyRead` implementors that aren't
`IAutoImplemented` get event subscriptions instead.

### 2.3 State\<T\>

A strongly-typed reactive wrapper. The most common way to declare mutable state
in a Comet view:

```csharp
// State.cs
public class State<T> : BindingObject
{
    T _value;
    bool _hasValue;
    static readonly string ValuePropertyName = "Value";

    public T Value
    {
        get
        {
            CallPropertyRead(ValuePropertyName);
            return _hasValue ? _value : default;
        }
        set
        {
            if (_hasValue && EqualityComparer<T>.Default.Equals(_value, value))
                return;
            _value = value;
            _hasValue = true;
            CallPropertyChanged(ValuePropertyName, value);
            ValueChanged?.Invoke(value);
        }
    }
}
```

Key design decisions:

| Feature | Detail |
|---------|--------|
| **Typed fast path** | `GetValueInternal` returns the typed `_value` directly, skipping the dictionary |
| **Equality check** | Uses `EqualityComparer<T>.Default` — no boxing for value types |
| **`ValueChanged` callback** | `Action<T>` for imperative subscribers who don't need full INPC |
| **Implicit operators** | `T → State<T>`, `State<T> → T`, `State<T> → Action<T>` |

**Usage in a view:**

```csharp
public class Counter : View
{
    readonly State<int> count = 0;   // implicit conversion from int

    [Body]
    View body() => new VStack
    {
        new Text(() => $"Count: {count.Value}"),
        new Button("Increment", () => count.Value++),
    };
}
```

### 2.4 Binding\<T\>

The bridge between state and view properties. A `Binding<T>` can wrap either a
raw value (`IsValue = true`) or a `Func<T>` (`IsFunc = true`):

```csharp
// Binding.cs (base)
public class Binding
{
    public object Value { get; protected set; }
    public bool IsValue { get; internal set; }
    public bool IsFunc { get; internal set; }
    WeakReference _view;
    internal View View { get; set; }           // target view (weak)
    internal View BoundFromView { get; set; }  // monitoring view (weak)
    internal bool IsDirty { get; set; }

    public IReadOnlyList<(INotifyPropertyRead BindingObject, string PropertyName)>
        BoundProperties { get; protected set; }
}
```

The `BoundFromView` vs `View` distinction is critical: `BoundFromView` is the
view whose `Body` lambda created the binding (the "monitoring" view), while
`View` is the control that displays the value (the "target" view). Registering
bindings in the monitoring view's `BindingState` prevents double-dispatch.

---

## 3. Property Tracking Pipeline

The tracking pipeline captures which state properties a view depends on. It
works by intercepting `CallPropertyRead` calls during two phases: **Body
evaluation** and **Binding creation**.

### 3.1 Build-Time Tracking (Body Evaluation)

```
┌──────────────────────────────────────────────────────────┐
│  view.GetRenderView()                                    │
│    ├─ new StateBuilder(view)  ←── pushes view onto stack │
│    │    └─ StateManager.StartBuilding(view)              │
│    ├─ Body.Invoke()                                      │
│    │    └─ any State<T>.Value access fires:              │
│    │         CallPropertyRead("Value")                   │
│    │           └─ StateManager.OnPropertyRead(obj, prop) │
│    │                └─ appends to _currentReadProperties  │
│    ├─ props = StateManager.EndProperty()                 │
│    │    └─ returns accumulated reads, clears list        │
│    ├─ State.AddGlobalProperties(props)                   │
│    └─ StateBuilder.Dispose()                             │
│         └─ StateManager.EndBuilding(view)                │
└──────────────────────────────────────────────────────────┘
```

`_currentReadProperties` is `[ThreadStatic]`, ensuring concurrent builds on
different threads don't interfere.

### 3.2 Binding-Time Tracking (Func\<T\> Bindings)

When a `Binding<T>` wraps a `Func<T>`, it tracks reads during the function's
first evaluation:

```csharp
// Binding.cs
protected void ProcessGetFunc()
{
    StateManager.StartProperty();
    var result = Get == null ? default : Get.Invoke();
    var props = StateManager.EndProperty();
    IsFunc = true;
    CurrentValue = result;
    BoundProperties = props;
    BoundFromView = StateManager.CurrentView;
}
```

`StartProperty()` sets `_isTrackingProperties = true` and flushes any
accumulated reads to the current view's global properties. `EndProperty()`
returns the reads captured during the Func evaluation, deduplicating them
for the multi-property case.

### 3.3 OnPropertyRead Flow

```csharp
// StateManager.cs
public static void OnPropertyRead(object sender, string propertyName)
{
    if (!IsBuilding)
        return;  // outside build phase — ignore
    var currentReadProperties = GetCurrentReadProperties();
    currentReadProperties.Add((sender as INotifyPropertyRead, propertyName));
}
```

`IsBuilding` is true when either the view stack is non-empty (Body evaluation)
or `_isTrackingProperties` is set (Binding Func evaluation). Outside these
windows, reads are silently ignored.

---

## 4. Two Types of State Changes

When a `BindingObject` property changes, `StateManager.OnPropertyChanged` routes
the notification to all views monitoring that object. Each view then calls
`BindingPropertyChanged`, which consults its `BindingState` to determine the
response:

### 4.1 Global Properties → Full Rebuild

A property is "global" if it was read during Body evaluation but wasn't captured
into a specific `Binding<T>`. When it changes, the view must re-execute its Body:

```csharp
// View.cs — BindingPropertyChanged<T>
if (!State.UpdateValue(this, (bindingObject, property),
        fullProperty, value, out bool bindingsHandled))
{
    // UpdateValue returned false → property is in GlobalProperties
    if (StateManager.IsBatching)
        StateManager.AddViewNeedingReload(this);
    else
        Reload(false);  // full rebuild + diff
}
```

### 4.2 View Properties → Targeted Update

A property is a "view property" if it was captured into a `Binding<T>` that's
registered in `ViewUpdateProperties`. The change is routed directly to the
binding, which re-evaluates and calls `ViewPropertyChanged`:

```csharp
// View.cs — BindingPropertyChanged<T> (continued)
else if (!StateManager.IsBatching && !bindingsHandled)
{
    ViewPropertyChanged(property, value);
}
```

`ViewPropertyChanged` updates the property value on the view, then calls
`ViewHandler?.UpdateValue(newPropName)` to push the change to the native
platform control — without rebuilding the view tree.

### Comparison

| Aspect | Global (Rebuild) | View (Targeted) |
|--------|-------------------|-----------------|
| **When** | State read in Body, not in a Binding | State read inside a `Func<T>` binding |
| **Response** | `Reload()` → Body re-invoked → diff | `Binding.EvaluateAndNotify()` → `ViewPropertyChanged` |
| **Cost** | Higher (full tree rebuild + diff) | Lower (single property update) |
| **Typical use** | Conditional rendering, adding/removing views | Text content, colors, numeric values |

---

## 5. The BindingState Machine

`BindingState` (defined in `BindingObject.cs`) is the per-view data structure
that categorizes every state dependency:

```csharp
// BindingObject.cs
public class BindingState
{
    // Properties that require a full rebuild when changed
    public HashSet<(INotifyPropertyRead BindingObject, string PropertyName)>
        GlobalProperties { get; set; } = new();

    // Properties that have targeted bindings
    public Dictionary<
        (INotifyPropertyRead BindingObject, string PropertyName),
        HashSet<(string PropertyName, Binding Binding)>
    > ViewUpdateProperties = new();

    // Tracks all property values that have changed (for hot reload transfer)
    Dictionary<string, object> changeDictionary = new();
}
```

### 5.1 UpdateValue — The Routing Decision

```csharp
// BindingState.cs
public bool UpdateValue<T>(View view,
    (INotifyPropertyRead, string) property,
    string fullProperty, T value,
    out bool bindingsHandled)
{
    changeDictionary[fullProperty] = value;

    // Propagate up the parent chain for hot reload state tracking
    if (view.Parent != null)
        UpdatePropertyChangeProperty(view, fullProperty, value);

    bindingsHandled = false;

    // 1. Check ViewUpdateProperties for targeted bindings
    if (ViewUpdateProperties.TryGetValue(property, out var bindings))
    {
        bindingsHandled = true;
        // Notify each binding (uses ArrayPool to avoid allocation)
        foreach (var binding in bindings)
            binding.Binding.BindingValueChanged(
                property.BindingObject, binding.PropertyName, value);
    }

    // 2. Check if it's a global property
    if (GlobalProperties.Contains(property))
        return false;   // signals caller to Reload()

    return true;        // handled (or not tracked)
}
```

**Return values:**

| Return | `bindingsHandled` | Meaning |
|--------|--------------------|---------|
| `false` | any | Property is global → Reload needed |
| `true` | `true` | Bindings handled the update → no further action |
| `true` | `false` | Property not in any category → `ViewPropertyChanged` fallback |

### 5.2 The changeDictionary

Every property change is recorded in `changeDictionary`. This dictionary is
**not** used for change routing — it's the source of truth for **hot reload
state transfer** (Section 10). When a view is hot-reloaded, its
`ChangedProperties` are replayed onto the replacement view.

---

## 6. Binding Evaluation & Stabilization

### 6.1 EvaluateAndNotify

When a `Binding<T>` with `IsFunc = true` receives a change notification, it
re-evaluates its `Func<T>` and optionally re-tracks dependencies:

```csharp
// Binding.cs
private void EvaluateAndNotify<TVal>(
    INotifyPropertyRead bindingObject, string propertyName, TVal value)
{
    var oldValue = CurrentValue;

    if (IsFunc)
    {
        if (_bindingStable)
        {
            // Fast path: skip property tracking
            CurrentValue = Get == null ? default : Get.Invoke();
        }
        else
        {
            var oldProps = BoundProperties;
            StateManager.StartProperty();
            var result = Get == null ? default : Get.Invoke();
            var props = StateManager.EndProperty();
            CurrentValue = result;
            BoundProperties = props;

            if (ArePropertiesDifferent(BoundProperties, oldProps))
                BindToProperty(View, PropertyName);  // re-register
            else
                _bindingStable = true;  // mark stable
        }
    }
    else
    {
        // Value binding: direct assignment
        CurrentValue = Unsafe.As<TVal, T>(ref value);
    }

    if (!(oldValue?.Equals(CurrentValue) ?? false))
        View?.ViewPropertyChanged(propertyName, CurrentValue);
}
```

### 6.2 The \_bindingStable Optimization

The `_bindingStable` flag is a **critical performance optimization**. Consider
a binding like:

```csharp
new Text(() => $"Count: {count.Value}")
```

The first evaluation tracks that this binding reads `(count, "Value")`. On the
second evaluation (when `count.Value` changes), the binding re-evaluates and
finds the same properties were read. It sets `_bindingStable = true`.

From that point forward, every subsequent evaluation:
- Skips `StateManager.StartProperty()` / `EndProperty()` entirely
- Skips the `ArePropertiesDifferent` check
- Just calls `Get.Invoke()` and compares the result

This avoids list allocation, deduplication, and lock contention in
`StateManager` for stable bindings — which is the common case.

### 6.3 Dynamic Dependency Tracking

For bindings whose dependencies change at runtime:

```csharp
new Text(() => showDetails.Value ? item.Description.Value : item.Title.Value)
```

When `showDetails` toggles, the set of read properties changes. The binding
detects this via `ArePropertiesDifferent`, resets `_bindingStable` to `false`,
and calls `BindToProperty` to re-register with the new set of dependencies.

---

## 7. State Batching

Comet has two levels of batching:

### 7.1 StateManager Batching (Global)

For batching multiple state mutations so that bindings re-evaluate only once:

```csharp
// StateManager.cs
static int _batchDepth;
static readonly List<Binding> _dirtyBindings = new();
static readonly HashSet<View> _viewsNeedingReload = new();

public static void BeginBatch() { _batchDepth++; }

public static void EndBatch()
{
    if (--_batchDepth <= 0)
    {
        _batchDepth = 0;
        FlushBatch();
    }
}

static void FlushBatch()
{
    // 1. Re-evaluate all dirty bindings
    for (int i = 0; i < _dirtyBindings.Count; i++)
        _dirtyBindings[i].Flush();
    _dirtyBindings.Clear();

    // 2. Reload all views with global property changes
    foreach (var v in _viewsNeedingReload)
    {
        if (!v.IsDisposed)
            v.Reload();
    }
    _viewsNeedingReload.Clear();
}
```

Batches are **nestable** — only the outermost `EndBatch` triggers a flush.

During batching, `Binding<T>.BindingValueChanged` defers work:

```csharp
// Binding.cs
public override void BindingValueChanged(
    INotifyPropertyRead bindingObject, string propertyName, object value)
{
    if (IsFunc && StateManager.IsBatching)
    {
        if (!IsDirty)
        {
            IsDirty = true;
            StateManager.AddDirtyBinding(this);
        }
        return;  // deferred
    }
    EvaluateAndNotify(bindingObject, propertyName, value);
}
```

### 7.2 View Batching (Local)

For batching `ViewPropertyChanged` calls on a single view:

```csharp
// View.cs
public void BatchBegin() { _isBatching = true; }
public void BatchCommit()
{
    _isBatching = false;
    // Apply all queued changes, then single handler update
    foreach (var (property, value) in changes)
        this.SetPropertyValue(property, value);
    foreach (var (property, _) in changes)
        ViewHandler?.UpdateValue(GetHandlerPropertyName(property));
    InvalidateMeasurement();
}
```

### 7.3 When to Use Which

| Pattern | Use Case |
|---------|----------|
| `StateManager.BeginBatch()` / `EndBatch()` | Mutating multiple `State<T>` fields at once |
| `Component<TState>.SetState()` | Mutating component state (wraps `BeginBatch`/`EndBatch`) |
| `view.BatchBegin()` / `BatchCommit()` | Bulk-updating view properties from external code |

---

## 8. Component\<TState\> Pattern

The `Component` hierarchy provides a React-like API on top of Comet's `View`.

### 8.1 Base Component

```csharp
// Component.cs
public abstract class Component : View, IComponentWithState
{
    protected Component()
    {
        Body = () => Render();  // wires into existing View pipeline
    }

    public abstract View Render();
    protected virtual void OnMounted() { }
    protected virtual void OnWillUnmount() { }
}
```

By setting `Body = () => Render()`, components plug into the standard
`GetRenderView` → `StateBuilder` → diff pipeline without any special handling.

### 8.2 Component\<TState\>

Adds a typed state object and `SetState` for batched mutations:

```csharp
// Component.cs
public abstract class Component<TState> : Component
    where TState : class, new()
{
    TState _state;
    public new TState State => _state ??= new TState();

    protected void SetState(Action<TState> mutator)
    {
        var state = State;

        StateManager.BeginBatch();
        try { mutator(state); }
        finally { StateManager.EndBatch(); }

        ThreadHelper.RunOnMainThread(() => Reload());
    }
}
```

**Important:** `TState` does NOT need to extend `BindingObject`. It's a plain
class. The `SetState` call always triggers a `Reload`, making it a simpler
(but less granular) alternative to `State<T>` fields.

### 8.3 Component\<TState, TProps\>

Adds parent-supplied props with a `ShouldUpdate` hook:

```csharp
// Component.cs
public abstract class Component<TState, TProps> : Component<TState>
    where TState : class, new()
    where TProps : class, new()
{
    TProps _props;

    public TProps Props
    {
        get => _props ??= new TProps();
        set
        {
            _props = value ?? new TProps();
            ThreadHelper.RunOnMainThread(() => Reload());
        }
    }

    protected virtual bool ShouldUpdate(TProps oldProps, TProps newProps)
        => true;  // default: always re-render
}
```

### 8.4 State Transfer for Components

The `IComponentWithState` interface enables hot reload to transfer state:

```csharp
// IComponentWithState.cs
public interface IComponentWithState
{
    object GetStateObject();
    void TransferStateFrom(IComponentWithState source);
}
```

During hot reload, `TransferHotReloadStateToCore` checks for this interface:

```csharp
// Component.cs
protected override void TransferHotReloadStateToCore(View newView)
{
    base.TransferHotReloadStateToCore(newView);
    if (newView is IComponentWithState newComponent &&
        this is IComponentWithState currentComponent)
    {
        newComponent.TransferStateFrom(currentComponent);
    }
}
```

---

## 9. Environment System

The environment is a hierarchical key-value store for cross-cutting concerns
(colors, fonts, layout properties). It integrates deeply with state management.

### 9.1 The Static Environment

```csharp
// ContextualObject.cs (base class of View)
internal static readonly EnvironmentData Environment = new EnvironmentData();
```

`EnvironmentData` extends `BindingObject`, which means it implements
`INotifyPropertyRead`. This is the key insight: **the global environment is a
BindingObject**, so changes to it flow through the same `StateManager` pipeline
as any other state change.

### 9.2 Environment Fields

Views declare environment dependencies with `[Environment]`:

```csharp
public class MyView : View
{
    [Environment] readonly IThemeService themeService;
}
```

During construction, `SetEnvironmentFields()` records these in
`usedEnvironmentData` and registers them as global properties:

```csharp
// View.cs
void SetEnvironmentFields()
{
    var fields = this.GetFieldsWithAttribute(typeof(EnvironmentAttribute));
    foreach (var f in fields)
    {
        var key = attribute.Key ?? f.Name;
        usedEnvironmentData.Add((f.Name, key));
        State.AddGlobalProperty((View.Environment, key));
    }
}
```

### 9.3 PopulateFromEnvironment

Before Body evaluation (and during `ResetView`), environment fields are
populated by walking the context hierarchy and DI container:

```csharp
// View.cs
void PopulateFromEnvironment()
{
    foreach (var item in usedEnvironmentData)
    {
        var value = this.GetEnvironment(item.Key);
        // Fallback: try DI container
        if (value == null)
        {
            var mauiContext = GetMauiContext();
            var service = mauiContext?.Services.GetService(prop.FieldType);
            if (service != null) value = service;
        }
        // Fallback: try replaced view (hot reload)
        // Fallback: try first-char-uppercased key

        if (value != null)
        {
            StateManager.ListenToEnvironment(this);
            State.AddGlobalProperty((View.Environment, key));
            this.SetDeepPropertyValue(item.Field, value);
        }
    }
}
```

### 9.4 Global Environment Changes

When `View.SetGlobalEnvironment(key, value)` is called, the change is pushed
to **all** active views via `ViewPropertyChanged`:

```csharp
// View.cs
public static void SetGlobalEnvironment(string key, object value)
{
    Environment.SetValue(key, value, true);
    ThreadHelper.RunOnMainThread(() => {
        List<View> views;
        lock (ActiveViewsLock)
            views = ActiveViews.OfType<View>().ToList();
        views.ForEach(x => x.ViewPropertyChanged(key, value));
    });
}
```

### 9.5 How Fluent APIs Set Environment Data

The fluent extension methods (e.g., `.Background(Colors.Red)`) call
`SetPropertyInContext`, which writes to the view's local `EnvironmentData`:

```csharp
// View.cs
internal T SetPropertyInContext<T>(T value,
    [CallerMemberName] string property = null)
{
    this.SetEnvironment(property, value, false);
    return value;
}
```

When `SetEnvironment` fires, it triggers `ContextPropertyChanged`, which calls
`ViewPropertyChanged`, routing the update to the handler.

---

## 10. Hot Reload State Transfer

When a view type is hot-reloaded, the old view's state must be transferred to
the replacement:

```
┌─────────────┐    TransferHotReloadStateTo()    ┌─────────────┐
│  Old View    │ ──────────────────────────────► │  New View    │
│              │                                  │              │
│  State:      │    ChangedProperties copied      │  State:      │
│  count = 5   │ ──────────────────────────────► │  count = 5   │
│  name = "Hi" │                                  │  name = "Hi" │
└─────────────┘                                  └─────────────┘
```

### 10.1 The Core Mechanism

```csharp
// View.cs
protected virtual void TransferHotReloadStateToCore(View newView)
{
    var oldState = this.GetState();
    if (oldState == null) return;

    var changes = oldState.ChangedProperties;  // the changeDictionary
    foreach (var change in changes)
    {
        newView.SetDeepPropertyValue(change.Key, change.Value);
    }
}
```

This relies on the `changeDictionary` in `BindingState`, which records every
property change that has occurred on the view. During transfer, each changed
property is set on the new view instance via reflection.

### 10.2 Component State Transfer

`Component` subclasses override `TransferHotReloadStateToCore` to also transfer
their typed state object:

```csharp
// Component.cs
protected override void TransferHotReloadStateToCore(View newView)
{
    base.TransferHotReloadStateToCore(newView);
    if (newView is IComponentWithState newComponent &&
        this is IComponentWithState currentComponent)
    {
        newComponent.TransferStateFrom(currentComponent);
    }
}
```

For `Component<TState>`, `TransferStateFrom` simply copies the `_state`
reference:

```csharp
public override void TransferStateFrom(IComponentWithState source)
{
    if (source is Component<TState> typed && typed._state != null)
        _state = typed._state;
}
```

### 10.3 Hot Reload Flow

```
IDE detects change
  └─ MauiHotReloadHelper.RegisterReplacedView(className, newType)
      └─ MauiHotReloadHelper.TriggerReload()
          └─ IHotReloadableView.Reload()  (on each registered view)
              └─ View.Reload(isHotReload: true)
                  └─ ResetView(isHotReload: true)
                      ├─ GetRenderView()
                      │    ├─ MauiHotReloadHelper.GetReplacedView(this)
                      │    ├─ SetHotReloadReplacement(replaced)
                      │    │    ├─ TransferHotReloadStateTo(replacement)
                      │    │    └─ PopulateFromEnvironment()
                      │    └─ replacement.GetRenderView()
                      └─ view.Diff(oldView, isHotReload: true)
```

---

## 11. Source Generation

### 11.1 AutoNotifyAttribute

The `AutoNotifyGenerator` is a Roslyn source generator that transforms annotated
fields into full properties with read/change notifications:

```csharp
// AutoNotifyGenerator.cs — generated property template
public FieldType PropertyName
{
    get
    {
        StateManager.OnPropertyRead(this, nameof(PropertyName));
        this.PropertyRead?.Invoke(this,
            new PropertyChangedEventArgs(nameof(PropertyName)));
        return this._fieldName;
    }
    set
    {
        this._fieldName = value;
        StateManager.OnPropertyChanged(this, nameof(PropertyName), value);
        this.PropertyChanged?.Invoke(this,
            new PropertyChangedEventArgs(nameof(PropertyName)));
    }
}
```

### 11.2 How It Works

The generator implements `ISourceGenerator` with a `SyntaxReceiver`:

1. **Registration:** In `Initialize`, registers `AutoNotifyAttribute` as a
   post-initialization source and a `SyntaxReceiver` that collects fields with
   attributes.

2. **Collection:** `SyntaxReceiver.OnVisitSyntaxNode` scans every
   `FieldDeclarationSyntax` with attributes, keeping those annotated with
   `[AutoNotify]`.

3. **Generation:** For each class containing annotated fields, emits a partial
   class that:
   - Implements `INotifyPropertyRead` and `IAutoImplemented`
   - Adds `PropertyChanged` and `PropertyRead` events (if not already present)
   - Generates a property for each field with get/set that call StateManager

### 11.3 Naming Convention

Field names are converted to property names by removing the `_` prefix and
capitalizing:

```
_count     → Count
_userName  → UserName
_x         → X
```

A custom name can be specified: `[AutoNotify(PropertyName = "MyCustomName")]`.

### 11.4 Constraints

- Classes must be **top-level** (not nested) — the generator reports
  `AutoGen101` diagnostic otherwise.
- Classes must be **partial** (implied by generated partial class output).
- Fields should be private/internal; the generated property is public.

---

## 12. Threading & Concurrency

### 12.1 ReaderWriterLockSlim

`StateManager` uses a single `ReaderWriterLockSlim` to protect its shared
dictionaries:

```csharp
// StateManager.cs
static readonly ReaderWriterLockSlim _rwLock = new ReaderWriterLockSlim();
```

**Write lock** is taken for:
- `ConstructingView` — registering view→object mappings
- `Disposing` — removing view mappings
- `RegisterChild`, `StartMonitoring`, `StopMonitoring` — modifying monitored objects
- `UpdateBinding` — adding binding→view mappings
- `ListenToEnvironment` — adding environment listeners
- Lazy disposed-view cleanup in `OnPropertyChanged`

**Read lock** is taken for:
- `OnPropertyChanged` — looking up which views to notify

### 12.2 ThreadStatic Fields

Per-thread state avoids lock contention during the hot path:

```csharp
// StateManager.cs
[ThreadStatic] static WeakStack<View> _viewStack;
[ThreadStatic] static List<(INotifyPropertyRead, string)> _currentReadProperties;
[ThreadStatic] static bool _isTrackingProperties;
[ThreadStatic] static List<(INotifyPropertyRead, string)> _endPropertyBuffer;
[ThreadStatic] static HashSet<(INotifyPropertyRead, string)> _endPropertySeen;
```

This means multiple threads can build views concurrently without contention on
the read-tracking lists. The view stack is a `WeakStack<View>` to avoid
retaining views.

### 12.3 WeakReferences

Comet uses weak references extensively to avoid memory leaks:

| Location | What's Weak | Why |
|----------|-------------|-----|
| `Binding._view` | Target view | Binding shouldn't prevent view GC |
| `Binding._boundFromView` | Monitoring view | Same reason |
| `View.parent` | Parent view | Child shouldn't retain parent |
| `View.__viewThatWasReplaced` | Hot reload predecessor | Old view should be GC'd |
| `StateManager.LastView` | Last constructed view | Convenience ref, must not leak |
| `ActiveViews` | `WeakList<IView>` | Global list must not prevent GC |

### 12.4 ArrayPool Usage

High-frequency operations use `System.Buffers.ArrayPool` to avoid allocations:

```csharp
// StateManager.cs — OnPropertyChanged (multi-view path)
viewsCopy = System.Buffers.ArrayPool<View>.Shared.Rent(viewCount);
views.CopyTo(viewsCopy);
// ... iterate ...
System.Buffers.ArrayPool<View>.Shared.Return(viewsCopy, true);

// BindingState.cs — UpdateValue
var bindingsArray = System.Buffers.ArrayPool<(string, Binding)>.Shared.Rent(count);
bindings.CopyTo(bindingsArray);
// ... iterate ...
System.Buffers.ArrayPool<(string, Binding)>.Shared.Return(bindingsArray, true);
```

The `true` parameter in `Return` clears the array, preventing stale references
from keeping objects alive.

---

## 13. Implicit Conversions & Pitfalls

### 13.1 Conversion Table

| From | To | Mechanism | Notes |
|------|----|-----------|-------|
| `T` | `State<T>` | `State<T>(T value)` | Creates new State wrapper |
| `State<T>` | `T` | Returns `state.Value` | **Triggers read tracking** |
| `State<T>` | `Action<T>` | Returns setter lambda | For two-way binding |
| `T` | `Binding<T>` | Calls `StateManager.EndProperty()` | Captures reads since last `StartProperty` |
| `Func<T>` | `Binding<T>` | Calls `ProcessGetFunc()` | Evaluates func, tracks reads |
| `State<T>` | `Binding<T>` | Creates func binding | Wraps `() => state.Value` |
| `Binding<T>` | `T` | Returns `CurrentValue` | Does NOT trigger tracking |

### 13.2 The T → Binding\<T\> Conversion (Most Complex)

```csharp
// Binding.cs
public static implicit operator Binding<T>(T value)
{
    var props = StateManager.EndProperty();

    // Multiple properties read → global (forces Reload)
    if (props?.Count > 1)
    {
        StateManager.CurrentView.GetState().AddGlobalProperties(props);
    }
    // Single property from State<T> → optimize to Binding from State
    else if (props?.Count == 1 && props[0].BindingObject is State<T> state)
    {
        return state;  // uses State<T> → Binding<T> conversion
    }

    return new Binding<T>()
    {
        IsValue = true,
        CurrentValue = value,
        BoundProperties = props,
        BoundFromView = StateManager.CurrentView,
    };
}
```

### 13.3 Common Pitfalls

**Pitfall 1: String interpolation creates global bindings.**

```csharp
// BAD — multiple state reads in interpolated string → global property
new Text($"Name: {name.Value}, Age: {age.Value}")
// This triggers a warning:
// "Warning: property is using Multiple state Variables.
//  For performance reasons, please switch to a Lambda."

// GOOD — lambda creates a Func binding → targeted updates
new Text(() => $"Name: {name.Value}, Age: {age.Value}")
```

**Pitfall 2: Formatted text with single state falls back to global.**

```csharp
// BAD — the evaluated string differs from the raw State value
new Text($"Count: {count.Value}")
// BindToProperty detects stateValue != CurrentValue → marks as global

// GOOD
new Text(() => $"Count: {count.Value}")
```

**Pitfall 3: Reading State<T>.Value outside Body/Binding context.**

```csharp
// No tracking occurs — IsBuilding is false
void SomeMethod()
{
    var x = myState.Value;  // read is silently ignored
}
```

**Pitfall 4: State fields must be `readonly`.**

```csharp
// THROWS ReadonlyRequiresException at construction time
State<int> count = 0;      // not readonly!

// CORRECT
readonly State<int> count = 0;
```

The framework enforces this in `CheckForStateAttributes`:

```csharp
if (!field.IsInitOnly)
    throw new ReadonlyRequiresException(field.DeclaringType?.FullName, field.Name);
```

---

## 14. Lifecycle & Cleanup

### 14.1 View Construction

```
new View()
  ├─ ActiveViews.Add(this)
  ├─ MauiHotReloadHelper.Register(this)
  ├─ State = new BindingState()
  ├─ StateManager.ConstructingView(this)
  │    ├─ LastView = new WeakReference(this)
  │    ├─ CheckForStateAttributes(this)
  │    │    ├─ Find State<T> and [State] fields via reflection
  │    │    ├─ Verify readonly (throw if not)
  │    │    ├─ RegisterChild(view, child, fieldName)
  │    │    └─ Add to ViewObjectMappings & NotifyToViewMappings
  │    └─ Flush any pending reads to current view's global props
  └─ SetEnvironmentFields()
       └─ Record [Environment] fields in usedEnvironmentData
```

### 14.2 View Disposal

```csharp
// View.cs
protected virtual void Dispose(bool disposing)
{
    ActiveViews.Remove(this);
    MauiHotReloadHelper.UnRegister(this);

    ViewHandler = null;
    replacedView?.Dispose();
    builtView?.Dispose();
    body = null;
    Context(false)?.Clear();

    StateManager.Disposing(this);    // ← cleanup mappings
    State?.Clear();
    State = null;
}
```

### 14.3 StateManager.Disposing

The disposal cleanup must avoid lock re-entrancy. It collects objects that need
event unsubscription inside the write lock, then unsubscribes outside:

```csharp
// StateManager.cs
public static void Disposing(View view)
{
    List<INotifyPropertyRead> toStopMonitoring = null;

    _rwLock.EnterWriteLock();
    try
    {
        if (ViewObjectMappings.TryGetValue(view.Id, out var mappings))
        {
            foreach (var obj in mappings)
            {
                if (NotifyToViewMappings.TryGetValue(obj, out var views))
                {
                    views.Remove(view);
                    if (views.Count == 0)
                    {
                        NotifyToViewMappings.Remove(obj);
                        if (MonitoredObjects.Remove(obj))
                        {
                            toStopMonitoring ??= new();
                            toStopMonitoring.Add(obj);
                        }
                    }
                }
            }
            ViewObjectMappings.Remove(view.Id);
        }
    }
    finally { _rwLock.ExitWriteLock(); }

    // Unsubscribe events outside lock
    if (toStopMonitoring != null)
    {
        foreach (var obj in toStopMonitoring)
        {
            if (!(obj is IAutoImplemented))
            {
                obj.PropertyChanged -= Obj_PropertyChanged;
                obj.PropertyRead -= Obj_PropertyRead;
            }
        }
    }
}
```

### 14.4 Disposed View Cleanup in OnPropertyChanged

When `OnPropertyChanged` encounters a disposed view while iterating, it removes
it from the `NotifyToViewMappings` set after iteration completes (to avoid
modifying the set during iteration):

```csharp
if (disposedViews?.Count > 0)
{
    _rwLock.EnterWriteLock();
    try
    {
        foreach (var view in disposedViews)
            viewsForCleanup.Remove(view);
    }
    finally { _rwLock.ExitWriteLock(); }
}
```

---

## 15. Architecture Diagrams

### 15.1 State Change Flow — Complete Pipeline

```
         ┌─────────────────────────────────────────────────────────┐
         │              User Code: state.Value = 42                │
         └───────────────────────┬─────────────────────────────────┘
                                 │
                                 ▼
         ┌─────────────────────────────────────────────────────────┐
         │  State<T>.Value setter                                  │
         │    ├─ Equality check → skip if equal                    │
         │    ├─ _value = 42                                       │
         │    ├─ CallPropertyChanged("Value", 42)                  │
         │    │    └─ StateManager.OnPropertyChanged(this, "Value") │
         │    └─ ValueChanged?.Invoke(42)                          │
         └───────────────────────┬─────────────────────────────────┘
                                 │
                                 ▼
         ┌─────────────────────────────────────────────────────────┐
         │  StateManager.OnPropertyChanged                         │
         │    ├─ Look up NotifyToViewMappings[state]               │
         │    ├─ Resolve property name (parent.child)              │
         │    └─ For each view:                                    │
         │         view.BindingPropertyChanged(state, prop, value) │
         └───────────────────────┬─────────────────────────────────┘
                                 │
                        ┌────────┴────────┐
                        ▼                 ▼
              ┌──────────────┐   ┌───────────────────┐
              │ GlobalProp?  │   │ ViewUpdateProp?    │
              │   (Rebuild)  │   │   (Targeted)       │
              └──────┬───────┘   └─────────┬─────────┘
                     │                     │
                     ▼                     ▼
              ┌──────────────┐   ┌───────────────────┐
              │ view.Reload()│   │ binding.Evaluate   │
              │  ResetView() │   │  AndNotify()       │
              │  Body.Invoke │   │  ├─ Re-eval Func   │
              │  Diff(old)   │   │  └─ ViewProperty   │
              └──────────────┘   │     Changed()      │
                                 └─────────┬─────────┘
                                           │
                                           ▼
                                 ┌───────────────────┐
                                 │ ViewHandler        │
                                 │  .UpdateValue()    │
                                 │  → native update   │
                                 └───────────────────┘
```

### 15.2 Dependency Tracking During Body Build

```
                  View.GetRenderView()
                          │
           ┌──────────────┴───────────────┐
           ▼                              │
    StateBuilder(view)                    │
    ┌───────────────────────┐             │
    │ Push view onto stack  │             │
    │ Start tracking reads  │             │
    └──────────┬────────────┘             │
               │                          │
               ▼                          │
    Body.Invoke()                         │
    ┌───────────────────────────┐         │
    │ new VStack {              │         │
    │   new Text(               │         │
    │     () => $"{count.Value}"│──► Func binding tracks:
    │   ),                      │    (count, "Value") → ViewUpdateProp
    │   new Text(name.Value),   │──► Raw read (in Body):
    │   ...                     │    (name, "Value") → GlobalProp
    │ }                         │         │
    └───────────────────────────┘         │
               │                          │
               ▼                          │
    EndProperty() → remaining reads       │
    added as GlobalProperties             │
               │                          │
           ┌───┴───────────────────┐      │
           ▼                       │      │
    StateBuilder.Dispose()         │      │
    ┌───────────────────────┐      │      │
    │ Pop view from stack   │      │      │
    │ Clear read list       │      │      │
    └───────────────────────┘      │      │
                                   ▼      │
                              BindToProperty() for each
                              Binding<T> with BoundProperties
                              → registers in BindingState
```

### 15.3 Batching Timeline

```
    Time ──────────────────────────────────────────────────────►

    BeginBatch()
      │
      ├── state1.Value = "a"
      │     └── Binding1 marks IsDirty ──┐
      │                                   │ (deferred)
      ├── state2.Value = 42               │
      │     └── Binding2 marks IsDirty ──┤
      │                                   │
      ├── state1.Value = "b"              │
      │     └── Binding1 already dirty ──┤ (no-op)
      │                                   │
    EndBatch()                            │
      │                                   │
      ├── FlushBatch()  ◄────────────────┘
      │     ├── Binding1.Flush() → evaluates ONCE with "b"
      │     ├── Binding2.Flush() → evaluates ONCE with 42
      │     └── Reload deferred views (if any global changes)
      │
      ▼
    Single render pass
```

### 15.4 StateManager Static Dictionaries

```
┌─────────────────────────────────────────────────────────┐
│                    StateManager                          │
│                                                         │
│  ViewObjectMappings                                     │
│  ┌──────────────┬───────────────────────────┐          │
│  │ view.Id      │ List<INotifyPropertyRead> │          │
│  │ "view-abc"   │ [countState, nameState]   │          │
│  │ "view-def"   │ [itemState]               │          │
│  └──────────────┴───────────────────────────┘          │
│                                                         │
│  NotifyToViewMappings                                   │
│  ┌─────────────────────┬────────────────────┐          │
│  │ INotifyPropertyRead │ HashSet<View>      │          │
│  │ countState          │ {view-abc}         │          │
│  │ nameState           │ {view-abc, view-x} │          │
│  │ itemState           │ {view-def}         │          │
│  └─────────────────────┴────────────────────┘          │
│                                                         │
│  ChildPropertyNamesMapping                              │
│  ┌─────────────────────┬──────────────────────────┐    │
│  │ INotifyPropertyRead │ Dict<viewId, fieldName>  │    │
│  │ countState          │ {"view-abc": "count"}    │    │
│  └─────────────────────┴──────────────────────────┘    │
│                                                         │
│  MonitoredObjects: [countState, nameState, itemState]   │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

### 15.5 BindingState Per-View Structure

```
┌─────────────────────────────────────────────────────────┐
│  View "Counter" — BindingState                          │
│                                                         │
│  GlobalProperties:                                      │
│  ┌──────────────────────────────────────┐               │
│  │ (nameState, "Value")                 │ ← full rebuild│
│  └──────────────────────────────────────┘               │
│                                                         │
│  ViewUpdateProperties:                                  │
│  ┌──────────────────────────┬───────────────────┐       │
│  │ (countState, "Value")    │ Set of bindings:  │       │
│  │                          │ ("Text", binding1)│       │
│  └──────────────────────────┴───────────────────┘       │
│                                                         │
│  changeDictionary:                                      │
│  ┌──────────────┬───────────────────────┐               │
│  │ "count.Value"│ 42                    │ ← for hot     │
│  │ "name.Value" │ "Hello"               │    reload     │
│  └──────────────┴───────────────────────┘               │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

---

## Quick Reference

### Setting Up State in a View

```csharp
public class MyView : View
{
    // State fields MUST be readonly
    readonly State<string> name = "World";
    readonly State<int> count = 0;

    [Body]
    View body() => new VStack
    {
        // Func binding → targeted updates (preferred)
        new Text(() => $"Hello, {name.Value}! Count: {count.Value}"),

        // Direct state binding → also targeted
        new TextField(name),

        // Action triggers state mutation → automatic UI update
        new Button("Increment", () => count.Value++),
    };
}
```

### Using Batching

```csharp
// Multiple mutations, single re-render
StateManager.BeginBatch();
try
{
    firstName.Value = "John";
    lastName.Value = "Doe";
    age.Value = 30;
}
finally
{
    StateManager.EndBatch();
}
```

### Using Component\<TState\>

```csharp
class CounterState
{
    public int Count { get; set; }
}

class CounterComponent : Component<CounterState>
{
    public override View Render() => new VStack
    {
        new Text($"Count: {State.Count}"),
        new Button("Add", () => SetState(s => s.Count++)),
    };
}
```

### Using AutoNotify

```csharp
public partial class TodoItem
{
    [AutoNotify] string _title;
    [AutoNotify] bool _isComplete;
}

// Generated:
//   public string Title { get { ... OnPropertyRead ... } set { ... OnPropertyChanged ... } }
//   public bool IsComplete { get { ... } set { ... } }
```
