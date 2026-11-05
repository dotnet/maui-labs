# BaristaNotes Android scrolling investigation

## Implementation and matched remeasurement

Follow-up completed on 2026-09-24 local time (2026-09-25 UTC). The initial
30-minute investigation is preserved below.

**First-scroll performance improved on the Pixel 5.** Three matched Release
baseline runs had p95 deadline overruns of 182-186 ms. Three candidate runs
had p95 overruns from -0.64 to 1.43 ms. Negative overrun means completion
before the recorded deadline. Jank was reduced, not eliminated.

| Run | Recorded first-scroll frames | App deadline misses | App miss rate | Total jank rate | Overrun p95, ms | Overrun p99, ms | Explicit Java collections |
|---|---:|---:|---:|---:|---:|---:|---:|
| Baseline 02 | 62 | 18 | 29.03% | 75.81% | 182.35 | 200.96 | 49 |
| Baseline 03 | 68 | 17 | 25.00% | 76.47% | 186.43 | 233.37 | 56 |
| Baseline 04 | 59 | 15 | 25.42% | 50.85% | 182.51 | 198.71 | 51 |
| Candidate 01 | 283 | 13 | 4.59% | 5.65% | -0.64 | 32.83 | 9 |
| Candidate 02 | 282 | 14 | 4.96% | 4.96% | 0.31 | 35.61 | 10 |
| Candidate 03 | 283 | 16 | 5.65% | 8.83% | 1.43 | 31.96 | 10 |

The median of the three run-level app miss rates fell from 25.42% to 4.96%.
The candidate produced many more frames during the gestures and scrolled
farther. Miss counts alone changed much less than the rates. Do not describe
the rate reduction as an equal reduction in the number of missed frames.

### Changes, in implementation order

1. **Reduce row-creation allocations.** Per-row diagnostic counters separated
   body creation, backend-node creation and layout. The large allocation was
   in body creation, not primarily in Java-backed state construction.
   Environment writes from row modifiers could run reactive flushes and
   whole-app layout inline, once per modifier. `NativeListRowCache` now holds
   flushes until the complete row is cached. Body-method metadata, including
   negative lookups for ordinary controls, is cached by runtime type.
   Delegates remain instance-bound; metadata updates clear the cache.
2. **Preserve unchanged rows during paging.** `CollectionView.RefreshItems()`
   re-reads the source and retains templates for equal items at unchanged
   indices. Android invalidates only the changed suffix, with independent
   backend ownership for each row. `ReloadData()` still performs a full
   refresh for edits in place. The old independent 150-template eviction
   could dispose rows still retained by the native backend; template lifetime
   now follows explicit invalidation and list disposal.
3. **Restore automatic paging.** The Compose backend observes the actual last
   visible native row, outside composition. Threshold notifications suppress
   duplicates and reentrant calls, and permit another request after item-count
   growth or leaving and reentering the threshold. The SwiftUI visibility
   callback uses the same notification rules. BaristaNotes gives the footer
   its own reactive body and refreshes items without rebuilding the page.

In the emulator allocation diagnostics, the median managed allocation for
materializing rows 9-25 fell from **16,364,080 to 1,617,656 bytes**, about 90%.
Both figures include synchronous work inside the materialization call; the
candidate figure includes release of the flush hold. These are diagnostic
allocation measurements, not Pixel timing results. Temporary counters and
log calls were removed from the final source and timed candidate.

### Matched protocol and functional results

Both measured APKs were built with the same isolated .NET 11 RC toolchain,
Native AOT, arm64, Release configuration, fixture assets and package identity.
The baseline source was a separate archive of HEAD
`2381fb14af2ac34c1881e3da1e8a028abd250406`, not a reversal of working-tree edits.
Neither timed build contains the temporary row-allocation counters.

The new test package is `com.comet.sample.scroll.baristanotes`. It has its own
synthetic 1,000-shot storage namespace. The existing BaristaNotes packages and
their data were not replaced. Each APK replacement used a non-destructive
update of this test package. Before each measured launch, only the test
package was compiled with Android's `speed` mode. No compilation profiles
or app data were reset.

The primary first-scroll phase uses **four**, not twelve, identical 650 ms
gestures from the top, before the paging boundary. Three reverse gestures
and three cached downward gestures follow. Ten additional downward gestures
exercise paging separately. This shorter protocol must not be compared
numerically with the initial 12-gesture investigation.
The six runs were collected in baseline/candidate, baseline/candidate,
candidate/baseline order.

