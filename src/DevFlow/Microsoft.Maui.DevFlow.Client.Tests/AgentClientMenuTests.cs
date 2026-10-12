using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

public class AgentClientMenuTests
{
    [Fact]
    public async Task GetMenusAsync_SendsWindowScope()
    {
        using var agent = FakeAgent.StartJson("""{"menuBar":{"items":[]},"native":null}""");
        using var client = new AgentClient("localhost", agent.Port);

        var result = await client.GetMenusAsync(window: 2);

        var request = Assert.Single(agent.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/api/v1/ui/menus", request.Path);
        Assert.Equal("window=2", request.Query);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("native").ValueKind);
    }

    [Fact]
    public async Task InvokeMenuAsync_SendsSelectorAndWindow_AndPreservesErrorBody()
    {
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json(
            """{"success":false,"error":"Menu item is disabled","reason":"menu-not-invokable"}""", 409));
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var result = await client.InvokeMenuAsync(path: "File/Save%2FExport", target: "maui", window: 1);

        var request = Assert.Single(agent.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/api/v1/ui/menus/invoke", request.Path);
        using var payload = JsonDocument.Parse(request.Body);
        Assert.Equal("File/Save%2FExport", payload.RootElement.GetProperty("path").GetString());
        Assert.Equal("maui", payload.RootElement.GetProperty("target").GetString());
        Assert.Equal(1, payload.RootElement.GetProperty("window").GetInt32());
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal("Menu item is disabled", result.GetProperty("error").GetString());
    }
}
