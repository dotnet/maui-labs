# MAUI Mobile Device Canvas

> **Experimental and optional.** This canvas drives a separately installed, pinned companion
> binary. It is not required by any other DevFlow surface, and it has no approval authority: VS Code
> remains the only trusted native approval host.

A standalone GitHub Copilot canvas for local iOS simulators and Android emulators. Unlike the
MAUI DevFlow Inspector canvas, it does not require a running MAUI app or DevFlow agent. The pinned
Mobile Canvas companion owns device discovery, lifecycle, input, video, screenshots, recording,
and its authenticated browser UI.

## Prepare the companion

```powershell
maui devflow devices host install
maui devflow devices host start
```

`host status` never downloads or starts anything. The canvas resolves only the content-addressed
runtime installed from the manifest pinned in this repository; it never falls back to `PATH`, a
global tool, an environment override, or a model-provided executable.

## Develop

```powershell
npm ci
npm test
```

The canvas ID is `maui-mobile-device`. It remains useful when no app is running. Once a
DevFlow-enabled app is attached, `maui-live-canvas` exposes the same capability-gated device
management in **Data → Device**, alongside semantic app inspection and workflow authoring.

Erase and delete remain human-confirmed controls inside the native canvas UI. Their agent actions
do not treat a model-supplied boolean or chat response as authorization.

Agent-callable device mutations claim the DevFlow broker's stable device lease before invoking the
companion, heartbeat while the command runs, and release after its terminal result. A missing
broker blocks the mutation rather than silently bypassing coordination.
