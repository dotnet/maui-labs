# Microsoft.Maui.Build.AppProjectReference

`Microsoft.Maui.Build.AppProjectReference` lets a test, packaging, or tooling
project build a MAUI/.NET app project and consume its final application
artifacts as MSBuild items.

> [!WARNING]
> This package is experimental. Its API may change before a stable release.

## Basic usage

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Maui.Build.AppProjectReference"
                    Version="0.1.0-preview"
                    PrivateAssets="all" />

  <MauiAppProjectReference Include="..\MyApp\MyApp.csproj"
                           SetTargetFramework="TargetFramework=net11.0-android" />
</ItemGroup>
```

For a multi-targeted app, specify the app target framework. Add a runtime
identifier or child-build properties when required:

```xml
<MauiAppProjectReference Include="..\MyApp\MyApp.csproj"
                         SetTargetFramework="TargetFramework=net11.0-ios;RuntimeIdentifier=iossimulator-arm64"
                         AdditionalProperties="EnableCodeSigning=false" />
```

## .NET 11 and later

.NET 11 Android and Apple application SDKs expose final outputs through the
standard `GetApplicationArtifacts` target and `@(ApplicationArtifact)` items.
The package therefore expands the short form above into the equivalent standard
project reference:

```xml
<ProjectReference Include="..\MyApp\MyApp.csproj"
                  ReferenceOutputAssembly="false"
                  BuildReference="true"
                  PrivateAssets="all"
                  IncludeAssets="none"
                  SkipGetTargetFrameworkProperties="true"
                  Targets="GetApplicationArtifacts"
                  OutputItemType="MauiAppArtifact"
                  SetTargetFramework="TargetFramework=net11.0-android" />
```

This is intentionally a normal buildable project reference:

- `ReferenceOutputAssembly="false"` prevents the app assembly from becoming a
  compiler reference. `ReferenceOutput` is not a supported `ProjectReference`
  metadata name.
- `BuildReference="true"` is required. Setting it to `false` prevents MSBuild
  from invoking `GetApplicationArtifacts`.
- `Targets="GetApplicationArtifacts"` asks the app SDK to build and return its
  authoritative final artifacts.
- `OutputItemType="MauiAppArtifact"` routes those target outputs away from
  compiler references and into the package's compatible item name.
- `PrivateAssets="all"` prevents the app reference from flowing to downstream
  consumers, while `IncludeAssets="none"` prevents its NuGet assets from
  becoming assets of the host.
- `SkipGetTargetFrameworkProperties="true"` plus `SetTargetFramework` avoids
  compatibility negotiation between a plain test TFM and a platform app TFM.

The .NET 11 implementation does not inject targets into the app, override its
output paths, run a second child build, or scan output directories. This also
means it does not return stale artifacts from earlier builds. For example,
Apple `.ipa` output requires `BuildIpa=true` in the same invocation:

```xml
<MauiAppProjectReference Include="..\MyApp\MyApp.csproj"
                         SetTargetFramework="TargetFramework=net11.0-ios;RuntimeIdentifier=ios-arm64"
                         AdditionalProperties="BuildIpa=true" />
