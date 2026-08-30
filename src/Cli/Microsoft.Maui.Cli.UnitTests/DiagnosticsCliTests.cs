using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Exercises <c>maui devflow diagnostics</c> end to end against the mock agent.
/// </summary>
[Collection("CLI")]
public class DiagnosticsCliTests
{
    private static async Task<(MockAgentServer server, CliTestHarness cli)> CreateFixturesAsync()
    {
        var server = new MockAgentServer();
        await server.StartAsync();
        return (server, new CliTestHarness(server.Port));
    }

    [Fact]
    public async Task DiagnosticsPerformance_RecordsAWindowAndSummarizesIt()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync(
            "devflow", "diagnostics", "performance", "--duration", "1", "--sample-interval", "250", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.Equal(2, json.GetProperty("session").GetProperty("sampleCount").GetInt32());
        Assert.Equal(500000, json.GetProperty("memory").GetProperty("managedDeltaBytes").GetInt64());
        Assert.Equal(2, json.GetProperty("gc").GetProperty("gen0Delta").GetInt32());
        Assert.True(json.GetProperty("cpu").GetProperty("peakPercent").GetDouble() > 0);
        // Estimated frame timings are never surfaced as a frame rate.
        Assert.False(json.GetProperty("frames").GetProperty("supported").GetBoolean());
        Assert.True(json.GetProperty("loss").GetProperty("anyLoss").GetBoolean());

        Assert.Contains(server.RecordedRequests, r => r.Path == "/api/v1/profiler/sessions" && r.Method == "POST");
        Assert.Contains(server.RecordedRequests, r => r.Method == "DELETE" && r.Path.StartsWith("/api/v1/profiler/sessions", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiagnosticsPerformance_HumanOutputWarnsAboutPerturbationAndLoss()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync(
            "devflow", "diagnostics", "performance", "--duration", "1", "--no-json");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Performance triage", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Buffer loss", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Frames: unavailable", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("native profiler", result.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Limitations:", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiagnosticsPerformance_AttachDoesNotStartOrStopASession()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "diagnostics", "performance", "--attach", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path == "/api/v1/profiler/sessions" && r.Method == "POST");
        Assert.DoesNotContain(server.RecordedRequests, r => r.Method == "DELETE" && r.Path.StartsWith("/api/v1/profiler/sessions", StringComparison.Ordinal));
        Assert.Contains(server.RecordedRequests, r => r.Path.Contains("/samples", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiagnosticsPerformance_RejectsAnOutOfRangeSampleInterval()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync(
            "devflow", "diagnostics", "performance", "--sample-interval", "5", "--json");

        Assert.Equal(1, result.ExitCode);
        using var error = JsonDocument.Parse(result.StdErr);
        Assert.Contains("sample-interval", error.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("--duration", "0")]
    [InlineData("--duration", "301")]
    [InlineData("--hotspots", "0")]
    [InlineData("--hotspots", "51")]
    public async Task DiagnosticsPerformance_RejectsInvalidRangesBeforeCallingTheAgent(
        string option,
        string value)
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync(
            "devflow", "diagnostics", "performance", option, value, "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("InvalidArgument", result.StdErr, StringComparison.Ordinal);
        Assert.Empty(server.RecordedRequests);
    }

    private static int GetUnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
