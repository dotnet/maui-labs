# Microsoft.Maui.DevFlow.Extensibility

> **Experimental preview**: this API may change between releases.

Build plugins for the .NET MAUI DevFlow agent. A plugin adds tools and live event streams to an app's
DevFlow agent. The `maui devflow` CLI, MCP clients (AI agents) and the DevFlow Inspector discover them
at runtime, with no plugin-specific code anywhere in DevFlow.

This package is the plugin contract only. It does not depend on the agent or on .NET MAUI, so a library
can ship a plugin for itself without pulling DevFlow into its consumers' release builds.

## Features

- **Tools**: routes described with a JSON schema and behavior hints. The CLI calls them with
  `maui devflow extensions call`, and MCP clients see each one as its own tool (`ext_{prefix}_{tool}`).
- **Event streams**: a bounded, thread-safe buffer the plugin publishes into. Clients follow it live
  (`maui devflow extensions watch`), poll it with a cursor, or read it as a tool.
- **App services**: the agent creates plugins from the app's service provider.
- **Contained failures**: a plugin that fails to start is reported as faulted instead of crashing the app.

## Platform support

Any app that runs the DevFlow agent: .NET MAUI on Android, iOS, Mac Catalyst, Windows, macOS (AppKit)
and Linux (GTK), and plain .NET Android, iOS, Mac Catalyst and macOS apps through
`Microsoft.Maui.DevFlow.Agent.Native`.

## Quick start

```csharp
using Microsoft.Maui.DevFlow.Extensibility;

public sealed class OrdersDevFlowExtension(IOrderQueue queue)
    : DevFlowExtension("com.contoso.orders", "Order queue: pending orders and a live feed of submissions.")
{
    public override string? ToolPrefix => "orders";

    protected override void Configure(IDevFlowExtensionBuilder builder)
    {
        builder.MapTool(
            "pending",
            "Orders waiting to be sent, oldest first.",
            "GET",
            "/pending",
            _ => Task.FromResult(DevFlowResponse.Json(queue.Pending)),
            annotations: new DevFlowToolAnnotations { ReadOnly = true });

        builder.MapStream("events", "Orders as they are queued, sent or rejected.", queue.Events, ["kind", "orderId"]);
    }
}
```

The app adds the plugin to the agent in debug builds:

```csharp
#if DEBUG
builder.AddMauiDevFlowAgent(options => options.AddExtension<OrdersDevFlowExtension>());
#endif
```

Then, with the app running:

```bash
maui devflow extensions list
maui devflow extensions call com.contoso.orders pending
maui devflow extensions watch com.contoso.orders events
```

## Packages

| Package | Use |
|---|---|
| `Microsoft.Maui.DevFlow.Extensibility` | Reference from the plugin |
| `Microsoft.Maui.DevFlow.Agent` (or `.Agent.Native`, `.Agent.Gtk`, `.Agent.WPF`) | Reference from the app, which hosts the plugin |

## Requirements

- .NET 10
- An app running a DevFlow agent with plugin support

## Learn more

- [Writing DevFlow plugins](https://github.com/dotnet/maui-labs/blob/main/docs/DevFlow/plugins.md)
- [Shiny BluetoothLE plugin sample](https://github.com/dotnet/maui-labs/tree/main/samples/DevFlow.Sample.ShinyBle)
