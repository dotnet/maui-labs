# Comet -- AI Agent Reference

Comet is an MVU (Model-View-Update) framework built on .NET MAUI.
Views are C# functions. State changes trigger automatic re-rendering. No XAML.

## Build and Test

Requires .NET 11 SDK (preview) with MAUI workload (`dotnet workload install maui`).

```bash
# Build order matters -- source generator MUST build first
dotnet build src/Comet.SourceGenerator/Comet.SourceGenerator.csproj -c Release
dotnet build src/Comet/Comet.csproj -c Release

# Tests reference maccatalyst DLL directly (not a project reference)
dotnet build tests/Comet.Tests/Comet.Tests.csproj -c Release
dotnet test tests/Comet.Tests/Comet.Tests.csproj --no-build -c Release

# Single test
dotnet test tests/Comet.Tests/Comet.Tests.csproj --no-build -c Release --filter "FullyQualifiedName~ClassName.MethodName"

# Run a sample on Mac Catalyst
dotnet build sample/CometMauiApp/CometMauiApp.csproj -t:Run -f net11.0-maccatalyst
```

## Core Patterns

### Pattern 1 (Legacy): Stateless view with [Body]

> The canonical surface for new code is **Pattern 4** (`Component<TState>` with
> `override View Render()`). The `[Body]` attribute is retained for backward
> compatibility but new components should not use it.

```csharp
using Comet;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

public class HelloView : View
{
	[Body]
	View body() => VStack(
		Text("Hello, Comet!")
			.FontSize(24)
			.Color(Colors.Blue),
		Text("Declarative UI in C#")
	);
}
```

### Pattern 2: Reactive state with Reactive<T>

```csharp
using Comet;
using Comet.Reactive;
using static Comet.CometControls;

public class CounterView : View
{
	readonly Reactive<int> count = 0;

	[Body]
	View body() => VStack(
		Text(() => $"Count: {count.Value}"),
		Button("Increment", () => count.Value++)
	);
}
```

Reading `count.Value` inside a `Func<>` (lambda) is tracked automatically.
When `count.Value` changes, the view rebuilds. This is the core reactive mechanism.

`Signal<T>` is the advanced variant with thread-safe writes and `Peek()` (read without tracking).

### Pattern 3: Two-way binding with callbacks

```csharp
using Comet;
using Comet.Reactive;
using static Comet.CometControls;

public class TextInputView : View
{
	readonly Signal<string> name = new("");

	[Body]
	View body() => VStack(
		TextField(name, "Enter name"),
		Text(() => $"Hello, {name.Value}!")
	);
}
```

`TextField(Signal<string>, placeholder)` creates a bidirectional binding.
For one-way display with change callback, use `OnTextChanged`:

```csharp
TextField("initial", "placeholder")
	.OnTextChanged(newText => Console.WriteLine(newText))
```

### Pattern 4: Component<TState> with SetState

```csharp
using Comet;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

public class TodoState
{
	public string NewItem { get; set; } = "";
	public List<string> Items { get; set; } = new();
}

public class TodoApp : Component<TodoState>
{
	public override View Render() => VStack(
		TextField(State.NewItem, "New todo")
			.OnTextChanged(t => SetState(s => s.NewItem = t)),
		Button("Add", () => SetState(s =>
		{
			s.Items.Add(s.NewItem);
			s.NewItem = "";
		})),
		new ListView<string>(() => State.Items)
		{
			ViewFor = item => Text(item)
		}
	);
}
```

`SetState(Action<TState>)` batches mutations into one render pass via ReactiveScheduler.
State objects are plain C# classes -- no base class required.

### Pattern 5: Layout containers with collection initializers

```csharp
// Static factory methods (via `using static Comet.CometControls`)
VStack(spacing, child1, child2, ...)
HStack(spacing, child1, child2, ...)

// Collection initializer syntax (equivalent)
new VStack { child1, child2, child3 }

// Grid with rows and columns
new Grid(
	columns: new object[] { "*", "*" },
	rows: new object[] { "Auto", "*" })
{
	Text("Top-Left").Cell(row: 0, column: 0),
	Text("Top-Right").Cell(row: 0, column: 1),
	Text("Bottom").Cell(row: 1, column: 0).GridColumnSpan(2),
}

// ZStack for overlay
new ZStack { background, foreground }

// ScrollView wrapping
ScrollView(Orientation.Vertical, VStack(...))
```

