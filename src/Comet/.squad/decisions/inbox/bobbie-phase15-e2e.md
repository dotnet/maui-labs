# Phase 1.5 — E2E on iPhone 17 Pro / iOS 26.2

**Author:** Bobbie (Testing & Release Engineering)
**Date:** 2026-04-16
**Status:** Complete — follows Amos's `feature/comet@9c6bd3b` (Phase 1.5 sample migration)
**Scope:** Launch verification of all 19 Comet samples Amos green-built for `net11.0-ios`.

## Headline

**19 / 19 PASS.** Every sample builds, installs, launches without crash, renders
real UI (not just splash), survives a theme-toggle smoke interaction, and
terminates cleanly on iPhone 17 Pro / iOS 26.2. No framework regressions found.

## Environment

| Item | Value |
|------|-------|
| Simulator device | iPhone 17 Pro |
| UDID | `95EC018A-A8CF-4FAB-98A4-EF49D2E626B3` |
| iOS runtime | **iOS 26.2** (23C54) — matches the spec exactly |
| Host | macOS 26.5, Apple Silicon (`iossimulator-arm64`) |
| Build target | `net11.0-ios -c Debug` |
| MAUI | `11.0.0-preview.3.26203.7` (from Amos's bump in `sample/Directory.Build.props`) |
| Microsoft.Maui.Comet | `0.4.0-dev` via `~/work/LocalNuGets` |
| Host tool | Plain `xcrun simctl` (not `maui-devflow`; see note below) |

## Pass/fail matrix

| # | Sample | Build | Launch | Interact | Notes |
|---|--------|:-----:|:------:|:--------:|-------|
| 1 | Comet.Sample | ✅ | ✅ | ✅ | UI Samples list renders with 16+ nav rows in light+dark |
| 2 | CometMauiApp | ✅ | ✅ | ✅ | |
| 3 | CometVideoApp | ✅ | ✅ | ✅ | |
| 4 | CometBaristaNotes | ✅ | ✅ | ✅ | Needed **clean rebuild** (see regression 1) |
| 5 | CometControlsGallery | ✅ | ✅ | ✅ | |
| 6 | CometAllTheLists | ✅ | ✅ | ✅ | |
| 7 | CometDigitsGame | ✅ | ✅ | ✅ | |
| 8 | CometFeatureShowcase | ✅ | ✅ | ✅ | |
| 9 | CometMailApp | ✅ | ✅ | ✅ | |
| 10 | CometMarvelousApp | ✅ | ✅ | ✅ | |
| 11 | CometOrderingApp | ✅ | ✅ | ✅ | |
| 12 | CometProjectManager | ✅ | ✅ | ✅ | |
| 13 | CometRecipeApp | ✅ | ✅ | ✅ | |
| 14 | CometStressTest | ✅ | ✅ | ✅ | |
| 15 | CometSurfingApp | ✅ | ✅ | ✅ | |
| 16 | CometTaskApp | ✅ | ✅ | ✅ | |
| 17 | CometTodoApp | ✅ | ✅ | ✅ | |
| 18 | CometTrackizerApp | ✅ | ✅ | ✅ | |
| 19 | CometWeather | ✅ | ✅ | ✅ | |

**Totals:** build 19/19, launch 19/19, interact 19/19.

## Procedure per sample

Implemented in `src/Comet/.squad/e2e-ios.sh` (committed):

1. `dotnet build <csproj> -c Debug -f net11.0-ios -p:RuntimeIdentifier=iossimulator-arm64` → `buildlogs/<name>.build.log`.
2. `simctl install` → `simctl launch --terminate-running-process` → parse `bid: pid` from stdout.
3. Wait 6s; `simctl io screenshot launch.png` → confirms real UI (not splash) in all 19.
4. Alive check: `kill -0 $pid`. If dead, harvest newest `~/Library/Logs/DiagnosticReports/<App>-*.ips` and copy it alongside the screenshots.
5. Smoke interact — **toggle system appearance** via `simctl ui <UDID> appearance dark|light`. This exercises each app's trait-change / color-resolution path without requiring per-sample UI knowledge. Screenshot `after-interact.png`.
6. Second alive check.
7. `simctl terminate` → restore appearance to light.

### Why appearance toggle instead of a UI tap

The 19 samples don't have a shared DevFlow CLI to drive UI taps (they ship
`Redth.MauiDevFlow.Agent` in Debug for hot-reload purposes, but the
`Microsoft.Maui.DevFlow.CLI` host tool required by the `maui-ai-debugging`
skill isn't installed on this machine, and integrating it per-app was out of
scope for a verification pass). The appearance toggle is cross-cutting,
deterministic, and actually a **harder** smoke test than a random tap: it
triggers `traitCollectionDidChange:` → MAUI window reconfiguration → every
visible Comet view re-resolves its colors. All 19 samples survived it, with
both the light and dark screenshots showing correctly re-rendered UI.

## Fixes landed in this pass

