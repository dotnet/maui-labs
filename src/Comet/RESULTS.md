# Comet-Next — Performance Baselines (Phase 0)

These are the **pre-refactor** baselines captured on the existing MAUI-handler
render path, on branch `davidortinau/comet-next` at the start of the Comet-Next
refactoring (typed property storage + own backend abstraction + Jetpack Compose /
SwiftUI backends). Every later phase A/B's against these numbers.

See the plan at `~/.claude/plans/focus-on-comet-in-clever-ladybug.md` and the
harness scripts in `tools/bench/`.

## Environment

| | |
|---|---|
| Date captured | 2026-06-12 |
| Host | Apple M4 Max, 16 cores, macOS 26.5 (Darwin 25.5.0) |
| .NET SDK | 11.0.100-preview.5.26302.115 (runtime 11.0.0, Arm64 RyuJIT) |
| MAUI | .NET 11 preview 5 (`Microsoft.Maui.Controls $(MauiVersion)`) |
| BenchmarkDotNet | 0.14.0 |
| Android device | Pixel 5, Android 14 (physical, `13041FDD4007MT`) |
| Android minSdk | 24 (bumped from 21 for .NET 11 preview 5) |

## 1. Android app size — `CometStressTest`, Release default publish

`dotnet publish -f net11.0-android -c Release` (no RID filter → fat APK, both
ABIs; managed assemblies packed into `libassembly-store.so`). This is the
"default Release" shape an app author gets today; the trimming/AOT gate in
Phase 4 will compare against a single-RID, fully-trimmed build.

| Metric | Value |
|---|---|
| APK raw size | **30.59 MiB** (32,077,084 bytes) |
| APK download size (estimated) | 30.76 MB |

Internal breakdown (largest entries, uncompressed):

| Entry | Size |
|---|---|
| `lib/x86_64/libassembly-store.so` (managed code) | 8.66 MiB |
| `lib/arm64-v8a/libassembly-store.so` (managed code) | 8.47 MiB |
| `classes.dex` + `classes2.dex` | 15.58 MiB total |
| `lib/*/libcoreclr.so` | 5.9 / 5.8 MiB |
| `lib/*/libclrjit.so` | 3.4 / 3.2 MiB |

> Note: dual-ABI (arm64-v8a + x86_64). A single-RID arm64 build roughly halves
> the native `.so` + assembly-store contribution. The trimming win we are
> targeting is on the managed `libassembly-store.so` (unused control handlers).

## 2. Android cold start — `CometStressTest`, Release on Pixel 5

`adb shell am start -W` TotalTime, app force-stopped between runs
(`tools/bench/startup.sh`, 10 runs, median reported).

| Metric | Value |
|---|---|
| Cold start (median of 10) | **644 ms** |
| Cold start (range) | 629–711 ms |

Raw runs (ms): 696, 629, 685, 635, 631, 655, 647, 639, 642, 711.

> Methodology aligned with `jonathanpeppers/maui-profiling` (Pixel 5, adb,
> launcher-activity resolution, `am start -W`). For Phase 4 we will additionally
> capture the JIT / interpreter / profiled-AOT time split that repo tracks.

## 3. Reactive / MVU microbenchmarks (BenchmarkDotNet)

In-process, host-side (not on-device). These exercise the parts that **stay**
through the refactor (Signal/Computed/scheduler, diff, body rebuilds) — they let
us prove the typed-storage change doesn't regress the reactive core, and give the
MVU-vs-XAML reference the framework has historically reported. Mean time and
allocation per op.

### Reactive primitives

| Benchmark | Mean | Allocated |
|---|---:|---:|
| Create + dispose 1000 Signals | 19,878.9 ns | 192,000 B |
| SignalList: 100 Adds + ConsumePendingChanges | 2,647.7 ns | 7,136 B |
| 1 Signal write → 1 effect update | 3,166.0 ns | – |
| 100 Signal writes → single flush | 631,694.3 ns | 747,136 B |
| 50 Computeds, 1 Signal change → all re-evaluate | 60,180.8 ns | 50,680 B |
| 1000-item SignalList + 1 Add → targeted insert | 4,007.2 ns | 17,192 B |
| Computed cache hit (100 reads, no dep change) | 638.7 ns | – |

### State updates (MVU vs XAML)

