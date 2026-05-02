# Architecture Overview

This document is for contributors working on the Comet framework internals. It
covers the key layers, the source generator pipeline, the reactive system, the
diff algorithm, hot reload, and the build system.


## Layer Overview

A Comet application flows through these layers:

```
View (C# code)
  |
  v
Body method --> returns a view tree (Func<View>)
  |
  v
PropertySubscription / State<T> --> reactive dependency tracking
  |
  v
Handler (MAUI handler) --> creates and manages the native platform control
  |
  v
Native View (UIView, Android.View, WinUI element)
```

### View

`View` is the base class for all Comet controls. It is defined in
`Controls/View.cs` and implements `IView`, `IHotReloadableView`, and numerous
other MAUI interfaces. Key members:

- `Body` -- a `Func<View>` that returns the view tree. Set via the `[Body]`
  attribute on a method, which the source generator wires up.
- `ViewHandler` -- the MAUI handler instance managing the native control.
- `Parent` / `Navigation` -- tree navigation references.
- `GetView()` -- resolves the Body to a concrete view for rendering.
- `SetEnvironment()` / `GetEnvironment<T>()` -- the environment dictionary for
  property propagation down the tree.

### Body Evaluation

When `GetView()` is called (by `CometView` on each platform), it invokes the
`Body` function. During this evaluation, the reactive tracking system records
which `State<T>` or `BindingObject` properties were read. When those properties
later change, the view's Body is re-evaluated and the result is diffed against
the previous tree.


## Source Generator Pipeline

The source generator is in `src/Comet.SourceGenerator/`. It targets
`netstandard2.0` (required for Roslyn analyzers) and produces two kinds of
generated code.

### CometViewSourceGenerator

Reads `[assembly: CometGenerate(...)]` attributes from `ControlsGenerator.cs`
and generates View subclasses. For each attribute, the generator produces:

1. **View class** -- inherits from `View` and implements the specified MAUI
   interface (e.g., `ITextButton`). Constructor parameters become typed
   `Binding<T>` properties. Interface members are implemented by reading from
   environment keys or Binding values.

2. **Extension methods** -- fluent setters for each property (e.g.,
   `.Text("Hello")`, `.FontSize(16)`).

3. **Factory methods** -- static creation helpers.

4. **Style builders** -- for themed control configuration.

Example: the attribute
```csharp
[assembly: CometGenerate(typeof(ITextButton),
    nameof(ITextButton.Text), nameof(IButton.Clicked),
    ClassName = "Button", Namespace = "Comet")]
```
generates a `Button` class with a `Text` binding and `Clicked` action, plus
`.Text()`, `.OnClicked()` extension methods.

### AutoNotifyGenerator

When a method is decorated with `[Body]`, the generator wires the return value
to the view's `Body` property:

```csharp
// Developer writes:
public class MyPage : View
{
	[Body]
	View Body() => new VStack { new Text("Hello") };
}

// Generator produces the Body property wiring so the framework
// can call Body() to obtain the view tree.
```

### CometControlStateAttribute

Design Decision D6 introduced per-control state metadata:

```csharp
[assembly: CometControlState(typeof(ITextButton),
    ControlName = "Button",
    States = new[] { "IsPressed", "IsHovered", "IsFocused" },
    ConfigProperties = new[] { "Label:string:Text" })]
```

The generator reads these to emit:
- `ButtonConfiguration` structs with state-aware property resolution
- Scoped `.ButtonStyle()` extension methods
- `ResolveCurrentStyle()` methods


## Reactive System

Comet uses two reactive tracking approaches that work together.

### Classic Tracking: State&lt;T&gt; and BindingObject

**`BindingObject`** (`BindingObject.cs`) is the base class for observable
objects. It implements `INotifyPropertyRead`, which extends
`INotifyPropertyChanged` with a `PropertyRead` event. When a property getter
executes, it fires `PropertyRead`. When a setter executes, it fires
`PropertyChanged`.

**`State<T>`** extends `BindingObject` and wraps a single value:

```csharp
readonly State<int> count = 0; // implicit conversion from T to State<T>

// Reading count.Value fires PropertyRead
// Writing count.Value fires PropertyChanged and triggers re-render
```

**Binding&lt;T&gt;** wraps either a value or a `Func<T>`. When the framework
evaluates a `Func<T>`, it records which properties were read. This forms the
automatic dependency tracking:

```csharp
new Text(() => $"Count: {count.Value}")
// The framework knows this Text depends on count.Value
```

