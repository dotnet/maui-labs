# Troubleshooting and FAQ

This guide documents common issues encountered when building with Comet, their
root causes, and how to fix them. These are based on real bugs found and fixed
during development.


## Reactive State Issues

### "My Text doesn't update when state changes"

**Symptom**: You set a `Signal` or `Reactive` value, but the UI does not
reflect the change.

**Root cause**: The text was bound using the inline `.Value` property instead of
a lambda expression. Without a lambda, Comet cannot track the dependency.

```csharp
// WRONG: reads the value once at build time -- no tracking
new Text($"Count: {count.Value}")

// CORRECT: lambda enables automatic dependency tracking
new Text(() => $"Count: {count.Value}")
```

When you pass a `Func<string>` (lambda), the framework evaluates it inside a
`ReactiveScope` that records which signals were read. When those signals change,
the framework knows to re-evaluate the lambda and update the text. A plain
string has no such tracking.

**Fix**: Always use `() => ...` lambda syntax for any text or property that
should update reactively. For a complete guide to reactive binding patterns,
see the [Reactive State Guide](reactive-state-guide.md).


### "Slider resets to its initial position while dragging"

**Symptom**: Dragging a `Slider` causes it to snap back to its starting
position on each drag tick.

**Root cause**: When the slider handler reports a new value, the framework
evaluates the `Binding<double>` function. If that function reads a `Signal`,
the read is tracked inside a `ReactiveScope`. The tracked dependency then
triggers a body rebuild, which creates a new slider with the original default
value, resetting the drag.

**Fix**: This was resolved by suppressing `ReactiveScope` tracking during
`Binding` evaluation (commit `a851527f`). The `PropertySubscription<T>`
system handles this correctly by isolating property-level reads from the
body-level scope. If you encounter similar behavior with a custom control,
ensure your binding evaluation does not leak reads into the parent scope.


### "Entry loses focus on each keystroke"

**Symptom**: Typing in a `TextField` causes it to lose focus after every
character.

**Root cause**: Same mechanism as the slider drag issue. Each keystroke updates
the bound state, which triggers a body rebuild, which creates a new `TextField`
instance, which loses the focus of the previous one.

**Fix**: Use `PropertySubscription<T>` (the default for generated controls)
rather than body-level `Binding<T>`. The property subscription system uses
fine-grained updates that modify the existing handler's property without
rebuilding the entire view tree.


### "StackOverflowException during state updates"

**Symptom**: Changing a state value causes a `StackOverflowException` with a
deep call stack alternating between `Reload` and state change handlers.

**Root cause**: The `ReactiveScheduler` was re-entering its flush loop. A state
change triggers a view reload, which transfers environment properties (gestures,
handlers), which marks the view dirty again, which triggers another reload.

**Fix**: The `ReactiveScheduler` now has two guards against this:

1. **SuppressNotifications flag**: Set to `true` during `UpdateFromOldView()`
   to prevent environment property transfers from marking views dirty.

   ```csharp
   ReactiveScheduler.SuppressNotifications = true;
   try { /* transfer state */ }
   finally { ReactiveScheduler.SuppressNotifications = false; }
   ```

2. **MaxFlushDepth limit**: The scheduler tracks recursion depth and breaks
   after 100 iterations (throws in DEBUG, swallows in Release).

This was fixed in commit `0b76fc95`.


### "View doesn't rebuild when state changes"

**Symptom**: You mutate a property on a model object, but the view does not
update.

**Root cause**: The model object does not implement `IReactiveSource` or use
`Signal<T>` fields. The reactive system can only track reads from objects that
participate in `ReactiveScope` tracking.

**Fix**: Use `Signal<T>` for any value that should trigger UI updates:

```csharp
// WRONG: plain property -- no reactive tracking
public class Model
{
	public string Name { get; set; }
}

// CORRECT: Signal-backed property -- tracked automatically
public class Model
{
	public readonly Signal<string> Name = new("");
}
```

Alternatively, use `Reactive<T>` which provides implicit conversions:

```csharp
public class Model
{
	public readonly Reactive<string> Name = "";
}
```

### "Background thread writes don't update the UI"

**Symptom**: Setting a `Signal` value from `Task.Run` or an HTTP callback does
not trigger a visual update.

