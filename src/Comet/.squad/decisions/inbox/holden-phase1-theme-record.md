# Phase 1 — Theme as record + per-view state leak fix

**Author:** Holden (Lead Architect)
**Date:** 2026-04-08
**Status:** Implemented (pending Amos cleanup for green build)
**Spec:** `docs/architecture/STYLE_THEME_SPEC.md` §4.8, §5.2, §10.3

## What I changed

### Deliverable 1 — `Theme` is now a `record` (spec §5.2)

`src/Comet/Styles/Theme.cs` rewritten from scratch. The new type is a pure
immutable record with:

- `required string Name`
- `required ColorTokenSet Colors`
- `required TypographyTokenSet Typography`
- `required SpacingTokenSet Spacing`
- `required ShapeTokenSet Shapes`
- `ImmutableDictionary<Type, object> _controlStyles` (private field)
- `Theme SetControlStyle<TControl, TConfig>(IControlStyle<TControl, TConfig>)`
- `IControlStyle<TControl, TConfig> GetControlStyle<TControl, TConfig>()`

**Deleted with no backward-compat bridge (per David's directive):**
`Theme.Current`, `Theme.Apply`, `Theme.ThemeChanged`, `Theme.Light`/`Theme.Dark`
static factories, `CurrentTheme`, `ColorScheme`, `PrimaryColor`, `SecondaryColor`,
`BackgroundColor`, `SurfaceColor`, `TextColor`, `SecondaryTextColor`,
`ErrorColor`, `GetColor(key)`, legacy `SetControlStyle<T>(ControlStyle<T>)`,
legacy `GetControlStyle<T>()`, `GetNewControlStyle<T>()`, the internal
`IControlStyleApplicable` interface, the `Comet.Styles.AppTheme` enum.

### Deliverable 2 — per-view state leak in `AppHostBuilderExtensions`

Renamed `ApplyModifierAsTypeScopedDefaults` → `ApplyModifierAsInstanceDefaults`.

The old path snapshotted the modifier's writes via `ContextualObject.MonitorChanges`
and replayed each one into `View.SetGlobalEnvironment(controlType, …)`. Because
the resolved modifier encodes per-view state (`IsPressed` / `IsHovered` /
`IsFocused` from the `Configuration` struct), this broadcast a single button's
"pressed" colour to every button via `ViewPropertyChanged` on the type-scoped
environment.

The new path replays each write through `view.SetEnvironment(key, value, cascades: true)`:

- **Per-instance.** State-dependent values live on the target view only.
- **Correct precedence.** Cascading `Context` sits below `LocalContext` in
  `ContextualObject.GetValue`, so explicit fluent setters
  (`.Background(Colors.Red)` → `cascades:false` → `LocalContext`) still win
  over style defaults (spec §10.3).
- **Scoped write filter.** Only writes whose `entry.Key.view` is the target
  view are replayed — modifiers that produce composed child views are not
  affected.

## Non-obvious call sites I already updated (so Amos doesn't redo them)

| File | What I did |
|------|------------|
| `src/Comet/Styles/ThemeManager.cs` | Self-contained active-theme store (internal `_defaultTheme` fallback). Deleted `SyncMauiAppTheme` — platform `UserAppTheme` sync is now app-level per spec §6.5. No more `AppTheme` enum refs. |
| `src/Comet/Styles/ThemeDefaults.cs` | `Defaults.Light` / `Defaults.Dark` now construct `Theme` records directly with all required init properties. `TypographyDefaults.Material3`, `SpacingDefaults.Standard`, `ShapeDefaults.Rounded` unchanged. |
| `src/Comet/Styles/ThemeExtensions.cs` | **Deleted** (whole file). Replaced by direct token reads per spec. |
| `src/Comet/Styles/ControlStyleExtensions.cs` | `theme.GetNewControlStyle<T>() as IControlStyle<…>` → `theme.GetControlStyle<TControl, TConfig>()` on all four `ResolveCurrentStyle` overloads. |
| `src/Comet/Helpers/ViewExtensions.cs` | Removed `ThemeColor`, `ThemeTextColor`, `ApplyTheme(Theme)`, `ApplyControlStyle(ControlStyle<T>)` — all relied on the deleted legacy API. |
| `src/Comet/Maui/CometApp.cs` | `IApplication.ThemeChanged` is now a documented no-op. System light/dark follow is app-level per spec §6.5. |
| `src/Comet/AppHostBuilderExtensions.cs` startup | `var defaultTheme = Defaults.Light with { };` — forks the shared singleton so framework registration of `ButtonStyles.Filled` doesn't mutate `Defaults.Light`. |

## What Amos needs to know

1. **`ControlStyle.cs` is the only current build blocker.** Line 20 still
   declares `: IControlStyleApplicable` — an internal interface that lived on
   old `Theme.cs` and I removed per the spec. The whole file is on your
   delete list, so this resolves when you delete it.
2. All other files on your delete list (`Style.cs`, `ButtonStyle.cs`,
   `NavbarStyle.cs`, `SliderStyle.cs`, `ProgressBarStyle.cs`, `TextStyle.cs`,
   `ViewStyle.cs`, `DefaultThemeStyles.cs`, `ResourceDictionary.cs`,
   `AppThemeBinding.cs`, `StyleAwareValue.cs`, `StyleType.cs`, `IThemeable.cs`,
   `VisualStateManager.cs`, `ThemeColors.cs`) still reference the removed
   legacy API (`Theme.Current`, `Theme.GetColor`, `ThemeColors`, `AppTheme`,
   etc.). I deliberately did **not** touch them — that's your scope.
3. `AppHostBuilderExtensions.UseCometHandlers` still contains
   `var style = new Styles.Style(); style.Apply();` at the top. That's yours
   to remove when `Style.cs` goes away.
4. `AppHostBuilderExtensions.UseCometHandlers` references `ButtonStyles.Filled`
   from `BuiltInStyles.cs` — I did not touch `BuiltInStyles.cs` per the task
   boundary. It should keep working as long as `ButtonStyles.Filled` remains
   an `IControlStyle<Button, ButtonConfiguration>`.
5. `sample/` apps and `tests/Comet.Tests/ThemeTests/ThemeBaseTests.cs` still
   consume the legacy API (`Theme.Current`, `ColorScheme`, `ThemeColor(t => t.PrimaryColor)`,
   etc.). Out of scope for Phase 1 foundation but they'll need migration or
   deletion in Amos's or a follow-up phase.

## Follow-ups (recommended separate tickets)

- **Reactive state → style re-resolution.** Today the handler mapper
  `CometButtonStyleResolution` fires only on the properties it's appended to.
  Spec §9.3 requires `OnControlStateChanged` to notify via `StyleToken<Button>.Key`
  so pressed/hovered state transitions re-run `ResolveCurrentStyle` on the
  instance. Per-instance fix here is correct; reactive invocation is a
  separate piece.
- **`ApplyModifierAsInstanceDefaults` cleanup on state transition.** When the
  "pressed" style writes `Background=0x88…` and then "released" resolves to
  `Background=0xFF…`, the second write overwrites the first because the same
  cascading key is used — good. But if a style stops writing a key entirely,
  the previous value leaks until something else clears it. Spec §10.5
  restricts control-style modifiers to appearance-only property writes, which
  limits exposure; a sweep-on-resolve can come later.
- **Theme system light/dark follow.** `CometApp.ThemeChanged` is now a no-op.
  If we want an opt-in helper (`ThemeManager.FollowSystemTheme(light, dark)`),
  it's a small addition later.

## Verification

- **Build:** `dotnet build src/Comet/Comet.csproj -c Debug` from `src/Comet/`
  → **3 errors, all `ControlStyle.cs(20,33): CS0246 'IControlStyleApplicable'`
  across the 3 Debug TFMs (android/ios/maccatalyst)**. All other files compile.
  Expected per task instructions ("call that out … Amos will clean those up").
- **Spec compliance:** Theme.cs matches §5.2 verbatim (names, types,
  `ImmutableDictionary` storage, `SetControlStyle`/`GetControlStyle` signatures).
- **Leak fix:** `ApplyModifierAsInstanceDefaults` writes via
  `view.SetEnvironment(key, value, cascades: true)` — instance-scoped Context,
  below LocalContext. No more `SetGlobalEnvironment(controlType, …)` on
  style-resolved values.
