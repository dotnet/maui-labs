# Bespoke collections: mobile photo masonry

An experimental, **nonshipping** example for the
[bespoke collections skill](../../plugins/dotnet-maui/skills/maui-bespoke-collections/SKILL.md).
It demonstrates one narrowly defined layout, not a new universal `CollectionView`
replacement. See the [research and roadmap](../../docs/collections/skill-plan.md)
and [execution evidence](EVIDENCE.md).

**Rendering is entirely native.** iOS uses `UICollectionView` with a custom
`UICollectionViewLayout`; Android uses `RecyclerView` with a custom
`LayoutManager`. Each card is a native image view plus a two-line native caption.
There is no WebView, HTML, JavaScript, or web-rendered layout.

HTTP is only this example's repeatable **data fixture**: metadata pages and
delayed thumbnails. It lets the proof exercise real I/O, cancellation, failure,
retry, and late images. The recipe does not require an HTTP data source.
For an in-memory or database-backed variant, change the repository/loading
boundary while preserving identity, geometry, publication, and ownership rules.
This particular sample wires `PhotoRepository` to HTTP; it does not implement a
database provider or a generic data-source framework.

Debug builds optionally include the in-repo DevFlow inspection agent, whose
transport is also HTTP. **Release builds do not include that agent.**

## Deliberate scope

| Capability | This example |
|---|---|
| Targets | iOS and Android only |
| Layout | Vertical, deterministic shortest-column masonry; responsive 2-4 columns |
| Sizing | Metadata aspect ratios, 8-unit gaps/insets, fixed 48-unit caption extent |
| Renderer | Typed native cells; no MAUI `DataTemplate` trees inside cards |
| Identity | Stable long keys, thumbnail revision, source epoch, bind generation |
| Updates | Append, prefix eviction, query reset, source/handler replacement |
| Anchoring | Surviving key and relative offset on append/prefix eviction; query reset goes to top |
| Loading | Cursor pages, single-flight page demand, visible errors and explicit retry |
| Bounds | 500 metadata records; 4 MiB encoded cache; 16 MiB applied decoded pixels |
| Thumbnail work | 64 pending keys, two HTTP requests, two decode/preparation operations |
| Native reuse | UIKit reuse queue; Android pool sized to peak viewport/overscan realization |
| Not implemented | Selection/invocation, groups, pinned headers/footers, arbitrary edits, self-sizing templates, RTL, desktop handlers |

The decoded-pixel budget is not a total process/GPU memory budget. HTTP buffers,
in-flight preparations, native bookkeeping, textures, and managed objects require
separate measurement. The fixture's fixed captions also do not certify dynamic
type or arbitrary text layouts.

## Structure

| Project | Responsibility |
|---|---|
| `Core` | Metadata geometry, indexed visibility, cursor feed, epochs, thumbnail cache and work limits |
| `Fixture` | ASP.NET Core service with original deterministic PNGs, delays and scripted failures |
| `App` | MAUI page/control, typed UIKit/RecyclerView handlers, native proof |
| `Tests` | Portable geometry, loading/cache races, fixture, decoder-lifetime tests |

Geometry preparation is O(N x C) on source or width changes, with N bounded to
500 and C at most four. Per-column interval indexes find viewport candidates
without scanning every retained frame. Native bind/measure never waits for HTTP
or image decoding; binding registers cancellable asynchronous thumbnail demand.

No third-party waterfall library, app-wide adapter framework, or published
package is introduced. `BespokeCollections.slnx` contains only the portable
projects; build the mobile app separately with its own SDK pin.

## Portable tests and fixture

From the repository root with .NET 10 installed:

```bash
dotnet test samples/BespokeCollections/BespokeCollections.slnx -c Release

ASPNETCORE_URLS=http://127.0.0.1:18550 \
  dotnet run --project samples/BespokeCollections/Fixture -c Release
```

Keep the fixture terminal running. Check `http://127.0.0.1:18550/health` before
connecting the app. The endpoint entry defaults to that URL.

