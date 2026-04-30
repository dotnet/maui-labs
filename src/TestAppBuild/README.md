# Microsoft.Maui.TestApp.Build

`Microsoft.Maui.TestApp.Build` lets test projects declare app projects as build-time dependencies and consume the built app artifacts as MSBuild items.

The recommended declaration is still a real `ProjectReference`, so tools that infer .NET project graphs from `.csproj` files continue to see the app/test dependency. The package removes marked references from the normal compile-time reference pipeline during the build and invokes them through its own MSBuild targets instead.

## Basic usage

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Maui.TestApp.Build" Version="0.1.0-preview" PrivateAssets="all" />

  <ProjectReference Include="..\MyApp\MyApp.csproj"
                    ReferenceOutputAssembly="false"
                    BuildReference="false"
                    PrivateAssets="all"
                    MauiTestApp="true"
                    TargetFramework="net10.0-android"
                    RuntimeIdentifier="android-arm64"
                    Properties="ApplicationId=com.example.myapp;AndroidPackageFormat=apk" />
</ItemGroup>
```

The test project build will:

1. Build the referenced app project with the supplied MSBuild properties.
2. Locate produced app artifacts such as `.apk`, `.aab`, `.app`, `.ipa`, `.msix`, `.appinstaller`, `.exe`, or `.dll`.
3. Expose the located artifacts as `@(MauiTestAppArtifact)` items with metadata.
4. Set `$(MauiTestAppArtifacts)` and `$(MauiTestAppArtifactPaths)` for simple target consumption.

iOS simulator/device builds commonly produce a `.app` bundle as the build artifact. `.ipa` files are also discovered when the child build is explicitly configured to produce one, such as for distribution packaging.

## OutputItemType compatibility

Projects that prefer the `OutputItemType` idiom can use:

```xml
<ProjectReference Include="..\MyApp\MyApp.csproj"
                  ReferenceOutputAssembly="false"
                  BuildReference="false"
                  PrivateAssets="all"
                  OutputItemType="MauiTestAppReference"
                  TargetFramework="net10.0-ios"
                  RuntimeIdentifier="iossimulator-arm64"
                  Properties="EnableCodeSigning=false" />
```

## Key metadata

| Metadata | Purpose |
| --- | --- |
| `TargetFramework` | Target framework to build in the app project, for example `net10.0-android`. |
| `RuntimeIdentifier` | Optional runtime identifier, for example `iossimulator-arm64`. |
| `Configuration` | Child build configuration. Defaults to the test project configuration. |
| `BuildTarget` | Child target to run before artifact discovery. Defaults to `Build`. |
| `Properties` | Semicolon-delimited extra child MSBuild properties. |
| `ExpectedArtifact` | Explicit artifact path when discovery should not infer output files. |
| `ArtifactName` | Name used for deterministic platform outputs such as `.app` bundles. |
| `OutputRoot` | Per-reference output root. Defaults under `$(BaseIntermediateOutputPath)maui-test-apps`. |
| `SetPlatformOutputPaths` | Set to `false` to avoid overriding platform output properties. |

`Properties` and `AdditionalProperties` are forwarded before package-managed child build properties. If a duplicate key is also set from metadata or defaults, such as `Configuration` or `MauiTestAppOutputRoot`, the package-managed value is appended later and wins. Use the dedicated metadata above to change those values.

## Consuming built app artifacts

Downstream targets can consume `@(MauiTestAppArtifact)` after `BuildMauiTestApps` runs:

```xml
<Target Name="UseMauiTestApps" AfterTargets="BuildMauiTestApps">
  <Message Importance="High"
           Text="%(MauiTestAppArtifact.ReferenceName): %(MauiTestAppArtifact.Identity) [%(MauiTestAppArtifact.ArtifactType)]" />
</Target>
```

Each artifact item includes metadata such as `ReferenceName`, `ProjectPath`, `TargetFramework`, `TargetPlatformIdentifier`, `RuntimeIdentifier`, `Configuration`, `ApplicationId`, `ArtifactType`, `Installable`, and `Launchable`.

For simple property-based consumers, `$(MauiTestAppArtifactPaths)` contains the resolved artifact paths separated by semicolons.

## Important defaults

- `MauiTestAppBuildOnBuild=true`: app artifacts are prepared during the test project build. `dotnet test` normally builds first, so artifact items are available to later build/test targets.
- `MauiTestAppSetPlatformOutputPaths=true`: platform output properties are set to deterministic locations under `MauiTestAppOutputRoot`.
- `MauiTestAppFailIfNoArtifacts=true`: declared app references must produce at least one artifact.
