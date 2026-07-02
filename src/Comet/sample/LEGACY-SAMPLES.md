# Legacy samples (parked)

Phase 5 (2026-07-01) **deleted the legacy MAUI ViewHandler render path** from
Comet (`Handlers/**`, `Maui/**`, `AppHostBuilderExtensions`, the native
`CUI*`/`Comet*` platform views, and the `Shared/RuntimeDebug` debug host).
The Compose (Android) / SwiftUI (iOS) node backend is the only render path.

Every sample in this folder that calls `UseCometApp`/`UseCometHandlers` is
**parked**: it no longer compiles and is kept only as reference source. That is
all of them except the node-backend probes:

- `CometComposeProbe` — Android, Jetpack Compose backend (active)
- `CometSwiftUIProbe` — iOS, SwiftUI backend (active)
- `Shared/Jetchat` — the shared gold-standard sample both probes build (active)

To build or run a parked sample, check out the last commit with the legacy
path: tag **`comet-pre-phase5-delete`**. Reviving one for the node backend
means porting its app host to a backend root (see the probes' `MainActivity`/
`AppDelegate`) — the view/tree code itself is largely portable.
