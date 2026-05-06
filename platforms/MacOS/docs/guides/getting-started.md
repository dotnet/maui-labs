# Getting Started

Set up a .NET MAUI macOS (AppKit) app from scratch, or add macOS support to an existing MAUI project.

## Overview

The macOS backend runs as a separate **app head project** that references your shared MAUI code. This is similar to how MAUI uses platform-specific head projects, but since the macOS/AppKit backend is a community package (not built into the MAUI workload), you create a standalone `net10.0-macos` project that links to your shared pages, resources, and platform code.

```
MyApp/
├── MyApp/                      # Shared MAUI project (pages, view models, etc.)
│   ├── Pages/
│   ├── Resources/
│   │   ├── Fonts/
│   │   └── AppIcon/
│   ├── wwwroot/                # (if using Blazor Hybrid)
│   └── Platforms/
│       └── macOS/              # macOS platform-specific code
│           ├── App.cs
│           ├── Main.cs
│           ├── MauiMacOSApp.cs
│           └── MauiProgram.cs
│
├── MyApp.MacOS/                # macOS app head project
│   ├── MyApp.MacOS.csproj
│   ├── Info.plist              # (optional — auto-generated if absent)
│   └── Entitlements.plist      # (optional — for sandboxing, etc.)
│
└── MyApp.sln
```

## Step 1: Create the macOS App Head Project

Create a new class library targeting `net10.0-macos`:

```xml
<!-- MyApp.MacOS/MyApp.MacOS.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-macos</TargetFramework>
    <SupportedOSPlatformVersion>14.0</SupportedOSPlatformVersion>
    <RootNamespace>MyApp</RootNamespace>

    <!-- Prevent MAUI's default single-project processing -->
    <UseMaui>false</UseMaui>
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
  </PropertyGroup>

  <!-- Platform NuGet packages -->
  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.Controls" Version="10.0.31" />
    <PackageReference Include="Microsoft.Maui.Platforms.MacOS" Version="0.2.0-beta.6" />
  </ItemGroup>

</Project>
```

> **Note:** If referencing the platform project source instead of NuGet, use `<ProjectReference>` instead of `<PackageReference>`.

## Step 2: Link Shared Code

Link your shared pages, view models, and helpers from the main MAUI project:

```xml
<!-- MyApp.MacOS/MyApp.MacOS.csproj -->
<ItemGroup>
  <!-- Platform bootstrap files -->
  <Compile Include="..\MyApp\Platforms\macOS\App.cs" Link="App.cs" />
  <Compile Include="..\MyApp\Platforms\macOS\Main.cs" Link="Main.cs" />
  <Compile Include="..\MyApp\Platforms\macOS\MauiMacOSApp.cs" Link="MauiMacOSApp.cs" />
  <Compile Include="..\MyApp\Platforms\macOS\MauiProgram.cs" Link="MauiProgram.cs" />

  <!-- Shared code -->
  <Compile Include="..\MyApp\Pages\*.cs" Link="Pages\%(Filename)%(Extension)" />
  <Compile Include="..\MyApp\ViewModels\*.cs" Link="ViewModels\%(Filename)%(Extension)" />
  <Compile Include="..\MyApp\Services\*.cs" Link="Services\%(Filename)%(Extension)" />
  <!-- Add more as needed -->
</ItemGroup>
```

### Link Resources

```xml
<ItemGroup>
  <!-- App icon (auto-converted to .icns) -->
  <MauiIcon Include="..\MyApp\Resources\AppIcon\appicon.png" />

  <!-- Fonts -->
  <MauiFont Include="..\MyApp\Resources\Fonts\*" />

  <!-- Blazor wwwroot (if applicable) -->
  <BundleResource Include="..\MyApp\wwwroot\**"
                  Link="wwwroot\%(RecursiveDir)%(Filename)%(Extension)" />
</ItemGroup>
```

### App Icon

The `MauiIcon` item is automatically processed into a macOS `.icns` file at build time. The build targets use `sips` and `iconutil` to generate all required icon sizes (16×16 through 512×512 with @2x variants) from your source PNG or SVG.

