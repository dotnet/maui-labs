# BaristaNotes performance status

Status at 2026-09-23: **startup investigation paused; page-transition and
rendering measurement is the next focus**.

This records bounded experiments, not a general framework ranking.
The comparison uses Original BaristaNotes (MAUI, MauiReactor and EF Core/SQLite)
and the Comet rewrite (Jetpack Compose and its SQLite store).
It does not isolate framework overhead.

## Product goal

Jetpack Compose on Android and SwiftUI on Apple are fixed goals for Comet.
The C#/Java bridge, state representation and callback implementation can
change while retaining these native frameworks and their real controls.

The target is competitive startup and interaction performance without an
intermediate blank page. A plain .NET for Android app could help separate
runtime and higher-level costs, but no such BaristaNotes app has been built
or authorized as part of the next step. There is no matched Kotlin Compose,
Flutter or React Native result in this study.

## First display is not a usable page

Android's time to initial display (TTID) ends at the first app-window display.
It does not establish that all required content is visible or responsive.
Time to full display (TTFD) needs a separate app signal after required content
is drawn. See [Android startup metrics](https://developer.android.com/topic/performance/issues/launch-time).

The user observes Original as splash -> blank page -> populated page, but
Comet as splash -> populated page, and prefers Comet's perceived startup.
This is a user observation, not a timed or blinded perceptual study.

The measured Original source first renders an initialization shell while
awaiting database setup. Its drink page then loads bags, profiles and equipment
asynchronously and has a separate loading branch. Comet materializes its root
before the activity calls `SetContentView`. Neither source path alone proves
when all required content reaches the display.

Thus a lower TTID does not establish faster usable startup. No accepted
usable-startup or TTFD comparison exists yet.

## Accepted startup measurements

All times are milliseconds. Each study has 30 measured pairs in three blocks.
Pilot and conditioning launches are excluded from the estimates, not silently
discarded from the evidence. All valid slow measured launches are retained.

| Study | App | Median | p90 | p95, descriptive | Mean | Sample SD |
|---|---|---:|---:|---:|---:|---:|
| Baseline, 100 drinks | Original | 312.5 | 699.4 | 703.55 | 373.73 | 150.51 |
| Baseline, 100 drinks | Comet baseline | 876.5 | 891.4 | 901.05 | 879.13 | 12.96 |
| Baseline, 1,000 drinks | Original | 329.5 | 966.2 | 980.65 | 594.93 | 325.27 |
| Baseline, 1,000 drinks | Comet baseline | 891.0 | 912.1 | 919.05 | 894.17 | 15.38 |
| Latest, 100 drinks | Original, unchanged APK | 310.0 | 675.1 | 683.70 | 359.30 | 129.63 |
| Latest, 100 drinks | Combined Comet candidate | 709.0 | 726.6 | 732.55 | 710.63 | 12.23 |

| Study | Comet minus Original median | Paired 95% interval |
|---|---:|---|
| Baseline, 100 drinks | +564.0 ms | +560.0 to +576.0 ms |
| Baseline, 1,000 drinks | +561.5 ms | -55.0 to +576.0 ms |
| Latest, 100 drinks | +399.0 ms | +387.5 to +407.0 ms |

The 1,000-drink interval includes zero: inconclusive, not equivalent.
Original's larger variation has no accepted root cause.
The latest effect is +128.71%, with a paired interval of +120.72% to +132.79%.

The latest Comet median is 167.5 ms, or 19.11%, below its historical median.
Those runs were not concurrent. This is descriptive, not a paired causal
estimate. Payload removal and inset filtering changed together; their
individual timing contributions were not measured.

The estimator is the difference of app medians, not the median of pair
differences. The analysis preserves pairs within the three observed blocks,
uses 2,000 bootstrap resamples with seed `20260922`, and type-7 percentiles.
Intervals describe these observed blocks, not a population of devices.
The latest study had 72 launches: two pilot, ten conditioning and sixty
measured; no exclusions or retries. Independent full replay produced
byte-identical analysis and measured inputs.

## APK size and baseline memory

| Artifact | Signed bytes |
|---|---:|
| Original | 37,023,552 |
| Baseline Comet | 37,655,924 |
| Barista-only payload candidate | 26,294,642 |
| Combined payload and inset candidate | 26,298,738 |

Combined saves 11,357,186 bytes (30.16%) versus baseline Comet and is 28.97%
smaller than Original. The source inventory found 97 unrelated sample files,
totaling 10,514,952 compressed bytes. Standalone packaging excludes other
sample code and assets while preserving required Barista content and the
general multi-sample probe.

The 4,096-byte difference between payload-only and combined includes code
and package-identity differences; it is not an isolated code-size estimate.
The payload-only build passed emulator checks. Combined superseded it for
Pixel checks and timing.

The earlier 100-drink memory study measured post-settle New Drink state:

| Counter | Original, Android kB | Baseline Comet, Android kB |
|---|---:|---:|
| PSS | 172,614.5 | 125,770.0 |
| RSS | 289,584.0 | 243,036.0 |
| Private dirty | 107,192.0 | 76,320.0 |

PSS was 27.14% lower for baseline Comet. Its paired percentage interval was
-27.38% to -27.03%. There were ten independent paired process observations,
each reduced to the median of three `dumpsys meminfo --local PID` snapshots.
The sixty snapshots are not sixty independent samples. No forced GC was used.
These are not memory-at-TTID results and do not belong to the combined APK.

## What the earlier traces show

These are pre-optimization diagnostics, not a breakdown of the latest 709 ms.
The trace-on `Displayed` values, 921/934/920 ms, are not formal timing samples.
Parent, child and GC intervals overlap; do not add the following rows.

| Observed scope | Duration |
|---|---:|
| Activity creation | 234-243 ms |
| First traversal | 540-575 ms |
| Inset dispatch inside the first traversal | 257-284 ms |
| Main CPU inside inset dispatch | 200-204 ms |
| First Compose composition | 87-101 ms |
| Native Compose measurement | 33-34 ms |
| Native Compose layout / drawing | 8-9 ms / about 4 ms |
| Explicit ART GC inside main-thread sleeps | 100-123 ms |

All 36 explicit GC slices were inside main-thread sleep intervals. Following
wakeups identify the GC-emitting thread. The managed requester is unknown.
Pinned Native AOT source contains a Java GC bridge and weak-reference wait
mechanism consistent with this pattern; that is not call-stack attribution.
The reachability protocol must not be bypassed.

A separate instrumented launch recorded 160.903 ms of initial node
materialization inside 231.277 ms of managed `OnCreate`, 24.349 ms of font
registration, 18.442 ms of `BuildUi`, and a 93.935 ms root-content callback.
Six backend layout scopes totaled 35.016 ms. Android's 514.196 ms
layout/measure metric is not Yoga execution time. These overlapping phase
measurements are not an isolated shim cost.

The separate native trace matched all 40 exported phases by exact
process/name/cookie, with checked clocks. Coverage limits include unlocated
service-level discarded chunks, 35 unavailable vendor probes, no Android log
rows and a later incomplete frame. No physical-presentation or
instrumentation-overhead claim was accepted.

The editor's broad safe-area dependency was corrected with an existing
property subscription and an owned revision signal. Top/bottom changes that
do not affect its Android geometry no longer rebuild its full body.
This correction does not change the scheduler, window metrics or GC policy.

Remaining questions include the cost of six Java-backed state objects per
base Compose node, control-specific state, Java-peer callbacks and temporary
text-measurement peers. Attribute the current candidate before changing the
bridge. Old inset intervals cannot quantify its remaining cost.

## Build and evidence limits

Measurements used one Pixel 5, Android 14/API 34, arm64, user 0, with existing
animation scales at zero. Process-cold means the selected package's processes
were absent; storage, shader/page caches and ART profiles were not reset.
Do not infer animated-transition smoothness from this configuration.

The experimental Android Native AOT builds used SDK
`11.0.100-rc.1.26425.128`, workload set `11.0.100-rc.1.26458.5`,
Android pack `37.0.0-rc.1.2257`, runtime `11.0.0-rc.1.26428.117`,
and explicit MAUI packages `11.0.0-preview.7.26406.9`.
They are Release, non-debuggable, separate test identities, with no DevFlow
or readiness instrumentation in the timing APKs. Existing AOT/trimming
warnings, including XA1040, remain disclosed. The signer is a test identity.

Original uses a disclosed local `Microsoft.Data.Sqlite.Core`
`11.0.0-preview.7.26381.103-local.barista.38971.1` backport of
[dotnet/efcore#38971](https://github.com/dotnet/efcore/pull/38971), not an
official package. Its source repository was unchanged.

Both apps passed full fixture and native-content checks. In the latest
collection Comet had fresh before/after canonical exports; Original had
unchanged exports from its completed operation, not a fresh live database
export. Its native content and selected fixture passed every launch.
Both apps were restored to Review and stopped after collection. This is
historical state, not a current device check.

The measured APK identities are:

| Artifact | SHA-256 |
|---|---|
| Original | `73de0a0bde231f489fd828bcc31cc8456e660683a9f69e486e4d7cab5b36b7af` |
| Baseline Comet | `ec272ab0d602514848a7b5aba8dbc4a6cb5dc8a11bdab26c6fb1d9fa441c8e99` |
| Combined Comet | `04ca980a4c6cc1d7e628f16d9fee55dbf9da9a509cd8946e9011e3626a418013` |

These identify measured artifacts, not a promise that rebuilding a later
commit produces identical APK bytes.

## Next: page transitions and rendering

The first bounded slice is New Drink -> Activity with 100 drinks.
The existing wider plan has all six root-switch directions, Equipment
list/detail, existing-drink editing and the temperature picker, separately
for 100/1,000 drinks and first-use/warm conditions.

The primary proposed interval is entry to the app's existing action handler
through its qualified native-ready frame-commit notification. Keep current
data-readiness and native control/layout milestones separately.
This includes UI-thread callback delivery delay, excludes input delivery
before handler entry, and is not exact buffer submission or physical display.
Pure CPU/GPU render duration, jank and input responsiveness need distinct
evidence; none has an accepted result in this comparison.

The schema-2 shared observer passed source/host checks. A Comet adapter exists
only as a source candidate based on an older snapshot, with one Activity
action per owner; Original needs its matching adapter. The earlier analyzer
still expects schema 1. Complete and review matching adapters and explicit
versioned ingestion before new artifact, accuracy and overhead gates.
Do not relabel historical v1 events or timing samples as schema 2.

Earlier ordinary Debug transition diagnostics used host tap-to-DevFlow-registry
observation, not native rendering or presentation. Their gains motivated
retained section roots and layout caches, but are not the Pixel Native AOT
app-pair benchmark. Keep the data sets separate.

## Resume evidence

Full raw data, signed APKs, isolated source snapshots and detailed handoffs
are retained outside the repository in the originating session's
`android-aot-comparison/20260921-pixel5-v2` evidence directory. They are not
distributed by this repository document.

Start with `comparison-report-02/startup-resume.md`,
`comparison-report-02/page-performance-resume.md` and
`comparison-report-02/comparison-summary.md`. The original baseline report,
failed attempts and all accepted records remain unchanged.
The latest full analysis is `ttid-insets-100-report-01/analysis/analysis.json`,
SHA-256 `c2c8be8eb0b95e7ce07bf2aa0bb1df1f23c9880945704b38ba08a9a2729b7baf`.

Before resuming startup, obtain the user's direction. Use new output folders
and fresh bounded device authorization; all earlier windows are expired.
Do not clear data, repeat completed imports, reset runtime profiles or reuse
unverified device state. Preserve matched fixtures, all valid slow trials,
and exact source/build/artifact identities.
