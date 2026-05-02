# COMPREHENSIVE DEEP DIVE: COMET STATE MANAGEMENT SYSTEM

**Framework**: Comet (MVU framework built on .NET MAUI)
**Version**: Based on current main branch

---

## Executive Summary

Comet's state management is built around a simple but powerful idea: **record which observable properties are read while building UI, then use that read-set to route later writes**.

That gives the framework two important capabilities at the same time:

- it can do **targeted property updates** when a binding maps cleanly to a control property
- it can do **full view rebuilds** when a state read affected view structure or environment composition

The implementation is centered on `INotifyPropertyRead`, `BindingObject`, `State<T>`, `Binding<T>`, `BindingState`, and `StateManager`.
Classic `View`-based pages and newer `Component<TState>` pages both ride on the same underlying engine.

In short, Comet is neither a plain `INotifyPropertyChanged` binding system nor a naive always-rerender MVU engine.
It is a hybrid dependency-tracking system that classifies state usage and chooses the cheapest safe update path.

---

## 1. STATE<T> CLASS - Full Implementation

**File**: `src/Comet/State.cs`

The `State<T>` class is the core reactive container for mutable data. It:
- Extends `BindingObject` to participate in the property change notification system
- Provides automatic INotifyPropertyRead/INotifyPropertyChanged events
- Optimizes for the common case of a single "Value" property
- Supports implicit conversions for seamless API integration

### Key Implementation Details

```csharp
public class State<T> : BindingObject
{
    T _value;
    bool _hasValue;
    static readonly string ValuePropertyName = "Value";

    // Constructor with initial value
    public State(T value)
    {
        _value = value;
        _hasValue = true;
        dictionary[ValuePropertyName] = value;  // Stored in parent's dictionary
    }

    // Constructor for uninitialized state
    public State() { }

    // The Value property with fast equality checking
    public T Value
    {
        get
        {
            CallPropertyRead(ValuePropertyName);  // Track reads for binding dependency tracking
            return _hasValue ? _value : default;
        }
        set
        {
            // Fast typed equality check — no dictionary lookup, no boxing
            if (_hasValue && EqualityComparer<T>.Default.Equals(_value, value))
                return;

            _value = value;
            _hasValue = true;

            CallPropertyChanged(ValuePropertyName, value);
            ValueChanged?.Invoke(value);
        }
    }

    // Override to return typed value without dictionary lookup
    internal override (bool hasValue, object value) GetValueInternal(string propertyName)
    {
        if (propertyName == ValuePropertyName)
            return (_hasValue, _value);
        return base.GetValueInternal(propertyName);
    }

    // Implicit Conversions for seamless API
    public static implicit operator T(State<T> state) => state.Value;                    // State<int> → int
    public static implicit operator Action<T>(State<T> state) => value => state.Value = value;  // State<int> → Action<int>
    public static implicit operator State<T>(T value) => new State<T>(value);            // int → State<int>

    public Action<T> ValueChanged { get; set; }  // Direct value change callback
}
```

### How Value Property Works

1. **Get**: Calls `CallPropertyRead()` to notify StateManager of read (for dependency tracking in Binding<T> functions)
2. **Set**: Uses fast `EqualityComparer<T>.Default.Equals()` to avoid re-notification if value hasn't changed
3. **Storage**: Uses internal `_value` field for performance, with `_hasValue` flag for uninitialized states

### StateBuilder Class

```csharp
public class StateBuilder : IDisposable
{
    public StateBuilder(View view)
    {
        View = view;
        StateManager.StartBuilding(view);  // Enables property read tracking
    }

    public View View { get; private set; }

    public void Dispose()
    {
        StateManager.EndBuilding(View);
        View = null;
    }
}
```

Used as a using block during View rendering to collect property read dependencies.

---

## 2. BINDINGOBJECT - Property Tracking & Notifications

**File**: `src/Comet/BindingObject.cs`

`BindingObject` is the base class for any reactive object that needs property change notification.

### Core Interface

```csharp
public interface INotifyPropertyRead : INotifyPropertyChanged
{
    event PropertyChangedEventHandler PropertyRead;  // New event: tracks property reads
}

public class BindingObject : INotifyPropertyRead, IAutoImplemented
{
    public event PropertyChangedEventHandler PropertyRead;
    public event PropertyChangedEventHandler PropertyChanged;

    // Internal dictionary stores all property values
    internal protected Dictionary<string, object> dictionary = new Dictionary<string, object>();
```

### Property Read/Change Tracking

```csharp
// Get a property value with automatic read notification
protected T GetProperty<T>(T defaultValue = default, [CallerMemberName] string propertyName = "")
{
    CallPropertyRead(propertyName);  // Notify StateManager of read
    
    if (dictionary.TryGetValue(propertyName, out var val))
        return (T)val;
    return defaultValue;
}

// Called when a property is read
protected virtual void CallPropertyRead(string propertyName)
{
    StateManager.OnPropertyRead(this, propertyName);
    if (PropertyRead != null)
        PropertyRead.Invoke(this, GetCachedArgs(propertyName));
}

// Set a property with change notification
protected bool SetProperty<T>(T value, [CallerMemberName] string propertyName = "")
{
    if (dictionary.TryGetValue(propertyName, out object val))
    {
        if (val is T typedVal && EqualityComparer<T>.Default.Equals(typedVal, value))
            return false;  // No change
    }

    dictionary[propertyName] = value;
    CallPropertyChanged<T>(propertyName, value);
    return true;
}

// Called when a property changes
protected virtual void CallPropertyChanged<T>(string propertyName, T value)
{
    StateManager.OnPropertyChanged(this, propertyName, value);
    if (PropertyChanged != null)
        PropertyChanged.Invoke(this, GetCachedArgs(propertyName));
}
```

### GetValueInternal - For Efficient StateManager Dispatch

```csharp
// Returns (hasValue, value) tuple instead of boxing
internal virtual (bool hasValue, object value) GetValueInternal(string propertyName)
{
    if (string.IsNullOrWhiteSpace(propertyName))
        return (false, null);
    var hasValue = dictionary.TryGetValue(propertyName, out var val);
    return (hasValue, val);
}
```

### BindingState - View's State Dictionary

