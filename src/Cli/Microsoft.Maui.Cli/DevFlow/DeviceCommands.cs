using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.Cli.DevFlow;

/// <summary>
/// <c>maui devflow devices</c> — the device layer at the command line.
/// <para>
/// Everything routes through the broker rather than a device host directly, so the CLI, the
/// Inspector, and the MCP server all share one view of which devices exist and which app is
/// running on each.
/// </para>
/// </summary>
internal static class DeviceCommands
{
    public static Command Create(
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        IDevFlowOutputWriter output)
    {
        var companionManager = new MobileCanvasCompanionManager();
        var devicesCommand = new Command(
            "devices",
            "Inspect and control the emulators and simulators DevFlow can reach");

        devicesCommand.Add(CreateListCommand(jsonOption, noJsonOption, output));
        devicesCommand.Add(CreateSetupCommand(jsonOption, noJsonOption, output));
        devicesCommand.Add(CreateHostCommand(jsonOption, noJsonOption, output, companionManager));
        devicesCommand.Add(CreateControlCommand("boot", "Boot a device and wait until it is ready", jsonOption, noJsonOption, output));
        devicesCommand.Add(CreateControlCommand("shutdown", "Power a device off without erasing it", jsonOption, noJsonOption, output));

        return devicesCommand;
    }

