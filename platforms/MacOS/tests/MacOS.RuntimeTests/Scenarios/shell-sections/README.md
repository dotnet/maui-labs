# Shell section selection

This module replaces the standalone `ShellSectionRegressionTests` executable.
It uses the shared host's `MauiRuntimeScenario` adapter, full
`MacOSMauiApplication` startup, Essentials registration and a real 800x600
MAUI/AppKit window. It does not create a separate app project or CI job.

Run with the shared runner on macOS:

```sh
python3 -B platforms/MacOS/tests/MacOS.RuntimeTests/run.py \
  --scenario shell-sections --evidence "$PWD/artifacts/shell-sections"
```

One completed lifecycle case must execute **exactly 59 assertions**, preserving
the original sequence: initial lazy page, route navigation and `Navigated`,
direct section/content changes, cached revisits, hide/remove/restore, dynamic
two-content section insertion and removal, inactive selection, handler rebind,
and queued selection followed by disconnect. Every displayed-page check verifies
the MAUI current page, native tree attachment/bounds, and visible native label
text/bounds. The shared two-turn main-queue drain includes callbacks enqueued by
selection callbacks, without replacing native dispatch with sleeps.

The baseline overlays only the three original Shell handler entry-point sources
from immutable pre-fix commit `23ea45e98b84bfbf9dd00f4859fd4cf30d44120d`.
The added selection partial remains compiled but inert: the old handlers neither
connect its lifecycle helpers nor invoke its queue. Host, scenario, dependencies
and unrelated production fixes remain identical between baseline and fixed.
The runner restores the overlaid sources even on failure.

Baseline acceptance requires successful compilation and exit 42 with
`shell-sections.lazy-route` and the exact regression message. The scenario first
observes a route ending in `/two`, null `CurrentPage`, one created template,
the original native view still attached, and no `Navigated` event.
An arbitrary exception, timeout, different failure, or failed build is not a
successful reproduction.

Both variants require `assertions.txt`, `route-state.txt`, `initial.png` and
`route-two.png`; fixed additionally requires `content-three.png`,
`dynamic-content-two.png` and `replacement-target.png`. Shared `result.json`
replaces the former `passed.txt`/`failure.txt` markers, and `summary.json` replaces
the per-variant summaries. Build logs/binlogs, process logs, assertion counts and
result/exit agreement are verified by the shared runner.

The WPF cases remain in the existing `HandlerTests` project and sample.
Additional AppKit tab activation/reentrant mount-count regressions belong to the
upper tab scenario; this module retains the lower selection contract unchanged.