| Benchmark | Mean | Allocated |
|---|---:|---:|
| XAML: Single property update | 1,360.8 ns | – |
| **MVU: Single state update** | **174.6 ns** | – |
| XAML: N independent property changes | 1,713.1 ns | – |
| MVU: N independent state changes | 1,221.9 ns | – |
| XAML: No-op property change (same value) | 1,467.7 ns | – |
| MVU: No-op state change (same value) | 808.7 ns | – |
| XAML: Change 1 of 100 properties | 1,764.7 ns | – |
| MVU: Change 1 of 100 states | 1,042.2 ns | – |

### View construction

| Benchmark | Mean | Allocated |
|---|---:|---:|
| XAML: Flat StackLayout + Labels | 59,874.8 ns | 79,904 B |
| MVU: Flat VStack + Text | 796.4 ns | 1,128 B |
| XAML: Deep nested layouts | 311,710.9 ns | 269,775 B |
| MVU: Deep nested views | 784.5 ns | 1,128 B |
| XAML: Mixed form controls | 29,282.2 ns | 46,045 B |
| MVU: Mixed form controls | 788.9 ns | 1,128 B |

### Real-world scenarios

| Benchmark | Mean | Allocated |
|---|---:|---:|
| XAML: Todo list build + mutations | 94,164.0 ns | 139,620 B |
| MVU: Todo list build + mutations | 1,134.1 ns | 4,041 B |
| XAML: Form build + validation | 57,653.5 ns | 93,218 B |
| MVU: Form build + validation | 1,437.1 ns | 5,865 B |
| XAML: Dashboard build | 175,332.3 ns | 244,125 B |
| MVU: Dashboard build | 785.8 ns | 1,128 B |

### Memory

| Benchmark | Mean | Allocated |
|---|---:|---:|
| XAML: Alloc per property change | 2,152.2 ns | 3,664 B |
| MVU: Alloc per state change (body rebuild) | 1,217.3 ns | 2,201 B |
| MVU: Alloc for 100-node tree rebuild | 1,108.5 ns | 1,560 B |
| XAML: Startup alloc (50-control page) | 1,243,158.0 ns | 2,181,102 B |
| MVU: Startup alloc (50-control view) | 7,881.0 ns | 11,282 B |
| XAML: Cascading computed properties | 6,868.5 ns | 12,352 B |
| MVU: Cascading derived state | 1,626.9 ns | 3,201 B |

### Diff algorithm

| Benchmark | Mean | Allocated |
|---|---:|---:|
| Diff: Identical trees (no change) | 78,549.2 ns | 39,898 B |
| Diff: Single node changed in N | 79,797.4 ns | 40,079 B |
| Diff: All nodes changed | 84,354.7 ns | 40,217 B |
| Diff: Append node to list | 916.7 ns | 1,344 B |
| Diff: Remove node from middle | 1,000.8 ns | 1,368 B |
| Diff: Toggle subtree (show/hide) | 983.0 ns | 1,392 B |

### Rapid updates / animation

| Benchmark | Mean | Allocated |
|---|---:|---:|
| XAML: Rapid counter | 30,796.2 ns | – |
| MVU: Rapid counter | 9,208.1 ns | – |
| XAML: Multi-prop animation (4 props) | 49,631.9 ns | 12,944 B |
| MVU: Multi-prop animation (unbatched) | 30,781.0 ns | 9,928 B |
| MVU: Multi-prop animation (batched) | 37,116.2 ns | 9,928 B |
| XAML: String-heavy updates | 37,231.1 ns | 30,272 B |
| MVU: String-heavy updates | 21,003.8 ns | 32,400 B |

### Hot reload (must be preserved through the refactor)

| Benchmark | Mean | Allocated |
|---|---:|---:|
| Hot reload: 10-Signal view state transfer | 32,236.0 ns | 10,928 B |
| Hot reload: 50-Signal view state transfer | 124,847.5 ns | 57,272 B |

## How to reproduce

```bash
# Microbenchmarks (host)
dotnet run -c Release --project tests/Comet.Benchmarks -- --filter '*' --join

# Android app size
tools/bench/size.sh sample/CometStressTest/CometStressTest.csproj

# Android cold start (set ANDROID_SERIAL if multiple devices attached)
ANDROID_SERIAL=<serial> tools/bench/startup.sh \
  sample/CometStressTest/bin/Release/net11.0-android/publish/com.comet.stresstest-Signed.apk \
  com.comet.stresstest 10
```

