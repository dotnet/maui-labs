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

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(501, null)]
    [InlineData(null, 0)]
    [InlineData(null, -1)]
    [InlineData(null, 501)]
    public async Task InvalidLimits_AreToolErrorsBeforeAnyAppRead(int? logLimit, int? networkLimit)
    {
        await using var agent = new EvidenceAgentFixture();
        var session = new McpAgentSession { DefaultAgentHost = "127.0.0.1" };

        var preview = await Assert.ThrowsAsync<McpException>(() => EvidenceTools.Preview(
            session, agentPort: agent.Port, logLimit: logLimit, networkLimit: networkLimit));
        var capture = await Assert.ThrowsAsync<McpException>(() => EvidenceTools.Capture(
            session, agentPort: agent.Port, includeScreenshot: true,
            logLimit: logLimit, networkLimit: networkLimit));

        Assert.Contains("between 1 and 500", preview.Message, StringComparison.Ordinal);
        Assert.Contains("between 1 and 500", capture.Message, StringComparison.Ordinal);
        Assert.Empty(agent.Requests);
    }

    [Fact]
    public async Task ExplicitOutputErrors_AreToolErrorsBeforeAnyAppRead()
    {
        await using var agent = new EvidenceAgentFixture();
        var session = new McpAgentSession { DefaultAgentHost = "127.0.0.1" };
        var existing = Path.Combine(_root, "existing.mauitrace");
        File.WriteAllText(existing, "original");
        var invalid = Path.Combine(_root, "bundle.zip");

        await Assert.ThrowsAsync<McpException>(() => EvidenceTools.Preview(
            session, agentPort: agent.Port, outputPath: invalid));
        foreach (var path in new[] { invalid, existing })
        {
            await Assert.ThrowsAsync<McpException>(() => EvidenceTools.Capture(
                session, agentPort: agent.Port, outputPath: path, includeScreenshot: true));
        }
        Assert.Empty(agent.Requests);
        Assert.Equal("original", File.ReadAllText(existing));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    public async Task Preview_AcceptsLimitBoundsWithoutChangingThem(int limit)
    {
        await using var agent = new EvidenceAgentFixture();
        var session = new McpAgentSession { DefaultAgentHost = "127.0.0.1" };

        using var preview = JsonDocument.Parse(await EvidenceTools.Preview(
            session, agentPort: agent.Port, logLimit: limit, networkLimit: limit));

        var limits = preview.RootElement.GetProperty("limits");
        Assert.Equal(limit, limits.GetProperty("logs").GetInt32());
        Assert.Equal(limit, limits.GetProperty("network").GetInt32());
    }

    [Fact]
    public async Task AgentFixture_DisposalWhileAcceptingConnectionsCompletesCleanly()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var agent = new EvidenceAgentFixture();
            await agent.DisposeAsync();
        }
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
