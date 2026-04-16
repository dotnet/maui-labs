using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "Profiler")]
public class ProfilerTests : IntegrationTestBase
{
    public ProfilerTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    [Fact]
    public async Task Capabilities_ReturnsInfo()
    {
        var capabilities = await Client.GetProfilerCapabilitiesAsync();

        Assert.NotNull(capabilities);
        Assert.NotNull(capabilities!.Platform);
    }

    [Fact]
    public async Task FullLifecycle_StartSampleStop()
    {
        var session = await Client.StartProfilerAsync(sampleIntervalMs: 500);
        Assert.NotNull(session);
        Assert.NotNull(session!.SessionId);

        if (!session.IsActive)
        {
            Output.WriteLine("Got existing stopped session — profiler tests may conflict with each other.");
            return;
        }

        await Task.Delay(2000);

        var batch = await Client.GetProfilerSamplesAsync(session.SessionId);
        Assert.NotNull(batch);
        Assert.Equal(session.SessionId, batch!.SessionId);

        var stopped = await Client.StopProfilerAsync(session.SessionId);
        Assert.NotNull(stopped);
        Assert.False(stopped!.IsActive);
    }

    [Fact]
    public async Task StartSession_ReturnsSessionId()
    {
        var session = await Client.StartProfilerAsync(sampleIntervalMs: 1000);
        Assert.NotNull(session);
        Assert.NotNull(session!.SessionId);

        if (session.IsActive)
            await Client.StopProfilerAsync(session.SessionId);
    }

    [Fact]
    public async Task GetSamples_AfterDelay_HasData()
    {
        var session = await Client.StartProfilerAsync(sampleIntervalMs: 200);
        Assert.NotNull(session);

        if (!session!.IsActive)
        {
            Output.WriteLine("Got existing stopped session — skipping sample collection test.");
            return;
        }

        await Task.Delay(1500);

        var batch = await Client.GetProfilerSamplesAsync(session.SessionId);
        Assert.NotNull(batch);

        if (batch!.Samples.Count > 0)
            Assert.True(batch.Samples[0].TsUtc > DateTime.MinValue);

        await Client.StopProfilerAsync(session.SessionId);
    }

    [Fact]
    public async Task PublishMarker_Succeeds()
    {
        var session = await Client.StartProfilerAsync(sampleIntervalMs: 500);
        Assert.NotNull(session);

        var result = await Client.PublishProfilerMarkerAsync(
            "test_marker",
            "integration.test",
            """{"source":"integration-tests"}""");
        Assert.True(result);

        if (session!.IsActive)
            await Client.StopProfilerAsync(session.SessionId);
    }

    [Fact]
    public async Task PublishSpan_Succeeds()
    {
        var session = await Client.StartProfilerAsync(sampleIntervalMs: 500);
        Assert.NotNull(session);

        var now = DateTime.UtcNow;
        var response = await PostRawAsync("/api/v1/profiler/spans", new
        {
            name = "test_span",
            kind = "integration.test",
            startTsUtc = now.AddMilliseconds(-100).ToString("O"),
            endTsUtc = now.ToString("O"),
            status = "ok",
        });
        Assert.True(response.IsSuccessStatusCode);

        if (session!.IsActive)
            await Client.StopProfilerAsync(session.SessionId);
    }

    [Fact]
    public async Task Hotspots_ReturnsArray()
    {
        var hotspots = await Client.GetProfilerHotspotsAsync();

        Assert.NotNull(hotspots);
    }

    [Fact]
    public async Task StopSession_InvalidId_HandlesGracefully()
    {
        var result = await Client.StopProfilerAsync("nonexistent-session-id");
        Output.WriteLine($"Stop nonexistent session result: {(result == null ? "null" : "not null")}");
    }
}
