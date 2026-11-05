# Performance optimization and validation

[Research index](README.md) | [Shared design](shared-design.md) |
[Skill plan](skill-plan.md)

This is a **measurement protocol and proposed acceptance model**. No results for
a new collection control are asserted here. A native toolkit's reputation, a
large logical count, and a visually smooth short demo are not substitutes for
measuring the requested workload.

## 1. Define what "high performance" means

Measure the user's scenario, not an empty-cell proxy:

| Metric | What it reveals |
|---|---|
| Time to first useful viewport | Initial query, first layout, native/MAUI creation and images |
| Presented frame intervals / missed deadlines | Visible scrolling quality, including native render/GPU work |
| Main-thread layout/bind/commit p50/p95/p99 | Hot-path cost and long-tail stalls |
| Native container and MAUI tree creation counts | Whether both levels actually recycle |
| Realized/pooled/pinned/focused peak counts | Bounded realization and overscan policy |
| Geometry candidates visited / items measured per scroll | An O(N) scan hidden behind view virtualization |
| Managed allocations/GC plus process/native/GPU memory | Bridge overhead and assets outside the managed heap |
| Page/query/thumbnail latency and request count | Loading bottleneck, duplicate requests and prefetch pressure |
| Update/resize/prepend duration | Scrolling can be fast while mutations are disruptive |
| Anchor/focus correctness | Performance optimizations that accidentally change behavior |