**Root cause (fixed)**: The `ReactiveScheduler`'s dispatcher was double-queuing
flush entries when the signal write itself was dispatched from a background
thread.

**Fix**: This was fixed in commit `cee1e2b4`. Signal writes from any thread
now correctly schedule a single flush on the main thread. If you encounter this,
ensure you are on a recent build.


## Hot Reload Issues

### "Hot reload doesn't transfer my state"

**Symptom**: After a hot reload, the view resets to its initial state.

**Root cause**: Only `Signal<T>` fields participate in hot reload state
transfer. `Reactive<T>` fields are not transferred.

**Fix**: Use `Signal<T>` for any state that must survive hot reload:

```csharp
class MyView : View
{
	// Transferred during hot reload
	public readonly Signal<string> title = new("Hello");
	public readonly Signal<int> count = new(0);

	// NOT transferred -- resets to default on reload
	Reactive<bool> isExpanded = new(false);

	[Body]
	View body() => new VStack
	{
		new Text(() => title.Value),
		new Text(() => $"Count: {count.Value}"),
	};
}
```

The hot reload system uses `TransferState()` which copies `Signal<T>` instances
from the old view to the new view. `Reactive<T>` is a simpler wrapper that does
not participate in this mechanism.

### "Hot reload replaces the view but constructor parameters are lost"

**Symptom**: A view that takes constructor arguments renders incorrectly after
hot reload.

**Fix**: Register constructor parameters with `MauiHotReloadHelper.Register()`:

```csharp
class DetailView : View
{
	readonly string itemId;

	public DetailView(string itemId)
	{
		this.itemId = itemId;
		MauiHotReloadHelper.Register(this, itemId);
	}

	[Body]
	View body() => new Text(() => $"Item: {itemId}");
}
```


## Build Issues

### "Tests won't compile: missing assembly reference"

**Symptom**: Building `Comet.Tests.csproj` fails with:

```
error CS0006: Metadata file 'src/Comet/bin/Release/net10.0-maccatalyst/Comet.dll' could not be found
```

**Root cause**: The test project uses a direct DLL reference to the Mac Catalyst
build of Comet, not a project reference.

**Fix**: Build in the correct order:

```bash
dotnet build src/Comet.SourceGenerator/Comet.SourceGenerator.csproj -c Release
dotnet build src/Comet/Comet.csproj -c Release
dotnet build tests/Comet.Tests/Comet.Tests.csproj -c Release
```

The source generator must build first because Comet depends on it. Comet must
build for Mac Catalyst (the default multi-target includes it) before the test
project can resolve the DLL reference.

### "Source generator errors after modifying CometGenerate attributes"

**Symptom**: After changing `[assembly: CometGenerate(...)]` attributes in
`Controls/ControlsGenerator.cs`, the build produces unexpected errors in
generated code.

**Fix**: Clean the build output and rebuild from scratch:

```bash
dotnet clean src/Comet.SourceGenerator/Comet.SourceGenerator.csproj
dotnet clean src/Comet/Comet.csproj
dotnet build src/Comet.SourceGenerator/Comet.SourceGenerator.csproj -c Release
dotnet build src/Comet/Comet.csproj -c Release
```

Source generators cache their output. A clean build forces full regeneration.

### "Build fails on net10.0-windows but I'm on macOS"

**Symptom**: Building Comet on macOS produces errors for the Windows target
framework.

**Fix**: Comet is multi-targeted. On macOS, the Windows targets are not
buildable. Use a specific target framework:

```bash
dotnet build src/Comet/Comet.csproj -c Release -f net10.0-maccatalyst
```

Or build for iOS:

```bash
dotnet build src/Comet/Comet.csproj -c Release -f net10.0-ios
```


## Layout and Rendering Issues

### "CALayerInvalidGeometry crash on iOS"

**Symptom**: The app crashes on iOS simulator with a `CALayerInvalidGeometry`
exception during layout.

**Root cause**: Grid-based layout helpers were producing invalid geometry when
eagerly laid out before the view hierarchy was fully established.

**Fix**: Replace complex grid layouts with simpler `HStack`/`VStack` containers
for content that is rendered during initial layout. This was discovered during
iOS simulator validation and resolved by using stack-based layouts.

