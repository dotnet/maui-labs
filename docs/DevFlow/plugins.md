# Writing DevFlow plugins

> **Experimental preview**: the plugin API may change between releases.

A DevFlow plugin adds tools and live event streams to the DevFlow agent running inside an app. The
`maui devflow` CLI, the MCP server and the Inspector discover them at runtime, so a library can ship a
plugin for itself without any change to DevFlow, and nothing in DevFlow knows about it.

A plugin is a class deriving from `DevFlowExtension` in the
[`Microsoft.Maui.DevFlow.Extensibility`](../../src/DevFlow/Microsoft.Maui.DevFlow.Extensibility/README.md)
package. That package has no dependency on the agent or on .NET MAUI.

## What a plugin gets

| Plugin declares | Agent serves | CLI | MCP | Inspector |
|---|---|---|---|---|
| `MapTool(...)` | `/api/v1/ext/{namespace}/{path}` | `extensions call` | `ext_{prefix}_{tool}` | Tool form generated from the JSON schema |
| `MapStream(...)` | `/ws/v1/ext/{namespace}/streams/{name}` (live) and `/api/v1/ext/{namespace}/streams/{name}` (polling) | `extensions watch`, `extensions events` | A read-only `…_events` tool | Live table with the stream's columns |
| `MapGet/Post/Put/Delete(...)` | `/api/v1/ext/{namespace}/{path}` | Advertised as a tool with a generated name, e.g. `get_status` for `GET /status` | Same, without a description or schema | Same |

Every route is advertised as a tool, as for inline extensions. Prefer `MapTool`, which lets you give the
tool a name, a description an AI agent can act on, and a parameter schema.

Write routes (POST, PUT, DELETE) not marked read-only require the DevFlow mutation lease, like the
agent's own mutations.

## A minimal plugin

```csharp
using Microsoft.Maui.DevFlow.Extensibility;

public sealed class OrdersDevFlowExtension(IOrderQueue queue)
    : DevFlowExtension("com.contoso.orders", "Order queue: pending orders and a live feed of submissions.")
{
    // MCP publishes the tools as ext_orders_*
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

        builder.MapStream("events", "Orders as they are queued, sent or rejected.", queue.Events, ["kind", "orderId", "detail"]);
    }

    protected override Task StartAsync(IDevFlowExtensionContext context, CancellationToken cancellationToken)
    {
        context.Logger.LogInformation("Orders plugin started on {Platform}", context.Platform);
        return Task.CompletedTask;
    }
}
```

The app adds it next to the agent, in debug builds:

```csharp
#if DEBUG
builder.AddMauiDevFlowAgent(options => options.AddExtension<OrdersDevFlowExtension>());
#endif
```

## Lifecycle

1. `AddExtension<T>()` records the type. Nothing is created yet.
2. When the agent starts, after the app's services are built, it creates the plugin from the app's
   service provider (constructor injection works) and calls `Configure`. Routes and streams are mapped
   before the HTTP server listens.
3. The server starts, then `StartAsync` runs in the background. Subscribe to whatever you monitor here.
4. `StopAsync` runs when the agent stops.

A plain .NET agent (`Microsoft.Maui.DevFlow.Agent.Native`) has no container. Pass an instance with
`options.AddExtension(new MyPlugin(...))`, or set `AgentOptions.Services`.

### Failures stay inside the plugin

- If the plugin cannot be created, it is listed in `extensionErrors` in the agent's capabilities.
- If `Configure` or `StartAsync` throws, the extension is reported as `faulted` with the message, its
  routes answer 503, and the MCP server stops publishing its tools.
- If a handler throws, the call returns a 500 error envelope with `reason: "extension-error"`.

None of these reach the app.

## Event streams

`DevFlowEventStream<T>` is a bounded buffer (1,000 events by default). `Publish` is thread-safe and
never blocks. When the buffer is full, the oldest event is dropped and readers are told how many they
missed. Sequence numbers start at 1 and never repeat while the app runs.

Each event is serialized when it is published (camelCase, enums as strings), so the agent and clients
only ever handle JSON. Keep payloads flat and give the stream `columns` so the CLI and Inspector can show
a readable row:

```text
$ maui devflow extensions watch org.shiny.ble events
#3      10:42:00.699  kind=scan  name=Heart Monitor  detail=rssi -57
#41     10:42:05.120  kind=notify  name=Heart Monitor  characteristic=2a37  value=0648  detail=2 bytes
```

`maui devflow extensions events <namespace> <stream> --since N` reads the buffer once, which is what
scripts and AI agents use. Every stream is also a read-only tool, so MCP clients can poll it.

## Discovery from AI agents

The MCP server publishes each plugin tool under its own name, `ext_{prefix}_{tool}`, where the prefix is
`ToolPrefix` or the last segment of the namespace. When two plugins share a prefix, both use their full
namespaces. The list refreshes after tool calls and when `maui_select_agent` picks another app. Clients
are told through `notifications/tools/list_changed`. `maui_extension_list` and `maui_extension_call`
remain for clients that do not refresh their tool list.

Write tool descriptions for an AI agent: say what the tool returns and when to use it.

## Packaging

- Reference `Microsoft.Maui.DevFlow.Extensibility` only, never the agent packages. It is small, so the
  plugin can live in your library's main package or in a separate `*.DevFlow` package.
- The agent is debug-only. Recommend that apps add the plugin inside `#if DEBUG`.
- Report what your library does; don't change its behavior from the plugin. Prefer read-only tools and
  mark anything that changes state as `Destructive`.

## Sample

[`samples/DevFlow.Sample.ShinyBle`](../../samples/DevFlow.Sample.ShinyBle/README.md) is a complete
plugin for Shiny BluetoothLE: status and peripheral tools, a live stream of scans, connections and GATT
traffic, a MAUI app that uses it, and tests that run it inside a real agent.