At 60 Hz the interval is approximately **16.67 ms**, at 90 Hz **11.11 ms**, and at
120 Hz **8.33 ms**. These are **whole-frame intervals**, not budgets that can all
be spent in one item binder. Variable-refresh displays also require recording
the actual frame cadence.
[U3](sources.md#u3) [B2](sources.md#b2)

Average FPS hides isolated long hitches. Prefer percentile intervals, deadline
misses/hitch duration and trace spans alongside averages. A managed stopwatch,
timer tick or `DisplayLink` callback alone does not establish presented-frame
quality or GPU completion.

## 2. Optimize in dependency order

| Order | Investigate | Fix only if evidence supports it |
|---|---|---|
| 1 | Lost virtualization / unbounded viewport | Correct scroll owner, native panel/layout and finite constraints |
| 2 | Repeated full invalidation or template inflation | Native incremental updates and compatible child-tree reuse |
| 3 | Synchronous I/O / decode on UI thread | Repository loading and documented background asset preparation |
| 4 | All-item geometry/measurement in scroll | Metadata, indexed range lookup, cached constraints and precise invalidation |
| 5 | Expensive per-item rendering | Shallow trees, simpler content, compiled/typed binding and fewer invalidations |
| 6 | Managed/native transitions and allocations | Cache references/values, skip redundant setters, batch where justified |
| 7 | Asset memory / GPU upload | Pixel-sized thumbnails and explicit decoded-byte budgets |
| 8 | Prefetch/cache/animation tuning | Balance latency, memory and power for the actual devices |

AOT or a faster serializer cannot fix a nonvirtualizing `WrapPanel`.
Increasing a recycler pool cannot fix blocking SQL in `OnBind`.
Disabling all animation/selection/accessibility is not an acceptable blanket fix.

### Renderer strategy: compare, do not assume

For a fixed card design, a typed native renderer avoids some managed layout/binding
machinery and may be the best option. A cached MAUI template can still be an
appropriate control contract. Benchmark the same content, images and behavior:

```text
native reference -> typed native handler -> cached MAUI template handler
```

Include native view count, MAUI tree count, time, allocation and accessibility
parity. Do not compare a fully featured MAUI card with a single-label native row
and attribute all differences to binding.

## 3. Published optimization lessons

| Source | Mechanism found/documented | Applicable lesson | Limit |
|---|---|---|---|
| Apple WWDC 2021 | Reused registrations, prefetched preparation, item reconfiguration, background image/thumbnail preparation | Keep configuration cheap; asynchronous content updates must be keyed, not tied to an old cell | UIKit-specific APIs and lifecycle; not a cross-platform frame guarantee |
| Android rendering guidance | Full invalidation, expensive bind/inflate/layout, compatible shared nested pools, texture uploads | Incremental updates, shallow renderers and pixel-sized assets; prefetch uses available UI time | Native Java examples and historical timing guidance need real managed/device traces |
| MAUI .NET 8 work | Java setter/marshaling overhead during scrolling; per-item context storage on Windows | Native bridges and metadata allocations matter even when views virtualize | Historical workload measurements, not new-control expected gains |
| CHT waterfall source | Cached attributes, metadata aspect ratios and union rectangles | Separate geometry preparation from view creation; reject unnecessary width-independent rebuild | Still linear union lookup/full geometry prepare |
| WinUI Gallery layouts | Realization-window arithmetic, context state, cached masonry bounds | Implement the native virtualizing layout contract, not an eager panel | Sample assumptions and linear lookup need production hardening |
| GTK native demos | Reusable widgets/model expressions with lazy object creation | UI and source architecture can scale independently | Huge model counts still have pointer-table/sorting costs |
| WPF guidance / virtualizing wrap source | Recycling, compatible generator/scroll contracts, metadata sizing | A wrapping layout needs real virtualization and size information | Library compatibility and publisher claims need independent evaluation |
| Texture | Asynchronous node layout/drawing and range-driven preparation | Some workloads benefit from a different rendering/ownership architecture | Nodes do not have ordinary cell reuse; MAUI views are not thread-safe nodes |

References: [U3](sources.md#u3), [B2](sources.md#b2), [M5](sources.md#m5),
[U6](sources.md#u6), [W8](sources.md#w8), [G7](sources.md#g7),
[P1](sources.md#p1), [P3](sources.md#p3), [U8](sources.md#u8).

## 4. Measurement traps

**Images:** a 4000 x 4000, four-byte-per-pixel decoded image needs about 64,000,000
bytes before extra copies/textures. Displaying it in a small image view does not
remove that decode/upload cost. Account for target pixel density, not only logical
card width, and record compressed disk versus decoded memory separately.

**Multiple caches:** native cell caches, managed template pools, HTTP/disk caches,
decoded images, view-model subscriptions and geometry metadata can all retain
different things. Report each; managed heap alone misses much of the cost.

**Warm reuse:** creation in an initial viewport is expected. After a warm traversal,
compatible cells/trees should stop being recreated on every bind. Test reverse
scrolling, rare row kinds and changes of renderer/template.

**Source size:** UI virtualization can coexist with an O(N) record list, full
snapshot diff, rectangle array or source wrapper. Increase logical/source scale
while holding viewport and content constant to expose hidden costs.

**Measurement strategy:** fixed extent/metadata helps; applying the first item's
size to variable content is a correctness bug, not a performance win. The
`MeasureAllItems` name in MAUI does not mean it is appropriate to instantiate all
offscreen rows in a custom handler.

**Debug instrumentation:** native profilers, debugger/DevFlow, tracing and animated
screen capture can change timings. Use a separate measured Release pass and record
which diagnostics were active.

**Headless/virtual environments:** simulator, emulator and Xvfb tests are useful
for correctness but not complete evidence for real input, GPU/display behavior or
low-end hardware. Do not infer a rendering defect from a black capture before
checking the capture/display environment.

## 5. Platform profiling plan

| Target | Native evidence | Managed/bridge evidence |
|---|---|---|
| iOS / Catalyst | Instruments Time Profiler, animation/hitch/render traces, allocations and image work | .NET allocations/GC where supported; cell/MAUI tree/handler counters |
| Android | Perfetto FrameTimeline/system trace; RecyclerView bind/layout/prefetch spans and texture uploads | .NET allocations/GC, JNI setter/callback cost and holder/template counters |
| WinUI | WPR/ETW + WPA, UI/layout/render and input timeline | .NET allocations/GC, element factory/lifecycle and key-update spans |
| AppKit | Instruments/native view/controller/scroll and render work, live-resize trace | .NET allocations/GC, ownership counters and geometry lookup spans |
| GTK4 | Sysprof/main-loop/native work, GTK frame diagnostics when supported; record renderer/display backend | .NET/GirCore allocation, model/factory/MAUI handler counters |
| WPF | ETW/WPR/WPA and native layout/render evidence | .NET allocations/GC, item-generator/panel and template counters |

Consult installed tool versions and supported providers before prescribing a
capture command. A tool name does not promise managed symbol resolution on every
native runtime. Relevant primary guidance:
[U3](sources.md#u3), [B2](sources.md#b2), [P5](sources.md#p5),
[G9](sources.md#g9).

DevFlow is useful for visual-tree/native-type inspection, screenshots, navigation
and reproducible interaction. It should supplement the native profile, not be
treated as proof of presented frame rate. For a new native-heavy control, expose
appropriate semantics/automation rather than assuming every native item appears
automatically in the MAUI logical tree.

### Apple scrolling: capture, analyze, interpret

The distributed skill now includes an [Apple-specific walkthrough](../../plugins/dotnet-maui/skills/maui-bespoke-collections/references/apple-scrolling-profiling.md):
explicit iOS/Catalyst/AppKit target selection, bounded xctrace capture, matching
signposts, TOC-driven export, commit/render attribution, symbols, native/managed
memory and supported Mono diagnostic tracing. It keeps the actual scrolling/
animation denominator and variable cadence, distinguishes buffered frame lifetime
from presentation intervals, and rejects unsupported/empty data instead of using
CPU samples or display callbacks as substitute frame measurements.

The 2026-10-13 local native-utility smoke checked xctrace 27.1 capture/export and
one completed Points of Interest interval. It did **not** measure a MAUI collection
or qualify physical iOS, Catalyst or AppKit scrolling. The earlier sample's
physical performance gates remain pending.

## 6. A repeatable benchmark fixture

Record a manifest containing:

- control/source revision and target backend;
- SDK/workloads/native package versions, OS, device, display cadence and scale;
- configuration, AOT/trimming settings and diagnostics;
- viewport dimensions, layout parameters, data seed/count and row kinds;
- text lengths/font scale and image dimensions/formats;
- page size, latency/error simulation, concurrency and cache budgets;
- cold/warm state and exact interaction sequence.

### Populated-workload validity is a gate

Before steady-state counter reset/capture, verify the intended app, platform,
window/screen and native host/layout; finite viewport; expected filtered/presented
count; actual loaded visible keys/content; settled native frames/update/transition;
and the accepted stable dataset revision. Repeat before each variant/repeat and
after the workload. Timeout, empty/unintended content or drift is an invalid run,
not evidence of speed.

Use a deterministic offline repository or explicitly isolated controlled transport
for renderer comparisons. Normal startup auth/sync/observers/feature gates must
not silently clear or replace seeded records. Keep production behavior unchanged
outside a scoped fixture. Startup timing can capture before readiness, but must
still reach and verify its expected first useful viewport.

See the [optional harness contract](../../plugins/dotnet-maui/skills/maui-bespoke-collections/references/benchmark-harness.md)
for run/variant selection, independent native/tree/cache/request/subscription
counters, matched repeated A/B traversal, metric sample/cadence definitions,
narrow diagnostics and cleanup/production-build verification. Callback histograms,
captured traces and analyzed native presentation evidence are not interchangeable.

Use the same seeded data/reference images and gestures across implementations.
Do not benchmark a no-image Android control against an image-heavy iOS control.
Compare each target to a native reference on that same target.

Suggested **fixture sizes**, not minimum production requirements:

| Fixture | Data/load design | Operations |
|---|---|---|
| Fixed list/grid | 1,000, 10,000, then 100,000 cheap metadata rows | Sustained forward/reverse fling, resize, jump, small changes |
| Photo masonry | Varied known aspect ratios, bounded captions, thumbnails | Cold/warm scroll, late image completion, rotation/resize, filter |
| Grouped timeline | Empty/small/large groups and uneven text | Prepend, move across groups, header/footer updates, text-scale change |
| Shelves | Many sections with bounded inner viewports | Outer recycle, inner offset restore, diagonal gesture, reorder |
| Known-count data window | Large logical count, delayed indexed pages | Deep seek, reverse seek, page eviction/refetch and focus retention |
| Cursor feed | Unknown total, empty/error/repeated-cursor responses | Initial fill, append, refresh cancellation, end-of-data and retry |
| Coverflow | Fixed cards with transformed overlap | Fast snap, keyboard movement, hit testing, reduced motion |

Do not require loading 100,000 images into memory to test a large logical dataset.
Use realistic sparse/windowed assets and measure actual demand.

## 7. Proposed numeric gates

Budgets must be established for the requested minimum hardware, renderer and
scenario before implementation. The following are **starter gates to calibrate**,
not achieved results or universal toolkit guarantees:

| Gate | How to make it falsifiable |
|---|---|
| Frame quality | For steady-state scrolling at cadence T, start with p95 presented interval <= T and p99 <= 2T; also set a missed-deadline/hitch budget. Record cadence changes and native measurement definitions. |
| Realization | Declare a maximum allowed native container/MAUI tree count for the viewport, native overscan, pool, focus, pinning and row kinds; report actual peak at each dataset size. |
| Reuse | After warm traversal, count new compatible cell/tree creations during a repeat traversal; permit only explicitly justified pool/type/viewport growth. |
| Geometry | Instrument visited candidates and offscreen measurements; fixed geometry should approach O(V), indexed variable geometry O(log N + V) or a documented per-column equivalent. |
| Memory | Declare separate managed/process/decoded-asset budgets and allowed steady-state growth; retained memory must plateau across repeated equivalent traversals. |
| Loading | For a scripted duplicate demand, assert one shared request per query/page key; assert concurrency <= configured limit and no old-epoch publication. |
| Anchor | For unchanged cross-axis/font constraints, assert keyed anchor identity and relative offset within declared subpixel/pixel tolerance after prepend/remove/size corrections. |
| First viewport/update | Set a millisecond budget measured on the agreed device and workload; separate query/network time from control rendering time. |

A p95 pass can still hide unacceptable p99 hitches; an average FPS pass can hide
both. If an initial budget proves unrealistic, report the evidence and consciously
revise it instead of silently changing the test or removing required content.

Big-O claims also need scale evidence. Compare candidate-visit/measure counts and
time across dataset sizes while holding viewport fixed. A clipped final frame is
not a legitimate way to reduce measured layout work.

## 8. Correctness and lifecycle tests

Portable tests should exercise real source/geometry logic, not mocks that repeat
the algorithm being tested. Where useful, compare indexed layout lookup to a slow
correct reference on randomized small fixtures.

| Area | Required cases |
|---|---|
| Geometry | Zero/one items, partial last row, spacing/insets, finite/nonnegative sizes, orientation, RTL, extreme aspect ratios |
| Measurement | Width/style/font changes, late size correction, changed renderer, atypical first item, bounded convergence |
| Changes | Add/remove/replace/move/reset, ranges, source replacement, duplicate-key rejection, section mutation/collapse |
| Paging | Shared demand, cancellation race, retry/error, terminal cursor, repeated cursor/no progress, bounded cache, query epoch |
| Anchoring | Prepend, deleted anchor fallback, resize, restored nested offset, follow-tail versus user-scrolled history |
| Reuse | Old image/text/selection/expansion/transforms do not leak, subscriptions released, full versus partial binds |
| Handler | Connect/disconnect/reconnect, context/source changes, disposed callbacks, owned/shared resource distinction |
| Interaction | Invocation versus selection, keyboard traversal, focus recovery, wheel/trackpad, transformed hit testing |
| Accessibility | Native role/name/state, virtualized bring-into-view, logical order, group headings, text scale/high contrast |

Anchor tolerance applies only to unchanged constraints. A width/font change can
legitimately reflow the anchor's item; test the specified identity/placement policy
instead of pretending absolute pixel coordinates must remain constant.

## 9. Native integration and fidelity

On every requested platform:

1. Assert the actual native host/layout/renderer types, not just a similar screenshot.
2. Assert a finite viewport and bounded realized tree.
3. Exercise the complete scrolling, update, loading and teardown sequence.
4. Verify group chrome, selection/focus, accessibility and resize behavior.
5. Measure the agreed Release workload and attach structured evidence.

Use existing backend runtime harnesses when adding a repo-level implementation.
AppKit's shared host already covers native collection/card resize scenarios; GTK's
runtime tests require explicit native environment setup. Portable test success
does not establish either native rendering.
[R5](sources.md#r5)

Do not emit "supports GTK masonry" when its handler is an eager FlowBox or a stub.
Do not call Material carousel a faithful 3D coverflow. If exact placement/fidelity
was requested, compare against the approved/native reference; "looks reasonable"
is a different acceptance condition.

## 10. Evidence and honest completion

Save a structured result with actual metric values, budgets, trace paths, device
manifest, native types and test outcomes. For unmeasured targets, record
`not-measured`, not zero frame time or a passing default.

Report separately:

```text
API/source research -> compile verified -> native behavior verified
                   -> lifecycle verified -> performance measured
```

An unsupported/unavailable device is a platform limitation to state clearly,
not a reason to mark all requested handlers verified. The proposed skill should
produce complete working code only within its evidenced capability set, and leave
an explicit blocker rather than a success-shaped fallback.
