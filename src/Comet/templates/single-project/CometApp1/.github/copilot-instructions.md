# Comet — Copilot Instructions

This is a .NET MAUI app built with **Comet**, an MVU (Model-View-Update) framework.
All UI is written in declarative C# — no XAML, no view models, no binding markup.

> **Styling system status: Phase 1.** Tokens, `Theme` (record), `ViewModifier`, and
> `IControlStyle<TControl, TConfig>` are the canonical styling APIs. Phase 2 will
> wire runtime pressed/hovered/focused state transitions and add built-in styles
> for controls beyond `Button`. Do not invent APIs; everything below exists today.

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
    Text(() => $"Count: {count.Value}"),   // reading .Value in lambda = binding
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
- `.Alignment(Alignment.Center)` — child alignment inside a container
- `.Frame(width, height)` — fixed size
- `.Padding(thickness)` / `.Margin(thickness)`
- `.Center()`, `.Top()`, `.Bottom()`, `.Leading()`, `.Trailing()` — self alignment

### Navigation

```csharp
NavigationView(content).Title("Page Title")        // wrap content
NavigationView.Navigate(this, new DetailPage());   // push
NavigationView.Pop(this);                          // pop
```

## Styling and Theming

Comet uses a **token-based** theme system (Material 3 by default) with three
layers: **tokens** for design values, **`ViewModifier`** for reusable styles, and
**`IControlStyle<TControl, TConfig>`** for per-control-type theming.

### Tokens — design values

Tokens are strongly-typed static fields. Pass them directly to fluent setters
(the source generator emits `Token<T>` overloads on every themable property):

```csharp
using Comet.Styles;

Text("Welcome")
    .Color(ColorTokens.Primary)
    .Typography(TypographyTokens.TitleLarge)

VStack(content)
    .Background(ColorTokens.SurfaceContainer)
    .Padding(SpacingTokens.Medium)
```

Built-in token families (all under `Comet.Styles`):

- `ColorTokens` — `Primary`, `OnPrimary`, `PrimaryContainer`, `Secondary`, `Surface`,
  `OnSurface`, `SurfaceContainer`, `Background`, `OnBackground`, `Error`, `Outline`, …
- `TypographyTokens` — `DisplayLarge`..`DisplaySmall`, `HeadlineLarge`..`Small`,
  `TitleLarge`..`Small`, `BodyLarge`..`Small`, `LabelLarge`..`Small`
- `SpacingTokens` — `None`, `ExtraSmall`, `Small`, `Medium`, `Large`, `ExtraLarge`
- `ShapeTokens` — `None`, `ExtraSmall`, `Small`, `Medium`, `Large`, `ExtraLarge`, `Full`

Tokens resolve against the nearest scoped theme automatically — inside a
`.UseTheme(dark)` subtree, `ColorTokens.Primary` resolves to dark's primary.

### ViewModifier — reusable style bundles

Inherit `ViewModifier` (any view) or `ViewModifier<T>` (typed view). Cache a
static `Instance` singleton for zero-alloc reuse:

```csharp
public sealed class CardStyle : ViewModifier
{
    public static readonly CardStyle Instance = new();
    public override View Apply(View view) => view
        .Background(ColorTokens.SurfaceContainer)
        .ClipShape(new RoundedRectangle(12))
        .Padding(SpacingTokens.Medium);
}

public sealed class HeaderTextStyle : ViewModifier<Text>
{
    public static readonly HeaderTextStyle Instance = new();
    public override Text Apply(Text view) => view
        .Typography(TypographyTokens.TitleLarge)
        .Color(ColorTokens.OnSurface);
}

// Usage
VStack(
    Text("Card Title").Modifier(HeaderTextStyle.Instance),
    Text("Card body")
).Modifier(CardStyle.Instance)
```

### IControlStyle — per-control-type styling

Built-in styles live in `ButtonStyles`:

```csharp
Button("Save",  () => Save()).ButtonStyle(ButtonStyles.Filled)
Button("Cancel",() => Back()).ButtonStyle(ButtonStyles.Outlined)
Button("Learn", () => Help()).ButtonStyle(ButtonStyles.Text)
Button("Share", () => Share()).ButtonStyle(ButtonStyles.Elevated)
```

