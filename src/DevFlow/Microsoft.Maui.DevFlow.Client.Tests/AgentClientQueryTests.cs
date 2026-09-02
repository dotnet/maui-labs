using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

/// <summary>
/// Query-side coverage for the portable protocol client. These run on every target family the
/// package supports (net472 against the netstandard2.0 asset, and the modern .NET target), which
/// is what proves a .NET Framework harness sees identical DevFlow query behavior.
/// </summary>
public class AgentClientQueryTests
{
    private const string TwoElementPayload = """
        [
          {
            "id": "el-1",
            "type": "Button",
            "fullType": "Microsoft.Maui.Controls.Button",
            "automationId": "SubmitButton",
            "text": "Submit",
            "isVisible": true,
            "isEnabled": true,
            "captureEpoch": 7,
            "registryGeneration": 3,
            "bounds": { "x": 10, "y": 20, "width": 100, "height": 40 }
          },
          {
            "id": "el-2",
            "type": "Label",
            "fullType": "Microsoft.Maui.Controls.Label",
            "text": "Hello",
            "isVisible": true,
            "isEnabled": false
          }
        ]
        """;

    [Fact]
    public async Task QueryAsync_DeserializesElementsFromAgent()
    {
        using var agent = FakeAgent.StartJson(TwoElementPayload);
        using var client = new AgentClient("localhost", agent.Port);

        var elements = await client.QueryAsync(type: "Button", automationId: "SubmitButton");

        Assert.Equal(2, elements.Count);
        var button = elements[0];
        Assert.Equal("el-1", button.Id);
        Assert.Equal("Button", button.Type);
        Assert.Equal("SubmitButton", button.AutomationId);
        Assert.Equal("Submit", button.Text);
        Assert.True(button.IsVisible);
        Assert.True(button.IsEnabled);
        Assert.Equal(7, button.CaptureEpoch);
        Assert.Equal(3, button.RegistryGeneration);
        Assert.NotNull(button.Bounds);
        Assert.Equal(10, button.Bounds!.X);
        Assert.Equal(40, button.Bounds.Height);
        Assert.False(elements[1].IsEnabled);
    }

    [Fact]
    public async Task QueryAsync_SendsFiltersAsEscapedQueryString()
    {
        using var agent = FakeAgent.StartJson("[]");
        using var client = new AgentClient("localhost", agent.Port);

        await client.QueryAsync(type: "Button", automationId: "Submit Button", text: "a&b");

        var request = Assert.Single(agent.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/api/v1/ui/elements", request.Path);
        Assert.Equal("type=Button&automationId=Submit%20Button&text=a%26b", request.Query);
    }

    [Fact]
    public async Task QueryAsync_EmptyResultIsEmptyList()
    {
        using var agent = FakeAgent.StartJson("[]");
        using var client = new AgentClient("localhost", agent.Port);

        var elements = await client.QueryAsync(text: "nothing");

        Assert.Empty(elements);
    }

    [Fact]
    public async Task GetTreeAsync_RequestsDepthAndParsesTree()
    {
        using var agent = FakeAgent.StartJson(TwoElementPayload);
        using var client = new AgentClient("localhost", agent.Port);

        var tree = await client.GetTreeAsync(maxDepth: 3);

        var request = Assert.Single(agent.Requests);
        Assert.Equal("/api/v1/ui/tree", request.Path);
        Assert.Equal("depth=3", request.Query);
        Assert.Equal(2, tree.Count);
        Assert.Equal("el-1", tree[0].Id);
    }

    [Fact]
    public async Task GetElementAsync_ResolvesSingleElementById()
    {
        using var agent = FakeAgent.StartJson("""
            { "id": "el-1", "type": "Entry", "fullType": "Microsoft.Maui.Controls.Entry", "value": "typed" }
            """);
        using var client = new AgentClient("localhost", agent.Port);

        var element = await client.GetElementAsync("el-1");

        var request = Assert.Single(agent.Requests);
        Assert.Equal("/api/v1/ui/elements/el-1", request.Path);
        Assert.NotNull(element);
        Assert.Equal("Entry", element!.Type);
        Assert.Equal("typed", element.Value);
    }

    [Fact]
    public async Task GetStatusAsync_ReachesAgentThroughLocalhostAlias()
    {
        // Also covers the netstandard2.0 loopback fallback: the agent binds IPv4 only, while
        // "localhost" may resolve to ::1 first.
        using var agent = FakeAgent.StartJson("""
            { "running": true, "agent": { "version": "1.2.3", "frameworkId": "maui" }, "app": { "name": "Sample" } }
            """);
        using var client = new AgentClient("localhost", agent.Port);

        var status = await client.GetStatusAsync();

        Assert.NotNull(status);
        Assert.True(status!.Running);
        Assert.Equal("1.2.3", status.Version);
        Assert.Equal("Sample", status.AppName);
    }

    [Fact]
    public async Task GetCapabilitiesAsync_ReturnsRawJsonDocument()
    {
        using var agent = FakeAgent.StartJson("""{ "capabilities": { "ui.tree": true } }""");
        using var client = new AgentClient("localhost", agent.Port);

        var capabilities = await client.GetCapabilitiesAsync();

        Assert.Equal(JsonValueKind.Object, capabilities.ValueKind);
        Assert.True(capabilities.GetProperty("capabilities").GetProperty("ui.tree").GetBoolean());
    }
}
