# Contributing Guide

This document covers everything needed to set up a development environment,
build the framework, run tests, and submit changes to the Comet project.


## Prerequisites

- **.NET 11 SDK (preview)**
- **MAUI workload** -- install with `dotnet workload install maui`
- **macOS** is required for running the test suite (tests reference the
  `net11.0-maccatalyst` build output)
- A code editor with C# support (Visual Studio, VS Code with C# Dev Kit, or
  Rider)


## Project Structure

```
Comet/
  src/
    Comet/                     # Main framework library
    Comet.SourceGenerator/     # Roslyn source generator
  tests/
    Comet.Tests/               # xUnit test project
  sample/
    Comet.Sample/              # Reference sample (50+ demos)
    CometMauiApp/              # Minimal starter template
    CometFeatureShowcase/      # Educational showcase
    CometAllTheLists/          # List/collection demos
    CometTaskApp/              # TabView navigation demo
    CometProjectManager/       # Shell + themes demo
    CometBaristaNotes/         # Syncfusion gauges demo
    CometWeather/              # Reactive data display
    CometStressTest/           # Performance stress tests
    MauiReference/             # Pure MAUI XAML comparison
  templates/
    single-project/            # dotnet new template
  docs/                        # Documentation
  tools/                       # Build tooling
  build/                       # CI configuration
```

### Core Projects

**`src/Comet/Comet.csproj`** -- The framework library. A single
multi-targeted .NET MAUI project:

- Target frameworks: `net11.0-android`, `net11.0-ios`,
  `net11.0-maccatalyst`, `net11.0-windows10.0.19041.0`
- MAUI workload: `UseMaui = true`, `SingleProject = true`
- Implicit usings: disabled (all `using` statements must be explicit)
- MAUI dependencies: `Microsoft.Maui.Controls` v11.0.0-preview.3.26207.5
  (centralised in `eng/Versions.props` as `MicrosoftMauiControlsVersion`)
- Platform minimums (enforced via `src/Comet/sample/Directory.Build.targets`):
  iOS 17.0, macCatalyst 17.0, Android 23.0, Windows 10.0.17763.0

**`src/Comet.SourceGenerator/Comet.SourceGenerator.csproj`** -- A Roslyn
source generator that runs at compile time inside the C# compiler. Targets
`netstandard2.0` (required for all Roslyn generators). Uses `Stubble.Core`
v1.9.3 as its Mustache template engine. Language version set to `preview`.

**`tests/Comet.Tests/Comet.Tests.csproj`** -- xUnit test project targeting
`net11.0`. References the maccatalyst DLL directly:

```xml
<Reference Include="Comet">
    <HintPath>..\..\src\Comet\bin\$(Configuration)\net11.0-maccatalyst\Comet.dll</HintPath>
</Reference>
```

This means Comet must be built for maccatalyst before the test project will
compile. The `CA1416` warning (platform compatibility) is suppressed.


## Build Order

The source generator must build before the main library because Comet's
controls are generated at compile time. The test project depends on the
maccatalyst build output.

```bash
# Step 1: Build the source generator
dotnet build src/Comet.SourceGenerator/Comet.SourceGenerator.csproj -c Release

# Step 2: Build the main library (generates controls, builds all platforms)
dotnet build src/Comet/Comet.csproj -c Release

# Step 3: Build the test project
dotnet build tests/Comet.Tests/Comet.Tests.csproj -c Release

# Step 4: Run tests
dotnet test tests/Comet.Tests/Comet.Tests.csproj --no-build -c Release
```

To run a single test:

```bash
dotnet test tests/Comet.Tests/Comet.Tests.csproj --no-build -c Release \
    --filter "FullyQualifiedName~ClassName.MethodName"
```

To build and run a sample:

```bash
# Build (must build Comet first)
dotnet build sample/Comet.Sample/Comet.Sample.csproj -c Release

# Run on Mac Catalyst
dotnet build sample/CometMauiApp/CometMauiApp.csproj -t:Run -f net11.0-maccatalyst
```


## Code Style

The project uses tabs for indentation and Allman brace style. All rules are
defined in `.editorconfig` at the repository root.

### Indentation

- **Tabs** (4-space width), never spaces
- Switch cases and labels are indented

### Braces

Allman style -- opening brace on a new line for all constructs:

```csharp
public class MyView : View
{
	[Body]
	View body()
	{
		if (condition)
		{
			return new Text("Yes");
		}
		else
		{
			return new Text("No");
		}
	}
}
```

`catch`, `else`, and `finally` each appear on their own line.

### Type Preferences

- Prefer `var` when the type is apparent or for built-in types
- Use language keywords (`int`, `string`, `bool`) not framework names
  (`Int32`, `String`, `Boolean`)
- No `this.` qualifier on fields, properties, or methods
- Expression-bodied members (`=>`) are preferred for simple properties and
  methods

### Usings

