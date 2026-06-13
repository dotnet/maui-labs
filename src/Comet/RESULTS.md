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
