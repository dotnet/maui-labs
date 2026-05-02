# Styling & Theming API Comparison: Comet vs MauiReactor vs SwiftUI

*Author: Holden (Lead Architect) — For David Ortinau*
*Date: Generated from source code analysis*

---

## 1. Executive Summary

**Comet** takes an environment-dictionary approach to styling. Every visual property (color, font, margin, shadow) is stored as a key-value pair in a cascading `EnvironmentData` system. Fluent extension methods like `.Background()`, `.FontSize()`, `.Color()` write into this dictionary, and the handler layer reads from it. Theming is layered on top via `Theme`/`ThemeColors` (MD3 semantic tokens), `ControlStyle<T>` (typed per-control style dictionaries), `Style` (legacy global style object), and `Style<T>` (functional action-based styles). The system is powerful but has multiple overlapping abstractions.

**MauiReactor** uses a `Theme` base class with an `OnApply()` override pattern. Developers define per-control-type style lambdas (`LabelStyles.Default`, `ButtonStyles.Themes["key"]`) that chain MAUI property setters directly. Dark/light mode reads `Application.Current.UserAppTheme`. The `.ThemeKey("name")` extension applies named style variants. It's straightforward, well-documented, and convention-driven — but it's a thin layer over MAUI's own property system with no environment/cascading beyond what MAUI provides.

**SwiftUI** has the deepest styling architecture. `ViewModifier` is the universal composition primitive for reusable style bundles. Per-control style protocols (`ButtonStyle`, `ToggleStyle`, `LabelStyle`) provide type-safe, configuration-driven customization that propagates through the view hierarchy. `@Environment(\.colorScheme)` provides reactive dark/light mode awareness. Custom `EnvironmentKey`s enable app-wide theme token propagation. `PreferenceKey` handles upward data flow. The whole system is protocol-based, composable, and deeply integrated into the language.

---

## 2. Feature Matrix

| Feature | Comet | MauiReactor | SwiftUI |
|---------|-------|-------------|---------|
| **Fluent modifier API** | ✅ Extension methods (`.Background()`, `.FontSize()`) | ✅ Extension methods (`.BackgroundColor()`, `.FontSize()`) | ✅ Dot-syntax modifiers (`.background()`, `.font()`) |
| **Environment/cascading values** | ✅ `EnvironmentData` dictionary, cascading down tree | ❌ No custom cascade; MAUI property system only | ✅ `@Environment`, `EnvironmentKey`, full cascade |
| **Reactive bindings in styles** | ✅ `Binding<T>` and `Func<T>` overloads on modifiers | ⚠️ State-driven re-render only (MVU) | ✅ Native `@State`/`@Binding` integration |
| **Semantic color tokens (MD3)** | ✅ `ThemeColors` with 27 MD3 token properties | ❌ Manual color definitions | ⚠️ System tokens only; custom via EnvironmentKey |
| **Per-control type styling** | ✅ `ControlStyle<T>`, `Style<T>`, legacy `ButtonStyle` class | ✅ `ButtonStyles.Default`, `LabelStyles.Themes[key]` | ✅ Protocols: `ButtonStyle`, `ToggleStyle`, etc. |
| **Named style variants** | ✅ `StyleId` + environment lookup | ✅ `.ThemeKey("name")` | ✅ `.buttonStyle(MyCustomStyle())` |
| **Theme class** | ✅ `Theme` with `Theme.Current`, `.Apply()` | ✅ `Theme` base class with `OnApply()` | ⚠️ No built-in; custom via EnvironmentKey |
| **Built-in light/dark themes** | ✅ `Theme.Light`, `Theme.Dark` with MD3 colors | ⚠️ Manual in `OnApply()` via `UserAppTheme` | ✅ `@Environment(\.colorScheme)` |
| **Theme switching at runtime** | ✅ `Theme.Current = Theme.Dark` + `ThemeChanged` event | ✅ `Application.Current.UserAppTheme = .Dark` | ✅ `.preferredColorScheme(.dark)` |
| **Scoped theme override** | ✅ `view.ApplyTheme(localTheme)` | ❌ Global only | ✅ `.environment(\.theme, myTheme)` |
| **Control state styling** | ✅ `StyleAwareValue<ControlState, T>` (Default/Hovered/Pressed/Disabled) | ✅ `.VisualState("CommonStates", "Disabled", prop, value)` | ✅ `ButtonStyle.Configuration.isPressed` |
| **Visual State Manager** | ✅ `VisualStateManager.GoToState()` with Setter-based states | ⚠️ Via MAUI's VisualStateManager property | ✅ Implicit via style configuration |
| **Implicit styles (auto-apply by type)** | ✅ `Style<T>.RegisterImplicit()` | ✅ `LabelStyles.Default = ...` in `OnApply()` | ⚠️ Via environment propagation only |
| **Resource dictionary** | ✅ `ResourceDictionary` with `MergedDictionaries`, typed `Get<T>()` | ❌ Uses MAUI's `ResourceDictionary` directly | ❌ No equivalent; environment replaces it |
| **Material Design styles** | ✅ `MaterialStyle` with `ColorPalette`, Outlined/Contained/Text button variants | ❌ Manual | ❌ Not applicable (Apple design) |
| **AppTheme-aware values** | ✅ `AppThemeValue.Get(light, dark)` | ✅ Conditional in `OnApply()` via `IsDarkTheme` | ✅ Native `@Environment(\.colorScheme)` |
| **Shadow** | ✅ `.Shadow(color, radius, x, y)` | ✅ `.HasShadow(true)` on Frame | ✅ `.shadow(color:radius:x:y:)` |
| **Border / Rounded corners** | ✅ `.RoundedBorder(radius, color, strokeSize)`, `.ClipShape()`, `.Border()` | ✅ `.CornerRadius()`, `.BorderWidth()` | ✅ `.clipShape()`, `.overlay(RoundedRectangle(...))` |
| **Animations** | ✅ `.Rotation()`, `.Scale()`, `.TranslationX/Y()` (no implicit animation) | ⚠️ MAUI animation APIs | ✅ `.animation()`, `withAnimation {}`, implicit transitions |
| **Style composition** | ✅ Chain multiple `.StyleApply()` calls | ✅ Chain lambdas | ✅ `.modifier()` composition, `ViewModifier` concat |
| **Preference keys (upward flow)** | ❌ Not implemented | ❌ Not implemented | ✅ `PreferenceKey` protocol |
| **Design token abstraction** | ✅ `EnvironmentKeys.ThemeColor.*` (27 MD3 tokens) | ❌ Manual constants | ⚠️ Custom `EnvironmentKey` structs |
| **Data triggers** | ✅ `DataTrigger` with `Attach()`/`Detach()` | ⚠️ Via MAUI triggers | ❌ Use `onChange` + state |
| **Behaviors** | ✅ `Behavior` with `Attach()`/`Detach()` via `.AddBehavior()` | ⚠️ Via MAUI behaviors | ❌ Use `ViewModifier` |

