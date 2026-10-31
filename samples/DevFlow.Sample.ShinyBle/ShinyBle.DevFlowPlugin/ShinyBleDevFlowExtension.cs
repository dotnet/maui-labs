using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.DevFlow.Extensibility;
using Shiny.BluetoothLE;

namespace ShinyBle.DevFlowPlugin;

/// <summary>
/// DevFlow plugin for Shiny BluetoothLE. The <c>maui devflow</c> CLI, MCP clients and the inspector see the
/// adapter, the peripherals the app has used, and a live feed of scans, connections and GATT traffic.
/// </summary>
/// <remarks>
/// Register both halves in a debug build:
/// <code>
/// builder.Services.AddBluetoothLE();
/// builder.Services.AddShinyBleDevFlowMonitor();
/// builder.AddMauiDevFlowAgent(o => o.AddExtension&lt;ShinyBleDevFlowExtension&gt;());
/// </code>
/// The plugin only observes. Its one write tool, <c>clear</c>, empties its own buffers.
/// </remarks>
public sealed class ShinyBleDevFlowExtension(IBleManager manager, BleActivityMonitor monitor)
    : DevFlowExtension(
        ExtensionNamespace,
        "Shiny BluetoothLE: adapter state, the peripherals the app has used, and a live feed of scans, connections and GATT reads, writes and notifications.",
        "1.0.0")
{
    /// <summary>The DevFlow namespace the plugin's tools and stream are served under.</summary>
    public const string ExtensionNamespace = "org.shiny.ble";

    static readonly JsonElement PeripheralsParameters = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "connectedOnly": { "type": "boolean", "description": "Only peripherals that are connected right now." },
            "name": { "type": "string", "description": "Only peripherals whose name contains this text (case-insensitive)." }
          }
        }
        """).RootElement.Clone();

    /// <summary>Tools are published to MCP as <c>ext_ble_*</c>.</summary>
    public override string? ToolPrefix => "ble";

    protected override void Configure(IDevFlowExtensionBuilder builder)
    {
        builder.MapTool(
            "status",
            "Bluetooth access state, whether a scan is running, and how many peripherals the app has seen and has connected.",
            "GET",
            "/status",
            _ => Task.FromResult(DevFlowResponse.Json(GetStatus())),
            annotations: new DevFlowToolAnnotations { ReadOnly = true, Idempotent = true, Category = "bluetooth" });

        builder.MapTool(
            "peripherals",
            "Peripherals the app has scanned, connected to or exchanged data with: name, connection state, MTU, last signal strength, advertised services, and how many reads, writes and notifications went through each.",
            "GET",
            "/peripherals",
            request => Task.FromResult(DevFlowResponse.Json(monitor.GetPeripherals(
                bool.TryParse(request.GetParameter("connectedOnly"), out var connectedOnly) && connectedOnly,
                request.GetParameter("name")))),
            parameters: PeripheralsParameters,
            annotations: new DevFlowToolAnnotations { ReadOnly = true, Idempotent = true, Category = "bluetooth" });

        builder.MapTool(
            "clear",
            "Forgets the peripherals seen so far and empties the event buffer. Bluetooth itself is not affected.",
            "POST",
            "/clear",
            _ =>
            {
                monitor.Clear();
                return Task.FromResult(DevFlowResponse.Ok("Cleared"));
            },
            annotations: new DevFlowToolAnnotations { Idempotent = true, Category = "bluetooth" });

        builder.MapStream(
            "events",
            "Scans, connection changes, and characteristic reads, writes and notifications with their values in hex.",
            monitor.Events,
            ["kind", "name", "characteristic", "value", "detail"]);
    }

    protected override Task StartAsync(IDevFlowExtensionContext context, CancellationToken cancellationToken)
    {
        if (manager is not MonitoredBleManager)
        {
            context.Logger.LogWarning(
                "IBleManager is not monitored, so only status is available. Call services.AddShinyBleDevFlowMonitor() after AddBluetoothLE().");
        }
        return Task.CompletedTask;
    }

    internal BleStatus GetStatus() => new(
        manager.CurrentAccess.ToString(),
        manager.IsScanning,
        manager is MonitoredBleManager,
        monitor.KnownPeripheralCount,
        manager.GetConnectedPeripherals().Count(),
        monitor.Events.LastSequence);
}

/// <summary>Result of the <c>status</c> tool.</summary>
public sealed record BleStatus(
    string Access,
    bool IsScanning,
    bool Monitored,
    int KnownPeripherals,
    int ConnectedPeripherals,
    long EventsPublished);
