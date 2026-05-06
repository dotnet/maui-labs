# Scribe: Phase 1 Session Close — Theme record + legacy deletion

**Session:** 2026-04-17T02:33:00Z
**Branch:** `feature/comet` (pushed to `origin/feature/comet`)
**Spec:** `docs/architecture/STYLE_THEME_SPEC.md`
**Decision notes (inbox):**
- `holden-phase1-theme-record.md`
- `amos-phase1-legacy-deletion.md`
- `bobbie-phase1-gate.md`

## Outcome

Phase 1 landed: `Theme` is now an immutable record matching spec §5.2, the
per-view state leak in the style-defaults pipeline is fixed, the entire legacy
`Style` / `ControlStyle<T>` / `ThemeColors` / `AppTheme` surface is deleted
(−4,719 LOC net across 47 files), and `BuiltInStyles` is now allocation-free
per state change via static singleton `ViewModifier<T>` instances. Template
scaffolds and builds clean on `net11.0-maccatalyst`. NuGet repacked. Framework
test suite green apart from 4 pre-existing unrelated failures.

## Commits landed

| SHA | Author | Summary |
|-----|--------|---------|
| `932129e` | Holden | Theme as `record` (spec §5.2) + fix per-view state leak in `ApplyModifierAsInstanceDefaults` |
| `58e3935` | Amos   | Delete legacy style/theme types + singleton `BuiltInStyles` (−4,719 LOC net, 47 files) |
| `4ab498b` | Holden | Fix `ThemeManager` tests for record value equality (`Assert.Same` → `Assert.Equal`) |
| `eb8a756` | Amos   | Rewrite template `copilot-instructions.md` for Token / `ViewModifier` / `IControlStyle` API |

All four commits pushed to `origin/feature/comet`.

Reviewer passes (both clean, no findings):
- `/review` on `932129e..58e3935`
- `/review` on `4ab498b..eb8a756`

## What Holden did (`932129e`)

- Rewrote `src/Comet/Styles/Theme.cs` as an immutable `record` with
  `required` Colors/Typography/Spacing/Shapes token sets and an internal
  `ImmutableDictionary<Type, object>` for per-control styles. Exposed only
  `SetControlStyle<TControl, TConfig>(IControlStyle<…>)` and
  `GetControlStyle<TControl, TConfig>()`.
- Deleted without shim: `Theme.Current`, `Theme.Apply`, `Theme.ThemeChanged`,
  `Theme.Light`/`Theme.Dark`, `CurrentTheme`, `ColorScheme`, `PrimaryColor`,
  `SecondaryColor`, `BackgroundColor`, `SurfaceColor`, `TextColor`,
  `SecondaryTextColor`, `ErrorColor`, `GetColor(key)`, legacy
  `ControlStyle<T>` overloads, `IControlStyleApplicable`, and the
  `Comet.Styles.AppTheme` enum.
- Fixed the per-view state leak: renamed
  `ApplyModifierAsTypeScopedDefaults` → `ApplyModifierAsInstanceDefaults`
  and replaced `View.SetGlobalEnvironment(controlType, …)` broadcasts with
  `view.SetEnvironment(key, value, cascades: true)` scoped to the target
  view. State-dependent writes (pressed / hovered / focused) no longer
  leak across instances, and fluent `LocalContext` setters still win over
  cascading style defaults per spec §10.3.
- Refactored `ThemeManager.cs`, `ThemeDefaults.cs`, `ControlStyleExtensions.cs`,
  `ViewExtensions.cs`, `CometApp.cs` and `AppHostBuilderExtensions.cs` to the
  new surface. Deleted `ThemeExtensions.cs` wholesale. `CometApp.ThemeChanged`
  is now a documented no-op (spec §6.5 moves system light/dark follow to the
  app layer).

## What Amos did (`58e3935`)

- Deleted ~1,400 LOC of framework style/theme legacy (20 files under
  `src/Comet/src/Comet/Styles/**` plus `Interactivity/VisualStateManager.cs`
  and the entire `Styles/Material/` subdirectory).
- Deleted ~2,676 LOC of tests whose intent no longer maps to the new API
  (`ThemeBaseTests`, `ThemeColorsTests`, `ControlStyleTests`,
  `ThemeIntegrationTests`, `StylingTests`, `ThemeTests`).