---

## 3. Detailed Comparisons

### 3a. Setting Colors on Controls

**Comet:**
```csharp
// Foreground (text) color
Text("Hello")
    .Color(Colors.Blue)

// Background color
Text("Hello")
    .Background(Colors.LightGray)

// Background with hex string
Text("Hello")
    .Background("#FF5733")

// Background with Paint
Text("Hello")
    .Background(new SolidPaint(Colors.Blue))

// Theme-aware color from semantic tokens
Text("Hello")
    .ThemeForeground(EnvironmentKeys.ThemeColor.OnSurface)
    .ThemeBackground(EnvironmentKeys.ThemeColor.Surface)

// Both background + foreground from theme
Button("OK")
    .ThemeColors(EnvironmentKeys.ThemeColor.Primary, EnvironmentKeys.ThemeColor.OnPrimary)

// Using ThemeColor selector function
Text("Hello")
    .ThemeColor(theme => theme.PrimaryColor)
    .ThemeTextColor(theme => theme.TextColor)
```

**MauiReactor:**
```csharp
Label("Hello")
    .TextColor(Colors.Blue)

Label("Hello")
    .BackgroundColor(Colors.LightGray)

// Theme-aware in OnApply()
LabelStyles.Default = _ => _
    .TextColor(AppTheme.IsDarkTheme ? Colors.White : Colors.Black);
```

**SwiftUI:**
```swift
Text("Hello")
    .foregroundStyle(.blue)

Text("Hello")
    .background(.gray.opacity(0.2))

// Theme-aware
Text("Hello")
    .foregroundStyle(colorScheme == .dark ? .white : .black)
```

**Analysis:** Comet has the richest color API with multiple overloads (Color, string hex, Paint, Binding\<Color\>, Func\<Color\>) plus dedicated theme-aware extensions. MauiReactor is the simplest — direct property setters. SwiftUI uses `foregroundStyle` (not `foregroundColor` in modern usage) with native color scheme integration.

---

### 3b. Typography / Font Styling

**Comet:**
```csharp
Text("Title")
    .FontSize(24)
    .FontWeight(FontWeight.Bold)
    .FontFamily("Helvetica")
    .FontSlant(FontSlant.Italic)

// Predefined text styles via StyleId
Text("Heading").StyleAsH1()
Text("Body").StyleAsBody1()
Text("Small").StyleAsCaption()

// Font cascade — set once, applies to children
VStack(
    Text("Child 1"),
    Text("Child 2")
).FontSize(16).FontWeight(FontWeight.Medium)
```

**MauiReactor:**
```csharp
Label("Title")
    .FontSize(24)
    .FontAttributes(FontAttributes.Bold)
    .FontFamily("Helvetica")

// Via theme
LabelStyles.Themes[AppTheme.Title] = _ => _
    .FontSize(24)
    .FontAttributes(FontAttributes.Bold)
    .TextColor(AppTheme.Primary);

Label("Title").ThemeKey(AppTheme.Title)
```

**SwiftUI:**
```swift
Text("Title")
    .font(.title)
    .fontWeight(.bold)
    .italic()

// System semantic fonts
Text("Body").font(.body)
Text("Caption").font(.caption)
Text("Title").font(.largeTitle)

// Custom font
Text("Custom").font(.custom("Helvetica", size: 24))
```

**Analysis:** Comet's `.StyleAsH1()` through `.StyleAsOverline()` map to the Material type scale (H1–H6, Subtitle1/2, Body1/2, Caption, Overline) defined in `Style.cs` with configurable `TextStyle` objects. SwiftUI has built-in semantic font tokens (`.title`, `.body`, `.caption`) that are the gold standard. MauiReactor relies entirely on manual font property setting.

---

### 3c. Layout Spacing (Margin, Padding)

**Comet:**
```csharp
// Margin with uniform value
Text("Hello").Margin(10)

// Margin with individual sides
Text("Hello").Margin(left: 5, top: 10, right: 5, bottom: 10)

// Margin with Thickness
Text("Hello").Margin(new Thickness(5, 10, 5, 10))

// Default margin (10dp)
Text("Hello").Margin()

// Padding
VStack(...).Padding(new Thickness(16))

// Frame constraints
Text("Hello").Frame(width: 200, height: 50)
```

**MauiReactor:**
```csharp
Label("Hello")
    .Margin(10)
    .Padding(5)

// Individual sides
Label("Hello")
    .Margin(5, 10, 5, 10)
```

**SwiftUI:**
```swift
Text("Hello")
    .padding(10)
    .padding(.horizontal, 16)

// Individual edges
Text("Hello")
    .padding(EdgeInsets(top: 10, leading: 5, bottom: 10, trailing: 5))

// Frame
Text("Hello")
    .frame(width: 200, height: 50)
```

**Analysis:** All three are functionally equivalent. Comet stores margin/padding in the environment system (`EnvironmentKeys.Layout.Margin/Padding`), enabling cascading. SwiftUI's `.padding()` also cascades via view hierarchy. MauiReactor sets MAUI properties directly.

---

### 3d. Borders, Shadows, Rounded Corners