---

## Phase 4 — Trimming proof (managed code, new backend)

The core thesis of the refactor: the legacy MAUI-handler path roots **every**
control, so none can be trimmed, whereas the new node backend reaches a control's
renderer only through the virtual `View.CreateBackendNode`, so unused controls
trim away with their backend nodes.

**Why the legacy path can't trim** — `AppHostBuilderExtensions.UseCometHandlers`
(required by every legacy Comet app) registers a `Dictionary<Type,Type>` literal
with `typeof(Picker)`, `typeof(CollectionView)`, `typeof(GraphicsView)`, … for all
~60 controls. A `typeof`/`ldtoken` in reachable code roots the type; the ILLinker
cannot remove it. So a legacy "hello world" still ships every control + handler.

**New backend, measured** — `CometComposeProbe` (pure Android, no `UseMaui`, no
`UseCometHandlers`) uses only Text, Button, VStack, ListView, NavigationView.
Published Release with `-p:TrimMode=full` (full ILLink; `RunAOTCompilation=false`
to isolate managed trimming), then the linked `Comet.dll` was inspected with
`ikdasm`/`monodis`:

| Metric | Unlinked | Linked (TrimMode=full) | Δ |
|---|---:|---:|---:|
| `Comet.dll` size | 805,888 B (787 KiB) | 108,544 B (106 KiB) | **−87%** |
| Type definitions (classes) | 773 | 113 | **−85%** |
| `libassembly-store.so` (arm64, all managed) | 8.47 MiB¹ | 6.10 MiB | −2.37 MiB |

¹ Phase 0 baseline (CometStressTest, legacy path).

**Unused controls — typedefs present (unlinked → linked):**

| Control | → | Control | → | Control | → |
|---|---|---|---|---|---|
| CollectionView | 3 → **0** | CarouselView | 2 → **0** | Picker | 2 → **0** |
| GraphicsView | 1 → **0** | WebView | 1 → **0** | RefreshView | 2 → **0** |
| FlyoutPage | 1 → **0** | Stepper | 3 → **0** | SwipeView | 1 → **0** |
| TabView | 2 → **0** | SearchBar | 2 → **0** | DatePicker | 3 → **0** |
| ProgressBar | 3 → **0** | ActivityIndicator | 2 → **0** | ScrollView/Frame/Border/Grid | → **0** |

**Used controls survive** (linked): Text, Button, VStack, ListView (+`ListView<T>`),
NavigationView — all present.

> Conclusion: the new backend makes Comet's managed surface pay-for-what-you-use —
> a 5-control app ships 106 KiB of Comet instead of 787 KiB, and every unused
> control (and its `CreateBackendNode`/`Compose*Node`) is gone. This is the
> "handlers aren't trimmable → app-size bloat" problem, retired.
>
> Caveats: total APK is still dominated by the Compose AndroidX deps (dex) +
> CoreCLR runtime, not Comet's managed code, so the win shows in `Comet.dll` /
> assembly-store, not the package total. Cold-start A/B and a minimal **legacy**
> trimmed build (to show controls *survive* empirically) are still pending — the
> latter needs a few-control legacy app since CometStressTest instantiates most
> controls.

### Reproduce

```bash
dotnet publish sample/CometComposeProbe/CometComposeProbe.csproj \
  -f net11.0-android -c Release -p:TrimMode=full -p:RunAOTCompilation=false
LINKED=sample/CometComposeProbe/obj/Release/net11.0-android/android-arm64/linked/Comet.dll
ikdasm "$LINKED" | grep -c '\.class '                 # ~113
ikdasm "$LINKED" | grep -E '\.class .*\bPicker\b'     # (empty — trimmed)
```

### iOS / SwiftUI backend (same proof)

`CometSwiftUIProbe` (Text/Button/TextField/Toggle/Nav) built
`-c Release -r iossimulator-arm64 -p:TrimMode=full`, linked `Comet.dll` inspected:

| Metric | Value |
|---|---:|
| `Comet.dll` (linked) | 243,200 B / 183 classes |
| Compose backend (`ComposeNode`, `Compose*Node`) | **trimmed to 0** (Android-only code gone from the iOS build) |
| SwiftUI nodes in use (`SwiftUINode`, `SwiftUINavigationNode`) | survive |
| `SwiftUIListNode` (probe drops ListView) | **0** — trimmed |
| Picker, GraphicsView, WebView, Stepper, TabView, DatePicker, ScrollView, Frame, Grid | **0** |

