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

## Build

> **Requires:** .NET 11 SDK (preview) with the MAUI workload. Comet has its own [`global.json`](global.json) that targets .NET 11.

From the `src/Comet/` directory:

```bash
# Source generator first, then the framework
dotnet build src/Comet.SourceGenerator/Comet.SourceGenerator.csproj -c Release
dotnet build src/Comet/Comet.csproj -c Release

# Tests
dotnet test tests/Comet.Tests/Comet.Tests.csproj -c Release
```

Or from the repo root:

```bash
dotnet build src/Comet/src/Comet/Comet.csproj -c Release
```

## Platforms

Comet targets every platform .NET MAUI supports: **Android**, **iOS**, **macOS (Catalyst)**, and **Windows**.

## Contributing

See the [maui-labs contributing guide](../../CONTRIBUTING.md) for build prerequisites, PR guidelines, and CI details.
