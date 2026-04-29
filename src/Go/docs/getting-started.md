# Getting started with Comet Go

This walkthrough takes you from a fresh checkout to seeing live edits on a device or simulator.

## Prerequisites

- The **latest .NET 11 SDK preview** — `src/Go/global.json` pins the version with `rollForward: latestPrerelease`
- The MAUI workload: `dotnet workload install maui` (this provides the `net11.0-*` runtime packs the companion app targets)
- A target you can deploy to:
  - macOS — Mac Catalyst (no extra setup)
  - iOS — Xcode + an iOS Simulator runtime
  - Android — Android SDK + an emulator or device with USB debugging
  - Windows — Visual Studio with the .NET MAUI workload

## 1. Build the server

The Comet Go server runs on your dev machine, watches a folder, and streams compiled deltas to connected companion apps.

```bash
dotnet build src/Go/Server/Microsoft.Maui.Go.Server.csproj -c Release
```

## 2. Deploy the companion app

The companion app is a MAUI + Comet app that loads code over the network. You install it once per device/simulator and then leave it alone — subsequent edits don't require redeploying.

Pick the target you want and run with `-t:Run` to build and launch in one step.

```bash
# Mac Catalyst
dotnet build src/Go/CompanionApp/Microsoft.Maui.Go.CompanionApp.csproj \
    -t:Run -f net11.0-maccatalyst -c Debug

# iOS Simulator
dotnet build src/Go/CompanionApp/Microsoft.Maui.Go.CompanionApp.csproj \
    -t:Run -f net11.0-ios -c Debug

# Android emulator
dotnet build src/Go/CompanionApp/Microsoft.Maui.Go.CompanionApp.csproj \
    -t:Run -f net11.0-android -c Debug
```

> **Build `Debug`, not `Release`.** Comet Go relies on `MetadataUpdater.ApplyUpdate`, which only works with the runtime in modifiable-assemblies mode. The companion app sets `DOTNET_MODIFIABLE_ASSEMBLIES=Debug` at startup, but the assemblies themselves still have to be built with debug metadata.

The first launch shows a "Scan QR" screen.

## 3. Create a project to serve

Make a folder with one `.csproj` and at least one `.cs` file. The simplest possible app is a single Comet view:

```bash
mkdir HelloGo && cd HelloGo
```

`HelloGo.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.Comet" Version="*-*" />
  </ItemGroup>
</Project>
```

`App.cs`:

```csharp
using Comet;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

public class HelloState
{
    public int Taps { get; set; }
}

public class App : Component<HelloState>
{
    public override View Render() => VStack(
        Text("Hello, Comet Go!").FontSize(28).Color(Colors.DodgerBlue),
        Text(() => $"You tapped {State.Taps} times"),
        Button("Tap me", () => SetState(s => s.Taps++))
    );
}
```

## 4. Run the server

Point the server at the folder you just created.

```bash
dotnet run --project src/Go/Server -c Release -- ./HelloGo
```

You'll see something like:

```
[Go] Watching: /Users/you/HelloGo/HelloGo.csproj
[Go] Listening on ws://192.168.1.42:9000
[Go] Scan to connect:
   ░░██████░░░░██░░...   (QR code)
```

Useful flags:

| Flag | Default | Notes |
|------|---------|-------|
| `--port <n>` | `9000` | Override the WebSocket port |
| `--no-qr` | off | Skip the QR rendering (CI / non-TTY) |

## 5. Connect the companion

Tap **Scan QR** in the companion app and point it at the terminal. (On Mac Catalyst or Windows, where there's no camera, paste the URL manually.) Within a second or two you should see your `HelloGo` app rendered on the device.

## 6. Edit, save, watch it update

Open `App.cs`, change a string or a colour, and save. The server logs `[Go] delta #N (xxx bytes)` and the running app updates without losing state. The tap counter keeps its value.

## What kinds of edits work?

| Edit | Hot-applies? |
|------|--------------|
| Change a string literal | ✅ |
| Change a number / colour / margin | ✅ |
| Edit method body | ✅ |
| Add a new method | ✅ |
| Add a new field to an existing class | ⚠️ Sometimes — depends on the runtime |
| Change a method signature | ❌ Rude edit — restart the app |
| Add a new type | ⚠️ Mostly works |
| Change a generic | ❌ Usually rude |

The server logs rude edits as warnings; the app keeps running on the previous version until your next successful save.

## Troubleshooting

**"Couldn't apply update"** in the companion app log → it was a rude edit. Save again with a smaller change, or restart the app to pick up the new full assembly.

**Companion app fails to connect** → check that your phone and laptop are on the same Wi-Fi network. Corporate / guest networks often block peer-to-peer traffic.

**`MetadataUpdater.IsSupported` is `false`** → the app was built `Release`, or `DOTNET_MODIFIABLE_ASSEMBLIES` wasn't set early enough. Rebuild `Debug` and confirm the env var is set before `MauiApp.CreateBuilder()`.

**Android emulator can't reach the server** → the QR code encodes your machine's LAN IP, which works for physical devices and bridged emulators. The default Android emulator's NAT can't route arbitrary LAN IPs, so paste the URL manually and replace the IP with `10.0.2.2` (the host alias).

## Next steps

- Read **[../README.md](../README.md)** for the architecture overview and wire protocol.
- Browse [`spike/MetadataUpdaterSpike/`](../spike/MetadataUpdaterSpike/) to see the platform-specific runtime checks the project relies on.
- File issues and ideas at [dotnet/maui-labs](https://github.com/dotnet/maui-labs/issues).