- Call-site migrations: removed `Style.Apply()` startup in
  `AppHostBuilderExtensions.UseCometHandlers`; fixed a latent CS0157
  (`return from finally`) in Holden's new `ApplyModifierAsInstanceDefaults`;
  stripped `ResourceDictionary` / `VisualStateManager` APIs from
  `View.cs`, `ViewExtensions.cs`, `StyleExtensions.cs`; cleaned
  `DataTrigger`, MAUI-compat `Trigger`/`MultiTrigger`, and `AdaptiveTrigger`
  of `List<Setter>`; stripped the style-builder emission from
  `CometViewSourceGenerator.cs`.
- `BuiltInStyles` rewritten to per-state **static singleton**
  `ViewModifier<Button>` instances — `FilledButtonStyle` /
  `OutlinedButtonStyle` / `TextButtonStyle` / `ElevatedButtonStyle` each
  return `Default.Instance` / `Pressed.Instance` / `Hovered.Instance` /
  `Disabled.Instance` from `Resolve(ButtonConfiguration)`. Zero heap
  allocation per state change (shared `RoundedRectangle` / `Thickness` /
  disabled colors cached as `static readonly`). Eager token resolution at
  `Apply(view)` time per spec §11.2 / §15.2 — binding-based deferral
  deliberately rejected.

## What Holden did (`4ab498b`)

- `ThemeManagerTests.ThemeManager_SetTheme_ChangesGlobalTheme` and
  `ThemeManager_CurrentView_ReturnsGlobalTheme` switched from `Assert.Same`
  to `Assert.Equal`. `Theme` is a record; value equality is the contract,
  not reference identity. This resolves the 2 Phase 1-induced failures
  Bobbie's gate flagged.

## What Amos did (`eb8a756`)

- Rewrote `src/Comet/templates/single-project/CometApp1/.github/copilot-instructions.md`.
  The shipped guidance no longer references `AppTheme`, `CurrentTheme`,
  `ColorScheme`, `ThemeColors`, `Theme.Current`, `ThemeBackground`,
  `ThemeForeground`, `ThemeColors`, or `Theme.Current.GetColor(...)`. All
  snippets now use the `Theme` record + `ColorTokens.*.Resolve(view)` +
  `IControlStyle<TControl, TConfig>` + `ThemeManager.SetTheme` surface.
  Required before Phase 2 ships because every `dotnet new comet` inherits
  this file as AI-agent context.

## Final state (Bobbie's gate, confirmed)

- **Framework build** (`src/Comet/Comet.csproj` Debug, net11.0-android/ios/maccatalyst):
  **0 errors, 0 new warnings** (incremental shows 4 pre-existing `NU1510`
  `System.Collections.Immutable` project warnings).
- **Source generator build**: 0 errors, 0 warnings.
- **Tests** (`dotnet test --no-build`): **800 passed / 4 failed / 26 skipped**.
  All 4 failures are pre-existing and unrelated to Phase 1 (reactive scheduler
  flake, `FlowDirectionDefault`, 2 template-surface-validation tests stale
  on net10 / `[Body]` attribute).
- **NuGet**: `~/work/LocalNuGets/Microsoft.Maui.Comet.0.4.0-dev.nupkg` repacked.
- **Template**: `dotnet new comet` + `dotnet build -f net11.0-maccatalyst` →
  0 errors / 0 warnings.
- **Samples**: 9 sample apps under `src/Comet/sample/` still reference deleted
  legacy API; **not in `MauiLabs.slnx`**, so not blocking.

## Open follow-ups (for future phases)

### Phase 1.5 — sample migration (per Bobbie §6 triage)
- **Delete:** `Comet.Sample` (demos deleted `MaterialStyle` concept).
- **Migrate (1-line each):** `CometMauiApp`, `CometVideoApp` — both just set
  `Theme.Current = Defaults.X`; switch to `ThemeManager.SetTheme(Defaults.X)`.
- **Migrate (substantive):** `CometBaristaNotes` (rework `CoffeeTheme` on
  `Theme` record + `CoffeeControlStyles` on `IControlStyle<TControl, TConfig>`);
  `CometControlsGallery` (swap `GetNewControlStyle<T>()` → `GetControlStyle<TControl, TConfig>()`
  in `StyleSystemPage.cs`).
