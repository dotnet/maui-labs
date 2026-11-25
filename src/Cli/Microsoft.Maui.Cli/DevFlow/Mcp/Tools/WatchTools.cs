using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Watch;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class WatchTools
{
    [McpServerTool(Name = "maui_watch_start"),
     Description("Build and launch the app under 'dotnet watch' in a background session so saved C# and XAML edits hot reload into the running app in about a second instead of a full rebuild. Reuses a matching running session. Waits until the app is running (state 'ready'); then use maui_wait/maui_tree as usual. After each edit, call maui_watch_wait.")]
    public static async Task<string> Start(
        [Description("Target framework to run, e.g. net11.0-android, net11.0-ios, net11.0-maccatalyst")] string framework,
        [Description("App project (.csproj) or its directory. Optional when the working directory has exactly one project.")] string? project = null,
        [Description("Device id passed to dotnet watch --device (Android serial or iOS simulator UDID). Optional.")] string? device = null,
        [Description("MSBuild properties as Name=Value passed to dotnet watch. Optional.")] string[]? properties = null,
        [Description("Stop a matching running session first instead of reusing it")] bool restart = false,
        [Description("Seconds to wait for the initial build and launch (default 600)")] int timeoutSeconds = 600,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projectPath = WatchSessions.ResolveProject(project);
            if (restart && WatchSessions.Load(WatchSessions.SessionId(projectPath, framework, device)) is { } existing)
                await WatchSessions.StopAsync(existing, TimeSpan.FromSeconds(15));
            var status = await WatchSessions.StartAsync(projectPath, framework, device, properties ?? [],
                waitForReady: true, TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
            return CliJson.SerializeUntyped(status, indented: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "maui_watch_wait"),
     Description("After saving a C#/XAML edit, wait for dotnet watch to report its outcome: applied (verify in the running app, no rebuild needed), no-changes, compile-error (fix the listed diagnostics and save again), restart-required (call maui_watch_restart), app-exited, failed, or timeout. Returns a cursor; pass it as 'since' next time.")]
    public static async Task<string> Wait(
        [Description("Event cursor from a previous maui_watch_* result. Omit to wait for the result of the latest file change.")] int? since = null,
        [Description("Seconds to wait for an outcome (default 60)")] int timeoutSeconds = 60,
        [Description("Watch session id. Optional when exactly one session is running.")] string? session = null,
        CancellationToken cancellationToken = default)
    {
        var info = Find(session);
        var result = await WatchSessions.WaitAsync(info, since, TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
        return CliJson.SerializeUntyped(result, indented: false);
    }

    [McpServerTool(Name = "maui_watch_status"),
     Description("Show the dotnet watch session state (starting, ready, applying, compile-error, app-exited, stopped, ...), last outcome, applied-update count, and event cursor. With no session, lists all sessions.")]
    public static string Status(
        [Description("Watch session id. Optional; omit to list all sessions.")] string? session = null)
    {
        if (string.IsNullOrWhiteSpace(session))
            return CliJson.SerializeUntyped(WatchSessions.List().ToList(), indented: false);
        return CliJson.SerializeUntyped(WatchSessions.GetStatus(Find(session)), indented: false);
    }

    [McpServerTool(Name = "maui_watch_restart"),
     Description("Stop and relaunch the dotnet watch session with the same arguments (full rebuild). Use when maui_watch_wait reports restart-required or the app exited.")]
    public static async Task<string> Restart(
        [Description("Watch session id. Optional when exactly one session exists.")] string? session = null,
        [Description("Seconds to wait for the rebuild and launch (default 600)")] int timeoutSeconds = 600,
        CancellationToken cancellationToken = default)
    {
        var info = Find(session);
        await WatchSessions.StopAsync(info, TimeSpan.FromSeconds(15));
        var status = await WatchSessions.StartAsync(info.Project, info.Framework, info.Device, info.Properties,
            waitForReady: true, TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
        return CliJson.SerializeUntyped(status, indented: false);
    }

    [McpServerTool(Name = "maui_watch_stop"),
     Description("Stop the dotnet watch session and the app it launched.")]
    public static async Task<string> Stop(
        [Description("Watch session id. Optional when exactly one session is running.")] string? session = null)
        => CliJson.SerializeUntyped(await WatchSessions.StopAsync(Find(session), TimeSpan.FromSeconds(15)), indented: false);

    static WatchSessionInfo Find(string? session)
    {
        try { return WatchSessions.Find(session); }
        catch (InvalidOperationException ex) { throw new McpException(ex.Message); }
    }
}
