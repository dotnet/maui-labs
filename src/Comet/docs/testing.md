# Testing Guide

This guide covers how to write and run tests for Comet. It describes the test
infrastructure, patterns for testing reactive state, view trees, and hot reload
behavior, and the build requirements specific to this project.


## Prerequisites

Comet tests target `net10.0` and reference the Comet assembly built for Mac
Catalyst. Before running tests, you must build the source generator and the
framework in the correct order:

```bash
# 1. Build the source generator (netstandard2.0)
dotnet build src/Comet.SourceGenerator/Comet.SourceGenerator.csproj -c Release

# 2. Build Comet for Mac Catalyst (produces the DLL that tests reference)
dotnet build src/Comet/Comet.csproj -c Release

# 3. Build the test project
dotnet build tests/Comet.Tests/Comet.Tests.csproj -c Release

# 4. Run all tests
dotnet test tests/Comet.Tests/Comet.Tests.csproj --no-build -c Release
```

The test project references `src/Comet/bin/$(Configuration)/net10.0-maccatalyst/Comet.dll`
directly via a `<Reference>` element rather than a `<ProjectReference>`. If
Comet has not been built for Mac Catalyst, the test project will fail to compile
with a missing assembly error.

### Running a Single Test

Use the `--filter` flag with the fully qualified name:

```bash
dotnet test tests/Comet.Tests/Comet.Tests.csproj --no-build -c Release \
	--filter "FullyQualifiedName~SignalTests.Signal_Int_ReadWriteRoundTrip"
```


## Test Infrastructure

### TestBase

Every test class inherits from `TestBase`, which lives in
`tests/Comet.Tests/TestBase.cs`. Its constructor calls `UI.Init()` to set up the
MAUI service provider and handler registrations that the framework requires.

```csharp
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Comet.Tests
{
	public class TestBase
	{
		public TestBase()
		{
			UI.Init();
		}
	}
}
```

Test parallelization is disabled at the assembly level. Comet relies on global
state (the reactive scheduler, environment system, and hot reload helper), so
concurrent test execution causes nondeterministic failures.

### UI.Init

`UI.Init()` in `tests/Comet.Tests/UI.cs` builds a minimal `MauiApp` with
handler registrations for the core control set:

- `Button`, `ContentView`, `Image`, `HStack`, `VStack`, `ZStack`, `ScrollView`,
  `ListView`, `Toggle`, `NativeHost`, `View` -- all use `GenericViewHandler`
- `Text` -- uses `TextHandler`
- `TextField` -- uses `TextFieldHandler`
- `Slider` -- uses `SliderHandler`
- `ProgressBar` -- uses `ProgressBarHandler`
- `SecureField` -- uses `SecureFieldHandler`

It also sets `MauiHotReloadHelper.IsEnabled = true` and applies the default
style. The `ThreadHelper` is configured to invoke actions synchronously, which
means scheduler flushes and state updates happen inline rather than being posted
to a dispatcher.

### InitializeHandlers

`TestBase.InitializeHandlers(View view)` walks a view tree and assigns a handler
to every node, using the same handler factory that MAUI uses at runtime. This
is required when your test needs to verify handler-level behavior such as
property mapper callbacks or platform view bridging.

```csharp
var view = new MyTestView();
view.GetView(); // Build the view tree
InitializeHandlers(view);
```

The overload `InitializeHandlers(View view, float width, float height)` also
runs measurement and arrangement, which is useful for layout tests.

### ResetComet

Call `ResetComet()` when a test mutates global state that could leak into
subsequent tests:

```csharp
public static void ResetComet()
{
	var v = new View();
	v.ResetGlobalEnvironment();
	UI.Init(true);
	MauiHotReloadHelper.Reset();
	CometHotReloadHelper.Reset();
	v?.Dispose();
}
```

This clears the global environment, reinitializes the service provider, and
resets the hot reload helpers.


## Testing Reactive State

### Signal

`Signal<T>` is the thread-safe reactive primitive. Tests verify read/write
round-trips, version tracking, equality checks, implicit conversions, and
concurrent access.

```csharp
public class SignalExampleTests : TestBase
{
	[Fact]
	public void Signal_ReadWrite()
	{
		var count = new Signal<int>(0);
		Assert.Equal(0, count.Value);

		count.Value = 42;
		Assert.Equal(42, count.Value);
	}

	[Fact]
	public void Signal_SameValue_DoesNotNotify()
	{
		var signal = new Signal<string>("hello");
		int notifyCount = 0;

		var subscriber = new TestSubscriber(() => notifyCount++);
		signal.Subscribe(subscriber);

		signal.Value = "hello"; // Same value
		Assert.Equal(0, notifyCount);
	}

	[Fact]
	public void Signal_Peek_DoesNotTrack()
	{
		var signal = new Signal<int>(10);
		using var scope = ReactiveScope.BeginTracking();
		var value = signal.Peek();
		var deps = scope.EndTracking();
		Assert.Empty(deps);
	}
}
```

### Reactive