Implicit usings are **disabled** project-wide. Every file must include its
own `using` statements. System namespaces are sorted first.

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Comet;
using Comet.Styles;
```


## How the Source Generator Works

The source generator is the foundation of Comet's control system. It reads
`[assembly: CometGenerate(...)]` attributes and produces View subclasses with
full constructor overloads, bindable properties, extension methods, factory
methods, and style builders.

### CometGenerate Attribute

Controls are defined in `src/Comet/Controls/ControlsGenerator.cs` as
assembly-level attributes:

```csharp
[assembly: CometGenerate(typeof(ITextButton), nameof(ITextButton.Text),
    nameof(ITextButton.Clicked), ClassName = "Button")]

[assembly: CometGenerate(typeof(IEntry), nameof(IEntry.Text),
    nameof(IEntry.Placeholder), nameof(IEntry.Completed),
    ClassName = "TextField")]

[assembly: CometGenerate(typeof(ISlider), nameof(ISlider.Value),
    nameof(ISlider.Minimum), nameof(ISlider.Maximum),
    ClassName = "Slider")]
```

Attribute parameters:

| Parameter | Description |
|-----------|-------------|
| First argument | MAUI interface type to wrap |
| Subsequent string arguments | Key properties (become constructor parameters) |
| `ClassName` | Name of the generated class (default: strip `I` prefix) |
| `Namespace` | Generated namespace (default: `Comet`) |
| `BaseClass` | Base class (default: `View`) |
| `DefaultValues` | Default property values (e.g., `"IsRunning=true"`) |
| `Skip` | Properties to skip generating (with optional environment key) |

### Generated Output

For each `[CometGenerate]` attribute, the generator produces four files:

1. **`{Control}.g.cs`** -- The View subclass with:
   - Multiple constructor overloads (static value, `Func<T>`, `Signal<T>`,
     `Computed<T>`)
   - `PropertySubscription<T>` fields for each key property
   - MAUI interface implementation with environment-backed accessors
   - Handler property mapping

2. **`{Control}Extension.g.cs`** -- Fluent extension methods for all
   non-key, non-action properties (e.g., `.FontSize()`, `.Color()`,
   `.Background()`)

3. **`{Control}Factory.g.cs`** -- Static factory methods on
   `CometControls` (e.g., `CometControls.TextField(signal, "hint")`)

4. **`{Control}StyleBuilder.g.cs`** -- A `{Control}StyleBuilder` class in
   the `Comet.Styles` namespace wrapping `ControlStyle<T>` with typed
   methods

### Template Engine

The generator uses Stubble.Core (Mustache templates) to render source code.
Templates are embedded in the generator assembly. The key templates are:

- Class template -- renders the main view class
- Extension template -- renders fluent extension methods
- Factory template -- renders CometControls static methods
- Style builder template -- renders per-control style builders

### Property Handling

The generator categorizes each interface property:

- **Key properties** -- become constructor parameters with
  `PropertySubscription<T>` fields
- **Action/delegate properties** -- get `On`-prefixed extension methods
  (e.g., `OnPressed()`)
- **Regular properties** -- get standard extension methods storing values
  in the environment dictionary
- **Skipped properties** -- excluded via the `Skip` attribute parameter

Properties that are delegates (`Action`, `Func<T>`, `EventHandler`) use
`Binding<T>` instead of `PropertySubscription<T>`.


## Adding a New Control

### Generated Control (Simple Interface)

If the MAUI interface is straightforward (properties + simple events), add
a `[CometGenerate]` attribute:

1. Open `src/Comet/Controls/ControlsGenerator.cs`

2. Add the attribute:

```csharp
[assembly: CometGenerate(typeof(IMyControl),
    nameof(IMyControl.Value), nameof(IMyControl.Minimum),
    ClassName = "MyControl")]
```

3. Build the source generator then the main project. The four generated
   files appear in the build output.

4. If the control needs state-aware styling, add a
   `[CometControlState]` attribute:

```csharp
[assembly: CometControlState(typeof(IMyControl),
    ControlName = "MyControl",
    States = new[] { "IsFocused", "IsActive" },
    ConfigProperties = new[] { "Value:double:Value" })]
```

5. Add tests in `tests/Comet.Tests/`.

### Handwritten Control (Complex Logic)

For controls requiring collection management, custom layout, or platform-
specific logic:

1. Create `src/Comet/Controls/MyControl.cs`

2. Extend `View` and implement the MAUI interface:

```csharp
namespace Comet
{
	public class MyControl : View, IMyControl
	{
		// Constructor with PropertySubscription<T> fields
		// Interface implementation via environment reads/writes
		// Custom logic that the generator cannot produce
	}
}
```

3. If the control needs a custom handler, create platform-specific files:
   - `src/Comet/Handlers/MyControlHandler.cs` (shared)
   - `src/Comet/Handlers/MyControlHandler.iOS.cs`
   - `src/Comet/Handlers/MyControlHandler.Android.cs`
   - `src/Comet/Handlers/MyControlHandler.Windows.cs`

4. Register the handler in `AppHostBuilderExtensions.cs`:

```csharp
handlers.AddHandler<MyControl, MyControlHandler>();
```


## Testing

### Test Infrastructure

All tests inherit from `TestBase`, which initializes the Comet test
environment. For the complete testing guide, see [Testing Guide](testing.md).

```csharp
public class MyTests : TestBase
{
	[Fact]
	public void MyControl_SetsValue()
	{
		var control = new MyControl(42d);
		Assert.Equal(42d, control.Value?.CurrentValue);
	}
}
```

`TestBase` calls `UI.Init()` in its constructor. Test parallelization is
disabled at the assembly level:

```csharp
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

