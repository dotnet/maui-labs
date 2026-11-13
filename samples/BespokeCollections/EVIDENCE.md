# Mobile masonry execution evidence

**Execution dates:** 2026-10-12 and 2026-10-13. **Status:** implementation and Release virtual-device behavior
proof; **not physically performance-qualified**.

This is evidence for the narrow [sample](README.md), not a benchmark of all MAUI
collections, a native-versus-MAUI-template comparison, or certification of the
other skill recipes. Rendering uses actual UIKit/RecyclerView controls. HTTP is
the deterministic data fixture; no WebView is involved.

## Build and device manifest

| Dimension | iOS | Android |
|---|---|---|
| Build | Release, `net10.0-ios26.5`, `iossimulator-arm64` | Release, `net10.0-android36.0`, `android-arm64`, standalone APK |
| Native host | `UIKit.UICollectionView` | `AndroidX.RecyclerView.Widget.RecyclerView` |
| Custom geometry | `PhotoMasonryHandler.MasonryLayout` | `PhotoMasonryHandler.MasonryManager` |
| Runtime | iOS 27.0 simulator, simulated iPhone 18 Pro | API 36 Google APIs emulator, Pixel 6 arm64 |
| Viewport in proof | 402 x 675.667 logical units | 411.429 x 736.762 logical units |
| Toolchain | SDK 10.0.401, workload set 10.0.401, MAUI 10.0.110 | Same |
| Native SDK | Apple 26.5.10318, build-scoped Xcode 26.6 | Android 36.1.69 |
| Inspection agent | Not included in Release | Not included in Release |
| Native profiler | Instruments 27.1 | On-device Perfetto, trace processor 58.2 |

Both target builds completed with zero warnings/errors. The focused portable
solution exercises 27 cases covering geometry/reference intersections, width
reflow, cursor single flight/epochs/failure/pruning, shared thumbnail demand,
cache eviction, fixture determinism, sync/async decoder disposal and report
serialization/provenance.

The fixture represents 100,000 logical records per query, not 100,000 resident
cards. Page size is 50; retained metadata is capped at 500. Image sizes are known
before download. Encoded and applied decoded budgets are 4 MiB and 16 MiB,
respectively. Two HTTP requests and two full decoder/preparation operations may
run concurrently. The queue is bounded to 64 thumbnail keys.

## Native Release behavior proof

Both platforms passed the expanded **22-assertion** proof. It checks real host
types; settled frames/captions/visible keys against metadata geometry; compatible
native reuse; image identity; visible HTTP 503 state, data retention and retry;
query epochs; 500-record pruning; surviving keyed relative anchors; image
budgets; source/handler replacement and decoded-resource teardown.

The proof performs warm and repeat nonoverlapping scroll jumps and can add four
sustained forward/reverse passes. It waits for actual layout and completed source
updates. Checking only an offset setter would allow queued scrolls to collapse
into one layout and falsely claim reuse.

The following snapshots came from successful sustained-scroll runs. The iOS run
includes the final display-scoped image-ownership fix. Counts are cumulative for
the primary handler, before replacement:

| Metric | iOS | Android |
|---|---:|---:|
| Warm / repeat created cells | 15 / 16 | 12 / 12 |
| Primary created cells | 18 | 14 |
| Peak realized cells | 8 | 12 |
| Primary bind / recycle counts | 278 / 262 | 286 / 275 |
| Primary retained items | 500 | 500 |
| Pages loaded / prefix items evicted | 11 / 50 | 11 / 50 |
| Peak applied decoded bytes | 5,543,424 | 8,773,632 |
| Encoded bytes at primary snapshot | 4,140,021 | 4,092,333 |
| Rejected stale publications | 3 | 2 |
| Thumbnail errors | 0 | 0 |

One extra UIKit cell during repeat fits the proof's declared warm-plus-eight
growth tolerance; it is not described as zero allocation. Snapshot image counts
depend on asynchronous arrival. The budgets are enforced independently from the
number of retained records.

Primary and replacement snapshots are separate. The replacement has 50 items,
new native cells and successful new image publication; the old source rejects
new work and the old handler's decoded ownership reaches zero.

A separate Android landscape run passed all 22 checks with a verified
865.524 x 258.667 logical-unit viewport and four columns. Warm/repeat creation
was 14/14 and peak realization 12. This proves width-driven reflow on that
emulator, not physical rotation/input performance or continuous gesture anchoring.

### Final run-identified verification

After adding report provenance, fresh sustained-scroll runs on 2026-10-13 passed
all 22 checks again:

| Target | Run ID | UTC completion | Warm / repeat created | Peak realized |
|---|---|---|---:|---:|
| iOS portrait | `488a0200-83e0-429f-a392-34be6fe4a9a6` | 07:49:09 | 15 / 16 | 8 |
| Android landscape | `c38f90e4-7d1b-44e4-a0d5-5f20778ee66c` | 07:47:57 | 14 / 14 | 14 |

Both reports identify `Release` and `scrollWorkload: true`, retain 500 records,
evict 50 prefix records, and record zero thumbnail errors. The full reports are
`ios-final-20261013.json` and `android-final-20261013.json` in the session artifacts.
Timing-dependent counts are not substituted into the earlier trace's manifest.

