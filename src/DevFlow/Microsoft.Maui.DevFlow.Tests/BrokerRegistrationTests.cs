using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class BrokerRegistrationTests
{
    [Fact]
    public void BrokerHttpHosts_PrioritizesLiteralLoopbackWithLocalhostFallback()
    {
        Assert.Equal(new[] { "127.0.0.1", "localhost" }, BrokerRegistration.BrokerHttpHosts);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.BadRequest, true)]
    [InlineData(System.Net.HttpStatusCode.NotFound, true)]
    [InlineData(System.Net.HttpStatusCode.Conflict, false)]
    [InlineData(System.Net.HttpStatusCode.InternalServerError, false)]
    public void ShouldTryAlternateBrokerHost_ResponseStatus_ReturnsExpected(
        System.Net.HttpStatusCode statusCode,
        bool expected)
    {
        Assert.Equal(expected, BrokerRegistration.ShouldTryAlternateBrokerHost(statusCode));
    }
}
