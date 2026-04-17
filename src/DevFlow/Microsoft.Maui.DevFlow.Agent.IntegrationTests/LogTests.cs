using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "Logs")]
public class LogTests : IntegrationTestBase
{
    public LogTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    [Fact]
    public async Task Logs_ReturnsEntries()
    {
        var logs = await Client.GetLogsAsync();

        Assert.NotNull(logs);
        Output.WriteLine($"Log output length: {logs.Length} chars");
    }

    [Fact]
    public async Task Logs_WithLimit_RespectsLimit()
    {
        var allLogs = await Client.GetLogsAsync(limit: 100);
        var limitedLogs = await Client.GetLogsAsync(limit: 5);

        Assert.NotNull(allLogs);
        Assert.NotNull(limitedLogs);
        Assert.True(limitedLogs.Length <= allLogs.Length,
            $"Limited logs ({limitedLogs.Length}) should be <= all logs ({allLogs.Length})");
    }

    [Fact]
    public async Task Logs_WithSource_FiltersResults()
    {
        var nativeLogs = await Client.GetLogsAsync(source: "native");

        Assert.NotNull(nativeLogs);
        Output.WriteLine($"Native source logs length: {nativeLogs.Length} chars");
    }
}