## App Entry Point

```csharp
// MauiProgram.cs
using Comet;
using Microsoft.Maui.Hosting;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseCometApp<MyApp>();
		return builder.Build();
	}
}

// MyApp.cs
public class MyApp : CometApp
{
	[Body]
	View body() => new MainPage();
}
```

## Control Quick Reference

All controls accept raw values, `Func<T>` (reactive), or `Signal<T>` (two-way) for their primary parameter.

| Control | Constructor | Key Callback |
|---------|------------|-------------|
| `Text` | `Text(string value)` | -- |
| `Button` | `Button(string text, Action handler)` | `handler` param |
| `TextField` | `TextField(string text, string placeholder, Action<string> onCompleted)` | `.OnTextChanged(Action<string>)` |
| `SecureField` | `SecureField(string text, string placeholder)` | `.OnTextChanged(Action<string>)` |
| `TextEditor` | `TextEditor(string text)` | `.OnTextChanged(Action<string>)` |
| `Toggle` | `Toggle(bool value)` | `.OnToggled(Action<bool>)` |
| `Slider` | `Slider(double value, double min=0, double max=1)` | `.OnValueChanged(Action<double>)` |
| `Stepper` | `Stepper(double value, double min, double max, double interval)` | `.OnValueChanged(Action<double>)` |
| `CheckBox` | `CheckBox(bool isChecked)` | `.OnCheckedChanged(Action<bool>)` |
| `DatePicker` | `DatePicker(DateTime date, DateTime min, DateTime max)` | -- |
| `TimePicker` | `TimePicker(TimeSpan time)` | -- |
| `Picker` | `Picker(int selectedIndex, params string[] items)` | `.OnSelectedIndexChanged(Action<int>)` |
| `ProgressBar` | `ProgressBar(double value)` | -- |
| `ActivityIndicator` | `ActivityIndicator(bool isRunning)` | -- |
| `SearchBar` | `SearchBar(string text, string placeholder, Action<string> onSearch)` | `onSearch` param |
| `Image` | `Image(string source)` or `Image(IImageSource source)` | -- |
| `ImageButton` | `ImageButton(IImageSource source, Action handler)` | `handler` param |
| `RadioButton` | `RadioButton(string label, bool selected, Action onClick)` | `onClick` param |
| `BoxView` | `BoxView(Color color)` | -- |
| `Spacer` | `Spacer()` | -- |

### Layout Containers

| Container | Constructor | Notes |
|-----------|------------|-------|
| `VStack` | `VStack(LayoutAlignment alignment, float? spacing)` | Vertical stack |
| `HStack` | `HStack(LayoutAlignment alignment, float? spacing)` | Horizontal stack |
| `ZStack` | `ZStack()` | Overlay |
| `Grid` | `Grid(object[] columns, object[] rows, float? spacing)` | Row/column grid |
| `ScrollView` | `ScrollView(Orientation orientation)` | Scrollable container |
| `Border` | `Border(View content)` | Bordered container |
| `Frame` | `Frame(View content)` | Frame container |
| `FlexLayout` | `FlexLayout()` | Flex layout |

### Navigation and Structure

| Control | Usage | Notes |
|---------|-------|-------|
| `NavigationView` | `NavigationView(content)` | Stack navigation |
| `TabView` | `tabView.AddTab("Title", content)` | Tab container |
| `ListView<T>` | `new ListView<T>(items) { ViewFor = x => ... }` | Virtual list |
| `CollectionView<T>` | `new CollectionView<T>(items) { ViewFor = x => ... }` | Virtual collection |
| `CarouselView<T>` | `new CarouselView<T>(items) { ViewFor = x => ... }` | Horizontal carousel |
| `WebView` | `new WebView { Source = "https://..." }` | Web content |
| `FlyoutPage` | `new FlyoutPage { Flyout = menu, Detail = content }` | Master-detail |

## Common Mistakes

### 1. String interpolation without lambda -- NO reactive tracking

