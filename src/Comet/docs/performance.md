# Performance Optimization Guide

This guide covers how the Comet framework schedules and batches UI updates,
when and why rebuilds happen, and techniques for keeping your application
responsive. Every recommendation is grounded in the actual implementation of
`ReactiveScheduler`, `Signal<T>`, `PropertySubscription<T>`, and the view diff
algorithm.


## How Rebuilds Work

### The Reactive Scheduler

All UI updates in Comet are coordinated by `ReactiveScheduler`, a static
scheduler that coalesces dirty notifications into a single batch flush on the
main thread.

When a `Signal<T>` value changes, the scheduler does not rebuild views
immediately. Instead it marks the affected views or effects as dirty and posts
a single flush to the dispatcher. Multiple signals can change in quick
succession and the scheduler will still perform only one flush.

```
Signal.Value = x   -->  MarkViewDirty(view)  -->  EnsureFlushScheduled()
Signal.Value = y   -->  MarkViewDirty(view)  -->  (already scheduled, no-op)
                         ...
                   -->  Dispatcher runs FlushEntry()
                   -->  Flush(depth=0): snapshot dirty sets, clear, process
```

Key implementation details:

- `_dirtyViews` and `_dirtyEffects` are `HashSet<T>` collections. Duplicate
  marks are coalesced automatically -- if the same view is marked dirty three
  times before the flush runs, it rebuilds exactly once.
- `Flush()` takes an atomic snapshot of the dirty sets, clears them, then
  processes the snapshot. Effects run first, then views. If processing creates
  new dirty entries, Flush recurses up to `MaxFlushDepth` (100) iterations
  before throwing to prevent infinite loops.
- Notifications are suppressed during `UpdateFromOldView()` to prevent
  cascading rebuilds while the diff algorithm transfers state between old and
  new view trees.


### Body Rebuilds vs. Property-Level Updates

Comet provides two levels of reactivity, and choosing the right one is the
single most impactful performance decision you can make.

**Body-level rebuild.** When a signal is read inside a view's `Body` function,
the framework tracks that dependency via `GetRenderViewReactive()`. Any change
to the signal marks the entire view dirty and triggers a full body re-execution
followed by a tree diff.

```csharp
class CounterView : View
{
	readonly Signal<int> count = new(0);

	[Body]
	View body() => new VStack
	{
		// Reading count.Value inside Body creates a body-level dependency.
		// Every change to count triggers a full rebuild of this VStack.
		new Text(() => $"Count: {count.Value}"),
		new Button("Increment", () => count.Value++)
	};
}
```

**Property-level (fine-grained) update.** When a signal is read inside a
lambda passed to a control constructor, the framework uses
`PropertySubscription<T>` to track the dependency at the property level. A
change to the signal updates only that one property -- no body rebuild, no
tree diff.

```csharp
class CounterView : View
{
	readonly Signal<int> count = new(0);

	[Body]
	View body() => new VStack
	{
		// The lambda () => $"Count: {count.Value}" is evaluated by
		// PropertySubscription. Changes to count update only Text.Value,
		// not the entire body.
		new Text(() => $"Count: {count.Value}"),
		new Button("Increment", () => count.Value++)
	};
}
```

The difference is subtle in this example because `Text(() => ...)` creates a
`PropertySubscription<string>` that tracks `count` at the property level. The
body itself does not read `count.Value` directly -- the lambda is evaluated
later, inside the subscription scope. This is the preferred pattern. See the
[Reactive State Guide](reactive-state-guide.md) for a comprehensive treatment
of fine-grained vs body-level reactivity.

The cost comparison:

| Mechanism | Trigger | Rebuild scope | Cost |
|-----------|---------|---------------|------|
| Body dependency | Signal read in Body | Full view tree rebuild + diff | O(tree size) |
| PropertySubscription | Signal read in lambda | Single property update | O(1) |

**Rule of thumb:** Pass lambdas (`() => ...`) to control constructors for
reactive values. Avoid reading `.Value` directly in the body method outside of
a lambda.