`Reactive<T>` is the public-facing wrapper with implicit conversions. It is
non-sealed and serves as the base for `State<T>`. Tests cover value change
notifications, null handling, collection types, and disposal safety.

```csharp
[Fact]
public void ReactiveNotifiesOnValueChange()
{
	var reactive = new Reactive<int>(0);
	int callbackValue = -1;
	reactive.ValueChanged = (v) => callbackValue = v;

	reactive.Value = 5;
	Assert.Equal(5, callbackValue);
}
```

### Computed

`Computed<T>` provides lazy-evaluated derived state. Tests verify lazy
evaluation, caching, invalidation on dependency change, diamond dependency
handling, and exception recovery.

```csharp
[Fact]
public void Computed_LazyEvaluation()
{
	int evalCount = 0;
	var a = new Signal<int>(1);
	var sum = new Computed<int>(() => { evalCount++; return a.Value + 1; });

	Assert.Equal(0, evalCount); // Not evaluated yet
	var result = sum.Value;
	Assert.Equal(1, evalCount);
	Assert.Equal(2, result);
}
```

### ReactiveScope

`ReactiveScope` tracks which reactive sources are read during a block of code.
It uses `[ThreadStatic]` storage, so background thread reads are untracked by
design.

```csharp
[Fact]
public void Scope_CapturesReads()
{
	var a = new Signal<int>(1);
	var b = new Signal<int>(2);

	using var scope = ReactiveScope.BeginTracking();
	var _ = a.Value;
	var __ = b.Value;
	var deps = scope.EndTracking();

	Assert.Equal(2, deps.Count);
	Assert.Contains(a, deps);
	Assert.Contains(b, deps);
}
```

### SetState and View Rebuilds

When a `Signal` or `Reactive` value changes, the reactive scheduler marks the
owning view as dirty and triggers a body rebuild. Tests verify this by counting
rebuilds:

```csharp
public class CounterView : View
{
	public readonly Reactive<int> count = new(0);
	public int RebuildCount { get; private set; }

	[Body]
	View body() => new Text(() =>
	{
		RebuildCount++;
		return $"Count: {count.Value}";
	});
}

[Fact]
public void ViewRebuildsOnStateChange()
{
	var view = new CounterView();
	view.GetView(); // Initial build
	int initial = view.RebuildCount;

	view.count.Value = 1;
	ReactiveScheduler.FlushSync();

	Assert.True(view.RebuildCount > initial);
}
```


## Testing View Trees

You can verify the structure of a view tree without running the app. Call
`GetView()` on a view to trigger the body evaluation, then walk the result.

```csharp
public class MyPage : View
{
	[Body]
	View body() => new VStack
	{
		new Text("Title"),
		new Button("Tap me"),
	};
}

[Fact]
public void ViewTree_ContainsExpectedChildren()
{
	var page = new MyPage();
	var built = page.GetView();

	Assert.IsType<VStack>(built);
	var stack = (VStack)built;
	Assert.Equal(2, stack.Count);
	Assert.IsType<Text>(stack[0]);
	Assert.IsType<Button>(stack[1]);
}
```

### Testing Computed Values in Views

To test that bindings produce the expected output, access the binding property
after building the view:

```csharp
[Fact]
public void TextBindingReflectsState()
{
	var view = new CounterView();
	view.GetView();

	var text = view.GetView() as Text;
	// The text handler tracks property changes
	InitializeHandlers(view);
}
```

### Testing with Handlers

When you need to verify that property changes propagate to handlers, initialize
handlers and check the `ChangedProperties` list:

```csharp
[Fact]
public void SliderUpdate_NotifiesHandler()
{
	var slider = new Slider(new Binding<double>(() => 0.5));
	slider.GetView();
	var handler = SetViewHandlerToGeneric(slider);

	// Simulate a state change that updates the slider value
	ReactiveScheduler.FlushSync();

	Assert.Contains(
		nameof(IRange.Value),
		handler.ChangedProperties
	);
}
```


## Testing Hot Reload

Comet's hot reload system replaces view types at runtime and transfers state to
the new instances. Three test files cover this behavior.

### Basic View Replacement (HotReloadTestsNoParameters)

Tests that `CometHotReloadHelper.RegisterReplacedView()` replaces a view class
with a new implementation:

```csharp
public class HotReloadTests : TestBase
{
	class MyOrgView : View
	{
		[Body]
		View body() => new Text("Original");
	}

	class MyNewView : View
	{
		[Body]
		View body() => new Text("Replaced");
	}

	[Fact]
	public void HotReloadRegisterReplacedViewReplacesView()
	{
		MauiHotReloadHelper.IsEnabled = true;
		var orgView = new MyOrgView();
		orgView.GetView();

		CometHotReloadHelper.RegisterReplacedView(
			typeof(MyOrgView).FullName,
			typeof(MyNewView)
		);

		// After reload, the view tree should reflect the new type
	}
}
```

### View Replacement with Parameters (HotReloadWithParameters)