- **Verify-only:** `CometMarvelousApp`, `CometProjectManager`, `CometWeather`,
  `MauiReference` — their `AppTheme` references resolve to
  `Microsoft.Maui.ApplicationModel.AppTheme`, not Comet's deleted type.
  `CometMauiApp` also surfaced an `NU1605` package downgrade — audit
  `Directory.Packages.props` alongside API migration.
- Run with Elena (API-consumer view) + Ralph (docs parity).

### Phase 2 — reactive state → style re-resolution (Holden's queued work)
- `OnControlStateChanged` must notify via `StyleToken<Button>.Key` so that
  pressed / hovered / focused transitions re-run `ResolveCurrentStyle` on
  the instance. Today `CometButtonStyleResolution` fires only on the
  properties it's appended to. The per-instance fix in `932129e` and the
  singleton `BuiltInStyles` from `58e3935` are both compatible with this
  wiring — no BuiltInStyles rework needed when it lands.
- Consider a sweep-on-resolve for cascading env keys that a new
  state's modifier stops writing (spec §10.5 currently limits exposure
  by restricting control-style modifiers to appearance-only writes).

### Phase 3 — Toggle / TextField / Slider built-ins
- Mirror the singleton-per-state pattern Amos used for Button across the
  remaining core controls. Config structs will need to surface their
  state inputs (checked, focused, editing, dragging) the same way
  `ButtonConfiguration` surfaces `IsPressed` / `IsHovered` / `IsFocused` /
  `IsEnabled`.

### Phase 4 — transitions
- Out of scope so far. Spec §15.x is the intended hook; timing / easing /
  property-diff mechanics to be designed once Phase 3 lands.

### Spec reconciliation — §7.3 `.Theme(theme)` vs `.UseTheme(theme)`
- Spec §7.3 and the template copilot-instructions both currently reference
  `.Theme(theme)` and `.UseTheme(theme)`. Only one should survive as the
  supported fluent entry point, and `ThemeManager.SetTheme(...)` is the
  imperative counterpart. Needs Holden + Ralph agreement before Phase 2
  ships so the template doc and spec don't drift from the framework API.

### Spec §15.2 example update
- The SwiftUI-comparison code block constructs a fresh appearance per
  `Resolve()`. Amos's singleton implementation is strictly better; the
  spec example should be rewritten to match. Holden to own.

### `AbstractLayout.GetDefaultPadding` → SpacingTokens
- Currently hard-codes `new Thickness(6)` because the sole writer
  (`Style.Apply()`) is deleted. Should read from `SpacingTokens` (e.g.
  `SpacingTokens.Small`) once layout-token wiring lands. Low priority —
  no current consumer depends on configurability.

### Source-generator cleanup tail
- `styleBuilderMustacheTemplate` and the `StylePropertyFunc` delegate
  chain remain as no-op stubs in `CometViewSourceGenerator.cs`. Pure
  dead-code cleanup; widen when convenient.

### Pre-existing test debt (not Phase 1)
- `TemplateCurrentSurfaceValidationTests.SingleProjectTemplateUsesCurrentComponentSurface` — regex forbids `[Body]`; template still uses it.
- `TemplateCurrentSurfaceValidationTests.SingleProjectTemplateTargetsCurrentMauiAndDropsLegacyDependencies` — expects `net10.0-maccatalyst`; template is `net11.0`.
- `ReactiveSchedulerTests.Scheduler_MaxFlushDepth_BreaksInfiniteLoop` — flake.
- `ViewExtensionTests.FlowDirectionDefault` — expects `LeftToRight`, gets `MatchParent`.

## Summary numbers

- Framework LOC delta vs `c5fb087`: **+721 / −5,440 (net −4,719)** across **47 files**.
- Framework build: **0 errors** on all 3 Debug TFMs (net11.0-android / -ios / -maccatalyst).
- Tests: **800 passed / 4 failed (pre-existing) / 26 skipped**.
- NuGet: `Microsoft.Maui.Comet.0.4.0-dev.nupkg` packed.
- Template: scaffolds + builds clean on net11.0-maccatalyst.

---

Scribe log only — no re-litigation. Decision notes remain canonical; see the
three files under `src/Comet/.squad/decisions/inbox/` for full context.
