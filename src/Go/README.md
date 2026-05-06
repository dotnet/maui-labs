# Comet Go 🚀

> **Status:** experimental / alpha. APIs and wire protocol may change.

Comet Go is a single-file dev server and companion app for [Comet](../Comet/README.md). Write a `.cs` file on your laptop, scan a QR code on your phone, and see your changes live — no project setup, no IDE deploy cycle, no rebuild.

It is to Comet what **Expo Go** is to React Native or **DartPad** is to Flutter: an opinionated, fast on-device feedback loop for prototyping, demos, workshops, and bug repros.

## What's in this folder

| Project | Description |
|---------|-------------|
| [`Server/`](Server/) | `Microsoft.Maui.Go.Server` — file-watching dev server. Compiles `.cs` deltas with Roslyn and pushes them over WebSocket. Targets `net10.0`, runs on your dev machine. |
| [`CompanionApp/`](CompanionApp/) | `Microsoft.Maui.Go.CompanionApp` — the on-device runtime. A MAUI + Comet app that scans a QR code, connects to the server, applies metadata/IL/PDB deltas at runtime, and renders your view. Targets `net11.0-android;net11.0-ios;net11.0-maccatalyst` (+ Windows on Windows hosts). |
| [`Shared/`](Shared/) | `Microsoft.Maui.Go.Shared` — wire protocol shared by server and companion (binary frames over WebSocket). |
| [`spike/`](spike/) | Throwaway experiments — primarily `MetadataUpdaterSpike`, used to validate `System.Reflection.Metadata.MetadataUpdater` behaviour on each platform. |

## How it works

```
┌──────────────────┐  WebSocket   ┌───────────────────────┐
│  Microsoft.Maui  │  binary      │ Microsoft.Maui.Go     │
│  .Go.Server      │  ───────────▶│ .CompanionApp         │
│  (your laptop)   │  deltas      │ (phone / sim / mac)   │
│                  │              │                       │
│  watches *.cs    │              │  MetadataUpdater      │
│  Roslyn delta    │              │  hot-applies deltas   │
└──────────────────┘              └───────────────────────┘
```

1. The server discovers a `.csproj` in your project folder, builds the initial assembly, and starts a WebSocket listener.
2. It prints a connection URL and a QR code in the terminal.
3. The companion app scans the QR code (or accepts a manual URL), receives the initial assembly, and loads your Comet view.
4. As you edit and save `.cs` files, the server compiles a metadata/IL/PDB delta with Roslyn and streams it to all connected clients. `MetadataUpdater.ApplyUpdate` patches the running assembly in place; Comet re-renders.

The wire format is documented inline in [`Shared/GoProtocol.cs`](Shared/GoProtocol.cs).

## Getting started

Comet Go is meant to feel like Expo Go for .NET MAUI: install one tool, install one companion app, write **one `.cs` file**, and watch it run.

### 1. Install prerequisites

```bash
# .NET 11 SDK preview (Comet targets net11.0)
# Download from https://dotnet.microsoft.com/download/dotnet/11.0

# MAUI workload (only needed once, for the companion app)
dotnet workload install maui

# The maui CLI (provides `maui go`)
# NOTE: not yet published to NuGet.org. Build and install from this repo:
git clone https://github.com/dotnet/maui-labs.git
cd maui-labs
dotnet pack src/Cli/Microsoft.Maui.Cli/Microsoft.Maui.Cli.csproj -c Debug
dotnet tool install -g --add-source ./artifacts/packages/Debug/Shipping Microsoft.Maui.Cli --prerelease
```

### 2. Install the companion app on a device or simulator

The companion app is the on-device runtime that hosts your `.cs` file and applies live updates.

```bash
# iOS Simulator
dotnet build src/Go/CompanionApp/Microsoft.Maui.Go.CompanionApp.csproj \
    -t:Run -f net11.0-ios -c Debug

# Mac Catalyst (runs on your dev Mac)
dotnet build src/Go/CompanionApp/Microsoft.Maui.Go.CompanionApp.csproj \
    -t:Run -f net11.0-maccatalyst -c Debug

# Android Emulator
dotnet build src/Go/CompanionApp/Microsoft.Maui.Go.CompanionApp.csproj \
    -t:Run -f net11.0-android -c Debug
```

> Pre-built companion app installers will ship with future previews. For now, build it once from this repo.

### 3. Create your first app

```bash
maui go create Hello
```

That writes a single file, `Hello.cs`, in the current directory:

```csharp
#:package Comet

using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Hello;

public class MainPage : View
{
    readonly Reactive<int> count = new(0);

    [Body]
    View body() =>
        VStack(20,
            Text("Welcome to Hello!")
                .FontSize(28)
                .FontWeight(FontWeight.Bold)
                .Color(Colors.Orange)
                .HorizontalTextAlignment(TextAlignment.Center),

            Text(() => $"Count: {count.Value}")
                .FontSize(22)
                .HorizontalTextAlignment(TextAlignment.Center),

            Button("Tap me!", () => count.Value++)
                .Color(Colors.White)
                .Background(new SolidPaint(Colors.Orange))
                .CornerRadius(12)
                .Frame(height: 50),

            Text("Edit this file and save -- the UI updates live!")
                .FontSize(14)
                .Color(Colors.Gray)
                .HorizontalTextAlignment(TextAlignment.Center)
        )
        .Padding(new Thickness(32))
        .Alignment(Alignment.Center);
}
```

No `.csproj`. No `Program.cs`. No `MauiProgram.cs`. Just one file.

### 4. Run it

```bash
maui go Hello.cs
```

You'll see something like:

```
Comet Go server listening on ws://192.168.1.42:9995/maui-go
QR code: /var/folders/.../T/comet-go-qr.png
Watching Hello.cs ...
```

The QR code PNG opens automatically in your image viewer (Preview on macOS, default image app on Windows/Linux). Open the companion app on your phone or simulator, tap **Scan QR Code**, point at the on-screen PNG — it will auto-fill and connect. (You can also paste the `ws://` URL manually.)

### 5. Edit and save

Open `Hello.cs` in any editor — VS Code, Cursor, Rider, vim — change a string, change a color, add a button. The moment you save, the server compiles a metadata/IL/PDB delta and pushes it over WebSocket; the companion app calls `MetadataUpdater.ApplyUpdate` and Comet re-renders. No rebuild, no reinstall.

Press <kbd>Ctrl+C</kbd> in the terminal to stop the server.

### Next steps

- Read **[docs/getting-started.md](docs/getting-started.md)** for the longer walkthrough (manual companion-app connect, multiple devices, troubleshooting).
- The **`comet-go` skill** at [`.github/skills/comet-go/SKILL.md`](../../.github/skills/comet-go/SKILL.md) is a complete reference for AI coding agents working in Comet Go (controls, state, fluent styling, hot-reload constraints, common mistakes).

## File-based directives

Comet Go uses [.NET file-based programs](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/tutorials/file-based-programs). The `#:` lines at the top of a `.cs` file are pre-processor directives the dev server understands:

| Directive | Purpose |
|---|---|
| `#:package Comet` | Resolve the `Comet` NuGet package. The server uses it to find the right reference assemblies. Optionally pin a version: `#:package Comet@1.0.0-preview`. |
| `#:property PublishAot=false` | Pass an MSBuild property hint. Optional; useful when you need to override defaults. |
| `#!/usr/bin/env dotnet` | Optional shebang — lets you `chmod +x Hello.cs` and run it directly on Unix. |

The server strips these lines before compilation, preserving line numbers so error messages still point at the right place.

## Requirements

- **.NET 11 SDK preview** — `src/Go/global.json` pins the version with `rollForward: latestPrerelease`, mirroring Comet.
- **MAUI workload** — `dotnet workload install maui`. The companion app targets `net11.0-*` and pulls in the .NET 11 runtime packs through the workload.
- **A device or simulator** — iOS Simulator, Android emulator, Mac Catalyst, or Windows.
- **`DOTNET_MODIFIABLE_ASSEMBLIES=Debug`** — required so `MetadataUpdater` will accept deltas. Already wired up in `GoApp.CreateMauiApp`.

## Limitations

- **Debug runtime only.** `MetadataUpdater` requires a debug-built CoreCLR / Mono runtime. Release builds will reject deltas.
- **Edit-and-Continue rules apply.** Adding fields to existing types, changing method signatures, and editing generics often emit "rude edits" the runtime can't apply hot — the server logs these, the companion app shows an error banner, and the next save will recover.
- **One file at a time.** The single-file flow compiles one `.cs` file (multi-file `.csproj` mode is also supported but not the default).
- **No NuGet hot-add.** Adding a `#:package` line requires restarting `maui go`.

## Status and roadmap

Comet Go is alpha. The wire protocol, project layout, and CLI are subject to change. Feedback, bug reports, and PRs are welcome at [dotnet/maui-labs](https://github.com/dotnet/maui-labs).

## License

MIT — see the repo root [LICENSE](../../LICENSE).
