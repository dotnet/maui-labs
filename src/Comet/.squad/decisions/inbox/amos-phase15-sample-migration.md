# Phase 1.5 — Sample app migration

**Author:** Amos (Controls & API Dev)
**Date:** 2026-04-08
**Status:** Implemented — follows `feature/comet@29d3136` (Phase 1 + 1.1)
**Spec:** `docs/architecture/STYLE_THEME_SPEC.md` §5.2, §6.2
**Target:** `net11.0-ios` (Simulator: iPhone 17 Pro, iOS 26.2) for the whole pass.

## Headline

All 19 Comet sample apps that can target `net11.0-ios` build with **0 errors**.
Ready for Bobbie's DevFlow E2E pass.

## Scope boundary

The task was framed around eight "known-broken" samples. Two of those
(`CometMarvelousApp/Pages/Components/WonderNavigator.cs`,
`CometProjectManager/ProjectManagerApp.cs`) and one collateral
(`CometWeather/WeatherPreferences.cs`) turned out to be **false-positive grep
hits** — they reference locally defined `AppTheme` types or MAUI's
`Microsoft.Maui.ApplicationModel.AppTheme`, not Comet's deleted
`Comet.Styles.AppTheme`. No changes were needed.

## Environment fixes (non-migration, but required for any build)

| File | Change |
|------|--------|
| `sample/Directory.Build.props` | `MauiVersion` bumped from `11.0.0-preview.2.26152.10` → `11.0.0-preview.3.26203.7`. Framework `Comet.csproj` resolves preview.3 transitively from the workload; sample pin caused `NU1605` downgrade errors on every single sample. |
| `sample/CometControlsGallery/CometControlsGallery.csproj` | Unconditional `<Import>` of `../../../mauiplatforms/.../Platform.Maui.MacOS.targets` broke every build (sibling repo not present locally). Conditioned on `$(TargetFramework.Contains('-macos'))` **and** `Exists(...)`. |

## Per-sample inventory

### Migrated (code rewrite to Phase 1 API)

| Sample | Files touched | Migration path |
|--------|---------------|----------------|
| **Comet.Sample** | `Views/MainPage.cs` | Removed "Material Design" menu entry whose target sample was deleted (below). |
| **CometBaristaNotes** | `Styles/CoffeeTheme.cs` | Stripped deleted legacy `Theme` props (`CurrentTheme`, `PrimaryColor`/`BackgroundColor`/`SurfaceColor`/`TextColor`/`SecondaryTextColor`/`ErrorColor`, `ColorScheme = new ThemeColors { … }`). The canonical `Colors = new ColorTokenSet { … }` blocks were already present from an earlier prep pass, so the fix was purely subtractive. |
| **CometControlsGallery** | `App.cs`, `Pages/ThemePage.cs`, `Pages/StyleSystemPage.cs` | `Theme` is now a record — every `theme.SetControlStyle<…>(…)` site was discarding the returned record. Re-bound the local before `ThemeManager.SetTheme(theme)`. `theme.GetNewControlStyle<Button>()` → `theme.GetControlStyle<Button, ButtonConfiguration>()`. |
| **CometMauiApp** | `MyApp.cs` | `Theme.Current = Defaults.Light;` → `ThemeManager.SetTheme(Defaults.Light);` |
| **CometVideoApp** | `VideoApp.cs` | Same one-line swap (`Defaults.Dark`). |

### Files deleted (no migration path — pure demos of removed APIs)

| File | Reason |
|------|--------|
| `Comet.Sample/Views/MaterialStylePicker.cs` | Demoed the removed `Comet.Styles.Material` namespace (`ColorPalette`, `MaterialStyle`, `.ApplyStyle()`). |
| `Comet.Sample/Views/MaterialSample.cs` | Demoed the removed `StyleAsContained/Outlined/Text` Material extensions. |
| `CometBaristaNotes/Styles/CoffeeControlStyles.cs` | Defined `ControlStyle<Button>` / `ControlStyle<View>` (deleted type). Confirmed via grep that **zero** call sites remained — all button styling had already migrated to `CoffeeModifiers.PrimaryButton` via `.Modifier(…)`. |

### Left as-is (already clean / false-positive grep)

| Sample | Why |
|--------|-----|
| `CometAllTheLists` | Clean. |
| `CometDigitsGame` | Clean. |
| `CometFeatureShowcase` | Clean. |
| `CometMailApp` | Clean. |
| `CometMarvelousApp` | Grep-matched `AppTheme.DarkTertiaryColor` — resolves to the sample's own `Services/AppTheme.cs` static class, not Comet. |
| `CometOrderingApp` | Clean. |
| `CometProjectManager` | Grep-matched `AppTheme.Light/Dark` — resolves to `Microsoft.Maui.ApplicationModel.AppTheme`. |
| `CometRecipeApp` | Clean. |
| `CometStressTest` | Clean. |
| `CometSurfingApp` | Clean. |
| `CometTaskApp` | Clean. |
| `CometTodoApp` | Clean. |
| `CometTrackizerApp` | Clean. |
| `CometWeather` | Grep-matched `Microsoft.Maui.ApplicationModel.AppTheme.Light/Dark` — fully qualified MAUI enum. |

