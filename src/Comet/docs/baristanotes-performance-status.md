# BaristaNotes performance investigation report

Closeout at 2026-09-24: **startup, APK size, the full memory study and the
requested two-route page study are recorded here.** The page study has ten
samples per app and route with 1,000 drinks. The earlier larger page-test
plan is stopped, not complete. No new device measurements ran for this report.

This records bounded experiments, not a general framework ranking.
The comparison uses Original BaristaNotes (MAUI, MauiReactor and EF Core/SQLite)
and the Comet rewrite (Jetpack Compose and its SQLite store).
It does not isolate framework overhead.

## Main results

| Measure | Result | Scope |
|---|---|---|
| Page transition: New Drink -> Activity | Comet median 1,490.6 ms; Original 1,854.5 ms. Comet was 19.6% lower. | Ten samples per app, 1,000 drinks; instrumented builds. |
| Page transition: Activity -> Settings | Comet median 399.9 ms; Original 247.8 ms. Comet was 61.3% higher. | Ten samples per app, 1,000 drinks; Original requests an extra draw. |
| Memory, full study | Comet used 20.6-23.6% less proportional set size (PSS). | Ten paired process runs per dataset, three screen states, 100 and 1,000 drinks. |
| Startup, applied ART profile | Comet median TTID 693.5/704.5 ms; Original 317.0/321.5 ms. | 100/1,000 drinks; first display, not usable-page time. |
| Signed APK size, applied ART profile | Comet 26,311,173 bytes; Original 37,023,552 bytes. | Startup/memory artifacts, not the later page-instrumented APKs. |

Comet did not win every measure. It used less memory and had a lower Activity
transition median, but a higher Settings transition median and higher startup
TTID. These separate studies used different artifacts and methods. Do not
combine them into one overall score.

## Product goal

Jetpack Compose on Android and SwiftUI on Apple are fixed goals for Comet.
The C#/Java bridge, state representation and callback implementation can
change while retaining these native frameworks and their real controls.

The target is competitive startup and interaction performance without an
intermediate blank page. A plain .NET for Android app could help separate
runtime and higher-level costs, but no such BaristaNotes app has been built
or authorized as part of the next step. There is no matched Kotlin Compose,
Flutter or React Native result in this study.

## Page transitions: 1,000 drinks

The 2026-09-24 study collected 40 successful Pixel 5 measurements: two apps,
two routes and ten samples per app/route. Both apps were Release, Android
arm64, .NET 11 Native AOT builds. No failed Pixel trial or slow value was
removed. These results replace the earlier screenshot-based readings, which
used unequal data and included host and image-transfer time.

| Route | Original p50, ms | Comet p50, ms | Original p90, ms | Comet p90, ms |
|---|---:|---:|---:|---:|
| Shot / New Drink -> Activity | 1,854.5 | 1,490.6 | 1,873.3 | 1,624.9 |
| Activity -> Settings | 247.8 | 399.9 | 253.2 | 450.2 |

Comet was faster in nine of ten numbered Activity pairs. Its 2,165.3 ms
Activity sample remains included. Original was faster in all ten Settings
pairs. The percentage changes compare app medians, not the median of paired
percentage changes.

### Timing boundary and content checks

The timer starts at the existing navigation action-handler entry and ends
at the Android frame-commit callback for content that passed the app-state
and native-view checks. Both timestamps use
`SystemClock.ElapsedRealtimeNanos()`.

The Activity check requires the active Activity page, completed loading,
the All filter, `1000 shots`, the expected first 50 records, and matching
visible native rows, text and layout. The Settings check requires completed
preference/theme state and matching native content and layout.

No screenshot, USB round trip or host wait is inside the interval. A native
tree lookup finds the button before the tap. The host waits 15 seconds after
the action before it reads the result; this wait is not part of the duration.
Input-event-to-handler delay is excluded. The callback does not identify the
exact time that pixels reach the display.

### Data and conditions

Both apps used `barista-perf-v2-1000`. Current database readback was validated
before each trial in both apps. The common input SHA-256 is
`58643ebd288f83b11259226b711bd0062d7b249ef369d18c39c2950b238aa115`;
the common data-content SHA-256 is
`eb97125e964e9504ce498e1f1bd85d297a1234d56eb2db00e7b5c14da68f092f`.

Each app/route/sample used a fresh process: 40 distinct process IDs.
Settings trials first navigated New Drink -> Activity outside the measured
interval. Normal preloading and caching stayed enabled. Database validation
reads data before navigation, so these are not cold-storage measurements.
Odd-numbered pairs ran Comet first; even-numbered pairs ran Original first.

