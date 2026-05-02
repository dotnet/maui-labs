# Styling and Theming Guide

Comet provides a design token system, typed control styles, composable view
modifiers, and a theme manager that integrates with the MAUI application
theme. This guide covers each layer from tokens through theme switching.


## Design Tokens

Design tokens are named values that define a visual language: colors,
typography, spacing, and corner radii. In Comet, tokens are strongly typed
via `Token<T>` and resolved against the active theme at read time.

### Token<T>

A `Token<T>` is a typed environment key with an optional resolver function.
It can extract a value from a `Theme` instance or fall back to a default:

```csharp
using Comet.Styles;

// Create a custom color token with a default value
var brandAccent = new Token<Color>(
	key: "app.color.brandAccent",
	name: "Brand Accent",
	defaultValue: Color.FromArgb("#FF6200"));

// Create a token with a resolver that reads from a Theme
var headerColor = new Token<Color>(
	key: "app.color.header",
	resolver: theme => theme.Colors.Primary,
	name: "Header Color");
```

Tokens are resolved in two ways:

- `token.Resolve(theme)` -- extracts the value from a specific theme,
  falling back to `DefaultValue` if the resolver returns null.
- `token.Resolve(view)` -- resolves against the nearest scoped theme for the
  given view, supporting per-subtree overrides.


### Built-in Token Sets

Comet ships with Material Design 3 token sets covering four categories.

**ColorTokens** -- 30 semantic color roles following Material Design 3
naming. Each token resolves to a `Color`:

```csharp
using Comet.Styles;

// Primary palette (Primary, OnPrimary, PrimaryContainer, OnPrimaryContainer)
// Secondary palette (same pattern)
// Tertiary palette (same pattern)
// Error palette (Error, OnError, ErrorContainer, OnErrorContainer)
// Surface roles (Surface, OnSurface, SurfaceVariant, SurfaceContainer, etc.)
// Background (Background, OnBackground)
// Outline (Outline, OutlineVariant)
// Inverse (InverseSurface, InverseOnSurface, InversePrimary)

ColorTokens.Primary          // "theme.color.primary"
ColorTokens.OnPrimary        // "theme.color.onPrimary"
ColorTokens.Surface          // "theme.color.surface"
ColorTokens.OnSurface        // "theme.color.onSurface"
ColorTokens.Error            // "theme.color.error"
```

**TypographyTokens** -- 15 type scale entries (Material 3). Each resolves to
a `FontSpec`:

```csharp
TypographyTokens.DisplayLarge   // 57sp, Weight 400
TypographyTokens.HeadlineMedium // 28sp
TypographyTokens.TitleLarge     // 22sp
TypographyTokens.BodyMedium     // 14sp
TypographyTokens.LabelSmall     // 11sp
// Also: DisplayMedium/Small, HeadlineLarge/Small, TitleMedium/Small,
//        BodyLarge/Small, LabelLarge/Medium
```

Each typography token resolves to a `FontSpec` record:

```csharp
public readonly record struct FontSpec(
	double Size,
	FontWeight Weight,
	string Family = null,
	double LineHeight = 0,
	double LetterSpacing = 0);
```

**SpacingTokens** -- 6 values: `None` (0), `ExtraSmall` (4), `Small` (8),
`Medium` (16), `Large` (24), `ExtraLarge` (32).

**ShapeTokens** -- 7 corner radius values: `None` (0), `ExtraSmall` (4),
`Small` (8), `Medium` (12), `Large` (16), `ExtraLarge` (28), `Full` (9999
for pill shapes).

### Token Data Containers

Tokens are grouped into record types for bulk assignment on a `Theme`.
`ColorTokenSet` holds all 30 color values. `TypographyTokenSet` holds 15
`FontSpec` entries. `SpacingTokenSet` and `ShapeTokenSet` hold doubles.

```csharp
var customColors = new ColorTokenSet
{
	Primary = Color.FromArgb("#1B6B4E"),
	OnPrimary = Colors.White,
	Secondary = Color.FromArgb("#526350"),
	OnSecondary = Colors.White,
	Surface = Color.FromArgb("#F8FAF5"),
	OnSurface = Color.FromArgb("#1A1C19"),
	// ... remaining properties
};
```


## Themes