### Signal<T>.Peek() -- Non-Tracking Reads

Sometimes you need the current value of a signal without creating a
dependency. `Peek()` returns the value without registering a read in the
active `ReactiveScope`:

```csharp
readonly Signal<int> count = new(0);

[Body]
View body()
{
	// Peek() reads the value without tracking. Changes to count
	// will NOT trigger a body rebuild.
	var initial = count.Peek();

	return new VStack
	{
		new Text($"Started at: {initial}"),
		new Text(() => $"Current: {count.Value}"),
		new Button("Increment", () => count.Value++)
	};
}
```

Use `Peek()` when:

- You need an initial value for display but do not want the view to re-render
  when it changes.
- You are reading state inside an event handler or callback (not a reactive
  context).
- You are comparing old and new values in validation logic.


## Minimizing Rebuild Cost

### Keep Bodies Lightweight

The body method runs on every rebuild. Expensive logic in the body function
directly impacts UI responsiveness:

```csharp
// BAD: expensive computation runs on every rebuild
[Body]
View body()
{
	var sorted = items.Value
		.OrderBy(x => x.Name)
		.Select(x => new ItemView(x))
		.ToList();

	return new VStack { sorted };
}

// GOOD: use Computed<T> to memoize the expensive work
readonly Signal<List<Item>> items = new(new());
readonly Computed<List<Item>> sortedItems;

public MyView()
{
	sortedItems = new Computed<List<Item>>(
		() => items.Value.OrderBy(x => x.Name).ToList());
}

[Body]
View body() => new VStack
{
	sortedItems.Value.Select(x => new ItemView(x))
};
```

`Computed<T>` re-evaluates only when its dependencies change, and it caches
the result. If the body rebuilds for an unrelated reason, the computed value
is returned from cache.


### Batch State Mutations with SetState

In `Component<TState>`, the `SetState()` method mutates state and marks the
component dirty. The scheduler coalesces multiple `SetState` calls into a
single rebuild:

```csharp
class TodoApp : Component<TodoState>
{
	protected override View Render()
	{
		return new VStack
		{
			new Button("Add Three", () =>
			{
				// Three SetState calls, but ReactiveScheduler coalesces
				// them into a single rebuild because they all call
				// MarkViewDirty(this) with the same view reference.
				SetState(s => s.Items.Add(new TodoItem("Task A")));
				SetState(s => s.Items.Add(new TodoItem("Task B")));
				SetState(s => s.Items.Add(new TodoItem("Task C")));
			})
		};
	}
}
```

For clarity and correctness, prefer a single `SetState` call that applies all
mutations:

```csharp
SetState(s =>
{
	s.Items.Add(new TodoItem("Task A"));
	s.Items.Add(new TodoItem("Task B"));
	s.Items.Add(new TodoItem("Task C"));
});
```


### Collection Performance: SignalList vs. ObservableCollection

`SignalList<T>` is purpose-built for Comet's reactive scheduler:

| Aspect | SignalList | ObservableCollection |
|--------|-----------|----------------------|
| Notification model | Batched via pending queue | Per-operation events |
| Tracking | ReactiveScope integration | No automatic tracking |
| Change delivery | `ConsumePendingChanges()` | `CollectionChanged` event |
| Mutation cost | O(1) per operation, queued | O(n) listener notification |
| Overflow behavior | Resets after 100 pending changes | N/A |

For bulk mutations, use `Batch()`:

```csharp
readonly SignalList<string> items = new();

void LoadData(IEnumerable<string> data)
{
	// BAD: each Add queues a separate change notification
	foreach (var item in data)
		items.Add(item);

	// GOOD: single Reset notification after all mutations
	items.Batch(list =>
	{
		list.Clear();
		list.AddRange(data);
	});
}
```

`SignalList<T>` caps pending changes at 100 entries. Beyond that it
automatically collapses to a single `Reset` change. If you are frequently
hitting this cap, use `Batch()` for cleaner semantics.


### Lazy View Construction

Views that are not immediately visible should be constructed lazily to reduce
initial body evaluation cost:

```csharp
[Body]
View body()
{
	var showDetails = detailsVisible.Value;

	return new VStack
	{
		new Text("Summary"),
		// Only construct the expensive detail view when needed
		showDetails
			? new DetailView(data)
			: (View)new Text("Tap to expand")
	};
}
```

For tab-based layouts, each tab's content is a separate `View` subclass.
Comet only evaluates the body of the active tab.


## The Diff Algorithm

When a view rebuilds, Comet does not tear down the entire native view
hierarchy. The diff algorithm in `DatabindingExtensions.Diff()` compares the
old and new view trees and reuses platform handlers where possible.

The algorithm works in two modes:

**Index-based (default).** Children are compared by position. If the view at
index N has the same type in both old and new trees, the old handler is
transferred. Added, removed, or type-changed children trigger handler
creation or disposal.

**Key-aware.** When any child has a key assigned via `.Key(string)`, the
algorithm switches to dictionary-based matching. Old children are indexed by
key for O(1) lookup. This preserves view identity across reorders:

```csharp
[Body]
View body() => new VStack
{
	items.Value.Select(item =>
		new ItemRow(item)
			.Key(item.Id.ToString()))
};
```

Without keys, reordering a list causes every shifted item to be diffed against
a different old item, which breaks animation continuity and wastes handler
creation. With keys, each item matches its previous instance regardless of
position.

Cost comparison:

| Mode | Best case | Worst case |
|------|-----------|------------|
| Index-based | O(n) | O(n) with handler churn on reorder |
| Key-aware | O(n) | O(n) with dictionary overhead |

Use keys when:

- List items can be reordered, inserted, or removed by the user.
- Items carry visual state (animations, scroll position) that should survive
  reorder.
- You are rendering a collection larger than a few dozen items.


## Computed<T> for Derived Values

`Computed<T>` memoizes a derived value and only re-evaluates when one of its
tracked dependencies changes:

```csharp
readonly Signal<List<Item>> allItems = new(new());
readonly Signal<string> filterText = new("");

readonly Computed<List<Item>> filteredItems;

public MyView()
{
	filteredItems = new Computed<List<Item>>(() =>
		allItems.Value
			.Where(i => i.Name.Contains(filterText.Value))
			.ToList());
}
```

The computed value caches its result. If neither `allItems` nor `filterText`
has changed, reading `filteredItems.Value` returns the cached list with zero
allocation. The comparer uses `EqualityComparer<T>.Default` to determine
whether the new result differs from the cached one; if they are equal, no
downstream notifications fire.


## Effect Batching

`Effect` runs a side-effect function whenever its dependencies change. Effects
are batched by the scheduler alongside view rebuilds:

```csharp
readonly Signal<string> searchQuery = new("");

public MyView()
{
	new Effect(() =>
	{
		// This runs once initially, then re-runs when searchQuery changes.
		// Multiple rapid changes are coalesced by the scheduler.
		Console.WriteLine($"Searching: {searchQuery.Value}");
	});
}
```

Key characteristics:

- Effects run after all dirty effects and views are processed in the current
  flush iteration, not immediately on dependency change.
- If an effect's function throws, the effect marks itself as not dirty and
  does not re-run until a dependency changes again.
- The scheduler coalesces duplicate effect marks via its `HashSet<Effect>`.
- Effects are disposed by calling `Dispose()`, which unsubscribes from all
  tracked dependencies.


## Common Anti-Patterns

### Reading .Value Directly in Body

```csharp
// BAD: creates body-level dependency on every signal read
[Body]
View body() => new VStack
{
	new Text($"Name: {name.Value}"),    // body-level dependency
	new Text($"Score: {score.Value}"),  // body-level dependency
	new Text($"Rank: {rank.Value}")     // body-level dependency
};

// GOOD: lambda constructors create property-level subscriptions
[Body]
View body() => new VStack
{
	new Text(() => $"Name: {name.Value}"),
	new Text(() => $"Score: {score.Value}"),
	new Text(() => $"Rank: {rank.Value}")
};
```

