using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

public class AgentClientHitTestTests
{
    private const string HitTestBody = """
        {"x":12.5,"y":34.25,"window":0,"captureEpoch":1,"registryGeneration":0,"elements":[{"id":"add-button"}],"futureWireField":"preserve"}
        """;

    [Theory]
    [InlineData("native-probe-busy")]
    [InlineData("capture-changed-during-read")]
    public async Task HitTestResultAsync_RetryableReadConflict_ReturnsUnmodifiedEnvelope(string reason)
    {
        var attempts = 0;
        using var agent = FakeAgent.Start(_ =>
            Interlocked.Increment(ref attempts) == 1
                ? FakeAgent.Response.Json(
                    $$$"""{"success":false,"reason":"{{{reason}}}","details":{"retryable":true}}""", 409)
                : FakeAgent.Response.Json(HitTestBody));
        using var client = new AgentClient("localhost", agent.Port);

        var result = await client.HitTestResultAsync(12.5, 34.25);

        Assert.True(result.Success);
        Assert.Equal(HitTestBody, result.Body);
        Assert.Equal(2, agent.Requests.Count);
        Assert.All(agent.Requests, request =>
        {
            Assert.Equal("GET", request.Method);
            Assert.Equal("/api/v1/ui/hit-test?x=12.5&y=34.25", request.Target);
            Assert.Empty(request.Body);
        });
    }

    [Fact]
    public async Task HitTestResultAsync_UnexpectedConflict_DoesNotRetry()
    {
        const string body = """{"success":false,"reason":"unexpected-conflict"}""";
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json(body, 409));
        using var client = new AgentClient("localhost", agent.Port);

        var result = await client.HitTestResultAsync(12.5, 34.25);

        Assert.False(result.Success);
        Assert.False(result.Retryable);
        Assert.Equal(body, result.Body);
        Assert.Single(agent.Requests);
    }

    [Fact]
    public async Task HitTestResultAsync_PersistentNativeProbeBusy_ReturnsFailure()
    {
        const string body = """{"success":false,"reason":"native-probe-busy","details":{"retryable":true}}""";
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json(body, 409));
        using var client = new AgentClient("localhost", agent.Port);

        var result = await client.HitTestResultAsync(12.5, 34.25);

        Assert.False(result.Success);
        Assert.True(result.Retryable);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(body, result.Body);
        Assert.NotEmpty(agent.Requests);
        Assert.All(agent.Requests, request => Assert.Equal("GET", request.Method));
    }
}
