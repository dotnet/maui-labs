# Shell tabs and native page mounting

This module uses the shared real MAUI application and AppKit bootstrap, not a
separate executable. It registers one case with exactly 63 native assertions.
The case completes only after the last queued rebind/disconnect assertion.
Portable `MacOS.Tests` and the separate-process `shell-sections` scenario remain
unchanged.

The generic runner overlays only `ShellHandler.cs` and `ShellHandler.Tabs.cs`
from `b0767ac6d972e2aaa5168339632822604c02a1f0`. The fixture and all other sources
remain identical. This is a two-file historical production overlay, not a checkout
of the complete old revision. Its exact expected failure is
`shell-tabs.single-click-attachment`, with three attachments to the logical page
for one native click. The fixed source must attach once and finish all 63 assertions.
Build errors, timeouts, another count, or another assertion are not accepted as
the baseline. The original missing-tabs/exit-42 evidence against `9206e7c` remains
historical and is not the baseline used by this migrated scenario.

Counts accumulate on the logical page across handler/view recreation. Coverage
includes fresh/cached clicks, retained handler/view reuse, layout/title/toolbar
refresh, detached remount, replacement handler, changed context, and disposed-view
fallback only after the native handle reaches zero on the UI queue. A newer
destination must already be rendered inside a lazy factory before its abandoned
outer render returns; the abandoned page must have zero attachments.

Both launches preserve screenshots, assertion logs, `click-refresh.json`, the raw
count and attachment stack traces. Fixed evidence additionally records disposal
and reentrant state, sidebar presentation, selection changes, and handler rebind.
The host's `result.json` is authoritative; a legacy `passed.txt` alone cannot pass.

Wide/narrow checks explicitly choose Locked at 1000 DIP and Disabled at 480 DIP.
They do not establish an automatic application resize policy. Native sidebar
title, SF-symbol image and geometry checks are not sidebar pixel proof:
`CacheDisplay` omits the vibrancy/sidebar region.
