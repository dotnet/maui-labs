# Microsoft.Maui.TestApp.Build

`Microsoft.Maui.TestApp.Build` lets test projects declare app projects as build-time dependencies and receive a manifest describing the built app artifacts.

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
2. Locate produced app artifacts such as `.apk`, `.aab`, `.app`, `.ipa`, `.msix`, `.exe`, or `.dll`.
3. Write `maui-test-apps.json` to the test output directory.
4. Generate an internal `MauiTestApps` class with a `ManifestPath` constant.

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

## Manifest

The generated manifest has this shape:

```json
{
  "version": 1,
  "apps": [
    {
      "name": "MyApp",
      "path": "/absolute/path/to/app.apk",
      "projectPath": "/absolute/path/to/MyApp.csproj",
      "targetFramework": "net10.0-android",
      "targetPlatformIdentifier": "android",
      "runtimeIdentifier": "android-arm64",
      "configuration": "Debug",
      "applicationId": "com.example.myapp",
      "bundleIdentifier": "com.example.myapp",
      "packageName": "com.example.myapp",
      "artifactType": "apk",
      "installable": true,
      "launchable": true
    }
  ]
}
```

## Important defaults

- `MauiTestAppBuildOnBuild=true`: app artifacts are prepared during the test project build. `dotnet test` normally builds first, so the manifest is available to tests.
- `MauiTestAppSetPlatformOutputPaths=true`: platform output properties are set to deterministic locations under `MauiTestAppOutputRoot`.
- `MauiTestAppFailIfNoArtifacts=true`: declared app references must produce at least one artifact.
- `MauiTestAppGenerateSource=true`: the test project receives an internal generated source file with the manifest path.