```csharp
public class BindingState
{
    public IEnumerable<KeyValuePair<string, object>> ChangedProperties => changeDictionary;
    Dictionary<string, object> changeDictionary = new Dictionary<string, object>();

    // Properties that affect ALL views (multiple dependencies)
    public HashSet<(INotifyPropertyRead BindingObject, string PropertyName)> GlobalProperties { get; set; }
        = new HashSet<(INotifyPropertyRead BindingObject, string PropertyName)>();

    // Property → Bindings that react to it
    public Dictionary<(INotifyPropertyRead BindingObject, string PropertyName), 
        HashSet<(string PropertyName, Binding Binding)>> ViewUpdateProperties
        = new Dictionary<(INotifyPropertyRead BindingObject, string PropertyName), 
            HashSet<(string PropertyName, Binding Binding)>>();

    // Tracks changed properties for hot reload state transfer
    public void UpdateValue<T>(View view, (INotifyPropertyRead BindingObject, string PropertyName) property, 
        string fullProperty, T value, out bool bindingsHandled)
    {
        changeDictionary[fullProperty] = value;
        
        // Check if there are specific bindings that handle this property
        if (ViewUpdateProperties.TryGetValue((property.BindingObject, property.PropertyName), out var bindings))
        {
            // Notify each bound property through the binding
            foreach (var binding in bindings)
                binding.Binding.BindingValueChanged(property.BindingObject, binding.PropertyName, value);
            bindingsHandled = true;
        }
        
        // If property is global (affects entire view), return false to trigger full reload
        if (GlobalProperties.Contains(property))
            return false;  // Trigger full view reload
        return true;  // Property is local, doesn't need reload
    }
}
```

---

## 3. BINDING<T> - Wraps Values vs Func<T> with Dependency Tracking

**File**: `src/Comet/Binding.cs`

`Binding<T>` is the glue layer between reactive State/BindingObject properties and View properties.

### Key Distinction: Value vs Func

### Stabilization

`Binding<T>` uses the `_bindingStable` flag to skip dependency retracking once a function binding proves that its `BoundProperties` set has stopped changing.

```csharp
public class Binding<T> : Binding
{
    Func<T> Get { get; set; }      // For Func-based bindings (auto re-evaluated on dependency change)
    Action<T> Set { get; set; }    // For two-way binding back to state

    public bool IsValue { get; internal set; }  // Binding created from a plain value (Binding<T> = myState)
    public bool IsFunc { get; internal set; }   // Binding created from a function (Binding<T> = () => myState.Value)

    // The current computed value
    public T CurrentValue { get => Value == null ? default : (T)Value; private set => Value = value; }

    // Record which properties this binding depends on
    public IReadOnlyList<(INotifyPropertyRead BindingObject, string PropertyName)> BoundProperties { get; protected set; }

    // Track if binding's dependencies have stabilized (optimization)
    bool _bindingStable;

    // Implicit conversion FROM plain value
    public static implicit operator Binding<T>(T value)
    {
        var props = StateManager.EndProperty();  // Stop tracking properties
        
        // Check if this simple value actually read any state properties
        if (props?.Count > 1)
        {
            // Multiple state properties read → convert to Global property
            StateManager.CurrentView.GetState().AddGlobalProperties(props);
        }
        else if (props?.Count == 1 && props[0].BindingObject is State<T> state)
        {
            // Single State<T> read → use it directly (most optimized path)
            return state;  // Use State<T>'s implicit operator
        }
        
        return new Binding<T>()
        {
            IsValue = true,
            CurrentValue = value,
            BoundProperties = props,
            BoundFromView = StateManager.CurrentView
        };
    }

    // Implicit conversion FROM Func<T>
    public static implicit operator Binding<T>(Func<T> value)
        => new Binding<T>(getValue: value, setValue: null);

    // Implicit conversion FROM State<T>
    public static implicit operator Binding<T>(State<T> state)
    {
        StateManager.StartProperty();      // Start tracking reads
        var result = state.Value;          // Read the value (triggering CallPropertyRead)
        var props = StateManager.EndProperty();  // Stop tracking, get list of reads

        var binding = new Binding<T>(
            getValue: () => state.Value,   // Getter delegates to state
            setValue: (v) => state.Value = v)  // Setter delegates to state
        {
            CurrentValue = result,
            BoundProperties = props,
            IsFunc = true,
        };
        return binding;
    }
}
```

### Automatic Dependency Tracking in Func

```csharp
protected void ProcessGetFunc()
{
    StateManager.StartProperty();  // Begin tracking all property reads
    var result = Get == null ? default : Get.Invoke();  // Execute function
    var props = StateManager.EndProperty();  // Collect all properties that were read

    IsFunc = true;
    CurrentValue = result;
    BoundProperties = props;  // Store dependency list for later re-evaluation
    BoundFromView = StateManager.CurrentView;
}
```

When `Binding<T>` wraps a `Func<T>`:
1. **StateManager.StartProperty()** - Begins listening for all property reads
2. **Func.Invoke()** - Executes the lambda, which reads state properties
3. **StateManager.EndProperty()** - Stops listening and returns list of read properties
4. **Re-evaluation**: When any of these properties change, the Func is automatically re-invoked

### Value Change Handling with Batching

```csharp
public override void BindingValueChanged(INotifyPropertyRead bindingObject, string propertyName, object value)
{
    // When batching, defer Func re-evaluation to avoid redundant work
    if (IsFunc && StateManager.IsBatching)
    {
        if (!IsDirty)
        {
            IsDirty = true;
            StateManager.AddDirtyBinding(this);  // Queue for later flushing
        }
        return;
    }
    EvaluateAndNotify(bindingObject, propertyName, value);
}

// Flush deferred binding updates when batch ends
internal override void Flush()
{
    if (!IsDirty) return;
    IsDirty = false;
    EvaluateAndNotify(null, PropertyName, null);
}
```

### Binding Registration with View