### Signal-Based Tracking

The newer signal-based system lives in `Reactive/`:

- **`Signal<T>`** -- a reactive primitive similar to `State<T>` but integrated
  with `ReactiveScope`.
- **`ReactiveScope`** -- tracks which signals are read during a scope's
  execution. When any tracked signal changes, the scope re-runs.
- **`ReactiveScheduler`** -- batches signal notifications to coalesce rapid
  changes into a single UI update.

The two systems coexist. Classic tracking handles `State<T>` and
`BindingObject`; signal tracking handles `Signal<T>`. Both trigger view
rebuilds through the same pipeline. For practical usage patterns, see the
[Reactive State Guide](reactive-state-guide.md).

### PropertySubscription&lt;T&gt;

`PropertySubscription<T>` wraps a value or function and integrates with the
view's property-change notification system. Views use it for constructor-bound
properties:

```csharp
PropertySubscription<string> _text;
public PropertySubscription<string> Text
{
	get => _text;
	private set => this.SetPropertySubscription(ref _text, value);
}
```

`SetPropertySubscription` registers the subscription so that when the upstream
value changes, `ViewPropertyChanged` is called, which triggers handler updates.


## Environment System

Properties set via fluent extension methods are stored in an environment
dictionary on each view. Environment values propagate down the view tree --
a child view inherits its parent's environment unless it overrides a key.

```csharp
new VStack
{
	new Text("Inherited blue"),
	new Text("Also blue"),
}.Color(Colors.Blue)  // sets EnvironmentKeys.Colors.Color on the VStack
```

Environment keys are defined in `EnvironmentData.cs` under namespaces like
`EnvironmentKeys.Colors`, `EnvironmentKeys.Layout`, `EnvironmentKeys.View`,
`EnvironmentKeys.Entry`, etc.

The environment is read via `view.GetEnvironment<T>(key)`, which walks up the
parent chain until a value is found.


## Diff Algorithm

Located in `Helpers/DatabindingExtensions.cs`, the `Diff()` method compares old
and new view trees after a Body re-evaluation. This is the core of Comet's
efficient update strategy.

### How Diff Works

1. **Type comparison** -- checks if old and new views are the same type. This
   includes hot-reload replacement types via
   `MauiHotReloadHelper.IsReplacedView()`.

2. **Built view resolution** -- if either view has a built (rendered) child
   view, the diff recurses into the built views.

3. **Container diffing** -- for container views (VStack, HStack, etc.), the
   algorithm compares child lists:
   - Detects added items (new children not in the old list)
   - Detects removed items (old children not in the new list)
   - Detects shifted items (same child at a different index)
   - Reuses handlers when view types match

4. **Handler reuse** -- when types match, the existing platform handler is
   transferred to the new view rather than creating a new native control. This
   is the key performance optimization.

5. **Main thread dispatch** -- all handler updates are dispatched to the main
   thread.

### State Transfer

During diff, `TransferState()` copies changed properties from the old view to
the new view:
- Reads `GetState().ChangedProperties` for modified properties
- Transfers environment data via `PopulateFromEnvironment()`
- Carries over handler references and navigation state


## Hot Reload Pipeline

Comet has deep hot reload integration. The pipeline:

1. The IDE detects a code change and applies it to the running process.
2. `MauiHotReloadHelper.RegisterReplacedView(className, newType)` records the
   type replacement.
3. `MauiHotReloadHelper.TriggerReload()` fires, calling
   `IHotReloadableView.Reload()` on all active views.
4. `View.Reload(isHotReload: true)` calls `ResetView()`:
   - Rebuilds the Body (re-evaluates the Func<View>)
   - Diffs the old and new view trees
   - Transfers state from old views to new views
   - Reuses platform handlers where possible
5. Platform `CometView` classes implement `IReloadHandler`. Their `Reload()`
   method calls `SetView(CurrentView, true)` to force a native view tree
   rebuild.

### Hot Reload Testing

Tests in `tests/Comet.Tests/` validate the hot reload pipeline:
- `HotReloadTestsNoParameters.cs` -- basic view type replacement
- `HotReloadWithParameters.cs` -- replacement with constructor parameters
- `ReloadTransfersStateTest.cs` -- state preservation across reloads

Tests require `MauiHotReloadHelper.IsEnabled = true`.


## Build System

### Project Structure

