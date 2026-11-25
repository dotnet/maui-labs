using System.CommandLine;
using Microsoft.Maui.Cli.DevFlow.Watch;

namespace Microsoft.Maui.Cli.DevFlow;

public partial class DevFlowCommands
{
    private static void RegisterWatchCommands(Command devflowCommand, Option<string?> deviceOption, Option<bool> json, Option<bool> noJson)
    {
        var watch = new Command("watch", "Run the app under 'dotnet watch' in the background so C#/XAML edits hot reload; wait for each edit's outcome");

        var session = new Option<string?>("--session") { Description = "Watch session id (default: the only running session)" };
        session.Recursive = true;
        watch.Add(session);

        var project = new Option<string?>("--project") { Description = "App project (.csproj) or its directory (default: the project in the current directory)" };
        var framework = new Option<string>("--framework", "-f") { Description = "Target framework to run, e.g. net11.0-android", Required = true };
        var property = new Option<string[]>("--property") { Description = "MSBuild property Name=Value passed to dotnet watch (repeatable)", AllowMultipleArgumentsPerToken = false };
        var noWait = new Option<bool>("--no-wait") { Description = "Return once dotnet watch starts instead of waiting for the app to be ready" };
        var restart = new Option<bool>("--restart") { Description = "Stop a matching running session first instead of reusing it" };
        var startTimeout = new Option<int>("--timeout", "-t") { Description = "Seconds to wait for the initial build and launch", DefaultValueFactory = _ => 600 };

        var start = new Command("start", "Start (or reuse) a background dotnet watch session for the app; waits until the app is running")
            { project, framework, property, noWait, restart, startTimeout };
        start.SetAction(async (ctx, ct) =>
        {
            var isJson = Output.ResolveJsonMode(ctx.GetValue(json), ctx.GetValue(noJson));
            try
            {
                var projectPath = WatchSessions.ResolveProject(ctx.GetValue(project));
                var tfm = ctx.GetValue(framework)!;
                var device = ctx.GetValue(deviceOption);
                var properties = ctx.GetValue(property) ?? [];
                if (ctx.GetValue(restart) && WatchSessions.Load(WatchSessions.SessionId(projectPath, tfm, device)) is { } existing)
                    await WatchSessions.StopAsync(existing, TimeSpan.FromSeconds(15));
                var status = await WatchSessions.StartAsync(projectPath, tfm, device, properties,
                    !ctx.GetValue(noWait), TimeSpan.FromSeconds(ctx.GetValue(startTimeout)), ct);
                WriteWatchStatus(status, isJson);
            }
            catch (Exception ex) { WriteWatchError(ex, isJson); }
        });
        watch.Add(start);

        var status = new Command("status", "Show the watch session state, last outcome, and event cursor");
        status.SetAction((ctx, ct) =>
        {
            var isJson = Output.ResolveJsonMode(ctx.GetValue(json), ctx.GetValue(noJson));
            try { WriteWatchStatus(WatchSessions.GetStatus(WatchSessions.Find(ctx.GetValue(session))), isJson); }
            catch (Exception ex) { WriteWatchError(ex, isJson); }
            return Task.CompletedTask;
        });
        watch.Add(status);

        var list = new Command("list", "List watch sessions");
        list.SetAction((ctx, ct) =>
        {
            var isJson = Output.ResolveJsonMode(ctx.GetValue(json), ctx.GetValue(noJson));
            Output.WriteResult(WatchSessions.List().ToList(), isJson, sessions =>
            {
                if (sessions.Count == 0)
                    Console.WriteLine("No watch sessions.");
                foreach (var s in sessions)
                    Console.WriteLine($"{s.Id}  {s.State,-14} {s.Framework}{(s.Device is null ? "" : $" ({s.Device})")}  {s.Project}");
            });
            return Task.CompletedTask;
        });
        watch.Add(list);

        var since = new Option<int?>("--since") { Description = "Event cursor from a previous start/status/wait; default waits for the result of the latest file change" };
        var waitTimeout = new Option<int>("--timeout", "-t") { Description = "Seconds to wait for an outcome", DefaultValueFactory = _ => 60 };
        var wait = new Command("wait", "Wait for the outcome of a saved edit: applied, no-changes, compile-error, restart-required, app-exited, ...")
            { since, waitTimeout };
        wait.SetAction(async (ctx, ct) =>
        {
            var isJson = Output.ResolveJsonMode(ctx.GetValue(json), ctx.GetValue(noJson));
            try
            {
                var info = WatchSessions.Find(ctx.GetValue(session));
                var result = await WatchSessions.WaitAsync(info, ctx.GetValue(since), TimeSpan.FromSeconds(ctx.GetValue(waitTimeout)), ct);
                Output.WriteResult(result, isJson, r =>
                {
                    Console.WriteLine($"{r.Outcome}: {r.Event?.Message ?? "no outcome yet"} (state {r.State}, cursor {r.Cursor})");
                    foreach (var diagnostic in r.Event?.Diagnostics ?? [])
                        Console.WriteLine($"  {diagnostic}");
                });
                // Non-zero unless the change reached the app (or there was nothing to apply), so `wait && verify` chains work.
                if (result.Outcome is not (WatchEventKinds.Applied or WatchEventKinds.NoChanges))
                    _errorOccurred = true;
            }
            catch (Exception ex) { WriteWatchError(ex, isJson); }
        });
        watch.Add(wait);

        var stop = new Command("stop", "Stop the watch session and the app it launched");
        stop.SetAction(async (ctx, ct) =>
        {
            var isJson = Output.ResolveJsonMode(ctx.GetValue(json), ctx.GetValue(noJson));
            try { WriteWatchStatus(await WatchSessions.StopAsync(WatchSessions.Find(ctx.GetValue(session)), TimeSpan.FromSeconds(15)), isJson); }
            catch (Exception ex) { WriteWatchError(ex, isJson); }
        });
        watch.Add(stop);

        var restartTimeout = new Option<int>("--timeout", "-t") { Description = "Seconds to wait for the rebuild and launch", DefaultValueFactory = _ => 600 };
        var restartCmd = new Command("restart", "Stop and start the session again with the same arguments (full rebuild)") { restartTimeout };
        restartCmd.SetAction(async (ctx, ct) =>
        {
            var isJson = Output.ResolveJsonMode(ctx.GetValue(json), ctx.GetValue(noJson));
            try
            {
                var info = WatchSessions.Find(ctx.GetValue(session));
                await WatchSessions.StopAsync(info, TimeSpan.FromSeconds(15));
                WriteWatchStatus(await WatchSessions.StartAsync(info.Project, info.Framework, info.Device, info.Properties,
                    waitForReady: true, TimeSpan.FromSeconds(ctx.GetValue(restartTimeout)), ct), isJson);
            }
            catch (Exception ex) { WriteWatchError(ex, isJson); }
        });
        watch.Add(restartCmd);

        var host = new Command("host", "Internal: runs dotnet watch for a session") { Hidden = true };
        host.SetAction(async (ctx, ct) =>
        {
            var id = ctx.GetValue(session);
            return string.IsNullOrEmpty(id) ? 1 : await WatchSessions.RunHostAsync(id);
        });
        watch.Add(host);

        devflowCommand.Add(watch);
    }

    private static void WriteWatchStatus(WatchStatus status, bool isJson)
    {
        Output.WriteResult(status, isJson, s =>
        {
            Console.WriteLine($"{s.Id}: {s.State}{(s.Reused == true ? " (reused)" : "")}  cursor {s.Cursor}, {s.AppliedCount} update(s) applied");
            if (s.LastOutcome is { } outcome)
                Console.WriteLine($"  last: {outcome.Kind}: {outcome.Message}");
            if (s.Error is not null)
                Console.WriteLine($"  error: {s.Error}");
            Console.WriteLine($"  log: {s.LogPath}");
        });
        if (status.Error is not null || status.State is "failed" or "compile-error")
            _errorOccurred = true;
    }

    private static void WriteWatchError(Exception exception, bool json)
    {
        var type = exception switch
        {
            FileNotFoundException => "ProjectNotFound",
            InvalidOperationException => "WatchSessionError",
            OperationCanceledException => "Cancelled",
            _ => "WatchError",
        };
        Output.WriteError(exception.Message, json, type);
        _errorOccurred = true;
    }
}
