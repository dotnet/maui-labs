---
name: maui-project-structure
description: >-
  Inspect and update .NET MAUI app project structure and app display/build
  versioning. Answer questions about ApplicationDisplayVersion,
  ApplicationVersion, display version, and build number, and reject using maui
  project version set for those app version properties. Also cover target
  frameworks, resources, package references, and platform folders.
  USE FOR: single-project MAUI layout, Resources/Images/Fonts/Raw,
  MauiImage/MauiBundledFont/MauiAsset resource item types, Android/iOS/Windows
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

1. Find the MAUI app project:

   ```bash
   grep -R -n --include="*.csproj" "<UseMaui>true</UseMaui>" .
   ```

2. Inspect target frameworks, package management, and shared build props:

   ```bash
   grep -R -n --include="*.csproj" --include="Directory.Build.props" "<TargetFramework" . 2>/dev/null
   test -f Directory.Packages.props && grep -n "PackageVersion" Directory.Packages.props
   ```

3. Respect Central Package Management:
   - If `Directory.Packages.props` exists, add versions there with
     `<PackageVersion Include="..." Version="..." />`.
   - Leave project `PackageReference` items versionless.
4. Use MAUI single-project folders:
   - `Resources/Images` with `MauiImage`
   - `Resources/Fonts` with `MauiFont`
   - `Resources/Raw` with `MauiAsset`
   - `Platforms/Android`, `Platforms/iOS`, `Platforms/MacCatalyst`,
     `Platforms/Windows`
5. For app display/build version edits, use project properties:

   ```xml
   <ApplicationDisplayVersion>1.2.3</ApplicationDisplayVersion>
   <ApplicationVersion>123</ApplicationVersion>
   ```

6. For the .NET MAUI package/workload version used by a project, use the MAUI
   project version command. Do **not** use `maui project version` to set
   `ApplicationDisplayVersion` or `ApplicationVersion`:

   ```bash
   maui project version --help
   ```

7. For environment issues, run:

   ```bash
   maui doctor
   ```

8. Build the edited app project for at least one target framework.

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
<MauiAsset Include="Resources/Raw/**" LogicalName="%(RecursiveDir)%(Filename)%(Extension)" />
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