A `Theme` bundles token sets with control styles and legacy color properties.

### Theme Structure

```csharp
var theme = new Theme
{
	Name = "Forest",
	Colors = customColors,
	Typography = TypographyDefaults.Material3,
	Spacing = SpacingDefaults.Standard,
	Shapes = ShapeDefaults.Rounded,
	ColorScheme = new ThemeColors
	{
		Primary = Color.FromArgb("#1B6B4E"),
		OnPrimary = Colors.White,
		// ... semantic colors
	}
};
```

The `Theme` class carries both new token-based properties (`Colors`,
`Typography`, `Spacing`, `Shapes`) and legacy simple properties
(`PrimaryColor`, `BackgroundColor`, `TextColor`, etc.). The legacy properties
exist for backward compatibility and are resolved as a fallback when tokens
are not set.

### Built-in Themes

Comet provides two pre-configured themes:

```csharp
// Pre-built light theme with Material 3 defaults
Theme.Light
Defaults.Light  // Equivalent, includes full token sets

// Pre-built dark theme
Theme.Dark
Defaults.Dark
```

Both include complete `ColorTokenSet`, `TypographyTokenSet`, `SpacingTokenSet`,
and `ShapeTokenSet` values.


### Setting the Active Theme

Use `ThemeManager` to set the global theme:

```csharp
// Set the active theme globally
ThemeManager.SetTheme(Theme.Dark);

// Or use the legacy property (also triggers ThemeChanged event)
Theme.Current = Theme.Dark;
```

`ThemeManager.SetTheme()` pushes all token values into the global environment
and synchronizes with MAUI's `Application.UserAppTheme` so platform chrome
(status bar, navigation bar) matches.

Subscribe to theme changes:

```csharp
Theme.ThemeChanged += newTheme =>
{
	Console.WriteLine($"Theme switched to: {newTheme.Name}");
};
```


### Theme Switching (Light/Dark)

A common pattern for toggling light and dark mode:

```csharp
class SettingsView : View
{
	readonly Signal<bool> isDark = new(false);

	[Body]
	View body() => new VStack
	{
		new Toggle(() => isDark.Value)
			.OnToggled(dark =>
			{
				isDark.Value = dark;
				ThemeManager.SetTheme(dark ? Theme.Dark : Theme.Light);
			}),
		new Text("Dark Mode")
	};
}
```

The `AppTheme` enum (`Light`, `Dark`, `System`) on each theme instance
indicates the intended appearance. When set to `System`, the framework defers
to the platform's current appearance.


### Scoped Theme and Token Overrides

Apply a different theme to a subtree, or override individual tokens:

```csharp
// Scoped theme override
new VStack
{
	new Text("Dark Section").Color(Colors.White),
	new Button("Action", () => { })
}.UseTheme(Theme.Dark);

// Individual token overrides
new VStack
{
	new Button("Custom Primary", () => { })
}
.OverrideToken(ColorTokens.Primary, Color.FromArgb("#FF6200"))
.OverrideToken(ShapeTokens.Medium, 24d);
```

Token bindings create lazy resolvers that evaluate against the nearest theme:

```csharp
Func<Color> primaryColor = ThemeManager.TokenBinding(ColorTokens.Primary);
```


## Control Styles

### ControlStyle<T>

`ControlStyle<T>` is a typed builder that sets environment properties scoped
to a specific control type:

```csharp
using Comet.Styles;

var buttonStyle = new ControlStyle<Button>()
	.Set(EnvironmentKeys.Colors.Background, Colors.Blue)
	.Set(EnvironmentKeys.Colors.Color, Colors.White);

// Apply globally
buttonStyle.Apply();

// Or apply to a specific scope
buttonStyle.Apply(someContainerView);

// Register on a theme
theme.SetControlStyle(buttonStyle);
```

Key methods: `Set(key, value)` for environment properties, `Apply(target)`
to push to global or scoped environment, `Get<T>(key)` to read back,
`HasProperty(key)` to check, and `Remove(key)` to delete.

`ControlStyle<T>` instances are registered on a theme via
`theme.SetControlStyle(style)`. When the theme is applied, all registered
styles push their properties into the environment, scoped by control type.


### IControlStyle<TControl, TConfiguration>