With the fixture deliberately stopped, a separate Android invocation produced a
fresh `status: "failed"` report with an explicit transport error, its own run ID
and timestamps, and no success assertions. It did not reuse the previous passing
report; it is preserved as `android-negative-20261013.json`. This verifies failure reporting; it is not a failed correctness run being
presented as a pass.

### Defects exposed and corrected

- **Android pool churn:** real nonoverlapping jumps exceeded a small default
  per-type pool. The custom manager now retains one peak realization window per
  type, measured from viewport/overscan demand, rather than using a large
  dataset-sized cache. A repeat traversal creates no additional holders in the
  recorded run.
- **UIKit batch/scroll ordering:** an asynchronous batch completion could restore
  its captured old offset over a later requested scroll. Newer scroll requests
  take precedence, and proof readiness includes settled source updates.
- **UIKit image ownership:** sustained traversal exposed that weak diagnostic
  cell references alone cannot own decoded resources. Image demand now follows
  display callbacks; a bounded strong set owns displayed cells, end-display
  releases demand/pixels, and reentry resumes work without recreating the card.
- **C# preparation binding:** the synchronous UIImage preparation binding
  enforced UI-thread access. Async native preparation is initiated on the owning
  dispatcher; the decoder slot spans native completion and disposal.
- **Standalone Debug deployment:** the sample embeds Android assemblies so
  manual APK installation does not depend on fast-deploy override files.

These corrections came from actual failures; earlier weaker passing reports are
not substitutes for the final proof.

## Android native FrameTimeline diagnostic

A 30-second Release trace includes a **10.190823505-second**
`MasonryScrollWorkload` async span and 611 application surface-frame records
within that span. The stimulus requested 50-unit steps with 16 ms delays;
those delays are not frame measurements.

The analysis selected only the sample process and workload span. For presentation
intervals, application frames were joined by `display_frame_token` to
SurfaceFlinger's actual timeline; distinct composition/presentation end timestamps
were ordered and differenced. Application actual-frame durations are reported
separately: their end is GPU/post completion, not on-screen presentation.

| Diagnostic | Result |
|---|---:|
| Compositor-linked intervals | 610 |
| Interval p50 / p95 / p99 | 16.683 / 18.228 / 21.875 ms |
| Maximum interval | 33.272 ms |
| App-frame duration p50 / p95 / p99 | 2.993 / 4.241 / 5.527 ms |
| Maximum app-frame duration | 18.145 ms |
| App on-time-finish misses | 1 / 611 |
| Parser/error statistics with positive values | None |

All 611 app records had **Prediction Error** classifications; 610 were Early
Present and one also had App Deadline Missed/Late Present. Emulator scheduling
therefore limits deadline interpretation. The interval p95 also exceeds the
research's illustrative 16.67 ms starter goal. **This is not a passing physical
frame-budget result.** No native Java reference or retained-MAUI renderer baseline
was measured.

One post-workload `dumpsys meminfo` sample reported 144,186 KiB total PSS,
242,108 KiB RSS and 18 tracked bitmaps totaling 9,932 KiB. This single snapshot
does not prove a memory plateau; it is separate from the applied-pixel ledger.
Native/process/GPU memory and sustained growth still require physical measurement.

## iOS trace outcome

Instruments **Animation Hitches** explicitly failed with:
`Hitches is not supported on this platform.` The partial trace was malformed
and could not be exported. This is missing evidence, not zero hitches.

Time Profiler successfully captured/exported CPU samples, with a warning about
one table without a known input source. An exact-PID attachment also succeeded.
These are simulator CPU diagnostics, not presented-frame or hitch measurements,
and no CPU sample percentile is being substituted for those metrics.

## Artifact provenance and remaining gates

Full native JSON reports, the Android `.pftrace`, the exact capture configuration,
Instruments trace bundles and exported tables were preserved in this Copilot
session's `files/masonry-mobile/` artifact directory. Large binary traces are
not committed to the sample. Relevant reports are `android-profile-proof.json`
and `ios-final-scroll-proof.json`; `android-landscape-proof.json` records the
separate four-column run. Failed reports remain distinguishable from
passing ones. A rerun deletes its own previous report and writes explicit failure
JSON if a check fails. Reports now include a unique run ID, UTC start/completion
timestamps and whether the optional scroll workload ran, in both success and
failure shapes.
The capture configuration and actual frame-analysis queries are also checked in
as [android-scroll.pbtxt](Profiling/android-scroll.pbtxt) and
[android-frames.sql](Profiling/android-frames.sql). The SQL reproduces the recorded
presentation/app-duration statistics; it does not reinterpret absent data as a
passing result.

The entry skill is individually distributable without these artifacts or a local
SDK. To reproduce, follow [README.md](README.md), save the complete report rather
than truncated logcat JSON, and record the actual control revision/toolchain/
hardware/cadence for the new run.

**Still required before phase 2 performance qualification:** physical Android/iOS
devices and calibrated frame/memory/loading budgets; real native hitch/presentation
traces; repeated warm-run memory plateau; and full interaction/accessibility/
rotation validation. The connected physical iPhone was explicitly unavailable;
no physical Android target was reachable. Android's initial orientation report
lagged the actual display; the four-column run was accepted only after verifying
native viewport dimensions. iOS rotation and comprehensive input/accessibility
remain unverified.
Desktop targets, groups/chrome, arbitrary edits, dynamic text and MAUI template
comparisons belong to later slices.