**Comet:**
```csharp
// Rounded border with stroke
Text("Card")
    .RoundedBorder(radius: 12, color: Colors.Grey, strokeSize: 1)

// Separate border + clip
Text("Card")
    .ClipShape(new RoundedRectangle(12))
    .Border(new RoundedRectangle(12).Stroke(Colors.Grey, 1))

// Shadow
Text("Card")
    .Shadow(Colors.Black, radius: 4, x: 0, y: 2)

// Shadow with Paint
Text("Card")
    .Shadow(paint: new SolidPaint(Colors.Grey), radius: 8, x: 2, y: 4)

// Border control with StrokeColor and StrokeThickness
Border( Text("Content") )
    .CornerRadius(8)
    .StrokeColor(Colors.Blue)
    .StrokeThickness(2)
```

**MauiReactor:**
```csharp
Frame(
    Label("Card")
)
.CornerRadius(12)
.HasShadow(true)
.BackgroundColor(Colors.White)
.Padding(16)

// Or Border control
Border(
    Label("Content")
)
.StrokeShape(new RoundedRectangle().CornerRadius(8))
.Stroke(Colors.Blue)
.StrokeThickness(2)
```

**SwiftUI:**
```swift
Text("Card")
    .padding()
    .background(
        RoundedRectangle(cornerRadius: 12)
            .fill(Color.white)
            .shadow(color: .black.opacity(0.2), radius: 4, x: 0, y: 2)
    )
    .overlay(
        RoundedRectangle(cornerRadius: 12)
            .stroke(Color.gray, lineWidth: 1)
    )

// Or simplified
Text("Card")
    .clipShape(RoundedRectangle(cornerRadius: 12))
    .shadow(radius: 4)
```

**Analysis:** Comet's `.RoundedBorder()` convenience is nice but conflates clip shape and border into one call. The separate `.ClipShape()` + `.Border()` API mirrors SwiftUI's composable approach. The `Shadow` class supports `WithPaint()`, `WithRadius()`, `WithOffset()` builder methods internally.

---

### 3e. Creating Reusable Styles

**Comet:**
```csharp
// Style<T> — functional action-based style
var headerStyle = new Style<Text>(t => t
    .FontSize(24)
    .FontWeight(FontWeight.Bold)
    .Color(Colors.White)
    .Background(Colors.DarkBlue)
);

// Apply explicitly
Text("Title").StyleApply(headerStyle);

// Register as implicit (auto-applies to all Text views)
headerStyle.RegisterImplicit();

// ControlStyle<T> — environment dictionary style
var buttonStyle = new ControlStyle<Button>()
    .Set(EnvironmentKeys.Colors.Background, new SolidPaint(Colors.Blue))
    .Set(EnvironmentKeys.Colors.Color, Colors.White)
    .Set(EnvironmentKeys.Button.CornerRadius, 8);

// ResourceDictionary-based styles
var resources = new ResourceDictionary {
    ["HeaderStyle"] = headerStyle,
    ["CardBackground"] = Colors.White,
};
view.Resources = resources;
var style = view.StaticResource<Style<Text>>("HeaderStyle");
```

**MauiReactor:**
```csharp
// Default style (implicit — applies to all Labels)
LabelStyles.Default = _ => _
    .FontSize(14)
    .TextColor(Colors.Black);

// Named theme variant
LabelStyles.Themes["Title"] = _ => _
    .FontSize(24)
    .FontAttributes(FontAttributes.Bold);

// Apply
Label("Welcome").ThemeKey("Title");
```

**SwiftUI:**
```swift
// ViewModifier (universal reusable style)
struct CardStyle: ViewModifier {
    func body(content: Content) -> some View {
        content
            .padding()
            .background(Color.white)
            .cornerRadius(12)
            .shadow(radius: 4)
    }
}

extension View {
    func cardStyle() -> some View {
        modifier(CardStyle())
    }
}

// Usage
Text("Hello").cardStyle()
```

**Analysis:** All three have viable reuse patterns. Comet's dual `Style<T>` (action-based) and `ControlStyle<T>` (dictionary-based) approaches serve different needs but create API confusion. MauiReactor's lambda-based approach is the simplest to understand. SwiftUI's `ViewModifier` is the most composable and type-safe.

---

### 3f. Per-Control Type Styling

**Comet:**
```csharp
// Via ControlStyle<T> on Theme
var theme = new Theme();
theme.SetControlStyle(new ControlStyle<Button>()
    .Set(EnvironmentKeys.Colors.Background, new SolidPaint(Colors.Blue))
    .Set(EnvironmentKeys.Colors.Color, Colors.White));

theme.SetControlStyle(new ControlStyle<Text>()
    .Set(EnvironmentKeys.Colors.Color, Colors.DarkGray));

Theme.Current = theme; // Auto-applies all control styles

// Via legacy Style class
var style = new Style();
style.Button = new ButtonStyle {
    TextColor = Colors.White,
    BackgroundColor = Colors.Blue,
    Border = new RoundedRectangle(4).Stroke(Colors.Grey, 1),
    Shadow = new Shadow().WithColor(Colors.Grey).WithRadius(1),
    Padding = new Thickness(16, 0),
};

// Material button variants
Button("Outlined").StyleAsOutlined()
Button("Contained").StyleAsContained()
Button("Text").StyleAsText()
```

**MauiReactor:**
```csharp
// Per-control type defaults in OnApply()
ButtonStyles.Default = _ => _
    .BackgroundColor(Primary)
    .TextColor(White)
    .CornerRadius(8);

LabelStyles.Default = _ => _
    .TextColor(Black)
    .FontSize(14);

EntryStyles.Default = _ => _
    .BackgroundColor(Gray100)
    .TextColor(Black)
    .PlaceholderColor(Gray500);
```

**SwiftUI:**
```swift
// Custom ButtonStyle protocol
struct PrimaryButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .padding(.horizontal, 16)
            .padding(.vertical, 10)
            .background(Color.blue)
            .foregroundStyle(.white)
            .cornerRadius(8)
            .scaleEffect(configuration.isPressed ? 0.95 : 1.0)
    }
}

Button("Action") { }.buttonStyle(PrimaryButtonStyle())

// Propagates to all child buttons
VStack { ... }.buttonStyle(PrimaryButtonStyle())
```

**Analysis:** SwiftUI's protocol-based approach is the most powerful — `ButtonStyle.Configuration` gives access to press state, enabling interactive styling. MauiReactor's `ButtonStyles.Default` pattern is clean and convention-driven. Comet has three overlapping mechanisms: `ControlStyle<T>`, legacy `ButtonStyle` class, and `MaterialStyle` extensions.

