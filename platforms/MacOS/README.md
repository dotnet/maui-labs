# .NET MAUI for macOS (AppKit)

A native [.NET MAUI](https://dot.net/maui) backend for macOS using AppKit — not Mac Catalyst.

This backend lets MAUI applications run as true native macOS apps that use AppKit controls
(`NSWindow`, `NSButton`, `NSScrollView`, etc.) and follow standard macOS UI conventions
(menu bar, toolbar, sidebar flyout, native dialogs, etc.).

> **Inspiration:** Originally based on the
> [shinyorg/mauiplatforms](https://github.com/shinyorg/mauiplatforms) project. The Xamarin.Forms
> [`Xamarin.Forms.Platform.MacOS`](https://github.com/xamarin/Xamarin.Forms/tree/5.0.0/Xamarin.Forms.ControlGallery.MacOS)
> backend is also a useful historical reference for AppKit control mappings, although this
> project uses MAUI's modern handler architecture rather than the legacy renderer model.

## Packages

| Package | Description |
| --- | --- |
| `Microsoft.Maui.Platforms.MacOS` | Core handlers, hosting, platform services |
| `Microsoft.Maui.Platforms.MacOS.Essentials` | MAUI Essentials implementations (clipboard, preferences, sensors, …) |
| `Microsoft.Maui.Platforms.MacOS.BlazorWebView` | Blazor Hybrid (`BlazorWebView`) support |

## Prerequisites

- .NET 10 SDK
- macOS 14 (Sonoma) or later
- Xcode command line tools (for `sips` / `iconutil` — used by the icon build target)

## Quick start

### Option 1: Use the template (recommended)

```bash
# Install the template
dotnet new install Microsoft.Maui.Platforms.MacOS.Templates --prerelease

# Create a new macOS MAUI app
dotnet new maui-macos -n MyApp.MacOS
cd MyApp.MacOS
dotnet run
```

### Option 2: Add to an existing project manually

#### 1. Project file

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-macos</TargetFramework>
    <OutputType>Exe</OutputType>
    <UseMaui>true</UseMaui>
    <SingleProject>true</SingleProject>
    <SupportedOSPlatformVersion>14.0</SupportedOSPlatformVersion>

    <ApplicationTitle>My macOS App</ApplicationTitle>
    <ApplicationId>com.example.myapp</ApplicationId>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.Controls" Version="$(MauiVersion)" />
    <PackageReference Include="Microsoft.Maui.Platforms.MacOS" Version="*" />
    <PackageReference Include="Microsoft.Maui.Platforms.MacOS.Essentials" Version="*" />
  </ItemGroup>

  <ItemGroup>
    <MauiIcon Include="Resources\AppIcon\appicon.png" />
  </ItemGroup>
</Project>
```

#### 2. `Main.cs`

```csharp
using AppKit;

public class MainClass
{
    static void Main(string[] args)
    {
        NSApplication.Init();
        NSApplication.SharedApplication.Delegate = new MauiMacOSApp();
        NSApplication.Main(args);
    }
}
```

#### 3. `MauiMacOSApp.cs`

```csharp
using Foundation;
using Microsoft.Maui.Platforms.MacOS.Platform;

[Register("MauiMacOSApp")]
public class MauiMacOSApp : MacOSMauiApplication
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
```

#### 4. `MauiProgram.cs`

```csharp
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Microsoft.Maui.Platforms.MacOS.Essentials;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiAppMacOS<App>()
            .AddMacOSEssentials()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        return builder.Build();
    }
}
```

#### 5. `App.cs`

```csharp
public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
        => new Window(new MainPage());
}
```

## Building / running the sample

```bash
dotnet build platforms/MacOS/MacOS.slnx
dotnet run --project platforms/MacOS/samples/MacOS.Sample/
```

## Native runtime scenarios

On macOS, run a registered scenario through the shared native host:

```bash
export NUGET_PACKAGES="$PWD/.packages"
python3 -B platforms/MacOS/tests/MacOS.RuntimeTests/run.py \
  --scenario layout --evidence "$PWD/artifacts/layout-run"
```

It opens an AppKit window and mutates a MAUI layout after its handler connects.
It checks nested child bounds, native insertion order, replacement, removal,
clear, and re-addition without resizing the window. Failures exit with code 1;
success prints a `PASS` line for each of the six cases and exits with code 0. A 90-second
watchdog fails a hung run.
This executable uses the AppKit main thread and is not a `dotnet test` project.
Building it on Windows does not validate AppKit behavior. For a before/after
comparison, run the same executable with `LayoutHandler`'s constructor passing
only `Mapper` (the original behavior), then with `Mapper, CommandMapper`.
The original behavior must fail the first dynamic addition check.

The shared `AppKit runtime` CI matrix performs that comparison on a
GitHub-hosted macOS runner. Its `appkit-runtime-layout-default` artifact contains both
process logs, native view screenshots and managed/native bounds after addition.
Screenshots use Aqua appearance and composite transparent view backgrounds over
white so native text remains readable in dark artifact viewers.
The required evidence directory also receives strict machine-readable terminal
results and assertion counts. Hosted execution is distinct from a local desktop run.
See [the shared host contract](tests/MacOS.RuntimeTests/README.md) to add a scenario,
version matrix entry or scoped fixture assets without another application/project.
Portable `MacOS.Tests` remains independent of native AppKit execution.

## Unit tests

The platform-neutral tests exercise the backend's managed lifecycle code using real MAUI
windows and handlers. They target `net10.0` and do not require AppKit, Xcode, or MAUI workloads:

```bash
dotnet test platforms/MacOS/tests/MacOS.Tests/MacOS.Tests.csproj
```

The AppKit CI workflow and official macOS product build also run these tests.
Native window notifications and rendering still require testing in a running macOS app.

## MAUI DevFlow integration

The sample app supports the optional in-process MAUI DevFlow agent:

```bash
dotnet run --project platforms/MacOS/samples/MacOS.Sample/ -p:EnableMauiDevFlow=true
```

This exposes a local HTTP API and MCP server for inspecting the running app's visual tree,
capturing screenshots, automating interactions, and more. See `src/DevFlow/` and the
`maui-platform-backend` skill's `devflow-integration.md` reference for details.

## License

MIT — see [LICENSE](LICENSE).