The fixture has 100,000 logical photos per query, generated on demand rather
than loaded as 100,000 images. Queries `"0"` and `"1"` use disjoint keys. Pages
contain 50 records; metadata latency defaults to 120 ms and thumbnail delays to
80-260 ms. These are deterministic test choices, not production HTTP guidance.

```bash
curl --fail http://127.0.0.1:18550/health
curl --fail http://127.0.0.1:18550/stats
curl --fail --request POST http://127.0.0.1:18550/control/fail-next
curl --fail --request POST http://127.0.0.1:18550/control/latency/500
```

`fail-next` fails one metadata request with HTTP 503. In the UI, **More** retries
the failed cursor; automatic paging pauses while an error is visible. **Refresh**
alternates queries. The native proof drives its own failure/retry scenario, so
do not concurrently arm failures or run another proof against the same fixture.

## Mobile toolchain

The sample-local `App/global.json` pins SDK **10.0.401** and workload set
**10.0.401**, separately from the machine's global workload selection.
The tested app package baseline is MAUI **10.0.110**, Android SDK **36.1.69**,
and Apple SDK **26.5.10318** with **Xcode 26.6**. App TFMs are
`net10.0-android36.0` and, on macOS, `net10.0-ios26.5`.
This does not update the repository's shared MAUI baseline.

If this exact workload set is not installed, install it locally using a .NET
10-or-newer host. From `samples/BespokeCollections/App`:

```bash
curl --fail --location https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
bash dotnet-install.sh --version 10.0.401 --install-dir .dotnet
rm dotnet-install.sh

# Always use the local binary for workload installation.
.dotnet/dotnet workload install maui-ios maui-android \
  --configfile ../../../NuGet.config
.dotnet/dotnet workload list
```

On non-macOS hosts install only supported workloads, such as `maui-android`.
Debug builds reference the multi-targeted in-repo DevFlow agent; on macOS its
restore graph also needs local `macos` and `maui-maccatalyst` workloads.

Use `DEVELOPER_DIR` for the app build instead of changing global `xcode-select`.
Xcode validation remains enabled. The sample's `Directory.Build.props` preserves
Arcade's repository/artifact root despite the nested `global.json`.
`.dotnet/` is gitignored; removing that specific app-local SDK directory undoes
the local installation without changing the system SDK.

## Build and run

From `samples/BespokeCollections/App`:

```bash
# Android arm64 device/emulator. Choose the RID for the actual device.
.dotnet/dotnet build BespokeCollections.App.csproj \
  -f net10.0-android36.0 -c Release -r android-arm64 \
  -p:AndroidPackageFormats=apk

# iOS simulator on Apple Silicon. Select an installed compatible Xcode.
DEVELOPER_DIR=/Applications/Xcode_26.6.app/Contents/Developer \
  .dotnet/dotnet build BespokeCollections.App.csproj \
  -f net10.0-ios26.5 -c Release -r iossimulator-arm64
```

Artifacts are under repository-root `artifacts/bin/BespokeCollections.App/`.
Always pick explicit, idle devices. Do not use an emulator or simulator already
running another investigation.

For Android, replace `SERIAL` with the intended device. Install the corresponding
`org.dotnet.mauilabs.bespokecollections-Signed.apk` and reverse only the fixture port:

```bash
adb -s SERIAL install -r PATH_TO_SIGNED_APK
adb -s SERIAL reverse tcp:18550 tcp:18550
adb -s SERIAL shell am force-stop org.dotnet.mauilabs.bespokecollections
adb -s SERIAL shell am start -W \
  -n org.dotnet.mauilabs.bespokecollections/crc644f624da821adb651.MainActivity \
  --ez run-proof true
```

For iOS, install the `.app` on the chosen booted simulator:

```bash
xcrun simctl install UDID PATH_TO_APP
SIMCTL_CHILD_BESPOKE_RUN_PROOF=1 \
  xcrun simctl launch UDID org.dotnet.mauilabs.bespokecollections
```