```csharp
public void BindToProperty(View view, string property)
{
    PropertyName = property;
    View = view;
    
    if (IsFunc && BoundProperties?.Count > 0)
    {
        // Register in the monitoring view's BindingState (the view that declared the State<T>),
        // NOT the target view. This prevents double-dispatch.
        var monitoringView = BoundFromView ?? view;
        StateManager.UpdateBinding(this, monitoringView);
        monitoringView.GetState().AddViewProperty(BoundProperties, this, property);
        return;
    }
    
    if (IsValue && BoundProperties?.Count > 0)
    {
        // Value binding can be 1-to-1 direct binding or global
        if (BoundProperties.Count == 1)
        {
            var prop = BoundProperties[0];
            var stateValue = prop.BindingObject.GetPropertyValue(prop.PropertyName).Cast<T>();
            
            if (EqualityComparer<T>.Default.Equals(stateValue, CurrentValue))
            {
                // 1-to-1 binding established
                Set = (v) => {
                    prop.BindingObject.SetPropertyValue(prop.PropertyName, v);
                    CurrentValue = v;
                };
                StateManager.UpdateBinding(this, view);
                view.GetState().AddViewProperty(prop, property, this);
            }
            else
            {
                // Formatted binding or type mismatch → treat as global
                StateManager.UpdateBinding(this, BoundFromView);
                BoundFromView.GetState().AddGlobalProperties(BoundProperties);
            }
        }
        else
        {
            // Multiple properties in value expression → global
            StateManager.UpdateBinding(this, BoundFromView);
            BoundFromView.GetState().AddGlobalProperties(BoundProperties);
        }
    }
}
```

---

## 4. INOTIFYPROPERTYREAD - Extension to INotifyPropertyChanged

**File**: `src/Comet/BindingObject.cs`

```csharp
public interface INotifyPropertyRead : INotifyPropertyChanged
{
    event PropertyChangedEventHandler PropertyRead;  // NEW: Fires when property is READ
}
```

**Purpose**: Extends the standard INotifyPropertyChanged with property READ tracking.

**Why**: 
- Standard .NET only notifies on property CHANGES
- Comet needs to know when properties are READ to track dependencies
- When a property is read during a Binding<T> function evaluation, StateManager records it
- If that property later changes, the binding is automatically re-evaluated

**Usage Flow**:
1. StateManager.StartProperty() - Enables property read tracking
2. Code reads properties (e.g., `state.Value`)
3. Each property getter calls CallPropertyRead()
4. CallPropertyRead() fires the PropertyRead event and notifies StateManager
5. StateManager.EndProperty() - Returns list of read properties
6. These properties are registered as binding dependencies

---

## 5. VIEW STATE TRACKING - GetState(), Reload Triggering

**File**: `src/Comet/Controls/View.cs`

### GetState() - Access View's BindingState

```csharp
public class View : ContextualObject, IDisposable
{
    protected BindingState State { get; set; }  // Each view has its own state dictionary
    
    internal BindingState GetState() => State;
    
    public View()
    {
        State = new BindingState();  // Initialize empty state
        StateManager.ConstructingView(this);
        SetEnvironmentFields();
    }
}
```

The State dictionary tracks:
- **GlobalProperties**: Properties that affect entire view
- **ViewUpdateProperties**: Properties → Bindings that should update
- **ChangedProperties**: Properties changed during hot reload (for state transfer)

### View Rendering and State Tracking

```csharp
public View GetRenderView()
{
    var replaced = viewThatWasReplaced == null
        ? CometHotReloadHelper.CreateReplacement(this) ?? MauiHotReloadHelper.GetReplacedView(this) as View
        : null;
    if (replaced != null && replaced != this)
    {
        SetHotReloadReplacement(replaced);
        return builtView = replacedView.GetRenderView();
    }
    
    CheckForBody();  // Load [Body] method if not yet assigned
    if (Body == null)
        return this;

    if (BuiltView == null)
    {
        Debug.WriteLine($"Building View: {this.GetType().Name}");
        using (new StateBuilder(this))  // Enable property read tracking during build
        {
            try
            {
                var view = Body.Invoke();  // Execute body lambda
                view.Parent = this;
                
                var props = StateManager.EndProperty();  // Get properties read during body execution
                var propCount = props.Count;
                if (propCount > 0)
                {
                    State.AddGlobalProperties(props);  // Mark these as global state
                }
                
                builtView = view.GetRenderView();
                UpdateBuiltViewContext(builtView);
            }
            catch (Exception ex) { /* error handling */ }
        }
    }

    return BuiltView;
}
```

### Reload Triggering

When a property changes:

```csharp
internal void BindingPropertyChanged<T>(INotifyPropertyRead bindingObject, string property, 
    string fullProperty, T value)
{
    if (!State.UpdateValue(this, (bindingObject, property), fullProperty, value, out bool bindingsHandled))
    {
        // UpdateValue returned false → property is GLOBAL (affects entire view)
        if (StateManager.IsBatching)
            StateManager.AddViewNeedingReload(this);  // Defer reload
        else
            Reload(false);  // Immediate reload
    }
    else if (!StateManager.IsBatching && !bindingsHandled)
    {
        // UpdateValue returned true AND no specific bindings handled it
        ViewPropertyChanged(property, value);
    }
    else
    {
        // Specific bindings handled the change (property is local)
    }
}
```

**Reload Logic**:
1. If property is GLOBAL (multiple dependencies or read at body level) → **Full reload**
2. If property is LOCAL (1-to-1 binding to specific control) → **Binding update only**
3. If batching → **Defer reload** until EndBatch()

---

## 6. STATEMANAGER - Property Read Interception & Recording

**File**: `src/Comet/StateManager.cs`

`StateManager` is the central coordination hub for all state tracking and notifications.

### Core Data Structures

```csharp
public static class StateManager
{
    // View stack during rendering
    [ThreadStatic] static WeakStack<View> _viewStack;
    public static View CurrentView => GetCurrentViewStack().Peek() ?? LastView?.Target as View;

    // Currently-tracked property reads (collected during Binding function evaluation)
    [ThreadStatic] static List<(INotifyPropertyRead bindingObject, string property)> _currentReadProperties;

    // Maps each state object to views that listen to it
    static Dictionary<INotifyPropertyRead, HashSet<View>> NotifyToViewMappings 
        = new Dictionary<INotifyPropertyRead, HashSet<View>>();

    // Maps views to state objects they track (for disposal cleanup)
    static Dictionary<string, List<INotifyPropertyRead>> ViewObjectMappings 
        = new Dictionary<string, List<INotifyPropertyRead>>();

    // For nested state objects, maps property name back to parent field name
    static Dictionary<INotifyPropertyChanged, Dictionary<string, string>> ChildPropertyNamesMapping 
        = new Dictionary<INotifyPropertyChanged, Dictionary<string, string>>();

    // State batching (multiple changes → single re-evaluation)
    static int _batchDepth;
    static readonly List<Binding> _dirtyBindings = new List<Binding>();
    static readonly HashSet<View> _viewsNeedingReload = new HashSet<View>();
    
    public static bool IsBatching => _batchDepth > 0;
}
```

