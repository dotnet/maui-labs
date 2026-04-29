# Contributing to .NET MAUI Labs

Thank you for your interest in contributing! This repository hosts experimental .NET MAUI packages that are in active development.

## Repository Structure

```
maui-labs/
├── src/                    # Source code, organized by product
│   └── {Product}/          # Each product has its own folder
│       ├── Version.props   # Per-product version
│       ├── {Product}.slnf  # Solution filter for this product
│       └── ...projects...
├── samples/                # Sample apps (not shipped)
├── playground/             # Manual verification/scratch apps
├── docs/                   # Documentation per product
├── eng/                    # Shared build infrastructure
└── MauiLabs.sln            # Full solution
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (see `global.json` for exact version)
- MAUI workload: `dotnet workload install maui`

### Building

```bash
# Build everything
dotnet build MauiLabs.sln

# Build just one product (e.g., DevFlow)
dotnet build src/DevFlow/DevFlow.slnf

# Build a specific project
dotnet build src/DevFlow/Microsoft.Maui.DevFlow.Agent.Core/
```

### Running Tests

```bash
# All tests
dotnet test MauiLabs.sln

# Just DevFlow tests
dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Tests/
```

### Opening in an IDE

For focused development on a single product, open the solution filter:

- **DevFlow**: `src/DevFlow/DevFlow.slnf`

For the full repo, open `MauiLabs.sln`.

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