```csharp
// WRONG -- evaluates once, never updates
Text($"Count: {count.Value}")

// CORRECT -- lambda is re-evaluated on state change
Text(() => $"Count: {count.Value}")
```

### 2. Forgetting OnTextChanged for two-way binding in Component<TState>

```csharp
// WRONG -- displays state but user edits are lost
TextField(State.Name, "Name")

// CORRECT -- writes user input back to state
TextField(State.Name, "Name")
	.OnTextChanged(t => SetState(s => s.Name = t))
```

When using `Signal<T>` or `Reactive<T>`, pass the signal directly for automatic two-way binding:
```csharp
readonly Signal<string> name = new("");
// This is automatically two-way:
TextField(name, "Name")
```

### 3. Wrong build order -- source generator must build first

```bash
# WRONG
dotnet build src/Comet/Comet.csproj

# CORRECT
dotnet build src/Comet.SourceGenerator/Comet.SourceGenerator.csproj -c Release
dotnet build src/Comet/Comet.csproj -c Release
```

### 4. Using `new VStack(child1, child2)` instead of collection initializer or factory

```csharp
// WRONG -- VStack constructor takes alignment and spacing, not children
new VStack(child1, child2)

// CORRECT -- collection initializer
new VStack { child1, child2 }

// CORRECT -- static factory (requires `using static Comet.CometControls`)
VStack(child1, child2)

// CORRECT -- factory with spacing
VStack(16, child1, child2)
```

### 5. Mutating Reactive<T> without .Value

```csharp
readonly Reactive<int> count = 0;

// WRONG -- reassigns the field (compile error if readonly)
count = 5;

// CORRECT -- triggers reactive update
count.Value = 5;
```

### 6. Reading .Value outside a lambda -- not tracked

```csharp
// WRONG -- read happens at construction time, not tracked
var label = $"Count: {count.Value}";
Text(label)

// CORRECT -- read happens inside tracked lambda
Text(() => $"Count: {count.Value}")
```

### 7. Forgetting `using static Comet.CometControls`

Without this import, factory methods like `VStack(...)`, `Text(...)`, `Button(...)`,
`NavigationView(...)`, `ScrollView(...)` are not available. You must use `new VStack { ... }` syntax instead.

### 8. Missing usings -- implicit usings are disabled

```csharp
// Required for all Comet files
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

// Required for Signal<T>, Computed<T>, SignalList<T>, Effect
using Comet.Reactive;
```

### 9. Using Component Render() with [Body] -- pick one pattern

```csharp
// View pattern -- use [Body]
public class MyView : View
{
	[Body]
	View body() => Text("hello");
}

// Component pattern -- override Render()
public class MyComponent : Component<MyState>
{
	public override View Render() => Text("hello");
}
```

### 10. Forgetting to call NavigationView.Navigate from a view context

```csharp
// WRONG -- no navigation context
NavigationView.Navigate(null, new DetailPage());

// CORRECT -- pass the current view as source
NavigationView.Navigate(this, new DetailPage());

// PREFERRED -- use OnSelectedNavigate on ListView
new ListView<Item>(items)
{
	ViewFor = item => Text(item.Name)
}.OnSelectedNavigate(item => new DetailPage(item))
```

## Environment and Styling

### Colors and Background

```csharp
view.Color(Colors.Red)                    // Foreground/text color
view.Background(Colors.Blue)              // Background
view.Background("#FF5733")                // Hex string
view.Opacity(0.5)                         // Transparency
```

### Typography

```csharp
view.FontSize(18)
view.FontWeight(FontWeight.Bold)
view.FontFamily("OpenSansRegular")
view.FontSlant(FontSlant.Italic)
view.HorizontalTextAlignment(TextAlignment.Center)
view.VerticalTextAlignment(TextAlignment.Center)
```

### Layout

```csharp
view.Padding(new Thickness(16))           // Padding
view.Margin(10)                           // Uniform margin
view.Margin(left: 10, top: 5)            // Specific sides
view.Frame(width: 200, height: 100)       // Size constraints
view.FillHorizontal()                     // Fill available width
view.FitHorizontal()                      // Fit to content width
view.Alignment(Alignment.Center)          // Alignment
view.IgnoreSafeArea()                     // Ignore safe area
```

### Drawing and Shapes

