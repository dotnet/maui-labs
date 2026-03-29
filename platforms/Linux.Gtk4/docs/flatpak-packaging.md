# Flatpak Packaging

Create Flatpak packages from your .NET MAUI Linux GTK4 apps for sandboxed distribution across Linux distributions.

## Quick Start

```bash
dotnet publish -r linux-x64 --self-contained -p:CreateFlatpak=true
```

This produces `com.mycompany.myapp.flatpak` in the publish output parent directory.

## Prerequisites

- **flatpak** and **flatpak-builder** installed on the build host
- **GNOME Platform runtime** installed:

```bash
flatpak install flathub org.gnome.Platform//47
flatpak install flathub org.gnome.Sdk//47
```

## How It Works

When `CreateFlatpak=true` is set during `dotnet publish`, the build:

1. **Validates** that `ApplicationId` and a Linux `RuntimeIdentifier` are set
2. **Detects dependencies** — checks if `Platform.Maui.Linux.Gtk4.BlazorWebView` is referenced
3. **Copies publish output** to a Flatpak work directory
4. **Generates a flatpak-builder manifest** (`$(ApplicationId).json`) using the `simple` buildsystem
5. **Generates `.desktop` and AppStream metadata** files
6. **Copies the app icon** from `MauiIcon`
7. **Runs `flatpak-builder`** to build the app into a local repository
8. **Runs `flatpak build-bundle`** to produce a single `.flatpak` bundle file

## MAUI Properties Used

| MAUI Property | Flatpak Use | Fallback |
|---|---|---|
| `ApplicationId` | Flatpak app ID, `.desktop` identity | **Required** |
| `ApplicationTitle` | Display name | `AssemblyName` |
| `ApplicationDisplayVersion` | Version | `Version` → `1.0.0` |
| `AssemblyName` | Binary name / command | Project name |
| `MauiIcon` | App icon | Optional |
| `Description` | `.desktop` Comment, AppStream summary | `ApplicationTitle` |

## Configuration Properties

| Property | Description | Default |
|---|---|---|
| `CreateFlatpak` | Enable Flatpak creation | `false` |
| `FlatpakRuntime` | Flatpak runtime | `org.gnome.Platform` |
| `FlatpakSdk` | Flatpak SDK | `org.gnome.Sdk` |
| `FlatpakRuntimeVersion` | Runtime version | `47` |
| `FlatpakFinishArgs` | Sandbox permissions | Wayland, X11, IPC, DRI, network |
| `FlatpakCategories` | `.desktop` Categories | `Utility;` |
| `FlatpakDescription` | App description | `$(Description)` |
| `FlatpakOutputDir` | Output directory | `$(PublishDir)..` |
| `FlatpakFileName` | Output filename | `$(ApplicationId).flatpak` |

### Example with Custom Properties

```xml
<PropertyGroup>
  <ApplicationId>com.mycompany.myapp</ApplicationId>
  <ApplicationTitle>My App</ApplicationTitle>
  <FlatpakRuntimeVersion>47</FlatpakRuntimeVersion>
  <FlatpakFinishArgs>--socket=wayland --socket=fallback-x11 --share=ipc --device=dri --share=network --filesystem=home</FlatpakFinishArgs>
</PropertyGroup>
```

## Sandbox Permissions (finish-args)

The default `FlatpakFinishArgs` grants:

| Permission | Flag | Reason |
|---|---|---|
| Wayland display | `--socket=wayland` | GTK4 primary display |
| X11 fallback | `--socket=fallback-x11` | Compatibility |
| IPC | `--share=ipc` | Required for X11 shared memory |
| GPU | `--device=dri` | Hardware acceleration |
| Network | `--share=network` | Web content (WebView, etc.) |
| Documents (read-only) | `--filesystem=xdg-documents:ro` | File access |

Customize by setting `FlatpakFinishArgs` in your project file.

## Automatic Dependency Detection

When `Platform.Maui.Linux.Gtk4.BlazorWebView` is referenced:
- AppStream metadata includes a WebKitGTK `<recommends>` entry
- The GNOME Platform runtime already includes WebKitGTK, so no additional Flatpak permissions are needed

## Installing and Running

```bash
# Install the bundle
flatpak install com.mycompany.myapp.flatpak

# Run
flatpak run com.mycompany.myapp

# Uninstall
flatpak uninstall com.mycompany.myapp
```

## Troubleshooting

### "flatpak-builder: command not found"

```bash
# Ubuntu/Debian
sudo apt install flatpak-builder

# Fedora
sudo dnf install flatpak-builder

# Arch
sudo pacman -S flatpak-builder
```

### "Runtime org.gnome.Platform not installed"

```bash
flatpak remote-add --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo
flatpak install flathub org.gnome.Platform//47
flatpak install flathub org.gnome.Sdk//47
```

### App can't access files

Adjust `FlatpakFinishArgs` to grant filesystem access:

```xml
<PropertyGroup>
  <FlatpakFinishArgs>--socket=wayland --socket=fallback-x11 --share=ipc --device=dri --share=network --filesystem=home</FlatpakFinishArgs>
</PropertyGroup>
```

### Generated manifest location

The manifest is generated at `obj/Debug/net10.0/Flatpak/$(ApplicationId).json`. You can inspect and customize it for advanced use cases, or use it directly with `flatpak-builder` outside of the MSBuild workflow.
