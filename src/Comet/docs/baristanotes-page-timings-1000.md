# BaristaNotes page timings: 1,000-record dataset, Native AOT

**Collected on Pixel 5, 2026-09-24.** Ten samples per route per app:
40 successful measurements, with no failed Pixel trials or discarded values.
Both apps used the same approved 1,000-drink dataset.
The Activity destination queried the total count and loaded the first 50 rows.

## Results

All values are milliseconds. Lower is better.

| Route | MAUI p50 | Comet p50 | MAUI p90 | Comet p90 |
|---|---:|---:|---:|---:|
| Shot / New Drink -> Activity | 1,854.5 | 1,490.6 | 1,873.3 | 1,624.9 |
| Activity -> Settings | 247.8 | 399.9 | 253.2 | 450.2 |

For Shot -> Activity, Comet's median was **19.6% lower**. Comet was faster in
9 of 10 numbered pairs. One Comet sample took 2,165.3 ms and is retained.

For Activity -> Settings, Comet's median was **61.3% higher**. MAUI was faster
in all 10 numbered pairs.

These are observations for these instrumented app builds on this device, not
a general claim about either framework. Ten samples show a useful direction,
but do not support a reliable p99 or a statistical significance claim.

## What the timer measured

The start was the app's existing navigation action-handler entry. The end was
the Android frame-commit callback for destination content that passed the
app-state and native-view checks. Both timestamps used
`SystemClock.ElapsedRealtimeNanos()`.

The Activity check required the Activity page, loading complete, All filter,
`1000 shots`, the expected first 50 records, and the visible native rows and
their text/layout. The Settings check required completed preference/theme
state and matching native page content and layout.

**No screenshots, USB round trips, or host waits are inside the measured
interval.** Native visual-tree lookup located the button before each tap.
The host then remained quiet for 15 seconds before reading the recorded result.
This wait is not part of the reported duration.

The start does **not** include the delay between the original input event and
handler entry. The end is frame-commit notification, not the exact time at
which the display shows the pixels. This run used the existing handler-entry
instrumentation rather than adding another input-event timing system.

## Data and run conditions

| Item | Value |
|---|---|
| Device | Physical Pixel 5, Android 14; serial `13041FDD4007MT` |
| App builds | Release, Android arm64, .NET 11 Native AOT |
| Instrumentation | Separate page-timing builds with in-app readiness and frame observers |
| Toolchain | Isolated SDK `11.0.100-rc.1.26425.128` |
| Dataset | `barista-perf-v2-1000`, 1,000 drinks in both apps |
| Activity query | Total-count query plus the first 50 records |
| Dataset input SHA-256 | `58643ebd288f83b11259226b711bd0062d7b249ef369d18c39c2950b238aa115` |
| Data-content SHA-256 | `eb97125e964e9504ce498e1f1bd85d297a1234d56eb2db00e7b5c14da68f092f` |
| MAUI timing APK SHA-256 | `12e6809b39f6a8042a5e7d177133c36fb906d2546b299ba18db78558957376b1` |
| Comet timing APK SHA-256 | `9054675443a202c76298be19615bffe0dc8a4dd4ab137c45653362fd8d9c556c` |
| MAUI stored dataset | `fixture-v2-1000-gtpix339c09f1` |
| Comet stored dataset | `fixture-v2-1000-artpix01` |
| Process policy | Fresh process for each app/route/sample; 40 distinct PIDs |
| Settings setup | New Drink -> Activity first, outside the measured interval |
| Data check | Current database readback validated before each trial in both apps |
| App order | Odd-numbered pairs Comet first; even-numbered pairs MAUI first |
| Pixel sample period | 21:36-21:56 UTC |
| System animation scales | Window, transition, and animator scales were all 0; left unchanged |
| Battery / thermal | USB powered, 100%; 25.8 C during preparation, 26.7 C after; thermal status 0 at the checks |
| Post-run state | Both timing apps stopped; saved datasets retained |