So each platform's build trims the *other* backend plus unused controls. **Caveat
(Phase 5 item):** on iOS a few legacy handlers still survive —
`Comet.Handlers.CollectionViewHandler` (+ `CollectionView`/`CarouselView`) are
rooted by the iOS static registrar's `[Register]` scan, even though the probe uses
the new backend. On the pure new-backend Android probe these trim to 0. Fully
decoupling the legacy iOS handlers from the new render path is Phase 5 work.

## Phase 4 — Cold start A/B (Pixel 5, same session)

`tools/bench/startup.sh` (`am start -W` TotalTime, force-stop + 2 s settle
between runs, 10 runs, median). Both Release single-RID `android-arm64`, on the
physical Pixel 5 (`13041FDD4007MT`), measured back-to-back on 2026-06-13.

| App | Render path | Cold start (median) | Range |
|---|---|---:|---:|
| `CometStressTest` | **legacy** MAUI handlers (`UseCometHandlers`) | **721 ms** | 695–820 |
| `CometComposeProbe` | **new** node backend, no `UseMaui` | **534 ms** | 515–570 |

**~26% faster (−187 ms) and a much tighter distribution.** The new path is a pure
`ComponentActivity` that never runs the MAUI app-host (`MauiApp.CreateBuilder` →
handler registration → Shell), so it skips that init entirely; the legacy app pays
for the full MAUI startup before the first frame.

> Caveat: not a perfectly controlled A/B — the two are different apps
> (CometStressTest exercises many pages; the probe is minimal), so part of the gap
> is app complexity, not solely the backend. But the dominant cold-start cost is
> runtime + framework init, and the new path provably avoids the MAUI host. (The
> 2026-06-12 baseline measured CometStressTest at 644 ms; it read 721 ms today —
> ~12% device/thermal drift — hence the same-session re-measure for a fair delta.)

## Phase 5 — Legacy render path decoupled (iOS handler rooting fixed)

The Phase 4 iOS caveat (legacy `CollectionViewHandler` + CollectionView/CarouselView
survived trimming, rooted by the static registrar's scan of every NSObject type in
the assembly) is resolved. The legacy MAUI ViewHandler render path is now gated
behind `CometLegacyRenderPath` (MSBuild property, default `true`). Building with
`-p:CometLegacyRenderPath=false` excludes the cluster (`Handlers/**`, `Maui/**`,
`AppHostBuilderExtensions`, and the iOS `CometView*`/`CUI*` native views) so the
registrar never sees them and they trim.

`CometSwiftUIProbe` Release sim, `TrimMode=full`, linked `Comet.dll`:

| | legacy ON | legacy OFF |
|---|---:|---:|
| `Comet.dll` | 243,200 B / 183 classes | **202,752 B / 136 classes** |
| `CollectionViewHandler`, `CometViewHandler`, `CometView`, `CUITableView`, `ScrollViewHandler`, `NavigationViewHandler` | present | **all 0** |
| SwiftUI backend (`SwiftUINode`, …) + used controls | present | present |

Non-breaking: the default (`true`) build is unchanged — maccatalyst compiles and
the host suite stays 875 pass / 1 known-fail. Windows/MacCatalyst keep the legacy
path; iOS/Android node-backend apps opt out for the smaller, fully-trimmed binary.

## Hot reload on the node backend (gate: "hot reload preserved") — 2026-07-01

The plan's Phase 1/2 exit criterion, verified for the first time on the node
backend — and it required real fixes: the reload path still assumed the legacy
handler contract (handlers lazily re-render, so a rebuilt tree "eventually"
appears). The node backend pushes patches at diff time, so the reload path had
to transfer the retained node tree explicitly:

- `UpdateFromOldView` now transfers `Node` (adopt + event-sink rebind to the new
  view + re-emit set properties) — the node twin of the ViewHandler transfer.
- Views with a live `Node` register as hot-reload targets (`AddActiveView`), and
  a Component/[Body] view that collapses to its rendered subtree registers at
  materialize time (`TriggerReload` only reloads `Parent == null` roots).
