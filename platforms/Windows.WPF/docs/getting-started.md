# Getting Started with .NET MAUI on WPF

This guide walks you through creating your first .NET MAUI app running on Windows using the WPF backend.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- MAUI workload: `dotnet workload install maui`

## Quick Start

1. Install the project template:

```bash
dotnet new install Microsoft.Maui.Platforms.Windows.WPF.Templates --prerelease
```

2. Create a new project:

```bash
dotnet new maui-wpf -n MyWpfApp
```

3. Build and run:

```bash
cd MyWpfApp
dotnet run
```

The packed template references the WPF backend and Essentials packages from the
same release as the template. It also explicitly references the MAUI Controls
version used to build the backend, so the SDK's implicit MAUI version cannot
cause a package downgrade.

## Using the NuGet Package Directly

Use a `net10.0-windows` project with `UseMaui` and `UseWPF` enabled. For a
code-only entry point, also set `EnableDefaultApplicationDefinition` and
`EnableDefaultPageItems` to `false`, as the template does.

Reference `Microsoft.Maui.Controls` explicitly at the version required by the
backend (currently `10.0.41`), and reference both
`Microsoft.Maui.Platforms.Windows.WPF` and
`Microsoft.Maui.Platforms.Windows.WPF.Essentials` at the same full release
version. For example, these references select the preview.12 release:

```xml
<PackageReference Include="Microsoft.Maui.Controls" Version="10.0.41" />
<PackageReference Include="Microsoft.Maui.Platforms.Windows.WPF" Version="0.1.0-preview.12.26421.1" />
<PackageReference Include="Microsoft.Maui.Platforms.Windows.WPF.Essentials" Version="0.1.0-preview.12.26421.1" />
```

Do not shorten a release version to `0.1.0-preview`: NuGet treats that as a
minimum version and selects the oldest matching package, not the latest preview.

Then configure your `MauiProgram.cs`:

```csharp
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiAppWPF<App>()
            .UseWPFEssentials();
        return builder.Build();
    }
}
```

Here, `App` is your `Microsoft.Maui.Controls.Application` subclass. The WPF
entry point must be a separate `MauiWPFApplication` subclass, which connects
the MAUI application to WPF and creates its native window:

```csharp
using System;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;

public class WpfApp : MauiWPFApplication
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

public static class Program
{
    [STAThread]
    public static void Main()
    {
        new WpfApp().Run();
    }
}
```

Creating a plain `System.Windows.Application` and an unrelated `MauiApp` does
not initialize the WPF backend or display the MAUI application's window.

## Validating Template Changes

Run the Windows smoke test from the repository root:

```powershell
eng\smoke-tests\wpf-template-smoke-test.ps1
```

It packs the backend, Essentials, and template, installs the template in an
isolated hive, and generates a consumer project without repository build imports.
It checks exact restored dependency versions, builds the unmodified generated
app, and verifies that the app opens a native window. It also repacks the template
with a changed version without cleaning to catch stale substitutions.
Artifacts are retained in a unique `wpf-template-smoke-*` directory under the
system temporary directory to avoid Windows path-length limits in deep worktrees;
use `-ArtifactsDirectory` to choose a new directory. No global template installation
is changed. `-SdkDirectory` can select an already-installed SDK for local
diagnostics when the repository-pinned SDK is unavailable. This also selects the
`dotnet.exe` from that SDK's installation rather than the host on `PATH`. CI installs
`maui` and `maui-tizen` at the pinned workload version into the setup-dotnet SDK
and explicitly uses that same SDK for packing, generation, and the consumer build.

Add `-FontScenarios` to exercise registered fonts in that same generated
PackageReference consumer (enabled in WPF CI). The harness adds the sample's shared
font registration and native scenario, without adding any ProjectReference or
repository target import. It checks the actual packed font target and its consumer
integration, then compares native font identity, glyphs, and pixels against an
independent trusted Open Sans control in three working directories for both build
and no-build/no-restore publish output. The normal template window check still runs.
The package contract, evaluated imports, native results, and tested packages are
retained in the smoke-test artifacts.

Install the packed `.nupkg`, not the template source directory: dependency version
tokens in the source project are replaced during packing.

## How It Works

The WPF backend renders .NET MAUI controls using WPF (Windows Presentation Foundation) instead of WinUI 3. This provides:

- Broader Windows version support (Windows 7+)
- Familiar WPF rendering pipeline
- Access to WPF-specific features and controls

## Sample App

See the [Windows.WPF.Sample](../samples/Windows.WPF.Sample/) for a full Control Gallery demonstrating all supported handlers.

## Documentation

- [Backend Implementation Checklist](backend-implementation-checklist.md) — handler implementation status and progress
