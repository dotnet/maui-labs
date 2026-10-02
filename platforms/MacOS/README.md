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

## Shell navigation

Shell displays native segmented tabs for a `TabBar` (including a single tab) and for
`FlyoutItem`s with multiple visible sections. The strip scrolls horizontally when
the tabs do not fit a narrow window. Section titles, enabled/visible state, collection
changes and selection update the strip without creating inactive pages.
`Shell.TabBarIsVisible` on the displayed page hides the strip.

Portable presentation tests run with
`dotnet test platforms/MacOS/tests/MacOS.Tests/MacOS.Tests.csproj`.
The shared `MacOS.RuntimeTests` host runs the `shell-tabs` scenario in an isolated
real AppKit process. Its `native-runtime` CI matrix row uploads screenshots, native
attachment traces and exact results as `appkit-runtime-shell-tabs-default`.
Run it through `tests/MacOS.RuntimeTests/run.py --scenario shell-tabs --evidence <new-directory>`
on macOS. The identical fixture checks a pinned two-file production overlay from
`b0767ac6` for exactly three mounts per native click, then the current source for one.
The original `9206e7c` missing-tabs proof remains historical, not this overlay's baseline.
The scenario also checks that the selected tab and displayed native page agree
after programmatic selection, hiding/removing the active section, and handler
rebinding, and that queued work is ignored after disconnect.
Native tab clicks use the same queued section refresh as programmatic selection.
The harness counts native attachments across recreated handlers to verify that
new and cached pages are displayed once per click, and checks narrow tabs and
explicit wide-sidebar presentation with titles and system icons.
Same-page refreshes retain valid native views while updating layout and chrome;
the native tests also cover remounts, handler/context replacement, and navigation
to a different destination during lazy page creation.
The scenario requires exactly 63 assertions; lower section navigation requires 59.
Wide/narrow checks explicitly select Locked at 1000 DIP and Disabled at 480 DIP,
not an automatic resize policy. Native sidebar title/icon/bounds checks are not
pixel proof: `CacheDisplay` omits the vibrancy/sidebar region.
Compilation and portable tests alone do not establish native rendering.

## Shell section navigation

Shell observes section and content selection changes, including `GoToAsync` and
programmatic `CurrentItem` updates. The selected lazy page is created and hosted
without requiring a manual handler refresh; unselected templates remain lazy.
The shared `MacOS.RuntimeTests` host's `shell-sections` scenario exercises the
pre-fix Shell handlers and the current code through full MAUI/AppKit startup.
The `native-runtime` CI matrix uploads screenshots, navigation state and all 59
assertions as `appkit-runtime-shell-sections-default`. See the
[shared runner](tests/MacOS.RuntimeTests/README.md) for local macOS usage.

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
    <MauiImage Include="Resources\Images\*" />
    <MauiFont Include="Resources\Fonts\*" />
    <MauiAsset Include="Resources\Raw\**\*" LogicalName="%(RecursiveDir)%(Filename)%(Extension)" />
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

### Bundled resources

The package's build targets translate `MauiImage`, `MauiFont`, and `MauiAsset` into
Apple SDK `BundleResource` items, including files linked from outside the head project.
Images are bundled as `Contents/Resources/Images/<filename>` and fonts as
`Contents/Resources/Fonts/<filename>`. Raw assets use `LogicalName` relative to
`Contents/Resources` (or the filename when no logical name is supplied), matching
`IFileSystem.OpenAppPackageFileAsync`. Backslashes in logical names are normalized.
The MAUI mapping replaces the SDK's default resource entry for the same source file;
unrelated `BundleResource` items are unchanged.
For apps that intentionally manage all these paths using custom `BundleResource` items,
set `EnableMacOSMauiResourceMapping` to `false` to retain that mapping instead.

This bundles image sources as-is; it does not add Resizetizer resizing or SVG conversion.
Image names and font names must be unique within their respective bundle directories.
Use `LogicalName` to preserve nested raw-asset paths.

The `bundle-resources` scenario runs inside the shared `MacOS.RuntimeTests` host,
using external image/font/raw fixtures and a local raw item also matched by the
SDK glob. On macOS with Xcode and the MAUI/macOS workloads, point the runner at
freshly built core and Essentials packages and a new evidence directory:

```bash
export RUNTIME_TEST_PACKAGES="$PWD/artifacts/packages"
python3 -B platforms/MacOS/tests/MacOS.RuntimeTests/run.py \
  --scenario bundle-resources --evidence "$PWD/artifacts/resources-run"
```

The scenario's stage driver composes the common build/launch runner: historical
target reproduction, clean package-consumer rebuild, incremental build and
`dotnet publish` without an installer. Each native run verifies the produced `.app`,
MAUI image/label handlers and raw content through registered `IFileSystem`.
Logical paths include spaces, renamed assets and equal filenames in different
folders. Shipping targets perform the bundling; the harness never copies resources.
The same host also runs item-metadata assertions for normal mapping, platform guards
and opt-out. Its package mode rejects ProjectReferences.

The existing `native-runtime` CI matrix supplies the newly packed packages and uploads
`appkit-runtime-bundle-resources-default` with strict terminal results, native captures,
bundle inventory, package references and stage binlogs. All native rows wait for the
product build; optional matrix `packages` and `workloads` inputs reuse common setup.
These checks require macOS; item-mapping checks alone on Windows are not proof of
rendering.

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
