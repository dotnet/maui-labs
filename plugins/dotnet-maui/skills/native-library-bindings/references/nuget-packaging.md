# NuGet Packaging for Native Bindings

Package bindings only after the binding works from a sample app and redistribution rights are clear.

## Package models

| Model | Use when | Tradeoff |
|-------|----------|----------|
| App-local project reference | Internal app integration | Fastest, not reusable. |
| One binding package per native artifact | Public or reusable packages | Best dependency graph, more packages. |
| Aggregated private binding package | Internal distribution | Simpler consumption, higher duplicate/conflict risk. |
| Wrapper package plus platform packages | Multi-platform public SDK | Cleaner TFMs, more packaging work. |

For public packages, prefer one native library per package so NuGet dependencies model native dependencies accurately.

### Feature packages, a shared core, and a meta-package

For a large SDK split across many features, a proven layout (the Plugin.Firebase model) is:

- A shared `Core` package with the common abstractions/helpers, referenced by every feature package.
- One package per feature (Auth, Storage, Messaging, ...), each depending on `Core` and on only the platform binding NuGets that feature needs, under TFM conditions.
- A meta/"bundled" package (e.g. `Contoso.Firebase`) that has no code of its own and simply `ProjectReference`s/depends on every feature package, so consumers who want everything install one package.

Version feature packages independently — a bug fix in Storage should not force a version bump of Auth. The meta-package pins the feature versions it aggregates.

### Mirror a modular native SDK as per-module packages

Large modular SDKs (Stripe, AndroidX, Jetpack Compose) ship as many native modules with a dependency graph among them. The scalable pattern is one binding project/package per native module, wiring `ProjectReference`s (local) or `PackageReference`s (published) so the managed graph mirrors the native module graph exactly — for example `StripePaymentsUI` -> `StripePayments` -> `StripeCore`, or `compose.material` -> `compose.material-ripple`.

- Give each package `PackageTags` including its native `group:artifact` so the correlation is discoverable.
- Some graph nodes need no binding of their own (they are pure transitive dependencies fulfilled by another package or resolved at build time). Model those as a plain dependency, not as another generated binding project.
- Do not collapse a modular SDK into one giant binding just to reduce package count — that reintroduces the version-conflict and duplicate-type problems the split avoids.

For very large graphs this is tedious by hand. Community tooling (`Binderators.Gradle`/`Dependencies.Gradle`) auto-generates one binding csproj + `.targets` per resolved artifact from a Gradle-resolved tree; `MetadataFetcher` maps native `group:artifact` coordinates to existing C# binding NuGet ids. Know they exist before hand-authoring dozens of projects.

### Let consumers toggle coupled versions and build behavior

Binding NuGets frequently ship MSBuild props/targets that consumers toggle (for example an iOS Firebase binding exposes `FirebaseCrashlyticsUploadSymbolsEnabled` to control dSYM upload). When your package pulls transitive binding NuGets whose versions are tightly coupled (see `android-bindings.md`), expose those versions as MSBuild properties with defaults so consumers can float them without editing your package.

## Package metadata

Include:

```xml
<PackageId>Contoso.NativeSdk.Bindings</PackageId>
<Version>1.2.3</Version>
<Authors>Contoso</Authors>
<Description>.NET MAUI bindings for Contoso Native SDK.</Description>
<PackageTags>maui;ios;android;binding;native;artifact=com.contoso:sdk:1.2.3</PackageTags>
<RepositoryUrl>https://github.com/contoso/contoso-dotnet-bindings</RepositoryUrl>
```

Add license metadata and notice files according to the native SDK's license.

## Apple packaging

Binding package should include:

- Managed binding assembly for each Apple TFM.
- Native `.xcframework`/framework/static library assets.
- Build assets that add `NativeReference` for consumers when needed.

Typical package target snippet:

```xml
<ItemGroup Condition="'$(TargetFramework)' == 'net10.0-ios'">
  <NativeReference Include="$(MSBuildThisFileDirectory)..\native\MySdk.xcframework">
    <Kind>Framework</Kind>
    <ForceLoad>true</ForceLoad>
    <SmartLink>true</SmartLink>
  </NativeReference>
</ItemGroup>
```

