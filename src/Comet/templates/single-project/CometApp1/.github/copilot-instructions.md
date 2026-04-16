# Comet — Copilot Instructions

This is a .NET MAUI app built with **Comet**, an MVU (Model-View-Update) framework.
All UI is written in declarative C# — no XAML, no view models, no binding markup.

## Core Patterns

### Views and the [Body] attribute

Every page is a class that extends `View`. The UI is returned from a method
marked with `[Body]`:

```csharp
public class MyPage : View
{
    [Body]
    View body() => Text("Hello, Comet!");
}
```

### Reactive state with Reactive<T>

Declare state with `Reactive<T>`. Read `.Value` inside a lambda to create
a binding. Write `.Value` anywhere to trigger a UI update:

```csharp
readonly Reactive<int> count = 0;

[Body]
View body() => VStack(spacing: 12,
    Text(() => $"Count: {count.Value}"),  // reading .Value in lambda = binding
    Button("Add", () => count.Value++)     // writing .Value = triggers update
);
```

### Component<TState> for complex state

For pages with multiple related state fields, use `Component<TState>`:

```csharp
public class TodoState
{
    public List<string> Items { get; set; } = [];
}

public class TodoPage : Component<TodoState>
{
    public override View Render() => VStack(
        State.Items.Select(item => Text(item)).ToArray()
    );

    void AddItem(string text) => SetState(s => s.Items.Add(text));
}
```

### Two-way binding with Signal<T>

Use `Signal<T>` (from `Comet.Reactive`) for input controls like `TextField`:

```csharp
readonly Signal<string> name = new("");

[Body]
View body() => TextField(name, "Enter name...");
```

### Static imports

Use `using static Comet.CometControls;` to access factory methods like
`Text()`, `Button()`, `VStack()`, `HStack()`, `Image()`, etc. without `new`.

### Layout

- `VStack(spacing, children...)` — vertical stack
- `HStack(spacing, children...)` — horizontal stack
- `ZStack(children...)` — overlay stack
- `Grid(rows, columns, children...)` — grid layout
- `.Alignment(Alignment.Center)` — center a view
- `.Frame(width, height)` — fixed size
- `.Padding(thickness)` — padding
- `.Margin(thickness)` — margin

### Navigation

```csharp
// Wrap content in NavigationView
NavigationView(content).Title("Page Title")

// Push a page
NavigationView.Navigate(this, new DetailPage());

// Pop back
NavigationView.Pop(this);
```

## Theming and Styling

Comet has a token-based theme system following Material Design 3 conventions.

### Setting up a custom theme

Create a `Theme` and set it as current in your `App` constructor or `MauiProgram.cs`:

```csharp
using Comet.Styles;
using Microsoft.Maui.Graphics;

public static class AppTheme
{
    public static Theme Light => new Theme
    {
        Name = "Light",
        CurrentTheme = AppTheme.Light,
        ColorScheme = new ThemeColors
        {
            Primary = Color.FromArgb("#6750A4"),
            OnPrimary = Colors.White,
            PrimaryContainer = Color.FromArgb("#EADDFF"),
            OnPrimaryContainer = Color.FromArgb("#21005D"),
            Secondary = Color.FromArgb("#625B71"),
            OnSecondary = Colors.White,
            Surface = Color.FromArgb("#FFFBFE"),
            OnSurface = Color.FromArgb("#1C1B1F"),
            Background = Colors.White,
            OnBackground = Color.FromArgb("#1C1B1F"),
            Error = Color.FromArgb("#B3261E"),
            OnError = Colors.White,
            Outline = Color.FromArgb("#79747E"),
        },
    };
}
```

Apply in your App:

```csharp
public class App : CometApp
{
    public App()
    {
        Theme.Current = AppTheme.Light;
        Body = () => new MainPage();
    }
}
```

### Using theme colors in views

```csharp
// Semantic theme extensions
button.ThemeBackground(EnvironmentKeys.ThemeColor.Primary)
text.ThemeForeground(EnvironmentKeys.ThemeColor.OnSurface)
card.ThemeColors(EnvironmentKeys.ThemeColor.Surface, EnvironmentKeys.ThemeColor.OnSurface)

// Or use the theme directly
button.Background(Theme.Current.GetColor(EnvironmentKeys.ThemeColor.Primary))
```

### Common styling extensions

```csharp
view.Background(color)          // background color or paint
view.Color(color)               // text/foreground color
view.FontSize(size)             // font size
view.FontWeight(FontWeight.Bold)
view.CornerRadius(radius)       // on Border or Button
view.Padding(thickness)
view.Frame(width, height)
view.Shadow(new Shadow { Brush = Colors.Black, Offset = new Point(2, 2), Radius = 4 })
```

## Important Rules

- **Always use lambda wrappers for Button actions**: `Button("Go", () => Method())` — never `Button("Go", Method)` (causes CS1503)
- **Reactive<T> is for display binding** — read `.Value` in lambdas passed to controls
- **Signal<T> is for two-way binding** — required for `TextField` and other input controls
- **Multiple .Value writes in the same sync block are batched** into a single UI update
- **Don't use XAML patterns** — no `BindingContext`, no `INotifyPropertyChanged`, no `{Binding}` markup
