using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.DevFlow;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests.DevFlow;

/// <summary>
/// Tests for <see cref="HookCheckCommand"/>. They skip the real
/// <c>dotnet msbuild</c> call by injecting an evaluator delegate, so the
/// hook's parsing and decision logic is exercised without depending on a
/// restored SDK state on the test machine.
/// </summary>
public class HookCheckCommandTests : IDisposable
{
    private readonly string _cwd;
    private readonly string _pluginData;

    public HookCheckCommandTests()
    {
        _cwd = Directory.CreateTempSubdirectory("devflow-hook-cwd-").FullName;
        _pluginData = Directory.CreateTempSubdirectory("devflow-hook-data-").FullName;
        // Every test needs at least one csproj present for the hook to
        // get past its "not a project dir" short-circuit.
        File.WriteAllText(Path.Combine(_cwd, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
    }

    public void Dispose()
    {
        try { Directory.Delete(_cwd, recursive: true); } catch { }
        try { Directory.Delete(_pluginData, recursive: true); } catch { }
    }

    private static JsonNode StubEvaluation(string? useMaui, params string[] packageIds)
    {
        var refs = new JsonArray();
        foreach (var id in packageIds)
            refs.Add(new JsonObject { ["Identity"] = id });
        return new JsonObject
        {
            ["Properties"] = new JsonObject
            {
                ["UseMaui"] = useMaui ?? string.Empty,
                ["EnableDevFlow"] = string.Empty
            },
            ["Items"] = new JsonObject { ["PackageReference"] = refs }
        };
    }

    private HookCheckCommand.HookEnvironment CreateEnv(
        string eventName,
        Func<string, JsonNode?>? evaluator,
        string? stdin = null,
        IDictionary<string, string?>? extraEnv = null)
    {
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CLAUDE_PLUGIN_DATA"] = _pluginData
        };
        if (extraEnv is not null)
            foreach (var kv in extraEnv) env[kv.Key] = kv.Value;

        return new HookCheckCommand.HookEnvironment
        {
            EventName = eventName,
            Cwd = _cwd,
            Stdin = new StringReader(stdin ?? string.Empty),
            Stdout = new StringWriter(),
            Stderr = new StringWriter(),
            Env = env,
            MsbuildEvaluator = evaluator
        };
    }

    private static async Task<string> RunAndGetOutputAsync(HookCheckCommand.HookEnvironment env)
    {
        var code = await HookCheckCommand.RunCoreAsync(env);
        Assert.Equal(0, code);
        return env.Stdout.ToString() ?? string.Empty;
    }

    [Fact]
    public async Task EmptyDirectory_EmitsNothing()
    {
        // Remove the seeded csproj for this one.
        File.Delete(Path.Combine(_cwd, "App.csproj"));
        var env = CreateEnv("SessionStart", _ => null);
        var output = await RunAndGetOutputAsync(env);
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public async Task NonMauiCsproj_EmitsNothing()
    {
        var env = CreateEnv("SessionStart", _ => StubEvaluation(null));
        var output = await RunAndGetOutputAsync(env);
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public async Task StandardMauiProject_EmitsStandardNudge()
    {
        var env = CreateEnv("SessionStart",
            _ => StubEvaluation("true", "Microsoft.Maui.Controls"));
        var output = await RunAndGetOutputAsync(env);
        Assert.Contains("(standard)", output);
        Assert.Contains("set up DevFlow", output);
    }

    [Fact]
    public async Task BlazorHybridProject_EmitsBlazorFlavor()
    {
        var env = CreateEnv("SessionStart",
            _ => StubEvaluation("true",
                "Microsoft.Maui.Controls",
                "Microsoft.AspNetCore.Components.WebView.Maui"));
        var output = await RunAndGetOutputAsync(env);
        Assert.Contains("Blazor hybrid", output);
    }

    [Fact]
    public async Task GtkProject_EmitsGtkFlavor()
    {
        // GTK apps don't set <UseMaui>true</UseMaui>; detection keys off the
        // Platform.Maui.Linux.Gtk4 package reference instead.
        var env = CreateEnv("SessionStart",
            _ => StubEvaluation(null, "Platform.Maui.Linux.Gtk4"));
        var output = await RunAndGetOutputAsync(env);
        Assert.Contains("(GTK)", output);
    }

    [Fact]
    public async Task AlreadyWired_EmitsNothing()
    {
        var env = CreateEnv("SessionStart",
            _ => StubEvaluation("true",
                "Microsoft.Maui.Controls",
                "Microsoft.Maui.DevFlow.Agent"));
        var output = await RunAndGetOutputAsync(env);
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public async Task Debounce_SecondSessionStart_IsSilent()
    {
        var env1 = CreateEnv("SessionStart",
            _ => StubEvaluation("true", "Microsoft.Maui.Controls"));
        var first = await RunAndGetOutputAsync(env1);
        Assert.Contains("set up DevFlow", first);

        var env2 = CreateEnv("SessionStart",
            _ => StubEvaluation("true", "Microsoft.Maui.Controls"));
        var second = await RunAndGetOutputAsync(env2);
        Assert.Equal(string.Empty, second);
    }

    [Fact]
    public async Task PostToolUse_UnrelatedFile_IsSilent()
    {
        var stdin = new JsonObject
        {
            ["tool_input"] = new JsonObject
            {
                ["file_path"] = Path.Combine(_cwd, "README.md")
            }
        }.ToJsonString();

        var env = CreateEnv("PostToolUse",
            _ => StubEvaluation("true", "Microsoft.Maui.Controls"),
            stdin: stdin);
        var output = await RunAndGetOutputAsync(env);
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public async Task PostToolUse_MauiProgramEdit_Nudges()
    {
        var stdin = new JsonObject
        {
            ["tool_input"] = new JsonObject
            {
                ["file_path"] = Path.Combine(_cwd, "MauiProgram.cs")
            }
        }.ToJsonString();

        var env = CreateEnv("PostToolUse",
            _ => StubEvaluation("true", "Microsoft.Maui.Controls"),
            stdin: stdin);
        var output = await RunAndGetOutputAsync(env);
        Assert.Contains("set up DevFlow", output);
    }

    [Fact]
    public async Task DebounceState_WritesToPluginDataDirectory_NotRepo()
    {
        var env = CreateEnv("SessionStart",
            _ => StubEvaluation("true", "Microsoft.Maui.Controls"));
        _ = await RunAndGetOutputAsync(env);

        // Nothing should have been written inside the user's cwd.
        Assert.False(Directory.Exists(Path.Combine(_cwd, ".devflow")),
            ".devflow directory must not be created in the user's repo");

        // State should live under CLAUDE_PLUGIN_DATA/hook-state/*.json.
        var stateDir = Path.Combine(_pluginData, "hook-state");
        Assert.True(Directory.Exists(stateDir), "hook state dir expected under plugin data");
        Assert.NotEmpty(Directory.EnumerateFiles(stateDir, "*.json"));
    }

    [Fact]
    public void StubOverride_LoadsJsonFromDisk()
    {
        var stubPath = Path.Combine(_cwd, "stub.json");
        File.WriteAllText(stubPath,
            StubEvaluation("true", "Microsoft.Maui.Controls").ToJsonString());

        var env = new HookCheckCommand.HookEnvironment
        {
            EventName = "SessionStart",
            Cwd = _cwd,
            Stdin = new StringReader(string.Empty),
            Stdout = new StringWriter(),
            Stderr = new StringWriter(),
            Env = new Dictionary<string, string?>
            {
                ["MAUI_DEVFLOW_HOOK_STUB"] = stubPath
            }
        };

        var node = HookCheckCommand.EvaluateCsproj(env, Path.Combine(_cwd, "App.csproj"));
        Assert.NotNull(node);
        Assert.Equal("true", node!["Properties"]?["UseMaui"]?.GetValue<string>());
    }

    [Fact]
    public void PackageIdentities_ReturnsIdentities()
    {
        var node = StubEvaluation("true", "A", "B", "C");
        var ids = HookCheckCommand.PackageIdentities(node);
        Assert.Equal(new[] { "A", "B", "C" }, ids);
    }

    [Fact]
    public void DetectFlavor_BlazorGtk_WhenBothPresent()
    {
        var node = StubEvaluation(null,
            "Platform.Maui.Linux.Gtk4",
            "Microsoft.AspNetCore.Components.WebView.Maui");
        Assert.Equal("blazor-gtk", HookCheckCommand.DetectFlavor(node));
    }
}