    /// <summary>
    /// <c>maui devflow devices setup</c> — reports what the device layer needs, and how to get it.
    /// <para>
    /// This deliberately diagnoses rather than downloads. Silently fetching and executing a binary
    /// on a developer's machine is not something a diagnostic command should do, so it names the
    /// exact state and the exact next step and leaves the decision with the human.
    /// </para>
    /// </summary>
    private static Command CreateSetupCommand(
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        IDevFlowOutputWriter output)
    {
        var command = new Command("setup", "Check what the device layer needs on this machine");

        command.SetAction(async (ctx, ct) =>
        {
            var isJson = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));

            IDeviceSurface surface = MobileCanvasHost.IsPresent()
                ? new MobileCanvasDeviceSurface()
                : new NullDeviceSurface();

            var health = await surface.GetHealthAsync(ct);
            var (state, action) = DescribeSetup(health);

            if (isJson)
            {
                output.WriteRawJson(JsonSerializer.Serialize(new JsonObject
                {
                    ["available"] = health.Available,
                    ["state"] = health.Availability.ToString().ToLowerInvariant(),
                    ["reason"] = health.Reason,
                    ["hostVersion"] = health.Version,
                    ["stateFile"] = MobileCanvasHost.StateFilePath,
                    ["nextStep"] = action,
                }));
                return;
            }

            Console.WriteLine(state);
            if (!string.IsNullOrWhiteSpace(action))
                Console.WriteLine(action);
        });

        return command;
    }

    private static Command CreateHostCommand(
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        IDevFlowOutputWriter output,
        MobileCanvasCompanionManager manager)
    {
        var host = new Command(
            "host",
            "Install and manage the pinned optional Mobile Canvas companion host");

        host.Add(CreateHostStatusCommand(jsonOption, noJsonOption, output, manager));
        host.Add(CreateHostOperationCommand(
            "install",
            "Download and verify the pinned companion runtime without starting it",
            manager.InstallAsync,
            jsonOption,
            noJsonOption,
            output));
        host.Add(CreateHostOperationCommand(
            "start",
            "Start the installed, checksum-verified companion host",
            manager.StartAsync,
            jsonOption,
            noJsonOption,
            output));
        host.Add(CreateHostOperationCommand(
            "stop",
            "Stop the running companion host without starting an absent host",
            manager.StopAsync,
            jsonOption,
            noJsonOption,
            output));
        host.Add(CreateHostOperationCommand(
            "update",
            "Install the companion version pinned by this MAUI CLI",
            manager.UpdateAsync,
            jsonOption,
            noJsonOption,
            output));
        host.Add(CreateHostMcpCommand(manager));

        return host;
    }

    private static Command CreateHostMcpCommand(MobileCanvasCompanionManager manager)
    {
        var command = new Command(
            "mcp",
            "Run the separately namespaced Mobile Canvas stdio MCP server");

        command.SetAction(async (_, ct) =>
        {
            var result = await manager.RunMcpAsync(ct);
            if (!result.Started && !string.IsNullOrWhiteSpace(result.Error))
                Console.Error.WriteLine(result.Error);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateHostStatusCommand(
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        IDevFlowOutputWriter output,
        MobileCanvasCompanionManager manager)
    {
        var command = new Command(
            "status",
            "Check the pinned companion installation and running host without downloading or starting anything");

        command.SetAction(async (ctx, ct) =>
        {
            var isJson = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            var status = await manager.GetStatusAsync(probeHost: true, cancellationToken: ct);

            if (isJson)
            {
                output.WriteRawJson(JsonSerializer.Serialize(
                    status,
                    MobileCanvasCompanionJsonContext.Default.MobileCanvasCompanionStatus));
                return;
            }

            if (!status.Supported)
            {
                Console.WriteLine(status.Reason);
                return;
            }

            Console.WriteLine($"Mobile Canvas {status.Version}");
            Console.WriteLine($"Runtime: {status.RuntimeKey}");
            Console.WriteLine($"Installed: {status.Installed}");
            Console.WriteLine($"Integrity valid: {status.IntegrityValid}");
            Console.WriteLine($"Running: {status.Running}");
            if (!string.IsNullOrWhiteSpace(status.Reason))
                Console.WriteLine(status.Reason);
        });

        return command;
    }

    private static Command CreateHostOperationCommand(
        string name,
        string description,
        Func<CancellationToken, Task<MobileCanvasCompanionOperationResult>> operation,
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        IDevFlowOutputWriter output)
    {
        var command = new Command(name, description);

        command.SetAction(async (ctx, ct) =>
        {
            var isJson = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            var result = await operation(ct);

            if (isJson)
            {
                output.WriteRawJson(JsonSerializer.Serialize(
                    result,
                    MobileCanvasCompanionJsonContext.Default.MobileCanvasCompanionOperationResult));
                return result.Success ? 0 : 1;
            }

            if (!result.Success)
            {
                output.WriteError(
                    result.Message,
                    json: false,
                    suggestions: name is "start"
                        ? ["Run 'maui devflow devices host install' first."]
                        : null);
                return 1;
            }

            Console.WriteLine(result.Message);
            return 0;
        });

        return command;
    }

    /// <summary>
    /// Turns a health state into a description and an actionable next step. Each state gets a
    /// different instruction on purpose: "not installed" and "installed but unusable" look
    /// identical to a user but need opposite responses.
    /// </summary>
    private static (string State, string Action) DescribeSetup(DeviceHostHealth health) =>
        health.Availability switch
        {
            DeviceHostAvailability.Available =>
                ($"Device layer ready{(health.Version is null ? "" : $" (host {health.Version})")}.",
                 "Run 'maui devflow devices list' to see what is available."),

            DeviceHostAvailability.Absent =>
                ("No device host is running, so emulator and simulator control is unavailable.",
                 "Install and start the Mobile Canvas host, then run this again. "
                 + "DevFlow works without it — only device-level control is affected."),

            DeviceHostAvailability.NotResponding =>
                ("A device host was registered but is not responding.",
                 "It most likely crashed or was stopped. Start it again, then run this."),

            DeviceHostAvailability.Unauthorized =>
                ("The device host rejected DevFlow's control token.",
                 "The host was probably restarted and reissued one. Restart it, then run this."),

            DeviceHostAvailability.Incompatible =>
                ($"The device host speaks a protocol this build does not support. {health.Reason}",
                 "Update DevFlow or the device host so their major versions match."),

            _ => ("The device layer state could not be determined.", ""),
        };

    private static Command CreateListCommand(
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        IDevFlowOutputWriter output)
    {
        var command = new Command("list", "List devices, each paired with the app running inside it");

        command.SetAction(async (ctx, ct) =>
        {
            var isJson = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            var brokerPort = await BrokerClient.EnsureBrokerRunningAsync() ?? BrokerServer.DefaultPort;
            var payload = await BrokerClient.ListDevicesAsync(brokerPort);

            if (payload is null)
            {
                output.WriteError(
                    "The DevFlow broker could not be reached.",
                    isJson,
                    retryable: true,
                    suggestions: ["Start it with 'maui devflow broker start'."]);
                return;
            }

            if (isJson)
            {
                output.WriteRawJson(payload);
                return;
            }

            WriteHumanReadable(payload, output, isJson);
        });

        return command;
    }

    private static Command CreateControlCommand(
        string action,
        string description,
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        IDevFlowOutputWriter output)
    {
        var deviceArg = new Argument<string>("device") { Description = "Device id from 'maui devflow devices list'" };
        var command = new Command(action, description) { deviceArg };

        command.SetAction(async (ctx, ct) =>
        {
            var isJson = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            var deviceId = ctx.GetValue(deviceArg)!;
            var brokerPort = await BrokerClient.EnsureBrokerRunningAsync() ?? BrokerServer.DefaultPort;

            var result = await BrokerClient.ControlDeviceAsync(brokerPort, deviceId, action);

            if (!result.Success)
            {
                output.WriteError(result.Reason ?? $"The device refused {action}.", isJson);
                return;
            }

            output.WriteActionResult(true, action, deviceId, isJson, $"{deviceId}: {action} succeeded.");
        });

        return command;
    }

    /// <summary>
    /// Renders the device list for a human.
    /// <para>
    /// "No devices" and "no device layer" get different messages on purpose: the first is a
    /// prompt to create one, the second a prompt to install something. Conflating them leaves the
    /// reader with no idea which applies.
    /// </para>
    /// </summary>
    private static void WriteHumanReadable(string payload, IDevFlowOutputWriter output, bool isJson)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            output.WriteError("The broker returned an unreadable device list.", isJson);
            return;
        }

        var available = root?["available"]?.GetValue<bool>() ?? false;
        if (!available)
        {
            var reason = root?["reason"]?.GetValue<string>()
                ?? "No device host is available, so emulator and simulator control is disabled.";
            Console.WriteLine(reason);
            return;
        }

        if (root?["devices"] is not JsonArray devices || devices.Count == 0)
        {
            Console.WriteLine("No emulators or simulators were found. Create one, then run this again.");
            return;
        }

        foreach (var device in devices)
        {
            if (device is null)
                continue;

            var name = device["name"]?.GetValue<string>() ?? "(unnamed)";
            var id = device["id"]?.GetValue<string>() ?? "";
            var state = device["state"]?.GetValue<string>() ?? "unknown";
            var agentPort = device["agentPort"]?.GetValue<int?>();

            var app = agentPort is null ? "" : $"  - app on port {agentPort}";
            Console.WriteLine($"{name}  [{state}]  {id}{app}");
        }
    }
}
