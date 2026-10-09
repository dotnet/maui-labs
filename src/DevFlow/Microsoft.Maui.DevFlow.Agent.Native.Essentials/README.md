# Microsoft.Maui.DevFlow.Agent.Native.Essentials

Optional add-on for [`Microsoft.Maui.DevFlow.Agent.Native`](https://www.nuget.org/packages/Microsoft.Maui.DevFlow.Agent.Native/)
that lights up the device, storage and sensor endpoints in a **plain .NET app** using
.NET MAUI Essentials.

Without it those endpoints answer `501 not_supported`. With it they answer exactly as they do in a
MAUI app — the implementations are shared source, so the two agents cannot drift.

> ⚠️ **Experimental** — APIs may change between releases. Not covered by the Microsoft Support Policy.

## This does not make your app a MAUI app

It references `Microsoft.Maui.Essentials`, which needs the MAUI workload installed
(`dotnet workload install maui`) but pulls in **no** `Microsoft.Maui.Controls` and needs no
`MauiApp` host. Your app stays a plain .NET Android / iOS / Mac Catalyst / macOS app.

## Install

```xml
<PackageReference Include="Microsoft.Maui.DevFlow.Agent.Native.Essentials" />
```

You do not also need `Microsoft.Maui.DevFlow.Agent.Native` — this package brings it.

## Quick start

Use `EssentialsDevFlowAgent` in place of `DevFlowAgent`. The substitution is explicit rather than
discovered by reflection, so it survives the trimming and AOT that plain .NET iOS apps rely on.

**iOS / Mac Catalyst** — `AppDelegate.FinishedLaunching`.
**macOS** — `AppDelegate.DidFinishLaunching`:

```csharp
using Microsoft.Maui.DevFlow.Agent.Native.Essentials;

#if DEBUG
EssentialsDevFlowAgent.Start();
#endif
```

**Android** — `MainActivity.OnCreate`. Essentials requires `Platform.Init` before any of its APIs
are used:

```csharp
using Microsoft.Maui.DevFlow.Agent.Native.Essentials;

protected override void OnCreate(Bundle? savedInstanceState)
{
    base.OnCreate(savedInstanceState);
    SetContentView(Resource.Layout.activity_main);
#if DEBUG
    Microsoft.Maui.ApplicationModel.Platform.Init(this, savedInstanceState);
    this.StartDevFlowAgentWithEssentials();
#endif
}
```

For the permission and geolocation endpoints, also forward permission results — this is Essentials'
own requirement, not DevFlow's:

```csharp
public override void OnRequestPermissionsResult(
    int requestCode, string[] permissions, Android.Content.PM.Permission[] grantResults)
{
    Microsoft.Maui.ApplicationModel.Platform.OnRequestPermissionsResult(
        requestCode, permissions, grantResults);
    base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
}
```

## What it turns on

| Endpoint group | Backed by |
|---|---|
| Preferences read/write/delete/clear | `Microsoft.Maui.Storage.Preferences` |
| Secure storage read/write/delete/clear | `Microsoft.Maui.Storage.SecureStorage` |
| App data file browsing, upload, download | Essentials app-data paths |
| Device info, display info | `DeviceInfo`, `DeviceDisplay` |
| Battery, connectivity | `Battery`, `Connectivity` |
| Version tracking | `VersionTracking` |
| Permissions, geolocation | `Permissions`, `Geolocation` |
| Sensors (accelerometer, gyroscope, magnetometer, compass, orientation, barometer) | Essentials sensors |

App theme stays unsupported: MAUI's theme endpoints read `Application.RequestedTheme`, which is a
Controls concept with no Essentials equivalent.

## Platform support

`net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-macos`.

## Links

- [Source and docs](https://github.com/dotnet/maui-labs/tree/main/src/DevFlow)
- [Native samples](https://github.com/dotnet/maui-labs/tree/main/samples/DevFlow.Sample.Native)
- [HTTP/WebSocket protocol spec](https://github.com/dotnet/maui-labs/tree/main/docs/DevFlow/spec)