`.ButtonStyle(...)` on an ancestor container cascades to every descendant
`Button`. Set it on the button itself to override for that one button.

> Only `Button` has built-in styles today. `ToggleStyle`, `TextFieldStyle`,
> `SliderStyle` extension methods exist but there are no built-in implementations
> yet — those arrive in Phase 2.

### Defining a theme

`Theme` is an immutable `record`. Start from `Defaults.Light` or `Defaults.Dark`
and use a `with` expression to override just what you need:

```csharp
using Comet.Styles;
using Microsoft.Maui.Graphics;

public static class AppThemes
{
    public static readonly Theme Light = Defaults.Light with
    {
        Name = "Brand Light",
        Colors = Defaults.Light.Colors with
        {
            Primary          = Color.FromArgb("#0A6CF5"),
            OnPrimary        = Colors.White,
            PrimaryContainer = Color.FromArgb("#D8E6FF"),
        },
    };

    public static readonly Theme Dark = Defaults.Dark with
    {
        Name = "Brand Dark",
        Colors = Defaults.Dark.Colors with
        {
            Primary   = Color.FromArgb("#9EC2FF"),
            OnPrimary = Color.FromArgb("#002F6C"),
        },
    };
}
```

To change the default style of a control type in a theme, chain
`SetControlStyle` (it returns the theme for fluent use):

```csharp
public static readonly Theme Light = (Defaults.Light with { Name = "Brand" })
    .SetControlStyle<Button, ButtonConfiguration>(ButtonStyles.Outlined);
```

### Switching and scoping themes

```csharp
// Global — set in App() or MauiProgram. All views re-resolve tokens.
ThemeManager.SetTheme(AppThemes.Dark);

// Read the active theme for a view (honors scoped overrides).
var theme = ThemeManager.Current(view);

// Scoped override — applies only to this subtree.
VStack(settingsContent).UseTheme(AppThemes.Dark)
```

```csharp
public class App : CometApp
{
    public App()
    {
        ThemeManager.SetTheme(AppThemes.Light);
        Body = () => new MainPage();
    }
}
```

### Common fluent setters

```csharp
view.Background(color-or-Token)    // background
view.Color(color-or-Token)         // text/foreground
view.Typography(Token<FontSpec>)   // font from a typography token
view.FontSize(size) / .FontWeight(FontWeight.Bold)
view.CornerRadius(radius)          // on Border / Button
view.ClipShape(new RoundedRectangle(12))
view.Padding(thickness) / .Margin(thickness) / .Frame(w, h)
view.Shadow(new Shadow(2, 2, 4, Colors.Black.WithAlpha(0.15f)))
```

### Phase 1 gaps — do not demo these yet

- Pressed/Hovered/Focused state transitions are **not yet wired** to re-run
  `IControlStyle.Resolve()` at runtime. Only the `Default` and `Disabled`
  branches visibly differentiate today. Don't write demos whose whole point
  is hover/press feedback until Phase 2.
- Built-in styles exist for `Button` only.
- Style/theme transitions (animated swaps) are not implemented.

## Important Rules

- **Always use lambda wrappers for Button actions**: `Button("Go", () => Method())` — never `Button("Go", Method)` (causes CS1503).
- **`Reactive<T>` is for display binding** — read `.Value` in lambdas passed to controls.
- **`Signal<T>` is for two-way binding** — required for `TextField` and other input controls.
- **Multiple `.Value` writes in the same sync block are batched** into a single UI update.
- **No XAML patterns** — no `BindingContext`, no `INotifyPropertyChanged`, no `{Binding}` markup, no `ResourceDictionary`, no `VisualStateManager`.
- **No legacy style API** — `Theme.Current`, `ColorScheme`, `ThemeColors`, `.ThemeBackground()`, `ControlStyle<T>`, `Style<T>` were removed. Use tokens, `ViewModifier`, and `IControlStyle<TControl, TConfig>` instead.
