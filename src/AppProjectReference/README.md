# Microsoft.Maui.Build.AppProjectReference

`Microsoft.Maui.Build.AppProjectReference` lets a consuming project (a test project, packaging project, etc.) declare a MAUI/.NET app project as a build-time dependency and consume the resulting platform artifacts (`.apk`, `.app`, `.ipa`, `.msix`, `.appinstaller`, `.exe`, `.dll`) as MSBuild items.

The package projects each `<MauiAppProjectReference>` item into a real `<ProjectReference>` once its build assets are imported, so project-graph builds, IDE solution explorer, and external project-graph analyzers (e.g. `@nx/dotnet`) see a real project edge while the reference-stripping plumbing is applied automatically. The app project is also restored before the package invokes its child build, so clean builds do not require a separate restore of the app project.

## Basic usage (recommended)

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Maui.Build.AppProjectReference" Version="0.1.0-preview" PrivateAssets="all" />

  <MauiAppProjectReference Include="..\MyApp\MyApp.csproj" />
</ItemGroup>
```

That single line is all you need for the common case. Supply a `TargetFramework` for multi-TFM apps:

```xml
<MauiAppProjectReference Include="..\MyApp\MyApp.csproj"
                         TargetFramework="net10.0-android"
                         RuntimeIdentifier="android-arm64" />
```

Pass arbitrary properties through to the child build:

```xml
<MauiAppProjectReference Include="..\MyApp\MyApp.csproj"
                         TargetFramework="net10.0-ios"
                         RuntimeIdentifier="iossimulator-arm64"
                         Properties="EnableCodeSigning=false;ApplicationId=com.example.app" />
```

The host project build will:

1. Build the referenced app project with the supplied MSBuild properties.
2. Locate produced app artifacts such as `.apk`, `.aab`, `.app`, `.ipa`, `.msix`, `.appinstaller`, `.exe`, or `.dll`.
3. Expose the located artifacts as `@(MauiAppArtifact)` items with metadata.
4. Set `$(MauiAppArtifacts)` and `$(MauiAppArtifactPaths)` for simple target consumption.

## Implicit defaults

Each `<MauiAppProjectReference>` is projected into a `<ProjectReference>` with these defaults. Any user-supplied value on the source item wins.

| Metadata | Default | Why |
| --- | --- | --- |
| `ReferenceOutputAssembly` | `false` | The host project should not consume the app's compile-time output. |
| `BuildReference` | `false` | The package invokes the child build itself; we do not want the implicit dependent build. |
| `PrivateAssets` | `all` | Avoid leaking the reference into transitive consumers. |
| `SkipGetTargetFrameworkProperties` | `true` | Avoid TFM negotiation between host and app. |
| `IncludeAssets` | `none` | Belt-and-suspenders to keep the app's outputs out of the host's compile/runtime sets. |
| `MauiAppProjectReference` (marker) | `true` | Identifies the projected reference for our resolve target. |

## Explicit `<ProjectReference>` form (escape hatch)

If you already maintain ProjectReference declarations (or generate projects programmatically), you can mark a vanilla `<ProjectReference>` with `MauiAppProjectReference="true"`. You own the metadata on that item; the package does not apply implicit defaults to it.

```xml
<ProjectReference Include="..\MyApp\MyApp.csproj"
                  ReferenceOutputAssembly="false"
                  BuildReference="false"
                  PrivateAssets="all"
                  MauiAppProjectReference="true"
                  TargetFramework="net10.0-android"
                  RuntimeIdentifier="android-arm64"
                  Properties="ApplicationId=com.example.myapp;AndroidPackageFormat=apk" />
```

## Key metadata

| Metadata | Purpose |
| --- | --- |
| `TargetFramework` | Target framework to build in the app project, for example `net10.0-android`. |
| `RuntimeIdentifier` | Optional runtime identifier, for example `iossimulator-arm64`. |
| `Configuration` | Child build configuration. Defaults to the host project configuration. |
| `BuildTarget` | Child target to run before artifact discovery. Defaults to `Build`. |
| `Properties` | Semicolon-delimited extra child MSBuild properties. |
| `ExpectedArtifact` | Explicit artifact path when discovery should not infer output files. |
| `ArtifactName` | Name used for deterministic platform outputs such as `.app` bundles. |
| `OutputRoot` | Per-reference output root. Defaults under `$(BaseIntermediateOutputPath)maui-app-refs`. |
| `SetPlatformOutputPaths` | Set to `false` to avoid overriding platform output properties. |
| `ReferenceName` | Friendly name on `@(MauiAppArtifact)` items. Defaults to the project filename. |

`Properties` and `AdditionalProperties` are forwarded before package-managed child build properties. If a duplicate key is also set from metadata or defaults (e.g. `Configuration` or `MauiAppRefOutputRoot`), the package-managed value is appended later and wins. Use the dedicated metadata above to change those values.

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

- `MauiAppRefBuildOnBuild=true`: app artifacts are prepared during the host project build. `dotnet test` normally builds first, so artifact items are available to later build/test targets.
- `MauiAppRefSetPlatformOutputPaths=true`: platform output properties are set to deterministic locations under `MauiAppRefOutputRoot`.
- `MauiAppRefFailIfNoArtifacts=true`: declared app references must produce at least one artifact.
