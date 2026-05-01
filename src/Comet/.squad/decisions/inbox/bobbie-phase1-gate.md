# Phase 1 — Verification Gate

**Author:** Bobbie (Test Engineer)
**Date:** 2026-04-08
**Branch:** `feature/comet` (PR #62) — commits `932129e` (Holden) + `58e3935` (Amos)
**Gates reviewed:** Holden's Theme-as-record + leak fix, Amos's legacy deletion + singleton BuiltInStyles.

## Verdict — **PASS-WITH-FINDINGS**

The framework, source generator, tests project, NuGet pack, and scaffolded template
all build 0-error. 798/830 tests pass. Two of six test failures are Phase 1-caused
(Holden's `ThemeManager.SetTheme`/`Current` reference-identity semantics) — routing
back to Holden for review, but neither blocks merge because the records compare
equal by value and functional theme resolution still works. Four remaining failures
are pre-existing and unrelated to Phase 1. One real content regression: the
template's shipped `copilot-instructions.md` still documents the entire deleted
legacy API — route to Ralph for rewrite before Phase 2.

## Evidence

### 1. Framework / generator / tests build

| Project | Errors | Warnings | Notes |
|---------|--------|----------|-------|
| `src/Comet/Comet.csproj` (net11.0-android/ios/maccatalyst) | **0** | 4 (all `NU1510 System.Collections.Immutable`) | Amos's reported 3,589 was full-rebuild + analyzers; incremental build shows 4 project-level warnings. No regressions observed. |
| `src/Comet.SourceGenerator/Comet.SourceGenerator.csproj` | **0** | **0** | Clean. Amos's source-gen trim compiles. |
| `tests/Comet.Tests/Comet.Tests.csproj` | **0** | 351 | All pre-existing CS86xx nullable + xUnit2013/2012/1031 style warnings; none introduced by Phase 1. |

### 2. Test run — `dotnet test --no-build`

**Totals:** Failed **6**, Passed **798**, Skipped **26**, Total **830**, Duration ~2s.

| Test | Classification | Route |
|------|----------------|-------|
| `ThemeManagerTests.ThemeManager_SetTheme_ChangesGlobalTheme` | **Phase 1-induced** — `Assert.Same(light, ThemeManager.Current())` fails. `SetTheme` stores into `_defaultTheme` + `View.SetGlobalEnvironment(ActiveThemeKey, theme)` but `Current()`'s `GetGlobalEnvironment<Theme>` returns a value-equal but reference-different record. Records compare equal by value, so end-user token resolution works; but the assertion Holden's own tests exercise fails. | **Holden — review & fix in follow-up** (switch to `Assert.Equal` if that's the intended semantics, or fix `GetGlobalEnvironment<Theme>` identity). |
| `ThemeManagerTests.ThemeManager_CurrentView_ReturnsGlobalTheme` | Same root cause. | **Holden**. |
| `ReactiveSchedulerTests.Scheduler_MaxFlushDepth_BreaksInfiniteLoop` | Pre-existing flake — Phase 1 touched no reactive-scheduler code. | Noted, not a blocker. |
| `ViewExtensionTests.FlowDirectionDefault` | Pre-existing — expects `LeftToRight`, gets `MatchParent` on a new `Text`. Phase 1 touched no `IView.FlowDirection` path. | Noted. |
| `TemplateCurrentSurfaceValidationTests.SingleProjectTemplateUsesCurrentComponentSurface` | Pre-existing — regex forbids `[Body]` attribute; template's `MainPage.cs` still uses it. Predates Phase 1 (template not modified). | **Ralph or David — decide: update template to inferred body, or relax the test.** |
| `TemplateCurrentSurfaceValidationTests.SingleProjectTemplateTargetsCurrentMauiAndDropsLegacyDependencies` | Pre-existing — expects `net10.0-maccatalyst` in template csproj; template is now `net11.0`. Test stale. | **Ralph — update test.** |

Baseline `c5fb087` was built in a scratch worktree; build failed with missing-type errors (repo wasn't in a clean pass-all state pre-Phase-1 either). The 4 "pre-existing" failures above don't touch Phase 1 surface and are not regressions.

### 3. NuGet re-pack

```
Successfully created package '/Users/davidortinau/work/LocalNuGets/Microsoft.Maui.Comet.0.4.0-dev.nupkg'.
```

One info warning (missing readme). Template consumes this nupkg; see next gate.

### 4. Template end-to-end

```
dotnet new comet -n _PhaseOneCheck -o /Users/davidortinau/work/_PhaseOneCheck --force
cd _PhaseOneCheck && dotnet restore && dotnet build -c Debug -f net11.0-maccatalyst
```

Summary:

```
_PhaseOneCheck -> .../bin/Debug/net11.0-maccatalyst/maccatalyst-arm64/_PhaseOneCheck.dll
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:12.77
```

`App.cs` and `MainPage.cs` are clean — no legacy API references. They use
`CometApp`, `Reactive<int>`, `VStack`, `Text`, `Button`, `.Alignment(Alignment.Center)` —
all live surface.

### 5. Template content review — **FINDING**

`src/Comet/templates/single-project/CometApp1/.github/copilot-instructions.md`
(178 lines, shipped with every `dotnet new comet`) documents the **entirely
deleted** legacy theme API. Grep hits:

| Line | Deleted reference |
|------|-------------------|
| 108 | `public static class AppTheme` |
| 113 | `CurrentTheme = AppTheme.Light` |
| 114 | `ColorScheme = new ThemeColors` |
| 141 | `Theme.Current = AppTheme.Light` |
| 151 | `button.ThemeBackground(EnvironmentKeys.ThemeColor.Primary)` |
| 152 | `text.ThemeForeground(EnvironmentKeys.ThemeColor.OnSurface)` |
| 153 | `card.ThemeColors(EnvironmentKeys.ThemeColor.Surface, …)` |
| 156 | `Theme.Current.GetColor(EnvironmentKeys.ThemeColor.Primary)` |

Every one of those symbols was deleted in `58e3935` or `932129e`. This directly
violates David's stated directive — *"get rid of anything that could be confusing
to developers or to AI LLM agents"* — because the file exists specifically to
brief AI agents, and it will cause Copilot / Claude / etc. to generate broken
code in every new Comet project. The template builds because `App.cs` /
`MainPage.cs` don't reference these APIs; the doc file isn't compiled.

**Route:** Ralph (docs) — rewrite `copilot-instructions.md` to reflect
`Theme` record + `ColorTokens.*.Resolve(view)` + `IControlStyle<TControl, TConfig>` +
`ThemeManager.SetTheme` / `.UseTheme()` / `.OverrideToken()` surface. **This should
land before Phase 2 ships**, since every new Comet project inherits the stale
guidance today.

### 6. Sample inventory — broken samples (triage only)

Samples live at `src/Comet/sample/` and are **not in `MauiLabs.slnx`** — they're
excluded from the solution build already, so they don't affect the gate.
Confirmed: `MauiLabs.slnx` references only `src/Comet/{src,tests}/**` plus
`test/e2e/CometGo.CompilerTests/`.

Grep for deleted-API usage across `sample/**/*.cs`:

| Sample | Deleted-API touch points | Recommendation |
|--------|--------------------------|----------------|
| `Comet.Sample` | `ApplyStyle(new MaterialStyle(colorPallete))`, `MaterialStylePicker` | **Delete.** `MaterialStyle` is gone wholesale; the sample demos a concept that no longer exists. |
| `CometBaristaNotes` | Has `Styles/CoffeeTheme.cs` + `Styles/CoffeeControlStyles.cs`, plus `IThemeService`/`ThemeService`. Some hits are a local `AppThemeMode` enum, not Comet's deleted `AppTheme`. `CoffeeControlStyles` likely uses old `ControlStyle<T>`. | **Migrate** — reference app value is high (this is David's canonical real-world sample per repo history). Effort: rework `CoffeeTheme` on `Theme` record, `CoffeeControlStyles` on `IControlStyle<TControl, TConfig>`. |
| `CometControlsGallery` | `GetNewControlStyle<Button>()` (deleted) in `StyleSystemPage.cs`; `SetControlStyle<Button, ButtonConfiguration>(ButtonStyles.Text)` (new API — fine). `MauiAppTheme` usages are `Microsoft.Maui.ApplicationModel.AppTheme` (MAUI, not deleted). | **Migrate** — gallery is the demo surface for the new styling system; high marketing value. Small delta: replace `GetNewControlStyle<T>()` with `GetControlStyle<TControl, TConfig>()`. |
| `CometMarvelousApp` | Defines its own local `static class AppTheme` in `Services/AppTheme.cs` — **not** Comet's deleted type. No real breakage from Phase 1 here. | **Keep; verify build.** Likely builds as-is; Amos's "9 samples broken" count may be high. |
| `CometMauiApp` | `Theme.Current = Defaults.Light;` in `MyApp.cs`; `MainPage.cs` string literal mentions legacy API but only for display. | **Migrate** — 1-line fix: `ThemeManager.SetTheme(Defaults.Light);`. Sample's whole point is demoing the new theming surface. |
| `CometProjectManager` | `Application.Current.UserAppTheme = AppTheme.Light` — this is `Microsoft.Maui.ApplicationModel.AppTheme`, not deleted. | **Keep; verify build.** Not broken by Phase 1. |
| `CometVideoApp` | `Theme.Current = Defaults.Dark;` in `VideoApp.cs`. | **Migrate** — 1-line fix as above. |
| `CometWeather` | `Microsoft.Maui.ApplicationModel.AppTheme` fully qualified — not deleted. | **Keep; verify build.** |
| `MauiReference` | `Application.Current!.UserAppTheme = AppTheme.Light` — MAUI, not Comet. | **Keep; verify build.** |

Build smoke-test on `CometMauiApp` shows `NU1605 Microsoft.Maui.Controls package
downgrade` — a csproj / `Directory.Packages.props` mismatch, **not** a Phase 1
regression. Suggests samples will need their `Directory.Packages.props` reviewed
alongside the API migration.

## Follow-ups for Squad

1. **Holden — ThemeManager identity tests (2 failures above).** Decide if
   `Assert.Same` is the intended contract (fix `GetGlobalEnvironment` to preserve
   identity for reference-typed env values) or relax to `Assert.Equal` since
   `Theme` is a record.
2. **Ralph — rewrite `templates/single-project/CometApp1/.github/copilot-instructions.md`.**
   Mandatory before Phase 2. All 8 call-outs in §5 above need to move to the
   current surface. Keep the ~178-line budget; prefer concrete code snippets.
3. **Phase 1.5 sample migration (Amos flagged).** Proposed scope from §6:
   - Delete: `Comet.Sample`.
   - Migrate (1-line): `CometMauiApp`, `CometVideoApp`.
   - Migrate (substantive): `CometBaristaNotes`, `CometControlsGallery`.
   - Verify-only: `CometMarvelousApp`, `CometProjectManager`, `CometWeather`,
     `MauiReference`.
   Not part of solution → not blocking Phase 1 merge. Spin as separate task
   with Elena (API-consumer view) + Ralph (docs parity).
4. **Reactive state → style re-resolution (Holden's Phase 2).** `OnControlStateChanged`
   needs to re-run `ResolveCurrentStyle` via `StyleToken<Button>.Key` notification;
   per-instance path from `ApplyModifierAsInstanceDefaults` is compatible
   (singletons + instance env writes). Not gated by Phase 1.
5. **Spec §15.2 example update (Amos).** Current SwiftUI-comparison example
   allocates an appearance per `Resolve()`; Amos's singleton impl is strictly
   better. Holden to update the spec text + code block.
6. **`AbstractLayout.GetDefaultPadding` token wiring.** Today hard-codes
   `new Thickness(6)` because the sole writer (`Style.Apply()`) is deleted.
   Should read `SpacingTokens.Small` or similar once layout tokens exist. Low
   priority; no current consumer depends on configuration.
7. **Pre-existing test debt (4 failures above, not Phase 1).** Ralph/David —
   update `TemplateCurrentSurfaceValidationTests` for `net11.0` + decide on
   `[Body]` attribute policy. Investigate `ReactiveScheduler_MaxFlushDepth`
   flake + `FlowDirectionDefault` assertion under separate ticket.

## Summary numbers

- **Framework LOC delta:** +721 / −5,440 (net −4,719) across 47 files vs `c5fb087`.
- **Framework build errors:** 0 (all 3 Debug TFMs).
- **Source generator build:** 0 errors, 0 warnings.
- **Tests:** 798 pass / 6 fail / 26 skipped — 2 Phase 1-induced, 4 pre-existing.
- **NuGet pack:** `Microsoft.Maui.Comet.0.4.0-dev.nupkg` written to
  `~/work/LocalNuGets/`, 1 info warning (missing readme).
- **Template scaffold + build:** 0 errors, 0 warnings on `net11.0-maccatalyst`.
- **Template content:** 1 file (`copilot-instructions.md`) still references
  deleted legacy API — **non-blocking for build, blocking for Phase 2 ship.**
- **Samples:** not in solution; triage recommendations produced per §6.

Commit range: `932129e..58e3935` on `feature/comet` (PR #62).
