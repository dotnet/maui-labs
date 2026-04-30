# Getting Started with .NET MAUI on WPF

This guide walks you through creating your first .NET MAUI app running on Windows using the WPF backend.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- MAUI workload: `dotnet workload install maui`

## Quick Start

1. Install the project template:

```bash
dotnet new install Microsoft.Maui.Platforms.Windows.WPF.Templates
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

## Using the NuGet Package Directly

Add the WPF backend package to an existing .NET MAUI project:

```xml
<PackageReference Include="Microsoft.Maui.Platforms.Windows.WPF" Version="0.1.0-preview" />
```

Then configure your `MauiProgram.cs`:

```csharp
using Microsoft.Maui.Platforms.Windows.WPF;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseWpfBackend();
        return builder.Build();
    }
}
```

## How It Works

The WPF backend renders .NET MAUI controls using WPF (Windows Presentation Foundation) instead of WinUI 3. This provides:

- Broader Windows version support (Windows 7+)
- Familiar WPF rendering pipeline
- Access to WPF-specific features and controls

## Sample App

See the [Windows.WPF.Sample](../samples/Windows.WPF.Sample/) for a full Control Gallery demonstrating all supported handlers.

## Documentation

- [Backend Implementation Checklist](backend-implementation-checklist.md) — handler implementation status and progress
