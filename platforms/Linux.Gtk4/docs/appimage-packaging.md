# AppImage Packaging

Create distributable AppImage packages from your .NET MAUI Linux GTK4 apps with a single publish command.

## Quick Start

```bash
dotnet publish -r linux-x64 --self-contained -p:CreateAppImage=true
```

This produces `YourApp-1.0.0-x86_64.AppImage` in the publish output parent directory.

## Prerequisites

- **Build host**: Linux x86_64 or ARM64 (matching your target)
- **curl**: For automatic `appimagetool` download (first build only)
- **Target system**: GTK4 installed (the AppImage checks for this at launch)

## How It Works

When `CreateAppImage=true` is set during `dotnet publish`, the build:

1. **Validates** that `ApplicationId` and a Linux `RuntimeIdentifier` are set
2. **Detects dependencies** — checks if `Platform.Maui.Linux.Gtk4.BlazorWebView` is referenced to include WebKitGTK checks
3. **Constructs an AppDir** from the publish output
4. **Generates a `.desktop` file** using MAUI project properties
5. **Generates an `AppRun` script** with system dependency checks
6. **Generates AppStream metadata** (`appdata.xml`) for software center integration
7. **Copies the app icon** from `MauiIcon` to the AppDir root
8. **Downloads `appimagetool`** (cached in `~/.maui-gtk/tools/`)
9. **Produces the final `.AppImage`** file

## MAUI Properties Used

The targets automatically map standard MAUI project properties:

| MAUI Property | AppImage Use | Fallback |
|---|---|---|
| `ApplicationId` | `.desktop` identity, WM_CLASS | **Required** |
| `ApplicationTitle` | Display name | `AssemblyName` |
| `ApplicationDisplayVersion` | Version string | `Version` → `1.0.0` |
| `AssemblyName` | Executable name, output filename | Project name |
| `MauiIcon` | AppImage icon | Optional |

## Configuration Properties

All properties are optional (except `ApplicationId` which is required by MAUI convention):

| Property | Description | Default |
|---|---|---|
| `CreateAppImage` | Enable AppImage creation | `false` |
| `AppImageToolPath` | Path to existing appimagetool binary | Auto-downloaded |
| `AppImageCategories` | `.desktop` Categories field | `Utility;` |
| `AppImageComment` | `.desktop` Comment field | `$(Description)` |
| `AppImageGenericName` | `.desktop` GenericName field | *(empty)* |
| `AppImageOutputDir` | Directory for .AppImage output | `$(PublishDir)..` |
| `AppImageFileName` | Output filename | `$(ApplicationTitle)-$(ApplicationDisplayVersion)-$(AppImageArch).AppImage` |
| `AppImageArch` | Target architecture | Auto-detected from RID |
| `SelfContainedAppImage` | Warn if not self-contained | `true` |
| `AppImageCheckDependencies` | Include runtime dependency checks in AppRun | `true` |

### Example with Custom Properties

```xml
<PropertyGroup>
  <ApplicationId>com.mycompany.myapp</ApplicationId>
  <ApplicationTitle>My App</ApplicationTitle>
  <ApplicationDisplayVersion>2.1.0</ApplicationDisplayVersion>
  <AppImageCategories>Office;ProjectManagement;</AppImageCategories>
  <AppImageComment>A project management tool built with .NET MAUI</AppImageComment>
</PropertyGroup>
```

## System Dependency Detection

The generated AppImage automatically checks for required system libraries at launch:

- **GTK4** (`libgtk-4.so`) — always checked
- **WebKitGTK 6.0** (`libwebkitgtk-6.0.so`) — checked only when `Platform.Maui.Linux.Gtk4.BlazorWebView` is referenced

If libraries are missing, the user sees a friendly error message with install commands for Ubuntu/Debian, Fedora, and Arch Linux. If `zenity` is available, a graphical error dialog is also shown.

To disable dependency checks:

```xml
<PropertyGroup>
  <AppImageCheckDependencies>false</AppImageCheckDependencies>
</PropertyGroup>
```

## Architecture Support

| RuntimeIdentifier | AppImage Architecture |
|---|---|
| `linux-x64` | `x86_64` |
| `linux-arm64` | `aarch64` |

Build for ARM64:

```bash
dotnet publish -r linux-arm64 --self-contained -p:CreateAppImage=true
```

> **Note**: You must build on the same architecture as your target. Cross-architecture AppImage creation is not supported by `appimagetool`.

## AppDir Structure

The generated AppDir has this structure:

```
MyApp.AppDir/
├── AppRun                              # Launcher script with dependency checks
├── com.mycompany.myapp.desktop         # Desktop entry
├── com.mycompany.myapp.svg             # App icon (from MauiIcon)
└── usr/
    ├── bin/
    │   ├── MyApp                       # .NET executable
    │   ├── *.dll                       # Managed assemblies
    │   ├── hicolor/                    # Icon theme data
    │   └── ...                         # Other publish output
    └── share/
        └── metainfo/
            └── com.mycompany.myapp.appdata.xml  # AppStream metadata
```

## Troubleshooting

### "CreateAppImage=true has no effect with 'dotnet build'"

AppImage creation requires `dotnet publish` to produce a deployable output directory. Use:

```bash
dotnet publish -r linux-x64 -p:CreateAppImage=true
```

### "ApplicationId is required"

Add `<ApplicationId>` to your project file:

```xml
<PropertyGroup>
  <ApplicationId>com.yourcompany.yourapp</ApplicationId>
</PropertyGroup>
```

### AppImage won't run — "FUSE not found"

Modern AppImages may require FUSE. Install it:

```bash
# Ubuntu/Debian
sudo apt install libfuse2

# Fedora
sudo dnf install fuse-libs

# Or extract and run without FUSE:
./MyApp-1.0.0-x86_64.AppImage --appimage-extract-and-run
```

### Missing GTK4 or WebKitGTK at runtime

The AppImage's startup checks will tell you exactly what's missing and how to install it. Follow the instructions shown in the terminal or dialog.

### Custom appimagetool

If you have a local copy of `appimagetool` or need a specific version:

```xml
<PropertyGroup>
  <AppImageToolPath>/path/to/appimagetool</AppImageToolPath>
</PropertyGroup>
```
