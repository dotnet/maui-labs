# Writing DevFlow plugins

> **Experimental preview**: the plugin API may change between releases.

A DevFlow plugin adds operations and live event streams, either inside an app or on the developer
machine. The
`maui devflow` CLI, the MCP server and the Inspector discover them at runtime, so a library can ship a
plugin for itself without any change to DevFlow, and nothing in DevFlow knows about it.

A plugin is a class deriving from `DevFlowExtension` in the
[`Microsoft.Maui.DevFlow.Extensibility`](../../src/DevFlow/Microsoft.Maui.DevFlow.Extensibility/README.md)
package. That package has no dependency on the agent or on .NET MAUI.

## What a plugin gets

| Plugin declares | Agent serves | CLI | MCP | Inspector |
|---|---|---|---|---|
| `MapOperation(...)` | `/api/v1/ext/{namespace}/operations/{name}` | `operations call` | `ext_{prefix}_{tool}` | Schema-driven tool form |
| `MapTool(...)` | `/api/v1/ext/{namespace}/{path}` | `extensions call` | `ext_{prefix}_{tool}` | Tool form generated from the JSON schema |
| `MapStream(...)` | `/ws/v1/ext/{namespace}/streams/{name}` (live) and `/api/v1/ext/{namespace}/streams/{name}` (polling) | `extensions watch`, `extensions events` | A read-only `…_events` tool | Live table with the stream's columns |
| `MapGet/Post/Put/Delete(...)` | `/api/v1/ext/{namespace}/{path}` | Advertised as a tool with a generated name, e.g. `get_status` for `GET /status` | Same, without a description or schema | Same |

Every route is advertised as a tool, as for inline extensions. Prefer `MapOperation` for new code:
handlers receive JSON parameters, cancellation, and an explicitly selected target without depending
on HTTP. Use `MapTool` when retaining a particular HTTP binding.
Route parameters such as `/items/{id}` are supplied as string JSON arguments, escaped into a
single path segment, and excluded from GET query parameters. Missing values and dot segments are
rejected before sending the request.

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

### Registering from the library

Most plugins need something installed before the app's container is built: a decorator around the
service they watch, a middleware, or a recorder that has to catch events from startup. Give the library
one registration method that installs that and registers the plugin in the app's services with
`AddDevFlowExtension<T>()`. The agent creates every plugin registered this way when it starts, so the app
does not touch the agent options:

```csharp
public static IServiceCollection AddOrdersDevFlow(this IServiceCollection services)
{
    services.AddSingleton<OrderRecorder>();    // catches events from startup
    // ...wrap or hook whatever the plugin watches here...
    services.AddDevFlowExtension<OrdersDevFlowExtension>();
    return services;
}
```

```csharp
#if DEBUG
builder.Services.AddOrdersDevFlow();
builder.AddMauiDevFlowAgent();
#endif
```

Registering the same plugin type twice, or both in services and in the agent options, creates it once.
If the app does not run the agent, the registration is never used and no plugin is created.

## Lifecycle

1. `AddExtension<T>()` or `AddDevFlowExtension<T>()` records the type. Nothing is created yet.
2. When the agent starts, after the app's services are built, it creates the plugin from the app's
   service provider (constructor injection works) and calls `Configure`. Routes and streams are mapped
   before the HTTP server listens.
3. The server starts, then `StartAsync` runs in the background. Subscribe to whatever you monitor here.
   The plugin stays `starting` until it completes; calls, polling, and subscriptions return 503.
4. `StopAsync` runs when the agent stops. New calls are refused while `stopping`.

Shutdown cancels startup and waits for it before invoking cleanup. The caller waits at most five
seconds, including synchronous plugin code and cancellation callbacks; late startup is cleaned up
when it finishes. Plugins must still honor cancellation and finish cleanup promptly. Await the
agent's `StopAsync` before disposing it when cleanup needs the UI thread. Synchronous `Dispose`
does not block that thread; cleanup may continue after it returns.

A plain .NET agent (`Microsoft.Maui.DevFlow.Agent.Native`) has no container. Pass an instance with
`options.AddExtension(new MyPlugin(...))`, or set `AgentOptions.Services`, which also makes plugins
registered with `AddDevFlowExtension` available.

### Failures stay inside the plugin

- If the plugin cannot be created, it is listed in `extensionErrors` in the agent's capabilities.
- If `Configure` or `StartAsync` throws, the extension is reported as `faulted` with the message, its
  routes answer 503, and the MCP server stops publishing its tools.
- If a handler throws, the call returns a 500 error envelope with `reason: "extension-error"`.

None of these reach the app.

## Event streams

