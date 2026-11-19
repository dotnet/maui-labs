# Comet node-backend app template

A `dotnet new` template for a single-project Comet app rendered natively by the
**node backend** — Jetpack Compose on Android, SwiftUI on iOS — with **no MAUI in
the render path**. One shared view tree (`App.cs`) is hosted by a thin native head
per platform (`Platforms/Android/MainActivity.cs`, `Platforms/iOS/AppDelegate.cs`).

This is the node-backend successor to the legacy `single-project/` template (which
scaffolded the deleted `UseCometApp` MAUI-host model — see `LEGACY-TEMPLATE.md`).

## Reference model — the Comet package (local feed)

Comet's node backend is published to a **local NuGet feed** (not yet on nuget.org).
The generated app references the `Comet` package, which pulls in everything else
transitively:

- `Comet.Layout.Yoga` (the C# flexbox engine)
- `Microsoft.AndroidX.Compose` (the Jetpack Compose facade) on Android, plus its
  ~35 Xamarin.AndroidX.Compose.* dependencies
- `Comet.SwiftUI.Binding` + the `CometSwiftUIShim.xcframework` on iOS, and a
  `build/Comet.targets` that adds the `SmartLink=False` `NativeReference` the iOS
  static registrar needs (so `_OBJC_CLASS_$_CometSwiftUIHost` resolves at link time)

The generated `nuget.config` points at the local feed. Edit it if your
`Comet.*.nupkg` files live elsewhere. Produce/refresh the packages with:

```
# Comet.Layout.Yoga, the Compose facade, the SwiftUI binding
dotnet pack src/Comet/src/Comet.Layout.Yoga/Comet.Layout.Yoga.csproj                 -c Release -p:Version=0.5.1-local -p:IsPackable=true -o ~/work/LocalNugets
dotnet pack src/Comet/src/vendor/Microsoft.AndroidX.Compose/Microsoft.AndroidX.Compose.csproj -c Release -p:Version=0.5.1-local -p:IsPackable=true -o ~/work/LocalNugets
dotnet pack src/Comet/src/Comet.SwiftUI.Binding/Comet.SwiftUI.Binding.csproj         -c Release -p:Version=0.5.1-local -p:IsPackable=true -o ~/work/LocalNugets
# Comet itself (depends on the three above; ships the iOS shim + build targets)
dotnet pack src/Comet/src/Comet/Comet.csproj                                          -c Release -p:Version=0.5.1-local -o ~/work/LocalNugets
```

The SwiftUI shim xcframework must be built once (and after any shim change) before
packing Comet:

```
./src/Comet/src/Comet.SwiftUI.Shim/build-xcframework.sh
```

## Use it

```
# from repo root
dotnet new install src/Comet/templates/single-project-node

dotnet new cometnode -n MyApp
cd MyApp

# Android
dotnet build MyApp.csproj -f net11.0-android -t:Run

# iOS (simulator)
dotnet build MyApp.csproj -f net11.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-arm64
```

Options: `--applicationId <id>` (app id / bundle id),
`--cometPackageVersion <version>` (which Comet package version to reference).

## Developing against Comet source instead of the package

For live-source iteration inside the maui-labs repo, replace the
`<PackageReference Include="Comet" .../>` with a `ProjectReference` to
`src/Comet/src/Comet/Comet.csproj`, and add a `<NativeReference>` to
`src/Comet/src/Comet.SwiftUI.Shim/CometSwiftUIShim.xcframework`
(`Kind=Framework`, `SmartLink=False`) for the iOS head — that is the setup the
`CometComposeProbe` / `CometSwiftUIProbe` samples use.
