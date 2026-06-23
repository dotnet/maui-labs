using System.Net;
using System.Net.Sockets;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Covers the broker-liveness probe parity behind issue #342: `agent status` resolves the broker
/// through the sync <c>GetRunningBrokerPort</c> while `diagnose` uses the async
/// <c>GetRunningBrokerPortAsync</c>. If the two probes use different loopback strategies they can
/// disagree about whether the broker is running — the exact status-vs-diagnose contradiction in the
/// issue. These tests assert the two probes always agree.
/// </summary>
[Collection("CLI")]
public class BrokerClientLivenessTests
{
    [Fact]
    public async Task SyncAndAsyncProbes_Agree_WhenBrokerListensOnIPv4LoopbackOnly()
    {
        using var listener = StartLoopbackListener(IPAddress.Loopback, out var port);

        await WithBrokerStateAsync(port, async () =>
        {
            var sync = BrokerClient.GetRunningBrokerPort();
            var async = await BrokerClient.GetRunningBrokerPortAsync();

            Assert.Equal(port, sync);
            Assert.Equal(port, async);
            Assert.Equal(sync, async);
        });
    }

    [Fact]
    public async Task SyncAndAsyncProbes_Agree_WhenBrokerListensOnIPv6LoopbackOnly()
    {
        TcpListener? listener;
        int port;
        try
        {
            listener = StartLoopbackListener(IPAddress.IPv6Loopback, out port);
        }
        catch (SocketException)
        {
            // IPv6 loopback unavailable on this host — nothing to assert.
            return;
        }

        using (listener)
        {
            await WithBrokerStateAsync(port, async () =>
            {
                var sync = BrokerClient.GetRunningBrokerPort();
                var async = await BrokerClient.GetRunningBrokerPortAsync();

                Assert.Equal(port, sync);
                Assert.Equal(port, async);
                Assert.Equal(sync, async);
            });
        }
    }

    [Fact]
    public async Task SyncAndAsyncProbes_Agree_WhenBrokerIsNotListening()
    {
        var port = GetFreePort(); // recorded in state but nothing is bound to it

        await WithBrokerStateAsync(port, async () =>
        {
            var sync = BrokerClient.GetRunningBrokerPort();
            var async = await BrokerClient.GetRunningBrokerPortAsync();

            Assert.Null(sync);
            Assert.Null(async);
            Assert.Equal(sync, async);
        });
    }

    private static async Task WithBrokerStateAsync(int port, Func<Task> body)
    {
        var tempDir = Directory.CreateTempSubdirectory("maui-broker-probe-");
        var previousOverride = BrokerPaths.ConfigDirOverride;
        BrokerPaths.ConfigDirOverride = tempDir.FullName;

        try
        {
            Directory.CreateDirectory(BrokerPaths.ConfigDir);
            File.WriteAllText(
                BrokerPaths.StateFile,
                $"{{\"pid\":0,\"port\":{port},\"startedAt\":\"2026-01-01T00:00:00Z\"}}");

            await body();
        }
        finally
        {
            BrokerPaths.ConfigDirOverride = previousOverride;
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    private static TcpListener StartLoopbackListener(IPAddress address, out int port)
    {
        var listener = new TcpListener(address, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}