For state-aware styling (pressed, hovered, disabled), implement
`IControlStyle<TControl, TConfiguration>`. The framework passes a
configuration struct containing the current control state:

```csharp
public class MyButtonStyle : IControlStyle<Button, ButtonConfiguration>
{
	public ViewModifier Resolve(ButtonConfiguration config)
	{
		Color bg = config.IsPressed
			? Colors.DarkBlue
			: config.IsHovered
				? Colors.MediumBlue
				: Colors.Blue;

		return new MyButtonAppearance(bg, config.IsEnabled);
	}
}
```

Configuration structs carry the view reference and state flags:

| Configuration | Properties |
|---------------|------------|
| `ButtonConfiguration` | `TargetView`, `IsPressed`, `IsHovered`, `IsEnabled`, `IsFocused`, `Label` |
| `ToggleConfiguration` | `TargetView`, `IsOn`, `IsEnabled`, `IsFocused` |
| `TextFieldConfiguration` | `TargetView`, `IsEditing`, `IsEnabled`, `IsFocused`, `Placeholder` |
| `SliderConfiguration` | `TargetView`, `Value`, `Minimum`, `Maximum`, `IsEnabled`, `IsDragging` |

The `TargetView` property allows styles to resolve tokens against the
nearest scoped theme for that specific view instance.


### Built-in Button Styles

Comet provides four Material 3 button styles in the `ButtonStyles` static
class:

**ButtonStyles.Filled** -- Solid primary background with rounded corners.
State-aware: pressed reduces alpha to 0.88, hovered to 0.92. Disabled uses
grey with 0.38 opacity.

```csharp
// The Filled style applies:
//   .Background(PrimaryColor)
//   .Color(OnPrimaryColor)
//   .ClipShape(new RoundedRectangle(20))
//   .Padding(new Thickness(24, 12))
```

**ButtonStyles.Outlined** -- Transparent background with a primary-colored
border. Uses `ColorTokens.Outline` for the border in the default state.

```csharp
// The Outlined style applies:
//   .Background(Colors.Transparent)
//   .Color(PrimaryColor)
//   .RoundedBorder(radius: 20, color: OutlineColor, strokeSize: 1)
//   .Padding(new Thickness(24, 12))
```

**ButtonStyles.Text** -- No background, no border, primary-colored text only.
Minimal padding (24x8 vs 24x12 for other variants).

**ButtonStyles.Elevated** -- Surface-colored background with a drop shadow.
Shadow radius varies by state: 2 (default), 4 (hovered), 1 (pressed), 0
(disabled).

```csharp
// The Elevated style applies:
//   .Background(SurfaceContainerColor)
//   .Color(PrimaryColor)
//   .ClipShape(new RoundedRectangle(20))
//   .Shadow(Colors.Black.WithAlpha(0.15f), radius: 2, x: 0, y: 1)
//   .Padding(new Thickness(24, 12))
```

All four styles resolve colors from `ColorTokens`, making them theme-aware.
Changing the active theme automatically updates button appearance.


### Default Theme Styles

When `Theme.Apply()` is called, `DefaultThemeStyles.Register()` registers
`ControlStyle<T>` entries for Button (Primary background), Text (OnSurface),
TextField (SurfaceVariant background, Outline border), Toggle (Primary
track), and Slider (Primary track). Default styles are skipped for any
control type that already has a custom style. Override defaults by
registering your style before calling `ThemeManager.SetTheme()`.


## View Modifiers

### Creating a ViewModifier

`ViewModifier` is the base class for reusable sets of view property changes.
Subclass it and implement `Apply()`:

```csharp
using Comet.Styles;

public class CardModifier : ViewModifier
{
	public override View Apply(View view) => view
		.Background(Colors.White)
		.ClipShape(new RoundedRectangle(12))
		.Shadow(Colors.Black.WithAlpha(0.1f), radius: 4, x: 0, y: 2)
		.Padding(new Thickness(16));
}
```

For type-specific modifiers, use the generic `ViewModifier<T>`:

```csharp
public class PrimaryButtonModifier : ViewModifier<Button>
{
	public override Button Apply(Button view) => view
		.Background(new SolidPaint(Colors.Blue))
		.Color(Colors.White)
		.ClipShape(new RoundedRectangle(20))
		.Padding(new Thickness(24, 12));
}
```