`DevFlowEventStream<T>` is a bounded buffer (1,000 events by default). `Publish` is thread-safe and
does not wait for transport readers. Explicit `Subscribe` callbacks run synchronously and must be
quick. When the buffer is full, the oldest event is dropped and readers are told how many they
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
Names that need sanitization or truncation, or otherwise collide, receive a stable hash derived
from their full provider and tool identity; distinct declarations are never silently dropped.

## One catalog, compatible transports

`DevFlowOperationCatalog` is the common registration and invocation model. `AgentExtension` and
`DevFlowExtension` remain supported adapters into it; there is no second app-plugin registry.
`GET /api/v1/operations` discovers app providers plus `[DevFlowAction]` methods as `devflow.actions`
and migrated built-ins as `devflow.agent` (currently status and capabilities). Existing `/ext`,
action, status, and capabilities URLs still work. Built-ins keep their original response envelopes,
UI dispatch, capture headers, and lease checks. Built-in provider identities cannot be replaced;
additional `devflow.*` providers require explicit registration grants.

```bash
maui devflow operations list
maui devflow operations describe devflow.actions
maui devflow operations call devflow.agent status
maui devflow operations call devflow.actions reset '{"args":[]}'
```

Actions retain their positional `args` array and existing hot-reload discovery. More built-ins can
migrate incrementally; this PR does not replace every built-in endpoint. Legacy `extensions`
commands and capabilities discovery remain available. New clients fall back to capabilities when
an older agent does not implement `/api/v1/operations`.

### Approved transport bindings

Ordinary plugins always stay under `/api/v1/ext/{namespace}`, `/ws/v1/ext/{namespace}`, and
`ext_{prefix}_{tool}`. Setting a plugin's `ToolPrefix`, or advertising a different path, does not
grant access to non-extension names.

The application and tooling host can explicitly allow a provider to use other transport bindings:

```csharp
var bindings = new DevFlowProviderBindings(
    httpRoutePrefix: "/api/v1/diagnostics",
    webSocketRoutePrefix: "/ws/v1/diagnostics",
    mcpToolPrefix: "maui_diagnostics");

// Application owner: configure every grant before registering providers.
options.ProviderPolicy.Allow("com.contoso.diagnostics", bindings);
options.AddExtension<DiagnosticsPlugin>();

// Tooling host owner: independently configure the CLI's root service provider.
// Do not populate this policy from the connected app's discovery response.
var policy = new DevFlowProviderPolicy()
    .Allow("com.contoso.diagnostics", bindings);
services.AddSingleton(policy);
// An embedding CLI host sets its configured root provider before first use.
Microsoft.Maui.Cli.Program.Services = services.BuildServiceProvider();
```

The same plugin can now map `/status` at `/api/v1/diagnostics/status` and publish streams under
`/api/v1/diagnostics/streams/...` and `/ws/v1/diagnostics/streams/...`. Its MCP tools use
`maui_diagnostics_{tool}`. Each prefix is independent; omitted bindings retain their extension
defaults. For host-side MCP grants use the full `host:{namespace}` identity.

These are local, registration-owned grants, not flags a plugin can set in its descriptor.
The stock CLI grants only known built-ins; additional grants belong in an embedding/custom host's
root services. Agent-side approval alone does not authorize MCP or Inspector proxy breakout.
Policies freeze when providers are registered or the host starts loading plugins.

Built-in status/capabilities and action adapters already use this policy, publishing as
`maui_agent_status`, `maui_agent_capabilities`, and `maui_action_{name}`. Existing hand-written
`maui_status`, `maui_capabilities`, and `maui_invoke_action` tools remain compatible. Moving an
existing tool name into the catalog requires removing its old registration in the same change.
Conflicting approved names or routes fail explicitly instead of replacing existing handlers.
Ordinary extension naming collisions retain their stable disambiguating suffixes.

Discovery-aware stream consumers use `AgentClient.GetExtensionEventsAsync(streamDescriptor, ...)`
and `GetExtensionStreamWebSocketUrl(streamDescriptor, ...)`, so they honor approved bindings.
The namespace/name overloads retain their original `/ext` URLs for compatibility.
Naming grants never bypass mutation leases, lifecycle admission, or the host/agent execution split,
and are not a sandbox for loaded code.

## Host-side plugins

Derive from `DevFlowHostExtension` to execute ADB commands or native developer-machine APIs.
Reference only Extensibility, not an app agent package. `Configure`, `StartAsync`, streams, JSON
schemas, and annotations are shared with app plugins. Dependencies come from the host container,
not the app's services. A host embedding DevFlow can register compiled plugins using
`services.AddDevFlowHostExtension<T>()`.

