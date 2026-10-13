# Valid collection benchmarks and an optional repeatable harness

Read when comparing/profile-testing implementations, not for every property fix.
This is a measurement contract, not a packaged benchmark framework or an achieved
performance claim. [Validation](performance-and-validation.md)

## Hard populated-workload gate

Before resetting frame counters or capturing the measured interval, assert:

1. Intended app/package, platform, window, active screen and native host/layout.
2. Finite, nonzero viewport with the intended scroll owner.
3. Expected **filtered/presented** data count, not only database/logical count.
4. Expected actual visible keys and loaded content/chrome; placeholders or a
   transient empty view do not satisfy a populated fixture.
5. Settled native layout/update/transition and frames matching current geometry.
6. Accepted query/dataset revision and no unauthorised writer/background task.

Use deterministic in-memory/offline repository data by default for renderer
comparisons, or an explicitly isolated controlled transport. Normal authentication,
sync, deletion, feature gates or observers must not replace the fixture during
the run. A private seeded database does not help if normal startup sync can clear
it. Do not switch off production behavior globally as a benchmark workaround.

Keep source/seed/filter/count/keys stable across variants. Record permitted changes
if refresh/paging is the workload; verify their exact expected outcome. Gate before
**each** variant/repeat and after traversal. Any timeout, unintended host/screen,
missing content, count/revision drift or unsettled layout is **invalid**, not fast,
zero-cost or successful empty content.

For startup measurements, capture can start before readiness to measure time to
first useful viewport. The run must still reach and verify its expected loaded
viewport; do not conflate this interval with steady-state scroll metrics.

## Minimal optional harness, not production hooks

Keep the smallest fixture/entry separate from normal application lifecycle where
possible. Explicitly select a typed/enumerated variant rather than mutating source
files between A/B runs or silently falling back to a default renderer.

```text
seed/query/filter/count + expected keys/chrome
variant + target/device/window + readiness timeout
equivalent scripted forward/reverse traversal and refresh/editor cases
cold/warm cache policy + permitted data changes
run ID + source/build revision + validity result + artifacts
```

Mix short/long text, header/footer, nonzero margins/padding and reserved image
metadata where those are part of the real design. Do not compare a label-only
row to a fully featured card and blame all differences on binding.

Use stable automation IDs plus keyed/frame assertions, not taps at an old device
size or an offset field alone. Prefer narrow queries/properties/counter summaries;
save scoped logs/trace artifacts only when needed instead of repeatedly dumping
whole trees or raw logs into context.

## Independent counters

| Counter | What it can prove |
|---|---|
| Native created/realized/recycled/pooled | Native container bounds and warm repeat churn |
| MAUI trees/handlers created/retained/disconnected | Actual child reuse and ownership, not just native reuse |
| Payload/window records and encoded/decoded bytes | Source/assets, independently bounded |
| Pending/active page, thumbnail and decode operations | Concurrency, demand and drain after cancellation |
| Subscriptions/controllers/timers/popups/previews | Lifetime plateau after repeated interactions |
| Geometry candidates/measured rows/rebuilds | Visibility lookup and update complexity |
| Actual visible keys/revision/frames | Correct populated workload and settled layout |

Counters do not substitute for native process/GPU memory or frame analysis.
Snapshots before teardown and replacement/disconnect must be separate; a zeroed
new handler must not erase the primary run's peaks.

## Metric provenance and repeated comparisons

For every reported metric state producer/API, definition, units, run/variant,
sample count, interval/cadence, reset scope, cold/warm state and diagnostic overhead.
Unknown, unsupported, invalid and not-collected are not zero.

| Evidence | Permitted claim |
|---|---|
| Stopwatch/layout callback | That managed/layout operation's duration |
| Timer/DisplayLink/Choreographer histogram | Observed callback intervals, not automatically displayed frames |
| Native frame histogram | Its actual API-defined app/window samples, exclusions and sample count |
| Captured trace | Trace collected; percentiles/hitches not known until analyzed |
| Analyzed FrameTimeline/render trace | Defined frame/presentation spans with actual sample/filter/cadence and limitations |
| Simulator/emulator result | Virtual-device behavior/diagnostic evidence, not physical display qualification |
| Physical-device repeated runs | Results for that device/workload, not every layout/platform |

For close A/B results, repeat matched cold/warm conditions and alternate/randomize
order where appropriate. Report spread/sample counts and plausible noise rather
than a broad faster/slower guarantee from one pass. Keep control/data/accessibility
contracts equivalent. Calibrate budgets on intended minimum hardware.

## Cleanup manifest and final application build

Record task-owned instrumentation/configuration, seeded data, temporary hooks,
services, captures, device mappings, package/settings changes and restoration plan
before adding them. Keep legitimate production fixes; remove or explicitly gate
benchmark-only entry points and data. A cleanup manifest is scoped operational
state, not a required new Markdown file.

After the run, verify restored runtime settings, no active task-owned subscriptions/
requests/timers/services, expected production startup/source/renderer and a clean
production-configuration build. Do not kill unrelated processes/devices or erase
real application data. Retain useful narrow evidence outside shipped artifacts.

Unavailable physical hardware/native analysis stays a named qualification gate.
Do not edit pins, signing checks, durability or required behavior merely to make
the fixture look successful.