The typed variant adds a runtime type check -- if the modifier is applied to a
view of the wrong type, it returns the view unchanged.


### Applying Modifiers

Use `.Modifier()` to apply one or more modifiers to a view:

```csharp
var card = new CardModifier();
var primaryButton = new PrimaryButtonModifier();

new VStack { new Text("Content") }.Modifier(card);
new Button("Submit", () => { }).Modifier(primaryButton);

// Multiple modifiers in sequence
new VStack { ... }.Modifier(card, new DeepShadowModifier());
```


### Composing Modifiers with Then()

Chain modifiers into a single reusable unit with `Then()`:

```csharp
var cardWithShadow = new CardModifier()
	.Then(new DeepShadowModifier());

// Use the composed modifier
new VStack { ... }.Modifier(cardWithShadow);
```

`Then()` creates a `ComposedModifier` that applies the first modifier, then
the second. The composed modifier can itself be composed further.


### The Empty Modifier

`ViewModifier.Empty` is a no-op modifier that returns the view unchanged.
Use it as a default or sentinel value:

```csharp
ViewModifier GetModifier(bool isActive)
{
	if (isActive)
		return new ActiveModifier();
	return ViewModifier.Empty;
}
```


## Theme-Aware Views

### Using Theme Extensions

Apply semantic theme colors without hardcoding values:

```csharp
using Comet.Styles;

[Body]
View body() => new VStack
{
	new Text("Welcome")
		.ThemeForeground()                          // OnSurface color
		.Typography(TypographyTokens.HeadlineLarge), // Material 3 type

	new VStack
	{
		new Text("Card content").ThemeForeground()
	}
	.ThemeBackground()                              // Background color
	.ThemeColors(                                    // Both at once
		EnvironmentKeys.ThemeColor.Surface,
		EnvironmentKeys.ThemeColor.OnSurface)
};
```

Available theme extension methods: `ThemeBackground()`,
`ThemeBackground(string key)`, `ThemeForeground()`,
`ThemeForeground(string key)`, and `ThemeColors(string bgKey, string fgKey)`.


### Typography Extensions

Apply the full type scale from typography tokens:

```csharp
new Text("Display Title")
	.Typography(TypographyTokens.DisplayLarge);

new Text("Body text for reading")
	.Typography(TypographyTokens.BodyMedium);

new Text("Small label")
	.Typography(TypographyTokens.LabelSmall);
```

The `Typography()` extension applies `FontSize`, `FontWeight`, and
`FontFamily` from the `FontSpec` resolved by the token.


## Custom Environment Values for Theming

Define custom tokens for application-specific theme values:

```csharp
static readonly Token<Color> CardBackground = new(
	key: "app.card.background",
	defaultValue: Colors.White);

// Resolve against the active theme
var color = CardBackground.Resolve(ThemeManager.Current());

// Override for a subtree
new VStack { ... }.OverrideToken(CardBackground, Colors.LightGrey);
```


## Type-Targeted Styling

Apply styles scoped to a specific control type within a container. This uses
the environment system's type-targeted propagation:

```csharp
var theme = Defaults.Light;

// All Text views under this theme get OnSurface color
theme.SetControlStyle(new ControlStyle<Text>()
	.Set(EnvironmentKeys.Colors.Color, Colors.DarkSlateGray));

// All Buttons under this theme get a green background
theme.SetControlStyle(new ControlStyle<Button>()
	.Set(EnvironmentKeys.Colors.Background, Colors.Green)
	.Set(EnvironmentKeys.Colors.Color, Colors.White));

ThemeManager.SetTheme(theme);
```

Type-targeted styles cascade through the view tree. A `Text` view inside
a `VStack` inside a `ScrollView` still resolves the `Text`-scoped style.

For the full list of styleable controls, see the
[Control Catalog](controls.md).


## See Also

- [Control Catalog](controls.md) -- all controls that can be styled, including
  constructor parameters and fluent API methods.
- [Platform-Specific Guides](platform-guides.md) -- platform differences in
  theming behavior and native appearance integration.
- [Accessibility Guide](accessibility.md) -- color contrast requirements and
  accessible color token pairing for WCAG compliance.
