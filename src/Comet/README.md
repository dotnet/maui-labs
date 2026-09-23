# Comet ☄️

Comet is an MVU framework for [.NET MAUI](https://learn.microsoft.com/dotnet/maui/what-is-maui). Write your entire UI in C# with a reactive state system that tracks what you read and updates only what changed. No XAML, no view models, no binding markup.

> **Status:** Comet is **experimental** and part of [dotnet/maui-labs](https://github.com/dotnet/maui-labs) — the home for pre-release .NET MAUI tooling. APIs may change between releases.

```csharp
using Comet;
using static Comet.CometControls;

public class MyApp : Component
{
    public override View Render() => Text("Hello, Comet!");
}
```

## Components and State

The canonical surface is `Component<TState>` paired with `override View Render()`. State is a plain C# object — no base class required. `SetState` batches mutations into a single render pass.

```csharp
using Comet;
using static Comet.CometControls;

public class CounterState
{
    public int Count { get; set; }
}

public class CounterView : Component<CounterState>
{
    public override View Render() => VStack(
        Text(() => $"Count: {State.Count}"),
        Button("Increment", () => SetState(s => s.Count++))
    );
}
```

Lambdas (`() => ...`) passed to controls are tracked. When `State.Count` changes, only the `Text` rebuilds — `Render()` does not re-execute.

### Reactive Primitives

For lighter-weight reactive values without a state class, use `Reactive<T>` or `Signal<T>` from `Comet.Reactive`:

```csharp
using Comet;
using Comet.Reactive;
using static Comet.CometControls;

public class GreetingView : Component
{
    readonly Signal<string> name = new("World");

    public override View Render() => VStack(
        Text(() => $"Hello, {name.Value}!"),
        TextField(name, "Enter name")
    );
}
```

`TextField(Signal<string>, placeholder)` creates a two-way binding. `Signal<T>.Peek()` reads the value without registering a dependency, even inside a reactive scope.

### Async State Updates

The reactive scheduler dispatches rebuilds to the main thread automatically — `.Value` writes from background threads are safe.

```csharp
public class ProfileState
{
    public string Name { get; set; } = "";
    public bool Loading { get; set; }
}

public class ProfileView : Component<ProfileState>
{
    public override View Render() => VStack(
        Text(() => State.Loading ? "Loading..." : $"Hello, {State.Name}!"),
        Button("Load Profile", LoadProfile)
    );

    async void LoadProfile()
    {
        SetState(s => s.Loading = true);
        var result = await Api.FetchProfile();
        SetState(s => { s.Name = result.Name; s.Loading = false; });
    }
}
```

### Batching

Multiple writes inside a single `SetState` action — or multiple synchronous `.Value` writes on a `Reactive<T>` — coalesce into one UI update. The scheduler posts a single flush to the dispatcher.

## XAML+MVVM vs Comet

A text field bound to a greeting label — same UI, different approaches.

**XAML + MVVM** — ViewModel + XAML + code-behind:

```csharp
public partial class GreetingViewModel : ObservableObject
{
    [ObservableProperty] string name = "World";
    public string Greeting => $"Hello, {Name}!";
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Greeting));
}
```
```xml
<VerticalStackLayout>
    <Label Text="{Binding Greeting}" />
    <Entry Text="{Binding Name, Mode=TwoWay}" />
</VerticalStackLayout>
```

**Comet** — one file:

```csharp
public class GreetingView : Component
{
    readonly Signal<string> name = new("World");

    public override View Render() => VStack(
        Text(() => $"Hello, {name.Value}!"),
        TextField(name, "Enter name")
    );
}
```

## Getting Started

Comet requires **.NET 11 SDK (preview)** with the MAUI workload.

```bash
dotnet workload install maui
dotnet add package Microsoft.Maui.Comet
```

Wire up Comet in `MauiProgram.cs` with `UseCometApp<TApp>()` — it registers the handlers, sets the app type, and adds Comet's lifecycle hooks in one call:

```csharp
var builder = MauiApp.CreateBuilder();
builder.UseCometApp<MyApp>();
return builder.Build();
```

Define the root view in your `CometApp`:

```csharp
public class MyApp : CometApp
{
    public MyApp() => Body = () => new CounterView();
}
```

## Hot Reload

MAUI's built-in hot reload works with Comet. Edit `Render()`, save, and the view updates on the running app — state is preserved across reloads.

For an even faster loop on physical devices, see **[Comet Go](../Go/README.md)** — a single-file dev server with a companion app.

## Styling and Theming

Comet ships a design token and styling system inspired by SwiftUI and Material Design 3. Every visual property is a fluent method call — no XAML styles, no CSS, no resource dictionaries.

```csharp
Text("Welcome")
    .FontSize(24)
    .FontWeight(FontWeight.Bold)
    .Color(Colors.White)
    .Background(Colors.DodgerBlue)
    .Padding(new Thickness(16, 12))
    .Shadow(Colors.Black, radius: 4f, x: 0f, y: 2f)
    .ClipShape(new RoundedRectangle().CornerRadius(8))
```

### Design Tokens

Semantic tokens resolve colors, typography, spacing, and shapes from the active theme. Switch themes and every token-based view updates automatically.

```csharp
using Comet.Styles;

Text("Hello")
    .Typography(TypographyTokens.TitleLarge)
    .Color(ColorTokens.OnSurface);

Button("Action", () => { })
    .ButtonStyle(ButtonStyles.Filled);
```

Token sets follow Material Design 3: `ColorTokens` (`Primary`, `OnPrimary`, `Surface`, `Error`, …), `TypographyTokens` (`DisplayLarge` through `LabelSmall`), `SpacingTokens`, and `ShapeTokens`.

### View Modifiers

Bundle styling into reusable modifiers — same concept as SwiftUI's `ViewModifier`:

```csharp
public class CardModifier : ViewModifier
{
    public override View Apply(View view)
    {
        view
            .Background(new SolidPaint(ColorTokens.Surface.Resolve(ThemeManager.Current())))
            .ClipShape(new RoundedRectangle(16))
            .Padding(new Thickness(20));
        return view;
    }
}

VStack(...).Modifier(new CardModifier());

// Compose modifiers
var highlighted = new CardModifier().Then(new HighlightModifier());
```

### Control Styles

Built-in button variants — `Filled`, `Outlined`, `Text`, `Elevated` — adapt to pressed, hovered, and disabled states using design tokens:

```csharp
Button("Save", onSave).ButtonStyle(ButtonStyles.Filled);
Button("Cancel", onCancel).ButtonStyle(ButtonStyles.Outlined);
Button("Details", onDetails).ButtonStyle(ButtonStyles.Text);
```

Set a default for all buttons in a subtree:

```csharp
VStack(...).ButtonStyle(ButtonStyles.Text);
```

Or globally via the theme. `Theme` is a `record` and `SetControlStyle` returns a derived theme — capture the return value, then activate it:

```csharp
var theme = ThemeManager.Current()
    .SetControlStyle<Button, ButtonConfiguration>(ButtonStyles.Text);
ThemeManager.SetTheme(theme);
```

### Cascading Styles

Font properties cascade from containers to children — set once, apply everywhere:

```csharp
VStack(
    Text("Title"),
    Text("Subtitle"),
    Text("Body text")
)
.FontSize(18)
.Color(Colors.DarkSlateGray);
```

Type-targeted overloads apply only to a specific control type:

```csharp
VStack(
    Text("Label"),
    Button("Action", () => { })
)
.Color(typeof(Text), Colors.Navy)
.Background(typeof(Button), Colors.Orange);
```

### Custom Environment Values

The styling system is built on a key-value environment that propagates down the view tree. You can store and retrieve your own values the same way:

```csharp
VStack(...).SetEnvironment("App.Accent", Colors.Coral, cascades: true);

// Any descendant view can read it
var accent = this.GetEnvironment<Color>("App.Accent");
```

## Navigation

Typed navigation through `NavigationView` — no route strings at call sites. `Navigation` is an instance property on `View`, so call it from inside a component or any view:

```csharp
public class ListView : Component
{
    public override View Render() => Button("Open detail",
        () => Navigation?.Navigate<DetailPage>(new DetailProps { Id = 42 }));
}
```

Wrap your root view in a `NavigationView` to enable the navigation stack:

```csharp
public class App : CometApp
{
    public App() => Body = () => new NavigationView { new HomePage() };
}
```

## MAUI Interop

Embed MAUI views in Comet, or Comet views in MAUI:

- **`CometHost`** — host a Comet `View` inside a MAUI `ContentPage`
- **`MauiViewHost`** — host a MAUI `IView` inside a Comet view tree
- **`NativeHost`** — embed raw platform views (`UIView`, `Android.Views.View`, …)

## Legacy `[Body]` Pattern

Earlier Comet code uses a `[Body]` attribute on a method instead of overriding `Render()`. This pattern remains supported for backward compatibility but should not be used in new code.

```csharp
// Legacy — kept working for existing apps:
public class HelloView : View
{
    [Body]
    View body() => Text("Hello, Comet!");
}
```

The modern equivalent is `Component` / `Component<TState>` with `override View Render()`, shown throughout this README.

## Samples

The [`sample/`](sample/) directory contains working apps:

| Sample | What it demonstrates |
|--------|----------------------|
| [CometControlsGallery](sample/CometControlsGallery) | 30+ controls with sidebar navigation |
| [Comet.Sample](sample/Comet.Sample) | 50+ component and feature demos |
| [CometMauiApp](sample/CometMauiApp) | Minimal starter template (`Component<TState>`) |
| [CometTaskApp](sample/CometTaskApp) | TabView navigation pattern |
| [CometBaristaNotes](sample/CometBaristaNotes) | Real app with Syncfusion gauges |
| [CometStressTest](sample/CometStressTest) | Performance and stress tests |

### Native BaristaNotes picker behavior

The [shared BaristaNotes sample](sample/Shared/BaristaNotes) runs in both native
probes with `-p:CometSample=baristanotes`. Drink Logging lists center the selected
item when opened, including the first and last items. Single-choice pickers apply
on tap and close, including Water Temp. Machine and Grinder are optional: Clear
or tapping the selected item removes the selection. The cleared selection stays
empty after you save an edited drink and reopen it.

Dose, Yield, Time, Made By/For, and Grind use Done. Grind stages both the micron
value and grinder; its nested grinder picker returns to Grind without discarding
the pending value. Close cancels the pending Grind changes. These confirmation
and equipment-toggle rules include approved changes to the reference app.

### Native BaristaNotes list and detail layouts

Settings, Activity, management lists, detail forms, and value-range pages share
one header layout. Its minimum covers the complete top region: Android's
separate status-bar strip counts toward that minimum, while iOS includes the
safe inset in the header padding. Text can increase the intrinsic height; pages
do not set separate header heights.
Management pages also share the source's hidden native navigation bar, so a
platform back button does not add a second top region or move the footer.

Management rows use the source's 20-point name style and one-unit separators.
Profiles have a distinct row: MEMBER starts above the avatar, the avatar and
name have the same vertical center, and the chevron stays centered in the row.
This profile arrangement is an approved change to the reference layout. Detail
forms share an outline-colored section stack that paints the one-unit gaps
between fields without coloring the unused area below the form.

### Retained route lifecycle

The native probes keep each root `ContentSwitcher` route materialized after its
first activation. Rebuilding the switcher reconciles a same-type, same-key route
declaration onto the retained native generation: current properties, callbacks,
constructor data, and children replace the prior declaration without discarding
the route's native tree. Routes with a different identity or rendered root start
a new generation.

Inactive routes remain subscribed to application data but are detached from
layout and hidden from DevFlow lookup and semantic actions. Reactivation restores
the same DevFlow identities. This contract is covered by
`RetainedLifecycleContractTests` and `CometDevAgentTests`.

## Build

> **Requires:** .NET 11 SDK (preview) with the MAUI workload. Comet has its own [`global.json`](global.json) that targets .NET 11.

From the `src/Comet/` directory:

```bash
# Rebuild the first-party SwiftUI native artifact after any shim or binding change
./src/Comet.SwiftUI.Shim/build-xcframework.sh

# Source generator first, then the framework
dotnet build src/Comet.SourceGenerator/Comet.SourceGenerator.csproj -c Release
dotnet build src/Comet/Comet.csproj -c Release

# Tests
dotnet test tests/Comet.Tests/Comet.Tests.csproj -c Release
```

### NativeAOT probe publishes

NativeAOT is opt-in per app so `PublishAot` does not flow into Comet's
`netstandard2.0` source generators. From `src/Comet/`:

```bash
# Android arm64 device/emulator, side-by-side with the normal BaristaNotes probe.
# Runtime diagnostics starts the opt-in Comet/DevFlow-compatible agent on port 9225.
dotnet publish sample/CometComposeProbe/CometComposeProbe.csproj \
  -f net11.0-android -c Release \
  -p:CometSample=baristanotes \
  -p:EnableProbeAot=true \
  -p:ProbeAotRuntimeIdentifier=android-arm64 \
  -p:EnableProbeAotTestIdentity=true \
  -p:EnableProbeRuntimeDiagnostics=true

# Android x64 emulator, side-by-side with the normal BaristaNotes probe.
# Runtime diagnostics starts the opt-in Comet/DevFlow-compatible agent.
dotnet publish sample/CometComposeProbe/CometComposeProbe.csproj \
  -f net11.0-android -c Release \
  -p:CometSample=baristanotes \
  -p:EnableProbeAot=true \
  -p:ProbeAotRuntimeIdentifier=android-x64 \
  -p:EnableProbeAotTestIdentity=true \
  -p:EnableProbeRuntimeDiagnostics=true

# iOS NativeAOT supports device RIDs. The iOS SDK rejects simulator RIDs
# for publish, so simulator deployment is not a NativeAOT verification.
dotnet publish sample/CometSwiftUIProbe/CometSwiftUIProbe.csproj \
  -f net11.0-ios -c Release \
  -p:CometSample=baristanotes \
  -p:EnableProbeAot=true \
  -p:RuntimeIdentifier=ios-arm64 \
  -p:EnableProbeAotTestIdentity=true \
  -p:EnableProbeRuntimeDiagnostics=true
```

`EnableProbeRuntimeDiagnostics` is deliberately separate from
`EnableProbeAot`: release apps do not start the in-process diagnostics server
unless explicitly requested. `EnableProbeAotTestIdentity` changes the package
or bundle ID to `com.comet.sample.aot.<sample>` while keeping the final segment
as the startup-screen key. NativeAOT intermediates and outputs are isolated under
`obj/sample-<sample>-nativeaot` and `bin/sample-<sample>-nativeaot` so normal,
simulator, and NativeAOT package graphs cannot contaminate one another.

The Preview 7 Android SDK emits `XA1040` because Android NativeAOT remains
experimental. Do not suppress that warning or treat a successful publish as a
production-support statement.

#### Generic navigation query parameters

For trimmed or NativeAOT apps, pass the concrete runtime type as `TParameters`
to `GoToAsync<TView, TParameters>` or `Navigate<TView, TParameters>`. The generic
metadata contract preserves that type's public properties. String-valued or
object-valued query dictionaries also work, including when passed through
`object`. Component props are assigned directly and are not serialized unless
the destination also requests query attributes.

On the ordinary untrimmed CLR, object-, base- and interface-typed query values
retain their runtime properties for compatibility. That fallback uses the
existing trim-unsafe reflection path and retains its IL2026 warning; dynamic-code
support is not proof that metadata survived trimming. When dynamic code is
disabled, including NativeAOT, a differing runtime property type throws
`NotSupportedException` before navigation instead of dropping properties. Use
the concrete generic type or a query dictionary in that case.

#### BaristaNotes Android comparison profiles

The opt-in `BaristaComparison` profiles build the same complete BaristaNotes app
as **Release NativeAOT, Android ARM64, APK only**. They do not remove dependencies
or app features. Use the approved corrected Android toolchain (including the
incremental `BuildArchive` service-file fix) and absolute paths. From `src/Comet/`:

```bash
# Timing: no runtime diagnostics, startup smoke checks, or DevFlow agent.
"$COMET_DOTNET" publish "$PWD/sample/CometComposeProbe/CometComposeProbe.csproj" \
  -f net11.0-android -c Release \
  -p:CometSample=baristanotes -p:BaristaComparison=timing \
  -p:BaristaFixtureInputs=/absolute/path/to/fixtures-v2

# Diagnostic: opt-in NativeAOT smoke and Comet/DevFlow agent on port 9225.
"$COMET_DOTNET" publish "$PWD/sample/CometComposeProbe/CometComposeProbe.csproj" \
  -f net11.0-android -c Release \
  -p:CometSample=baristanotes -p:BaristaComparison=diagnostic \
  -p:BaristaFixtureInputs=/absolute/path/to/fixtures-v2
```

| Profile | Application ID | Runtime diagnostics |
|---------|----------------|---------------------|
| `timing` | `com.comet.sample.perf.baristanotes` | Disabled |
| `diagnostic` | `com.comet.sample.perf.diag.baristanotes` | Enabled |

The Android Comet agent captures its own rendered activity window with PixelCopy
on API 26 and later. Use `maui devflow ui screenshot -ap 9225 --output screen.png`
after forwarding the agent port. Capture errors return HTTP 503, not a substitute
image. Timing mode starts neither the agent nor its screenshot provider.
The independent Comet agent has no log endpoint; use process-scoped native logs.

Set `COMET_DOTNET` to the approved SDK wrapper, or to a matched corrected SDK's
`dotnet` executable. Both profiles define `BARISTA_PERF` and include the same
shared fixture contract and approved input assets. Definitions are loaded only
for an explicit unmeasured provision/validate/export operation, never for
ordinary review or selected `run` startup. No measurement markers are added.
Restore intermediates are isolated
in `obj/barista-comparison-<profile>/`; compiled outputs and APKs are under
`bin/barista-comparison-<profile>/Release/net11.0-android/` (with ARM64-specific
compiler outputs in its `android-arm64/` subdirectory).
The comparison-only manifest disables Android backup while retaining the
normal permissions, queries and photo `FileProvider`. Normal probes and other
samples keep their existing identity, manifest and build behavior.

The build rejects unknown variants, a missing/different `CometSample`, Debug,
non-ARM64 RIDs, non-APK packaging, disabled NativeAOT, conflicting diagnostic
flags, and `EnableProbeAotTestIdentity`. Do not override `PublishAot` globally:
the profile sets it app-locally so source-generator projects are unaffected.
Publish does not deploy or prove runtime behavior; inspect the diagnostic
variant separately before collecting timings from the timing variant.

##### Comparison storage selection

Comparison profiles keep normal review storage until an explicit namespace is
selected. The app-owned Android control selects an app-local database before
any Barista service or theme access, through this supported boundary:

```csharp
CometBaristaNotes.Services.BaristaAppStorage.Current
    .ConfigureComparisonDatabase(validatedAbsoluteDatabasePath);
```

Call this before the first Barista-specific startup work. In the Android host,
voice platform configuration and photo adapter registration precede the first
`CoffeeTheme.SurfaceColor` read; that theme read precedes `BuildUi()` and
`BaristaNotesApp.InitializeShell()`. Selection must therefore occur before that
theme read, not inside the app view. Voice binds to the selected services only
after shell construction; photo callbacks also receive those services.

The selected SQLite file opens with `seedOnFirstRun: false`. Existing rows are
retained. Drink preferences and `BaristaServices.ThemePreferences` use that same
database; Settings no longer bypasses the selected theme store. The default
database and default MAUI `baristanotes_theme` key are not used by the selected
path. Without this explicit call, the default database path, first-run sample
seed, and MAUI theme-key read/write behavior stay unchanged.

Selection accepts an absolute database-file path, not a fixture or namespace
format. It rejects the default path (including resolved symbolic-link aliases),
directories, duplicate selection, and selection after service/theme access.
Changing the app namespace requires a new process. The static service locator
rejects a second active store while a comparison store is bound; disposal releases
that binding but does not change the process's selected path. Select once per
process; do not repeat configuration when an Android activity is recreated.

The Android host owns and tears down its logical and Compose roots. It waits for
fixture work and asynchronous voice/service cleanup before activity replacement.
`BaristaHostRoot<BaristaNotesApp>` tracks one teardown task: wait for owned work,
detach native callbacks and composition, await the app root and its services,
then await platform shutdown. `BaristaHostOwnership` prevents a replacement root
from being created before this task completes. Cleanup errors propagate and
block replacement; they do not bypass the exclusive-store guard. Compose root
disposal also removes its layout/inset listeners and window-metrics provider,
drains an active layout callback, and rejects queued callbacks after disposal.
Hosts must supply a validated private path; the storage hook is not general
filesystem sandboxing or isolation of other platform resources.

The opt-in lifecycle test compiles the real app root and pages in a separate
non-friend test assembly. It uses the production host teardown path with real
SQLite and delayed voice cancellation/platform disposal. From `src/Comet`:

```bash
dotnet build src/Comet/Comet.csproj -f net11.0-maccatalyst
dotnet test tests/Comet.Tests/Comet.Tests.csproj \
  -p:BaristaLifecycleTests=true \
  --artifacts-path /absolute/path/to/lifecycle-tests \
  --filter FullyQualifiedName~BaristaHostLifecycleTests
```

Use a separate artifacts path to keep this assembly apart from normal host
tests. This test checks same-process data/theme preservation and cleanup order;
it does not replace an Android activity-recreation check on the exact APK.

See [Comet fixture adapter](tools/perf/baristanotes/Comet/README.md) for the shared
contract, strict namespace/refusal rules, Android selection/status/chunked export
commands, and fresh-process SQLite proof. These app-owned controls are not
shared DevFlow extension routes. Android fixture-data acceptance requires a
separate device check with the exact published APK.

`src/Comet.SwiftUI.Shim/CometSwiftUIShim.xcframework` is a tracked package input, not
an external dependency. Changes to its Swift source must include the regenerated device
and simulator slices. Clean and rebuild `sample/CometSwiftUIProbe` after regeneration so
the app relinks the current native framework.

Or from the repo root:

```bash
dotnet build src/Comet/src/Comet/Comet.csproj -c Release
```

## Platforms

Comet targets every platform .NET MAUI supports: **Android**, **iOS**, **macOS (Catalyst)**, and **Windows**.

## Contributing

See the [maui-labs contributing guide](../../CONTRIBUTING.md) for build prerequisites, PR guidelines, and CI details.