### Property Read Tracking

```csharp
[ThreadStatic] static bool _isTrackingProperties;

internal static void StartProperty()
{
    _isTrackingProperties = true;
    var currentReadProperies = GetCurrentReadProperties();
    if (currentReadProperies.Count > 0)
    {
        CurrentView.GetState()?.AddGlobalProperties(currentReadProperies);
    }
    currentReadProperies.Clear();
}

public static void OnPropertyRead(object sender, string propertyName)
{
    if (!IsBuilding)
        return;  // Only track reads during view building or binding evaluation
    
    var currentReadProperies = GetCurrentReadProperties();
    currentReadProperies.Add((sender as INotifyPropertyRead, propertyName));
}

internal static IReadOnlyList<(INotifyPropertyRead BindingObject, string PropertyName)> EndProperty()
{
    _isTrackingProperties = false;
    var currentReadProperies = GetCurrentReadProperties();
    var count = currentReadProperies.Count;
    
    if (count == 0)
        return Array.Empty<(INotifyPropertyRead, string)>();

    if (count == 1)
    {
        var single = currentReadProperies[0];
        currentReadProperies.Clear();
        var buf = _endPropertyBuffer ??= new List<(INotifyPropertyRead, string)>(1);
        buf.Clear();
        buf.Add(single);
        return buf;
    }

    // Multi-property: deduplicate in-place
    var seen = _endPropertySeen ??= new HashSet<(INotifyPropertyRead, string)>();
    seen.Clear();
    var result = new List<(INotifyPropertyRead, string)>(count);
    foreach (var prop in currentReadProperies)
    {
        if (seen.Add(prop))
            result.Add(prop);
    }
    currentReadProperies.Clear();
    return result;
}
```

### Property Change Dispatching

```csharp
public static void OnPropertyChanged<T>(object sender, string propertyName, T value)
{
    if (value?.GetType() == typeof(View))
        return;  // Don't track View properties
    
    if (value is INotifyPropertyRead iNotify)
        StartMonitoring(iNotify);  // Auto-subscribe to nested state objects
    
    var notify = sender as INotifyPropertyRead;
    if (notify == null)
        return;

    HashSet<View> views;
    Dictionary<string, string> mappings;
    View singleView = null;

    _rwLock.EnterReadLock();
    try
    {
        if (!NotifyToViewMappings.TryGetValue(notify, out views))
            return;
        if (views.Count == 0)
            return;
        
        ChildPropertyNamesMapping.TryGetValue(notify, out mappings);

        // Fast path for single-view: extract view inside same lock
        if (views.Count == 1)
        {
            using var enumerator = views.GetEnumerator();
            singleView = enumerator.MoveNext() ? enumerator.Current : null;
        }
    }
    finally
    {
        _rwLock.ExitReadLock();
    }

    if (singleView != null)
    {
        if (singleView.IsDisposed)
        {
            _rwLock.EnterWriteLock();
            try { views.Remove(singleView); }
            finally { _rwLock.ExitWriteLock(); }
            return;
        }
        
        string parentproperty = null;
        if (mappings != null && mappings.Count > 0 && !mappings.TryGetValue(singleView.Id, out parentproperty))
            parentproperty = mappings.First().Value;
        
        var prop = ResolvePropertyName(parentproperty, propertyName);
        singleView.BindingPropertyChanged(notify, propertyName, prop, value);
        return;
    }

    // Multi-view path: use ArrayPool for efficiency
    View[] viewsCopy = null;
    int viewCount;

    _rwLock.EnterReadLock();
    try
    {
        viewCount = views.Count;
        viewsCopy = System.Buffers.ArrayPool<View>.Shared.Rent(viewCount);
        views.CopyTo(viewsCopy);
    }
    finally
    {
        _rwLock.ExitReadLock();
    }

    try
    {
        for (int i = 0; i < viewCount; i++)
        {
            var view = viewsCopy[i];
            if (view == null || view.IsDisposed)
                continue;
            
            string parentproperty = null;
            if (mappings != null && mappings.Count > 0 && 
                !mappings.TryGetValue(view.Id, out parentproperty))
            {
                parentproperty = mappings.First().Value;
            }
            
            var prop = ResolvePropertyName(parentproperty, propertyName);
            view.BindingPropertyChanged(notify, propertyName, prop, value);
        }
    }
    finally
    {
        if (viewsCopy != null)
            System.Buffers.ArrayPool<View>.Shared.Return(viewsCopy, true);
    }
}
```

### State Batching

```csharp
public static void BeginBatch()
{
    _batchDepth++;
}

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
    // Flush dirty bindings first
    if (_dirtyBindings.Count > 0)
    {
        for (int i = 0; i < _dirtyBindings.Count; i++)
            _dirtyBindings[i].Flush();  // Re-evaluate deferred Funcs
        _dirtyBindings.Clear();
    }

    // Then reload views that had global property changes
    if (_viewsNeedingReload.Count > 0)
    {
        foreach (var v in _viewsNeedingReload)
        {
            if (!v.IsDisposed)
                v.Reload();
        }
        _viewsNeedingReload.Clear();
    }
}
```

---

## 7. COMPONENT<T> vs VIEW - State Differences

**File**: `src/Comet/Component.cs`

Comet provides three component patterns with increasing state sophistication:

### Pattern 1: Component (No State)

```csharp
public abstract class Component : View, IComponentWithState
{
    bool _mounted;

    protected Component()
    {
        // Wire Body to call Render
        Body = () => Render();
    }

    public abstract View Render();

    protected virtual void OnMounted() { }    // Called after handler is set
    protected virtual void OnWillUnmount() { }  // Called on dispose

    // For hot reload compatibility
    public virtual object GetStateObject() => null;
    public virtual void TransferStateFrom(IComponentWithState source) { }
}
```

**Usage**: Pure functional rendering, all state managed outside component.

### Pattern 2: Component<TState> (Internal State)

