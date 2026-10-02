using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using ModelContextProtocol;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class EvidenceMcpTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devflow-evidence-mcp-{Guid.NewGuid():N}");

    public EvidenceMcpTests() => Directory.CreateDirectory(_root);
    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Preview_IncludesTheSameExplicitWorkflowAsCaptureWithoutTakingAScreenshot()
    {
        await using var agent = new EvidenceAgentFixture();
        var session = new McpAgentSession { DefaultAgentHost = "127.0.0.1" };
        var workflow = Path.Combine(_root, "repro.md");
        File.WriteAllText(workflow, "# Repro\n1. Tap OrderConfirmation");
        using var preview = JsonDocument.Parse(await EvidenceTools.Preview(
            session, agentPort: agent.Port, includeScreenshot: true, workflowFile: workflow));
        var included = preview.RootElement.GetProperty("included").EnumerateArray()
            .Select(entry => entry.GetProperty("name").GetString()).ToArray();
        Assert.Contains("workflow.md", included);
        Assert.Contains("screenshot.png", included);
        Assert.Equal(0, agent.ScreenshotRequests);
        using var captured = JsonDocument.Parse(await EvidenceTools.Capture(session,
            agentPort: agent.Port, outputPath: Path.Combine(_root, "capture.mauitrace"), workflowFile: workflow));
        Assert.True(captured.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("mcp", captured.RootElement.GetProperty("manifest").GetProperty("source").GetString());
    }

    [Fact]
    public async Task InvalidWorkflow_IsAToolErrorBeforeAnyAppRead()
    {
        await using var agent = new EvidenceAgentFixture();
        var session = new McpAgentSession { DefaultAgentHost = "127.0.0.1" };
        var missing = Path.Combine(_root, "missing.md");
        await Assert.ThrowsAsync<McpException>(() => EvidenceTools.Preview(session, agentPort: agent.Port, workflowFile: missing));
        await Assert.ThrowsAsync<McpException>(() => EvidenceTools.Capture(session, agentPort: agent.Port, workflowFile: missing));
        Assert.Empty(agent.Requests);
    }

    [Fact]
    public async Task NetworkRead_StrictOverloadSurfacesFailuresWithoutChangingLegacyBehavior()
    {
        await using var agent = new EvidenceAgentFixture(networkUnavailable: true);
        using var client = new AgentClient("127.0.0.1", agent.Port);
        Assert.Empty(await client.GetNetworkRequestsAsync());
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetNetworkRequestsAsync(100, CancellationToken.None));
    }

    [Fact]
    public async Task NetworkRead_CancellationPreventsTheRequest()
    {
        await using var agent = new EvidenceAgentFixture();
        using var client = new AgentClient("127.0.0.1", agent.Port);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetNetworkRequestsAsync(100, cancelled.Token));
        Assert.Empty(agent.Requests);
    }
}
