# BaristaNotes performance status

Status at 2026-09-23: **the applied ART-profile startup rerun is complete.
Page-transition and rendering measurements remain pending**.

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
| Combined candidate, 100 drinks | Original, unchanged APK | 310.0 | 675.1 | 683.70 | 359.30 | 129.63 |
| Combined candidate, 100 drinks | Combined Comet candidate | 709.0 | 726.6 | 732.55 | 710.63 | 12.23 |
| Applied ART profile, 100 drinks | Original, unchanged APK | 317.0 | 693.5 | 702.40 | 391.27 | 156.41 |
| Applied ART profile, 100 drinks | Comet with applied profile | 693.5 | 709.3 | 713.10 | 695.27 | 11.14 |
| Applied ART profile, 1,000 drinks | Original, unchanged APK | 321.5 | 979.2 | 982.10 | 493.67 | 296.19 |
| Applied ART profile, 1,000 drinks | Comet with applied profile | 704.5 | 771.2 | 780.15 | 713.60 | 28.99 |

| Study | Comet minus Original median | Paired 95% interval |
|---|---:|---|
| Baseline, 100 drinks | +564.0 ms | +560.0 to +576.0 ms |
| Baseline, 1,000 drinks | +561.5 ms | -55.0 to +576.0 ms |
| Combined candidate, 100 drinks | +399.0 ms | +387.5 to +407.0 ms |
| Applied ART profile, 100 drinks | +376.5 ms | +368.5 to +384.0 ms |
| Applied ART profile, 1,000 drinks | +383.0 ms | +369.0 to +392.5 ms |

The baseline 1,000-drink interval includes zero: inconclusive, not equivalent.
Original's larger variation has no accepted root cause.
The combined-candidate effect was +128.71%, with a paired interval of
+120.72% to +132.79%.

The combined Comet median was 167.5 ms, or 19.11%, below its baseline median.
Those runs were not concurrent. This is descriptive, not a paired causal
estimate. Payload removal and inset filtering changed together; their
individual timing contributions were not measured.

The estimator is the difference of app medians, not the median of pair
differences. The analysis preserves pairs within the three observed blocks,
uses 2,000 bootstrap resamples with seed `20260922`, and type-7 percentiles.
Intervals describe these observed blocks, not a population of devices.
The combined-candidate study and both complete profile studies each had
72 timing-protocol launches: two pilot, ten conditioning and sixty measured;
no timing exclusions or retries.
Independent replay reproduced the measured inputs and analysis.

## Applied ART-profile rerun

The profiled APK was built from `d2b1c2e3bbd5a12eb913be6915e1e0609f3ad58e`
plus the reviewed 12-file profile update. The profile and build targets come
from upstream `8be2f82fa16abb2bb1180b04a38e4de943b749e9`; see
[vendor provenance](../src/vendor/VENDORED.md). The existing Comet facade
was not replaced. The rerun used the same RC1 comparison toolchain and the
Pixel 5 only, without an emulator step.

The install included a checksum-matched API 34 `.dm` file. Android reported
Comet as `speed-profile/install-dm` and Original as `verify/install`.
Required ART states were checked before and after each pilot, conditioning
phase and measured block: 20 boundary records per complete study.
This establishes applied profile-guided compilation, not only a profile
file packaged in the APK. No forced compilation or ART reset was used.

The new 100-drink difference is +118.77%, with a paired 95% interval of
+113.58% to +123.20%. For 1,000 drinks it is +119.13%, with an interval of
+110.81% to +123.04%.

| Dataset | Block | Pairs | Original median ms | Comet median ms |
|---|---:|---:|---:|---:|
| 100 | 1 | 10 | 316.5 | 700.0 |
| 100 | 2 | 10 | 316.5 | 696.0 |
| 100 | 3 | 10 | 320.5 | 692.0 |
| 1,000 | 1 | 10 | 320.0 | 701.5 |
| 1,000 | 2 | 10 | 328.0 | 717.5 |
| 1,000 | 3 | 10 | 324.0 | 703.0 |

Each block has five Original-first and five Comet-first pairs. The separate
1,000-drink block 2 and 3 intervals include zero; those block comparisons
are inconclusive. The pooled interval remains conditional on these three
observed blocks. Original's 1,015 ms maximum remains in the data. Original
has a lower median but a higher p90 than Comet in the 1,000-drink sample.

Comet's 100-drink median is 15.5 ms (2.19%) below the earlier combined
candidate's 709 ms. Its 1,000-drink median is 186.5 ms (20.93%) below the
891 ms baseline. These nonconcurrent comparisons do not isolate the
profile's effect. The latter also includes the earlier packaging and inset
changes. The rebuilt source includes the committed navigation correction
and a new test package identity.