Collection ran from 21:36 to 21:56 UTC. Sample 1 for each route came from the
initial Pixel check. Its app observations were several minutes apart while
the final Original emulator check finished. Samples 2-10 used the same
builds in alternating app order.

All three system animation scales were zero and stayed unchanged. The phone
was USB powered at 100% battery. Temperature was 25.8 C during preparation
and 26.7 C after collection; thermal status was 0 at the checks. Both timing
apps were stopped after collection and their stored datasets were retained.
These are recorded conditions, not a current device check.

### Samples and histogram

All values below are milliseconds, rounded to one decimal for display.
Statistics use the full-precision records. The p50 is the median; p90 is
nearest rank, the ninth sorted value from ten samples. This differs from
the type-7 percentile method in the startup studies. Ten samples do not
support a reliable p99 or a statistical significance claim.

| Sample | Original New Drink -> Activity | Comet New Drink -> Activity | Original Activity -> Settings | Comet Activity -> Settings |
|---|---:|---:|---:|---:|
| 1 | 1859.2 | 1524.3 | 260.3 | 386.0 |
| 2 | 1823.7 | 1352.7 | 246.8 | 414.2 |
| 3 | 1851.4 | 2165.3 | 241.8 | 399.4 |
| 4 | 1856.3 | 1456.9 | 252.2 | 395.3 |
| 5 | 1833.8 | 1526.5 | 241.5 | 450.2 |
| 6 | 1854.1 | 1115.1 | 246.1 | 451.6 |
| 7 | 1843.9 | 1346.8 | 253.2 | 397.8 |
| 8 | 1873.3 | 1399.3 | 244.2 | 400.4 |
| 9 | 1855.0 | 1559.8 | 248.9 | 423.5 |
| 10 | 1878.0 | 1624.9 | 249.3 | 392.8 |

Bins include the lower boundary and exclude the upper boundary.

| Duration bin | Original New Drink -> Activity | Comet New Drink -> Activity | Original Activity -> Settings | Comet Activity -> Settings |
|---|---:|---:|---:|---:|
| 0-250 ms | 0 | 0 | 7 | 0 |
| 250-500 ms | 0 | 0 | 3 | 10 |
| 500-1,000 ms | 0 | 0 | 0 | 0 |
| 1,000-1,500 ms | 0 | 5 | 0 | 0 |
| 1,500-2,000 ms | 10 | 4 | 0 | 0 |
| 2,000 ms or more | 0 | 1 | 0 | 0 |

### Measurement limits and artifact identity

Original's normal startup preload was initially rejected as a previous
Activity visit. That test-only restriction was removed. The Settings
observer was also corrected to retain candidate-frame evidence and check
the accepted frame.

**Original requests a draw after Settings passes its content check.** A
same-value state update could otherwise leave the callback waiting for
another hardware frame. Its measured duration includes this request; Comet
did not need it. Native content inspection and observer code also run inside
the interval. Their implementations differ, and their overhead was not
measured separately. These are instrumented-app observations, not pure
render time. The cause of the Settings difference is not established.

The APKs contain the same fixture asset and their respective native
libraries, `libBaristaNotes.so` and `libCometComposeProbe.so`. They used SDK
`11.0.100-rc.1.26425.128`. Existing Original trimming/AOT warnings were not
suppressed. Comet contains its ART baseline profile, but this page study
does not establish an applied ART compilation mode.

| Page-study artifact | SHA-256 |
|---|---|
| Original, instrumented | `12e6809b39f6a8042a5e7d177133c36fb906d2546b299ba18db78558957376b1` |
| Comet, instrumented | `9054675443a202c76298be19615bffe0dc8a4dd4ab137c45653362fd8d9c556c` |

Earlier emulator failures remain separate development evidence, not Pixel
samples. No input-queue delay, jank, observer-cost comparison, or physical
presentation time was measured.

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
analysis and charts. Those startup studies did not measure TTFD, usable
content, transitions, rendering or memory. The later page and memory
studies are reported separately.

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

## Full memory study: 100 and 1,000 drinks

The 2026-09-24 study used the clean Original and applied-profile Comet APKs
listed below, not the later page-instrumented builds. It completed 40 app
runs: ten paired process runs per dataset, with three states in each run.
The 120 run/state observations used 360 snapshot groups and 640 navigation
actions. No command failed and no sample was excluded or retried. The
earlier eight-run pilot is separate and is not included.

PSS includes private resident memory plus the process's proportional share
of shared memory. Each run/state value is the median of three snapshots
after a ten-second settle. Each table value is then the median of ten
independent process-run values. Android-reported kB are divided by 1,024
to obtain MiB. Effects are calculated before rounding.