The candidate first-scroll phases ended near shots 968-970, while the baseline
ended near 973-975. The gestures were matched; completed scroll distance and
frame count were not. The primary phase does not include automatic paging.
The paging phase deliberately exercises different behavior: all baseline runs
stopped at shot 951 with `LOAD MORE`; candidate runs continued automatically
to visible shots 905-910. No direct paging-duration speedup is claimed from
these different journeys.

The exact final candidate also passed emulator inspection, including paging
beyond 150 rows and reverse scrolling through retained rows. The Pixel was
left running the candidate test app after a final native content check.
Readback validation preserved the synthetic 1,000-shot fixture.

The broader host suite passed **469 tests**, with one existing skipped test.
It covers backend ownership, reflection, metadata updates, collections,
reactive state, cache misses, retained append rows, suffix disposal, full
reload, threshold boundaries and reentrancy. Android Release, Mac Catalyst
host and iOS compilation passed. The SwiftUI change was compiled, not
device-tested. Cached templates still grow with the number of visited rows;
this change does not introduce a bounded native row-cache eviction policy.

### Follow-up artifacts and limits

| Artifact | SHA-256 |
|---|---|
| Clean baseline APK | `97a5b200f319a8038ac20e2bb820a060678abc2d7bc8e8cc0ea7b1eab6589e8c` |
| Final candidate APK | `8f708d48c4485922fe38d35f3982f7e16f21b2a20471c4c912bf8ccc13cd1aa5` |

Raw runs are `v2-baseline-02` through `v2-baseline-04` and
`v2-candidate-01` through `v2-candidate-03` in the same session evidence folder.
`measure-scroll-v2.py` records commands, fixture validation, APK hashes,
compilation state, native trees, device-clock boundaries and transferred
trace hashes. `analyze-scroll.py` regenerates the frame and collection results.
Run `v2-baseline-01` timed out during initial fixture provisioning, before
measurement. The app was left running to finish that operation; no data was
cleared and no measured result was discarded.

Temperature was 26.8 C before collection and 27.3 C after. Both checks showed
100% battery, USB power, thermal status 0 and unchanged animation settings.
There are only three runs per build on one device. Frame percentiles remain
descriptive. Trace overhead was not measured. The trace processor still
reports setup notices and service-discarded chunks; their affected intervals
were not established. Results concern recorded frames, not a claim of
complete capture or zero jank. The first-pass p99 remains about 32-36 ms late,
so further frame-time work is warranted.

## Initial 30-minute investigation

Date: 2026-09-24. Scope: a 30-minute investigation on the attached Pixel 5.

**The main improvement target is first-time row creation, composition and
layout at the Comet/native-runtime boundary.** Repeat scrolling through cached
rows had far fewer app deadline misses. Loading another page also caused a
long frame. No runtime change or before/after optimization result is claimed.

## Measurements

The app contained the validated 1,000-shot test dataset. Activity initially
loaded 50 shots. These measurements cover normal scrolling through that first
page, not a list with all 1,000 rows already loaded.

Five fresh-process runs used the same installed Release, arm64 Native AOT
artifact. Each first pass used twelve downward-content scroll gestures:
physical coordinates `(540,1850)` to `(540,700)`, duration 650 ms, with a
350 ms host pause after each command. These are controlled drags, not fast
flings. Native trees confirmed the same first and final visible records.

| First downward pass | Recorded frames | App deadline misses | App miss rate | Deadline overrun p95, ms | Deadline overrun p99, ms |
|---|---:|---:|---:|---:|---:|
| Baseline 03 | 305 | 36 | 11.80% | 179.18 | 195.20 |
| Baseline 04 | 319 | 39 | 12.23% | 168.26 | 220.02 |
| Baseline 05 | 328 | 38 | 11.59% | 159.16 | 188.36 |
| Direction control 01 | 310 | 35 | 11.29% | 175.20 | 186.74 |
| Direction control 02 | 301 | 35 | 11.63% | 180.75 | 209.22 |

Six reverse gestures through cached rows had 0 or 1 app deadline miss per
run, out of 512-517 recorded frames. Their p95 overrun was negative:
about -9 ms, meaning work completed before the recorded deadline.

Two additional **same-direction** controls then scrolled down through those
cached rows. This helps separate cache state from scroll direction:

| Cached downward pass | Recorded frames | App deadline misses | App miss rate | Deadline overrun p95, ms |
|---|---:|---:|---:|---:|
| Direction control 01 | 516 | 0 | 0.00% | -8.91 |
| Direction control 02 | 515 | 1 | 0.19% | -8.91 |