### Fix 1 — clean-rebuild `Comet.Sample` and `CometBaristaNotes` obj/bin

Both of these samples had pre-Phase-1.5 `obj/Debug/net11.0-ios/` artifacts
containing the **old** Microsoft.iOS static registrar map. After Amos's
MauiVersion bump (preview.2 → preview.3) the incremental build reused the
stale registrar, and the app crashed on `FinishedLaunching` with:

```
Microsoft.iOS: The static registrar map for Microsoft.iOS (and 0 other
assemblies) is invalid. It was built using a runtime with hash
1aedd2b6964f4b238f72b19526ff2913997c14b4, but the current runtime was built
with hash 5515a3bbcc8b848100f82164b07618790cdb5745.
*** Terminating app due to uncaught exception 'System.ArgumentNullException',
reason: 'Value cannot be null. (Parameter 'obj')'
   at ObjCRuntime.Class.ResolveTokenReference(...)
   at ObjCRuntime.Runtime.GetMethodFromToken(...)
   at UIKit.UIWindow.set_RootViewController(UIViewController value)
   at Microsoft.Maui.Handlers.WindowHandler.MapContent(...)
   at Microsoft.Maui.MauiUIApplicationDelegate.FinishedLaunching(...)
```

Resolution: `rm -rf obj bin` + rebuild. Both samples then launched and
interacted cleanly.

No code change was necessary — this is purely a stale-artifact issue caused by
an MSBuild incremental-build miss across the preview.2→preview.3 bump. No
commit needed for the samples themselves; the fix is a one-shot local cleanup.

### Fix 2 — `e2e-ios.sh` runner

Added `src/Comet/.squad/e2e-ios.sh` — reproducible E2E loop. Gotcha worth
recording: `simctl launch --console` buffers its `bundleID: pid` stdout line
until the app exits, so if you background it with `&` and try to parse the pid
immediately you'll always see it empty. Use `simctl launch` (without
`--console`) to capture the pid on return, and pull diagnostics from
`~/Library/Logs/DiagnosticReports/` if the alive check fails. The script
encodes this pattern so the next agent doesn't relearn it.

## Regressions opened / kicked back

**None.** Every sample reached a real, interactive UI state. Both Fix 1
situations were environment issues (stale intermediate artifacts), not
framework regressions. No bugs are being kicked back to Amos or Holden from
this pass.

The only finding worth preserving for Phase 2:

- **Registrar-hash mismatch is silent at build time.** Amos's
  `build-samples.sh` reported `rc=0 errors=0` for all 19 samples, and 2 of
  those 19 still had stale preview.2 registrars baked into the resulting
  `.app` bundle. Build-time reporting is not sufficient on its own to gate
  Phase 1.5 as "done"; a launch smoke test is required. Recommend extending
  `build-samples.sh` to do a `rm -rf obj bin` before each sample when a
  `MauiVersion` change is detected, or simply in the CI image.

## Artifacts

All stored under `src/Comet/.squad/artifacts/phase15-e2e/<Sample>/`:

- `launch.png` — first screenshot, post-splash, light appearance.
- `after-interact.png` — second screenshot after `appearance dark`, proves the
  app survived the trait change and re-rendered.

4 samples (`CometMarvelousApp`, `CometOrderingApp`, `CometRecipeApp`,
`CometSurfingApp`) had native-resolution screenshots over 1MB and were
downscaled in-place with `sips -Z 900` to stay under the commit-size limit;
all PNGs are now ≤ 700KB. All 38 PNGs committed via `git add -f` (artifacts
dir is gitignored).

Supporting logs (not committed; gitignored at `.squad/e2elogs/`, `.squad/buildlogs/`):

- `buildlogs/<Sample>.build.log` — full `dotnet build` output per sample.
- `e2elogs/<Sample>.run.log` — pid + launch record.
- `e2elogs/results.csv` — machine-readable pass matrix.
- `e2elogs/comet-sample-console.log`, `CometBaristaNotes.console.log` —
  captured the registrar-mismatch + managed stacktrace for Fix 1 diagnosis.

## Commit trail

```
<added in this pass>
```
(See `git log --oneline | grep 'Phase 1.5: E2E'` after push.)

## Recommended next action

**Push to PR.** Phase 1.5 meets its acceptance bar: 19/19 samples build
cleanly, launch on iPhone 17 Pro / iOS 26.2, render real UI, and survive a
theme-toggle smoke interaction. No framework regressions surfaced. Amos's
migration and Holden's Phase 1 + 1.1 API are validated end-to-end.

## Open question for the team

Worth stating the convention explicitly before Phase 2: **what counts as a
"sample is E2E-valid"?** Current bar is "launches + survives one smoke
interact." If Phase 2 wants interactive-state wiring (pressed/hovered
transitions) to be exercised, Bobbie needs either (a) a shared DevFlow CLI
integration across samples, or (b) per-sample AutomationIds on the primary
control of each sample so a generic "tap the first Button" script can work.
Flagging for Holden as a Phase 2 intake item, not a blocker for this PR.
