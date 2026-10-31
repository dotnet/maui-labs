# DevFlow plugin sample: Shiny BluetoothLE

A complete [DevFlow plugin](../../docs/DevFlow/plugins.md) for
[Shiny BluetoothLE](https://shinylib.net/client/ble/). With it, `maui devflow`, MCP clients and the
Inspector can see what an app is doing with Bluetooth while it runs:

- **`status`**: access state, whether a scan is running, and how many peripherals the app has seen
  and connected.
- **`peripherals`**: every peripheral the app has scanned, connected to or exchanged data with, with
  its connection state, MTU, last RSSI, advertised services, and read/write/notification counts.
- **`events` stream**: scans, connects and disconnects, connection state changes, and characteristic
  reads, writes and notifications with their values in hex. Repeated advertisements from the same
  device are reported again only every 5 seconds or when the RSSI moves by 10 dBm.
- **`clear`**: empties the plugin's buffers. Bluetooth is not affected.

| Project | What it shows |
|---|---|
| `ShinyBle.DevFlowPlugin` | The plugin. It references only `Microsoft.Maui.DevFlow.Extensibility` and `Shiny.BluetoothLE`, the way a library would ship one. |
| `ShinyBle.App` | A MAUI app (Android, iOS, Mac Catalyst) that scans, connects, reads and subscribes, with the agent and plugin added in debug builds. |
| `ShinyBle.DevFlowPlugin.Tests` | Runs the plugin inside a real DevFlow agent with a fake Shiny manager, and reads it back through `AgentClient` as the CLI and MCP server do. |

## Run it

```bash
cd samples/DevFlow.Sample.ShinyBle

# Plugin tests, no device needed
dotnet test ShinyBle.DevFlowPlugin.Tests

# The app (Mac Catalyst shown; Android and iOS need a physical device for Bluetooth)
dotnet build ShinyBle.App -f net10.0-maccatalyst -t:Run
```

Tap **Scan** in the app, or drive it with DevFlow, then:

```bash
maui devflow extensions list
maui devflow extensions call org.shiny.ble status
maui devflow extensions watch org.shiny.ble events
maui devflow ui tap --automationId ScanButton
maui devflow extensions call org.shiny.ble peripherals '{"connectedOnly":true}'
```

From an MCP client, the same tools appear as `ext_ble_status`, `ext_ble_peripherals`,
`ext_ble_events` and `ext_ble_clear`.

## How it hooks into Shiny

The app registers Shiny as usual, then two lines for the plugin:

```csharp
builder.Services.AddBluetoothLE();
#if DEBUG
builder.Services.AddShinyBleDevFlowMonitor();                                // wraps IBleManager
builder.AddMauiDevFlowAgent(o => o.AddExtension<ShinyBleDevFlowExtension>()); // adds the plugin
#endif
```

`AddShinyBleDevFlowMonitor()` replaces the registered `IBleManager` with `MonitoredBleManager`, a
decorator that hands back `MonitoredPeripheral` wrappers. Everything the app does through them is
recorded by `BleActivityMonitor`, which the plugin serves.

This sample can only use Shiny's public API, so it uses a decorator. A plugin shipped inside Shiny would
use an internal diagnostics hook instead. That would also see activity from code that bypasses
`IBleManager`, and it would not hide Shiny's optional peripheral features (MTU requests, pairing,
L2CAP), which `is` checks against a wrapper cannot find.

## Why this folder has its own nuget.config

Shiny is published to nuget.org only, while the repo restores from the dnceng feeds alone. The
`nuget.config` here adds nuget.org for this sample, and `Directory.Packages.props` raises the
Microsoft.Extensions and MAUI versions to the ones Shiny 6.5.1 needs. The sample is not part of
`MauiLabs.slnx` or any CI build, so the repo's feed policy is unchanged.