### Testing with Handlers

To test handler integration, use `InitializeHandlers()`:

```csharp
[Fact]
public void MyControl_HandlerReceivesValue()
{
	var control = new MyControl(42d);
	InitializeHandlers(control, width: 400, height: 100);

	// Handler is now attached and layout is applied
	Assert.NotNull(control.ViewHandler);
}
```

`InitializeHandlers` overloads:

- `InitializeHandlers(View view)` -- attaches handlers recursively
- `InitializeHandlers(View view, float width, float height)` -- attaches
  handlers and applies layout with the given dimensions

### Testing Hot Reload

Enable hot reload in tests:

```csharp
[Fact]
public void HotReload_PreservesState()
{
	MauiHotReloadHelper.IsEnabled = true;

	var view = new MyView();
	InitializeHandlers(view);

	// Simulate hot reload replacement
	MauiHotReloadHelper.RegisterReplacedView(
		typeof(MyView).FullName, typeof(MyViewV2));
	MauiHotReloadHelper.TriggerReload();

	// Verify state was transferred
	Assert.NotNull(view.ViewHandler);
}
```

### Resetting State Between Tests

Call `ResetComet()` to clean up global state:

```csharp
public override void Dispose()
{
	ResetComet();
	base.Dispose();
}
```

`ResetComet()` clears the global environment, reinitializes UI, and resets
hot reload helpers.


### Test Organization

Tests are organized by feature in subdirectories but use the flat
`Comet.Tests` namespace:

```
tests/Comet.Tests/
  TestBase.cs
  ComponentTests/
  NavigationApiTests/
  ReconciliationTests/
  ReactiveTests/
  ...
```


## Platform-Specific Code

Platform-specific files are included/excluded by `Directory.Build.targets`
based on the target framework:

| Suffix/Folder | Platform |
|---------------|----------|
| `*.iOS.cs` or `iOS/` | iOS |
| `*.Android.cs` or `Android/` | Android |
| `*.Windows.cs` or `Windows/` | Windows |
| `*.Mac.cs` or `Mac/` / `MacCatalyst/` | Mac Catalyst |
| `*.Standard.cs` or `Standard/` | Cross-platform fallback |

When adding platform-specific code, name the file accordingly and it will
be automatically included only for the correct target.


## NuGet Packaging

The main package is `Microsoft.Maui.Comet`. CI uses `dotnet pack` with a computed
version:

```bash
dotnet pack src/Comet/Comet.csproj -c Release \
    -p:PackageVersion=$VERSION -o ./artifacts
```

Version format: `0.4.$MINOR` where `$MINOR = $BASE + $GITHUB_RUN_NUMBER`.

The package includes:

- The compiled library for all four target frameworks
- The source generator DLL (`Comet.SourceGenerator.dll`) under
  `analyzers/cs/`
- The Stubble.Core dependency (`Stubble.Core.dll`) under `analyzers/cs/`
- `Directory.Build.targets` as a build target for file inclusion rules

The `.nuspec` files at the repository root (`Comet.nuspec`,
`Comet.Skia.nuspec`, `Comet.Reload.nuspec`) are legacy and not used by CI.


## Pull Request Process

1. Create a branch from `main` with a descriptive name.
   If working from an issue, use `squad/{issue-number}-{slug}`.

2. Make your changes following the code style rules above.

3. Build and test:

```bash
dotnet build src/Comet.SourceGenerator/Comet.SourceGenerator.csproj -c Release && \
dotnet build src/Comet/Comet.csproj -c Release && \
dotnet build tests/Comet.Tests/Comet.Tests.csproj -c Release && \
dotnet test tests/Comet.Tests/Comet.Tests.csproj --no-build -c Release
```

4. Verify no regressions. There are a small number of pre-existing test
   failures -- your changes should not add new failures.

5. Open a pull request against `main`. Reference any related issues.

6. Ensure all CI checks pass.


## Architecture Overview

See the [Architecture Overview](architecture.md) for a full description of
the reactive pipeline, handler system, environment propagation, and diff
algorithm.


## See Also

- [Architecture Overview](architecture.md) -- detailed codebase overview for
  contributors, covering the source generator, reactive system, and build system.
- [Testing Guide](testing.md) -- test infrastructure, patterns, and the
  requirements for submitting changes with passing tests.
- [Handler Architecture](handlers.md) -- how to add handlers for new controls,
  including property mappers and platform-specific files.
