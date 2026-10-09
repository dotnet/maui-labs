# Arcade Build System

This repository uses the [dotnet/arcade](https://github.com/dotnet/arcade) build system for versioning, signing, packaging, publishing, and CI/CD.

## Key Files

| File | Purpose |
|------|---------|
| `global.json` | Pins Arcade SDK version |
| `Directory.Build.props` | Imports Arcade Sdk.props, sets common properties |
| `Directory.Build.targets` | Imports Arcade Sdk.targets |
| `eng/Versions.props` | Version prefix, pre-release label, all dependency versions |
| `eng/Version.Details.xml` | Tracks dependency source repos and SHAs for maestro |
| `eng/Publishing.props` | Publishing configuration (V3) |
| `eng/common/` | Shared arcade build scripts (do not edit manually) |
| `Directory.Packages.props` | Central Package Management — versions reference `eng/Versions.props` properties |

## Versioning

Arcade manages versions automatically:
- **VersionPrefix**: `0.1.0` (from `eng/Versions.props`)
- **PreReleaseVersionLabel**: `preview`
- Official builds append build number: `0.1.0-preview.26166.3`

## Building

```bash
# Local build
dotnet build

# CI-style build (uses arcade scripts)
./eng/common/cibuild.sh --configuration Release --prepareMachine
```

## Shipping Packages

All DevFlow library packages have `IsShipping=true` and will be published to NuGet feeds:
- Microsoft.Maui.DevFlow.Agent
- Microsoft.Maui.DevFlow.Agent.Core
- Microsoft.Maui.DevFlow.Agent.Gtk
- Microsoft.Maui.DevFlow.Blazor
- Microsoft.Maui.DevFlow.Blazor.Gtk
- Microsoft.Maui.DevFlow.CLI
- Microsoft.Maui.DevFlow.Client
- Microsoft.Maui.DevFlow.Driver
- Microsoft.Maui.DevFlow.Logging

## Dependency Flow

Dependencies are tracked in `eng/Version.Details.xml` and updated automatically by maestro via darc subscriptions. See the PR description for setup commands.

## Updating eng/common/

The `eng/common/` directory is managed by arcade and updated automatically via darc. Do not edit files in this directory manually.