```csharp
public abstract class Component<TState> : Component, IComponentWithState
    where TState : class, new()
{
    TState _state;

    /// The component's typed state object (not BindingState - a plain class)
    public new TState State => _state ??= new TState();

    /// Mutate state and trigger re-render. Safe from any thread.
    protected void SetState(Action<TState> mutator)
    {
        if (mutator == null)
            throw new ArgumentNullException(nameof(mutator));

        var state = State;

        StateManager.BeginBatch();  // Batch all mutations
        try
        {
            mutator(state);
        }
        finally
        {
            StateManager.EndBatch();
        }

        // Trigger re-render on main thread
        ThreadHelper.RunOnMainThread(() => Reload());
    }

    internal void MergeStateFrom(Component<TState> oldComponent)
    {
        if (oldComponent?._state != null)
        {
            _state = oldComponent._state;  // Preserve state across hot reload
        }
    }

    public override object GetStateObject() => _state;
    public override void TransferStateFrom(IComponentWithState source)
    {
        if (source is Component<TState> typed && typed._state != null)
        {
            _state = typed._state;
        }
    }
}
```

**Key Difference from View**:
- View.State is a `BindingState` (tracks property reads/changes)
- Component<TState>.State is a plain class TState (just data)
- SetState() batches changes → single re-render
- State persists across hot reload via TransferStateFrom()

### Pattern 3: Component<TState, TProps> (State + Props)

```csharp
public abstract class Component<TState, TProps> : Component<TState>, IComponentWithState
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
            ThreadHelper.RunOnMainThread(() => Reload());  // Re-render when props change
        }
    }

    internal void UpdatePropsFromDiff(TProps newProps)
    {
        if (newProps == null)
            newProps = new TProps();
        _props = newProps;
        // Don't trigger Reload - diff cycle handles it
    }

    protected virtual bool ShouldUpdate(TProps oldProps, TProps newProps) => true;  // For optimization

    public override void TransferStateFrom(IComponentWithState source)
    {
        base.TransferStateFrom(source);
        if (source is Component<TState, TProps> typed && typed._props != null)
        {
            _props = typed._props;
        }
    }
}
```

**Comparison**:
- **View**: Uses BindingState + State<T> fields for reactive data
- **Component<TState>**: Uses SetState() for React-like immutable updates
- **Component<TState, TProps>**: Adds Props for parent-supplied data

---

## 8. ENVIRONMENT SYSTEM - Relationship to State

**File**: `src/Comet/EnvironmentData.cs`, `src/Comet/EnvironmentAware.cs`

The Environment system is Comet's dependency injection + theming mechanism.

### Environment Attributes and Keys

```csharp
[AttributeUsage(AttributeTargets.Field)]
public class EnvironmentAttribute : StateAttribute
{
    public EnvironmentAttribute(string key = null)
    {
        Key = key;  // Custom key, or defaults to field name
    }
    public string Key { get; }
}
```

### View Environment Integration

```csharp
public class View : ContextualObject
{
    static public EnvironmentData Environment { get; } = new EnvironmentData();
    
    void SetEnvironmentFields()
    {
        var fields = this.GetFieldsWithAttribute(typeof(EnvironmentAttribute));
        if (!fields.Any())
            return;
        foreach (var f in fields)
        {
            var attribute = f.GetCustomAttributes(true).OfType<EnvironmentAttribute>().FirstOrDefault();
            var key = attribute.Key ?? f.Name;
            usedEnvironmentData.Add((f.Name, key));
            State.AddGlobalProperty((View.Environment, key));  // Track as global
        }
    }
    
    void PopulateFromEnvironment()
    {
        var keys = usedEnvironmentData.ToList();
        foreach (var item in keys)
        {
            var key = item.Key;
            var value = this.GetEnvironment(key);
            
            if (value == null && GetMauiContext() != null)
            {
                // Try to get from DI container
                var type = this.GetType();
                var prop = type.GetDeepField(item.Field);
                var service = GetMauiContext().Services.GetService(prop.FieldType);
                if (service != null)
                    value = service;
            }
            
            if (value != null)
            {
                StateManager.ListenToEnvironment(this);
                State.AddGlobalProperty((View.Environment, key));
                
                if (value is INotifyPropertyRead notify)
                    StateManager.RegisterChild(this, notify, key);
                
                this.SetDeepPropertyValue(item.Field, value);
            }
        }
    }
}

public static void SetGlobalEnvironment(string key, object value)
{
    Environment.SetValue(key, value, true);
    ThreadHelper.RunOnMainThread(() => {
        List<View> views;
        lock (ActiveViewsLock)
            views = ActiveViews.OfType<View>().ToList();
        views.ForEach(x => x.ViewPropertyChanged(key, value));  // Notify all views
    });
}
```

**Environment Properties Are Global**: Any property read from Environment during body evaluation is added to GlobalProperties, triggering full view reload on change.

---

## 9. HOT RELOAD STATE TRANSFER - TransferState Mechanism

**File**: `src/Comet/Controls/View.cs`

Hot reload preserves state when code changes during development.

### State Transfer During Hot Reload

```csharp
internal void SetHotReloadReplacement(View replacement, bool transferState = true)
{
    if (replacement == null || replacement == this)
        return;

    replacement.viewThatWasReplaced = this;
    replacement.ViewHandler = ViewHandler;           // Reuse same handler
    replacement.Navigation = Navigation;
    replacement.Parent = this;
    replacement.ReloadHandler = ReloadHandler;
    replacement.PopulateFromEnvironment();
    
    if (transferState)
        TransferHotReloadStateTo(replacement);  // TRANSFER STATE

    replacedView = replacement;
}

internal void TransferHotReloadStateTo(View newView)
{
    if (newView == null)
        return;
    TransferHotReloadStateToCore(newView);
}

protected virtual void TransferHotReloadStateToCore(View newView)
{
    var oldState = this.GetState();
    if (oldState == null)
        return;
    
    // Transfer only CHANGED properties (from changeDictionary)
    var changes = oldState.ChangedProperties;
    foreach (var change in changes)
    {
        newView.SetDeepPropertyValue(change.Key, change.Value);
    }
}
```

**For Component<T>**:
```csharp
protected override void TransferHotReloadStateToCore(View newView)
{
    base.TransferHotReloadStateToCore(newView);
    if (newView is IComponentWithState newComponent &&
        this is IComponentWithState currentComponent)
    {
        newComponent.TransferStateFrom(currentComponent);  // Transfer TState object
    }
}
```

**How it works**:
1. BindingState tracks all changed properties in `changeDictionary`
2. On hot reload, new view instance is created
3. Changed properties are copied to new instance via SetDeepPropertyValue()
4. Component<T> also transfers its typed _state object
5. View is re-rendered with preserved state