These are cache-state observations, not the effect of a code change. The
windows differ in length and include short gaps between gestures. Do not
calculate an optimization percentage from them.

### Jank needs a cause label

Perfetto's total jank count includes more than app deadline misses. First-pass
total jank ranged from 40.65% to 63.79%. Cached downward passes still had total
jank of 33.72% and 23.30%, mainly labelled `Buffer Stuffing`.

Therefore the cached result does **not** establish perfect presentation or
input latency. Report total jank, app deadline misses and queue-related jank
separately. A low app miss rate must not hide queued-frame latency.

The device reported 90 Hz during preparation. Use recorded expected-frame
deadlines, not a hard-coded 16.7 ms threshold. Average frames per second is
not an adequate primary measure.

## What the traces establish

On the five first passes, `compose:lazy:prefetch:compose` scopes totaled
5.25-5.70 seconds per pass. On the two cached downward passes they totaled
97-100 ms. Individual first-pass prefetch composition scopes reached
213-282 ms; cached downward maxima were about 5.3-5.4 ms.

The first passes contained **117-128 explicit Java garbage collections**,
totaling **2.83-3.11 seconds**. Almost all this collection time fell inside
main-thread sleep intervals. Cached downward passes had zero or one such
collection.

For example, in Baseline 03, a 217 ms prefetch composition scope contained
four explicit Java collections of about 27-28 ms each. The main thread slept
across each collection and was then awakened by `Thread-2`, the same thread
that emitted the collection slices.

These intervals overlap. Do not add collection time to composition time.
They show that the collection work is not harmless background activity.
They do not identify the managed allocation or peer lookup that requested
each collection.

The previously pinned runtime source provides a consistent mechanism:
the Android bridge requests Java collection through `Runtime.gc()`, and
Native AOT weak-reference lookup can wait for cross-runtime reference
processing to finish. This is a source-supported mechanism, not a captured
managed call stack. Do not bypass the bridge wait or suppress collection:
it protects object reachability across runtimes.

Source references:

- [Android bridge collection request](https://github.com/dotnet/android/blob/b65b55d5357abb23960a999c7c5ffaceb75c4515/src/native/clr/host/gc-bridge.cc#L77-L116).
- [Native AOT bridge wait](https://github.com/dotnet/dotnet/blob/ae1d06fe401b1fae8cb3dc6b6b1d8a7f4120cfa9/src/runtime/src/coreclr/nativeaot/Runtime/interoplibinterface_java.cpp).

## Paging is a separate problem

All five downward passes stopped with shot 951 and `LOAD MORE` visible.
The expected automatic threshold callback did not load the next page.
Source search found `RemainingItemsThresholdReached` declarations and a
configuration helper, but no invocation in the current node backend.

Two explicit `LOAD MORE` taps produced main-thread `Choreographer#doFrame`
intervals of **1,763 ms and 1,636 ms**. These are long-frame durations, not
input-to-presentation measurements. Their enclosing recomposition accounts
for nearly all of the frame interval.

After the first tap, native inspection confirmed the existing visible
position was retained and shot 950 appeared. A further scroll exposed shots
949-945. The data still contained 1,000 shots.

Relevant source:

- [ActivityFeedPage.cs](../sample/Shared/BaristaNotes/Pages/ActivityFeedPage.cs):
  page size 50, threshold 5, list construction, and `LoadMoreAsync`.
- [CollectionView.cs](../src/Comet/Controls/CollectionView.cs):
  threshold properties.
- [ComposeListNode.cs](../src/Comet/Platform/Compose/ComposeListNode.cs):
  native lazy list, row materialization cache, and full row release on a
  list-version change.
- [InMemoryShotService.cs](../sample/Shared/BaristaNotes/Services/InMemoryShotService.cs):
  synchronous sorting and mapping before returning a completed task.

The source supports a risk of full row-cache invalidation during paging.
The trace does not isolate service time from row reconstruction. Do not
attribute the entire page-load stall to sorting or database access.

## Recommended improvement order

1. **Reduce first-time row work in Comet.** Add narrow, matched diagnostic
   scopes and allocation counters around `GetRow`, `NativeListRow.Materialize`,
   `NativeListRow.Layout`, and text measurement. Determine which allocations
   precede bridge collection. Candidate sites include the six Java-backed
   state objects in every base `ComposeNode`, control-specific state,
   callbacks, and temporary text-measurement peers. Reduce measured work
   while preserving native Compose controls, reactivity, and disposal.
2. **Preserve unchanged rows on append.** Measure list invalidations during
   `LoadMoreAsync`, then separate append updates from full reloads. Keep
   explicit reload behavior for edited data, style changes and other
   invalidations. Do not simply remove cache disposal.
3. **Implement and verify the missing threshold notification.** Avoid
   repeated notifications while loading. Test it separately from row-cost
   changes, because automatic loading changes the measured journey.
4. **Optimize app-side sorting only if its measured contribution warrants
   it.** The current evidence gives row composition and bridge waits higher
   priority.

The time limit did not permit a safe implementation plus matched Release
validation. No runtime code was changed, no package was installed, and no
performance improvement is claimed.

## Method and limits

- Frames come from Perfetto FrameTimeline, filtered to the exact main process
  and one app layer. Every included actual frame matched exactly one expected
  frame by process and surface token.
- Overrun is `(actual.ts + actual.dur) - (expected.ts + expected.dur)`.
  Percentiles use linear interpolation over the recorded frame samples.
  These short runs do not support population-level p99 claims.
- Scroll windows use device `/proc/uptime` readings around input commands.
  The trace uses the same boot-time clock. The readings have 10 ms precision
  and include command-boundary uncertainty; they are not per-input latency
  timestamps. Trees, startup, fixture validation and navigation are outside
  the selected scroll windows.
- Baselines 03-05 originally used twelve reverse gestures. Their later
  gestures reached the top and could trigger pull-to-refresh. Only the first
  six are used for cached-scroll analysis. Raw traces retain the excluded
  tail, including a long refresh frame. The two later controls use only six
  reverse gestures and avoid this confound.
- Baseline 01 failed native-tree preparation. Baseline 02 failed to start
  tracing because Perfetto could not read a config from `/data/local/tmp`.
  Both failures are retained. Subsequent captures supply config through
  standard input. No failed measured scroll run was discarded.
- Trace processor reported no error/data-loss-severity counters, but each
  trace had 35 ftrace setup notices and 4-8 service-discarded chunks. The
  affected sources/intervals were not established. Frame counts are therefore
  **recorded-frame counts**, not proof that every produced frame was captured.
  Each analysis window was inside the trace bounds.
- This is a heavy diagnostic trace with scheduling events. Trace overhead
  was not measured. The installed artifact also contains earlier page-study
  instrumentation. Do not present these results as uninstrumented production
  benchmarks or as a framework comparison.
- The app is the pure Android Comet host. Release excludes the DevFlow agent.
  The installed DevFlow CLI failed Android-native inspection, and rebuilding
  the CLI was blocked by the unavailable root-pinned .NET 10.0.400 SDK.
  Native Android input and tree inspection were used for this diagnostic
  Release run. No Debug build was substituted.

## Artifact and device identity

| Item | Value |
|---|---|
| Package | `com.comet.sample.perf.art.baristanotes` |
| Installed APK SHA-256 | `9054675443a202c76298be19615bffe0dc8a4dd4ab137c45653362fd8d9c556c` |
| Build | Release, Android arm64, .NET 11 Native AOT; retained page-study artifact |
| Current source HEAD | `2381fb14af2ac34c1881e3da1e8a028abd250406` |
| Dataset | Validated `barista-perf-v2-1000`; synthetic test records |
| Device | Pixel 5, serial `13041FDD4007MT`, Android 14 / API 34 |
| ART state read before capture | `speed-profile`, reason `bg-dexopt` |
| Battery | 100%, USB powered |
| Battery temperature | 26.9 C before; 27.2 C after |
| Thermal status | 0 at both checks |
| Animation scales | All 0, unchanged |

The installed APK hash matched the retained page-study record. Measured
snapshot copies of `ComposeListNode`, `ComposeNode` and `ComposeLeafNodes`
matched this worktree. `ActivityFeedPage` differed only by the earlier
measurement hooks and partial-class declaration.

Read-only fixture validation passed before each capture and after collection.
No user data, fixture selection, system setting or compilation profile was
reset.

Raw traces, native trees, command records, scripts, summaries and condition
snapshots are retained under session
`c34609dd-7e0c-4040-8097-ec029694a210/files/`. The measured runs are
`baseline-03`, `baseline-04`, `baseline-05`, `direction-control-01`, and
`direction-control-02`. `analyze-scroll.py` regenerates the summaries.

Verified: five on-device Release trace runs; matched-frame joins; native
content and paging checks; fixture validation; collection/UI-wait overlap;
source comparison; final device conditions. No optimized build was tested.