---

### 3g. Theme Definition (Light/Dark Palettes)

**Comet:**
```csharp
// Built-in themes with MD3 semantic tokens
var light = Theme.Light;
// Includes: CurrentTheme=Light, BackgroundColor=White, TextColor=Black
// Plus ThemeColors.LightScheme with 27 MD3 tokens:
//   Primary=#512BD4, OnPrimary=White, PrimaryContainer=#EADDFF, ...

var dark = Theme.Dark;
// Includes: CurrentTheme=Dark, BackgroundColor=#121212, TextColor=White
// Plus ThemeColors.DarkScheme with matching dark tokens

// Custom theme
var custom = new Theme {
    CurrentTheme = AppTheme.Light,
    ColorScheme = new ThemeColors {
        Primary = Color.FromArgb("#006D77"),
        OnPrimary = Colors.White,
        Secondary = Color.FromArgb("#83C5BE"),
        OnSecondary = Colors.Black,
        Background = Color.FromArgb("#EDF6F9"),
        OnBackground = Color.FromArgb("#1C1B1F"),
        Surface = Color.FromArgb("#FFDDD2"),
        OnSurface = Color.FromArgb("#1C1B1F"),
        Error = Color.FromArgb("#B3261E"),
        OnError = Colors.White,
        // ... all 27 tokens
    }
};
custom.SetControlStyle(new ControlStyle<Button>()
    .Set(EnvironmentKeys.Colors.Background, new SolidPaint(Color.FromArgb("#006D77")))
    .Set(EnvironmentKeys.Colors.Color, Colors.White));
```

**MauiReactor:**
```csharp
class AppTheme : Theme
{
    public static Color Primary = Color.FromArgb("#512BD4");
    public static Color PrimaryDark = Color.FromArgb("#ac99ea");
    public static Color White = Color.FromArgb("White");
    public static Color Black = Color.FromArgb("Black");
    public static Color OffBlack = Color.FromArgb("#1f1f1f");
    public static Color Gray100 = Color.FromArgb("#E1E1E1");

    static bool LightTheme =>
        Application.Current?.UserAppTheme == AppTheme.Light;
    public static bool IsDarkTheme => !LightTheme;

    protected override void OnApply()
    {
        ContentPageStyles.Default = _ => _
            .BackgroundColor(IsDarkTheme ? OffBlack : White);

        LabelStyles.Default = _ => _
            .TextColor(LightTheme ? Black : White);

        ButtonStyles.Default = _ => _
            .BackgroundColor(LightTheme ? Primary : PrimaryDark)
            .TextColor(LightTheme ? White : Black);
    }
}
```

**SwiftUI:**
```swift
// Define theme tokens
struct AppTheme {
    let primary: Color
    let onPrimary: Color
    let background: Color
    let surface: Color
    let text: Color
}

extension AppTheme {
    static let light = AppTheme(
        primary: Color(hex: "#512BD4"),
        onPrimary: .white,
        background: .white,
        surface: Color(hex: "#F5F5F5"),
        text: .black
    )
    static let dark = AppTheme(
        primary: Color(hex: "#D0BCFF"),
        onPrimary: Color(hex: "#381E72"),
        background: Color(hex: "#1C1B1F"),
        surface: Color(hex: "#2C2C2C"),
        text: .white
    )
}

// Inject via Environment
struct ThemeKey: EnvironmentKey {
    static let defaultValue = AppTheme.light
}
extension EnvironmentValues {
    var theme: AppTheme {
        get { self[ThemeKey.self] }
        set { self[ThemeKey.self] = newValue }
    }
}
```

**Analysis:** Comet has the most structured approach with `ThemeColors` mapping to the full MD3 specification (27 semantic tokens). MauiReactor keeps it simple with static color properties and conditional logic in `OnApply()`. SwiftUI requires manual setup but gets native environment propagation in return.

---

### 3h. Theme Switching (Dark Mode Toggle)

**Comet:**
```csharp
// Switch globally — triggers ThemeChanged event and re-applies all styles
Theme.Current = Theme.Dark;

// AppThemeValue for reactive light/dark values
var bg = AppThemeValue.Get(light: Colors.White, dark: Colors.Black);

// Listen for changes
Theme.ThemeChanged += (newTheme) => {
    // Update UI
};
AppThemeValue.ThemeChanged += () => {
    // Re-evaluate AppThemeValue.Get() calls
};

// System theme detection
var theme = new Theme { CurrentTheme = AppTheme.System };
// AppThemeValue.Get() reads AppInfo.Current.RequestedTheme when System
```

**MauiReactor:**
```csharp
// Toggle via MAUI's Application object
public static void ToggleCurrentAppTheme()
{
    Application.Current.UserAppTheme = IsDarkTheme
        ? AppTheme.Light
        : AppTheme.Dark;
}

// In a component
Button("Toggle Theme", AppTheme.ToggleCurrentAppTheme)
```

**SwiftUI:**
```swift
// Override color scheme on a subtree
ContentView()
    .preferredColorScheme(.dark)

// Read current scheme reactively
@Environment(\.colorScheme) var colorScheme

// Toggle
@State private var isDark = false
Toggle("Dark Mode", isOn: $isDark)
    .onChange(of: isDark) { newValue in
        // App-level override
    }
```

**Analysis:** Comet's `Theme.Current` setter triggers `Apply()` which pushes all values through the environment system and notifies `IThemeable` views. This is the most complete approach — a single assignment propagates everything. MauiReactor delegates to MAUI's own `UserAppTheme`. SwiftUI's `.preferredColorScheme()` is elegant but scoped to view subtrees.

---

### 3i. Theme Propagation (How Theme Flows Through the View Tree)

**Comet:**
```
Theme.Current = myTheme
    → Theme.Apply(target: null)
        → ThemeColors.ApplyToEnvironment()
            → View.SetGlobalEnvironment(key, value) for each of 27 MD3 tokens
        → DefaultThemeStyles.Register(theme)
            → ControlStyle<Button/Text/TextField/Toggle/Slider>.Apply()
                → View.SetGlobalEnvironment(typeof(T), key, value)
        → Per-control ControlStyle<T>.Apply()
            → View.SetGlobalEnvironment(typeof(T), key, value)
        → IThemeable.ApplyTheme() on all active views

// Scoped: theme applied to a subtree
view.ApplyTheme(localTheme)
    → Theme.Apply(target: view)
        → view.SetEnvironment(key, value) // scoped, not global
```

