using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class EvidenceCliTests : IDisposable
{
    private readonly string _root = Path.Combine(
        AppContext.BaseDirectory, "evidence-cli-tests", Guid.NewGuid().ToString("N"));

    public EvidenceCliTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Preview_ReportsEnvironmentAndLimitsWithoutWritingOrTakingAScreenshot()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);
        var output = Path.Combine(_root, "preview.mauitrace");

        var result = await cli.InvokeAsync("devflow", "evidence", "preview", "--output", output, "--json");

        Assert.Equal(0, result.ExitCode);
        var plan = result.ParseJsonOutput();
        Assert.Equal("cli", plan.GetProperty("source").GetString());
        Assert.False(plan.GetProperty("screenshot").GetProperty("requested").GetBoolean());
        Assert.True(plan.GetProperty("environment").GetProperty("unavailable").GetArrayLength() > 0);
        Assert.True(plan.GetProperty("neverIncluded").GetArrayLength() > 0);
        Assert.False(File.Exists(output));
        Assert.DoesNotContain(server.RecordedRequests, request => request.Path == "/api/v1/ui/screenshot");
        Assert.Single(server.RecordedRequests, request => request.Path == "/api/v1/ui/tree");
    }

    [Fact]
    public async Task CaptureAndView_RoundTripABundleAndGenerateAnOfflineReport()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);
        var bundle = Path.Combine(_root, "capture.mauitrace");
        var report = Path.Combine(_root, "report.html");

        var capture = await cli.InvokeAsync("devflow", "evidence", "capture", "--output", bundle, "--json");
        Assert.True(capture.ExitCode == 0, capture.StdErr);
        Assert.True(capture.ParseJsonOutput().GetProperty("ok").GetBoolean());
        Assert.False(capture.ParseJsonOutput().GetProperty("manifest").GetProperty("screenshot").GetProperty("included").GetBoolean());

        var view = await cli.InvokeRawAsync("devflow", "evidence", "view", bundle, "--no-open", "--output-report", report, "--json");
        Assert.True(view.ExitCode == 0, view.StdErr);
        Assert.False(view.ParseJsonOutput().GetProperty("opened").GetBoolean());
        var html = File.ReadAllText(report);
        Assert.Contains("script-src 'none'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Environment", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_ReportsWorkflowConsentAndHumanReadableEnvironment()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);
        var workflow = Path.Combine(_root, "repro.md");
        File.WriteAllText(workflow, "# Repro\n1. Tap");
        var preview = await cli.InvokeAsync("devflow", "evidence", "preview", "--workflow", workflow, "--json");
        Assert.Equal(0, preview.ExitCode);
        Assert.Contains(preview.ParseJsonOutput().GetProperty("included").EnumerateArray(),
            entry => entry.GetProperty("name").GetString() == "workflow.md");
        var text = await cli.InvokeAsync("devflow", "evidence", "preview", "--no-json");
        Assert.Equal(0, text.ExitCode);
        Assert.Contains("Current environment", text.StdOut, StringComparison.Ordinal);
        Assert.Contains("Not captured/enforced", text.StdOut, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureAndView_RequireExplicitOverwrite(bool report)
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);
        var bundle = Path.Combine(_root, "capture.mauitrace");
        var destination = report ? Path.Combine(_root, "report.html") : bundle;
        if (report)
            Assert.Equal(0, (await cli.InvokeAsync("devflow", "evidence", "capture", "--output", bundle, "--json")).ExitCode);
        File.WriteAllText(destination, "original");
        var args = report
            ? new[] { "devflow", "evidence", "view", bundle, "--no-open", "--output-report", destination, "--json" }
            : new[] { "devflow", "evidence", "capture", "--output", destination, "--json" };

        var refused = report ? await cli.InvokeRawAsync(args) : await cli.InvokeAsync(args);
        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("already exists", refused.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("original", File.ReadAllText(destination));
        var replaced = report
            ? await cli.InvokeRawAsync([.. args, "--overwrite"])
            : await cli.InvokeAsync([.. args, "--overwrite"]);
        Assert.Equal(0, replaced.ExitCode);
        Assert.NotEqual("original", File.ReadAllText(destination));
    }

    [Fact]
    public async Task PreviewAndCapture_RejectAnUnreachableAgent()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var cli = new CliTestHarness(port);
        var output = Path.Combine(_root, "unreachable.mauitrace");
        var preview = await cli.InvokeAsync("devflow", "evidence", "preview", "--json");
        var capture = await cli.InvokeAsync("devflow", "evidence", "capture", "--output", output, "--json");
        Assert.Equal(1, preview.ExitCode);
        Assert.Equal(1, capture.ExitCode);
        Assert.Contains("No DevFlow agent responded", preview.StdErr, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task Capture_RejectsInvalidOutputAndMissingWorkflow()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);
        var invalidOutput = await cli.InvokeAsync("devflow", "evidence", "capture", "--output", Path.Combine(_root, "bundle.zip"), "--json");
        var missingWorkflow = await cli.InvokeAsync("devflow", "evidence", "capture", "--workflow", Path.Combine(_root, "missing.md"), "--json");
        Assert.Equal(1, invalidOutput.ExitCode);
        Assert.Contains(".mauitrace", invalidOutput.StdErr, StringComparison.Ordinal);
        Assert.Equal(1, missingWorkflow.ExitCode);
        Assert.Contains("Workflow file not found", missingWorkflow.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Capture_JsonErrorsDoNotContainHumanAgentLabels()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);
        var output = Path.Combine(_root, "existing.mauitrace");
        File.WriteAllText(output, "original");
        DevFlowCommands.ResolveRunningBrokerPortAsync = () => Task.FromResult<int?>(19223);
        DevFlowCommands.ListBrokerAgentsAsync = _ => Task.FromResult<AgentRegistration[]?>([
            new() { Id = "target", AppName = "Target", Platform = "Windows", Port = server.Port },
            new() { Id = "other", AppName = "Other", Platform = "Android", Port = server.Port + 1 },
        ]);
        try
        {
            var result = await cli.InvokeAsync("devflow", "evidence", "capture", "--output", output, "--json");
            Assert.Equal(1, result.ExitCode);
            using var error = JsonDocument.Parse(result.StdErr);
            Assert.Equal("InvocationError", error.RootElement.GetProperty("type").GetString());
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
        }
    }
}