---

## 10. Source Generation & Runtime Discovery - [State], [Body], and Generated Helpers

### [State] Attribute Registration

**File**: `src/Comet/Attributes.cs`

```csharp
[AttributeUsage(AttributeTargets.Field)]
public class StateAttribute : Attribute { }
```

### [Body] Attribute Runtime Discovery

```csharp
[AttributeUsage(AttributeTargets.Method)]
public class BodyAttribute : Attribute { }
```

### How They're Used

```csharp
public class MyView : View
{
    [State]
    readonly State<int> count = new State<int>(0);
    
    [Body]
    View body() => new VStack
    {
        new Text(() => $"Count: {count.Value}"),
        new Button("Increment", () => count.Value++)
    };
}
```

### Runtime Discovery

```csharp
// In View.CheckForBody()
void CheckForBody()
{
    if (didCheckForBody)
        return;
    StateManager.CheckBody(this);
    didCheckForBody = true;
    if (Body != null)
        return;
    
    var bodyMethod = this.GetBody();  // Find [Body] method
    if (bodyMethod != null)
        Body = bodyMethod;  // Wire it up
}

// In Internal/Extensions.cs
public static Func<View> GetBody(this View view)
{
    var bodyMethod = view.GetType().GetDeepMethodInfo(typeof(BodyAttribute));
    if (bodyMethod != null)
        return (Func<View>)Delegate.CreateDelegate(typeof(Func<View>), view, bodyMethod.Name);
    return null;
}

// In StateManager
static IEnumerable<INotifyPropertyRead> CheckForStateAttributes(object obj, View view)
{
    var type = obj.GetType();
    var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
        .Where(x => (x.FieldType.Assembly == CometAssembly && x.FieldType.Name == "State`1") 
                 || Attribute.IsDefined(x, typeof(StateAttribute)))
        .ToList();

    if (fields.Any())
    {
        foreach (var field in fields)
        {
            if (!field.IsInitOnly)
                throw new ReadonlyRequiresException(field.DeclaringType?.FullName, field.Name);
            
            var fieldValue = field.GetValue(obj);
            var child = fieldValue as INotifyPropertyRead;
            if (child != null)
            {
                RegisterChild(view, child, field.Name);  // Register as state dependency
                yield return child;
            }
        }
    }
}
```

### AutoNotifyGenerator

**File**: `src/Comet.SourceGenerator/AutoNotifyGenerator.cs`

Generates properties from [AutoNotify] fields:

```csharp
[Generator]
public class AutoNotifyGenerator : ISourceGenerator
{
    private const string attributeText = @"
using System;
namespace Comet
{
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    sealed class AutoNotifyAttribute : Attribute
    {
        public AutoNotifyAttribute() { }
        public string PropertyName { get; set; }
    }
}
";

    public void Execute(GeneratorExecutionContext context)
    {
        if (!(context.SyntaxContextReceiver is SyntaxReceiver receiver) || !receiver.Fields.Any())
            return;

        foreach (IGrouping<INamedTypeSymbol, IFieldSymbol> group in receiver.Fields.GroupBy(f => f.ContainingType))
        {
            string classSource = ProcessClass(group.Key, group.ToList(), ...);
            if(!string.IsNullOrWhiteSpace(classSource))
                context.AddSource($"{group.Key.Name}_autoNotify.cs", SourceText.From(classSource, Encoding.UTF8));
        }
    }