**Key mechanism:** `EnvironmentData` is a `BindingObject` attached to each `View`. When a view reads a key, it walks up the parent chain if the local dictionary doesn't have it. Global environment (`View.SetGlobalEnvironment`) is the fallback root. Changes trigger `StateManager.OnPropertyChanged()` which invalidates and re-renders affected views.

**MauiReactor:**
```
builder.UseMauiReactorApp<MainPage>(app => app.UseTheme<AppTheme>())
    → AppTheme.OnApply() called on startup and theme changes
        → Sets LabelStyles.Default, ButtonStyles.Default, etc.
        → Each component's Render() picks up current styles
        → .ThemeKey("name") reads from Themes dictionary

// No cascading — styles are global singletons
// Component re-renders read fresh style values
```

**SwiftUI:**
```
// Environment propagation — automatic cascade
ContentView()
    .environment(\.theme, AppTheme.dark)
    // Every child view with @Environment(\.theme) gets dark theme
    // Children can override: .environment(\.theme, .light)

// Style propagation
VStack { ... }
    .buttonStyle(PrimaryButtonStyle())
    // All Buttons in this subtree use PrimaryButtonStyle
```

**Analysis:** Comet and SwiftUI both have genuine cascade mechanisms. Comet's is dictionary-key based (string keys in `EnvironmentData`), while SwiftUI's is type-safe (protocol-based `EnvironmentKey`). MauiReactor has no cascading — it's global singletons only. **This is a significant architectural difference.**

---

### 3j. Conditional Styling (Based on State)

**Comet:**
```csharp
readonly State<bool> isActive = false;

[Body]
View body() => VStack(
    // Reactive binding — auto-updates when state changes
    Text("Status")
        .Color(() => isActive.Value ? Colors.Green : Colors.Red)
        .FontWeight(() => isActive.Value ? FontWeight.Bold : FontWeight.Regular),

    // AppThemeValue for light/dark conditional
    Text("Themed")
        .Background(AppThemeValue.Get(light: Colors.White, dark: Colors.Black)),

    // ControlState-aware styling (in legacy Style system)
    // ButtonStyle supports Default, Hovered, Pressed, Disabled states
);
```

**MauiReactor:**
```csharp
class MyPage : Component
{
    bool _isActive;

    public override VisualNode Render()
        => VStack(
            Label("Status")
                .TextColor(_isActive ? Colors.Green : Colors.Red)
                .FontAttributes(_isActive ? FontAttributes.Bold : FontAttributes.None),

            // Conditional rendering
            Label("Active!").When(_isActive)
        );
}
```

**SwiftUI:**
```swift
@State private var isActive = false

var body: some View {
    Text("Status")
        .foregroundStyle(isActive ? .green : .red)
        .fontWeight(isActive ? .bold : .regular)
        .animation(.easeInOut, value: isActive) // Animate the change!
}
```

**Analysis:** Comet's `Func<T>` overloads (`() => condition ? a : b`) enable reactive styling that re-evaluates when state changes — this is a unique strength over MauiReactor's plain ternary approach (which requires full re-render). SwiftUI matches Comet's reactivity via `@State` and adds implicit animation.

---

### 3k. Animation of Style Changes

**Comet:**
```csharp
// Transform properties (no implicit animation — must be driven externally)
Text("Hello")
    .Rotation(45)
    .Scale(1.2)
    .TranslationX(50)
    .TranslationY(-20)
    .Opacity(0.5)
    .AnchorX(0.5)
    .AnchorY(0.5)

// No built-in animated style transitions.
// Animation must be handled imperatively via MAUI's animation API
// or by updating State<T> values over time.
```

**MauiReactor:**
```csharp
// MAUI animation APIs available
Label("Hello")
    .TranslationX(isAnimating ? 100 : 0)
    .WithAnimation(duration: 300)
```

**SwiftUI:**
```swift
Text("Hello")
    .scaleEffect(isExpanded ? 1.5 : 1.0)
    .opacity(isVisible ? 1.0 : 0.0)
    .animation(.spring(), value: isExpanded)

// Or explicit
withAnimation(.easeInOut(duration: 0.3)) {
    isExpanded.toggle()
}
```

**Analysis:** **This is Comet's biggest gap.** SwiftUI's implicit animation system (`.animation()` modifier that automatically animates any changed property) is its killer feature. Comet has the transform properties (`.Rotation()`, `.Scale()`, `.Opacity()`, etc.) but no declarative way to animate transitions between style states. MauiReactor is in a similar position.

---

### 3l. Platform-Adaptive Styling

**Comet:**
```csharp
// Via MAUI's DeviceInfo at runtime
Text("Hello")
    .FontSize(DeviceInfo.Platform == DevicePlatform.iOS ? 17 : 14)

// Via platform-specific files (Directory.Build.targets convention)
// MyView.iOS.cs, MyView.Android.cs, MyView.Windows.cs

// No built-in adaptive style system — manual conditional logic
```

**MauiReactor:**
```csharp
Label("Hello")
    .FontSize(DeviceInfo.Platform == DevicePlatform.iOS ? 17 : 14)
    .OnPlatform(ios: _ => _.FontSize(17), android: _ => _.FontSize(14))
```

**SwiftUI:**
```swift
// Automatic — system fonts and controls adapt to platform
Text("Hello")
    .font(.body) // Automatically correct size per platform

// Explicit
#if os(iOS)
Text("Hello").font(.system(size: 17))
#elseif os(macOS)
Text("Hello").font(.system(size: 13))
#endif
```

**Analysis:** SwiftUI wins by default — the entire design language is platform-native. Comet and MauiReactor both require manual platform checks. MauiReactor has a cleaner `.OnPlatform()` helper.

---

### 3m. Design Token Patterns