```csharp
view.ClipShape(new RoundedRectangle(16))  // Rounded corners via clip
view.RoundedBorder(radius: 8, color: Colors.Grey, strokeSize: 1)
view.Shadow(Colors.Black, radius: 4, x: 0, y: 2)
```

### Grid Cell Placement

```csharp
view.Cell(row: 0, column: 1)             // Place in grid
view.GridRowSpan(2)                       // Span rows
view.GridColumnSpan(3)                    // Span columns
```

### Design Tokens (Theme System)

```csharp
using Comet.Styles;

// Color tokens resolve from the active theme
view.Color(ColorTokens.Primary)
view.Background(ColorTokens.Surface)
view.Color(ColorTokens.OnSurface)

// Typography tokens
view.Typography(TypographyTokens.TitleLarge)
view.Typography(TypographyTokens.BodyMedium)

// Button styles
button.ButtonStyle(ButtonStyles.Filled)
button.ButtonStyle(ButtonStyles.Outlined)
button.ButtonStyle(ButtonStyles.Text)
button.ButtonStyle(ButtonStyles.Elevated)
```

### Gestures

```csharp
view.OnTap(() => Console.WriteLine("tapped"))
view.OnLongPress(() => Console.WriteLine("long press"))
```

### Control-Specific Styling

```csharp
slider.MinimumTrackColor(Colors.Blue)     // Track left of thumb
slider.MaximumTrackColor(Colors.Grey)     // Track right of thumb
slider.ThumbColor(Colors.White)

toggle.OnColor(Colors.Green)              // On-state color
toggle.ThumbColor(Colors.White)

textField.PlaceholderColor(Colors.Grey)
textField.Keyboard(Keyboard.Email)

button.CornerRadius(8)
button.BorderWidth(1)
button.BorderColor(Colors.Grey)

image.Aspect(Aspect.AspectFill)
```

## Navigation

> The examples below use the legacy `[Body]` pattern for brevity. Real
> navigation code should prefer `Component<TState>` with `override View Render()`
> (Pattern 4) — the navigation primitives (`NavigationView`, `Navigate`, `Pop`)
> work identically with either pattern.

### Stack Navigation

```csharp
public class MyApp : CometApp
{
	[Body]
	View body() => NavigationView(new HomePage());
}

public class HomePage : View
{
	[Body]
	View body() => VStack(
		Button("Go to Detail", () =>
			NavigationView.Navigate(this, new DetailPage()))
	).Title("Home");
}

public class DetailPage : View
{
	[Body]
	View body() => VStack(
		Text("Detail content"),
		Button("Go Back", () => NavigationView.Pop(this))
	).Title("Detail");
}
```

### Tab Navigation

```csharp
public class TabApp : View
{
	[Body]
	View body()
	{
		var tabs = new TabView();
		tabs.AddTab("Home", new HomePage());
		tabs.AddTab("Settings", new SettingsPage());
		return tabs;
	}
}
```

### List with Navigation

```csharp
new ListView<Item>(items)
{
	ViewFor = item => HStack(
		Text(item.Name),
		Spacer()
	).Frame(height: 44)
}.OnSelectedNavigate(item => new DetailPage(item).Title(item.Name))
```

### Modal Presentation

```csharp
ModalView.Present(new MyModalContent());
ModalView.Dismiss();
```

## Reactive Types Reference

| Type | Purpose | Two-Way | Thread-Safe |
|------|---------|---------|-------------|
| `Reactive<T>` | Mutable state field in views | Yes (implicit) | No |
| `Signal<T>` | Advanced mutable state | Yes (explicit) | Yes (lock) |
| `Computed<T>` | Derived value from signals | No (read-only) | Yes |
| `SignalList<T>` | Observable list | N/A | Yes |
| `Effect` | Side-effect on dependency change | N/A | N/A |
| `PropertySubscription<T>` | Internal binding primitive | Yes | Yes |

```csharp
// Reactive<T> -- simplest, used in [Body] views
readonly Reactive<int> count = 0;
count.Value++;                             // triggers re-render

// Signal<T> -- thread-safe, has Peek()
readonly Signal<string> text = new("");
var current = text.Peek();                 // read without tracking

// Computed<T> -- derived value
readonly Computed<string> label;
label = new Computed<string>(() => $"Count: {count.Value}");

// SignalList<T> -- reactive list with granular changes
readonly SignalList<string> items = new();
items.Add("item");
items.Batch(list => { list.Add("a"); list.Add("b"); }); // single notification
```