```

Android currently returns APK/AAB artifacts. Apple platforms return `.app`
bundles and, when enabled, `.ipa`, `.pkg`, and `.xcarchive` artifacts. A
referenced SDK must implement `GetApplicationArtifacts`; Windows application
SDKs do not currently provide this common contract.

Set `MauiAppRefUseProjectReferenceArtifacts=false` in the host project when a
.NET 11 host must reference an older app SDK or another SDK that does not
implement `GetApplicationArtifacts`.

Visual Studio builds use the legacy implementation because Visual Studio's
project build manager builds the default project-reference targets rather than
the `Targets` metadata used by command-line MSBuild. Command-line and static
graph builds use the .NET 11 implementation.

## .NET 10 and earlier

The package automatically imports its legacy implementation for hosts targeting
.NET 10 or earlier. That path builds the child project, uses deterministic
package-owned output directories, and discovers `.apk`, `.aab`, `.app`, `.ipa`,
`.msix`, `.appinstaller`, `.exe`, and `.dll` outputs.

Legacy-only metadata includes:

| Metadata | Purpose |
| --- | --- |
| `ExpectedArtifact` | Explicit artifact path when discovery should not infer it. |
| `ArtifactName` | Name used for deterministic platform outputs such as `.app` bundles. |
| `OutputRoot` | Per-reference output root. |
| `SetPlatformOutputPaths` | Set to `false` to avoid overriding platform output properties. |

## Reference metadata

| Metadata | Purpose |
| --- | --- |
| `SetTargetFramework` | .NET 11 target framework and optional RID, for example `TargetFramework=net11.0-ios;RuntimeIdentifier=iossimulator-arm64`. |
| `SetConfiguration` | Optional .NET 11 child configuration, for example `Configuration=Release`. |
| `SetPlatform` | Optional .NET 11 child platform. |
| `AdditionalProperties` | Additional .NET 11 child-build global properties. |
| `Targets` | Optional .NET 11 target override. Defaults to `GetApplicationArtifacts`. |
| `TargetFramework` | Legacy app target framework, for example `net10.0-android`. |
| `RuntimeIdentifier` | Legacy runtime identifier. |
| `Configuration` | Legacy child configuration. Defaults to the host configuration. |
| `BuildTarget` | Legacy child target. Defaults to `Build`. |
| `Properties` | Legacy semicolon-delimited child-build global properties. |
| `ReferenceName` | Legacy friendly artifact source name. |

The .NET 11 short form intentionally uses standard `ProjectReference` metadata
so graph construction and command-line builds observe the same configuration.
`OutputItemType` is fixed to `MauiAppArtifact` for the short form.

## Consuming artifacts

Artifacts are exposed as `@(MauiAppArtifact)` after
`BuildAppProjectReferences`:

```xml
<Target Name="UseMauiAppProjectReferences"
        AfterTargets="BuildAppProjectReferences">
  <Message Importance="High"
           Text="%(MauiAppArtifact.ReferenceName): %(MauiAppArtifact.Identity) [%(MauiAppArtifact.ArtifactType)]" />
</Target>
```

`$(MauiAppArtifacts)` contains item identities and
`$(MauiAppArtifactPaths)` contains full paths separated by semicolons.

On .NET 11, platform-owned metadata is preserved. Common values include
`PackageFormat`, `ApplicationId`, `ApplicationTitle`, `ApplicationName`,
`ApplicationDisplayVersion`, and `ApplicationVersion`. Android additionally
provides values such as `Signed`, `PackageId`, and `Abi`; Apple provides values
such as `BundleIdentifier`, `PlatformName`, and `IsDirectory`. The package maps
`PackageFormat` to its compatibility metadata `ArtifactType`, `Installable`,
and `Launchable`.

## Explicit project reference

On .NET 11, the expanded `ProjectReference` form shown above can be used
directly without a package-specific marker. Use `OutputItemType="MauiAppArtifact"`
to retain the package's consuming-item convention.

For .NET 10 and earlier, mark an explicit reference with
`MauiAppProjectReference="true"` and use the legacy metadata:

```xml
<ProjectReference Include="..\MyApp\MyApp.csproj"
                  ReferenceOutputAssembly="false"
                  BuildReference="false"
                  PrivateAssets="all"
                  MauiAppProjectReference="true"
                  TargetFramework="net10.0-android" />
```

## Authoritative references

- [MSBuild `ProjectReference` metadata](https://learn.microsoft.com/visualstudio/msbuild/common-msbuild-project-items#projectreference)
- [.NET for Android `GetApplicationArtifacts`](https://github.com/dotnet/android/blob/main/Documentation/docs-mobile/building-apps/build-targets.md#getapplicationartifacts)
- [.NET for Android `ApplicationArtifact`](https://github.com/dotnet/android/blob/main/Documentation/docs-mobile/building-apps/build-items.md#applicationartifact)
- [.NET for Apple `GetApplicationArtifacts`](https://github.com/dotnet/macios/blob/main/docs/building-apps/build-targets.md#getapplicationartifacts)
- [.NET for Apple `ApplicationArtifact`](https://github.com/dotnet/macios/blob/main/docs/building-apps/build-items.md#applicationartifact)
