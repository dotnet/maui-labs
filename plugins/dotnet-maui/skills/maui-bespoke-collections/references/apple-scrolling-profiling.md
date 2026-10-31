# Apple collection scrolling: capture, analyze, interpret

For UIKit iOS/Mac Catalyst collections and native AppKit collections. Read the
[benchmark contract](benchmark-harness.md) first. This is a profiling recipe,
not a certified control, C# signpost library or new physical-device benchmark.
[Sources AP1-AP6](sources.md#ap1); commands checked with xctrace 27.1.

## 1. Select the target and a valid workload

| Target | Collection/input/display to verify |
|---|---|
| Physical iOS/iPadOS | UICollectionView, intended device/app, touch drag and deceleration, actual variable display cadence |
| Mac Catalyst | UIKit collection in the intended Mac window; trackpad/wheel, window size, backing scale and selected display |
| Native macOS | Actual NSCollectionView/NSScrollView or declared custom native virtualizer; AppKit item/controller ownership, live resize and display |
| iOS simulator | Behavior/CPU diagnostics only; not physical GPU/input qualification |

Catalyst is not AppKit. A template appearing in the installed list does not prove
that its instrument works on a particular target or that it captured useful data.
If Hitches reports unsupported, preserve the failure and mark hitch/frame evidence
unavailable. Time Profiler success cannot replace it. The existing mobile proof
encountered exactly this simulator limitation. [R4](sources.md#r4)

Use a Release, appropriately signed/deployable build with symbols. Preserve
production runtime/AOT/trimming choices; record any diagnostic-build difference.
Record OS, SDK/Xcode, hardware, viewport, display/cadence, thermal/power state,
fixture revision, content/assets, cache state and instrumentation.

Before **each** measured traversal, verify the intended native host/window, finite
viewport, filtered count, visible loaded keys/content and settled layout. Isolate
background writers. Warm a declared route, then repeat equivalent forward/reverse
drag/fling and deceleration. Keep distance, velocity policy, row kinds, images,
width/font and starting keys equivalent across variants. Reset between iterations
outside the measured interval; don't repeatedly swipe an already-ended list.

Record cold image/loading, warm reuse, prepend/refresh and resize separately.
Use real gestures for an input/deceleration claim. Programmatic offset steps are
a legitimate **different** layout/render workload; a 16 ms delay between steps is
not a measured frame interval. Confirm native frames/keys after asynchronous
layout rather than trusting the offset setter.

## 2. Make a narrow native capture

Start with installed-tool discovery, not hardcoded device/template assumptions:

```bash
xcrun xctrace version
xcrun xctrace list devices
xcrun xctrace list templates
xcrun xctrace help record
xcrun xctrace help export
```

On physical iOS, verify a target PID/executable through the Instruments process
picker or, when supported, `xcrun devicectl device info processes --device UDID`.
For Mac targets, verify the executable and window of the selected host PID.
`--attach` accepts a PID/process name; do not assume a bundle ID is a process name.
Use an explicit device from xctrace even for the Mac when several targets exist.
Resolve pairing/Developer Mode/signing/profiling permission through the normal
toolchain; don't disable signing or broaden entitlements as a shortcut.

In Instruments, select the actual target, then an available Animation Hitches
template for frame/commit/render evidence. Record while performing the verified
route; stop after the final deceleration/layout settles. Inspect the relevant
app/window and exact active scrolling interval, not startup or idle time.
Make a separate Time Profiler pass for CPU attribution and a separate Allocations/
available VM-tracking pass for memory. Keep profiler sets comparable: adding every
instrument or frequent screenshots/tree dumps changes the workload.

For a bounded command-line capture, set inspected values and a fresh artifact
directory outside shipped files. Start the warmed app before attachment:

```bash
: "${APPLE_DEVICE:?Set an explicit xctrace device identifier}"
: "${APP_TARGET:?Set the verified target PID or unique executable name}"
: "${TRACE_DIR:?Set a fresh absolute artifact directory}"
mkdir -p "$TRACE_DIR"

# Only when this template actually supports the selected target.
xcrun xctrace record --device "$APPLE_DEVICE" \
  --template 'Animation Hitches' --attach "$APP_TARGET" \
  --time-limit 30s --output "$TRACE_DIR/scroll-hitches.trace"

# Separate equivalent traversal; requires the installed instrument name.
xcrun xctrace record --device "$APPLE_DEVICE" \
  --template 'Time Profiler' --instrument 'Points of Interest' \
  --attach "$APP_TARGET" --time-limit 30s \
  --output "$TRACE_DIR/scroll-cpu.trace"

# Separate memory pass over equivalent traversal intervals.
xcrun xctrace record --device "$APPLE_DEVICE" \
  --template 'Allocations' --attach "$APP_TARGET" --time-limit 30s \
  --output "$TRACE_DIR/scroll-allocations.trace"
```

Drive the workload after recording starts, using the UI or existing automation;
do not wait until the bounded recorder has exited. Name/mark each iteration and
verify its end. Use fresh paths/run IDs; `--append-run` mixes runs and reuses the
original template. Do not default to `--all-processes`. If render analysis needs
broader system capture, explicitly agree its scope and filter app/window evidence.
Keep raw traces local: they can contain private paths, symbols and content.

## 3. Mark collection work without inventing a C# API

Useful intervals: `CollectionScroll`, `Bind`, `Measure`, `GeometryRebuild`,
`ApplySnapshot`, `PrepareImage`, `PublishPage`. Capture native/MAUI creation counts,
candidate visits and asset/request counters alongside them. Keep callback work
cheap; aggregate counters rather than logging every setter, pixel or frame.

Apple signposts provide native timeline correlation. `ILogger`, EventSource,
CoreFoundation.OSLog.Log and a Stopwatch do **not** automatically create native
signpost intervals. Verify the consuming .NET binding or use an explicitly scoped,
version-checked native bridge. Legacy `os_signpost_interval_begin/end` are macros,
not functions to invent in a DllImport; don't call private encoding helpers.

This native Swift fragment illustrates the signpost contract, not an app-ready
C# wrapper. OSSignposter requires iOS/Catalyst 15 or macOS 12; gate older targets.

```swift
import os

let signposter = OSSignposter(
    subsystem: "com.example.collection", category: .pointsOfInterest)
let state = signposter.beginInterval(
    "CollectionScroll", id: signposter.makeSignpostID())
// Perform the actual workload; retain state across asynchronous completion.
signposter.endInterval("CollectionScroll", state)
```

Pair the same static name/state exactly once, including cancellation/teardown;
mark an interrupted iteration invalid. Don't end when a method merely enqueues a
scroll or image decode. Concurrent intervals need distinct IDs, not `.exclusive`.
Keep row contents/URLs out of public signpost metadata.

**Category trap:** the checked Points of Interest instrument filters
`PointsOfInterest`. A custom `"Scrolling"` category produced a valid exported
table with zero workload rows in the native smoke check. Use `.pointsOfInterest`
for that instrument, or configure an appropriate os_signposts/custom instrument
for the intended subsystem/category. Verify actual matching begin/end rows.

A plain work interval selects/correlates time; it does not automatically enroll
every custom animation in a system animation metric. Where supported, Apple's
`beginAnimationInterval` supplies the animation-signpost contract. Keep CPU/bind/
network intervals distinct from real animation duration and verify metric coverage.

## 4. Analyze the selected interval, not the tool's existence

Open the trace in Instruments. Select the workload interval, then correlate its
hitch/frame tracks with main-thread CPU and render work. Preserve the original
trace, exact selection and metric definitions.

| Observation | Collection-specific investigation |
|---|---|
| Commit hitch / main-thread stall | Cell/tree creation, bind/getters/converters, wrapping/self-sizing, full/suffix geometry rebuilds, broad snapshot updates, synchronous SQL/decode or dispatch backlog |
| Render hitch despite cheap bind | Layer count, large textures/uploads, masks/shadows/blending, offscreen rendering and expensive transforms; render server/GPU work is on the app's behalf |
| Stalls at page boundaries | Separate query/download latency from accepted UI publication, diff/layout/anchor work and image preparation |
| Repeated creation on warm reverse scroll | Registration/template compatibility, native reuse versus MAUI tree reuse, overscan/pool growth and row kinds |
| Memory growth each equivalent pass | Native cells/controllers, MAUI trees/handlers, decoded pixels, pending work, delegates/subscriptions and retained previews independently |

Time Profiler is sampling, not exact duration/count for every bind. Restrict to the
main thread for commit work; inspect background decode/loading separately. A quiet
main thread does not exclude render hitches. Use signpost/counter observations for
short or infrequent callbacks that sampling misses. Fix the observed owner, then
repeat the same frame workload; don't turn off required content/accessibility.

### Frame lifetime is not presentation cadence

Apple's expected/actual **frame lifetime** runs from frame begin to presentation.
It can span two or three refresh intervals because the pipeline is buffered.
For example, a 25 ms expected/actual lifetime in triple buffering at 120 Hz can
coexist with a new frame every 8.33 ms. It is not automatically three missed frames.
Hitch duration is lateness against that frame's expected timing, not the entire
lifetime. Use recorded begin/commit/presentation deadlines, not a guessed fixed
VSYNC calculation. [AP1](sources.md#ap1)

For true presentation-interval percentiles, require actual applicable presentation
records scoped to the app/window/active scroll, their timestamps and sample count.
Don't compute them from CPU samples, CADisplayLink callbacks, commit durations,
signpost durations or a Frame Lifetime column. CADisplayLink can describe callback
cadence, not prove each frame was presented. Variable-refresh/power changes need
their actual cadence/deadlines; neither a maximum display rate nor a forced 60 Hz
budget describes every run.

### Hitch-rate units and denominator

Report the tool/API's hitch count, total/max hitch duration, tracked animation/
scroll duration and hitch time ratio when available. For raw compatible totals,
`120 ms hitch / 20 s tracked animation = 6 ms/s`, not `120 / 30 s` because the
recorder ran for 30 seconds. Never divide by a period padded with startup/idle.
Do not double-count commit/render events for the same frame or combine unlike
provider totals. Missing animation coverage/zero denominator is not a smooth pass.

Record the provider/version/unit and project budget. Apple's older 5/10 ms/s
guidance and the current Organizer 10/25/50 ms/s categories refer to their cited
metrics; don't turn historical labels into universal assertions or assume newer
perceptually adjusted metrics equal a raw sum. [AP1, AP6](sources.md#ap1)

### Export actual tables

```bash
xcrun xctrace export --input "$TRACE_DIR/scroll-cpu.trace" \
  --toc --output "$TRACE_DIR/scroll-cpu-toc.xml"

# Example only if this exact run/table occurs in the exported TOC.
TABLE_XPATH='/trace-toc/run[@number="1"]/data/table[@schema="OSSignpostIntervals"]'
xcrun xctrace export --input "$TRACE_DIR/scroll-cpu.trace" \
  --xpath "$TABLE_XPATH" --output "$TRACE_DIR/scroll-workload.xml"
```

Discover frame/hitch/CPU tables independently from **that trace's** TOC; schema
names, columns and run numbers vary. A CPU/signpost export does not contain frame
evidence merely because another template does. Resolve XML `id/ref` references and
schema/engineering types; formatted `fmt` strings are rounded display values.
Require the intended process, nonempty relevant rows and exactly the expected
completed workload intervals. Check recording/export warnings, truncation and
analysis filters. Successful exit status with empty/malformed/unrelated tables is
not a successful measurement. Unknown sample count/units block percentiles.

## 5. Correlate native CPU/memory with managed work

Keep the build-matched executable, native dSYM, managed assemblies/PDBs and manifest.
Check Mach-O/dSYM UUIDs with `xcrun dwarfdump --uuid`; rebuilding a similar source
revision doesn't produce equivalent symbols. Inspect unresolved frames before
attributing native/AOT addresses to C# code. Symbolicate a copy when necessary:

```bash
: "${DSYM_PATH:?Set the matching native dSYM path}"
xcrun xctrace symbolicate --input "$TRACE_DIR/scroll-cpu.trace" \
  --output "$TRACE_DIR/scroll-cpu-symbolicated.trace" --dsym "$DSYM_PATH"
```

Instruments Allocations/VM data and native retain paths do not, by themselves,
measure managed allocations or prove GC pauses. Pair them with supported .NET
events/counters and owned-resource counts. Record allocation rate, GC count/pause
coverage, native/process footprint and decoded bytes separately. Total allocated
bytes are not retained bytes; managed heap size isn't process/GPU memory.

On the inspected .NET Apple SDK, Release **Mono** tracing needs
`EnableDiagnostics=true`; diagnostic properties can enable it implicitly and affect
EventSource/trimming/app size. CoreCLR has a different default; NativeAOT cannot
be made EventPipe-capable with that Mono switch. Verify runtime/provider support.
Do not copy runtime-repository `RuntimeComponents`/internal builder instructions
into an ordinary MAUI project or switch production runtimes to make a profile work.
[AP4](sources.md#ap4)

For an authorized diagnostic Mono build, inspect compatible tooling first:

```bash
dotnet-dsrouter --version
dotnet-dsrouter ios --help
dotnet-trace collect --help

# Dedicated terminal; simulator uses its own ios-sim profile instead.
dotnet-dsrouter ios -i --runtime-timeout 60
```

Use the router's **actual printed** DiagnosticPorts/address/port/listen-connect
values, not settings copied from another profile/tool version. Some profiles
route physical iOS over USB/usbmux. Ensure the intended device/app; stop if routing
cannot disambiguate attached targets. Keep endpoints loopback, not unauthenticated
LAN listeners. Apply those values through the consuming SDK's properties in a
scoped diagnostic build, retaining the intended configuration/RID:

```bash
: "${APP_PROJECT:?Set the inspected app project}"
: "${APPLE_TFM:?Set its actual Apple target framework}"
: "${APPLE_RID:?Set the intended device or Mac runtime identifier}"
: "${DIAGNOSTIC_ADDRESS:?Use the router's printed loopback address}"
: "${DIAGNOSTIC_PORT:?Use the router's printed port}"
: "${DIAGNOSTIC_MODE:?Use the router's printed listen or connect mode}"

dotnet build "$APP_PROJECT" -f "$APPLE_TFM" -r "$APPLE_RID" -c Release \
  -p:EnableDiagnostics=true \
  -p:DiagnosticAddress="$DIAGNOSTIC_ADDRESS" \
  -p:DiagnosticPort="$DIAGNOSTIC_PORT" \
  -p:DiagnosticListenMode="$DIAGNOSTIC_MODE" \
  -p:DiagnosticSuspend=false
```

Keep normal signing/deployment and project-local SDK selection. Use
`DiagnosticSuspend=false` for scrolling; if passing DiagnosticConfiguration
instead, it overrides the other diagnostic properties and its commas must be
encoded as `%2c` on the MSBuild command line. NativeAOT/unsupported providers stay
an explicit gap, not zero GC or allocation.

After deploying that diagnostic build and verifying its useful viewport, connect
to the printed **router PID**, not a guessed host copy of the iPhone app PID:

```bash
: "${ROUTER_PID:?Set the intended router PID printed by its info output}"
dotnet-trace collect --process-id "$ROUTER_PID" --profile gc-verbose \
  --duration 00:00:30 --output "$TRACE_DIR/scroll-managed.nettrace"
```

For a supported local Mac CoreCLR/Mono process, use its verified app PID/diagnostic
endpoint instead. Collect available counters or the required EventSource/provider
in a separate equivalent pass where appropriate. The inspected `gc-verbose`
profile samples object allocations; it does not count every per-bind allocation.
Missing events don't establish zero allocations/pauses. `maui profile startup`
is startup/exit-control tooling, not a scrolling recorder; don't invent a
`maui profile scroll` command.

Carry the same run/iteration ID through native markers and managed events/counters.
Establish clock alignment if combining traces: recording-relative nanoseconds,
Stopwatch ticks and wall-clock UTC are not interchangeable. If exact GC/CPU/hitch
overlap cannot be established, report correlation limits, not causality.

For memory, mark generations before/after repeated routes in a live Instruments
Allocations session, or inspect matching time ranges/persistent allocations in a
command-line capture. Record MAUI tree/handler/controller/subscription/image peaks
and post-disconnect ownership. Test replaced queries and closed menus/previews.
A single footprint sample or forced GC after every swipe doesn't prove the normal
workload plateaus.

## 6. Optional native regression tests and field telemetry

If a compatible XCTest UI target already exists, inspect
`XCTOSSignpostMetric.scrollingAndDecelerationMetric` (iOS/Catalyst 15, macOS 11)
or newer `XCTHitchMetric` (26+) against the actual SDK/target. Drive a real scroll,
wait through deceleration, and reset the starting viewport/data outside measurement.
Verify actual metric coverage/results on the custom host. For the signpost metric,
no matching begin/end pair means no measurement, not a perfect result.
Native XCTest isn't automatically an API in a C# xUnit/DevFlow test.
[AP5](sources.md#ap5)

Organizer/MetricKit help detect shipping regressions across supported cohorts and
reporting windows; they aren't automatically a trace of this one 30-second list.
Legacy MXAnimationMetric and newer Swift HitchTimeMetric APIs have different
availability/binding/report contracts. Verify SDK/runtime and units before choosing
one; don't paste a Swift-only/newer type into an older .NET Apple pack. Match app/
OS versions, sample coverage and tracked animation, not just an aggregate ratio.

## 7. Re-measure and leave an honest result

Compare matched Release variants on the same target; repeat/alternate order when
differences are small. Report spread and sample counts, hitch/presentation budgets,
creation/geometry/memory/loading counters and actual viewport correctness. A capture
is not analysis; a passing native-tool probe is not a passing collection benchmark.

Stop only owned recordings/routers and remove only scoped diagnostic settings,
hooks/data/ports. Preserve useful traces/symbols privately and rebuild/verify the
production configuration without diagnostic endpoints/components. Keep legitimate
fixes. Record unsupported metrics and unrun physical/backend targets explicitly.