Tests replacement of views that take constructor arguments, including required
and default parameters. The views register their parameters with
`MauiHotReloadHelper.Register(this, ...)`:

```csharp
class MyOrgView : View
{
	public MyOrgView(string text)
	{
		MauiHotReloadHelper.Register(this, text);
	}

	[Body]
	View body() => new Text(text);
	readonly string text;
}
```

### State Transfer (ReloadTransfersStateTest)

Tests that `Signal<T>` fields are transferred to the reloaded view instance.
`Reactive<T>` fields are not transferred -- only `Signal<T>` participates in
hot reload state transfer.

```csharp
class MyOrgView : View
{
	public readonly Signal<string> title = new("Original");
	public readonly Signal<bool> isEnabled = new(true);
	Reactive<bool> myBoolean = new(false); // Not transferred

	[Body]
	View body() => new Text(() => title.Value);
}

[Fact]
public void StateIsTransferredToReloadedView()
{
	var orgView = new MyOrgView();
	orgView.title.Value = "Modified";
	orgView.GetView();

	// After hot reload, the new view should have the same
	// Signal instances as the original
}
```


## Threading and Concurrency Tests

The reactive system is designed for thread-safe writes from background threads.
Tests verify this with concurrent access patterns:

```csharp
[Fact]
public async Task Signal_ConcurrentWrites_NoTornReads()
{
	var signal = new Signal<decimal>(0m);
	var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
	var tasks = new Task[4];

	for (int i = 0; i < tasks.Length; i++)
	{
		int id = i;
		tasks[i] = Task.Run(() =>
		{
			while (!cts.IsCancellationRequested)
				signal.Value = id * 1.1m;
		});
	}

	await Task.WhenAll(tasks);
	// No torn reads or exceptions
}
```

The `ReactiveScheduler` tests verify coalescing (multiple writes produce a
single flush), the max flush depth guard (100 iterations before breaking an
infinite loop), and effect ordering (effects run before view reloads).


## Test Organization

Tests are organized by feature area in subdirectories, all sharing the flat
`Comet.Tests` namespace:

| Directory | Coverage |
|-----------|----------|
| `ReactiveTests/` | Signal, Computed, Effect, ReactiveScope, PropertySubscription, ReactiveScheduler |
| `HotReloadTests/` | Hot reload integration tests |
| `ComponentTests/` | Component base class, state, props, lifecycle |
| `NavigationApiTests/` | Shell routing, typed navigation |
| `ReconciliationTests/` | Key-aware view diffing |
| `Styles/` | Token, ViewModifier, Theme, ControlStyle |
| `ThemeTests/` | Theme system integration |
| `Handlers/` | Handler-specific tests |
| Root | View tests, binding tests, layout tests, bug fixes |


## Mocking Patterns

The test infrastructure uses lightweight test handlers rather than mocks:

- `GenericViewHandler` -- a no-op handler that satisfies the handler contract
  without platform dependencies.
- `TextHandler`, `SliderHandler`, etc. -- minimal handlers that track property
  changes via a `ChangedProperties` list, enabling assertions without a running
  platform layer.
- `TestSubscriber` -- implements `IReactiveSubscriber` to count or capture
  dependency change notifications.

There is no dependency on a mocking framework. Tests create real Comet views and
verify behavior through the public API.


## Async Test Patterns

Async tests follow standard xunit patterns with `async Task` return types.
Use `ReactiveScheduler.FlushSync()` to force synchronous processing of pending
state updates when you need deterministic behavior in tests:

```csharp
[Fact]
public void FlushSync_ProcessesPending()
{
	var signal = new Signal<int>(0);
	var effect = new Effect(() => { var _ = signal.Value; }, runImmediately: true);

	signal.Value = 42;
	ReactiveScheduler.FlushSync();

	// Effect has run with the new value
}
```

For concurrent tests, use `CancellationTokenSource` with a timeout to bound
execution and `Task.Run` to simulate background thread writes.


## Common Pitfalls

- **Forgetting to call `GetView()`**: The view tree is not built until
  `GetView()` is called. Assertions on `BuiltView` will see `null` without it.
- **Skipping `FlushSync()`**: State changes are batched and flushed
  asynchronously. In tests, call `ReactiveScheduler.FlushSync()` to process
  pending updates immediately.
- **Global state leaks**: If a test modifies `Theme.Current`, global
  environment keys, or hot reload state, call `ResetComet()` in cleanup to
  prevent interference with subsequent tests.
- **Build order**: Always build the source generator, then Comet for Mac
  Catalyst, then the test project. The direct DLL reference means stale builds
  will silently use outdated code. See the
  [Contributing Guide](contributing.md) for the full build sequence.


## See Also

- [Reactive State Guide](reactive-state-guide.md) -- the state primitives
  tested in this suite: Signal, Computed, Effect, and ReactiveScope.
- [Contributing Guide](contributing.md) -- test requirements, build order, and
  PR process for submitting changes.
- [Architecture Overview](architecture.md) -- test infrastructure details and
  the framework internals that tests validate.
