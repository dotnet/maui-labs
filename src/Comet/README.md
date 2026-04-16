# Comet ☄️

[![dev-build](https://github.com/dotnet/Comet/actions/workflows/dev.yml/badge.svg)](https://github.com/dotnet/Comet/actions/workflows/dev.yml)  [![Microsoft.Maui.Comet on fuget.org](https://www.fuget.org/packages/Microsoft.Maui.Comet/badge.svg)](https://www.fuget.org/packages/Microsoft.Maui.Comet)

Comet is an MVU framework for [.NET MAUI](https://learn.microsoft.com/dotnet/maui/what-is-maui). Write your entire UI in C# with a reactive state system that tracks what you read and updates only what changed. No XAML, no view models, no binding markup.

```csharp
public class MyApp : View
{
    [Body]
    View body() => new Text("Hello, Comet!");
}
```

## Reactive State

One primitive: `Reactive<T>`. Declare it, read `.Value` in a lambda, write `.Value` anywhere. The UI updates automatically.

```csharp
public class CounterView : View
{
    readonly Reactive<int> count = 0;

    [Body]
    View body() => new VStack {
        new Text(() => $"Count: {count.Value}"),
        new Button("Increment", () => count.Value++)
    };
}
```

When the button increments `count.Value`, only the `Text` control updates — `body()` does not re-execute. Comet tracks the read inside the lambda and performs a fine-grained update at the control level.

### Two-Way Binding

Bind a `Reactive<T>` to input controls. Typing updates the signal; changing the signal updates the control.

```csharp
public class GreetingView : View
{
    readonly Reactive<string> name = "World";

    [Body]
    View body() => new VStack {
        new Text(() => $"Hello, {name.Value}!"),
        new TextField(() => name.Value, () => "Enter name")
            .OnTextChanged(v => name.Value = v ?? "")
    };
}
```

### State Updates in Methods

Writing to `.Value` triggers a UI refresh. The reactive scheduler dispatches a rebuild to the main thread automatically, even from background threads.

```csharp
public class ProfileView : View
{
	readonly Reactive<string> name = "";
	readonly Reactive<bool> loading = false;

	[Body]
	View body() => new VStack {
		new Text(() => loading.Value ? "Loading..." : $"Hello, {name.Value}!"),
		new Button("Load Profile", LoadProfile)
	};

	async void LoadProfile()
	{
		loading.Value = true;                   // triggers rebuild → shows "Loading..."
		var result = await Api.FetchProfile();
		name.Value = result.Name;               // triggers rebuild
		loading.Value = false;                   // triggers rebuild → shows greeting
	}
}
```

### Reading State Without Creating a Binding

Reads inside `() => ...` lambdas passed to controls create reactive bindings. Reads in regular methods are plain value access — no binding, no tracking.

```csharp
public class DiagnosticsView : View
{
	readonly Reactive<int> count = 0;

	[Body]
	View body() => new VStack {
		// This read IS tracked — Text updates when count changes
		new Text(() => $"Count: {count.Value}"),
		new Button("Increment", () => count.Value++),
		new Button("Log", LogCount)
	};

	void LogCount()
	{
		// This read is NOT tracked — just retrieves the current value
		Console.WriteLine($"Current count: {count.Value}");
	}
}
```

`Signal<T>` (in `Comet.Reactive`) provides a `Peek()` method that reads the value without triggering any tracking, even inside a reactive scope:

```csharp
var signal = new Signal<int>(0);
int current = signal.Peek();    // no tracking, no PropertyRead event
```

### Batching Multiple State Updates

Multiple `.Value` writes in the same synchronous block are coalesced into a single UI update. The `ReactiveScheduler` posts one flush to the dispatcher — rapid writes before that flush piggyback on it.

```csharp
public class SettingsView : View
{
	readonly Reactive<bool> darkMode = false;
	readonly Reactive<bool> notifications = true;
	readonly Reactive<string> language = "en";

	[Body]
	View body() => new VStack {
		new Toggle(() => darkMode.Value).OnToggled(v => darkMode.Value = v),
		new Toggle(() => notifications.Value).OnToggled(v => notifications.Value = v),
		new Text(() => $"Language: {language.Value}"),
		new Button("Reset All", ResetDefaults)
	};

	void ResetDefaults()
	{
		// Three writes, one UI update — the scheduler coalesces them
		darkMode.Value = false;
		notifications.Value = true;
		language.Value = "en";
	}
}
```

For components with typed state, `SetState()` batches mutations explicitly — the entire action runs before a single rebuild is scheduled:

```csharp
class FormState
{
	public string FirstName { get; set; } = "";
	public string LastName { get; set; } = "";
	public string Email { get; set; } = "";
}

public class FormComponent : Component<FormState>
{
	public override View Render() => new VStack {
		new TextField(() => State.FirstName).OnTextChanged(v =>
			SetState(s => s.FirstName = v ?? "")),
		new TextField(() => State.LastName).OnTextChanged(v =>
			SetState(s => s.LastName = v ?? "")),
		new TextField(() => State.Email).OnTextChanged(v =>
			SetState(s => s.Email = v ?? "")),
		new Button("Reset", () => SetState(s => {
			s.FirstName = "";
			s.LastName = "";
			s.Email = "";
		}))
	};
}
```

## XAML+MVVM vs Comet

A text field bound to a greeting label — same UI, different approaches.

**XAML + MVVM** — ViewModel + XAML + code-behind:

```csharp
// GreetingViewModel.cs
public partial class GreetingViewModel : ObservableObject
{
    [ObservableProperty] string name = "World";
    public string Greeting => $"Hello, {Name}!";
    partial void OnNameChanged(string value) =>
        OnPropertyChanged(nameof(Greeting));
}
```
```xml
<!-- GreetingPage.xaml -->
<VerticalStackLayout>
    <Label Text="{Binding Greeting}" />
    <Entry Text="{Binding Name, Mode=TwoWay}" />
</VerticalStackLayout>
```

**Comet** — one file:

```csharp
public class GreetingView : View
{
    readonly Reactive<string> name = "World";

    [Body]
    View body() => new VStack {
        new Text(() => $"Hello, {name.Value}!"),
        new TextField(() => name.Value, () => "Enter name")
            .OnTextChanged(v => name.Value = v ?? "")
    };
}
```

## Getting Started

Comet requires .NET 10 with the MAUI workload.

```bash
dotnet workload install maui
dotnet add package Microsoft.Maui.Comet
```

Register Comet handlers in `MauiProgram.cs`:

```csharp
var builder = MauiApp.CreateBuilder();
builder.UseMauiApp<MyApp>();
builder.UseCometHandlers();
return builder.Build();
```

## Hot Reload

MAUI's built-in hot reload works with Comet. Change a `[Body]` method, save, and the view updates on the running app. State is preserved across reloads.

## Styling and Theming

Comet ships a design token and styling system inspired by SwiftUI and Material Design 3. Every visual property is a fluent method call — no XAML styles, no CSS, no resource dictionaries.

```csharp
new Text("Welcome")
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

new Text("Hello")
    .Typography(TypographyTokens.TitleLarge)
    .Color(ColorTokens.OnSurface),

new Button("Action", () => { })
    .ButtonStyle(ButtonStyles.Filled)
```

Token sets follow Material Design 3: `ColorTokens` (Primary, OnPrimary, Surface, Error, etc.), `TypographyTokens` (DisplayLarge through LabelSmall), `SpacingTokens`, and `ShapeTokens`.

### View Modifiers

Bundle styling into reusable modifiers — same concept as SwiftUI's `ViewModifier`:

```csharp
public class CardModifier : ViewModifier
{
    public override View Apply(View view)
    {
        view
            .Background(new SolidPaint(
                ColorTokens.Surface.Resolve(ThemeManager.Current())))
            .ClipShape(new RoundedRectangle(16))
            .Padding(new Thickness(20));
        return view;
    }
}

// Apply to any view
new VStack { ... }.Modifier(new CardModifier())

// Compose modifiers
var highlighted = new CardModifier().Then(new HighlightModifier());
```

### Control Styles

Built-in button variants — `Filled`, `Outlined`, `Text`, `Elevated` — adapt to pressed, hovered, and disabled states using design tokens:

```csharp
new Button("Save", onSave).ButtonStyle(ButtonStyles.Filled),
new Button("Cancel", onCancel).ButtonStyle(ButtonStyles.Outlined),
new Button("Details", onDetails).ButtonStyle(ButtonStyles.Text),
```

Set a default for all buttons in a subtree:

```csharp
new VStack { ... }.ButtonStyle(ButtonStyles.Text)
```

Or globally via the theme:

```csharp
var theme = ThemeManager.Current();
theme.SetControlStyle<Button, ButtonConfiguration>(ButtonStyles.Text);
ThemeManager.SetTheme(theme);
```

### Cascading Styles

Font properties cascade from containers to children — set once, apply everywhere:

```csharp
new VStack {
    new Text("Title"),
    new Text("Subtitle"),
    new Text("Body text")
}
.FontSize(18)
.Color(Colors.DarkSlateGray)
```

Type-targeted overloads apply only to a specific control type:

```csharp
new VStack {
    new Text("Label"),
    new Button("Action", () => { }),
}
.Color(typeof(Text), Colors.Navy)
.Background(typeof(Button), Colors.Orange)
```

### Custom Environment Values

The styling system is built on a key-value environment that propagates down the view tree. You can store and retrieve your own values the same way:

```csharp
new VStack { ... }
    .SetEnvironment("App.Accent", Colors.Coral, cascades: true);

// Any descendant view can read it
var accent = this.GetEnvironment<Color>("App.Accent");
```

## Navigation

Fluent Shell wrapper with typed navigation — no route strings at call sites:

```csharp
Navigation.Navigate<DetailPage>(new DetailProps { Id = 42 });
```

## MAUI Interop

Embed MAUI views in Comet or Comet views in MAUI:

- **`CometHost`** — use a Comet `View` inside a MAUI `ContentPage`
- **`MauiViewHost`** — use a MAUI `IView` inside a Comet view tree
- **`NativeHost`** — embed raw platform views (`UIView`, `Android.Views.View`)

## Samples

The [`sample/`](sample/) directory contains working apps:

| Sample | What it demonstrates |
|--------|---------------------|
| [CometControlsGallery](sample/CometControlsGallery) | 30+ controls with sidebar navigation |
| [Comet.Sample](sample/Comet.Sample) | 50+ component and feature demos |
| [CometMauiApp](sample/CometMauiApp) | Minimal starter template |
| [CometTaskApp](sample/CometTaskApp) | TabView navigation pattern |
| [CometBaristaNotes](sample/CometBaristaNotes) | Real app with Syncfusion gauges |
| [CometStressTest](sample/CometStressTest) | Performance and stress tests |

## Build

```bash
# Source generator first, then the framework
dotnet build src/Comet.SourceGenerator/Comet.SourceGenerator.csproj -c Release
dotnet build src/Comet/Comet.csproj -c Release

# Tests
dotnet build tests/Comet.Tests/Comet.Tests.csproj -c Release
dotnet test tests/Comet.Tests/Comet.Tests.csproj --no-build -c Release
```

## Platforms

Comet targets every platform .NET MAUI supports: **Android**, **iOS**, **macOS (Catalyst)**, and **Windows**.

## Status

Comet is **experimental** and lives in [maui-labs](https://github.com/dotnet/maui-labs) — the home for pre-release .NET MAUI tooling. APIs may change between releases. Community contributions and feedback are welcome.
