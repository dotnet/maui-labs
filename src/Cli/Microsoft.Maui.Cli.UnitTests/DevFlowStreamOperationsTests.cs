using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class LogOperationsTests
{
    [Fact]
    public async Task Get_ForwardsPagingAndSource()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var body = await new LogOperations(client).GetAsync(7, 2, "webview & app");

        Assert.StartsWith("[", body);
        var request = Assert.Single(server.RecordedRequests);
        Assert.Contains("limit=7", request.QueryString);
        Assert.Contains("skip=2", request.QueryString);
        Assert.Contains("source=webview%20%26%20app", request.QueryString);
    }

    [Fact]
    public async Task Follow_DecodesFragmentedReplayAndLiveEntriesAndCancels()
    {
        await using var server = new MockAgentServer { StreamFragmentSize = 5 };
        server.StreamMessages["logs"] =
        [
            "invalid JSON",
            """{"type":"replay","entries":[{"m":"first"}]}""",
            """{"type":"log","entry":{"m":"second"}}"""
        ];
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var entries = new List<string>();
        var connections = 0;

        await new LogOperations(client).FollowAsync("webview & app", 2, () => connections++, entry =>
        {
            entries.Add(entry.GetProperty("m").GetString()!);
            if (entries.Count == 2) cts.Cancel();
        }, cts.Token);

        Assert.Equal(["first", "second"], entries);
        Assert.Equal(1, connections);
        var request = Assert.Single(server.RecordedRequests);
        Assert.Equal("?source=webview%20%26%20app&replay=2", request.QueryString);
        Assert.NotNull(await client.GetStatusAsync());
    }
}

[Collection("CLI")]
public sealed class NetworkOperationsTests
{
    [Fact]
    public async Task FetchAndClear_UseExistingAgentProtocol()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var operations = new NetworkOperations(client);

        Assert.Empty(await operations.ListAsync(7, "example.test", "POST"));
        Assert.Equal("request-1", (await operations.DetailAsync("request-1"))!.Id);
        Assert.True(await operations.ClearAsync());

        Assert.Contains(server.RecordedRequests, r => r.Method == "DELETE" && r.Path == "/api/v1/network/requests");
        Assert.NotNull(await client.GetStatusAsync());
    }

    [Fact]
    public async Task Monitor_PreservesReplayLimitAndFilteredLiveCounting()
    {
        await using var server = new MockAgentServer { StreamFragmentSize = 7 };
        server.StreamMessages["network"] =
        [
            """{"type":"replay","entries":[{"host":"api.test","method":"GET","id":"one"},{"host":"api.test","method":"GET","id":"two"},{"host":"other","method":"GET","id":"filtered"}]}""",
            """{"type":"request","entry":{"host":"api.test","method":"GET","id":"over-limit"}}""",
            """{"type":"request","entry":{"host":"api.test","method":"POST","id":"filtered-method"}}""",
            """{"type":"replay","entries":[{"host":"api.test","method":"GET","id":"complete"}]}"""
        ];
        await server.StartAsync();
        using var client = new AgentClient("127.0.0.1", server.Port);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var entries = new List<string>();
        var completed = false;

        var count = await new NetworkOperations(client).MonitorAsync(1, "API", "get", _ => { },
            (_, entry) =>
            {
                var id = entry.GetProperty("id").GetString()!;
                if (id == "complete")
                {
                    completed = true;
                    cts.Cancel();
                }
                else
                {
                    entries.Add(id);
                }
            },
            ex => throw new InvalidOperationException("Unexpected stream error", ex), cts.Token);

        Assert.True(completed, "The client did not process the complete ordered stream within 30 seconds.");
        Assert.Equal(["one", "two"], entries);
        // Two replay entries, one suppressed live entry, and the completion marker are counted.
        Assert.Equal(4, count);
    }

    [Theory]
    [InlineData("""{"host":"api.test","method":"GET"}""", true)]
    [InlineData("""{"host":"other","method":"GET"}""", false)]
    [InlineData("""{"method":"GET"}""", false)]
    [InlineData("""{"host":"api.test","method":"POST"}""", false)]
    public void Filter_PreservesHostSubstringAndCaseInsensitiveMethod(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, NetworkOperations.MatchesFilter(document.RootElement, "API", "get"));
    }
}

[Collection("CLI")]
public sealed class SensorOperationsTests
{
    [Fact]
    public async Task Stream_StartsSensorWithSpeedAndThrottleThenCancels()
    {
        await using var server = new MockAgentServer();
        server.StreamMessages["sensors"] = ["""{"value":42}"""];
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var readings = new List<string>();
        var operations = new SensorOperations(client);

        Assert.True(await operations.StartAsync("compass", "Game", 250));
        await operations.StreamAsync("compass", reading =>
        {
            readings.Add(reading);
            cts.Cancel();
        }, cts.Token);

        Assert.Equal("""{"value":42}""", Assert.Single(readings));
        var start = Assert.Single(server.RecordedRequests, r => r.Path.EndsWith("/start"));
        Assert.Contains("speed=Game", start.QueryString);
        Assert.Contains("throttleMs=250", start.QueryString);
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path.EndsWith("/stop"));
        Assert.NotNull(await client.GetStatusAsync());
    }

    [Fact]
    public async Task Stream_AlreadyCancelled_DisposesSocketWithoutReadings()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SensorOperations(client).StreamAsync("compass",
                _ => throw new InvalidOperationException("Unexpected reading"), cts.Token));

        Assert.NotNull(await client.GetStatusAsync());
    }
}