### "SetEnvironment causes StackOverflow with keyed views"

**Symptom**: Views using `.Key()` for reconciliation cause a stack overflow
when environment properties are set.

**Root cause**: This is a known framework-level issue where `SetEnvironment`
recurses infinitely through keyed view children.

**Status**: This is tracked as a known blocker. Tests affected by this issue
are skipped with an explanatory message.


## Debugging Tips

### Tracing Reactive Subscriptions

Use `ReactiveDiagnostics` to inspect the dependency graph at runtime:

```csharp
// Check what a Computed depends on
var signal = new Signal<int>(1);
var computed = new Computed<int>(() => signal.Value * 2);
_ = computed.Value; // Force evaluation to establish dependencies
```

### Checking Handler Mappings

When a property change does not reach the native control, verify that the
handler mapper is registered:

1. Check that `UseCometHandlers()` is called in `MauiProgram.cs`.
2. Verify the control type has a handler mapping in `AppHostBuilderExtensions`.
3. In tests, call `InitializeHandlers(view)` to wire up handlers and then
   inspect the handler's `ChangedProperties` list.

### Using ReactiveScheduler.FlushSync

In debugging scenarios, call `ReactiveScheduler.FlushSync()` to force immediate
processing of all pending state updates. This is useful when stepping through
code and wanting to see the effects of a state change without waiting for the
dispatcher.

### Verifying View Rebuilds

Add a rebuild counter to your view to confirm that state changes trigger body
re-evaluation:

```csharp
class DebugView : View
{
	readonly Signal<int> count = new(0);
	int rebuildCount = 0;

	[Body]
	View body()
	{
		rebuildCount++;
		System.Diagnostics.Debug.WriteLine($"Body rebuild #{rebuildCount}");
		return new Text(() => $"Count: {count.Value}");
	}
}
```

### Verifying Fine-Grained Updates

For `PropertySubscription`-bound properties (sliders, text fields), the handler
receives property-level updates without a full body rebuild. Verify this by
checking that the body rebuild count stays constant while the handler's
`ChangedProperties` list grows. For more on fine-grained vs body-level updates,
see [Performance Optimization](performance.md).


## FAQ

**Q: Do I need to call `Dispose()` on views?**

A: Comet manages view lifecycle automatically during reconciliation. You
generally do not need to dispose views manually. However, if you hold a
reference to a view outside the normal tree (for example, in a cache), dispose
it when done to release reactive subscriptions.

**Q: Can I use `async`/`await` in a `[Body]` method?**

A: No. The `[Body]` method must return synchronously. Load async data in a
lifecycle callback or an `Effect`, then store the result in a `Signal`:

```csharp
class DataView : View
{
	readonly Signal<string> data = new("Loading...");

	public DataView()
	{
		Task.Run(async () =>
		{
			var result = await FetchDataAsync();
			data.Value = result;
		});
	}

	[Body]
	View body() => new Text(() => data.Value);
}
```

**Q: Why are some tests skipped?**

A: Tests are skipped for two reasons: (1) they depend on framework features
that are not yet implemented (marked with `Skip = "reason"`), or (2) they
trigger known framework-level issues like the `SetEnvironment` stack overflow.
The skip messages document the specific blocker.

**Q: How do I run only the reactive tests?**

A:
```bash
dotnet test tests/Comet.Tests/Comet.Tests.csproj --no-build -c Release \
	--filter "FullyQualifiedName~ReactiveTests"
```

For the full testing guide, see [Testing Guide](testing.md).


## See Also

- [Reactive State Guide](reactive-state-guide.md) -- detailed coverage of
  every state primitive, resolving the "why doesn't my UI update?" questions.
- [Performance Optimization](performance.md) -- diagnosing performance-related
  issues like excessive rebuilds and flush depth limits.
- [Testing Guide](testing.md) -- reproducing and isolating issues using the
  test infrastructure and FlushSync.

**Q: What is the difference between `Signal<T>` and `Reactive<T>`?**

A: `Signal<T>` is sealed, thread-safe (uses a write lock), participates in
hot reload state transfer, and is the recommended primitive for new code.
`Reactive<T>` is non-sealed, supports implicit conversions, and exists for
backward compatibility. Use `Signal<T>` unless you need subclassing or implicit
conversion from `T`.