### Not covered by this pass — recommend a separate ticket

| Sample | Status |
|--------|--------|
| `CometMacApp` | **Not built.** `<TargetFramework>net11.0-macos</TargetFramework>` only (no `-ios`), pins `MauiVersion=10.0.31`, and depends on a sibling checkout of `dotnet/mauiplatforms` that isn't present in `maui-labs`. The only build error was the missing `Platform.Maui.MacOS.targets` import + `NETSDK1147` (workload not installed) — both environment, not API. Phase 1 API usage was not inspected here because the build never got past restore. |
| `MauiReference`, `MauiControlsGallery` | Pure-MAUI samples, out of scope for Phase 1.5 per spawn prompt. Not re-built. |

## Patterns discovered / worth noting

1. **`Theme` is a record → return value must be used.** The most common latent
   bug across migrated samples: `theme.SetControlStyle<T, C>(x);` with the
   return discarded. Compiles clean, silently loses the mutation. Recommend
   that a future compiler / analyser diagnostic flag discarded record-with
   results on `Theme`. Until then, `ControlsGallery`'s `App.cs` /
   `StyleSystemPage.cs` / `ThemePage.cs` are the canonical patterns to grep
   against when reviewing future sample PRs.
2. **"Legacy Theme property cleanup" is purely subtractive for well-maintained
   samples.** `CoffeeTheme.cs` already had the new token sets (`Colors`,
   `Typography`, `Spacing`, `Shapes`) in place alongside the deleted legacy
   ones. The migration was *only* deletion of the dead fields + one enum
   reference. This is likely the shape of any future theme-migration work.
3. **MAUI's `AppTheme` enum is confusable with Comet's deleted `AppTheme`.**
   Three false-positive grep hits in this pass. If Holden ever wants to
   reintroduce a Comet theme-mode concept, recommend **not** naming it
   `AppTheme` again.
4. **`mauiplatforms` import shouldn't be unconditional.** `CometControlsGallery`
   had `<Import Project="…\Platform.Maui.MacOS.targets" />` with no
   condition; `CometMacApp` has the same issue but wasn't fixed in this pass
   because its whole toolchain is macOS-workload-gated. If anyone else starts
   building `CometMacApp` without the sibling checkout, the same condition
   pattern I used in `CometControlsGallery.csproj` should be applied.

## Commit trail

```
7ce62c1  Phase 1.5: bump sample MauiVersion to preview.3.26203.7
e4b4438  Phase 1.5: migrate CometMauiApp + CometVideoApp Theme startup
dfe891f  Phase 1.5: drop deleted Material style demo from Comet.Sample
c913e32  Phase 1.5: migrate CometBaristaNotes CoffeeTheme to Theme record
c6359cb  Phase 1.5: migrate CometControlsGallery to Theme-as-record API
```
(+ this decision note.)

## Verification

Full build matrix across every `net11.0-ios`-capable Comet* sample:

```
Comet.Sample            rc=0 errors=0
CometAllTheLists        rc=0 errors=0
CometBaristaNotes       rc=0 errors=0
CometControlsGallery    rc=0 errors=0
CometDigitsGame         rc=0 errors=0
CometFeatureShowcase    rc=0 errors=0
CometMailApp            rc=0 errors=0
CometMarvelousApp       rc=0 errors=0
CometMauiApp            rc=0 errors=0
CometOrderingApp        rc=0 errors=0
CometProjectManager     rc=0 errors=0
CometRecipeApp          rc=0 errors=0
CometStressTest         rc=0 errors=0
CometSurfingApp         rc=0 errors=0
CometTaskApp            rc=0 errors=0
CometTodoApp            rc=0 errors=0
CometTrackizerApp       rc=0 errors=0
CometVideoApp           rc=0 errors=0
CometWeather            rc=0 errors=0
```

Reproduce via `.squad/build-samples.sh <sample-name…>` (included in the
adjoining commit).

## Open questions

1. **`CometMacApp`** — does anyone still need this sample? It is not
   buildable in the current `maui-labs` checkout (missing sibling repo +
   workload). If it's ownership-orphaned, recommend removal rather than
   letting it rot. If still live, it'll need its own Phase 1.5 mini-ticket
   with the mauiplatforms checkout resolved first.
2. **`StyleSystemPage` / `ThemePage`** in `CometControlsGallery` — these now
   compile and exercise the new API, but they also depend on visible pressed/
   hovered transitions which Holden's Phase 1 note explicitly lists as a
   Phase 2 gap. Bobbie may want to annotate the pages with a "Phase 2 will
   wire interactive states" banner once E2E runs surface the static-looking
   state buttons.
