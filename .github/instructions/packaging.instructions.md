---
applyTo: "eng/**,Directory.Build.props,Directory.Build.targets,Directory.Packages.props,**/*.csproj,global.json,NuGet.config"
---

# Packaging and Build Infrastructure

## Arcade SDK

This repo uses the [Microsoft.DotNet.Arcade.Sdk](https://github.com/dotnet/arcade) for build infrastructure. Key rules:

- **Never modify files in `eng/common/`** — they are auto-generated and overwritten by Dependency Flow updates
- Arcade version is pinned in `global.json` under `msbuild-sdks`
- CI scripts: `eng/common/cibuild.sh` (macOS/Linux) and `eng\common\cibuild.cmd` (Windows)

## Central Package Management

All NuGet package versions are defined in **`Directory.Packages.props`** at the repo root.

- `.csproj` files reference packages **without versions**: `<PackageReference Include="xunit" />`
- To add a new dependency: add `<PackageVersion Include="Package.Name" Version="X.Y.Z" />` to `Directory.Packages.props`
- To override a version in a specific project: use `VersionOverride="X.Y.Z"` on the `PackageReference`
- MAUI and runtime package versions flow via Dependency Flow (Maestro/DARC) and are defined in `eng/Versions.props`

## Adding a New NuGet Package

1. **Create the project** in `src/{Product}/`:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net10.0</TargetFramework>
       <IsPackable>true</IsPackable>
       <IsShipping>true</IsShipping>
       <PackageId>Microsoft.Maui.DevFlow.NewPackage</PackageId>
       <Description>What this package does</Description>
     </PropertyGroup>
   </Project>
   ```
2. Set `IsPackable` and `IsShipping` to `true` (defaults are `false` in `Directory.Build.props`)
3. For CLI tools, also set `PackAsTool=true` and `ToolCommandName=your-command`
4. Add any new third-party dependencies to `Directory.Packages.props`
5. Add signing entries in `eng/Signing.props` for third-party DLLs:
   ```xml
   <FileSignInfo Include="ThirdParty.dll" CertificateName="3PartySHA2" />
   ```
6. Add the project to the solution: `MauiLabs.sln` and the product's `.slnf`
7. Add to `DevFlow.slnf` if it should be built by CI

## Version Management

```
eng/Versions.props              ← Product version (VersionPrefix + VersionSuffix)
src/{Product}/Version.props     ← Per-product override (if needed)
Directory.Packages.props        ← NuGet dependency versions
eng/Version.Details.xml         ← Dependency Flow tracking (auto-managed by DARC)
```

- **Product version**: `eng/Versions.props` defines `VersionPrefix` (e.g., `0.1.0`) and `VersionSuffix` (e.g., `preview.3`)
- **Bump version**: Update `VersionPrefix` or `VersionSuffix` in `eng/Versions.props`
- **Per-product override**: `src/{Product}/Version.props` can override for independent versioning

## Signing Configuration

`eng/Signing.props` controls code signing:

| Certificate | Used For |
|------------|----------|
| `UseDotNetCertificate` | First-party Microsoft DLLs (default for all projects) |
| `3PartySHA2` | Third-party dependency DLLs bundled in packages |
| `NuGet` | `.nupkg` files |
| `None` | Static assets (JS, CSS files) |

When adding a new third-party dependency that gets bundled into a NuGet package, add a `<FileSignInfo>` entry.

## NuGet Feed Configuration

`NuGet.config` defines package sources. **Only use approved internal feeds:**

- `dotnet-public` (dnceng) — public .NET packages
- `dotnet-tools` — internal tooling
- `dotnet-eng` — engineering infrastructure
- `dotnet10` — version-specific feed

**Do NOT add `nuget.org` as a direct source.** All public packages are available through the dnceng proxy feeds.

## Publishing Flow

1. **GitHub Actions CI**: Build + test on PR (no publishing)
2. **Azure DevOps official build**: Build → Sign (MicroBuild) → Validate → Publish to internal feeds via DARC
3. **NuGet.org release**: Separate pipeline (`eng/pipelines/release-publish-nuget.yml`) with `networkIsolationPolicy: Permissive`

The official build pipeline (`eng/pipelines/devflow-official.yml`) has `enableMicrobuild: true` which enforces CFS network isolation — this is why NuGet.org publishing must happen in a separate pipeline.
