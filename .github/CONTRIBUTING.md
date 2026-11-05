# Contributing to .NET MAUI Labs

Thank you for your interest in contributing! This repository hosts experimental .NET MAUI packages that are in active development.

## Repository Structure

```
maui-labs/
├── src/                # Source code, organized by product
│   ├── Cli/            # MAUI CLI and device management
│   ├── DevFlow/        # DevFlow agent and testing toolkit
│   ├── Comet/          # Comet MVU framework
│   └── Go/             # Single-file Comet apps (Comet Go)
├── samples/                # Sample apps (not shipped)
├── playground/             # Manual verification/scratch apps
├── docs/                   # Documentation per product
├── eng/                    # Shared build infrastructure
└── MauiLabs.slnx           # Full solution
```

## Getting Started

### Prerequisites

- [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0) (see `global.json` for the pinned version)
- MAUI workloads: `dotnet workload install maui macos --from-rollback-file eng/workloads/dotnet11-rc2.json`

Use the exact pinned SDK with this rollback file; manifests are SDK-feature-band
specific. Do not install a .NET 10 workload set into the .NET 11 SDK. The RC2
manifests retain .NET 10 Apple packs for Xcode 26.0 alongside .NET 11 Apple
packs for Xcode 27.0. Select the matching Xcode when building app heads.
Resolve Xcode aliases with `pwd -P` before setting `DEVELOPER_DIR` and
`MD_APPLE_SDK_ROOT`: the Apple pack's explicit SDK paths must use the physical
Xcode application directory, not a runner alias such as `Xcode_26.0.app`.
For iOS asset compilation, install the SDK-compatible simulator runtime with
`xcodebuild -downloadPlatform iOS -buildVersion 26.0.1`, even when the test device
uses a later runtime such as 26.2.
Existing .NET 10 target frameworks and central MAUI package versions remain
unchanged; AI's `UseXcode27Preview=true` adds .NET 11 OS 27 targets.

### Building

```bash
# Build everything
dotnet build MauiLabs.slnx

# Build just one product (e.g., DevFlow)
dotnet build src/DevFlow/DevFlow.slnf

# Build a specific project
dotnet build src/DevFlow/Microsoft.Maui.DevFlow.Agent.Core/
```

### Running Tests

```bash
# All tests
dotnet test MauiLabs.slnx

# Just DevFlow tests
dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Tests/
```

### Opening in an IDE

For focused development on a single product, open the solution filter:

- **DevFlow**: `src/DevFlow/DevFlow.slnf`

For the full repo, open `MauiLabs.slnx`.

## Adding a New Product

### 1. Source code

1. Create `src/{NewProduct}/` with:
   - `Version.props` (copy from an existing product)
   - Project folders with `.csproj` files
   - Test project
   - `{NewProduct}.slnf` solution filter
2. Add projects to `MauiLabs.slnx`
3. Add any new package versions to `Directory.Packages.props`

### 2. GitHub Actions CI workflow

Create `.github/workflows/ci-{newproduct}.yml` that calls the reusable `_build.yml` workflow.

> See the **"CI/CD — New Product Checklist"** section in `.github/copilot-instructions.md` for the complete copy-paste template with all required inputs and decision guidance.

Key points:
- One workflow file per product, path-filtered to its source folder
- Always include `types: [opened, synchronize, reopened, edited]` on `pull_request` (the `edited` type ensures CI re-runs when a PR is auto-retargeted after a stacked branch merges)

### 3. Azure DevOps official pipeline

The official build/sign/publish pipeline is `eng/pipelines/devflow-official.yml`. For each new product add:
1. A **boolean parameter** to gate NuGet.org publishing
2. A **build job** in the `build` stage (parallel with existing jobs)
3. A **conditional publish stage** that filters your product's `.nupkg` files and pushes to NuGet.org

> See `.github/copilot-instructions.md` for complete annotated YAML snippets for all three blocks.

### 4. Signing

Add entries in `eng/Signing.props` for any new third-party DLLs (`3PartySHA2`).

## Versioning

Each product manages its own version in `src/{Product}/Version.props`:

```xml
<Project>
  <PropertyGroup>
    <VersionPrefix>1.0.0</VersionPrefix>
    <VersionSuffix>preview.1</VersionSuffix>
  </PropertyGroup>
</Project>
```

## Package Management

This repo uses [Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management). All package versions are defined in `Directory.Packages.props` at the repo root. Individual `.csproj` files reference packages without specifying versions.

## Code Style

- `ImplicitUsings` and `Nullable` are enabled repo-wide
- Follow standard .NET naming conventions

## Pull Requests

- PRs trigger CI builds only for products with changed files
- Ensure tests pass before requesting review
- Update `Version.props` if your change warrants a version bump
