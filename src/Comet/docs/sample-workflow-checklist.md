# Per-sample workflow checklist (compose-samples build-out)

The repeatable delivery process for reproducing an android/compose-samples app on
Comet's node backends (Compose/Android + SwiftUI/iOS) at Jetchat-grade fidelity.
Derived from the Jetchat effort's retro; the named verification techniques at the
bottom are the ones that actually worked there. Gold-standard Kotlin source is
local at `~/work/compose-samples/<Sample>` — read it directly.

Order of samples (reuse-ordered): **Reply → JetNews → Jetsnack → Jetcaster → JetLagged.**

## The checklist

1. **Gold capture.** Enumerate the sample's screens (including compact/medium/expanded
   adaptive states and interaction states — open drawers, sheets, filters) and request
   user-captured gold screenshots for each. These are the fidelity contract; store them
   with the sample.

2. **Source survey → parity backlog.** Write `docs/<sample>-parity-backlog.md` in the
   `jetchat-parity-backlog.md` format: every widget, color, dp value, and type style
   cited `file:line` from the Kotlin source. Classify each needed capability:
   **WORKING / bound-unwired / facade-missing / no-control.** This document is the
   sample's living plan; mark items with commit hashes as they land.

3. **Framework work, host-test-first.** For each new node/control:
   FakeBackendNode contract tests (`tests/Comet.Tests/Backend/`) → Compose node in
   `src/Comet/Platform/Compose/` → device verify → SwiftUI shim kind **designed now**,
   implemented by the iOS gate. Framework node + its host tests = one commit,
   separate from sample commits. Keep the host-test baseline green at every commit.

4. **Screen build.** `sample/Shared/<Sample>/` mirroring the Jetchat layout
   (Theme / Icons / Screens files). Theme = the `JetchatTheme.cs` M3 generator seeded
   with this sample's color/type tokens from source (swap font families per sample).

5. **Fidelity passes.** Values-from-source sweep, then pixel-scan vs gold
   (`tools/snapshot_compare.py`), forcing end-state signals for render-only states.
   Loop until the backlog's fidelity items are closed.

6. **DevFlow smoke script — written AS YOU GO, not after.**
   `tools/smoke/<sample>.<platform>.sh`: launch → walk every screen → exercise each
   interaction → assert via tree/element queries → screenshot each screen.
   Missing DevFlow verb? 30-minute timebox to add it upstream (agent or src/DevFlow);
   otherwise adb fallback + a backlog entry.

7. **iOS gate.** All screens on CometSwiftUIProbe; same smoke script
   (`<sample>.ios.sh`); same values-from-source bar. Parity bar is **structure,
   values, and interaction** — not cross-OS pixel identity. Budget for known iOS debt
   the sample exercises (SelectorPanel, FormattedText runs, hot-reload own-content).
   **Hand-composed chrome checklist** (anything an iOS twin builds from Comet
   primitives where Android uses a real M3 widget): capture SAME-SCALE
   side-by-side screenshots and pixel-measure each chrome piece — Yoga's
   cross-axis default is flex-start while M3 widgets CENTER their slots, so
   every slot needs explicit centering, every strip an explicit background,
   every shape the real M3 token (16dp CornerLarge, 28dp capsule…). The Reply
   gate shipped four such misses (search-pill slots, bar/rail icons, safe-area
   strip, FAB shape) that only a measured side-by-side caught.
   The sample is not done — and the next does not start — until this gate passes.

8. **Perf/size snapshot (emulator).** `tools/bench/size.sh` trimmed Release APK,
   `tools/bench/startup.sh` cold start, gfxinfo first-pass on the heaviest scroll
   screen → the sample's RESULTS.md section, alongside the gold Kotlin app's own
   Release APK size. (Pixel 5 numbers are batched: B1 after JetNews, B2 after
   Jetcaster, B3 after JetLagged + Jetchat re-run.)

9. **/code-review** on the full sample diff before calling it done; fix findings.

10. **Docs/memory.** Backlog marked with commit hashes; new platform gotchas →
    AGENTS.md; DevFlow gaps → upstream backlog; newly discovered framework debt →
    the infra ledger in the plan.

11. **Retro (15 min).** What was mis-estimated, which lazy-infra call was wrong,
    what should change for the next sample.

## Named verification techniques (use these; they worked)

- **values-from-source** — never eyeball a dimension/color; cite the Kotlin
  `file:line` and use its exact value.
- **pixel-scan** — locate/verify small UI elements by scanning gold screenshot
  pixels (`tools/snapshot_compare.py`), not by judgment.
- **gfxinfo first-pass** — `adb shell dumpsys gfxinfo <pkg>` after ONE scroll pass,
  reset stats first; later passes hide first-composition jank.
- **forced end-state signals** — for states only reachable through timing
  (animations, refreshes), drive the terminal signal directly to verify the render.
- **device-tap method** — Comet OnTap needs `input swipe X Y X Y 120`, NOT
  `input tap`; compute coordinates at full resolution (1080-wide), not screenshot scale.
- **black screencap ≠ render bug** — check `mScreenState`/keyguard first.
- **host-tests-first** — patch-stream/signal/reactive behavior proven in
  FakeBackendNode tests before any device build.

## Timeboxes

- Hard problem with a pre-written fallback: use the timebox from the plan
  (shared-element 3d, Canvas 4d, scrollBehavior 1d), then take the fallback and
  document the decision in the sample's backlog doc.
- Platform rabbit hole (build system, emulator, tooling): **90 minutes**, then write
  the AGENTS.md gotcha and route around it.
