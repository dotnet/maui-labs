# Microsoft.Maui.Build.AppProjectReference

`Microsoft.Maui.Build.AppProjectReference` lets test projects declare app projects as build-time dependencies and consume the built app artifacts as MSBuild items.

The recommended declaration is still a real `ProjectReference`, so tools that infer .NET project graphs from `.csproj` files continue to see the app/test dependency. The package removes marked references from the normal compile-time reference pipeline during the build and invokes them through its own MSBuild targets instead.

## Basic usage

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Maui.Build.AppProjectReference" Version="0.1.0-preview" PrivateAssets="all" />

  <ProjectReference Include="..\MyApp\MyApp.csproj"
                    ReferenceOutputAssembly="false"
                    BuildReference="false"
                    PrivateAssets="all"
                    MauiAppProjectReference="true"
                    TargetFramework="net10.0-android"
                    RuntimeIdentifier="android-arm64"
                    Properties="ApplicationId=com.example.myapp;AndroidPackageFormat=apk" />
</ItemGroup>
```

The test project build will:

1. Build the referenced app project with the supplied MSBuild properties.
2. Locate produced app artifacts such as `.apk`, `.aab`, `.app`, `.ipa`, `.msix`, `.appinstaller`, `.exe`, or `.dll`.
3. Expose the located artifacts as `@(MauiAppArtifact)` items with metadata.
4. Set `$(MauiAppArtifacts)` and `$(MauiAppArtifactPaths)` for simple target consumption.

iOS simulator/device builds commonly produce a `.app` bundle as the build artifact. `.ipa` files are also discovered when the child build is explicitly configured to produce one, such as for distribution packaging.

## OutputItemType compatibility (removed)

The package previously recognized `OutputItemType="MauiTestAppReference"` and a `MauiTestApp="true"` marker. Both of those legacy markers were removed in favor of the single `MauiAppProjectReference="true"` metadata flag shown above. A future version will introduce a dedicated `<MauiAppProjectReference>` item type for a one-line declaration form.

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
| `OutputRoot` | Per-reference output root. Defaults under `$(BaseIntermediateOutputPath)maui-app-refs`. |
| `SetPlatformOutputPaths` | Set to `false` to avoid overriding platform output properties. |

`Properties` and `AdditionalProperties` are forwarded before package-managed child build properties. If a duplicate key is also set from metadata or defaults, such as `Configuration` or `MauiAppRefOutputRoot`, the package-managed value is appended later and wins. Use the dedicated metadata above to change those values.

## Consuming built app artifacts

Downstream targets can consume `@(MauiAppArtifact)` after `BuildAppProjectReferences` runs:

```xml
<Target Name="UseMauiAppProjectReferences" AfterTargets="BuildAppProjectReferences">
  <Message Importance="High"
           Text="%(MauiAppArtifact.ReferenceName): %(MauiAppArtifact.Identity) [%(MauiAppArtifact.ArtifactType)]" />
</Target>
```

Each artifact item includes metadata such as `ReferenceName`, `ProjectPath`, `TargetFramework`, `TargetPlatformIdentifier`, `RuntimeIdentifier`, `Configuration`, `ApplicationId`, `ArtifactType`, `Installable`, and `Launchable`.

For simple property-based consumers, `$(MauiAppArtifactPaths)` contains the resolved artifact paths separated by semicolons.

## Important defaults

- `MauiAppRefBuildOnBuild=true`: app artifacts are prepared during the test project build. `dotnet test` normally builds first, so artifact items are available to later build/test targets.
- `MauiAppRefSetPlatformOutputPaths=true`: platform output properties are set to deterministic locations under `MauiAppRefOutputRoot`.
- `MauiAppRefFailIfNoArtifacts=true`: declared app references must produce at least one artifact.
