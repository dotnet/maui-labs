# Comet templates

## Use the node-backend template

**`single-project-node/`** (shortName **`cometnode`**) is the current template — a
node-backend single-project app (Jetpack Compose on Android, SwiftUI on iOS, no
MAUI in the render path). See `single-project-node/README.md`.

## `single-project/` is parked (legacy)

The old `single-project` template scaffolds the **legacy MAUI-hosted Comet app**
model (`public class App : CometApp` + `builder.UseCometApp<App>()`). Phase 5
(2026-07-01) **deleted that render path** — `CometApp`, `UseCometApp`, the MAUI
ViewHandlers, and the native platform views are gone (see
`../sample/LEGACY-SAMPLES.md`). So `dotnet new` from the legacy template produces a
project that does **not** compile against current Comet; check out tag
**`comet-pre-phase5-delete`** to use it. Do not publish the legacy pack as-is —
use `single-project-node/` instead.
