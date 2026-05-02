# Comet Reactive State Guide

A practical, code-forward guide to every state management pattern in Comet.
Every example compiles against the current API surface.

---

## Table of Contents

1. [Stateless Views](#1-stateless-views)
2. [Stateful Views and the Rebuild Cycle](#2-stateful-views-and-the-rebuild-cycle)
3. [Core Primitives: Reactive\<T\> and Signal\<T\>](#3-core-primitives-reactivet-and-signalt)
4. [Fine-Grained Updates vs Body Rebuilds](#4-fine-grained-updates-vs-body-rebuilds)
5. [Reading State Without Triggering Rebuilds](#5-reading-state-without-triggering-rebuilds)
6. [Two-Way Binding](#6-two-way-binding)
7. [Computed and Derived State](#7-computed-and-derived-state)
8. [Side Effects](#8-side-effects)
9. [Lists and Collections](#9-lists-and-collections)
10. [Component\<TState\> Pattern](#10-componenttstate-pattern)
11. [View Lifecycle](#11-view-lifecycle)
12. [Shared State Across Views](#12-shared-state-across-views)
13. [Async State Updates](#13-async-state-updates)
14. [Hot Reload and State Preservation](#14-hot-reload-and-state-preservation)
15. [Best Practices](#15-best-practices)
16. [Quick Reference](#16-quick-reference)

---

## 1. Stateless Views

A view with no state fields is stateless. It receives data from its parent
and renders it. No `Reactive<T>`, no `Signal<T>`, no `SetState`. The body
never re-executes on its own -- only when the parent rebuilds and creates
a new instance.

### Minimal stateless view

```csharp
using Comet;
using static Comet.CometControls;

public class Greeting : View
{
readonly string name;

public Greeting(string name)
{
this.name = name;
}

[Body]
View body() =>
Text($"Hello, {name}!")
.FontSize(24);
}
```

### Composition: breaking pages into child views

Build pages from smaller views. Pass data via constructor parameters.

```csharp
public class ProfileHeader : View
{
readonly string title;
readonly string subtitle;

public ProfileHeader(string title, string subtitle)
{
this.title = title;
this.subtitle = subtitle;
}

[Body]
View body() =>
VStack(4,
Text(title).FontSize(20).FontWeight(FontWeight.Bold),
Text(subtitle).FontSize(14).Color(Colors.Grey)
);
}

public class ProfilePage : View
{
[Body]
View body() =>
VStack(16,
new ProfileHeader("Jane Doe", "Developer"),
Text("Bio goes here...").FontSize(14)
);
}
```

### Passing reactive data to child views

A child can accept a `Reactive<T>` from its parent. The child reads
`.Value` inside a lambda, and updates flow through automatically.

```csharp
public class CountDisplay : View
{
readonly Reactive<int> count;

public CountDisplay(Reactive<int> count)
{
this.count = count;
}

[Body]
View body() =>
Text(() => $"Current count: {count.Value}")
.FontSize(32)
.Color(Colors.DodgerBlue);
}

public class CounterPage : View
{
readonly Reactive<int> count = 0;

[Body]
View body() =>
VStack(16,
new CountDisplay(count),
Button("Increment", () => count.Value++)
);
}
```

The `CountDisplay` view is stateless in the sense that it owns no state.
It binds to state owned by its parent.

---

## 2. Stateful Views and the Rebuild Cycle

A view becomes stateful by declaring `Reactive<T>` fields. When any tracked
reactive field changes, the framework rebuilds the affected UI.

### Declaring state

```csharp
using Comet;
using Comet.Reactive;
using static Comet.CometControls;

public class CounterView : View
{
readonly Reactive<int> count = 0;
readonly Reactive<string> label = "Ready";

[Body]
View body() =>
VStack(16,
Text(() => $"Count: {count.Value}")
.FontSize(48),
Text(() => label.Value)
.Color(Colors.Grey),
Button("Increment", () =>
{
count.Value++;
label.Value = $"Incremented to {count.Value}";
})
);
}
```

### The full rebuild cycle

Here is exactly what happens when you write `count.Value++`:

1. **Setter fires.** `Reactive<T>.Value` setter checks equality via
   `EqualityComparer<T>.Default`. If unchanged, nothing happens.
2. **Subscribers notified.** All `IReactiveSubscriber` instances that
   subscribed to this reactive source receive `OnDependencyChanged`.
3. **Scheduler queues.** `ReactiveScheduler.EnsureFlushScheduled()` posts
   a single flush callback to the MAUI dispatcher. Multiple writes before
   the flush coalesce into one update.
4. **Flush executes.** The scheduler flushes all dirty effects first, then
   all dirty views. For each dirty view, `Reload()` is called.
5. **Body re-evaluates.** `Reload()` calls `ResetView()` which invokes
   the `[Body]` method inside a `ReactiveScope`. The scope tracks which
   reactive sources are read, so the view auto-subscribes to its new
   dependencies and unsubscribes from stale ones.
6. **Diff and update.** The new view tree is diffed against the old one.
   Matching views reuse their platform handlers. Only changed properties
   are pushed to native controls.

### Equality short-circuit

If you assign the same value, nothing happens:

```csharp
readonly Reactive<int> count = 5;

// No rebuild -- value is already 5
count.Value = 5;
```

`Reactive<T>` uses `EqualityComparer<T>.Default`. `Signal<T>` accepts a
custom comparer via its constructor.

---

## 3. Core Primitives: Reactive\<T\> and Signal\<T\>

Comet has two reactive value holders. Both track reads and notify on writes.

### Reactive\<T\>

The primary state primitive for app development. Lives in the `Comet`
namespace.

```csharp
using Comet;

readonly Reactive<int> count = 0;
readonly Reactive<string> name = "World";
readonly Reactive<bool> isOn = false;
readonly Reactive<double> progress = 0.5;
```

Key characteristics:
- Implicit conversion from `T` (`readonly Reactive<int> x = 0`)
- Implicit conversion to `T` (can pass a `Reactive<int>` where `int` is expected)
- Uses `EqualityComparer<T>.Default` for change detection
- Not sealed -- can be subclassed

### Signal\<T\>

Lower-level primitive in `Comet.Reactive`. Sealed, with extra capabilities.

```csharp
using Comet.Reactive;

readonly Signal<int> count = new(0);
readonly Signal<string> name = new("World", StringComparer.OrdinalIgnoreCase);
```

Key characteristics:
- Sealed
- `Peek()` method for reading without tracking
- `DebugName` property for diagnostics
- `Version` property (incremented on each change)
- Custom `EqualityComparer<T>` via constructor
- Automatic hot reload state transfer (field-by-field, by name and type)

### When to use which

| Use case | Choose |
|----------|--------|
| App-level view state | `Reactive<T>` |
| Need `Peek()` (non-tracking read) | `Signal<T>` |
| Need custom equality comparer | `Signal<T>` |
| Need debug diagnostics | `Signal<T>` |
| State that must survive hot reload | `Signal<T>` |
| Shared service state (DI) | Either -- `Signal<T>` preferred for `Peek()` |

---

## 4. Fine-Grained Updates vs Body Rebuilds

Where you read `.Value` determines the scope of the update.

### Lambda reads: control-level updates

When `.Value` is read inside a lambda passed to a control constructor,
only that control updates. The body method does NOT re-execute.

```csharp
[Body]
View body() =>
VStack(
// Only this Text updates when count changes
Text(() => $"Count: {count.Value}"),

// This Button label is static -- never updates
Button("Add", () => count.Value++)
);
```

This is the preferred pattern for most UI. It avoids rebuilding the
entire view tree on every state change.

### Direct reads: full body rebuild

When `.Value` is read directly in the body method (outside any lambda),
the entire body re-executes when that value changes.

```csharp
[Body]
View body()
{
// Reading here means: ANY change to selectedIndex rebuilds the whole body
var idx = selectedIndex.Value;
var page = pages[idx];

return Grid(
new object[] { 200, "*" },
null,
BuildSidebar().Cell(row: 0, column: 0),
page.Cell(row: 0, column: 1)
);
}
```

Use this pattern when the state change requires a structural change to the
view tree (swapping pages, showing/hiding entire sections).

### Summary

| Read location | Update scope | Use for |
|---------------|-------------|---------|
| `() => x.Value` (lambda) | Single control | Display values, formatting |
| `x.Value` (in body directly) | Full body rebuild | Structural changes, page swaps |
| `x.Value` (in button callback) | No tracking | Writes only -- callbacks are not tracked |

### Common mistake: missing lambda

```csharp
// WRONG -- evaluated once at body build time, never updates
Text($"Count: {count.Value}")

// RIGHT -- re-evaluated whenever count changes
Text(() => $"Count: {count.Value}")
```

---

## 5. Reading State Without Triggering Rebuilds

Sometimes you need to read a value without creating a reactive dependency.

### Signal\<T\>.Peek()

`Peek()` returns the current value without tracking. No `ReactiveScope`
registration, no `PropertyRead` event.

```csharp
readonly Signal<int> count = new(0);

void LogCurrentCount()
{
// Does NOT create a dependency -- safe to call from anywhere
var current = count.Peek();
Console.WriteLine($"Count is {current}");
}
```

### Reactive\<T\> has no Peek()

`Reactive<T>` does not have a `Peek()` method. If you need non-tracking
reads, use `Signal<T>` instead. Alternatively, read `.Value` outside of
any tracked context (outside a `[Body]` method or lambda passed to a
control), and the read is effectively untracked.

### Tracked vs untracked contexts

The reactive system only creates bindings when a read occurs inside a
`ReactiveScope`. Scopes are active in two places:

1. **Body evaluation** -- the `[Body]` method runs inside a scope
2. **Control lambdas** -- `Text(() => ...)`, `Slider(() => ...)`, etc.

Reads anywhere else (event handlers, constructors, plain methods) do not
create bindings:

```csharp
public class ExampleView : View
{
readonly Reactive<int> count = 0;

public ExampleView()
{
// NOT tracked -- constructor is not inside a ReactiveScope
var initial = count.Value;
}

void OnButtonTapped()
{
// NOT tracked -- event handlers are not inside a ReactiveScope
var current = count.Value;
Console.WriteLine($"Current: {current}");
}

[Body]
View body() =>
VStack(
// TRACKED (body-level) -- body rebuilds when count changes
Text($"Static at build: {count.Value}"),

// TRACKED (lambda-level) -- only this Text updates
Text(() => $"Live: {count.Value}"),

Button("Tap", () =>
{
// NOT tracked -- Action callback, not a Func binding
OnButtonTapped();
count.Value++;
})
);
}
```

### Using Peek() to avoid unwanted dependencies

In an `Effect` or `Computed`, you may want to read one signal without
depending on it:

```csharp
readonly Signal<string> query = new("");
readonly Signal<int> pageSize = new(20);

var effect = new Effect(() =>
{
var q = query.Value;       // tracked -- effect re-runs when query changes
var size = pageSize.Peek(); // NOT tracked -- effect ignores pageSize changes
Console.WriteLine($"Searching '{q}' with page size {size}");
});
```

---

## 6. Two-Way Binding

Controls that accept user input need both a read path (lambda) and a
write path (callback). Without the callback, the binding is read-only.

### TextField

```csharp
readonly Reactive<string> name = "";

TextField(() => name.Value, () => "Enter your name...")
.OnTextChanged(v => name.Value = v ?? "")
```

The first argument `() => name.Value` is a `Func<string>` that the control
reads to display the current value. `OnTextChanged` is called when the user
types, writing the new value back to state.

### Slider

```csharp
readonly Reactive<double> sliderValue = 50.0;

Slider(() => sliderValue.Value, () => 0.0, () => 100.0)
.OnValueChanged(v => sliderValue.Value = v),
Text(() => $"Value: {sliderValue.Value:F1}")
```

Note: Slider requires `Reactive<double>`, not `Reactive<int>`.

### Toggle

```csharp
readonly Reactive<bool> isOn = false;

HStack(12,
Toggle(() => isOn.Value)
.OnToggled(v => isOn.Value = v),
Text(() => isOn.Value ? "ON" : "OFF")
)
```

### CheckBox

```csharp
readonly Reactive<bool> isChecked = false;

HStack(12,
CheckBox(() => isChecked.Value)
.OnCheckedChanged(v => isChecked.Value = v),
Text(() => isChecked.Value ? "Checked" : "Unchecked")
)
```

### Picker

```csharp
readonly Reactive<int> selectedIndex = 0;
readonly string[] options = { "Red", "Green", "Blue" };

Picker(options)
.OnSelectedIndexChanged(idx => selectedIndex.Value = idx),
Text(() => $"Selected: {options[selectedIndex.Value]}")
```

### Shared state between controls

Two controls can bind to the same `Reactive<T>`. Changing one updates both:

```csharp
readonly Reactive<string> sharedText = "Hello";

VStack(16,
TextField(() => sharedText.Value, () => "Field A...")
.OnTextChanged(v => sharedText.Value = v ?? ""),
TextField(() => sharedText.Value, () => "Field B...")
.OnTextChanged(v => sharedText.Value = v ?? ""),
Text(() => $"Live: \"{sharedText.Value}\"")
)
```

### Pitfall: forgetting the callback

Without `OnTextChanged`/`OnValueChanged`/`OnToggled`, the control displays
the current state but user input is silently discarded:

```csharp
// Read-only -- user can type but state never updates
TextField(() => name.Value, () => "Placeholder...")

// Two-way -- user input flows back to state
TextField(() => name.Value, () => "Placeholder...")
.OnTextChanged(v => name.Value = v ?? "")
```

### Available two-way callbacks

| Control | Callback | Parameter type |
|---------|----------|---------------|
| `TextField` | `.OnTextChanged(Action<string>)` | `string` |
| `Slider` | `.OnValueChanged(Action<double>)` | `double` |
| `Stepper` | `.OnValueChanged(Action<double>)` | `double` |
| `Toggle` | `.OnToggled(Action<bool>)` | `bool` |
| `CheckBox` | `.OnCheckedChanged(Action<bool>)` | `bool` |
| `Picker` | `.OnSelectedIndexChanged(Action<int>)` | `int` |

---

## 7. Computed and Derived State

`Computed<T>` derives a value from one or more reactive sources. It caches
the result and only recalculates when a dependency changes.

```csharp
using Comet.Reactive;

public class ProfileView : View
{
readonly Reactive<string> firstName = "Jane";
readonly Reactive<string> lastName = "Doe";
readonly Reactive<double> age = 30;

readonly Computed<string> fullName;
readonly Computed<string> greeting;

public ProfileView()
{
fullName = new Computed<string>(() =>
$"{firstName.Value} {lastName.Value}");

greeting = new Computed<string>(() =>
{
var a = (int)age.Value;
if (a < 18) return $"Hey {firstName.Value}!";
if (a < 65) return $"Hello, {firstName.Value}.";
return $"Good day, {firstName.Value}.";
});
}

[Body]
View body() =>
VStack(16,
TextField(() => firstName.Value, () => "First name...")
.OnTextChanged(v => firstName.Value = v ?? ""),
TextField(() => lastName.Value, () => "Last name...")
.OnTextChanged(v => lastName.Value = v ?? ""),
Slider(() => age.Value, () => 0.0, () => 100.0)
.OnValueChanged(v => age.Value = v),
Text(() => fullName.Value).FontSize(18),
Text(() => greeting.Value).Color(Colors.MediumSeaGreen)
);
}
```

### How recalculation works

1. Computed tracks which sources its function reads (via `ReactiveScope`).
2. When any dependency changes, Computed marks itself dirty.
3. The cached value is only recomputed on the next `.Value` access.
4. If the recomputed value equals the cached value (via equality comparer),
   downstream subscribers are NOT notified -- no redundant updates.

### Peek() on Computed

`Computed<T>` has `Peek()`, which returns the cached value (recomputing
if dirty) without registering a reactive dependency:

```csharp
readonly Computed<string> fullName;

void LogName()
{
// Gets the value without tracking
Console.WriteLine(fullName.Peek());
}
```

### Custom equality comparer

```csharp
var rounded = new Computed<double>(
() => Math.Round(rawValue.Value, 2),
new DoubleEpsilonComparer(0.001)
);
```

With a custom comparer, the computed value only propagates downstream when
the difference exceeds the epsilon threshold.

### When to use Computed vs inline lambda

| Approach | Use when |
|----------|----------|
| `Text(() => expr)` | Simple expression, used in one place |
| `Computed<T>` | Expensive calculation, or value used by multiple controls |

### Disposal

`Computed<T>` implements `IDisposable`. Disposing unsubscribes from all
dependencies:

```csharp
readonly Computed<string> derived;

protected override void Dispose(bool disposing)
{
if (disposing)
derived?.Dispose();
base.Dispose(disposing);
}
```

---

## 8. Side Effects

`Effect` runs an action whenever its tracked dependencies change. Use it
for logging, persistence, analytics, or any non-UI reaction to state.

```csharp
using Comet.Reactive;

public class SearchView : View
{
readonly Reactive<string> query = "";
readonly Reactive<string> results = "Type to search...";
Effect? searchEffect;

public SearchView()
{
searchEffect = new Effect(() =>
{
var q = query.Value; // tracked dependency
if (string.IsNullOrWhiteSpace(q))
results.Value = "Type to search...";
else
results.Value = $"Searching for: {q}";
});
}

[Body]
View body() =>
VStack(16,
TextField(() => query.Value, () => "Search...")
.OnTextChanged(v => query.Value = v ?? ""),
Text(() => results.Value)
);
}
```

### How Effect works

1. On construction (or when `Run()` is called), the action executes inside
   a `ReactiveScope` that captures all reactive reads.
2. The Effect subscribes to those sources.
3. When any dependency changes, the Effect is marked dirty and queued via
   `ReactiveScheduler.ScheduleEffect()`.
4. On the next scheduler flush, `Flush()` calls `Run()`, which re-executes
   the action and re-captures dependencies.

### Deferred first run

By default, the Effect runs immediately on construction. Pass
`runImmediately: false` to defer:

```csharp
var effect = new Effect(() =>
{
Console.WriteLine($"Query: {query.Value}");
}, runImmediately: false);

// Runs later, when a dependency changes (or call effect.Run() manually)
```

### Batching

Effects are batched by the scheduler. Rapid signal changes coalesce into
a single Effect re-run:

```csharp
// All three writes happen synchronously.
// The Effect runs ONCE after the scheduler flush.
firstName.Value = "Jane";
lastName.Value = "Doe";
age.Value = 30;
```

### Avoid cycles

An Effect that writes to a signal that another Effect reads (which writes
to a signal the first Effect reads) creates a cycle. The scheduler breaks
cycles after 100 flush iterations. Avoid this pattern:

```csharp
// WRONG -- a.Value -> effect1 -> b.Value -> effect2 -> a.Value (cycle)
var effect1 = new Effect(() => { b.Value = a.Value + 1; });
var effect2 = new Effect(() => { a.Value = b.Value + 1; });
```

### Disposal

Dispose an Effect to stop it from running:

```csharp
searchEffect?.Dispose();
searchEffect = null;
```

---

## 9. Lists and Collections

`SignalList<T>` is a reactive list. Mutations trigger UI updates for any
view that reads the list.

### Basic usage

```csharp
using Comet.Reactive;

public class TodoView : View
{
readonly SignalList<string> items = new();
readonly Reactive<string> newItem = "";
int nextId;

[Body]
View body()
{
var itemViews = new View[items.Count];
for (int i = 0; i < items.Count; i++)
{
var index = i; // capture loop variable
var text = items[index];
itemViews[i] = HStack(8,
Text($"  {text}").FontSize(14),
new Spacer(),
Button("Remove", () =>
{
if (index < items.Count)
items.RemoveAt(index);
}).Color(Colors.Crimson)
);
}

return VStack(16,
HStack(8,
TextField(() => newItem.Value, () => "New item...")
.OnTextChanged(v => newItem.Value = v ?? ""),
Button("Add", () =>
{
var text = string.IsNullOrWhiteSpace(newItem.Value)
? $"Item {++nextId}"
: newItem.Value;
items.Add(text);
newItem.Value = "";
})
),
items.Count == 0
? (View)Text("No items yet").Color(Colors.Grey)
: VStack(4, itemViews),
Button("Clear All", () => items.Clear())
.Color(Colors.Crimson)
);
}
}
```

### SignalList API

| Method | Description |
|--------|-------------|
| `Add(T item)` | Appends an item (Insert change) |
| `Insert(int index, T item)` | Inserts at position (Insert change) |
| `Remove(T item)` | Removes first match, returns `bool` (Remove change) |
| `RemoveAt(int index)` | Removes at position (Remove change) |
| `Clear()` | Removes all items (Reset change) |
| `Batch(Action<List<T>>)` | Mutate inner list directly, single Reset notification |
| `this[int index]` | Get or set by index (setter sends Replace change) |
| `Count` | Reactive -- reads are tracked |
| `ConsumePendingChanges()` | Returns and clears queued `ListChange<T>` records |

### Batch mutations

For bulk operations, use `Batch` to avoid per-item notifications:

```csharp
items.Batch(list =>
{
list.AddRange(newData);
list.Sort();
list.RemoveAll(x => x.IsExpired);
});
// Single UI update after the batch completes
```

`SignalList<T>` coalesces changes internally. If more than 100 individual
changes queue before a flush, they collapse into a single Reset.

### Pre-populating a list

```csharp
readonly SignalList<string> items = new(new[] { "Alpha", "Beta", "Gamma" });
```

---

## 10. Component\<TState\> Pattern

`Component<TState>` offers a React-like pattern where state is a plain
class and mutations go through `SetState()`.

### Basic Component

```csharp
public class CounterState
{
public int Count { get; set; }
public string Message { get; set; } = "Ready";
}

public class CounterComponent : Component<CounterState>
{
public override View Render()
{
return VStack(16,
Text($"Count: {State.Count}")
.FontSize(32),
Text(State.Message)
.Color(Colors.Grey),
Button("Increment", () => SetState(s =>
{
s.Count++;
s.Message = $"Incremented to {s.Count}";
}))
);
}
}
```

### How SetState works

1. The mutator lambda receives the current `TState` instance.
2. You modify properties directly on the state object.
3. After the mutator returns, `ReactiveScheduler.MarkViewDirty(this)` is
   called, queueing a single rebuild.
4. `Render()` re-executes and produces a new view tree.

Multiple property mutations inside one `SetState` call are batched -- only
one rebuild occurs.

### Component with Props

`Component<TState, TProps>` accepts parent-supplied data:

```csharp
public class CardProps
{
public string Title { get; set; } = "";
public string Subtitle { get; set; } = "";
}

public class CardState
{
public bool IsExpanded { get; set; }
}

public class ExpandableCard : Component<CardState, CardProps>
{
public override View Render()
{
return VStack(8,
Text(Props.Title)
.FontSize(18)
.FontWeight(FontWeight.Bold),
State.IsExpanded
? Text(Props.Subtitle).Color(Colors.Grey)
: null,
Button(
State.IsExpanded ? "Collapse" : "Expand",
() => SetState(s => s.IsExpanded = !s.IsExpanded))
);
}
}

// Usage from a parent view:
new ExpandableCard
{
Props = new CardProps
{
Title = "Details",
Subtitle = "More info here..."
}
}
```

### ShouldUpdate for memoization

Override `ShouldUpdate` to skip re-renders when props haven't meaningfully
changed:

```csharp
public class OptimizedCard : Component<CardState, CardProps>
{
protected override bool ShouldUpdate(CardProps oldProps, CardProps newProps)
{
return oldProps.Title != newProps.Title
|| oldProps.Subtitle != newProps.Subtitle;
}

public override View Render() { /* ... */ }
}
```

### View + \[Body\] vs Component\<TState\>

| Feature | `View` + `[Body]` | `Component<TState>` |
|---------|--------------------|-----------------------|
| State declaration | `Reactive<T>` fields | Plain C# class |
| Triggering updates | Automatic (write to `.Value`) | Explicit (`SetState`) |
| Render method | `[Body] View body()` | `override View Render()` |
| Update granularity | Fine-grained per control (lambdas) | Full re-render on `SetState` |
| Lifecycle hooks | `OnLoaded`, `ViewDidAppear` | `OnMounted`, `OnWillUnmount` |
| Hot reload state | `Signal<T>` fields transferred | `TState` object transferred by reference |
| Best for | Views with fine-grained reactivity | Self-contained components with complex state |

### When to use Component vs View

- **View + Reactive\<T\>**: Most views. Fine-grained updates mean better
  performance for frequently-changing data (sliders, animations, live text).
- **Component\<TState\>**: When state is complex (many interrelated fields)
  and you want explicit control over when renders happen via `SetState`.
  Good for form-heavy screens, wizards, or components that map closely to
  a data model.

---

## 11. View Lifecycle

### View lifecycle hooks

Views have these hooks, in order of a typical lifecycle:

| Hook | Called when |
|------|------------|
| `OnLoaded()` | Handler assigned (view enters the visual tree) |
| `ViewDidAppear()` | View becomes visible on screen |
| `ViewDidDisappear()` | View is no longer visible |
| `OnUnloaded()` | Handler removed (view leaves the visual tree) |
| `Dispose()` | View is permanently disposed |

Override these in your view subclass:

```csharp
public class DataPage : View
{
readonly Reactive<string> data = "Loading...";

protected override void OnLoaded()
{
base.OnLoaded();
// View is now in the visual tree -- start loading data
LoadDataAsync();
}

protected override void Dispose(bool disposing)
{
if (disposing)
{
// Clean up subscriptions, timers, etc.
}
base.Dispose(disposing);
}

[Body]
View body() => Text(() => data.Value).FontSize(18);

async void LoadDataAsync()
{
var result = await FetchFromApi();
data.Value = result;
}
}
```

### Appearing and Disappearing events

Views also expose events you can subscribe to from the outside:

```csharp
var page = new DataPage();
page.Appearing += (s, e) => Console.WriteLine("Page appeared");
page.Disappearing += (s, e) => Console.WriteLine("Page disappeared");
```

### Component lifecycle hooks

`Component` and `Component<TState>` add two more hooks:

| Hook | Called when |
|------|------------|
| `OnMounted()` | Once, after the component's handler is first assigned |
| `OnWillUnmount()` | Once, when the component is disposed |

```csharp
public class TimerComponent : Component<TimerState>
{
Timer? timer;

protected override void OnMounted()
{
timer = new Timer(_ =>
{
SetState(s => s.Elapsed++);
}, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
}

protected override void OnWillUnmount()
{
timer?.Dispose();
timer = null;
}

public override View Render()
{
return Text($"Elapsed: {State.Elapsed}s").FontSize(24);
}
}

public class TimerState
{
public int Elapsed { get; set; }
}
```

### Initializing state from async operations

A common pattern is loading data when a view appears. Because `[Body]`
runs on the UI thread, never block it with synchronous I/O:

```csharp
public class UserListView : View
{
readonly Reactive<string> status = "Loading...";
readonly SignalList<string> users = new();

protected override void OnLoaded()
{
base.OnLoaded();
_ = LoadUsersAsync();
}

async Task LoadUsersAsync()
{
try
{
status.Value = "Loading...";
var result = await HttpClient.GetStringAsync("https://api.example.com/users");
var names = JsonSerializer.Deserialize<string[]>(result) ?? Array.Empty<string>();
users.Batch(list =>
{
list.Clear();
list.AddRange(names);
});
status.Value = $"Loaded {users.Count} users";
}
catch (Exception ex)
{
status.Value = $"Error: {ex.Message}";
}
}

[Body]
View body()
{
if (users.Count == 0)
return Text(() => status.Value).Color(Colors.Grey);

var rows = new View[users.Count];
for (int i = 0; i < users.Count; i++)
{
var name = users[i];
rows[i] = Text(name).FontSize(14);
}
return ScrollView(VStack(4, rows));
}
}
```

---

## 12. Shared State Across Views

Three patterns for sharing state between views.

### Pattern 1: Prop drilling

Pass `Reactive<T>` or `Signal<T>` references through constructors.

```csharp
public class AppState
{
public readonly Reactive<string> Username = "Guest";
public readonly Reactive<int> NotificationCount = 0;
}

public class AppShell : View
{
readonly AppState state = new();

[Body]
View body() =>
VStack(
new HeaderBar(state.Username, state.NotificationCount),
new ContentArea(state)
);
}

public class HeaderBar : View
{
readonly Reactive<string> username;
readonly Reactive<int> notificationCount;

public HeaderBar(Reactive<string> username, Reactive<int> notificationCount)
{
this.username = username;
this.notificationCount = notificationCount;
}

[Body]
View body() =>
HStack(12,
Text(() => username.Value).FontSize(14),
Text(() => $"({notificationCount.Value})")
.Color(Colors.Red)
);
}
```

Simple and explicit. Works well for shallow hierarchies. Gets unwieldy
when passing state through many levels.

### Pattern 2: Dependency injection

Register shared state as a singleton service and inject it where needed.

```csharp
// Define a shared state service
public class SessionState
{
public readonly Reactive<string> Username = "Guest";
public readonly Reactive<bool> IsLoggedIn = false;
public readonly Reactive<int> CartCount = 0;
}

// Register in MauiProgram.cs
builder.Services.AddSingleton<SessionState>();

// Consume in any view via constructor injection
public class CartBadge : View
{
readonly SessionState session;

public CartBadge(SessionState session)
{
this.session = session;
}

[Body]
View body() =>
Text(() => session.CartCount.Value > 0
? $"Cart ({session.CartCount.Value})"
: "Cart")
.FontSize(14);
}
```

Views are resolved by the DI container when navigated to via Shell or
`NavigationView`. For manually constructed views, resolve the service:

```csharp
var session = serviceProvider.GetRequiredService<SessionState>();
var badge = new CartBadge(session);
```

### Pattern 3: Environment values

The environment system cascades data down the view tree. Any descendant
can read environment values without explicit constructor parameters.

```csharp
// Parent sets an environment value
public class ThemeProvider : View
{
[Body]
View body() =>
VStack(
new ContentPage()
).SetEnvironment("app.accent", (object)Colors.DodgerBlue);
}

// Any descendant reads it
public class ContentPage : View
{
[Body]
View body()
{
var accent = this.GetEnvironment<Color>(this, "app.accent");
return Text("Styled content")
.Color(accent ?? Colors.Black);
}
}
```

Environment values cascade from parent to child. They do not propagate
upward or sideways. There are three scopes:

| Scope | Method | Cascades? |
|-------|--------|-----------|
| Cascading (default) | `.SetEnvironment(key, value)` | Yes -- children inherit |
| Local | `.SetEnvironment(key, value, cascades: false)` | No -- only on this view |
| Global | `ContextualObject.Environment.SetValue(key, value)` | Everywhere |

Use environment values for theming, configuration, and data that many
descendants need without threading it through every constructor.

---

## 13. Async State Updates

### Loading pattern: loading, data, error

A standard three-state pattern for async data:

```csharp
public class WeatherView : View
{
readonly Reactive<string> status = "idle";
readonly Reactive<string> temperature = "";
readonly Reactive<string> error = "";

protected override void OnLoaded()
{
base.OnLoaded();
_ = FetchWeatherAsync();
}

async Task FetchWeatherAsync()
{
status.Value = "loading";
error.Value = "";

try
{
var data = await WeatherService.GetCurrentAsync();
temperature.Value = $"{data.Temp} F";
status.Value = "loaded";
}
catch (Exception ex)
{
error.Value = ex.Message;
status.Value = "error";
}
}

[Body]
View body()
{
var s = status.Value; // body-level read for structural changes
return s switch
{
"loading" => Text("Loading weather...").Color(Colors.Grey),
"error" => VStack(8,
Text(() => $"Error: {error.Value}").Color(Colors.Red),
Button("Retry", () => _ = FetchWeatherAsync())
),
"loaded" => Text(() => temperature.Value).FontSize(48),
_ => Button("Load Weather", () => _ = FetchWeatherAsync()),
};
}
}
```

### Background thread writes

`Reactive<T>` and `Signal<T>` can be written from any thread. The
`ReactiveScheduler` dispatches the UI flush to the main thread
automatically:

```csharp
readonly Reactive<string> data = "Loading...";

async Task LoadAsync()
{
// This runs on a thread pool thread
var result = await Http.GetStringAsync(url);

// Safe -- scheduler handles dispatch to UI thread
data.Value = result;
}
```

### Thread safety guarantees

| Operation | Thread safety |
|-----------|-------------|
| `Reactive<T>.Value` setter | Safe from any thread. Uses `StrongBox<T>` swap. |
| `Signal<T>.Value` setter | Safe from any thread. Uses explicit write lock. |
| `ReactiveScheduler.EnsureFlushScheduled()` | Thread-safe. Posts to UI dispatcher. |
| `SignalList<T>` mutations | NOT thread-safe. Use `Dispatcher.Dispatch()` or synchronize externally. |
| `Computed<T>.Value` getter | Thread-safe for reads. Re-evaluation is lazy and cached. |
| `Effect.Run()` | Should run on UI thread. Scheduled by `ReactiveScheduler`. |

### Dispatching SignalList mutations

Since `SignalList<T>` is not thread-safe, dispatch mutations from
background threads:

```csharp
readonly SignalList<string> items = new();

async Task FetchItemsAsync()
{
var data = await Api.GetItemsAsync();

// Dispatch to UI thread for thread safety
Dispatcher.Dispatch(() =>
{
items.Batch(list =>
{
list.Clear();
list.AddRange(data);
});
});
}
```

### Known limitation: background thread UI updates

Signal writes from background threads (e.g. inside `Task.Run` or HTTP
handlers) schedule a UI flush via the dispatcher. In rare cases, the flush
may not reliably update platform controls when the write and the dispatch
overlap. If you observe missed updates from async code, dispatch the write
explicitly:

```csharp
Dispatcher.Dispatch(() => data.Value = result);
```

---

## 14. Hot Reload and State Preservation

Comet integrates with .NET Hot Reload. Edit code, save, and see changes
without restarting the app.

### What happens during hot reload

1. The IDE detects a code change and sends the updated type.
2. `MauiHotReloadHelper.RegisterReplacedView` registers the replacement.
3. `TriggerReload()` fires, calling `Reload()` on all active views.
4. Each view's `TransferHotReloadStateTo` copies state from the old
   instance to the new one.
5. The new body executes, the view tree is diffed, and handlers are reused
   where possible.

### Signal\<T\> fields: automatic transfer

`Signal<T>` fields are transferred by reflection during hot reload. The
framework matches fields by name and type between the old and new view
instances:

```csharp
public class MyView : View
{
// This Signal<int> field survives hot reload -- value is preserved
readonly Signal<int> count = new(0);

[Body]
View body() =>
Text(() => $"Count: {count.Value}");
}
```

- Field name and type must match between old and new code
- Adding a new field gives it its initial value
- Removing a field drops it silently
- Renaming a field resets it (treated as remove + add)

### Reactive\<T\> fields: NOT automatically transferred

`Reactive<T>` fields are not transferred during hot reload. The view
rebuilds correctly, but accumulated state resets to the initial value:

```csharp
public class MyView : View
{
// This resets to 0 on hot reload
readonly Reactive<int> count = 0;

// To preserve across hot reload, use Signal<T> instead:
// readonly Signal<int> count = new(0);
}
```

This is a known gap. If state preservation during hot reload is critical,
use `Signal<T>`.

### Component state: transferred by reference

`Component<TState>` state survives hot reload. The `TransferStateFrom`
method copies the entire `TState` object by reference:

```csharp
public class MyComponent : Component<MyState>
{
// State.Count preserves its value across hot reload
public override View Render()
{
return Text($"Count: {State.Count}");
}
}
```

Both state and props are transferred for `Component<TState, TProps>`.

### Computed and Effect during hot reload

Constructors re-run on hot reload. `Computed<T>` and `Effect` instances
created in the constructor are re-created, which is usually fine:

- `Computed<T>` re-discovers its dependencies automatically on first access
- `Effect` re-runs and re-captures dependencies
- Their old instances are disposed during the old view's cleanup

### Summary

| State type | Hot reload behavior |
|------------|-------------------|
| `Signal<T>` field | Transferred (field name + type match) |
| `Reactive<T>` field | Resets to initial value |
| `Component<TState>.State` | Transferred by reference |
| `Component<TState, TProps>.Props` | Transferred by reference |
| `Computed<T>` | Re-created (re-discovers dependencies) |
| `Effect` | Re-created (re-runs and re-captures) |
| Environment data | Transferred (context dictionaries copied) |

---

## 15. Best Practices

### Declare state fields as readonly

```csharp
// Prevents accidental reassignment of the reactive wrapper
readonly Reactive<int> count = 0;
```

### Use lambdas for display, callbacks for writes

```csharp
// Display: Func<T> lambda
Text(() => $"Value: {x.Value}")

// Write: Action callback
Button("Go", () => x.Value++)
TextField(() => x.Value, () => "...").OnTextChanged(v => x.Value = v ?? "")
```

### Keep body() fast

The body method runs on the UI thread. Never block it. For performance
guidelines on body evaluation cost, see the
[Performance Optimization Guide](performance.md).

```csharp
// WRONG -- blocks UI thread
[Body]
View body()
{
var data = LoadFromDatabase();
return Text(data);
}

// RIGHT -- load async, store in state
readonly Reactive<string> data = "Loading...";

protected override void OnLoaded()
{
base.OnLoaded();
_ = Task.Run(async () =>
{
var result = await LoadFromDatabase();
data.Value = result;
});
}

[Body]
View body() => Text(() => data.Value);
```

### Rapid writes coalesce automatically

Multiple signal writes within a synchronous handler produce one UI update:

```csharp
Button("Add 100", () =>
{
for (int i = 0; i < 100; i++)
count.Value++;
// ONE body rebuild after this handler returns
})
```

The `ReactiveScheduler` posts a single flush to the dispatcher. This
applies to writes from background threads too.

### Capture loop variables

```csharp
for (int i = 0; i < items.Count; i++)
{
var index = i; // capture -- closures see final value of i otherwise
Button($"Item {index}", () => selectedIndex.Value = index);
}
```

### Prefer lambda reads for frequently-changing values

For controls driven by rapid input (Slider, TextField), always use lambda
bindings to avoid full body rebuilds:

```csharp
// Fine-grained -- only the Text updates per slider drag
Slider(() => val.Value, () => 0.0, () => 100.0)
.OnValueChanged(v => val.Value = v),
Text(() => $"Value: {val.Value:F1}")

// Avoid -- rebuilds entire view tree on every drag
var current = val.Value;
Text($"Value: {current:F1}")
```

### Dispose Computed and Effect

Clean up subscriptions when the view is disposed:

```csharp
readonly Computed<string> derived;
Effect? sideEffect;

protected override void Dispose(bool disposing)
{
if (disposing)
{
derived?.Dispose();
sideEffect?.Dispose();
}
base.Dispose(disposing);
}
```

### Use SetState for multi-field Component mutations

When a Component has multiple related state fields, `SetState` batches
them into one render:

```csharp
// One render, not two
SetState(s =>
{
s.Count++;
s.Message = $"Count is now {s.Count}";
});
```

---

## 16. Quick Reference

```csharp
using Comet;
using Comet.Reactive;
using static Comet.CometControls;

// --- State primitives ---
readonly Reactive<int> count = 0;              // mutable state
readonly Signal<int> precise = new(0);         // lower-level, Peek(), DebugName
readonly SignalList<string> items = new();      // reactive collection

// --- Derived state (initialize in constructor) ---
readonly Computed<string> label;
// label = new Computed<string>(() => $"Count is {count.Value}");

// --- Side effects (initialize in constructor) ---
// var effect = new Effect(() => Console.WriteLine($"Count: {count.Value}"));

// --- One-way display (Func lambda) ---
Text(() => $"Count: {count.Value}")

// --- Write (Action callback) ---
Button("Add", () => count.Value++)

// --- Two-way binding ---
TextField(() => name.Value, () => "Placeholder...")
.OnTextChanged(v => name.Value = v ?? "")

Slider(() => val.Value, () => 0.0, () => 100.0)
.OnValueChanged(v => val.Value = v)

Toggle(() => isOn.Value)
.OnToggled(v => isOn.Value = v)

// --- Non-tracking read ---
var x = mySignal.Peek(); // Signal<T> only, no dependency created

// --- Batch list mutations ---
items.Batch(list => { list.AddRange(data); list.Sort(); });

// --- Component with SetState ---
SetState(s => { s.Count++; s.Label = "Updated"; });

// --- Static (no reactivity) ---
Text("Hello, World!")

// --- Environment ---
view.SetEnvironment("key", (object)value);
var val = this.GetEnvironment<Color>(this, "key");
```


## See Also

- [Performance Optimization](performance.md) -- how state patterns impact
  performance, including body-level vs property-level updates and the diff
  algorithm.
- [Testing Guide](testing.md) -- patterns for testing reactive state, including
  Signal, Computed, Effect, and view rebuild verification.
- [Troubleshooting](troubleshooting.md) -- common state bugs like missing UI
  updates, slider drag resets, and StackOverflowException during state updates.
- [Form Handling](forms.md) -- two-way binding patterns for form controls using
  Signal and PropertySubscription.