Alternatively launch normally, press **Connect**, then **Run proof**.
Android embeds managed assemblies in its APK so standalone installation does
not depend on fast-deploy override files.
The component above is the generated name for this sample; after renaming its
assembly/namespace, resolve the new component with
`adb -s SERIAL shell cmd package resolve-activity --brief org.dotnet.mauilabs.bespokecollections`.

## Native proof and profiling boundary

The proof checks actual host/layout types, settled native frames and visible
keys against portable geometry, warm/repeat creation counts, delayed image
identity, failed-page retention/retry, query replacement, 500-record window
eviction, relative anchors, image budgets, and disconnect/replacement.
It is not a frame-rate test or accessibility certification.

Each run removes its own old report. Success writes `status: "passed"`; failure
writes `status: "failed"` with the error and native snapshot. A stale passing
file must never be used as evidence for a newer run. Both report shapes include
a unique `runId`, UTC `startedAt`/`completedAt` and `scrollWorkload`; verify those
fields against the intended invocation rather than trusting an old UI label.

- Android: app-owned external `files/native-proof.json`, retrievable without
  marking Release debuggable:
  `adb -s SERIAL pull /sdcard/Android/data/org.dotnet.mauilabs.bespokecollections/files/native-proof.json`.
- iOS: `Library/native-proof.json` in the app data container returned by
  `xcrun simctl get_app_container UDID org.dotnet.mauilabs.bespokecollections data`.

The full file matters: Android logcat truncates long JSON records. Inspect
`native` (before replacement) and `replacementNative` separately. The replacement
snapshot must not erase the primary traversal's metrics.

For a native Release trace, add `--ez scroll-workload true` on Android or
`SIMCTL_CHILD_BESPOKE_SCROLL_WORKLOAD=1` on iOS. The optional stimulus makes four
forward/reverse 0-6000-unit passes with 16 ms waits. Those waits are **not**
presented frame measurements. Android emits a `MasonryScrollWorkload` async trace
span and is shell-profileable, not debuggable. Use native FrameTimeline/Instruments
evidence with an explicit workload, device/cadence, and measurement definition.

The Android capture configuration and analysis are checked in under `Profiling/`.
From `samples/BespokeCollections`, with only the selected device/app and fixture
running:

```bash
adb -s SERIAL shell perfetto --background-wait --txt -c - \
  -o /data/misc/perfetto-traces/bespoke-collection-scroll.pftrace \
  < Profiling/android-scroll.pbtxt
adb -s SERIAL shell am force-stop org.dotnet.mauilabs.bespokecollections
adb -s SERIAL shell am start -W \
  -n org.dotnet.mauilabs.bespokecollections/crc644f624da821adb651.MainActivity \
  --ez run-proof true --ez scroll-workload true
```

The recorder is bounded to 30 seconds; confirm it completed before pulling the
trace. Do not kill other profiling sessions. Use the officially published Perfetto
trace processor locally, without uploading traces:

```bash
adb -s SERIAL pull \
  /data/misc/perfetto-traces/bespoke-collection-scroll.pftrace
trace_processor query -f Profiling/android-frames.sql \
  bespoke-collection-scroll.pftrace
```

Require exactly one completed workload span, nonempty app/presentation records,
and no positive parser errors before interpreting the tables. Missing rows or
NULL statistics are not zero-cost frames. Inspect Prediction Error and other
native classifications; emulator scheduling is not physical display performance.

Simulator/emulator results are behavior and diagnostic evidence, not physical
device performance qualification. Consult [EVIDENCE.md](EVIDENCE.md) for actual
runs, trace limitations, and remaining gates.

After use, stop only this fixture/app and remove only mappings created for it:

```bash
adb -s SERIAL reverse --remove tcp:18550
```

DevFlow's optional broker/agent mappings are separate and unnecessary for the
standalone Release proof. Do not alter other devices' mappings or global tools.