    private string ProcessClass(INamedTypeSymbol classSymbol, List<IFieldSymbol> fields, ...)
    {
        string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

        StringBuilder source = new StringBuilder($@"
using Comet;
namespace {namespaceName}
{{
    public partial class {classSymbol.Name} : INotifyPropertyRead, IAutoImplemented
    {{
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        public event System.ComponentModel.PropertyChangedEventHandler PropertyRead;
");

        foreach (IFieldSymbol fieldSymbol in fields)
        {
            // Generate property from field
            string fieldName = fieldSymbol.Name;
            ITypeSymbol fieldType = fieldSymbol.Type;
            string propertyName = chosenName(fieldName);
            
            source.Append($@"
public {fieldType} {propertyName} 
{{
    get 
    {{
        StateManager.OnPropertyRead(this, nameof({propertyName}));
        PropertyRead?.Invoke(this, new PropertyChangedEventArgs(nameof({propertyName})));
        return this.{fieldName};
    }}
    set
    {{
        this.{fieldName} = value;
        StateManager.OnPropertyChanged(this, nameof({propertyName}), value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof({propertyName})));
    }}
}}
");
        }

        source.Append("} }");
        return source.ToString();
    }
}
```

**Usage**:
```csharp
public partial class MyData
{
    [AutoNotify]
    private int _count;  // Generates Count property with PropertyRead/PropertyChanged
    
    [AutoNotify(PropertyName = "Title")]
    private string _title;  // Custom property name
}
```

### StateWrapperGenerator

**File**: `src/Comet.SourceGenerator/StateWrapperGenerator.cs`

Generates state wrapper classes from [GenerateStateClass] models:

```csharp
[GenerateStateClass(typeof(MyModel))]
public partial class MyModelState : INotifyPropertyRead, IAutoImplemented
{
    // Generated from MyModel.cs

    public MyModelState(MyModel model)
    {
        OriginalModel = model;
        InitStateProperties();
    }

    public string Name 
    {
        get 
        {
            NotifyPropertyRead();
            return OriginalModel.Name;
        }
        set 
        {
            OriginalModel.Name = value;
            NotifyPropertyChanged(value);
        }
    }

    public void NotifyChanged()
    {
        // Updates all dirty properties
        UpdateDirtyProperty("Name");
        // ... other properties
    }
}
```

---

## KEY ARCHITECTURAL PATTERNS

### 1. Property Read Tracking

During Binding<T> function evaluation:
```
StateManager.StartProperty()
  ↓
Func<T>.Invoke()
  ↓
  Property.get { CallPropertyRead() }
  ↓
  StateManager.OnPropertyRead() adds (object, propertyName) to list
  ↓
StateManager.EndProperty()
  ↓
Returns list of read properties → stored in Binding.BoundProperties
```

### 2. Change Notification Flow

When a State<T> value changes:
```
state.Value = newValue
  ↓
CallPropertyChanged()
  ↓
StateManager.OnPropertyChanged()
  ↓
NotifyToViewMappings[state] → all views that listen to this state
  ↓
View.BindingPropertyChanged()
  ↓
State.UpdateValue() → checks if property is Global or Local
  ↓
Global → Reload()/Reload (batched)
Local → Update specific bindings via Binding.BindingValueChanged()
```

### 3. Global vs Local Properties

**Global** (triggers full reload):
- Multiple state objects read in body
- Properties read at body scope level
- Conditional logic based on state

**Local** (updates specific control):
- 1-to-1 binding: Control.Text(() => state.Value)
- Only that control updates when state changes

### 4. Batching for Performance

```csharp
StateManager.BeginBatch();
try 
{
    count.Value = 10;
    name.Value = "John";
    // Both changes accumulated
}
finally 
{
    StateManager.EndBatch();  // Single reload or Binding re-evaluation
}
```

---

## PERFORMANCE OPTIMIZATIONS

1. **Implicit Conversions**: State<T> → T removes wrapper overhead
2. **GetValueInternal()**: Tuple unpacking avoids boxing
3. **EqualityComparer<T>.Default**: Fast typed equality check
4. **Cached PropertyChangedEventArgs**: Reuses event args objects
5. **ArrayPool**: Multi-view dispatch uses pooled arrays
6. **_bindingStable flag**: Skips dependency retracking if properties haven't changed
7. **Single-view fast path**: Optimized for 1-to-1 bindings
8. **Binding deferred evaluation**: During batches, Funcs re-evaluate once at end

---

## THREADING SAFETY

- **ReaderWriterLockSlim**: Protects NotifyToViewMappings for concurrent reads/writes
- **ThreadStatic fields**: Each thread has separate property read list and view stack
- **WeakReferences**: Views are weakly referenced to allow GC
- **Disposed view cleanup**: Disposed views are removed from mappings

---

This comprehensive documentation covers all aspects of Comet's state management system, from the atomic State<T> class through View lifecycle, Component patterns, and hot reload mechanics.


---

## 11. Source Generation Clarifications - What Is Generated vs What Is Reflected

The current Comet repository uses **both** source generation and runtime discovery, and it is important not to blur the two.

### What is runtime-discovered

- `BodyAttribute` is a marker attribute declared in `src/Comet/Attributes.cs`
- `View.CheckForBody()` calls `this.GetBody()` when `Body` is still null
- `GetBody()` in `src/Comet/Internal/Extensions.cs` uses reflection to find a method marked with `[Body]`
- The method is then wrapped as a `Func<View>` delegate and assigned to `Body`

That means `[Body]` is **not** currently wired up by `AutoNotifyGenerator`.
The runtime flow is:

```csharp
var bodyMethod = this.GetBody();
if (bodyMethod != null)
    Body = bodyMethod;
```

### What is source-generated

- `[AutoNotify]` fields are expanded into properties that raise both `PropertyRead` and `PropertyChanged`
- `CometViewSourceGenerator` produces control wrappers that accept `Binding<T>` and `Func<T>`
- `StateWrapperGenerator` produces `INotifyPropertyRead` wrappers around model types

### Why the distinction matters

If you are debugging state behavior around a `[Body]` method, the relevant code is **runtime reflection and `View.CheckForBody()`**.
If you are debugging state behavior around generated wrappers or `[AutoNotify]` fields, the relevant code is in the source generator outputs and templates.

### Maintainer takeaway

When documenting or extending Comet:

1. Treat `[Body]` as a runtime discovery feature.
2. Treat `[AutoNotify]` and control wrapper generation as compile-time ergonomics.
3. Remember that both paths ultimately feed the same `StateManager` and `Binding<T>` infrastructure.

---

## 12. LIFECYCLE & CLEANUP - How Views Leave the State Graph

Comet's state management is only safe because the framework removes views from its global monitoring tables when they are disposed.

### View disposal sequence

When `View.Dispose(bool disposing)` runs, the framework performs the following cleanup work:

1. Removes the view from `ActiveViews`
2. Removes gesture hookups from the handler
3. Unregisters the view from hot reload tracking
4. Disposes the current handler
5. Disposes any replacement view created for hot reload
6. Disposes the built child view
7. Clears the `Body` delegate
8. Clears contextual data
9. Calls `StateManager.Disposing(this)`
10. Clears visual state groups
11. Clears the per-view `BindingState`
12. Sets `State = null` in the `finally` block

This is more than normal UI cleanup.
It is specifically removing the view from the reactive dependency graph.

### `StateManager.Disposing(this)`

`StateManager.Disposing(view)` removes the view from these global structures:

- `ViewObjectMappings`
- `NotifyToViewMappings`
- `ChildPropertyNamesMapping` entries that are no longer needed
- `MonitoredObjects` entries that are no longer watched by any view

A particularly important detail is that **event unsubscription happens outside the lock**.
That prevents lock recursion and re-entrancy problems while mutating the global maps.

### Why `IAutoImplemented` matters during cleanup

Objects that implement `IAutoImplemented` are not subscribed through the normal event-hook path in `StartMonitoringCore(...)`.
Because of that, `Disposing(...)` only removes `PropertyChanged` / `PropertyRead` handlers for objects that are **not** `IAutoImplemented`.

This keeps generated or self-reporting objects from being double-managed.

### Readonly state fields reduce lifecycle complexity

`CheckForStateAttributes(...)` requires state fields to be `readonly`.
That rule helps lifecycle safety because the identity of the observed state object does not drift after construction.
The view can register once and dispose once, without chasing field replacement semantics.

### Hot reload and cleanup

Hot reload replacement would leak aggressively without explicit disposal.
Each replacement can carry handlers, environment links, and view registrations.
Comet's cleanup path ensures old generations stop participating in `NotifyToViewMappings` once they are no longer active.

### Lifecycle summary diagram

```text
View created
   |
   +--> BindingState allocated
   +--> StateManager.ConstructingView(this)
   +--> state fields registered
   +--> body/build tracking starts later
   |
   v
View live in tree
   |
   +--> state changes routed through StateManager
   +--> bindings reevaluated or view reloaded
   |
   v
View disposed
   |
   +--> StateManager.Disposing(this)
   +--> global watcher maps pruned
   +--> handler / built view / replacement view disposed
   +--> BindingState cleared
```

---

## 13. IMPLICIT CONVERSIONS - Detailed Behavioral Notes

The core state APIs rely heavily on C# implicit operators.
That makes author code small, but it also means tiny syntax differences can change update behavior.

### Conversion matrix

| Conversion | Source file | Behavioral meaning |
| --- | --- | --- |
| `State<T> -> T` | `src/Comet/State.cs` | Reads `Value`, so it participates in property tracking when tracking is active |
| `State<T> -> Action<T>` | `src/Comet/State.cs` | Produces a setter delegate over `state.Value` |
| `T -> State<T>` | `src/Comet/State.cs` | Wraps a literal or value into a tracked state box |
| `T -> Binding<T>` | `src/Comet/Binding.cs` | Captures a value snapshot plus any currently tracked dependencies |
| `Func<T> -> Binding<T>` | `src/Comet/Binding.cs` | Creates a re-evaluable computed binding |
| `State<T> -> Binding<T>` | `src/Comet/Binding.cs` | Creates a getter/setter binding over a specific `State<T>` instance |

### The most important pitfall

These two lines do **not** mean the same thing internally:

```csharp
new Text($"Count: {count}")
new Text(() => $"Count: {count}")
```

The first form typically creates a value snapshot during body execution.
The second form creates a function binding that can re-evaluate and stabilize.

### Best-practice guidance

- Prefer `Func<T>` bindings for computed UI text and expressions
- Use direct `State<T>` bindings when you actually want a two-way state bridge
- Assume that any implicit conversion in Comet carries semantics, not just syntax sugar

---

## 14. Threading & Concurrency - Deeper Notes

The public API feels UI-thread-centric, but the implementation still contains careful concurrency boundaries.

### Thread-local tracking state

`StateManager` uses `[ThreadStatic]` fields for:

- the current view stack
- the current list of read properties
- temporary buffers used by `EndProperty()`
- the boolean flag that marks a property-tracking window

This means dependency capture is **scoped to the current thread**.
That is exactly what Comet wants while building a view or evaluating a binding expression.

### Global maps under lock

The routing maps are shared process-wide and therefore guarded by `ReaderWriterLockSlim`.
The code intentionally:

- reads under the read lock
- copies interesting data out
- performs callbacks outside the lock
- re-enters a write lock later only if cleanup is needed

This pattern is visible in `OnPropertyChanged(...)` and keeps callback execution from happening while global tables are locked.

### Weak references are part of the design

Comet uses weak references in multiple places:

- `Binding._view`
- `Binding._boundFromView`
- `View.parent`
- `StateManager.LastView`
- `WeakStack<View>` entries

This does **not** eliminate the need for disposal, but it reduces the chance that routing structures accidentally keep a dead tree alive.

### Main-thread application remains essential

Even when tracking or bookkeeping can happen elsewhere, actual UI application is pushed back to the main thread through `ThreadHelper.RunOnMainThread(...)`.
That includes:

- `Component<TState>.SetState(...)`
- environment property application
- hot reload-triggered reloads
- global environment broadcasts

### Concurrency summary

Comet is best described as:

- thread-local for dependency capture
- lock-protected for global watcher registration
- main-thread-oriented for visual application

---

## 15. Architecture Diagrams - End-to-End Flows

### A. Body-build dependency capture

```text
View.GetRenderView()
   |
   +--> using StateBuilder(this)
           |
           +--> StateManager.StartBuilding(this)
           |
           +--> Body.Invoke()
                   |
                   +--> state getter(s) call CallPropertyRead(...)
                   |
                   +--> StateManager.OnPropertyRead(...) appends pairs
           |
           +--> StateManager.EndProperty()
           |
           +--> State.AddGlobalProperties(readSet)
```

### B. Binding-driven targeted update

```text
Func<T> binding created
   |
   +--> ProcessGetFunc()
           |
           +--> StartProperty()
           +--> evaluate getter
           +--> EndProperty()
           +--> BoundProperties captured
           +--> BindToProperty(...)

Later source property changes
   |
   +--> StateManager.OnPropertyChanged(...)
   +--> View.BindingPropertyChanged(...)
   +--> BindingState.UpdateValue(...)
   +--> Binding.BindingValueChanged(...)
   +--> EvaluateAndNotify(...)
   +--> View.ViewPropertyChanged(...)
```

### C. Global structural update

```text
State change
   |
   +--> BindingState.UpdateValue(...)
           |
           +--> property found in GlobalProperties
           +--> return false
   |
   +--> View.BindingPropertyChanged(...)
           |
           +--> batching? queue reload : Reload(false)
```

### D. Component `SetState(...)`

```text
SetState(mutator)
   |
   +--> BeginBatch()
   +--> mutator(State)
   +--> EndBatch()
           |
           +--> Flush dirty bindings
           +--> Reload queued views
   +--> RunOnMainThread(() => Reload())
```

### E. Hot reload transfer

```text
old view instance
   |
   +--> SetHotReloadReplacement(newView)
           |
           +--> copy handler/navigation/parent/reload handler
           +--> PopulateFromEnvironment()
           +--> TransferHotReloadStateTo(newView)
                   |
                   +--> replay BindingState.ChangedProperties
                   +--> for components, TransferStateFrom(oldComponent)
```

### F. Environment propagation

```text
SetGlobalEnvironment(key, value)
   |
   +--> write View.Environment
   +--> enumerate ActiveViews
   +--> ViewPropertyChanged(key, value) on each

[Environment] field on a view
   |
   +--> SetEnvironmentFields() registers global dependency
   +--> PopulateFromEnvironment() resolves value
   +--> optional RegisterChild(...) if resolved object is observable
```

---

## 16. FINAL RECAP - The Core Design in One Page

Comet's state system can be summarized as four big ideas working together:

1. **Observable reads are as important as observable writes**
   - `INotifyPropertyRead` is the key abstraction that makes automatic dependency tracking possible.

2. **Dependencies are classified, not treated uniformly**
   - `BindingState` decides whether a change is structural (`GlobalProperties`) or patchable (`ViewUpdateProperties`).

3. **Bindings are miniature runtime programs**
   - `Binding<T>` captures dependency sets, stabilizes when possible, and reevaluates only when necessary.

4. **Views and Components share one engine**
   - `Component<TState>` changes the authoring model, but it still runs on the same render, reload, batching, environment, and hot reload infrastructure as classic `View`.

If you keep those four ideas in mind, the rest of the implementation becomes much easier to reason about.
