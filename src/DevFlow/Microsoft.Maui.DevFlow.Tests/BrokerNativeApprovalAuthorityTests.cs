using System.Reflection;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class BrokerNativeApprovalAuthorityTests
{
    [Fact]
    public void ProductionBroker_CreatesFixedTimeNativeApprovalAuthorityAndStateToken()
    {
        using var broker = new BrokerServer(port: 20771);

        var token = GetField<string>(broker, "_nativeApprovalToken");
        var verifier = GetField<Func<string?, bool>>(broker, "_trustedHostApprovalVerifier");

        Assert.Matches("^[A-Za-z0-9_-]{32,}$", token);
        Assert.True(verifier(token));
        Assert.False(verifier(token + "x"));
        Assert.False(verifier(null));

        var state = JsonSerializer.Serialize(new BrokerState
        {
            Pid = 1,
            Port = 20771,
            StartedAt = DateTime.UtcNow,
            NativeApprovalToken = token,
        });
        Assert.Contains("\"nativeApprovalToken\"", state, StringComparison.Ordinal);
        Assert.Contains(token, state, StringComparison.Ordinal);
    }

    [Fact]
    public void InternalBroker_CanExplicitlyLeaveNativeApprovalUnavailable()
    {
        using var broker = new BrokerServer(
            20772,
            TimeSpan.FromMinutes(1),
            MauiPreviewFeatureFlags.CreateDefault());

        Assert.Null(GetRawField(broker, "_trustedHostApprovalVerifier"));
        Assert.Null(GetRawField(broker, "_nativeApprovalToken"));
    }

    private static T GetField<T>(BrokerServer broker, string name)
        => Assert.IsType<T>(GetRawField(broker, name));

    private static object? GetRawField(BrokerServer broker, string name)
        => typeof(BrokerServer)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(broker);
}
