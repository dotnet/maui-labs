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

## Quick start

See **[docs/getting-started.md](docs/getting-started.md)** for a full walkthrough. The short version:

```bash
# 1. Build the server
dotnet build src/Go/Server/Microsoft.Maui.Go.Server.csproj -c Release

# 2. Build and deploy the companion app to a device or simulator
dotnet build src/Go/CompanionApp/Microsoft.Maui.Go.CompanionApp.csproj \
    -t:Run -f net11.0-maccatalyst -c Debug

# 3. Run the server against a folder containing a .cs file
dotnet run --project src/Go/Server -c Release -- ./MyApp
```

Scan the QR code with the companion app, then start editing.

## Requirements

- The **latest .NET 11 SDK preview** — `src/Go/global.json` pins the version with `rollForward: latestPrerelease`, mirroring Comet
- The MAUI workload — `dotnet workload install maui`. The companion app targets `net11.0-*` and pulls in the .NET 11 runtime packs through the workload.
- A device or simulator capable of running .NET MAUI apps (iOS Simulator, Android emulator, Mac Catalyst, or Windows)
- The companion app **must run with `DOTNET_MODIFIABLE_ASSEMBLIES=Debug`** — already wired up in `GoApp.CreateMauiApp`

## Limitations

- **Debug runtime only.** `MetadataUpdater` requires a debug-built CoreCLR / Mono runtime. Release builds will reject deltas.
- **Edit-and-Continue rules apply.** Adding fields to existing types, changing method signatures, and editing generics often emit "rude edits" the runtime can't apply hot — the server logs these and the next save will recover.
- **Single project.** The server compiles deltas for one `.csproj`; multi-project solutions and source-generated code aren't supported yet.
- **No NuGet hot-add.** Reference changes in `.csproj` require a full rebuild.

## Status and roadmap

Comet Go is alpha. The wire protocol, project layout, and CLI are subject to change. Feedback, bug reports, and PRs are welcome at [dotnet/maui-labs](https://github.com/dotnet/maui-labs).

## License

MIT — see the repo root [LICENSE](../../LICENSE).