The first 1,000-drink attempt reached its cutoff after two complete pairs
and one valid unpaired Original observation. The next Comet launch was
refused before it started. All records remain saved. The separately
authorized full study did not append, replace, or pool those observations.
It used the same installed APKs and existing fixture namespaces without
another build, install, import, or repeat of the completed 100-drink study.

The 100-drink condition records showed 25.2-26.1 C against a 26.1 C baseline.
The full 1,000-drink records showed 25.9-26.8 C against a 25.6 C baseline.
Battery stayed at 100%, with USB power and thermal status NONE.
All three animation scales stayed at zero. The full 1,000-drink pilot ran
from 20:01:45 to 20:01:57 UTC and the batch from 20:01:57 to 20:09:00 UTC.
Data checks and restoration finished at 20:09:08 UTC. The final scoped
check at 20:10:20 UTC recorded HOME, no comparison processes, unchanged
APKs/settings/display, and the required ART states. This is a recorded
observation, not a current device check.

Preparation of the full 1,000-drink follow-up used four untimed main
launches: one Original and three Comet. Two Comet inspections failed with
a null accessibility root. The successful check used bounded same-PID
observations without repeating navigation. The third Comet launch had no
separate preceding authorization record. Later permission was not applied
retroactively; the observed evidence was accepted for future collection,
with this deviation recorded. All failed attempts remain saved, and no
preparation value is a timing sample.

Independent raw replay reproduced both complete studies. The 1,000-drink
replay matched 15 outputs byte-for-byte, including samples, dispositions,
analysis and charts. No new TTFD, usable-content, transition, rendering or
memory result is claimed.

## APK size and baseline memory

| Artifact | Signed bytes |
|---|---:|
| Original | 37,023,552 |
| Baseline Comet | 37,655,924 |
| Barista-only payload candidate | 26,294,642 |
| Combined payload and inset candidate | 26,298,738 |
| Applied ART-profile candidate | 26,311,173 |

The profiled APK is 12,435 bytes larger than the combined candidate.
Its 11,613-byte install-time `.dm` is separate and is not included in APK size.

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

These are pre-optimization diagnostics, not a breakdown of the combined
candidate's 709 ms or the new profiled medians.
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
Private default Review values, ordering and database byte equality were
not compared in the profile reruns. Preservation evidence is limited to
the recorded operations, fixture/receipt checks, native observations and
selector restoration; it is not a general loss-free claim.

The measured APK identities are:

| Artifact | SHA-256 |
|---|---|
| Original | `73de0a0bde231f489fd828bcc31cc8456e660683a9f69e486e4d7cab5b36b7af` |
| Baseline Comet | `ec272ab0d602514848a7b5aba8dbc4a6cb5dc8a11bdab26c6fb1d9fa441c8e99` |
| Combined Comet | `04ca980a4c6cc1d7e628f16d9fee55dbf9da9a509cd8946e9011e3626a418013` |
| Profiled Comet | `9bdb8a3837b840e19083a365ff28783cc9e0ac2c74bc4379b3f988294c045a32` |
| Profiled Comet install-time DM | `3cd6833f4035634a5cc76129fe2fa41ec889a53f17d4e752eb67c05fdcbcad22` |

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
The earlier combined analysis is `ttid-insets-100-report-01/analysis/analysis.json`,
SHA-256 `c2c8be8eb0b95e7ce07bf2aa0bb1df1f23c9880945704b38ba08a9a2729b7baf`.

The profile-rerun records are:

| File under the evidence directory | SHA-256 |
|---|---|
| `ttid-art-profile-01/report-100-02/analysis/analysis.json` | `b737d4fcc2f5f77dd774907e8e0ad05e2837b7132b547f2f40c5a8424c418f2d` |
| `ttid-art-profile-01/coordinator-replay-100-01/verification.json` | `d695e5c57740c9dbfc5771c5b38707cb2ca838fbaa93c2f7e52ed5fb4e014f23` |
| `ttid-art-profile-1000-followup-01/report-1000-01/analysis/analysis.json` | `652786e415c1fd2f4cc53e9775ac4ab63194e73644d67d0862214e0d81eac83c` |
| `ttid-art-profile-1000-followup-01/review-01/raw-replay-01-verification.json` | `9e78f67f8e4c9471a49bc019b15f1ad091f434336d036ff032736877f2370372` |
| `ttid-art-profile-1000-followup-01/handoff-01/handoff.json` | `9fafe7ad0f88574d9a774552c89f758133ac3c2934cae531aa0b9e28198c8638` |

Before resuming startup, obtain the user's direction. Use new output folders
and fresh bounded device authorization; all earlier windows are expired.
Do not clear data, repeat completed imports, reset runtime profiles or reuse
unverified device state. Preserve matched fixtures, all valid slow trials,
and exact source/build/artifact identities.