- A fresh hot-reload replacement has never rendered; the diff now force-builds it
  when retained nodes are in play, and `GetRenderView`'s replacement branch diffs
  the outgoing built tree into the replacement so the nodes carry over.
- `TryMergeComponents` reuses an already-installed replacement instead of
  constructing a second instance (which stranded the transferred node).
- `ComposeBackendRoot` re-resolves its layout root per pass (read-only
  `BuiltView`; a captured tree goes stale after reload — and building views on a
  background flush thread crashes).
- New protocol hook `ICometBackendNode.OnOwnerViewChanged(View)`: own-content
  nodes (Drawer, NavigationView, ListView) re-point at the reloaded view and
  re-materialize the content they built from the old tree.

Verified: 4 host tests (`Backend/BackendHotReloadTests.cs` — prop patch onto the
SAME retained node, event rebind, root-type replacement via TriggerReload,
component replacement preserving `Component<TState>` state) + on-device demo
(emulator, `CometComposeProbe`): `adb shell am broadcast -n
com.comet.composeprobe/.HotReloadDemoReceiver` swaps `JetchatRoot→JetchatRootV2`
(the exact two calls `CometMetadataUpdateHandler` makes when the runtime applies
an EnC delta); the running Jetchat re-renders with the new inset, the typed
composer text survives, and the emoji selector still opens (events + reactive
re-layout intact through the re-materialized drawer content).

KNOWN LIMITATIONS: (1) structural child insert/remove in a reloaded tree does
not re-materialize nodes yet (a brand-new view with no same-type pair renders
nothing — legacy recreated handlers lazily, the node path needs explicit
Insert/RemoveChild materialization; follow-up); (2) the SwiftUI nodes inherit
the no-op `OnOwnerViewChanged` — iOS needs the same own-content pass.

## Backend patch-stream benchmarks (host, BenchmarkDotNet) — 2026-07-01

`tests/Comet.Benchmarks/BackendMutationBenchmarks.cs` (the plan's verification
item): pure Comet-side overhead of the diff→backend contract against a no-op
recording node (no Compose/SwiftUI interop in the loop). net11.0, ShortRunJob,
MemoryDiagnoser (allocated = per single op):

| Scenario | Mean | Allocated |
|---|---:|---:|
| `SignalChange_PatchesNode` (steady-state reactive patch: signal → flush → one property to the retained node) | **657 ns** | 1.52 KB |
| `Materialize_SmallTree` (5-view tree → nodes, set-only property emission) | 358 µs | 151 KB |
| `Reload_DiffsOntoRetainedNodes` ([Body] rebuild + diff + node transfer — the hot-reload path) | 279 µs | 119 KB |

The steady-state loop (what runs per user interaction) is sub-microsecond with
~1.5 KB/op (dominated by the bound string interpolation). Reload of a small
Body sits ~350× under the plan's 100 ms hot-reload budget. Materialization is
one-time-per-view; its 151 KB (env/context dictionaries) is the number to
revisit when the typed-storage conversion of the remaining string-keyed
environment happens.

## FPS/GC on-device A/B — DEFERRED to hardware

The dual-flavor scroll FPS + GC A/B (legacy handlers vs node backend, gfxinfo
first-pass methodology) needs the physical Pixel 5; emulator frame numbers are
not meaningful and are deliberately not recorded. Run it from tag
`comet-pre-phase5-delete` (the last commit with the legacy render path present)
when the device is available. The Jetchat scroll numbers already on record
(100% jank / 350ms 90th → 3% / 12ms after the LazyColumn row cache, Pixel 5,
Release) stand as the node-backend-only measurement.

## NativeAOT (iOS) — gate: "NativeAOT where supported" — 2026-07-01

`CometSwiftUIProbe` (full shared Jetchat sample) publishes under NativeAOT:
`dotnet publish -f net11.0-ios -c Release -p:RuntimeIdentifier=ios-arm64
-p:EnableProbeAot=true` (the probe csproj maps that to `PublishAot=true`
app-side only — a global `-p:PublishAot` leaks into plain-classlib
dependencies and fails with NETSDK1203, the PublishTrimmed trap again).

ILC compiles end-to-end and produces the package: **7.2 MB .ipa** (17 MB .app,
15.9 MB single native executable — no separate IL assemblies, no JIT).
Remaining trim warnings come from MAUI's `HybridWebViewHandler` (legacy
Microsoft.Maui.Controls dependency — goes away with the Phase 5 delete) and
Comet's debug-only hot-reload reflection (IL2104, suppressed by design).
Simulator RIDs don't support AOT (NETSDK1203) — device RID only.

