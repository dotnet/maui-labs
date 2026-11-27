# Optional live video Preview transport spike

## Recommendation

**Defer production video.** Do not add media capabilities, protocol routes, codecs, or native
capture backends yet. The optimized PNG path is implemented, but no representative device
measurement shows that video improves interaction enough to justify the platform, codec, security,
and lifecycle cost.

PNG plus the immutable semantic frame remains authoritative for tree bounds, overlays, selection,
hit testing, properties, assertions, actions, automation, and Copilot context. Any future video is
presentation-only.

## Evidence

The #733 investigation found that a healthy Android screenshot request took 0.16-0.37 seconds,
while an artificially starved emulator took 1.11-4.67 seconds. The PNG was about 53 KB and the
local proxy connected in 0-21 milliseconds. On that machine, Agent-side capture dominated the wire
cost.

The prerequisites now exist on `main`:

- #804 defines the shared `streamId`, `frameId`, `frameRevision`, frame descriptor, supersession,
  and coordinate contracts.
- #806 centralizes Preview transport, capture, fallback, and health presentation.
- #809 negotiates bounded screenshot scale/pixel budgets and reports actual capture dimensions.

The remaining evidence gap is a real-device comparison between #809 and live capture/encoding.
Encoding the same expensive capture as video does not by itself remove UI-thread capture cost.

## Architecture boundary

```text
semantic frame + decoded exact PNG
  authoritative for inspection and actions

optional live media
  best-effort passive presentation only
```

`frameId` stays the sole cache identity. Video must not introduce another frame cache or
supersession rule. Matching `streamId` and `frameRevision` is necessary for presentation, but it
does not prove that later animated pixels still align with the semantic tree.

Required behavior for any future prototype:

1. Show media only after the referenced state and exact PNG have loaded successfully.
2. Stop media before hover, selection, inspection, pointer interaction, or actions are enabled.
3. Keep the PNG visible and ready for immediate fallback; media never supplies geometry or input.
4. Freeze to PNG on revision, window, size, offset, rotation, reconnect, decoder, or stall changes.
5. Stop capture/encode/decode while the browser, VS Code panel, or Canvas is hidden.
6. Route negotiation, stall, and fallback state through #806 instead of a separate status UI.

## Transport comparison

| Option | Finding |
|---|---|
| Binary WebSocket | Best candidate for a future local prototype because it fits the existing same-origin broker path and can carry explicit frame correlation. It would still need bounded latest-wins buffering, send deadlines, and keyframe recovery. |
| WebRTC | Congestion control is useful over hostile networks, but ICE/DTLS/RTP and native media-stack selection are not justified for the current loopback/forwarded Inspector. It was not evaluated deeply enough to select a redistribution-safe .NET stack. |
| Multipart/MJPEG | Broad image support, but weak per-frame semantic correlation and high intra-frame bandwidth make it a poor Inspector fit. |
| Rapid PNG/JPEG/WebP stills | Independent frames are easy to drop and recover. #809 makes this the baseline that video must beat. A lossy presentation format could be evaluated without changing semantic authority. |
| Platform-native streams | A capture-provider choice, not a transport. Permissions and available APIs differ substantially by platform. |

No transport is approved by this spike.

## Platform API matrix

The table records API constraints, not measured feasibility.

| Target | Candidate source | Evidence status |
|---|---|---|
| Android emulator/device | `PixelCopy` or app-surface capture plus `MediaCodec`; `MediaProjection` requires user consent and a foreground service on current Android. | No DevFlow video measurement. |
| iOS / Mac Catalyst | ReplayKit supplies continuous sample buffers; ScreenCaptureKit is available on newer Catalyst versions. | No DevFlow video measurement; consent and lifecycle behavior unverified. |
| macOS AppKit | `SCStream` filtered to the app window plus VideoToolbox. | No DevFlow video measurement; screen-capture permission behavior requires validation. |
| Windows / WPF | Windows.Graphics.Capture frame pools plus Media Foundation. | No DevFlow video measurement; picker/border, Windows N, and interop behavior unverified. |
| GTK/Linux | PipeWire ScreenCast portal plus an installed codec stack. | No DevFlow video measurement; portal consent and distribution-specific codecs are unresolved. |
| Native agents | Same platform APIs as above, implemented without MAUI. | No shared provider contract or measurement. |

