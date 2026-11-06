# Inspector video Preview capability probe

This non-shipping probe records which browser media APIs and codecs are available for DevFlow
issue #812. It does not capture, transmit, decode, record, or persist app pixels.

`capability-probe.mjs` checks WebCodecs decoding, MediaSource types, WebRTC receiver codecs,
WebSocket, and WebSocketStream. Support is detected at runtime rather than inferred from the
browser product name.

Run the local probe from the repository root:

```powershell
node src\DevFlow\js\prototypes\inspector-video-preview\serve.mjs
```

Open the printed loopback URL in the browser being evaluated. The checked-in `evidence` directory
contains redacted outputs for Chrome 156 and the repository's Playwright Chromium 136. The probe
does not reproduce VS Code port mapping, Remote/WSL/Codespaces, Canvas lifecycle, or host
visibility signals.

Run the deterministic probe tests with the existing JavaScript host suite:

```powershell
Set-Location src\DevFlow\js
npm test
```

The production decision and platform analysis are documented in
[`docs/DevFlow/inspector-video-preview-spike.md`](../../../../../docs/DevFlow/inspector-video-preview-spike.md).