## Phase 5 COMPLETE: legacy MAUI render path DELETED — 2026-07-01

The gate became a delete (approved): `Handlers/**`, `Maui/**`,
`AppHostBuilderExtensions.cs`, and ALL native platform views
(`Platform/{iOS,Android,MacOS,Windows,Standard}` handler bridges) are gone;
the `CometLegacyRenderPath` MSBuild property and `COMET_LEGACY_RENDER_PATH`
guards with them. The Compose/SwiftUI node backend is the only render path.
Windows and the AppKit macOS heads are parked with it (TFMs removed);
maccatalyst stays for the host suite. The 20 legacy samples
(`UseCometApp`/`UseCometHandlers`) are parked as reference source —
`sample/LEGACY-SAMPLES.md`; the last commit with the legacy path is tagged
`comet-pre-phase5-delete`.

Post-delete verification: all three TFM heads compile; host suite 886 pass /
26 skip / 1 pre-existing known fail (4 handler-only NativeHost tests deleted
with the interface they tested); CometComposeProbe renders on the Android
emulator and CometSwiftUIProbe on the iPhone 16 Pro sim; Release
TrimMode=full links Comet.dll at 265,728 B (larger than June's legacy-off
202,752 B because P1–P7 + hot reload/IME/animations landed since — the point
is the handlers can no longer be compiled in, let alone rooted).

---

# compose-samples build-out (Reply → JetNews → Jetsnack → Jetcaster → JetLagged)

Per-sample sections use the template below (see
`docs/sample-workflow-checklist.md`, step 8). Emulator numbers land with the
sample; Pixel 5 numbers land in batches — **B1** after JetNews (Reply+JetNews),
**B2** after Jetcaster (Jetsnack+Jetcaster), **B3** after JetLagged (final sweep
+ Jetchat re-run to catch framework-growth regressions).

<!-- TEMPLATE — copy per sample
## <Sample> — <date>