```
src/Comet.SourceGenerator/    netstandard2.0, Roslyn analyzer
src/Comet/                    multi-targeted MAUI library
tests/Comet.Tests/            xUnit tests
```

### Build Order

The source generator must build first because `Comet.csproj` references it as
an analyzer:

```bash
dotnet build src/Comet.SourceGenerator/Comet.SourceGenerator.csproj -c Release
dotnet build src/Comet/Comet.csproj -c Release
```

### Multi-Targeting

`Comet.csproj` targets `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`,
and `net10.0-windows`. Platform-specific code is included/excluded by
`Directory.Build.targets` using file naming conventions:

| File Pattern | Included For |
|-------------|--------------|
| `*.iOS.cs` or `iOS/` directory | iOS and Mac Catalyst |
| `*.Mac.cs` or `MacCatalyst/` directory | Mac Catalyst only |
| `*.Android.cs` or `Android/` directory | Android |
| `*.Windows.cs` or `Windows/` directory | Windows |
| `*.Standard.cs` or `Standard/` directory | netstandard / net6.0 / net7.0 |

The `Directory.Build.targets` file at the repository root uses MSBuild
conditions to remove files that do not match the current target framework. For
example, when building for Android, all `*.iOS.cs` files are removed from
compilation and added as `<None>` items instead.

### Test Project

The test project (`tests/Comet.Tests/`) references the Mac Catalyst DLL
directly (not a project reference):

```
src/Comet/bin/$(Configuration)/net10.0-maccatalyst/Comet.dll
```

This means Comet must be built for Mac Catalyst before tests can compile.
Test parallelization is disabled. Tests inherit from `TestBase`, which calls
`UI.Init()` to initialize the framework.

```bash
dotnet build tests/Comet.Tests/Comet.Tests.csproj -c Release
dotnet test tests/Comet.Tests/Comet.Tests.csproj --no-build -c Release
```

### NuGet Package

The main package is `Microsoft.Maui.Comet`. Packing uses `dotnet pack`:

```bash
dotnet pack src/Comet/Comet.csproj -c Release -p:PackageVersion=$VERSION -o ./artifacts
```

The package includes:
- Multi-targeted framework assemblies
- Source generator DLL under `analyzers/cs/`
- `Stubble.Core.dll` (dependency of the source generator)
- `Directory.Build.targets` as a build target


## Key Source Files

| File | Purpose |
|------|---------|
| `Controls/View.cs` | Base class for all views |
| `Controls/ControlsGenerator.cs` | CometGenerate attributes for source gen |
| `Controls/ContainerView.cs` | Base for layout containers |
| `Controls/AbstractLayout.cs` | Base for measured/arranged layouts |
| `Maui/CometApp.cs` | Application entry point |
| `AppHostBuilderExtensions.cs` | Handler registration and mapper setup |
| `Helpers/DatabindingExtensions.cs` | Diff algorithm |
| `Helpers/LayoutExtensions.cs` | Fluent layout API |
| `BindingObject.cs` | Observable base class |
| `State.cs` | Reactive state wrapper |
| `Reactive/Signal.cs` | Signal-based reactive primitive |
| `Reactive/ReactiveScope.cs` | Signal dependency tracker |
| `Reactive/ReactiveScheduler.cs` | Batched signal notification |
| `EnvironmentData.cs` | Environment key definitions |
| `Directory.Build.targets` | Platform file inclusion rules |

### Source Generator Files

| File | Purpose |
|------|---------|
| `CometViewSourceGenerator.cs` | Main generator entry point |
| `AutoNotifyGenerator.cs` | [Body] attribute processing |
| `CometGenerateAttribute.cs` | Attribute definition |
| `CometControlStateAttribute.cs` | Per-control state metadata |


## Code Conventions

- **Tabs for indentation** (configured in `.editorconfig`)
- **Allman brace style** for all constructs
- **`var` preferred** when type is apparent
- **No `this.` qualifier** on members
- **Explicit using statements** (implicit usings disabled)
- Layout containers use C# collection initializer syntax for children
- State fields: `readonly State<T> fieldName = defaultValue;`
- Complex state: `[State]` attribute on `BindingObject`-derived fields


## See Also

- [Handler Architecture](handlers.md) -- detailed handler layer documentation,
  property mappers, and customization patterns.
- [Reactive State Guide](reactive-state-guide.md) -- the reactive system from
  an application developer's perspective.
- [Contributing Guide](contributing.md) -- how to set up a development
  environment and submit changes to the framework.
