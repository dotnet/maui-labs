using System.CommandLine;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

public partial class DevFlowCommands
{
    private static void RegisterFoldCommands(Command deviceCommand, Option<string?> androidDevice,
        Option<bool> json, Option<bool> noJson, Option<string> agentHost, Option<int> agentPort)
    {
        var fold = new Command("fold", "Inspect or control an explicitly selected foldable emulator/simulator (no agent required)");
        fold.Aliases.Add("hinge");
        var udid = new Option<string?>("--udid") { Description = "Explicit iOS Simulator UDID; mutually exclusive with --device" };
        var experimental = new Option<bool>("--experimental") { Description = "Opt into private iOS Simulator hinge APIs (Apple Silicon, Xcode 27.1+); never used on physical devices" };
        udid.Recursive = true;
        experimental.Recursive = true;
        fold.Add(udid);
        fold.Add(experimental);

        var capabilities = new Command("capabilities", "Discover enabled hinges and supported fold operations");
        capabilities.SetAction(async (ctx, ct) =>
        {
            var isJson = Output.ResolveJsonMode(ctx.GetValue(json), ctx.GetValue(noJson));
            try
            {
                var device = FoldOperations.CreateDevice(ctx.GetValue(androidDevice), ctx.GetValue(udid), ctx.GetValue(experimental));
                var result = await device.GetCapabilitiesAsync(ct);
                Output.WriteResult(result, isJson, value =>
                    Console.WriteLine($"{value.DeviceId}: {(value.CanReadAngle ? "supported" : "unsupported")} ({value.Backend}){(value.Reason is null ? "" : $": {value.Reason}")}"));
            }
            catch (Exception ex) { WriteFoldError(ex, isJson); }
        });
        fold.Add(capabilities);

        var get = new Command("get", "Read hinge angles and observed mode; iOS also reports integrated displays");
        get.SetAction(async (ctx, ct) =>
        {
            var isJson = Output.ResolveJsonMode(ctx.GetValue(json), ctx.GetValue(noJson));
            try
            {
                var device = FoldOperations.CreateDevice(ctx.GetValue(androidDevice), ctx.GetValue(udid), ctx.GetValue(experimental));
                var state = await device.GetStateAsync(ct);
                Output.WriteResult(state, isJson, WriteFoldState);
            }
            catch (Exception ex) { WriteFoldError(ex, isJson); }
        });
        fold.Add(get);

        var mode = new Argument<string>("mode") { Description = "folded, unfolded, or partially-folded (default partial angle: 90 degrees)" };
        var angle = new Option<double?>("--angle") { Description = "Explicit degrees strictly between 0 and 180; requires partially-folded" };
        var index = new Option<int>("--hinge-index") { Description = "Enabled hinge index (default: 0); presets affect this hinge only" };
        var duration = new Option<double>("--duration") { Description = "Animate from the observed angle over 0..30 seconds (default: immediate)" };
        var timeout = new Option<int>("--timeout") { Description = "Overall operation timeout, including helper build and verification (1..120 seconds)", DefaultValueFactory = _ => 30 };
        var screenshot = new Option<string?>("--and-screenshot") { Description = "Capture the connected app after verification (optional output path); requires an agent", Arity = ArgumentArity.ZeroOrOne };
        var tree = new Option<bool>("--and-tree") { Description = "Read the connected app tree after verification; requires an agent" };
        var depth = new Option<int>("--and-tree-depth") { Description = "Tree depth after the mutation", DefaultValueFactory = _ => 2 };
        var set = new Command("set", "Set and verify a hinge angle; iOS endpoint modes also require the expected display handover")
            { mode, angle, index, duration, timeout, screenshot, tree, depth };
        set.SetAction(async (ctx, ct) =>
        {
            var isJson = Output.ResolveJsonMode(ctx.GetValue(json), ctx.GetValue(noJson));
            try
            {
                var parsedMode = FoldOperations.ParseMode(ctx.GetValue(mode)!);
                var device = FoldOperations.CreateDevice(ctx.GetValue(androidDevice), ctx.GetValue(udid), ctx.GetValue(experimental));
                var targetAngle = FoldableDevice.ValidateRequest(parsedMode, ctx.GetValue(angle), ctx.GetValue(index),
                    ctx.GetValue(duration), ctx.GetValue(timeout));
                if (Program.IsDryRun(ctx))
                {
                    var preview = new JsonObject
                    {
                        ["dryRun"] = true, ["deviceId"] = device.DeviceId, ["platform"] = device.Platform,
                        ["requestedMode"] = FoldModes.Format(parsedMode), ["requestedAngleDegrees"] = targetAngle,
                        ["hingeIndex"] = ctx.GetValue(index), ["durationSeconds"] = ctx.GetValue(duration),
                    };
                    Output.WriteResult(preview, isJson, _ => Console.WriteLine(
                        FormattableString.Invariant($"Would set hinge {ctx.GetValue(index)} on {device.DeviceId} to {targetAngle} degrees. No device access performed.")));
                    return;
                }
                var result = await device.SetAsync(parsedMode, ctx.GetValue(angle), ctx.GetValue(index),
                    ctx.GetValue(duration), ctx.GetValue(timeout), ct);
                Output.WriteResult(result, isJson, value => WriteFoldState(value.State));
                var hasScreenshot = ctx.GetResult(screenshot) is not null;
                if (hasScreenshot || ctx.GetValue(tree))
                    await HandlePostActionFlags(ctx.GetValue(agentHost)!, ctx.GetValue(agentPort), isJson,
                        hasScreenshot, ctx.GetValue(screenshot), ctx.GetValue(tree), ctx.GetValue(depth));
            }
            catch (Exception ex) { WriteFoldError(ex, isJson); }
        });
        fold.Add(set);
        deviceCommand.Add(fold);
    }

    private static void WriteFoldState(FoldState state)
    {
        Console.WriteLine($"{state.DeviceId}: {FoldModes.Format(state.Mode)} ({state.Backend}{(state.Experimental ? ", experimental" : "")})");
        foreach (var hinge in state.Hinges)
            Console.WriteLine(FormattableString.Invariant($"Hinge {hinge.Index}: {hinge.AngleDegrees:F2} degrees"));
        foreach (var display in state.Displays)
            Console.WriteLine(FormattableString.Invariant($"Display {display.Id} ({display.Name}): {(display.Active ? "active" : "inactive")}, {display.NativeWidth}x{display.NativeHeight}, scale {display.Scale}, {display.Rotation}"));
    }

    private static void WriteFoldError(Exception exception, bool json)
    {
        var type = exception switch
        {
            ArgumentException => "InvalidArgument",
            NotSupportedException => "NotSupported",
            TimeoutException => "FoldVerificationTimeout",
            OperationCanceledException => "Cancelled",
            _ => "FoldError",
        };
        Output.WriteError(exception.Message, json, type);
        _errorOccurred = true;
    }
}
