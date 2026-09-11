using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Flows;

/// <summary>
/// MCP tools for recorded workflow tests (<c>.md</c> files with a <c>```json maui-test</c> block).
/// Distinct from <c>maui_recording_*</c>, which is screen VIDEO capture.
/// </summary>
[McpServerToolType]
public sealed class FlowTools
{
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private const int MaxListResults = 500;

    [McpServerTool(Name = "maui_flow_replay"),
     Description("Replay a recorded workflow test (a .md file containing a ```json maui-test``` block) against the running app and return a per-step pass/fail report with assertion results. " +
                "WARNING: this DRIVES and MUTATES the live app — it performs the recorded taps, fills, scrolls, navigation, theme and property changes. Only replay .md files you trust. " +
                "Use maui_flow_validate to lint a file without running it.")]
    public static async Task<string> Replay(
        McpAgentSession session,
        [Description("Absolute path to the .md flow test file to replay")] string file,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var read = ReadFlowFile(file);
        if (read.Error is not null) return Error(read.Error);

        var parsed = FlowMarkdown.Parse(read.Text!, read.Path);
        if (!parsed.Ok) return Error(parsed.Error!);

        var validation = FlowValidator.Validate(parsed.Flow!);
        if (!validation.Ok)
            return Error("Flow failed validation: " + string.Join("; ", validation.Errors));

        using var agent = await session.GetAgentClientAsync(agentPort);
        var replayer = new FlowReplayer(agent);
        var report = await replayer.ReplayAsync(parsed.Flow!, read.Path);
        return Json(JsonSerializer.SerializeToNode(report, DevFlowCliJsonContext.Default.FlowReplayReport));
    }

    [McpServerTool(Name = "maui_flow_validate"),
     Description("Parse and lint a recorded workflow test (.md) WITHOUT running it. Returns the step count plus any errors and warnings (e.g. fragile selectors, unknown actions). Does not touch the running app.")]
    public static Task<string> Validate(
        McpAgentSession session,
        [Description("Absolute path to the .md flow test file to validate")] string file)
    {
        var read = ReadFlowFile(file);
        if (read.Error is not null) return Task.FromResult(Error(read.Error));

        var parsed = FlowMarkdown.Parse(read.Text!, read.Path);
        if (!parsed.Ok) return Task.FromResult(Error(parsed.Error!));

        var v = FlowValidator.Validate(parsed.Flow!);
        return Task.FromResult(Json(new JsonObject
        {
            ["ok"] = v.Ok,
            ["name"] = parsed.Flow!.Name,
            ["steps"] = parsed.Flow!.Steps.Count,
            ["errors"] = JsonSerializer.SerializeToNode(v.Errors, DevFlowCliJsonContext.Default.ListString),
            ["warnings"] = JsonSerializer.SerializeToNode(v.Warnings, DevFlowCliJsonContext.Default.ListString),
        }));
    }

    [McpServerTool(Name = "maui_flow_list"),
     Description("List recorded workflow tests (.md files) in a directory (non-recursive). Defaults to ./maui-tests under the current directory. Does not touch the running app.")]
    public static Task<string> List(
        McpAgentSession session,
        [Description("Directory to list .md flow tests from (default: ./maui-tests)")] string? directory = null)
    {
        string dir;
        try
        {
            dir = string.IsNullOrWhiteSpace(directory)
                ? Path.Combine(Directory.GetCurrentDirectory(), "maui-tests")
                : Path.GetFullPath(directory);
        }
        catch
        {
            return Task.FromResult(Error("Invalid directory path."));
        }

        if (!Directory.Exists(dir))
            return Task.FromResult(Json(new JsonObject
            {
                ["ok"] = true,
                ["directory"] = dir,
                ["tests"] = new JsonArray()
            }));

        try
        {
            var tests = Directory.EnumerateFiles(dir, "*.md", SearchOption.TopDirectoryOnly)
                .Take(MaxListResults)
                .Select(f => new FlowFileSummary(Path.GetFileNameWithoutExtension(f), f))
                .ToList();
            return Task.FromResult(Json(new JsonObject
            {
                ["ok"] = true,
                ["directory"] = dir,
                ["tests"] = JsonSerializer.SerializeToNode(tests, DevFlowCliJsonContext.Default.ListFlowFileSummary)
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Error($"Could not list tests: {ex.Message}"));
        }
    }

    /// <summary>A single .md flow test entry returned by <see cref="List"/>.</summary>
    internal sealed record FlowFileSummary(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("file")] string File);

    // ── File reading with validation (defence: .md only, existing regular file, size cap) ──
    private static (string? Path, string? Text, string? Error) ReadFlowFile(string file)
    {
        if (string.IsNullOrWhiteSpace(file))
            return (null, null, "A .md flow test file path is required.");

        string full;
        try
        {
            full = Path.GetFullPath(file);
        }
        catch
        {
            return (null, null, "Invalid file path.");
        }

        if (!full.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return (full, null, "Flow tests must be .md files.");
        if (!File.Exists(full))
            return (full, null, $"Flow test not found: {full}");

        try
        {
            var info = new FileInfo(full);
            if (info.Length > MaxFileBytes)
                return (full, null, "Flow file is too large to parse.");
            return (full, File.ReadAllText(full), null);
        }
        catch (Exception ex)
        {
            return (full, null, $"Could not read flow test: {ex.Message}");
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    private static string Json(JsonNode? value) => value?.ToJsonString(JsonOpts) ?? "null";
    private static string Error(string error) => Json(new JsonObject { ["ok"] = false, ["error"] = error });
}