## Handler Customization

Extend or customize how a Comet control maps to native platform views:

```csharp
// In MauiProgram.cs -- append to existing handler mapping
Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("CustomEntry", (handler, view) =>
{
#if ANDROID
	handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
#elif IOS
	handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
});
```

## File Conventions

| Pattern | Platform | Example |
|---------|----------|---------|
| `*.iOS.cs` or `iOS/` folder | iOS only | `MyView.iOS.cs` |
| `*.Android.cs` or `Android/` folder | Android only | `MyView.Android.cs` |
| `*.Windows.cs` or `Windows/` folder | Windows only | `MyView.Windows.cs` |
| `*.Mac.cs` or `MacCatalyst/` folder | Mac Catalyst only | `MyView.Mac.cs` |

Controlled by `Directory.Build.targets` -- files are included/excluded by target framework.

## Code Style

- Tabs for indentation (not spaces)
- Allman brace style
- `var` preferred when type is apparent
- No `this.` qualifier on members
- Implicit usings are disabled -- all `using` statements explicit
- Layout containers use C# collection initializer syntax

## Project Structure

```
src/Comet/                         -- Framework library (multi-targeted)
src/Comet.SourceGenerator/         -- Roslyn source generator (netstandard2.0)
tests/Comet.Tests/                 -- xUnit tests (inherits TestBase, calls UI.Init())
sample/                            -- 10 sample projects
docs/                              -- Detailed guides
```

## Documentation Pointers

For deep dives, see `docs/`:

| Topic | File |
|-------|------|
| Getting started | `docs/getting-started.md` |
| All controls | `docs/controls.md` |
| Layout system | `docs/layout.md` |
| Reactive state (practical) | `docs/reactive-state-guide.md` |
| State management (deep dive) | `docs/state-management.md` |
| Navigation | `docs/navigation.md` |
| Styling and theming | `docs/styling.md` |
| Forms and validation | `docs/forms.md` |
| Testing | `docs/testing.md` |
| Troubleshooting | `docs/troubleshooting.md` |
| Handler architecture | `docs/handlers.md` |
| Platform-specific code | `docs/platform-guides.md` |
| MAUI interop | `docs/maui-interop.md` |
| Performance | `docs/performance.md` |
| Animations and gestures | `docs/animations.md` |
| Accessibility | `docs/accessibility.md` |

## Squad Instructions

This project uses **Squad**, an AI team framework. When picking up issues autonomously, follow these guidelines.

### Team Context

Before starting work on any issue:

1. Read `.squad/team.md` for the team roster, member roles, and your capability profile.
2. Read `.squad/routing.md` for work routing rules.
3. If the issue has a `squad:{member}` label, read that member's charter at `.squad/agents/{member}/charter.md` to understand their domain expertise and coding style -- work in their voice.

### Capability Self-Check

Before starting work, check your capability profile in `.squad/team.md` under the **Coding Agent -> Capabilities** section.

- **Good fit** -- proceed autonomously.
- **Needs review** -- proceed, but note in the PR description that a squad member should review.
- **Not suitable** -- do NOT start work. Instead, comment on the issue:
  ```
  This issue doesn't match my capability profile (reason: {why}). Suggesting reassignment to a squad member.
  ```

### Branch Naming

Use the squad branch convention:
```
squad/{issue-number}-{kebab-case-slug}
```
Example: `squad/42-fix-login-validation`

### PR Guidelines

When opening a PR:
- Reference the issue: `Closes #{issue-number}`
- If the issue had a `squad:{member}` label, mention the member: `Working as {member} ({role})`
- If this is a needs-review task, add to the PR description: `This task was flagged as "needs review" -- please have a squad member review before merging.`
- Follow any project conventions in `.squad/decisions.md`

### Decisions

If you make a decision that affects other team members, write it to:
```
.squad/decisions/inbox/copilot-{brief-slug}.md
```
The Scribe will merge it into the shared decisions file.