**Comet:**
```csharp
// 27 MD3 semantic tokens in EnvironmentKeys.ThemeColor
EnvironmentKeys.ThemeColor.Primary          // "Theme.Primary"
EnvironmentKeys.ThemeColor.OnPrimary        // "Theme.OnPrimary"
EnvironmentKeys.ThemeColor.PrimaryContainer // "Theme.PrimaryContainer"
EnvironmentKeys.ThemeColor.Secondary        // "Theme.Secondary"
EnvironmentKeys.ThemeColor.Surface          // "Theme.Surface"
EnvironmentKeys.ThemeColor.Error            // "Theme.Error"
// ... 21 more

// Material Design ColorPalette (50-900 + A100-A700)
ColorPalette.Blue.P500  // Primary blue
ColorPalette.Blue.PD500 // On-primary for blue-500

// Text style tokens
EnvironmentKeys.Text.Style.H1       // H1-H6
EnvironmentKeys.Text.Style.Body1    // Body1, Body2
EnvironmentKeys.Text.Style.Caption  // Caption, Overline

// Sample-level design tokens (CometBaristaNotes)
Theme.Primary        // Color.FromArgb("#86543F")
Theme.SpacingM       // 16
Theme.RadiusCard     // 12f
Theme.FormFieldHeight // 50f
```

**MauiReactor:**
```csharp
// Manual static color constants — no formal token system
class AppTheme : Theme {
    public static Color Primary = Color.FromArgb("#512BD4");
    public static Color PrimaryDark = Color.FromArgb("#ac99ea");
    public static Color Gray100 = Color.FromArgb("#E1E1E1");
    // No spacing/radius/typography tokens built-in
}
```

**SwiftUI:**
```swift
// Custom token struct via EnvironmentKey
struct DesignTokens {
    struct Colors {
        let primary: Color
        let onPrimary: Color
        let surface: Color
        let onSurface: Color
    }
    struct Spacing {
        let xs: CGFloat = 4
        let sm: CGFloat = 8
        let md: CGFloat = 16
        let lg: CGFloat = 24
    }
    let colors: Colors
    let spacing: Spacing
}

// Injected via @Environment
@Environment(\.designTokens) var tokens

Text("Hello")
    .foregroundStyle(tokens.colors.primary)
    .padding(tokens.spacing.md)
```

**Analysis:** Comet has the most comprehensive built-in token system with 27 MD3 color tokens, Material ColorPalettes (16 colors × 15 palettes), and text style tokens. The CometBaristaNotes sample shows how apps add domain-specific tokens (spacing, radii, sizes). SwiftUI requires building your own token system but gets type safety. MauiReactor has no formal token abstraction.

---

### 3n. Style Composition / Combining Styles

**Comet:**
```csharp
// Chain multiple Style<T> applications
var baseStyle = new Style<Text>(t => t
    .FontSize(16)
    .FontFamily("Manrope"));

var headerStyle = new Style<Text>(t => t
    .FontWeight(FontWeight.Bold)
    .Color(Colors.DarkBlue));

Text("Title")
    .StyleApply(baseStyle)
    .StyleApply(headerStyle); // Both applied, later wins on conflicts

// Apply legacy Style to a view subtree
VStack(...).ApplyStyle(myGlobalStyle);

// Combine ControlStyle + explicit overrides (explicit always wins)
// ControlStyle<Button> sets Background=Blue via environment
// .Background(Colors.Red) on instance writes to same key → wins
```

**MauiReactor:**
```csharp
// Lambda composition — combine multiple theme keys manually
// No built-in composition; apply one ThemeKey per control
Label("Title")
    .ThemeKey(AppTheme.Title)
    // Cannot stack multiple ThemeKeys

// Manual composition via extension
static VisualNode WithCardStyle(this VisualNode node)
    => node.Margin(8).Padding(16);
```

**SwiftUI:**
```swift
// ViewModifier composition
struct BaseTextStyle: ViewModifier {
    func body(content: Content) -> some View {
        content.font(.body).foregroundStyle(.primary)
    }
}

struct HeaderModifier: ViewModifier {
    func body(content: Content) -> some View {
        content.font(.title).bold()
    }
}

// Combine via concat
Text("Title")
    .modifier(BaseTextStyle())
    .modifier(HeaderModifier()) // Last modifier wins on conflicts

// Or compose modifiers themselves
extension ViewModifier {
    func concat<T: ViewModifier>(_ modifier: T) -> some ViewModifier {
        ConcatModifier(first: self, second: modifier)
    }
}
```

**Analysis:** Comet allows stacking `Style<T>` applications with natural override semantics (later writes win in the environment dictionary). SwiftUI's modifier composition is the most principled. MauiReactor's single-ThemeKey limitation is a real constraint.

---

## 4. Architecture Deep Dive

### Comet Styling Engine

```
┌──────────────────────────────────────────────────────────────┐
│                    Theme.Current (singleton)                  │
│  ┌─────────────┐  ┌──────────────────┐  ┌────────────────┐  │
│  │ ThemeColors  │  │ ControlStyle<T>  │  │ Legacy Style   │  │
│  │ (27 MD3      │  │ (per-type        │  │ (ButtonStyle,  │  │
│  │  tokens)     │  │  env dict)       │  │  TextStyle,    │  │
│  └──────┬───────┘  └────────┬─────────┘  │  NavbarStyle)  │  │
│         │                   │            └───────┬────────┘  │
│         ▼                   ▼                    ▼           │
│  ┌──────────────────────────────────────────────────────┐    │
│  │         View.SetGlobalEnvironment(key, value)         │    │
│  └──────────────────────────┬───────────────────────────┘    │
└─────────────────────────────┼────────────────────────────────┘
                              ▼
┌──────────────────────────────────────────────────────────────┐
│              Global EnvironmentData (static)                  │
│         dictionary<string, object> — fallback root            │
└──────────────────────────────┬───────────────────────────────┘
                              ▼
┌──────────────────────────────────────────────────────────────┐
│           Per-View EnvironmentData (instance)                 │
│    ┌──────────────────────────────────────────────────┐      │
│    │ view.SetEnvironment(key, value, cascades)        │      │
│    │   → Writes to local dictionary                   │      │
│    │   → StateManager.OnPropertyChanged() if cascading│      │
│    └──────────────────────────────────────────────────┘      │
│                                                              │
│    ┌──────────────────────────────────────────────────┐      │
│    │ view.GetEnvironment<T>(key)                      │      │
│    │   → Check local dict                             │      │
│    │   → Walk parent chain                            │      │
│    │   → Fall back to global dict                     │      │
│    └──────────────────────────────────────────────────┘      │
└──────────────────────────────────────────────────────────────┘
                              ▼
┌──────────────────────────────────────────────────────────────┐
│               Handler Layer (Platform Bridge)                 │
│  CometViewHandler reads environment values and maps to       │
│  native platform properties (UIKit, Android Views, WinUI)    │
└──────────────────────────────────────────────────────────────┘
```