Official references:

- [Android MediaProjection](https://developer.android.com/media/grow/media-projection)
- [Android MediaCodec](https://developer.android.com/reference/android/media/MediaCodec)
- [ReplayKit `startCapture`](https://developer.apple.com/documentation/replaykit/rpscreenrecorder/startcapture(handler:completionhandler:).md)
- [ScreenCaptureKit](https://developer.apple.com/documentation/screencapturekit.md)
- [Windows.Graphics.Capture](https://learn.microsoft.com/windows/apps/develop/media-authoring-processing/screen-capture)
- [XDG ScreenCast portal](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.ScreenCast.html)

## Browser capability probe

WebCodecs is a decoder/encoder API, not a transport or demuxer. Support depends on the exact
browser build and codec string, so production code would need runtime probing and PNG fallback.

The checked-in loopback probe produced these results on a Windows 11 VM with GPU disabled:

| Runtime | H.264 WebCodecs decode | VP8/VP9/AV1 decode | H.264 MSE / WebRTC receive |
|---|---:|---:|---:|
| Playwright Chromium 136 | No | Yes | No |
| Chrome 156 | Yes | Yes | Yes |

Redacted outputs are stored beside the probe:

- [`evidence/playwright-chromium-136-windows-headless.json`](../../src/DevFlow/js/prototypes/inspector-video-preview/evidence/playwright-chromium-136-windows-headless.json)
- [`evidence/chrome-156-windows-headless.json`](../../src/DevFlow/js/prototypes/inspector-video-preview/evidence/chrome-156-windows-headless.json)

This rules out an H.264-only design and a CI suite that requires H.264. VS Code port mapping,
Remote/WSL/Codespaces, Copilot Canvas, and actual host visibility teardown remain unverified.

Codec availability is not redistribution clearance. The
[WebCodecs registry](https://www.w3.org/TR/webcodecs-codec-registry/) makes no intellectual-property
determination. Any codec or native media dependency requires separate legal and redistribution
review; the safe starting posture is to use operating-system codecs and retain PNG fallback.

## Security and resource requirements

Any future media path must:

- remain loopback-only and enforce the Inspector's origin/host policy;
- bind a short-lived stream to the selected agent and Inspector instance;
- bound encoded units, pending sends, decoder work, viewers, and session lifetime;
- drop stale work instead of accumulating latency, then recover from a keyframe;
- stop on hidden host, socket close, Inspector disposal, agent replacement, or app disconnect;
- capture app-owned content only, never silently expanding to system UI or other windows;
- avoid recording, persistence, telemetry, temporary frame files, and automatic Copilot attachment;
- use #806 for visible degraded/fallback state.

The existing sparse text-event relay should not be made into a media path by increasing its limits.

## Measurement needed before reconsidering

Measure capture, encode, transport, browser decode, commit-to-paint, and app UI-thread impact
separately. Compare:

1. current PNG;
2. #809 at the rendered resolution/DPR;
3. a latest-wins still-image loop with unchanged-frame suppression;
4. video at the same resolution and frame rate.

Use static, tap, scroll, and animation workloads on representative Android, Apple, and Windows
targets. Report p50/p95 with confidence intervals, CPU/GPU, memory, bandwidth, queue high-water
marks, and visible recovery after drops, stalls, rotation, resize, reconnect, and hide/show.

Proceed to production design only if video materially improves p95 interaction latency or
UI-thread cost over #809, works with operating-system codecs on at least two platform families,
and stops expensive work reliably in browser, VS Code, and Canvas. Reject video if optimized
stills meet the interaction target or capture remains the dominant cost. Otherwise continue to
defer.

## Outcome

The spike provides the architecture comparison, platform/API matrix, security/resource boundary,
runtime browser evidence, synchronization rules, measurement plan, and explicit recommendation.
It intentionally does not implement a production media protocol or platform encoder.

`Redth/mobile-canvas-ghcp` was used only as design evidence that simulator video can be responsive;
its host-side capture and macOS encoding choices do not establish a portable DevFlow design.
