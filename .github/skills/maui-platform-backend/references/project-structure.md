# Project Structure & Conventions

The canonical project structure for new MAUI backends in the `dotnet/maui-labs` repository, based on the Linux.Gtk4 backend at `platforms/Linux.Gtk4/` on `main`.

---

## Location & Naming

### Where backends live
```
maui-labs/
├── src/                    # Existing products (DevFlow, etc.)
├── platforms/              # ← ALL new backend platforms go here
│   ├── Linux.Gtk4/         # First canonical backend
│   ├── MacOS.AppKit/       # Future macOS backend
│   ├── [Platform.Name]/    # Your new backend
│   └── references/         # Shared reference documentation
│       └── PLATFORM_BACKEND_IMPLEMENTATION.md
└── ...
```

### Naming conventions
| Convention | Pattern | Example |
|-----------|---------|---------|
| Directory name | `[Platform.Name]` | `Linux.Gtk4`, `MacOS.AppKit` |
| Namespace | `Microsoft.Maui.Platforms.[Platform.Name]` | `Microsoft.Maui.Platforms.Linux.Gtk4` |
| Assembly name | `Microsoft.Maui.Platforms.[Platform.Name]` | `Microsoft.Maui.Platforms.Linux.Gtk4` |
| NuGet package ID | `Microsoft.Maui.Platforms.[Platform.Name]` | `Microsoft.Maui.Platforms.Linux.Gtk4` |
| Host builder extension | `UseMauiApp[ShortPlatform]<TApp>()` | `UseMauiAppLinuxGtk4<TApp>()` |
| MauiApplication class | `[Prefix]MauiApplication` | `GtkMauiApplication` |

> ⚠️ **NOT** `Platform.Maui.[Platform]` — that was the old standalone repo naming.

---

## Self-Contained Backend Structure

Each backend is a **repo-in-a-repo** — self-contained with its own build configuration:

```
platforms/[Platform.Name]/
├── .gitignore
├── Directory.Build.props           # Imports ../../Directory.Build.props, sets MauiVersion, version
├── Directory.Build.targets         # Imports ../../Directory.Build.targets
├── Directory.Packages.props        # DISABLES central package management
├── LICENSE
├── [Platform.Name].slnx            # Platform-local solution file
├── README.md                       # Getting started, features, prerequisites
├── docs/
│   ├── handler-audit-status.md     # Handler implementation parity tracking
│   ├── screenshots/                # Visual proof of working controls
│   └── (packaging guides, etc.)
├── samples/
│   └── [Platform.Name].Sample/     # Comprehensive demo app
│       └── [Platform.Name].Sample.csproj
├── src/
│   ├── [Platform.Name]/                        # Core handlers library
│   │   ├── Handlers/                           # One .cs file per control handler
│   │   │   ├── ActivityIndicatorHandler.cs
│   │   │   ├── ApplicationHandler.cs
│   │   │   ├── ButtonHandler.cs
│   │   │   ├── ContentPageHandler.cs
│   │   │   ├── EntryHandler.cs
│   │   │   ├── LabelHandler.cs
│   │   │   ├── LayoutHandler.cs
│   │   │   ├── WindowHandler.cs
│   │   │   └── ... (all control handlers)
│   │   ├── Hosting/
│   │   │   └── AppHostBuilderExtensions.cs     # UseMauiApp[Platform]<TApp>()
│   │   ├── Platform/
│   │   │   ├── [Prefix]AlertManager.cs         # DispatchProxy alert workaround
│   │   │   ├── [Prefix]DispatcherProvider.cs   # IDispatcher + IDispatcherProvider
│   │   │   ├── [Prefix]FontServices.cs         # IFontManager + IEmbeddedFontLoader
│   │   │   ├── [Prefix]FontNamedSizeService.cs # NamedSize → point sizes
│   │   │   ├── [Prefix]GestureExtensions.cs    # Native gesture → MAUI gesture mapping
│   │   │   ├── [Prefix]LayoutPanel.cs          # Container view for child management
│   │   │   ├── [Prefix]MauiApplication.cs      # IPlatformApplication + native app
│   │   │   ├── [Prefix]MauiContext.cs          # IMauiContext implementation
│   │   │   ├── [Prefix]PlatformTicker.cs       # ITicker for animations (~60fps)
│   │   │   ├── [Prefix]ThemeManager.cs         # Light/Dark theme detection
│   │   │   └── WindowRootViewContainer.cs      # Root view container
│   │   ├── Graphics/                           # Platform 2D graphics (e.g., Cairo)
│   │   ├── LifecycleEvents/                    # Platform lifecycle hooks
│   │   ├── buildTransitive/                    # MSBuild .targets for NuGet consumers
│   │   │   └── [Platform.Name].targets
│   │   └── [Platform.Name].csproj
│   │
│   ├── [Platform.Name].Essentials/              # MAUI Essentials implementations
│   │   ├── Accessibility/                       # SemanticScreenReader
│   │   ├── AppModel/                            # AppInfo, AppActions, Launcher, Browser, Map
│   │   ├── Authentication/                      # WebAuthenticator
│   │   ├── Communication/                       # Email, PhoneDialer, SMS, Contacts
│   │   ├── DataTransfer/                        # Clipboard, Share
│   │   ├── Devices/                             # Battery, DeviceDisplay, DeviceInfo, Haptic, Vibration
│   │   ├── Hosting/                             # EssentialsExtensions.cs (DI + SetDefault reflection)
│   │   ├── Media/                               # MediaPicker, TextToSpeech, Screenshot
│   │   ├── Networking/                          # Connectivity
│   │   ├── Sensors/                             # Accelerometer, Gyroscope, etc.
│   │   ├── Storage/                             # FileSystem, FilePicker, Preferences, SecureStorage
│   │   └── [Platform.Name].Essentials.csproj
│   │
│   └── [Platform.Name].BlazorWebView/           # Blazor Hybrid (optional)
│       ├── BlazorWebViewHandler.cs
│       ├── [Prefix]BlazorWebView.cs             # Native WebView wrapper
│       ├── [Prefix]WebViewManager.cs            # WebView manager
│       ├── BlazorWebViewExtensions.cs           # DI registration
│       ├── RootComponent.cs
│       └── [Platform.Name].BlazorWebView.csproj
│
└── templates/                                    # dotnet new templates
    ├── [Platform.Name].Templates.csproj
    └── maui-[platform]-app/                      # Template for new apps
```