Normal app preloading and cache behavior were preserved. A fresh process does
not imply cold filesystem, database, or OS caches. Database validation itself
reads the data before navigation.

Sample 1 in each app was collected during the initial Pixel check. Its two
members were several minutes apart while the final MAUI emulator check
finished. Samples 2-10 were collected in alternating app order without build
changes. The raw timestamps retain that gap.

The Android system animation scales were equal. App-owned animation work was
not separately measured or subtracted. Comet's APK contains its ART baseline
profile; this run does not claim that a particular ART compilation mode was
applied.

## Measurement-code repairs and limits

The earlier test code rejected MAUI's normal startup preload as a previous
Activity visit. That test-only rejection was removed. The actual source page
and one-measured-action-per-process checks remain.

The Settings observer incorrectly allowed only one candidate frame. It now
retains candidate evidence and checks the evidence for the accepted frame.
MAUI also requests a draw after its Settings content passes the ready check,
because a same-value state update could otherwise leave the callback waiting
without another hardware frame. **The MAUI Settings duration includes that
requested draw.** The Comet path did not need this extra request.

Content inspection and instrumentation execute inside the app and have
overhead. Their implementations differ between MAUI and Compose. Observer
overhead was not measured separately. No jank metrics, phase histogram from
production telemetry, or physical-presentation timing was collected.

The raw emulator failures are retained in the external evidence set. They are
development checks, not Pixel samples. They include one host tree-read error
and two Settings observer failures before the fixes. No failed Pixel sample
was retried or removed.

## Samples

The table rounds each duration to one decimal place for display. The p50,
p90, ranges, and percentage differences use the full nanosecond-derived
values in the [committed CSV](data/baristanotes-page-timings-1000.csv).

| Sample | MAUI Shot -> Activity | Comet Shot -> Activity | MAUI Activity -> Settings | Comet Activity -> Settings |
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

The p50 is the median. The p90 uses nearest rank: the ninth sorted value from
ten samples. Published summary values are rounded to one decimal place. No
averages or outlier removal are used.

## Histogram counts

Bins include the lower boundary and exclude the upper boundary.

| Duration bin | MAUI Shot -> Activity | Comet Shot -> Activity | MAUI Activity -> Settings | Comet Activity -> Settings |
|---|---:|---:|---:|---:|
| 0-250 ms | 0 | 0 | 7 | 0 |
| 250-500 ms | 0 | 0 | 3 | 10 |
| 500-1,000 ms | 0 | 0 | 0 | 0 |
| 1,000-1,500 ms | 0 | 5 | 0 | 0 |
| 1,500-2,000 ms | 10 | 4 | 0 | 0 |
| 2,000 ms or more | 0 | 1 | 0 | 0 |

## Evidence

The repository contains the exact
[40-sample CSV](data/baristanotes-page-timings-1000.csv), the
[computed summary](data/baristanotes-page-timings-1000-summary.json), and the
[build and verification manifest](data/baristanotes-page-timings-1000-manifest.json).

The larger evidence set remains outside the repository in the originating
session. It includes raw app exports and command records, the collection and
summary scripts, isolated source snapshots, build logs, APKs, and
emulator-development failures. The timing builds used isolated measurement
source snapshots, not a clean repository commit. The APK hashes above are the
durable identity of the measured binaries.

Both APKs contain the exact 1,000-drink fixture asset and the app's native
library (`libBaristaNotes.so` / `libCometComposeProbe.so`). Both were built
with Native AOT enabled. The existing MAUI trimming/AOT warnings remain in
the build log; they were not suppressed. Three focused compiled readiness
and clock tests passed. All four app/route combinations produced accepted
runtime exports, then the 40 Pixel records passed dataset, trial-ID, process,
sample-count, and timestamp-difference checks.

These results supersede the earlier screenshot-based exploratory numbers.
