using Microsoft.Maui.Cli.DevFlow.Watch;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

// Transcript lines are verbatim from .NET 11 `dotnet watch` sessions (Android, redirected output).
[Collection("CLI")]
public class DevFlowWatchTests
{
    const string Ready = "dotnet watch ⌚ Waiting for changes";
    const string XamlChanged = "dotnet watch ⌚ File updated: ./MainPage.xaml";
    const string CodeChanged = "dotnet watch ⌚ File updated: ./MainPage.xaml.cs";
    const string Applied = "dotnet watch 🔥 C# and Razor changes applied in 869ms.";
    const string NoChanges = "dotnet watch ⌚ No managed code changes to apply.";
    const string CompileError = "dotnet watch 🔥 Unable to apply changes due to compilation errors.";
    const string Cs0029 = "dotnet watch ❌ /src/MainPage.xaml.cs(9,45): error CS0029: Cannot implicitly convert type 'string' to 'int'";

    [Fact]
    public void Parse_AppliedEdit_ReportsFilesAndElapsedTime()
    {
        var events = WatchTranscriptParser.Parse(["dotnet watch 🔨 Loading projects ...", Ready, XamlChanged, Applied]);

        Assert.Equal(
            [WatchEventKinds.Loading, WatchEventKinds.Ready, WatchEventKinds.FileChanged, WatchEventKinds.Applied],
            events.Select(e => e.Kind));
        Assert.Equal([1, 2, 3, 4], events.Select(e => e.Seq));
        Assert.Equal(["./MainPage.xaml"], events[2].Files);
        Assert.Equal(869, events[3].ElapsedMs);
        Assert.Equal("ready", WatchTranscriptParser.DeriveState(events, watchRunning: true));
    }

    [Fact]
    public void Parse_HotReloadCompileError_AttachesTrailingDiagnostics()
    {
        var events = WatchTranscriptParser.Parse([Ready, CodeChanged, CompileError, Cs0029]);

        var error = Assert.Single(events, e => e.Kind == WatchEventKinds.CompileError);
        Assert.Equal(["/src/MainPage.xaml.cs(9,45): error CS0029: Cannot implicitly convert type 'string' to 'int'"], error.Diagnostics);
        Assert.Equal("compile-error", WatchTranscriptParser.DeriveState(events, watchRunning: true));
    }

    [Fact]
    public void Parse_BuildErrorsBeforeFailureSummary_AttachToTheFailure()
    {
        var events = WatchTranscriptParser.Parse([
            "/src/MainPage.xaml.cs(9,45): error CS0029: Cannot implicitly convert type 'string' to 'int' [/src/App.csproj]",
            "dotnet watch ❌ Build failed: /src/App.csproj",
        ]);

        var error = Assert.Single(events);
        Assert.Equal(WatchEventKinds.CompileError, error.Kind);
        Assert.Single(error.Diagnostics!);
    }

    [Fact]
    public void Parse_RudeEdit_ReportsRestartAndAppExit()
    {
        var events = WatchTranscriptParser.Parse([
            Ready,
            CodeChanged,
            "dotnet watch ⚠ Restart is needed to apply the changes.",
            "dotnet watch ❌ [DevFlow.Sample (net11.0-android)] Exited with error code 143",
            "dotnet watch ⏳ Waiting for a file to change before restarting ...",
        ]);

        Assert.Equal(
            [WatchEventKinds.Ready, WatchEventKinds.FileChanged, WatchEventKinds.RestartRequired, WatchEventKinds.ProcessExited, WatchEventKinds.WaitingForRestart],
            events.Select(e => e.Kind));
        Assert.Equal("DevFlow.Sample (net11.0-android)", events[3].Project);
        Assert.Equal("app-exited", WatchTranscriptParser.DeriveState(events, watchRunning: true));
        Assert.Equal("stopped", WatchTranscriptParser.DeriveState(events, watchRunning: false));
    }

    [Fact]
    public void Parse_UnrecognizedLines_AreIgnored()
    {
        var events = WatchTranscriptParser.Parse([
            "11-24 12:57:14.670 25250 25250 D InsetsController: hide(ime())",
            "dotnet watch 🕵️ [DevFlow.Sample (net11.0-android)] Hot reload capabilities: Baseline AddMethodToExistingType.",
            "Build succeeded.",
        ]);

        Assert.Empty(events);
        Assert.Equal("starting", WatchTranscriptParser.DeriveState(events, watchRunning: true));
    }