---

## Build Configuration

### Directory.Build.props
```xml
<Project>
  <Import Project="../../Directory.Build.props" />

  <PropertyGroup>
    <LangVersion>preview</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <MauiVersion>10.0.41</MauiVersion>
    <EnableMauiDevFlow Condition="'$(EnableMauiDevFlow)' == ''">false</EnableMauiDevFlow>
  </PropertyGroup>

  <PropertyGroup Condition="'$(EnableMauiDevFlow)' == 'true'">
    <DefineConstants>$(DefineConstants);MAUIDEVFLOW</DefineConstants>
  </PropertyGroup>

  <PropertyGroup>
    <VersionPrefix>0.1.0</VersionPrefix>
    <Version>$(VersionPrefix)</Version>
    <Authors>Microsoft</Authors>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/dotnet/maui-labs/tree/main/platforms/[Platform.Name]</PackageProjectUrl>
    <RepositoryUrl>https://github.com/dotnet/maui-labs.git</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
</Project>
```

### Directory.Packages.props
```xml
<Project>
  <PropertyGroup>
    <!-- Disable repo-level central package management — backends manage their own versions -->
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>false</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
</Project>
```

### Core .csproj pattern
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Microsoft.Maui.Platforms.[Platform.Name]</RootNamespace>
    <AssemblyName>Microsoft.Maui.Platforms.[Platform.Name]</AssemblyName>
    <PackageId>Microsoft.Maui.Platforms.[Platform.Name]</PackageId>
    <Description>A .NET MAUI backend for [Platform] using [UI Toolkit].</Description>
    <IsPackable>true</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.Controls" Version="$(MauiVersion)" />
    <!-- Add platform-specific binding packages here -->
  </ItemGroup>

  <ItemGroup>
    <None Include="../../README.md" Pack="true" PackagePath="" />
    <None Include="buildTransitive/**" Pack="true" PackagePath="buildTransitive/" />
  </ItemGroup>
</Project>
```

### TFM
All backends use `net10.0` (plain .NET — no custom platform TFM). Apps use the **"head project" pattern**: a separate `.csproj` that references the MAUI app's shared code.

---

## NuGet Packages (4 per backend)

| Package | Purpose |
|---------|---------|
| `Microsoft.Maui.Platforms.[Platform.Name]` | Core handlers, hosting, platform services |
| `Microsoft.Maui.Platforms.[Platform.Name].Essentials` | MAUI Essentials (clipboard, preferences, etc.) |
| `Microsoft.Maui.Platforms.[Platform.Name].BlazorWebView` | Blazor Hybrid support |
| `Microsoft.Maui.Platforms.[Platform.Name].Templates` | `dotnet new` project templates |

---

## Implementation Priority Order

### Phase 1: Foundation (Get a window with "Hello World")
1. Core infrastructure (base handler, dispatcher, context, host builder)
2. Application + Window handlers
3. ContentPage handler
4. LayoutHandler (VerticalStack, HorizontalStack)
5. Label handler
6. Basic essentials (AppInfo, DeviceInfo, FileSystem, Preferences)

### Phase 2: Basic Controls (Interactive app)
7. Button, Entry, Editor handlers
8. Image handler (FileImageSource first)
9. Switch, CheckBox, Slider, ProgressBar, ActivityIndicator
10. ScrollView + Border handlers
11. Font management (IFontManager, IEmbeddedFontLoader)
12. Gesture recognizers (Tap, Pan)

### Phase 3: Navigation (Multi-page app)
13. NavigationPage (push/pop)
14. TabbedPage, FlyoutPage
15. Alert/Dialog system (DispatchProxy workaround)
16. Animations (ITicker)

### Phase 4: Advanced Controls
17. CollectionView / ListView (virtualization)
18. Picker, DatePicker, TimePicker, SearchBar
19. RadioButton, Stepper, CarouselView, IndicatorView
20. GraphicsView + ShapeViewHandler

### Phase 5: Rich Features
21. Shell handler
22. WebView + BlazorWebView
23. MenuBar (desktop)
24. FormattedText (Label spans)
25. Remaining essentials, gestures, image sources
26. App Theme / Dark Mode, lifecycle events
27. Build targets / Resizetizer integration
28. `dotnet new` templates

---

## Building & Running

```bash
# Build the platform
dotnet build platforms/[Platform.Name]/[Platform.Name].slnx

# Run the sample
dotnet run --project platforms/[Platform.Name]/samples/[Platform.Name].Sample/

# Build from repo root
dotnet build platforms/[Platform.Name]/[Platform.Name].slnx
```

---

## MAUI DevFlow Integration

Optional — enable with `EnableMauiDevFlow=true`:

```bash
dotnet run --project platforms/[Platform.Name]/samples/[Platform.Name].Sample/ -p:EnableMauiDevFlow=true
```

See [devflow-integration.md](devflow-integration.md) for full setup details.
