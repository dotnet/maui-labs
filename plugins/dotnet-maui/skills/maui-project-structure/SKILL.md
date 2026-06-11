---
name: maui-project-structure
description: >-
  Inspect and update .NET MAUI app project structure and app display/build
  versioning. Answer questions about ApplicationDisplayVersion,
  ApplicationVersion, display version, and build number, and reject using maui
  project version set for those app version properties. Also cover target
  frameworks, resources, package references, and platform folders.
  USE FOR: single-project MAUI layout, Resources/Images/Fonts/Raw,
  MauiImage/MauiFont/MauiAsset resource item types, Android/iOS/Windows
  platform configuration, Central Package Management (Directory.Packages.props
  versionless PackageReference), app version properties such as
  ApplicationDisplayVersion/ApplicationVersion, target framework selection,
  maui doctor, MAUI package version pinning guidance, and explaining that maui
  project version is not for app display/build versioning.
  DO NOT USE FOR: runtime UI debugging (use maui-devflow-debug), SDK/workload
  discovery (use dotnet-workload-info), or general MVVM/Shell design (use
  maui-app-architecture).
---

# MAUI Project Structure

Use this skill when editing the shape of a MAUI app project rather than a single
feature implementation. Inspect existing conventions first and preserve them.
Critical app-version guardrail: never answer "yes" to using `maui project
version` for platform app display/build versions. Use
`ApplicationDisplayVersion` and `ApplicationVersion` directly.

## Workflow

1. Identify which file is the MAUI app project (has `<UseMaui>true</UseMaui>`).
2. Check for Central Package Management: if `Directory.Packages.props` exists, add
   new NuGet package versions there with `<PackageVersion Include="..." Version="..." />`,
   and keep `PackageReference` items in the `.csproj` versionless.
3. Use the MAUI resource folder layout for app assets:
   - `Resources/Images/` → `MauiImage` (images/icons)
   - `Resources/Fonts/` → `MauiFont` (custom fonts)
   - `Resources/Raw/` → `MauiAsset` (bundled JSON, data, HTML files)
4. For app display/build version edits, use `ApplicationDisplayVersion` and
   `ApplicationVersion` as MSBuild properties in the `.csproj`.
5. If environment issues arise, run `maui doctor`.
6. Build the edited project to verify.

## Project File Patterns

### Central Package Management

```xml
<!-- Directory.Packages.props -->
<PackageVersion Include="CommunityToolkit.Maui" Version="x.y.z" />
```

```xml
<!-- App.csproj -->
<PackageReference Include="CommunityToolkit.Maui" />
```

### App Version

```xml
<ApplicationDisplayVersion>1.2.3</ApplicationDisplayVersion>
<ApplicationVersion>123</ApplicationVersion>
```

`maui project version` is for the MAUI package/workload version used by the
project, not the platform app display/build version.

If the prompt asks whether to use `maui project version` for app display/build
versioning, the final response must explicitly answer: do **not** use it for
`ApplicationDisplayVersion` or `ApplicationVersion`; set those MSBuild
properties directly.

### Resources

```xml
<MauiImage Include="Resources/Images/*" />
<MauiFont Include="Resources/Fonts/*" />
<MauiAsset Include="Resources/Raw/**" />
```

## Platform Configuration Map

| Platform | Common files |
| --- | --- |
| Android | `Platforms/Android/AndroidManifest.xml`, `MainActivity.cs`, `MainApplication.cs` |
| iOS | `Platforms/iOS/Info.plist`, `AppDelegate.cs`, entitlements if needed |
| Mac Catalyst | `Platforms/MacCatalyst/Info.plist`, entitlements if needed |
| Windows | `Platforms/Windows/Package.appxmanifest`, `App.xaml.cs` |

## Anti-Patterns

- Do not put package versions in `.csproj` files when Central Package Management
  is active.
- Do not add raw platform assets outside the MAUI `Resources` or `Platforms`
  layout unless the project already has a custom build convention.
- Do not assume every app targets Android, iOS, Mac Catalyst, and Windows.
- Do not change signing, provisioning, package IDs, or app identifiers without
  explicit user intent.
- Do not claim `maui project version set` or similar CLI syntax can set the app
  display/build version; use `ApplicationDisplayVersion` and
  `ApplicationVersion`.

## Validation Checklist

- The project with `<UseMaui>true</UseMaui>` was identified.
- Package versions follow the repo's package management model.
- Resource files are in the correct MAUI resource folders.
- Platform-specific settings are in the matching `Platforms/*` folder.
- Version changes use `ApplicationDisplayVersion` and `ApplicationVersion`.
- Prompts that mention `maui project version` explicitly say it is not for app
  display/build versioning.
