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
