using Microsoft.Maui.Cli.DevFlow.Broker;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class BrokerClientSafetyTests
{
    [Fact]
    public void BrokerProcessIdentity_RequiresMatchingExecutableAndStartTime()
    {
        var started = new DateTime(2026, 7, 31, 9, 0, 0, DateTimeKind.Utc);
        var state = new BrokerState
        {
            Pid = 123,
            Port = BrokerServer.DefaultPort,
            StartedAt = started
        };
        var current = Path.Combine(Path.GetTempPath(), "maui.exe");

        Assert.True(BrokerClient.IsBrokerProcessIdentityMatch(
            state,
            started.AddSeconds(-2),
            current,
            current));
        Assert.False(BrokerClient.IsBrokerProcessIdentityMatch(
            state,
            started.AddMinutes(-5),
            current,
            current));
        Assert.False(BrokerClient.IsBrokerProcessIdentityMatch(
            state,
            started,
            Path.Combine(Path.GetTempPath(), "unrelated.exe"),
            current));

        var dotnet = Path.Combine(Path.GetTempPath(), "dotnet.exe");
        Assert.False(BrokerClient.IsBrokerProcessIdentityMatch(
            state,
            started,
            dotnet,
            dotnet));
    }
}
