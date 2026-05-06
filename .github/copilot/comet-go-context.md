# Comet Go — Project Context

## What Is This?

**Comet Go** is an "Expo Go for .NET MAUI" — a frictionless getting-started experience using Comet (MVU framework). It consists of:

1. **Dev Server** (`src/Go/Server/`) — Watches `.cs` files, uses Roslyn `EmitDifference` to compile incremental deltas, pushes them to connected apps via WebSocket.
2. **Companion App** (`src/Go/CompanionApp/`) — Comet-based MAUI app that receives deltas and applies them via `MetadataUpdater.ApplyUpdate()` for live hot reload.
3. **Shared Protocol** (`src/Go/Shared/`) — WebSocket message protocol between server and app.
4. **Test App** (`test-go-app/`) — Simple Comet view used for E2E testing.

## Architecture

```
edit .cs -> FileWatcher -> Roslyn EmitDifference -> WebSocket ->
MetadataUpdater.ApplyUpdate -> new View instance -> UI updates
```

## Branch & Repo

- **Repository**: `dotnet/maui-labs`
- **Branch**: `feature/comet`
- **All work lives here** — no separate repos needed

## Platform Status

| Platform | Runtime | Hot Reload | Status |
|----------|---------|-----------|--------|
| **Mac Catalyst** | CoreCLR | Works | 3+ consecutive deltas verified |
| **iOS Simulator** | CoreCLR | Works | 3 consecutive deltas verified |
| **iOS Device (iPhone 15 Pro)** | CoreCLR | Crashes on delta | Needs investigation |
| **Android Pixel 5** | Mono | Works | Preview 3 fixed SIGSEGV |
| **Android Pixel 5** | CoreCLR | App launches | Hot reload untested |

## Key Technical Details

### MetadataUpdater.ApplyUpdate()
- Requires `DOTNET_MODIFIABLE_ASSEMBLIES=Debug` set BEFORE process start
- On iOS: set in `Info.plist` (LSEnvironment) AND `Program.cs`
- On Android: set in `MainApplication.cs` constructor
- On MacCatalyst: set in `Info.plist` (LSEnvironment)

### CometApp vs MAUI Application
- CometApp is `Comet.View` implementing `IApplication` — NOT `Microsoft.Maui.Controls.Application`
- `Application.Current` is null in CometApp context
- Standard MAUI Navigation (`PushModalAsync`) doesn't work
- Modal presentation uses native iOS `UIViewController.PresentViewController` via `ModalPresenter.cs`

### .NET 11 Preview 3 Compatibility
- Color operator removal: All `== null`/`!= null` replaced with `is null`/`is not null` across 146 Comet files (commit `170cdee`)
- Xcode version mismatch: SDK wants Xcode 26.3, current is 26.2 — bypassed with `/p:ValidateXcodeVersion=false`
- CoreCLR on Android: requires `<UseMonoRuntime>false</UseMonoRuntime>` and correct MAUI version in `eng/Versions.props`

### iOS App Icon
- Must have `XSAppIconAssets` key in `Platforms/iOS/Info.plist` — without it, `actool` never compiles the icon into `Assets.car`
- Currently uses a single combined SVG (comet shape baked into dark background)

### Build Performance
- `<NuGetAudit>false</NuGetAudit>` in companion app csproj — prevents 10+ minute restore timeouts from NuGet vulnerability scanning
- NuGet.config `<clear/>` strips global feeds; use `--configfile ~/.nuget/NuGet/NuGet.Config` for workload installs

### QR Code Scanner
- Server shows QR code by default at startup (use `--no-qr` to suppress)
- Companion app uses ZXing.Net.Maui.Controls (v0.7.4) for camera-based scanning
- Scanner presented via native iOS `PresentViewController` (not MAUI navigation)
- Dismissal via `DismissViewController` in `QrScannerPage.cs`

## Key Files