For example, a plugin can declare a device-scoped operation:

```csharp
public sealed class AndroidTools : DevFlowHostExtension
{
    public AndroidTools() : base("com.contoso.android", "Local Android tools") { }

    protected override void Configure(IDevFlowExtensionBuilder builder)
    {
        builder.MapOperation("model", "Read the explicitly selected Android device's model.",
            async request =>
            {
                var start = new System.Diagnostics.ProcessStartInfo("adb")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                foreach (var arg in new[] { "-s", request.Target!.DeviceId!, "shell", "getprop", "ro.product.model" })
                    start.ArgumentList.Add(arg);
                using var process = System.Diagnostics.Process.Start(start)
                    ?? throw new InvalidOperationException("adb could not be started.");
                var stdout = process.StandardOutput.ReadToEndAsync(request.CancellationToken);
                var stderr = process.StandardError.ReadToEndAsync(request.CancellationToken);
                try
                {
                    await process.WaitForExitAsync(request.CancellationToken);
                }
                catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                    throw;
                }
                return process.ExitCode == 0
                    ? DevFlowResponse.Text((await stdout).Trim())
                    : DevFlowResponse.Error(await stderr, 502);
            }, annotations: new DevFlowToolAnnotations { ReadOnly = true },
            target: DevFlowTargetRequirement.Device);
    }
}
```

Load an explicitly trusted local assembly with `--host-plugin` (repeat for multiple assemblies).
Keep its dependency/native-library files alongside it. Loading executes arbitrary developer-machine
code; there is no sandbox. Never load a plugin based on an app's advertised path or package.

```bash
maui devflow --host-plugin /absolute/AndroidTools.dll operations list --location host
maui devflow --host-plugin /absolute/AndroidTools.dll --device SERIAL operations call host:com.contoso.android model
maui devflow --host-plugin /absolute/AndroidTools.dll --device SERIAL mcp
maui devflow --host-plugin /absolute/AndroidTools.dll --device SERIAL broker start --foreground
```

Host identity is `host:{namespace}`, so an app and host can use the same namespace without ambiguity.
`--location all` includes app and host providers; `--location host` does not resolve or contact an
app. Host-only operations and streams work without an app. Device operations require an explicit
`--device`; they never default to whichever device ADB finds first. Agent-targeted operations use
the selected app's host/port, and mutations claim the same lease as built-ins before host code runs.
These requirements are declared with `DevFlowTargetRequirement.None`, `Device`, or `Agent`.

MCP publishes trusted host tools alongside app tools. The Inspector merges the local host catalog,
preserves its token/origin/replay protections, and polls host streams locally rather than proxying
them to the app. A plugin-enabled Inspector broker must run in the foreground: detached brokers
cannot inherit loaded code. NativeAOT hosts support compiled-in service registrations, but explicitly
reject dynamic assembly loading; use the JIT CLI build for `--host-plugin`.

Plugin-owned child processes must be terminated on cancellation and native resources released in
`StopAsync`; canceling a .NET wait alone does not stop a native operation.

## NativeAOT and JSON contracts

`AgentJson` and `DevFlowJson` share one source-generated metadata registry. Primitive values,
JSON elements, and dynamic dictionary/sequence envelopes work with reflection disabled.
Register custom payload metadata before serializing or publishing it:

```csharp
DevFlowJson.RegisterContext(MyPluginJsonContext.Default);
// AgentJson.RegisterContext(...) is the compatible agent-side spelling.
```

Use web/camelCase source-generation options for plugin DTOs, with `UseStringEnumConverter = true`
when they contain enums. `DevFlowResponse.Json(value, typeInfo)`
also accepts explicit `JsonTypeInfo<T>`. A missing contract fails explicitly with JSON metadata
guidance, never an empty success response. Polling failures likewise preserve the plugin's error
instead of returning an empty event page.

## Packaging

- Ship a registration method that calls `AddDevFlowExtension<T>()`, so apps add the plugin in one line.
- Reference `Microsoft.Maui.DevFlow.Extensibility` only, never the agent packages. It is small, so the
  plugin can live in your library's main package or in a separate `*.DevFlow` package.
- The agent is debug-only. Recommend that apps add the plugin inside `#if DEBUG`.
- Report what your library does; don't change its behavior from the plugin. Prefer read-only tools and
  mark anything that changes state as `Destructive`.

## Sample

[`samples/DevFlow.Sample.ShinyBle`](../../samples/DevFlow.Sample.ShinyBle/README.md) is a complete
plugin for Shiny BluetoothLE: status and peripheral tools, a live stream of scans, connections and GATT
traffic, a MAUI app that uses it, and tests that run it inside a real agent.