## Step 3: Create Platform Bootstrap Files

Create these 4 files in your shared project's `Platforms/macOS/` folder.

### Main.cs — Entry Point

```csharp
// Platforms/macOS/Main.cs
using AppKit;

namespace MyApp;

static class MainClass
{
    static void Main(string[] args)
    {
        NSApplication.Init();
        NSApplication.SharedApplication.Delegate = new MauiMacOSApp();
        NSApplication.Main(args);
    }
}
```

### MauiMacOSApp.cs — Application Delegate

```csharp
// Platforms/macOS/MauiMacOSApp.cs
using Foundation;
using Microsoft.Maui.Platforms.MacOS.Hosting;

namespace MyApp;

[Register("MauiMacOSApp")]
public class MauiMacOSApp : MacOSMauiApplication
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
```

### MauiProgram.cs — App Builder

```csharp
// Platforms/macOS/MauiProgram.cs
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.MacOS.Hosting;

namespace MyApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiAppMacOS<MacOSApp>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        return builder.Build();
    }
}
```

### App.cs — MAUI Application

```csharp
// Platforms/macOS/App.cs
using Microsoft.Maui.Controls;

namespace MyApp;

public class MacOSApp : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage());
    }
}
```

Or with Shell navigation:

```csharp
public class MacOSApp : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var shell = new Shell();

        // Enable native macOS sidebar
        MacOSShell.SetUseNativeSidebar(shell, true);

        // Add shell items
        var main = new ShellContent
        {
            Title = "Home",
            ContentTemplate = new DataTemplate(typeof(MainPage)),
            Route = "home"
        };
        shell.Items.Add(main);

        return new Window(shell);
    }
}
```

## Step 4: Build and Run

```bash
# Build
dotnet build MyApp.MacOS/MyApp.MacOS.csproj

# The app bundle is created at:
# MyApp.MacOS/bin/Debug/net10.0-macos/osx-arm64/MyApp.app

# Launch
open MyApp.MacOS/bin/Debug/net10.0-macos/osx-arm64/MyApp.app
```

## Conditional Compilation

Use `#if` directives for macOS-specific code in shared files:

```xml
<!-- In your .csproj, define a constant -->
<PropertyGroup>
  <DefineConstants>$(DefineConstants);MACAPP</DefineConstants>
</PropertyGroup>
```

```csharp
#if MACAPP
using Microsoft.Maui.Platforms.MacOS.Platform;

// macOS-specific code
MacOSWindow.SetTitlebarStyle(window, MacOSTitlebarStyle.UnifiedCompact);
#endif
```

## Adding Platform-Specific Handlers

Register custom handlers or override existing ones in `MauiProgram.cs`:

```csharp
builder.ConfigureMauiHandlers(handlers =>
{
    // Use native sidebar for FlyoutPage
    handlers.AddHandler<FlyoutPage,
        Microsoft.Maui.Platforms.MacOS.Handlers.NativeSidebarFlyoutPageHandler>();
});
```

## Optional: Blazor Hybrid Support

See [Blazor Hybrid](blazor-hybrid.md) for adding BlazorWebView support.

## Optional: Essentials Support

Add the Essentials package for macOS implementations of device APIs:

```xml
<PackageReference Include="Microsoft.Maui.Platforms.MacOS.Essentials" Version="0.1.0-alpha-0001" />
```

```csharp
// In MauiProgram.cs
builder.AddMacOSEssentials();
```

## Tips

- **SF Symbols**: Use SF Symbol names directly as `IconImageSource` values (e.g., `"gear"`, `"plus"`, `"square.and.arrow.up"`)
- **Window size**: Set initial window size via `Window.Width` and `Window.Height` in your `App.CreateWindow()`
- **Debug**: Use `open MyApp.app` to launch — the app runs independently of the terminal
- **Hot reload**: Not currently supported — rebuild and relaunch for changes
- **MainThread**: `MainThread.BeginInvokeOnMainThread()` is **not supported** — use `Dispatcher.Dispatch()` or `MainThreadHelper.BeginInvokeOnMainThread()` instead (see [Controls & Platform Notes](controls.md#dispatcher--threading))