| File | Purpose |
|------|---------|
| `src/Go/CompanionApp/GoMainPage.cs` | Main Comet UI — connect screen + dynamic view host |
| `src/Go/CompanionApp/GoClient.cs` | WebSocket client with auto-reconnect |
| `src/Go/CompanionApp/GoApp.cs` | CometApp entry point, ZXing init |
| `src/Go/CompanionApp/QrScannerPage.cs` | Camera QR scanner (native MAUI page) |
| `src/Go/CompanionApp/ModalPresenter.cs` | Native iOS modal presentation helper |
| `src/Go/Server/GoDevServer.cs` | Dev server — file watcher + compiler + WebSocket |
| `src/Go/Server/Program.cs` | Server CLI entry point |
| `src/Go/Shared/GoProtocol.cs` | WebSocket message protocol |
| `src/Go/Shared/DeltaCompiler.cs` | Roslyn incremental compilation |
| `eng/Versions.props` | Central package versions (MAUI 11 Preview 3) |

## Build & Deploy Commands

### Server
```bash
dotnet run --project src/Go/Server/Microsoft.Maui.Go.Server.csproj -- test-go-app
```

### iOS Device (iPhone 15 Pro, UDID: CF4F94E3-A1C9-5617-A089-9ABB0110A09F)
```bash
dotnet build src/Go/CompanionApp/Microsoft.Maui.Go.CompanionApp.csproj \
  -f net11.0-ios -r ios-arm64 -c Debug \
  /p:CodesignKey="Apple Development" \
  /p:EnableAutomaticSigning=true \
  /p:ValidateXcodeVersion=false --no-restore

xcrun devicectl device install app --device CF4F94E3-A1C9-5617-A089-9ABB0110A09F \
  artifacts/bin/Microsoft.Maui.Go.CompanionApp/Debug/net11.0-ios/ios-arm64/Microsoft.Maui.Go.CompanionApp.app
```

### Android Device (Pixel 5, serial: 13041FDD4007MT)
```bash
dotnet build src/Go/CompanionApp/Microsoft.Maui.Go.CompanionApp.csproj \
  -f net11.0-android -c Debug --no-restore

adb -s 13041FDD4007MT reverse tcp:9000 tcp:9000
# Install via dotnet build output
```

### Mac Catalyst
```bash
dotnet build src/Go/CompanionApp/Microsoft.Maui.Go.CompanionApp.csproj \
  -f net11.0-maccatalyst -c Debug --no-restore \
  /p:ValidateXcodeVersion=false
```

## David's Preferences
- **NO EMOJIS** in code or UI text
- Naming: "Comet Go" (not "MAUI Go")
- Bundle ID for code signing: `com.simplyprofound.*`
- Current bundle: `com.simplyprofound.cometgo`
- UI theme: warm beige (#D2BCA5) matching splash screen

## Outstanding Issues

1. **iOS device crash on delta** — App crashes when MetadataUpdater.ApplyUpdate() is called on physical iPhone. Works fine on simulator. Logging added but crash needs investigation.
2. **Android CoreCLR hot reload** — App launches with CoreCLR on Pixel 5, but delta application hasn't been tested yet. Critical since Mono goes away in Preview 4.
3. **Connect screen UI polish** — Divider lines and borders may still need visibility tweaking (Comet's `RoundedBorder` may not render visibly on all elements).

## Session History (abbreviated)

1. Built E2E pipeline: file watcher -> Roslyn -> WebSocket -> MetadataUpdater
2. Verified on Mac Catalyst (3+ deltas), iOS simulator (3 deltas)
3. Android: fixed Mono SIGSEGV (Preview 3), enabled CoreCLR
4. Applied Comet branding, removed all emojis
5. Fixed Preview 3 Color operator removal (146 files)
6. Deployed to iPhone 15 Pro and Pixel 5
7. Fixed blank iOS icon (missing XSAppIconAssets in Info.plist)
8. Added QR code scanner (ZXing.Net.Maui) with native iOS presentation
9. Redesigned connect screen to match PolyPilot layout
10. Fixed contrast, borders, splash screen margins
