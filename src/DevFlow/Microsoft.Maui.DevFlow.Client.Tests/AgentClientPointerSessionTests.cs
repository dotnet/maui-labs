using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

public class AgentClientPointerSessionTests
{
    [Fact]
    public async Task PointerSessionLifecycle_UsesVersionedRoutesAndCamelCasePayloads()
    {
        using var agent = FakeAgent.Start(request => FakeAgent.Response.Json(
            request.Path.EndsWith("/end", StringComparison.Ordinal)
                ? """{"success":true,"sessionId":"pointer-1","state":"ended","lastSequence":2}"""
                : """{"success":true,"sessionId":"pointer-1","state":"active","lastSequence":1,"maxSamplesPerMove":64}"""));
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var start = await client.StartPointerSessionAsync(new PointerSessionStartRequest
        {
            SessionId = "pointer-1",
            PointerId = 7,
            X = 10,
            Y = 20,
            CaptureEpoch = 5,
            RegistryGeneration = 3,
            Correlation = new ActionCorrelation
            {
                Source = "inspector",
                FrameId = "frame-1",
                CoordinateSpace = "windowLogical"
            }
        });
        var move = await client.MovePointerSessionAsync(
            "pointer-1",
            1,
            [new PointerSessionSample { X = 11, Y = 21, ElapsedMs = 8 }]);
        var end = await client.EndPointerSessionAsync(
            "pointer-1",
            2,
            new PointerSessionSample { X = 12, Y = 22, ElapsedMs = 16 });

        Assert.True(start.Success);
        Assert.True(move.Success);
        Assert.True(end.Success);
        Assert.Equal(64, move.MaxSamplesPerMove);
        Assert.Equal(
            [
                "/api/v1/ui/actions/pointer-sessions",
                "/api/v1/ui/actions/pointer-sessions/pointer-1/move",
                "/api/v1/ui/actions/pointer-sessions/pointer-1/end"
            ],
            agent.Requests.Select(request => request.Path));

        using var startBody = JsonDocument.Parse(agent.Requests[0].Body);
        Assert.Equal("windowLogical", startBody.RootElement.GetProperty("coordinateSpace").GetString());
        Assert.Equal(5, startBody.RootElement.GetProperty("captureEpoch").GetInt64());
        Assert.Equal("frame-1", startBody.RootElement.GetProperty("correlation").GetProperty("frameId").GetString());

        using var moveBody = JsonDocument.Parse(agent.Requests[1].Body);
        Assert.Equal(1, moveBody.RootElement.GetProperty("sequence").GetInt32());
        Assert.Equal(8, moveBody.RootElement.GetProperty("samples")[0].GetProperty("elapsedMs").GetDouble());
    }

    [Fact]
    public async Task PointerStart_NotSupported_IsReturnedWithoutThrowing()
    {
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json(
            """{"error":"not_supported","reason":"The backend does not support pointer streaming.","capability":"ui.pointerStream"}""",
            statusCode: 501));
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var result = await client.StartPointerSessionAsync(new PointerSessionStartRequest
        {
            SessionId = "unsupported",
            PointerId = 1,
            X = 1,
            Y = 2
        });

        Assert.False(result.Success);
        Assert.True(result.Unsupported);
        Assert.Equal(501, result.StatusCode);
    }
}
