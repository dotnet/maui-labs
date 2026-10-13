# Performance and honest validation

No packaged bespoke prototype has a measured performance claim from this skill.
Define a workload and prove the generated implementation, not a label-only proxy.
[Sources U3, B2, M5, P5, G9](sources.md)

## Measure separately

| Dimension | Evidence |
|---|---|
| Native realization | Created/realized/pooled/pinned/focused container peaks |
| MAUI reuse | Created/retained trees and handlers across repeat traversals |
| Data/assets | Resident projections, decoded bytes, native/process/GPU memory |
| Geometry | Candidates visited, offscreen measurements, rebuild/seek cost |
| Frame quality | Presented intervals, p95/p99, missed deadlines/hitch duration |
| Loading | Requests, latency, concurrency, deduplication and epoch publication |
| Correctness | Anchors, focus, interaction, accessibility, RTL and resize |

Whole-frame intervals are about 16.67 ms at 60 Hz and 8.33 ms at 120 Hz, not
per-cell bind budgets. Record actual variable cadence. Average FPS, timer ticks,
screenshots and managed stopwatches alone do not prove presented-frame quality.

## Fix in order

1. Lost virtualization/unbounded viewport or eager panel.
2. Small edits causing full invalidation; compatible templates reinflated.
3. UI-thread SQL/HTTP/image decode.
4. All-item geometry scans/measure and excessive invalidation.
5. Deep renderers, expensive getters/converters, redundant native setters.
6. Oversized images, unbounded caches and managed/native allocations.
7. Prefetch/pool/animation tuning based on traces.

Do not add shims/AOT flags/huge caches without evidence. Compare typed native and
retained MAUI rendering with the same content and accessibility.

## Native profiling

| Target | Tools/evidence |
|---|---|
| iOS/Catalyst/AppKit | Instruments native time, allocation and animation/hitch/render traces |
| Android | Perfetto FrameTimeline/system trace; RV bind/layout/prefetch, texture upload and JNI costs |
| WinUI/WPF | WPR/ETW + WPA plus native UI/layout/render work |
| GTK4 | Sysprof/main-loop work and available GTK frame diagnostics; record renderer/display backend |

Pair native evidence with .NET allocations/GC and owned-resource counters using
supported runtime tooling. DevFlow helps inspect native types, tree, screenshots
and input, not certify frame rates. Check installed CLI help before using commands.

## Fixture and budgets

Record revision, SDK/native packages, OS/device, viewport/density/cadence, Release/
AOT/trimming/diagnostics, seeded content/images, load latency/errors, cache bounds,
and exact interactions. Separate cold/warm caches and diagnostics-heavy passes.

Increase logical count at fixed viewport (for example 1k/10k/100k metadata rows)
without loading all images. Native/MAUI counts must stay within declared viewport/
cache/type budgets. Candidate work must match documented geometry complexity.

Set project-specific frame, memory, initial viewport, update and anchor budgets.
Starter frame goals may be p95 <= actual cadence interval and p99 <= two intervals,
with a separate missed-deadline/hitch budget. Calibrate on minimum hardware,
record tolerance/definitions, and never report those goals as achieved results.

After warm-up, repeated equivalent traversal should not create compatible trees
on every bind or retain ever-growing resources. Inspect native memory too:
4000x4000 four-byte pixels need about 64 MB before textures/copies.

## Tests and completion

Portable tests: zero/one/partial rows, geometry intersections against a slow
reference, size invalidation, identity/content diffs, groups, ordered changes,
load races/deduplication, anchor fallback and bounded caches.

Native tests: actual host/layout/renderer types, finite viewport, reuse reset,
late assets, source/context replacement, connect/disconnect, focus/input,
accessibility, chrome and resize. Simulator/emulator/headless tests do not
establish physical-device GPU/input performance.

Use existing AppKit/GTK native runtime hosts when working in maui-labs. Report
unavailable platforms explicitly; do not silently skip and claim all passed.

Return a per-target status:

```text
source inspected | API compiled | native behavior tested
lifecycle tested | Release performance measured | not-run/blocked
```

Attach actual metric values/budgets, device manifest and trace locations.
Unknown metrics are not zero; a screenshot, timeout or portable test is not a
passing native benchmark. Do not remove required behavior to satisfy a budget.

## Interaction-specific evidence

For requested [interaction contracts](interaction-contracts.md), measure proposal/
draw/auto-scroll latency separately from mutation diff/layout and repository
latency. Bound previews, active controllers, drag-retained cells, popovers/adorners
and listeners; no full decode/source scan in hot interaction callbacks.

Exercise reorder/selected blocks/groups/empty targets; late swipe/menu/confirmation
after rebind; mutation failure/undo and page resurrection; refresh A/B ownership/
deferral cleanup; source replacement during drag/import and focus/accessibility.
Native examples and authored model evaluations are not execution evidence.