**Key resolution order:**
1. Explicit fluent method on instance (`.Background(Colors.Red)`)
2. Per-view environment (`view.SetEnvironment()`)
3. Type-scoped environment (`View.SetGlobalEnvironment(typeof(Button), key, value)`)
4. StyleId-scoped environment (via `StyleId` on the view)
5. Global environment (`View.SetGlobalEnvironment(key, value)`)

**Three overlapping style systems:**
1. **`Style` (legacy)** — Concrete class with typed sub-style properties (`ButtonStyle`, `TextStyle`, `SliderStyle`). Uses `StyleAwareValue<ControlState, T>` for multi-state values. Applied via `.Apply()` which calls `SetEnvironmentValue()`.
2. **`ControlStyle<T>` (Phase 3)** — Generic dictionary-based style. Cleaner API (`.Set(key, value)`). Registered on `Theme` via `.SetControlStyle()`. Applied through the same environment mechanism.
3. **`Style<T>` (functional)** — Action delegate that directly calls fluent methods on a view. Most flexible, least structured. Supports implicit registration.

### MauiReactor Styling Engine

```
┌──────────────────────────────────────────────────┐
│            AppTheme : Theme                       │
│  OnApply() called on:                            │
│    - App startup                                 │
│    - UserAppTheme change                         │
│                                                  │
│  Sets global style lambdas:                      │
│    LabelStyles.Default = _ => _.TextColor(...)   │
│    ButtonStyles.Default = _ => _.Background(...)  │
│    LabelStyles.Themes["Title"] = _ => _.Font(...)│
└──────────────────────┬───────────────────────────┘
                       ▼
┌──────────────────────────────────────────────────┐
│         Component.Render()                        │
│  Label("Hello")                                  │
│    → Creates VisualNode (virtual MAUI element)   │
│    → Default style lambda applied automatically  │
│    → .ThemeKey("Title") applies named variant    │
│    → Fluent methods set MAUI properties directly │
│                                                  │
│  No cascading. Each control's properties set     │
│  independently via the style lambda.             │
└──────────────────────┬───────────────────────────┘
                       ▼
┌──────────────────────────────────────────────────┐
│      MAUI Control Tree (reconciled)               │
│  Properties mapped 1:1 to native controls        │
└──────────────────────────────────────────────────┘
```

### SwiftUI Styling Engine

```
┌──────────────────────────────────────────────────┐
│          EnvironmentValues                        │
│  ┌────────────────────────────────────────┐      │
│  │ \.colorScheme  : ColorScheme           │      │
│  │ \.font         : Font                  │      │
│  │ \.theme        : AppTheme (custom)     │      │
│  │ \.buttonStyle  : any ButtonStyle       │      │
│  └────────────────────────────────────────┘      │
│  Propagates down the view tree automatically.    │
│  Children can override for subtrees.             │
└──────────────────────┬───────────────────────────┘
                       ▼
┌──────────────────────────────────────────────────┐
│          ViewModifier Chain                       │
│  Each modifier wraps the view in a new type:     │
│  Text("Hello")        → Text                     │
│    .font(.title)      → ModifiedContent<Text, _> │
│    .foregroundStyle()  → ModifiedContent<MC, _>   │
│    .padding()          → ModifiedContent<MC, _>   │
│                                                  │
│  Resolved at render time by the layout engine.   │
│  Later modifiers override earlier ones for same  │
│  property.                                       │
└──────────────────────┬───────────────────────────┘
                       ▼
┌──────────────────────────────────────────────────┐
│          Style Protocols                          │
│  ButtonStyle.makeBody(configuration:)            │
│    → configuration.label (the button content)    │
│    → configuration.isPressed (state)             │
│    → Returns new View with styling applied       │
│                                                  │
│  Propagated via environment — parent sets style, │
│  all descendant buttons use it automatically.    │
└──────────────────────────────────────────────────┘
```

---

## 5. Gap Analysis

### What Comet is Missing Relative to SwiftUI

| Gap | Severity | Description |
|-----|----------|-------------|
| **Implicit style transitions / animations** | 🔴 Critical | SwiftUI's `.animation()` modifier automatically animates any property change. Comet has no equivalent. Style changes are instant. |
| **Type-safe environment keys** | 🟡 Medium | Comet uses string keys (`"Theme.Primary"`) — easy to typo, no compile-time checks. SwiftUI uses `EnvironmentKey` protocol with associated types. |
| **Style protocol pattern** | 🟡 Medium | SwiftUI's `ButtonStyle`/`ToggleStyle` protocols give access to control state (isPressed, isEnabled) for interactive styling. Comet's `ControlState` enum exists but is only used in the legacy `StyleAwareValue` system. |
| **Preference keys (upward data flow)** | 🟡 Medium | SwiftUI's `PreferenceKey` allows children to communicate sizing/layout preferences upward. Comet has no equivalent. |
| **Semantic system fonts** | 🟢 Low | SwiftUI's `.font(.title)`, `.font(.body)` are platform-aware semantic tokens. Comet has `StyleAsH1()` etc. but they're hardcoded sizes, not platform-adaptive. |
| **Scoped style propagation** | 🟢 Low | SwiftUI: `VStack { }.buttonStyle(MyStyle())` affects all descendant buttons. Comet's `ApplyTheme()` is close but only works for whole-theme override, not per-control-type scoping. |

### What Comet is Missing Relative to MauiReactor

