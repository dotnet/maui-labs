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

Each artifact item includes source metadata such as `ReferenceName`, `ProjectPath`, `TargetFramework`, `TargetPlatformIdentifier`, `RuntimeIdentifier`, `Configuration`, and `ApplicationId`, plus the artifact contract described below.

For simple property-based consumers, `$(MauiAppArtifactPaths)` contains the resolved artifact paths separated by semicolons.

## Artifact contract

`@(MauiAppArtifact)` describes the output that was found; it does not select a host, device, deployment tool, or command line. The contract is intentionally structural so consumers can combine it with their own trust, provisioning, and capability checks.

| Metadata | Meaning |
| --- | --- |
| `ArtifactContractVersion` | Contract schema version. The current value is `1`. |
| `ArtifactRole` | `deployable` is a format intended for deployment, `distribution` is an archive or descriptor intended for handoff, `launcher` is a directly runnable desktop artifact, `supporting` is a runtime/support output, and `unknown` means the available facts are insufficient. |
| `TargetRuntimeKind` | Inferred from the target platform and RID: `android`, `ios`, `ios-simulator`, `ios-device`, `mac-catalyst`, `macos-appkit`, `windows`, or `unknown`. `ios-simulator` and `ios-device` require a recognizable RID. |
| `DeploymentModel` | Structural deployment shape: `package`, `store-bundle`, `physical-device-archive`, `bundle`, `apple-bundle`, `simulator-bundle`, `physical-device-bundle`, `desktop-bundle`, `descriptor`, `executable`, `library`, `directory`, or `unknown`. |
| `LaunchIdentityKind` | The identity scheme for `LaunchIdentity`: `android-package-name`, `apple-bundle-id`, `windows-package-identity`, `file-path`, or `none`. |
| `LaunchIdentity` | The known launch identifier. Android package and Apple bundle values come from `ApplicationId`; an executable uses its artifact path. Windows package artifacts remain empty because `ApplicationId` is not an AUMID or package identity. |
| `SigningState` | Whether the artifact is the signing output that installation needs: `signed`, `unsigned`, `unknown` (an Android package the SDK did not identify as either, such as a per-ABI package), or `not-applicable` (a format that is not produced as a signed/unsigned pair). Android is classified from the Android SDK's own `ApkFile`, `ApkFileSigned`, `_AabFile`, and `_AabFileSigned` properties, never from a file name suffix. |
| `Installable` / `Launchable` | Legacy compatibility values retained exactly for existing consumers. They predate the versioned artifact contract and are not conservative deployment decisions. New consumers must use `ArtifactRole`, `TargetRuntimeKind`, `DeploymentModel`, and launch identity instead. |

### Legacy compatibility values by artifact

The descriptive contract is conservative, but the two legacy booleans intentionally preserve their
pre-contract values to avoid silently changing existing MSBuild consumers.

| Artifact | Conservative contract classification | Legacy `Installable` / `Launchable` |
| --- | --- | --- |
| `.apk` | `deployable` / `package`; Android package identity | `true` / `true` |
| `.aab` | `distribution` / `store-bundle`; Android package identity | `true` / `false` |
| `.ipa` | `distribution` / `physical-device-archive`; Apple bundle identity | `true` / `false` |
| `.msix` | `deployable` / `package`; no inferred launch identity | `true` / `true` |
| `.appinstaller` | `distribution` / `descriptor`; no inferred launch identity | `false` / `false` — it describes distribution rather than an app payload. |
| `.app` for `ios` plus an `iossimulator*` RID | `deployable` / `simulator-bundle` | `true` / `true` |
| `.app` for `ios` plus an `ios-*` device RID | `deployable` / `physical-device-bundle` | `true` / `true` |
| `.app` for `maccatalyst` or `macos` | `launcher` / `desktop-bundle`; `macos` represents the AppKit target | `true` / `true` |
| `.app` without sufficient platform/RID facts | `unknown`; `bundle` or `apple-bundle` | `true` / `true` |

For example, legacy `Installable=true` on an AAB or IPA does not make that artifact directly
deployable, and legacy `Launchable=true` on an MSIX does not prove package trust. Execution tooling
must switch on the contract version, role, runtime kind, deployment model, artifact type, and
identity, then make environment-specific decisions without using the compatibility booleans.

The legacy booleans also do not distinguish signing: a debug Android build emits both
`<package>.apk` and `<package>-Signed.apk`, and both carry `Installable=true` / `Launchable=true`.
Consumers that install a package must select on `SigningState` — `adb install` requires the
`signed` one.

## Important defaults

- `MauiAppRefBuildOnBuild=true`: app artifacts are prepared during the host project build. `dotnet test` normally builds first, so artifact items are available to later build/test targets.
- `MauiAppRefSetPlatformOutputPaths=true`: platform output properties are set to deterministic locations under `MauiAppRefOutputRoot`.
- `MauiAppRefAndroidEmbedAssembliesIntoApk=true`: a reference that declares an Android `TargetFramework` is built with `EmbedAssembliesIntoApk=true`. Without it a Debug Android build produces a fast-deployment package that carries no managed assemblies, so installing that package on its own aborts at startup with `No assemblies found in '/data/user/0/<app>/files/.__override__/<abi>'`. Set the property to `false`, or pass an explicit `EmbedAssembliesIntoApk` through the reference's `Properties`/`AdditionalProperties` (any casing), to keep the SDK default. The reference-level value also overrides an app project that sets `<EmbedAssembliesIntoApk>false</EmbedAssembliesIntoApk>` itself, because it is passed as a global property to the child build; a global `-p:EmbedAssembliesIntoApk=false` on the *host* project is not an opt-out for the same reason.
- `MauiAppRefFailIfNoArtifacts=true`: declared app references must produce at least one artifact.