| Drinks | Screen state | Original PSS, MiB | Comet PSS, MiB | Difference, MiB | Difference | Paired 95% interval, MiB |
|---|---|---:|---:|---:|---:|---:|
| 100 | Initial New Drink | 169.27 | 132.95 | -36.32 | -21.46% | -36.83 to -34.52 |
| 100 | First Activity visit | 173.05 | 137.36 | -35.68 | -20.62% | -36.36 to -35.23 |
| 100 | Activity after repeated navigation | 183.62 | 145.78 | -37.84 | -20.61% | -39.05 to -36.79 |
| 1,000 | Initial New Drink | 176.91 | 135.21 | -41.70 | -23.57% | -41.82 to -40.45 |
| 1,000 | First Activity visit | 182.53 | 139.66 | -42.87 | -23.49% | -43.66 to -42.67 |
| 1,000 | Activity after repeated navigation | 190.85 | 150.04 | -40.81 | -21.39% | -42.09 to -39.48 |

Differences are Comet minus Original medians. The analysis uses 2,000 paired
bootstrap draws with seed `20260922`. It resamples complete paired process
runs, keeping their states and metrics together. There is one observed block
per dataset, with five Original-first and five Comet-first pairs. All six
PSS intervals exclude zero in these blocks; they do not establish results
for other devices.

Repeated navigation means five cycles of Activity -> Settings -> New Drink
-> Activity after the first Activity visit. Both apps retain more PSS after
this sequence. This does not establish a memory leak.

### Supporting memory measures

Private dirty counts modified private pages. Resident set size (RSS) counts
resident pages for the main process; it is not added across processes.
Each run had one verified app process.

| Drinks | Screen state | Original private dirty, MiB | Comet private dirty, MiB | Original main RSS, MiB | Comet main RSS, MiB |
|---|---|---:|---:|---:|---:|
| 100 | Initial New Drink | 106.85 | 76.06 | 281.76 | 246.14 |
| 100 | First Activity visit | 109.93 | 79.05 | 286.23 | 251.61 |
| 100 | Activity after repeated navigation | 120.11 | 87.38 | 297.00 | 260.45 |
| 1,000 | Initial New Drink | 113.22 | 78.04 | 289.01 | 248.30 |
| 1,000 | First Activity visit | 119.25 | 81.06 | 295.50 | 253.58 |
| 1,000 | Activity after repeated navigation | 126.99 | 90.96 | 304.20 | 264.38 |

### OS PSS categories after repeated navigation

Values are MiB, using the same median-of-three then median-of-ten reduction.
Native Heap and Private Other have the largest differences in this state.
These Android labels do not identify C# allocations or isolate framework cost.

| Category | 100 Original | 100 Comet | 1,000 Original | 1,000 Comet |
|---|---:|---:|---:|---:|
| Java Heap | 7.113 | 12.492 | 6.959 | 12.449 |
| Native Heap | 22.992 | 10.633 | 23.477 | 10.693 |
| Code | 57.398 | 52.510 | 57.160 | 52.672 |
| Stack | 0.572 | 0.436 | 0.582 | 0.439 |
| Graphics | 43.771 | 43.250 | 43.621 | 43.076 |
| Private Other | 46.404 | 20.721 | 52.785 | 24.748 |
| System | 6.253 | 6.079 | 6.256 | 6.114 |
| Sum of category medians | 184.505 | 146.120 | 190.840 | 150.192 |
| Median total PSS | 183.616 | 145.779 | 190.854 | 150.040 |

The categories sum to total PSS in each of the 360 raw snapshots. Their
medians need not sum to median total PSS. No category was rescaled.

Collection ran from 04:10:50 to 06:15:05 UTC; cleanup ended at 06:15:11 UTC.
No new APK, install, import, reset, forced garbage collection or ART change
was needed. Comet remained `speed-profile/install-dm`; Original remained
`verify/install`. Both datasets passed the recorded checks. Review was
restored and both apps were stopped. This is historical state.

The saved offline replay reproduced 103,724 generated files exactly, with
four further bound input files checked and no device commands. Separate
checks covered the numeric summaries and category reductions. The analyzer
retains legacy `pilot` / `PILOT_NOT_FINAL` labels, but the memory contract
states `phase: collection`: all 18 metric/state/dataset cells have ten pairs.
This does not mean that the separate page-test matrix is complete.

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

The startup and memory Android Native AOT builds used SDK
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

Both apps passed full fixture and native-content checks. In the profile
startup reruns and full memory study, Comet had fresh canonical exports; Original had
unchanged exports from its completed operation, not a fresh live database
export. Its native content and selected fixture passed every launch.
Both apps were restored to Review and stopped after collection. This is
historical state, not a current device check.
Private default Review values, ordering and database byte equality were
not compared in those studies. The later page trials checked current database
readback in both apps. Preservation evidence is limited to
the recorded operations, fixture/receipt checks, native observations and
selector restoration; it is not a general loss-free claim.