    [Fact]
    public void DefaultCursor_ReplaysTheLatestChange()
    {
        var events = WatchTranscriptParser.Parse([Ready, XamlChanged, Applied, CodeChanged, NoChanges]);

        var cursor = WatchSessions.DefaultCursor(events);

        Assert.Equal(3, cursor);
        Assert.Equal(WatchEventKinds.NoChanges, WatchSessions.FindOutcome(events, cursor)?.Kind);
        Assert.Equal(1, WatchSessions.DefaultCursor(WatchTranscriptParser.Parse([Ready])));
    }

    [Fact]
    public void FindOutcome_SkipsOutcomesOfSupersededSaves()
    {
        // Two saves after the cursor: the first one's compile error is stale once the second is applied.
        var events = WatchTranscriptParser.Parse([Ready, CodeChanged, CompileError, Cs0029, CodeChanged, Applied]);

        var outcome = WatchSessions.FindOutcome(events, cursor: 1);

        Assert.Equal(WatchEventKinds.Applied, outcome?.Kind);
        Assert.Null(WatchSessions.FindOutcome(events.Take(4).ToList(), cursor: 1));
    }

    [Fact]
    public void FindOutcome_WatcherExitEndsAPendingWait()
    {
        var events = WatchTranscriptParser.Parse([Ready, CodeChanged]);
        events.Add(new WatchEvent { Seq = 3, Kind = WatchEventKinds.WatchExited, Message = "dotnet watch exited with code 1." });

        Assert.Equal(WatchEventKinds.WatchExited, WatchSessions.FindOutcome(events, cursor: 1)?.Kind);
    }

    [Fact]
    public async Task WaitAsync_SaveDuringSettleDelay_ReportsNewOutcomeNotPreviousOne()
    {
        var root = WatchSessions.RootDirectory;
        var settle = WatchSessions.SaveSettleDelay;
        var temp = Directory.CreateTempSubdirectory("devflow-watch-");
        try
        {
            WatchSessions.RootDirectory = temp.FullName;
            WatchSessions.SaveSettleDelay = TimeSpan.FromMilliseconds(400);
            var info = new WatchSessionInfo { Id = "sample-0123456789" };
            var log = WatchSessions.LogPath(info.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            await File.WriteAllLinesAsync(log, [Ready, CodeChanged, NoChanges]);

            // The agent saves and immediately waits; the watcher only reports the save afterwards.
            var wait = WatchSessions.WaitAsync(info, since: null, TimeSpan.FromSeconds(10), CancellationToken.None);
            await Task.Delay(100);
            await File.AppendAllLinesAsync(log, [XamlChanged]);
            await Task.Delay(100);
            await File.AppendAllLinesAsync(log, [Applied]);

            var result = await wait;

            Assert.Equal(WatchEventKinds.Applied, result.Outcome);
            Assert.Equal(5, result.Cursor);
            Assert.Equal(869, result.Event?.ElapsedMs);
        }
        finally
        {
            WatchSessions.RootDirectory = root;
            WatchSessions.SaveSettleDelay = settle;
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    public void BuildWatchArguments_ForwardsDeviceAndProperties()
    {
        var args = WatchSessions.BuildWatchArguments("/src/App.csproj", "net11.0-android", "emulator-5554", ["Configuration=Debug", " "]);

        Assert.Equal(
            ["watch", "--project", "/src/App.csproj", "-f", "net11.0-android", "--non-interactive", "--device", "emulator-5554", "--property:Configuration=Debug"],
            args);
    }

    [Fact]
    public void SessionId_IsStablePerProjectFrameworkAndDevice()
    {
        var android = WatchSessions.SessionId("/src/App/App.csproj", "net11.0-android", "emulator-5554");

        Assert.StartsWith("app-", android);
        Assert.Equal(android, WatchSessions.SessionId("/src/App/App.csproj", "net11.0-android", "emulator-5554"));
        Assert.NotEqual(android, WatchSessions.SessionId("/src/App/App.csproj", "net11.0-ios", "emulator-5554"));
        Assert.NotEqual(android, WatchSessions.SessionId("/src/App/App.csproj", "net11.0-android", "emulator-5556"));
    }
}
