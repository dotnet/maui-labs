# Debian (.deb) Packaging

Create `.deb` packages from your .NET MAUI Linux GTK4 apps for distribution on Debian, Ubuntu, and derivatives.

## Quick Start

```bash
dotnet publish -r linux-x64 --self-contained -p:CreateDeb=true
```

This produces `myapp_1.0.0_amd64.deb` in the publish output parent directory.

## Prerequisites

- **Build host**: Linux with `dpkg-deb` installed (available on Debian/Ubuntu by default, installable on other distros)
- **Target system**: Debian, Ubuntu, or derivative with GTK4 installed

## How It Works

When `CreateDeb=true` is set during `dotnet publish`, the build:

1. **Validates** that `ApplicationId` and a Linux `RuntimeIdentifier` are set
2. **Detects dependencies** — checks if `Platform.Maui.Linux.Gtk4.BlazorWebView` is referenced to add `libwebkitgtk-6.0-4` to the `Depends` field
3. **Creates the staging directory** with standard Debian package layout
4. **Generates `DEBIAN/control`** from MAUI project properties
5. **Generates a launcher script** in `/usr/bin/`
6. **Generates a `.desktop` file** in `/usr/share/applications/`
7. **Copies the app icon** to `/usr/share/icons/hicolor/scalable/apps/`
8. **Runs `dpkg-deb --build`** to produce the final `.deb` file

## MAUI Properties Used

| MAUI Property | Deb Use | Fallback |
|---|---|---|
| `ApplicationId` | Package name derivation, `.desktop` identity | **Required** |
| `ApplicationTitle` | `.desktop` Name, description fallback | `AssemblyName` |
| `ApplicationDisplayVersion` | Package version | `Version` → `1.0.0` |
| `AssemblyName` | Binary name | Project name |
| `MauiIcon` | Package icon | Optional |
| `Description` | Package description, `.desktop` Comment | `ApplicationTitle` |
| `Authors` | Maintainer fallback | `MAUI Developer` |
| `PackageProjectUrl` | Homepage field | Optional |

## Configuration Properties

| Property | Description | Default |
|---|---|---|
| `CreateDeb` | Enable .deb creation | `false` |
| `DebPackageName` | Package name | Last segment of `ApplicationId` (lowercase) |
| `DebSection` | Package section | `utils` |
| `DebPriority` | Package priority | `optional` |
| `DebMaintainer` | Maintainer field | `$(Authors)` |
| `DebDepends` | Dependency list | Auto-detected (GTK4 + optional WebKitGTK) |
| `DebHomepage` | Homepage URL | `$(PackageProjectUrl)` |
| `DebDescription` | Package description | `$(Description)` |
| `DebCategories` | `.desktop` Categories | `Utility;` |
| `DebArch` | Architecture | Auto-detected from RID |
| `DebOutputDir` | Output directory | `$(PublishDir)..` |
| `DebFileName` | Output filename | `$(DebPackageName)_$(Version)_$(DebArch).deb` |

### Example with Custom Properties

```xml
<PropertyGroup>
  <ApplicationId>com.mycompany.myapp</ApplicationId>
  <ApplicationTitle>My App</ApplicationTitle>
  <ApplicationDisplayVersion>2.1.0</ApplicationDisplayVersion>
  <DebSection>office</DebSection>
  <DebDepends>libgtk-4-1, libwebkitgtk-6.0-4, libsqlite3-0</DebDepends>
  <DebMaintainer>My Name &lt;me@example.com&gt;</DebMaintainer>
</PropertyGroup>
```

## Automatic Dependency Detection

The `Depends` field in `DEBIAN/control` is automatically populated:

- **GTK4** (`libgtk-4-1`) — always included
- **WebKitGTK** (`libwebkitgtk-6.0-4`) — included when `Platform.Maui.Linux.Gtk4.BlazorWebView` is referenced

Override with `DebDepends` if your app has additional system dependencies.

## Installed File Layout

```
/usr/bin/myapp                              # Launcher script
/usr/lib/myapp/                             # Application files
    ├── MyApp                               # .NET executable
    ├── *.dll                               # Managed assemblies
    └── ...
/usr/share/applications/com.mycompany.myapp.desktop
/usr/share/icons/hicolor/scalable/apps/com.mycompany.myapp.svg
```

## Architecture Support

| RuntimeIdentifier | Debian Architecture |
|---|---|
| `linux-x64` | `amd64` |
| `linux-arm64` | `arm64` |

## Installing and Removing

```bash
# Install
sudo dpkg -i myapp_1.0.0_amd64.deb

# Fix any missing dependencies
sudo apt-get install -f

# Remove
sudo dpkg -r myapp
```

## Troubleshooting

### "dpkg-deb: command not found"

Install dpkg tools:

```bash
# Fedora/RHEL (for cross-building)
sudo dnf install dpkg

# Arch
sudo pacman -S dpkg
```

### Package installs but app doesn't launch

Check that all dependencies are installed: `sudo apt-get install -f`

### Custom dependencies

If your app needs additional system libraries, set `DebDepends`:

```xml
<PropertyGroup>
  <DebDepends>libgtk-4-1, libcurl4, libsqlite3-0</DebDepends>
</PropertyGroup>
```