The startup and memory APK identities are:

| Artifact | SHA-256 |
|---|---|
| Original | `73de0a0bde231f489fd828bcc31cc8456e660683a9f69e486e4d7cab5b36b7af` |
| Baseline Comet | `ec272ab0d602514848a7b5aba8dbc4a6cb5dc8a11bdab26c6fb1d9fa441c8e99` |
| Combined Comet | `04ca980a4c6cc1d7e628f16d9fee55dbf9da9a509cd8946e9011e3626a418013` |
| Profiled Comet | `9bdb8a3837b840e19083a365ff28783cc9e0ac2c74bc4379b3f988294c045a32` |
| Profiled Comet install-time DM | `3cd6833f4035634a5cc76129fe2fa41ec889a53f17d4e752eb67c05fdcbcad22` |

These identify measured artifacts, not a promise that rebuilding a later
commit produces identical APK bytes.

## Committed progress and remaining questions

The implementation progress is already retained in these commits:

| Commit | Changes |
|---|---|
| `d2b1c2e3bbd5a12eb913be6915e1e0609f3ad58e` | Retained section roots, layout/measurement caches, narrower editor inset updates, smaller standalone sample payload, Native AOT support, fixture tools and regression checks. |
| `ad4fd380b8629b339929eb70309f6042f9f92719` | Pinned Compose ART-profile generation and packaging, profile checks, and accepted Pixel 5 startup results. |

This closeout updates documentation only. Page-observer changes and the
larger experimental test system remain in isolated evidence snapshots, not
in these product commits. They are not additional product performance fixes.

The requested ten-sample, 1,000-drink, two-route study replaced the earlier
100-drink/30-pair plan. The wider 14-hop, two-dataset, first-use/warm matrix
was not completed and is not required to close this request.

Open questions are usable startup/TTFD, input-event-to-handler delay,
observer overhead, the Settings difference, current bridge costs, unmeasured
routes and warm transitions, jank, and physical presentation. The results
above do not resolve those questions. Additional experiments need a separate
scope; no further collection is scheduled by this report.

Earlier ordinary Debug transition diagnostics used host tap-to-DevFlow-registry
observation, not native rendering or presentation. Their gains motivated
retained section roots and layout caches, but are not the Pixel Native AOT
app-pair benchmark. Keep the data sets separate.

## Retained evidence

Full raw data, signed APKs, isolated source snapshots and detailed handoffs
are retained outside the repository in the originating session's
`files/android-aot-comparison/20260921-pixel5-v2` evidence directory, under
session ID `aac04559-3f94-4b1c-bd96-ff00e87f411e`. This report retains the
results, rounded page samples, histogram and limits in the repository.
The full raw archive and experimental test system are not distributed here.

Start with `comparison-report-02/startup-resume.md`,
`comparison-report-02/page-timings-1000.md` and
`comparison-report-02/comparison-summary.md`. The original baseline report,
failed attempts and all accepted records remain unchanged. The summary
also contains old progress entries; this report's closeout scope and the
separate page-timing report supersede those earlier pending-work statements.
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

The new records below are relative to
`page-performance-resume-20260923-01/` inside the same evidence directory.

| File | SHA-256 |
|---|---|
| `run1000/samples.csv` | `76de5b9e632415a5463629134ad78562316aacc2c8ceb37d0216fbd82f0a3613` |
| `run1000/summary.json` | `db347835386c38e6dacb8259dfc8124411e7408e62665445c6c88a0d5a7d53a6` |
| `run1000/apk-identities.json` | `dd2f7498dd91bfc0e1dc92ac123317280c06cc90057bab0a979327d89437078f` |
| `memory/implementation-03/full-collection-01/coordinator-replay-01/analysis/analysis.json` | `c78b7985d8f3fb766f40f24a373115d8aeee77be50d9fbf93841c18daed30cec` |
| `memory/implementation-03/full-collection-01/coordinator-replay-01/analysis/samples.csv` | `addead072db1ec71274a0d249ad9fbeab947b218e05d17866ac5b08b95d5e32e` |
| `memory/implementation-03/full-collection-01/coordinator-replay-01/verification.json` | `1aad3b6ca23022311ee54949432303dfd569e4221e8da8090b43344a66c0c50e` |

Before resuming measurement, obtain the user's direction. Use new output folders
and fresh bounded device authorization; all earlier windows are expired.
Do not clear data, repeat completed imports, reset runtime profiles or reuse
unverified device state. Preserve matched fixtures, all valid slow trials,
and exact source/build/artifact identities.
