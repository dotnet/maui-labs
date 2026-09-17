using System.Text.Json;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class DevFlowCliEditingCommandTests
{
    private static async Task<(MockAgentServer server, CliTestHarness cli)> CreateFixturesAsync()
    {
        var server = new MockAgentServer();
        await server.StartAsync();
        return (server, new CliTestHarness(server.Port));
    }

    [Fact]
    public async Task UiAddElement_PostsXamlAndIndex_AndReportsNewElement()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "add-element", "stack-1", "<Label Text=\"Hi\" />", "--index", "0", "--json");

        Assert.Equal(0, result.ExitCode);
        var request = Assert.Single(server.RecordedRequests, r => r.Path.EndsWith("/children", StringComparison.Ordinal));
        Assert.Equal("POST", request.Method);
        Assert.Equal("/api/v1/ui/elements/stack-1/children", request.Path);
        Assert.Contains("Label Text=", request.Body);
        Assert.Contains("\"index\":0", request.Body);
        var output = result.ParseJsonOutput();
        Assert.Equal("new-1", output.GetProperty("elementId").GetString());
        Assert.Equal("stack-1", output.GetProperty("parentId").GetString());
    }

    [Fact]
    public async Task UiAddElement_Rejection_SurfacesReasonAndFails()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "add-element", "leaf", "<Button />", "--json");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("not-a-container", result.StdErr + result.StdOut);
    }

    [Fact]
    public async Task UiRemoveElement_SendsDelete()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "remove-element", "el-1", "--json");

        Assert.Equal(0, result.ExitCode);
        var request = Assert.Single(server.RecordedRequests, r => r.Method == "DELETE");
        Assert.Equal("/api/v1/ui/elements/el-1", request.Path);
        Assert.Equal("parent-1", result.ParseJsonOutput().GetProperty("parentId").GetString());
    }

    [Fact]
    public async Task UiMoveElement_PostsParentAndIndex()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "move-element", "el-1", "parent-2", "--index", "1", "--json");

        Assert.Equal(0, result.ExitCode);
        var request = Assert.Single(server.RecordedRequests, r => r.Path.EndsWith("/move", StringComparison.Ordinal));
        Assert.Equal("/api/v1/ui/elements/el-1/move", request.Path);
        Assert.Contains("\"parentId\":\"parent-2\"", request.Body);
        Assert.Contains("\"index\":1", request.Body);
    }

    [Fact]
    public async Task UiClearProperty_SendsDeleteToPropertyRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "clear-property", "el-1", "FontSize", "--json");

        Assert.Equal(0, result.ExitCode);
        var request = Assert.Single(server.RecordedRequests, r => r.Method == "DELETE");
        Assert.Equal("/api/v1/ui/elements/el-1/properties/FontSize", request.Path);
    }

    [Fact]
    public async Task UiReloadXaml_SendsFileContents()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;
        var file = Path.Combine(Path.GetTempPath(), $"reload-{Guid.NewGuid():N}.xaml");
        await File.WriteAllTextAsync(file, "<ContentPage x:Class=\"App.MainPage\" />");
        try
        {
            var result = await cli.InvokeAsync("devflow", "ui", "reload-xaml", file, "--json");

            Assert.Equal(0, result.ExitCode);
            var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/xaml/reload");
            Assert.Contains("App.MainPage", request.Body);
            // the agent needs the path to build a source map when it has no build-time one
            Assert.Equal(file, JsonDocument.Parse(request.Body!).RootElement.GetProperty("sourceFile").GetString());
            var output = result.ParseJsonOutput();
            Assert.Equal(1, output.GetProperty("reloaded").GetInt32());
            Assert.Equal("abc123", output.GetProperty("sourceHash").GetString());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task UiReloadXaml_ParseError_ReportsLocationAndFails()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;
        var file = Path.Combine(Path.GetTempPath(), $"broken-{Guid.NewGuid():N}.xaml");
        await File.WriteAllTextAsync(file, "<ContentPage><Broken /></ContentPage>");
        try
        {
            var result = await cli.InvokeAsync("devflow", "ui", "reload-xaml", file, "--json");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(":3:5", result.StdErr + result.StdOut);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task UiReloadXaml_MissingFile_FailsWithoutCallingAgent()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "reload-xaml", "/does/not/exist.xaml", "--json");

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path == "/api/v1/ui/xaml/reload");
    }

    [Theory]
    [InlineData(new[] { "el-1" }, "\"elementId\":\"el-1\"")]
    [InlineData(new string[0], "\"elementId\":null")]
    public async Task UiHighlight_PutsElementOrClears(string[] args, string expectedBody)
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync(["devflow", "ui", "highlight", .. args, "--json"]);

        Assert.Equal(0, result.ExitCode);
        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/highlight");
        Assert.Equal("PUT", request.Method);
        Assert.Contains(expectedBody, request.Body);
    }
}
