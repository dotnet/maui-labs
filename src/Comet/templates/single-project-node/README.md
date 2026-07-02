# Comet node-backend app template

A `dotnet new` template for a single-project Comet app rendered natively by the
**node backend** — Jetpack Compose on Android, SwiftUI on iOS — with **no MAUI in
the render path**. One shared view tree (`App.cs`) is hosted by a thin native head
per platform (`Platforms/Android/MainActivity.cs`, `Platforms/iOS/AppDelegate.cs`).

This is the node-backend successor to the legacy `single-project/` template (which
scaffolded the deleted `UseCometApp` MAUI-host model — see `LEGACY-TEMPLATE.md`).

## Reference model (pre-packaging)

Comet's node backend is **not published as a NuGet package yet**, so the generated
app **ProjectReferences Comet in this repo** and links the SwiftUI shim framework.
The `CometRoot` MSBuild property points at the repo's `src/Comet` directory; its
default (`..\..`) resolves when the app is created **under `src/Comet/sample/<name>/`**
(the same depth as the `CometComposeProbe`/`CometSwiftUIProbe` samples). Override it
for any other location:

```
dotnet build -p:CometRoot=/abs/path/to/src/Comet
```

Once Comet ships as a package, swap the `ProjectReference` + `NativeReference` in the
generated `.csproj` for the corresponding `PackageReference`s.

## Prerequisites

- The SwiftUI shim xcframework must be built once (and after any shim change) before
  the iOS head links:

  ```
  ./src/Comet.SwiftUI.Shim/build-xcframework.sh
  ```

## Use it

```
# from repo root
dotnet new install src/Comet/templates/single-project-node

# scaffold under sample/ so the default CometRoot resolves
dotnet new cometnode -n MyApp -o src/Comet/sample/MyApp

# Android
dotnet build src/Comet/sample/MyApp/MyApp.csproj -f net11.0-android -t:Run

# iOS (simulator)
dotnet build src/Comet/sample/MyApp/MyApp.csproj -f net11.0-ios \
  -c Debug -p:RuntimeIdentifier=iossimulator-arm64
```

Options: `--applicationId <id>` (app id / bundle id), `--cometRoot <path>` (Comet
`src/Comet` location).
