using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.DevFlow.Driver;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// The MCP tool surface for on-demand diagnostics.
///
/// Tool descriptions are the only contract an AI agent reads before calling, so they are asserted
/// like code: the layout tool must state that it cannot prove clipping/occlusion and that missing
/// geometry is incomplete, and the performance tools must state that they are triage with a native
/// profiler handoff.
/// </summary>
public class DiagnosticsMcpToolTests
{
    [Theory]
    [InlineData(typeof(LayoutDiagnosticsTool), "maui_layout_diagnostics")]
    [InlineData(typeof(PerformanceTools), "maui_performance_start")]
    [InlineData(typeof(PerformanceTools), "maui_performance_snapshot")]
    [InlineData(typeof(PerformanceTools), "maui_performance_stop")]
    public void ToolsAreRegisteredWithTheExpectedNames(Type toolType, string toolName)
    {
        Assert.NotNull(toolType.GetCustomAttribute<McpServerToolTypeAttribute>());
        Assert.NotNull(FindTool(toolType, toolName));
    }

    [Fact]
    public void LayoutToolDescriptionRequiresAuthoritativeEvidence()
    {
        var description = DescriptionOf(typeof(LayoutDiagnosticsTool), "maui_layout_diagnostics");

        Assert.Contains("read-only", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("authoritative visible region", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("geometric overlap never implies visual", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("incomplete", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("coverage", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no watch mode", description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("maui_performance_start")]
    [InlineData("maui_performance_snapshot")]
    [InlineData("maui_performance_stop")]
    public void PerformanceToolDescriptionsEmphasizeTriageAndHandoff(string toolName)
    {
        var description = DescriptionOf(typeof(PerformanceTools), toolName);

        Assert.Contains("triage", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("native profiler", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("estimated frame rates are never shown", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryToolParameterIsDescribedForTheCallingAgent()
    {
        foreach (var toolType in new[] { typeof(LayoutDiagnosticsTool), typeof(PerformanceTools) })
        {
            foreach (var method in toolType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null))
            {
                foreach (var parameter in method.GetParameters().Where(p =>
                    p.ParameterType != typeof(McpAgentSession) &&
                    p.ParameterType != typeof(RequestContext<CallToolRequestParams>) &&
                    p.ParameterType != typeof(CancellationToken)))
                {
                    Assert.True(
                        parameter.GetCustomAttribute<DescriptionAttribute>() is not null,
                        $"{toolType.Name}.{method.Name} parameter '{parameter.Name}' is missing a [Description].");
                }
            }
        }
    }

    [Fact]
    public async Task LayoutTool_ReturnsTheAgentReportAsJson()
    {
        await using var server = new Fixtures.MockAgentServer();
        await server.StartAsync();
        var session = new McpAgentSession { DefaultAgentHost = "localhost" };

        var result = await LayoutDiagnosticsTool.GetLayoutDiagnostics(session, server.Port, maxElements: 50);
        var json = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

        using var document = JsonDocument.Parse(json!);
        Assert.Equal("1.0", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("summary").GetProperty("violations").GetInt32());
        Assert.NotEmpty(document.RootElement.GetProperty("coverage").GetProperty("limitations").EnumerateArray());
        var request = Assert.Single(server.RecordedRequests, recorded =>
            recorded.Path == "/api/v1/ui/diagnostics/layout");
        Assert.Equal("POST", request.Method);
        using var requestBody = JsonDocument.Parse(request.Body!);
        Assert.Equal(50, requestBody.RootElement.GetProperty("maxElements").GetInt32());
        Assert.Equal("info", requestBody.RootElement.GetProperty("minimumSeverity").GetString());
        Assert.Equal(
            "activePage",
            requestBody.RootElement.GetProperty("scope").GetProperty("mode").GetString());
        Assert.Equal("agent", requestBody.RootElement.GetProperty("profile").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5001)]
    public async Task LayoutTool_RejectsOutOfRangeElementBudgets(int maxElements)
    {
        var session = new McpAgentSession { DefaultAgentHost = "localhost" };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            LayoutDiagnosticsTool.GetLayoutDiagnostics(session, agentPort: 1, maxElements: maxElements));
    }

    [Fact]
    public async Task PerformanceSnapshotTool_NeverReportsEstimatedFrameRates()
    {
        await using var server = new Fixtures.MockAgentServer();
        await server.StartAsync();
        var session = new McpAgentSession { DefaultAgentHost = "localhost" };

        var json = await PerformanceTools.SnapshotPerformance(session, server.Port);

        using var document = JsonDocument.Parse(json);
        var frames = document.RootElement.GetProperty("frames");
        Assert.False(frames.GetProperty("supported").GetBoolean());
        Assert.False(frames.TryGetProperty("averageFps", out var fps) && fps.ValueKind == JsonValueKind.Number);
        Assert.True(document.RootElement.GetProperty("loss").GetProperty("anyLoss").GetBoolean());
        Assert.NotEmpty(document.RootElement.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public async Task PerformanceSnapshotTool_RejectsAReplacementSession()
    {
        await using var server = new Fixtures.MockAgentServer();
        await server.StartAsync();
        var session = new McpAgentSession { DefaultAgentHost = "localhost" };

        var valid = await PerformanceTools.SnapshotPerformance(
            session,
            server.Port,
            sessionId: "session-1");
        using var document = JsonDocument.Parse(valid);
        Assert.Equal("session-1", document.RootElement.GetProperty("session").GetProperty("sessionId").GetString());

        await Assert.ThrowsAsync<ProfilerSessionMismatchException>(() =>
            PerformanceTools.SnapshotPerformance(session, server.Port, sessionId: "stale-session"));
    }

    private static MethodInfo? FindTool(Type toolType, string toolName)
        => toolType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name == toolName);

    private static string DescriptionOf(Type toolType, string toolName)
    {
        var method = FindTool(toolType, toolName)
            ?? throw new InvalidOperationException($"Tool '{toolName}' was not found on {toolType.Name}.");
        return method.GetCustomAttribute<DescriptionAttribute>()?.Description
            ?? throw new InvalidOperationException($"Tool '{toolName}' has no description.");
    }
}