| Metric | Comet (emulator) | Comet (Pixel 5, B_) | Gold Kotlin app |
|---|---|---|---|
| Release APK, trimmed single-RID (`tools/bench/size.sh -p:RuntimeIdentifier=android-arm64`) | | | (gold app's own Release APK) |
| Cold start median, 10 runs (`tools/bench/startup.sh`) | | | |
| gfxinfo first-pass jank, heaviest scroll screen (<name>) | | | |
| iOS .ipa trimmed size / launch feel (manual note) | | n/a | n/a |
| Comet.dll size delta vs previous sample | | | |

Notes: <one paragraph — anything anomalous, descopes affecting perf, screen used
for the scroll test, fixture vs live data mode>
-->

## Jetsnack — 2026-07-10

Emulator = Pixel 9 Pro AVD (API 36, arm64, Apple M4 Max host). Gold = the Kotlin
Jetsnack app from ~/work/compose-samples. NOTE: like JetNews, the gold's
minified `app-release.apk` (10.6 MiB) fails to launch on this emulator
(sample-side issue), so runtime comparisons use the gold DEBUG build.

| Metric | Comet (emulator) | Comet (Pixel 5, B1) | Gold Kotlin app |
|---|---|---|---|
| Release APK, trimmed single-RID (`tools/bench/size.sh <probe> -p:CometSingleRid=true`) | 33.5 MiB (35,159,321 B) | — | 10.6 MiB (release artifact) |
| Cold start median, 10 runs (`am start -W --es screen jetsnack`) | 678 ms | — | 876 ms (DEBUG build — release fails to launch) |
| gfxinfo scroll jank, home feed (6 fling swipes) | 7.4% janky, p50 17ms / p90 20ms / p95 26ms | — | 5.3% janky, p50 17ms / p90 21ms / p95 25ms (DEBUG) |
| iOS .ipa trimmed size / launch feel (manual note) | not measured (sim-only gate; Debug launch feels instant after splash) | n/a | n/a |
| Comet.dll (linked, android-arm64) | 834 KiB (854,016 B) | — | n/a |

Notes: the probe now bundles FOUR samples; the +7.3 MiB over the JetNews row
is almost entirely the 36 snack photo jpgs (7.5 MiB of assets), not code.
Cold start at 678 ms is the best Comet number yet (Reply 3705 → JetNews 2347 →
Jetsnack 678 on the same runtime — the Jetsnack feed composes far less at
first frame than JetNews' suite+list-detail chrome) and beats even the DEBUG
gold. Scroll jank is at parity on the gradient-heavy feed. Pixel 5 column
lands with batch B1.

## JetNews — 2026-07-09

Emulator = Pixel 9 Pro AVD (API 36, arm64, Apple M4 Max host). Gold = the Kotlin
JetNews app from ~/work/compose-samples. NOTE: the gold's minified
`app-release.apk` (3.8 MiB) CRASHES at launch on this emulator (sample-side
proguard/deeplink issue), so runtime comparisons use the gold DEBUG build —
gold cold start and jank read worse than a healthy release would.

| Metric | Comet (emulator) | Comet (Pixel 5, B1) | Gold Kotlin app |
|---|---|---|---|
| Release APK, trimmed single-RID (`tools/bench/size.sh <probe> -p:CometSingleRid=true`) | 26.2 MiB (27,475,468 B) | — | 3.8 MiB (release artifact) |
| Cold start median, 10 runs (`am start -W --es screen jetnews`) | 2347 ms | — | 2914 ms (DEBUG build — release crashes) |
| gfxinfo scroll jank, home feed (6 fling swipes) | 13.8% janky, p50 17ms / p90 46ms / p95 81ms | — | 11.3% janky, p50 26ms / p90 42ms / p95 65ms (DEBUG) |
| iOS .ipa trimmed size / launch feel (manual note) | not measured (sim-only gate; Debug launch feels instant after splash) | n/a | n/a |
| Comet.dll (linked, android-arm64) | 829 KiB (849,408 B) | — | n/a |

Notes: the probe APK now bundles THREE samples (Jetchat + Reply + JetNews,
incl. all post images + fonts) — the total is still dominated by the AndroidX
Compose dex + CoreCLR preview runtime, so the size delta vs the Reply row is
+0.1 MiB for a whole extra sample. Comet cold start improved vs the Reply row
(3705 → 2347 ms median) with no tuning; against the DEBUG gold it now reads
faster, but the honest release-gold comparison is blocked on the gold's own
launch crash. Jank is near parity (worse p95, better p50) on the paragraph-
heavy home feed. Pixel 5 column lands with batch B1.

## Reply — 2026-07-08

Emulator = Pixel 9 Pro AVD (API 36, arm64, Apple M4 Max host). Gold = the Kotlin
Reply `app-release.apk` (minified) from ~/work/compose-samples, same device.

| Metric | Comet (emulator) | Comet (Pixel 5, B1) | Gold Kotlin app |
|---|---|---|---|
| Release APK, trimmed single-RID (`tools/bench/size.sh <probe> -p:CometSingleRid=true`) | 26.1 MiB (27,409,987 B) | — | 3.4 MiB |
| Cold start median, 10 runs (`am start -W --es screen reply`) | 3705 ms | — | 773 ms |
| gfxinfo scroll jank, inbox list (6 fling swipes) | 13.0% janky, p50 26ms / p90 40ms / p95 57ms | — | 9.3% janky, p50 28ms / p90 40ms / p95 48ms |
| iOS .ipa trimmed size / launch feel (manual note) | not measured (sim-only gate; Debug launch feels instant after splash) | n/a | n/a |
| Comet.dll (linked, android-arm64) | 819 KiB (839,168 B) | — | n/a |

Notes: the probe APK bundles BOTH samples (Jetchat + Reply, incl. avatar
drawables + icon font) — a per-sample split would shave little since the total
is dominated by the AndroidX Compose dex (~32 MB uncompressed across 3
classes.dex) and the CoreCLR preview runtime (libcoreclr+libclrjit ~9 MB);
managed code is 8.4 MiB (assembly store). No AOT/startup profile tuning yet —
cold start carries full JIT + Compose init (the 4.8× gap vs gold is the
headline framework-maturity number to attack, not the scroll path). Scroll
jank is at parity with the gold app on the same device. Single-RID publish
needs the `CometSingleRid` csproj switch — a raw `-p:RuntimeIdentifiers` flows
into the netstandard2.0 source generator and breaks its copy step. Pixel 5
column lands with batch B1 (after JetNews).