Use `buildTransitive` for assets that must flow to app consumers through intermediate projects. Use `build` when only direct consumers need imports.

Validate by:

1. `dotnet pack`
2. Inspecting the `.nupkg`.
3. Referencing the package from a new MAUI app.
4. Building and running on each target platform.

## Android packaging

Binding package should include:

- Managed binding assembly for `net10.0-android`.
- Bound AAR/JAR assets when appropriate.
- NuGet dependencies for existing Java binding packages.
- `AndroidMavenLibrary` or `AndroidLibrary` items as needed.
- Native `.so` ABI assets when not already inside an AAR.

Use `Pack` metadata intentionally:

```xml
<AndroidMavenLibrary Include="com.vendor:sdk" Version="1.2.3" Pack="true" />
<AndroidMavenLibrary Include="com.vendor:runtime" Version="1.2.3" Bind="false" Pack="true" />
```

For existing NuGet bindings:

```xml
<PackageReference Include="Xamarin.AndroidX.Core" Version="1.13.1.3" />
```

If a dependency is fulfilled by a package without artifact metadata:

```xml
<PackageReference Include="Vendor.Dependency.Binding" Version="1.2.3"
                  JavaArtifact="com.vendor:dependency:1.2.3" />
```

### Embed the AAR vs resolve it at consumer build

There are two ways to get the native AAR into the consumer's app:

- Embed it in the `.nupkg` (`AndroidLibrary`/bound AAR asset). Self-contained, works offline, but the package is large and you own transitive-dependency accuracy.
- Ship a `.targets` that resolves it from Maven via Gradle at app build time (inject `GradleImplementation` plus `AndroidLibrary Pack="false" Bind="false"`, gated on `'$(AndroidApplication)' == 'true'`). Tiny package with always-correct transitive resolution, but the consumer needs the Android SDK, Gradle, and network access at build, and the target must handle Unix vs Windows Gradle-cache paths.

Prefer embedding for public packages unless the native SDK's transitive graph is too large or license terms forbid redistribution.

### Ship ProGuard/R8 keep rules with the package

If the native library needs consumer-side keep rules (the Android analog of iOS `[Preserve]`), ship a `proguard.txt` and reference it from your `buildTransitive` `.targets`, gated on the consumer being an app:

```xml
<ItemGroup>
  <ProguardConfiguration
    Condition=" '$(AndroidApplication)' == 'true' and Exists('$(MSBuildThisFileDirectory)proguard.txt') "
    Include="$(MSBuildThisFileDirectory)proguard.txt" />
</ItemGroup>
```

Without this, Release builds with R8 can strip classes the native code reflects on, producing `ClassNotFoundException` only in release.

## build vs buildTransitive

| Folder | Effect |
|--------|--------|
| `build/` | Imported by direct package consumers. |
| `buildTransitive/` | Flows transitively through projects that reference the package. |

For MAUI app consumption, native asset wiring often needs `buildTransitive` so the final app project receives native items even if a shared library references the binding package.

Use `assets/multi-platform-package.targets.xml` as a starting snippet for wiring Apple `NativeReference` and Android native dependency items from a package. Rename the file to match the package ID when placing it under `build/` or `buildTransitive/`.

## Package validation checklist

- Native library redistribution license verified.
- Package contains no secret tokens, private repo URLs with credentials, or local absolute paths.
- Apple package contains all required platform slices.
- Android package satisfies Java dependency verification.
- No duplicate native frameworks/AARs are included by both package and app.
- Consumer app builds from a clean NuGet cache.
- Consumer app runs at least one native call per target platform.
- Package tags include native artifact identifiers when useful.
- README or package docs explain native SDK version and setup requirements.

## Failure patterns

| Symptom | Likely packaging issue |
|---------|------------------------|
| App compiles but Apple runtime cannot load framework | Native asset not imported or embedded for final app. |
| Android `NoClassDefFoundError` | Java runtime dependency not packaged or referenced. |
| Duplicate class/type errors | Same AAR/JAR included by multiple packages/items. |
| Duplicate symbols on Apple | Same static library linked twice. |
| Works with project reference but fails from NuGet | Missing build/buildTransitive props/targets or native files in package. |
