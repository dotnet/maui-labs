using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using ModelContextProtocol;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class DevFlowMcpEditingToolsTests
{
    [Fact]
    public async Task AddElement_ReportsNewElement()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();

        var result = await EditingTools.AddElement(CreateSession(server.Port), "stack-1", "<Label />", index: 0, agentPort: server.Port);

        Assert.Contains("new-1", result);
        Assert.Contains("index 0", result);
    }

    [Fact]
    public async Task AddElement_Rejection_ThrowsWithReason()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();

        var ex = await Assert.ThrowsAsync<McpException>(
            () => EditingTools.AddElement(CreateSession(server.Port), "leaf", "<Label />", agentPort: server.Port));

        Assert.Contains("not-a-container", ex.Message);
    }

    [Fact]
    public async Task RemoveAndMoveElement_Succeed()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var session = CreateSession(server.Port);

        Assert.Contains("parent-1", await EditingTools.RemoveElement(session, "el-1", agentPort: server.Port));
        Assert.Contains("parent-2", await EditingTools.MoveElement(session, "el-1", "parent-2", 1, agentPort: server.Port));
    }

    [Fact]
    public async Task ReloadXaml_RequiresExactlyOneSource()
    {
        var session = CreateSession(1);

        await Assert.ThrowsAsync<McpException>(() => EditingTools.ReloadXaml(session));
        await Assert.ThrowsAsync<McpException>(() => EditingTools.ReloadXaml(session, xaml: "<a/>", filePath: "b.xaml"));
    }

    [Fact]
    public async Task ReloadXaml_FromFile_ForwardsThePathAsSourceFile()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var file = Path.Combine(Path.GetTempPath(), $"mcp-reload-{Guid.NewGuid():N}.xaml");
        await File.WriteAllTextAsync(file, "<ContentPage x:Class=\"App.MainPage\" />");
        try
        {
            await EditingTools.ReloadXaml(CreateSession(server.Port), filePath: file, agentPort: server.Port);

            var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/xaml/reload");
            Assert.Equal(file, JsonDocument.Parse(request.Body!).RootElement.GetProperty("sourceFile").GetString());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ReloadXaml_ParseError_IncludesLocation()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();

        var ex = await Assert.ThrowsAsync<McpException>(
            () => EditingTools.ReloadXaml(CreateSession(server.Port), xaml: "<Broken />", agentPort: server.Port));

        Assert.Contains("line 3", ex.Message);
    }

    [Fact]
    public async Task HighlightAndClearProperty_Succeed()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var session = CreateSession(server.Port);

        Assert.Equal("Highlighted 'el-1'.", await EditingTools.HighlightElement(session, "el-1", server.Port));
        Assert.Equal("Cleared highlight.", await EditingTools.HighlightElement(session, null, server.Port));
        Assert.Equal("Cleared 'FontSize' on element 'el-1'.", await PropertyTools.ClearProperty(session, "el-1", "FontSize", server.Port));
    }

    private static McpAgentSession CreateSession(int port)
        => new()
        {
            DefaultAgentHost = "127.0.0.1",
            DefaultAgentPort = port
        };
}
