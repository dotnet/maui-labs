# Comet Style System Showcase

This sample demonstrates Comet's new token-based style and theme system built on top of the MVU `Component<TState>` surface.

## What it demonstrates

- **Theme Definition** — `Defaults.Light` applied at startup via `Theme.Current`, providing Material 3 color, typography, spacing, and shape tokens
- **Token Usage** — `ColorTokens.Primary`, `TypographyTokens.TitleLarge` used directly on views
- **Built-in Button Styles** — `ButtonStyles.Filled`, `.Outlined`, `.Text`, `.Elevated` applied via `.ButtonStyle()`
- **ViewModifier** — Custom `CardModifier` reused across sections; `ComposedModifier` chains Card + Highlight
- **Control State** — Toggle enables/disables a button to show disabled-state rendering via `IsEnabled`
- **Typography** — `.Typography(TypographyTokens.BodyMedium)` applies size, weight, and family from the theme

## Key files

- `MyApp.cs` — sets `Theme.Current = Defaults.Light` at app startup
- `MainPage.cs` — style system showcase using `Component<StyleDemoState>`

## Build

From the repository root:

```bash
dotnet build src/Comet.SourceGenerator/Comet.SourceGenerator.csproj -c Release
dotnet build src/Comet/Comet.csproj -c Release
dotnet build sample/CometMauiApp/CometMauiApp.csproj -c Release -f net11.0-maccatalyst
```

## Style system entry points

| API | Purpose |
|-----|---------|
| `Theme.Current = Defaults.Light` | Sets the active theme globally |
| `ColorTokens.Primary` | Strongly-typed color token that resolves from the active theme |
| `TypographyTokens.TitleLarge` | Typography token with size, weight, family |
| `.ButtonStyle(ButtonStyles.Filled)` | Applies a state-aware IControlStyle to a button |
| `.Modifier(new CardModifier())` | Applies a reusable ViewModifier to any view |
| `.Typography(token)` | Shorthand to apply all font properties from a token |