| Gap | Severity | Description |
|-----|----------|-------------|
| **Simplified theme class pattern** | 🟡 Medium | MauiReactor's single `OnApply()` override with `LabelStyles.Default` / `Themes["key"]` pattern is cleaner than Comet's three overlapping systems. |
| **`.ThemeKey()` named variant selector** | 🟡 Medium | MauiReactor's `.ThemeKey("PrimaryButton")` is simpler than Comet's `StyleId` + environment lookup. |
| **VisualState integration in theme** | 🟢 Low | MauiReactor can set visual states inline: `.VisualState("CommonStates", "Disabled", prop, value)`. Comet's `VisualStateManager` requires separate setup. |
| **`.When()` conditional rendering** | 🟢 Low | MauiReactor's `.When(condition)` cleanly handles conditional visibility. Comet uses `if` in the body lambda, which is already idiomatic. |

### What Comet Has That Others Don't

| Advantage | Description |
|-----------|-------------|
| **Cascading environment system** | Neither MauiReactor nor vanilla MAUI has Comet's parent-chain environment lookup. Only SwiftUI matches this. |
| **MD3 semantic token system** | 27 Material Design 3 color tokens built-in (`ThemeColors`). MauiReactor has nothing comparable. |
| **Material ColorPalettes** | 15 pre-built Material color palettes (Red through DeepOrange) with 50-900 + A100-A700 shades including contrast colors. |
| **Reactive bindings in style modifiers** | `Func<T>` overloads on `.Color()`, `.FontSize()`, `.Background()` etc. enable reactive styling without full re-render. MauiReactor requires component-level re-render. |
| **Multiple style abstraction levels** | `Style<T>` (functional), `ControlStyle<T>` (dictionary), `Style` (legacy), `MaterialStyle` — serves different needs, though at the cost of complexity. |
| **ResourceDictionary with typed access** | `Get<T>(key)`, `TryGet<T>()`, `ApplyStyle<T>()`, `MergedDictionaries` — MAUI-familiar resource pattern for Comet. |

---

## 6. Recommendations

### Priority 1: Declarative Style Animations (🔴 Critical)

**What:** Add an `.animation()` modifier or `withAnimation {}` pattern that automatically animates property changes between render cycles.

**Why:** This is the single biggest UX gap vs. SwiftUI. Every modern declarative framework needs this. Without it, achieving polished UI transitions requires dropping down to imperative MAUI animation code.

**Approach:** Intercept environment value changes in the handler layer and create `Microsoft.Maui.Animations.Animation` instances for animatable properties (color, opacity, transform, size).

---

### Priority 2: Consolidate Style Systems (🟡 High)

**What:** Deprecate the legacy `Style` class (with `ButtonStyle`/`TextStyle`/`SliderStyle` properties) and the `MaterialStyle` subclass. Promote `ControlStyle<T>` + `Style<T>` as the two official style mechanisms.

**Why:** Three overlapping systems confuse developers:
- `ControlStyle<T>` for environment-driven per-type styling (theme integration)
- `Style<T>` for functional/reusable style bundles (ad-hoc composition)
- `Style` (legacy) for the old Material-era global style — but it's doing the same job as `ControlStyle<T>` with a less clean API

**Migration:** `MaterialStyle` becomes a theme preset that creates `ControlStyle<T>` instances. `ButtonStyle`/`SliderStyle`/etc. classes become internal implementation details.

---

### Priority 3: Type-Safe Environment Keys (🟡 Medium)

**What:** Replace string-based `EnvironmentKeys` constants with a typed key pattern inspired by SwiftUI's `EnvironmentKey` protocol.

**Example:**
```csharp
public struct BackgroundKey : IEnvironmentKey<Paint>
{
    public static Paint DefaultValue => null;
}

// Usage
view.SetEnvironment(BackgroundKey.Instance, new SolidPaint(Colors.Red));
var bg = view.GetEnvironment(BackgroundKey.Instance);
```

**Why:** Eliminates string typo bugs, enables IDE completion, provides compile-time type safety. The current system has 50+ string constants that could silently mismatch.

---

### Priority 4: Style Protocol Pattern for Controls (🟡 Medium)

**What:** Add a `ButtonStyle` protocol (interface) pattern where implementations receive a `Configuration` object with press/hover/disabled state, similar to SwiftUI.

**Example:**
```csharp
public interface IButtonStyle
{
    View MakeBody(ButtonStyleConfiguration config);
}

public record ButtonStyleConfiguration(View Label, bool IsPressed, bool IsHovered, bool IsEnabled);

// Usage
Button("OK").ButtonStyle(new PrimaryButtonStyle())
```

**Why:** Bridges the gap between Comet's existing `ControlState` enum and interactive styling. Currently, `StyleAwareValue<ControlState, T>` in the legacy system is the only way to differentiate pressed/hovered states, and it's not exposed through the modern `ControlStyle<T>` API.

---

### Priority 5: Scoped Style Propagation (🟢 Nice-to-have)

**What:** Allow setting a `ControlStyle<T>` on a view subtree, not just globally.

**Example:**
```csharp
VStack(
    Button("A"),
    Button("B")
).ButtonStyle(myCustomStyle) // All buttons in this VStack get the style
```

**Why:** SwiftUI's `.buttonStyle()` propagation through the view hierarchy is one of its most elegant patterns. Comet's `ApplyTheme()` does this for entire themes but not for individual control styles.

---

### Priority 6: Preference Keys (🟢 Nice-to-have)

**What:** Add upward data flow from child to parent, equivalent to SwiftUI's `PreferenceKey`.

**Why:** Enables patterns like: child reports its ideal width → parent uses that to size a container. Currently impossible in Comet without imperative callbacks.

---

### Summary Priority Table

| # | Recommendation | Effort | Impact | Frameworks that have it |
|---|---------------|--------|--------|------------------------|
| 1 | Declarative style animations | High | 🔴 Critical | SwiftUI |
| 2 | Consolidate style systems | Medium | 🟡 High | N/A (internal cleanup) |
| 3 | Type-safe environment keys | Medium | 🟡 Medium | SwiftUI |
| 4 | Style protocol for controls | Medium | 🟡 Medium | SwiftUI |
| 5 | Scoped style propagation | Low | 🟢 Nice-to-have | SwiftUI |
| 6 | Preference keys | Medium | 🟢 Nice-to-have | SwiftUI |
