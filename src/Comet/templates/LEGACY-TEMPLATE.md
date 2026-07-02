# Comet templates (parked)

The `single-project` template here scaffolds the **legacy MAUI-hosted Comet app**
model (`public class App : CometApp` + `builder.UseCometApp<App>()`). Phase 5
(2026-07-01) **deleted that render path** — `CometApp`, `UseCometApp`, the MAUI
ViewHandlers, and the native platform views are gone (see
`../sample/LEGACY-SAMPLES.md`). So `dotnet new` from this template produces a
project that does **not** compile against current Comet.

Until a node-backend single-project template exists, this template targets the
pre-Phase-5 tree: check out tag **`comet-pre-phase5-delete`** to use it.

A node-backend app is a plain platform head (Android `ComponentActivity`, iOS
`AppDelegate`) that hosts a `ComposeBackendRoot` / `SwiftUIBackendRoot` — see
`../sample/CometComposeProbe/MainActivity.cs` and
`../sample/CometSwiftUIProbe/AppDelegate.cs` for the shape a future template
should scaffold. Do not publish this template pack as-is.