In the bad example, changing any one signal rebuilds the entire body. In the
good example, each `Text` independently subscribes to its signal.


### Over-Granular State

```csharp
// BAD: separate signals for tightly coupled state
readonly Signal<string> firstName = new("");
readonly Signal<string> lastName = new("");
readonly Signal<string> email = new("");
readonly Signal<int> age = new(0);

// GOOD: group related state in a BindingObject or Component<TState>
class UserState
{
	public string FirstName { get; set; } = "";
	public string LastName { get; set; } = "";
	public string Email { get; set; } = "";
	public int Age { get; set; }
}

class UserForm : Component<UserState>
{
	protected override View Render() => new VStack
	{
		new TextField(() => State.FirstName),
		new TextField(() => State.LastName),
		new TextField(() => State.Email)
	};
}
```

Too many independent signals create excessive subscriptions and make it
harder to batch related mutations.


### Allocating in Body

```csharp
// BAD: new list allocation on every rebuild
[Body]
View body() => new VStack
{
	new List<string> { "A", "B", "C" }.Select(x => new Text(x))
};

// GOOD: constant data defined once
static readonly string[] options = { "A", "B", "C" };

[Body]
View body() => new VStack
{
	options.Select(x => new Text(x))
};
```


### Nested Body Dependencies

If you must read a signal in the body for branching, use `Peek()` for the
branch condition and a lambda for display:

```csharp
[Body]
View body()
{
	var current = mode.Peek();  // no dependency
	return new VStack
	{
		new Text(() => $"Mode: {mode.Value}"),  // fine-grained
		current == "edit" ? new EditView() : new ReadView()
	};
}
```


## Profiling Tips

1. **Count rebuilds.** Add a counter or log statement inside your body method
   to see how often it runs. If it runs more than expected, check for
   unintentional body-level dependencies.

2. **Check the scheduler.** `ReactiveScheduler.MaxFlushDepth` is 100. If you
   hit this limit, you have a cycle -- an effect or computed that triggers
   its own dependency. Fix the cycle rather than raising the limit.

3. **Use Peek() defensively.** When debugging, temporarily replace `.Value`
   with `.Peek()` to see if a specific signal is causing unnecessary
   rebuilds. If the UI still works, the body-level dependency was not needed.

4. **Profile Computed re-evaluations.** If a `Computed<T>` re-evaluates too
   often, verify its dependencies are minimal. A computed that reads five
   signals re-evaluates when any of them changes.

5. **Watch SignalList overflow.** If `ConsumePendingChanges()` returns a
   `Reset` change, your list exceeded 100 pending operations. Switch to
   `Batch()` for bulk updates.

6. **Measure diff cost.** For deep view trees, the diff algorithm walks every
   node. Flatten your hierarchy where possible and extract stable subtrees
   into separate `View` subclasses so they diff independently.


## Summary

| Technique | When to use | Impact |
|-----------|-------------|--------|
| Lambda constructors `() => ...` | Always for reactive values | O(1) updates vs. O(tree) rebuilds |
| `Signal<T>.Peek()` | Non-reactive reads in body | Prevents unnecessary rebuild |
| `Computed<T>` | Expensive derived values | Memoized, re-evaluates only on change |
| `SignalList<T>.Batch()` | Bulk collection mutations | Single notification vs. per-item |
| `.Key(string)` | Dynamic lists | Preserves view identity on reorder |
| `SetState` grouping | Multiple state changes | Scheduler coalesces into one rebuild |
| Separate View subclasses | Stable subtrees | Diff scope limited to subtree |

For a deep dive on the diff algorithm, see the
[Architecture Overview](architecture.md).


## See Also

- [Reactive State Guide](reactive-state-guide.md) -- state patterns that
  directly affect performance, including Signal, Computed, and Peek.
- [Layout System](layout.md) -- layout containers and how view tree depth
  impacts diff cost.
- [Architecture Overview](architecture.md) -- the diff algorithm implementation
  details and handler reuse strategy.
