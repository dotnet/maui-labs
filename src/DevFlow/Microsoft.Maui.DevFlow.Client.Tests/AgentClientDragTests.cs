using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

/// <summary>
/// How the portable client shapes a drag from a point, and how it reads the agent's answer, on
/// both target families.
/// </summary>
public class AgentClientDragTests
{
    [Fact]
    public async Task DragAsync_PostsTheStartAndEndPointsAsADragGesture()
    {
        using var agent = FakeAgent.StartJson(
            """{ "success": true, "type": "drag", "handledBy": "native", "detail": "MotionEvent drag" }""");
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var result = await client.DragAsync(
            "slider-1", 4, 12.5, toX: 180, toY: 12.5, holdMs: 300, durationMs: 400, steps: 20,
            captureEpoch: 7, registryGeneration: 2);

        Assert.True(result.Success);
        Assert.Equal("native", result.HandledBy);
        var request = Assert.Single(agent.Requests);
        Assert.Equal("/api/v1/ui/actions/gesture", request.Path);
        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal("drag", root.GetProperty("type").GetString());
        Assert.Equal("slider-1", root.GetProperty("elementId").GetString());
        Assert.Equal(4, root.GetProperty("x").GetDouble());
        Assert.Equal(12.5, root.GetProperty("y").GetDouble());
        Assert.Equal(180, root.GetProperty("toX").GetDouble());
        Assert.Equal(12.5, root.GetProperty("toY").GetDouble());
        Assert.Equal(300, root.GetProperty("holdMs").GetInt32());
        Assert.Equal(400, root.GetProperty("durationMs").GetInt32());
        Assert.Equal(20, root.GetProperty("steps").GetInt32());
        Assert.Equal(7, root.GetProperty("captureEpoch").GetInt64());
        Assert.False(root.TryGetProperty("toElementId", out _));
    }

    [Fact]
    public async Task DragAsync_OntoADropTarget_SendsOnlyWhatWasGiven()
    {
        using var agent = FakeAgent.StartJson("""{ "success": true, "type": "drag" }""");
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        await client.DragAsync("card-1", 10, 10, toElementId: "lane-2");

        var request = Assert.Single(agent.Requests);
        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal("lane-2", root.GetProperty("toElementId").GetString());
        Assert.False(root.TryGetProperty("toX", out _));
        Assert.False(root.TryGetProperty("toY", out _));
        Assert.False(root.TryGetProperty("holdMs", out _));
        Assert.False(root.TryGetProperty("captureEpoch", out _));
    }

    [Fact]
    public async Task DragAsync_AgentRefusalCarriesTheReason()
    {
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json(
            """{ "success": false, "type": "drag", "handledBy": "none", "error": "Start point (500, 4) is outside the element's 300x24 bounds" }""",
            statusCode: 400));
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var result = await client.DragAsync("slider-1", 500, 4, toX: 10, toY: 4);

        Assert.False(result.Success);
        Assert.Contains("outside the element", result.Error);
    }
}
